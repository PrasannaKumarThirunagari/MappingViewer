using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using MappingViewer.Web.Models;
using Microsoft.Extensions.Options;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace MappingViewer.Web.Services;

/// <summary>
/// Reads .xlsx workbooks from a configured folder using EPPlus.
/// Table layout is driven entirely by <see cref="TableDetectionSettings"/> (no hardcoded rows/columns).
/// Optimized for large workbooks: bulk value reads, per-styleId style decoding,
/// and an in-memory cache keyed by (file path, last-write time).
/// </summary>
public class ExcelReaderService : IExcelReaderService
{
    private readonly ExcelSettings _settings;
    private readonly TableDetectionSettings _detection;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ExcelReaderService> _logger;
    private readonly HashSet<string> _neutralColors;

    // Cache parsed workbooks. Key = absolute path. Entry is invalidated when the file's
    // last-write time changes, so the user always sees the on-disk content.
    private readonly ConcurrentDictionary<string, CachedWorkbook> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed class CachedWorkbook
    {
        public required DateTime LastWriteUtc { get; init; }
        public required long Length { get; init; }
        public required WorkbookData Data { get; init; }
    }

    /// <summary>Compact bundle of style fields we actually care about.</summary>
    private readonly record struct DecodedStyle(
        string? Background,
        string? FontColor,
        bool Bold,
        int Indent,
        bool WrapText,
        string? HorizontalAlignment);

    public ExcelReaderService(
        IOptions<ExcelSettings> settings,
        IWebHostEnvironment env,
        ILogger<ExcelReaderService> logger)
    {
        _settings = settings.Value;
        _detection = _settings.TableDetection;
        _env = env;
        _logger = logger;

        _neutralColors = new HashSet<string>(
            _detection.NeutralBackgroundColors
                .Select(ExcelReaderService.NormalizeHex)
                .Where(IsValidHexColor),
            StringComparer.OrdinalIgnoreCase);

        ValidateDetectionSettings();
    }

    private void ValidateDetectionSettings()
    {
        if (_detection.HeaderMinimumFilledCells < 1)
        {
            _logger.LogWarning("HeaderMinimumFilledCells was {Value}; using 1.", _detection.HeaderMinimumFilledCells);
            _detection.HeaderMinimumFilledCells = 1;
        }

        if (_detection.TableTitleMaximumFilledCells < _detection.TableTitleMinimumFilledCells)
        {
            _logger.LogWarning(
                "TableTitleMaximumFilledCells ({Max}) < TableTitleMinimumFilledCells ({Min}); swapping.",
                _detection.TableTitleMaximumFilledCells,
                _detection.TableTitleMinimumFilledCells);
            (_detection.TableTitleMaximumFilledCells, _detection.TableTitleMinimumFilledCells) =
                (_detection.TableTitleMinimumFilledCells, _detection.TableTitleMaximumFilledCells);
        }

        if (!_detection.DetectHeaderByBackgroundColor
            && !_detection.DetectHeaderByBoldFont
            && !_detection.DetectHeaderByColumnLabels)
        {
            _logger.LogWarning(
                "No header detection method is enabled. Enable DetectHeaderByBackgroundColor, DetectHeaderByBoldFont, or DetectHeaderByColumnLabels.");
        }

        if (_detection.DetectHeaderByBackgroundColor && _detection.HeaderBackgroundColors.Count == 0)
        {
            _logger.LogWarning("DetectHeaderByBackgroundColor is true but HeaderBackgroundColors is empty.");
        }

        if (_detection.DetectHeaderByColumnLabels && _detection.HeaderColumnLabels.Count == 0)
        {
            _logger.LogWarning("DetectHeaderByColumnLabels is true but HeaderColumnLabels is empty.");
        }

        if (_detection.HeaderColumnLabelMinimumMatches < 1)
        {
            _detection.HeaderColumnLabelMinimumMatches = 1;
        }

        if (_detection.DetectHeaderByColumnLabels)
        {
            foreach (var label in _detection.HeaderColumnLabels)
            {
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                if (_detection.HeaderColumnLabelsAreRegexPatterns)
                {
                    try
                    {
                        _ = new Regex(label.Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogWarning(ex, "Invalid HeaderColumnLabels regex: {Pattern}", label);
                    }
                }
                else if (SheetTableDetector.StripForLabelMatch(label).Length == 0)
                {
                    _logger.LogWarning("HeaderColumnLabels entry is empty after normalization: {Pattern}", label);
                }
            }
        }
    }

    /// <summary>True when a background should not be painted in the UI (white / automatic).</summary>
    public bool IsNeutralDisplayColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return true;
        return _neutralColors.Contains(NormalizeHex(hex));
    }

    public string GetFolderPath()
    {
        var path = _settings.FolderPath;
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(_env.ContentRootPath, path);
        }

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        return path;
    }

    public IEnumerable<string> ListWorkbookFiles()
    {
        var folder = GetFolderPath();
        return Directory
            .EnumerateFiles(folder, "*.xlsx", SearchOption.TopDirectoryOnly)
            .Where(p => !Path.GetFileName(p).StartsWith("~$"))
            .Select(p => Path.GetFileName(p)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
    }

    public WorkbookData? ReadWorkbook(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeName)
            || !safeName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejected invalid workbook name: {FileName}", fileName);
            return null;
        }

        var full = Path.Combine(GetFolderPath(), safeName);
        var info = new FileInfo(full);
        if (!info.Exists)
        {
            _logger.LogWarning("Workbook not found: {Path}", full);
            return null;
        }

        // Serve cached parse if the file hasn't changed since we last read it.
        if (_cache.TryGetValue(full, out var cached)
            && cached.LastWriteUtc == info.LastWriteTimeUtc
            && cached.Length == info.Length)
        {
            return cached.Data;
        }

        try
        {
            var workbook = new WorkbookData { FileName = safeName };

            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var package = new ExcelPackage(fs);

            foreach (var ws in package.Workbook.Worksheets)
            {
                workbook.Sheets.Add(ParseSheet(ws));
            }

            _cache[full] = new CachedWorkbook
            {
                LastWriteUtc = info.LastWriteTimeUtc,
                Length = info.Length,
                Data = workbook
            };

            return workbook;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to read workbook: {Path}", full);
            return null;
        }
    }

    private SheetData ParseSheet(ExcelWorksheet ws)
    {
        var sheet = new SheetData { Name = ws.Name };
        if (ws.Dimension == null) return sheet;

        int rowCount = ws.Dimension.End.Row;
        int colCount = ws.Dimension.End.Column;
        sheet.MaxColumns = colCount;

        var detector = new SheetTableDetector(_detection);
        var rowCache = BuildRowCache(ws, rowCount, colCount, detector);

        TableData? currentTable = null;
        bool firstTableSeen = false;
        SheetRowSnapshot? pendingTitleRow = null;
        var betweenTableRows = new List<List<CellData>>();

        void FlushBetweenTableNotes()
        {
            if (betweenTableRows.Count == 0) return;
            sheet.Blocks.Add(new SheetBlock { FreeformRows = new List<List<CellData>>(betweenTableRows) });
            betweenTableRows.Clear();
        }

        void CloseCurrentTable()
        {
            if (currentTable == null) return;
            sheet.Blocks.Add(new SheetBlock { Table = currentTable });
            currentTable = null;
        }

        int FindNextNonBlank(int afterRow)
        {
            for (int r = afterRow + 1; r <= rowCount; r++)
            {
                if (rowCache.TryGetValue(r, out var snap) && !snap.IsBlank)
                {
                    return r;
                }
            }
            return -1;
        }

        for (int r = 1; r <= rowCount; r++)
        {
            if (!rowCache.TryGetValue(r, out var row))
            {
                continue;
            }

            if (row.IsBlank)
            {
                continue;
            }

            var nextRowIndex = FindNextNonBlank(r);
            SheetRowSnapshot? nextRow = nextRowIndex > 0 ? rowCache[nextRowIndex] : null;
            var kind = detector.Classify(row, nextRow);

            if (kind == SheetRowKind.Header)
            {
                var headerCells = TrimTrailingBlanks(row.Cells);
                var headerHasText = headerCells.Exists(c => !string.IsNullOrWhiteSpace(c.Value));

                if (!headerHasText)
                {
                    // Misclassified or empty header band — keep as freeform content.
                    var orphanHeader = TrimTrailingBlanks(row.Cells);
                    if (!firstTableSeen)
                    {
                        sheet.Description.Add(orphanHeader);
                    }
                    else
                    {
                        betweenTableRows.Add(orphanHeader);
                    }
                    continue;
                }

                FlushBetweenTableNotes();
                CloseCurrentTable();

                var titleText = pendingTitleRow != null ? detector.PickTitleText(pendingTitleRow) : null;
                var titleUsesColor = pendingTitleRow != null && detector.TitleUsesConfiguredColor(pendingTitleRow);
                var titleBg = titleUsesColor ? pendingTitleRow!.Cells.FirstOrDefault(c => c.BackgroundColor != null)?.BackgroundColor : null;
                var titleFont = titleUsesColor ? pendingTitleRow!.Cells.FirstOrDefault(c => c.FontColor != null)?.FontColor : null;

                currentTable = new TableData
                {
                    Title = titleText,
                    TitleBackgroundColor = NeutralizeDisplayColor(titleBg),
                    TitleFontColor = titleFont,
                    Headers = headerCells
                };
                pendingTitleRow = null;
                firstTableSeen = true;
                continue;
            }

            if (kind == SheetRowKind.TableTitle)
            {
                pendingTitleRow = row;
                continue;
            }

            if (currentTable != null && detector.IsTableDataRow(row, currentTable.Headers.Count))
            {
                var trimmed = row.Cells.Take(currentTable.Headers.Count).ToList();
                while (trimmed.Count < currentTable.Headers.Count)
                {
                    trimmed.Add(new CellData());
                }
                currentTable.Rows.Add(trimmed);
                continue;
            }

            var trimmedRow = TrimTrailingBlanks(row.Cells);
            if (!firstTableSeen)
            {
                sheet.Description.Add(trimmedRow);
            }
            else
            {
                betweenTableRows.Add(trimmedRow);
            }
        }

        FlushBetweenTableNotes();
        CloseCurrentTable();

        if (pendingTitleRow != null)
        {
            var pendingTrimmed = TrimTrailingBlanks(pendingTitleRow.Cells);
            if (pendingTrimmed.Exists(c => !string.IsNullOrWhiteSpace(c.Value)))
            {
                if (!firstTableSeen)
                {
                    sheet.Description.Add(pendingTrimmed);
                }
                else
                {
                    betweenTableRows.Add(pendingTrimmed);
                    FlushBetweenTableNotes();
                }
            }
        }

        if (!sheet.HasDetectedTables)
        {
            sheet.RawSheetRows = BuildRawSheetRows(rowCache, rowCount);
            sheet.Description.Clear();
            sheet.Blocks.Clear();
        }

        return sheet;
    }

    /// <summary>All non-blank rows in sheet order — used when no structured table was detected.</summary>
    private static List<List<CellData>> BuildRawSheetRows(
        Dictionary<int, SheetRowSnapshot> rowCache,
        int rowCount)
    {
        var rows = new List<List<CellData>>();
        for (int r = 1; r <= rowCount; r++)
        {
            if (!rowCache.TryGetValue(r, out var snap) || snap.IsBlank)
            {
                continue;
            }

            var trimmed = TrimTrailingBlanks(snap.Cells);
            if (trimmed.Count == 0 || trimmed.TrueForAll(c => string.IsNullOrWhiteSpace(c.Value)))
            {
                continue;
            }

            rows.Add(trimmed);
        }

        return rows;
    }

    /// <summary>
    /// Build every row of a worksheet in one fast pass:
    ///   * Bulk-read all values via <see cref="ExcelRange.Value"/> as an <c>object[,]</c>
    ///     (single internal traversal instead of one ExcelRange allocation per cell).
    ///   * Decode each unique styleId once and cache the result (most workbooks reuse
    ///     a handful of style ids across thousands of cells).
    ///   * Only call the slow <see cref="ExcelRange.Text"/> formatter for numeric/date
    ///     cells (strings are the common case and need no formatting).
    /// </summary>
    private Dictionary<int, SheetRowSnapshot> BuildRowCache(
        ExcelWorksheet ws,
        int rowCount,
        int colCount,
        SheetTableDetector detector)
    {
        var cache = new Dictionary<int, SheetRowSnapshot>(rowCount);

        // One pass: bulk fetch every value. EPPlus returns a 1-based jagged storage
        // surfaced as object[,] when the address is a multi-cell range.
        object[,]? values = null;
        if (rowCount > 0 && colCount > 0)
        {
            var rangeValue = ws.Cells[1, 1, rowCount, colCount].Value;
            values = rangeValue as object[,];
        }

        // Decode each unique styleId at most once. Most workbooks reuse a handful of
        // style ids across thousands of cells, so this dominates the cost reduction.
        var styleCache = new Dictionary<int, DecodedStyle>(64);
        // The default style (id 0 in most workbooks) is overwhelmingly common — decode
        // it once up front so the inner loop can skip the dictionary lookup entirely.
        var defaultStyle = DecodeStyle(ws, 1, 1);
        int defaultStyleId = ws.Cells[1, 1].StyleID;
        styleCache[defaultStyleId] = defaultStyle;

        for (int r = 1; r <= rowCount; r++)
        {
            // --- Fast path 1: row entirely empty in the bulk values array. ---
            // For very large mostly-empty sheets this saves O(colCount) work and
            // O(colCount) CellData allocations per blank row.
            if (values != null)
            {
                bool allNull = true;
                for (int c = 0; c < colCount; c++)
                {
                    if (values[r - 1, c] != null) { allNull = false; break; }
                }
                if (allNull)
                {
                    cache[r] = SheetTableDetector.CreateBlankSnapshot(r);
                    continue;
                }
            }

            var list = new List<CellData>(colCount);
            for (int c = 1; c <= colCount; c++)
            {
                var raw = values != null ? values[r - 1, c - 1] : ws.GetValue(r, c);
                string text;
                if (raw is null)
                {
                    text = string.Empty;
                }
                else if (raw is string s)
                {
                    text = s;
                }
                else
                {
                    // Numbers and dates: defer to EPPlus' formatter so we keep display
                    // fidelity (number formats, dates) — only the rare non-string cell pays.
                    text = ws.Cells[r, c].Text;
                    if (string.IsNullOrEmpty(text))
                    {
                        text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                    }
                }

                // --- Fast path 2: skip the dict lookup for the default styleId. ---
                int styleId = ws.Cells[r, c].StyleID;
                DecodedStyle style;
                if (styleId == defaultStyleId)
                {
                    style = defaultStyle;
                }
                else if (!styleCache.TryGetValue(styleId, out style))
                {
                    style = DecodeStyle(ws, r, c);
                    styleCache[styleId] = style;
                }

                list.Add(new CellData
                {
                    Value = text,
                    BackgroundColor = style.Background,
                    FontColor = style.FontColor,
                    Bold = style.Bold,
                    Indent = style.Indent,
                    WrapText = style.WrapText,
                    HorizontalAlignment = style.HorizontalAlignment
                });
            }
            cache[r] = SheetTableDetector.AnalyzeRow(list, r);
        }
        return cache;
    }

    /// <summary>
    /// Decode the style fields we care about for one cell. Called at most once per
    /// unique styleId in a sheet — subsequent cells sharing that style reuse the result.
    /// </summary>
    private DecodedStyle DecodeStyle(ExcelWorksheet ws, int row, int col)
    {
        var s = ws.Cells[row, col].Style;

        string? bg = null;
        if (s.Fill.PatternType != ExcelFillStyle.None)
        {
            bg = ResolveColorValue(s.Fill.BackgroundColor)
                 ?? ResolveColorValue(s.Fill.PatternColor);
            bg = NeutralizeDisplayColor(bg);
        }

        string? fontColor = ResolveColorValue(s.Font.Color);
        bool bold = s.Font.Bold;
        int indent = s.Indent;
        bool wrap = s.WrapText;
        string hAlign = s.HorizontalAlignment.ToString();

        return new DecodedStyle(bg, fontColor, bold, indent, wrap, hAlign);
    }

    private static string? ResolveColorValue(ExcelColor color)
    {
        try
        {
            if (!string.IsNullOrEmpty(color.Rgb))
            {
                return ToSafeHex(color.Rgb);
            }
            var looked = color.LookupColor();
            if (!string.IsNullOrEmpty(looked) && looked != "#")
            {
                return ToSafeHex(looked);
            }
        }
        catch
        {
            // EPPlus can throw on missing theme info — treat as no color.
        }
        return null;
    }

    private string? NeutralizeDisplayColor(string? hex) =>
        hex != null && IsNeutralDisplayColor(hex) ? null : hex;

    private static List<CellData> TrimTrailingBlanks(List<CellData> cells)
    {
        // Compute how many cells from the end are blank+uncoloured; only copy if needed.
        int trailing = 0;
        for (int i = cells.Count - 1; i >= 0; i--)
        {
            var c = cells[i];
            if (string.IsNullOrWhiteSpace(c.Value) && c.BackgroundColor == null) trailing++;
            else break;
        }
        if (trailing == 0) return cells;
        if (trailing == cells.Count) return new List<CellData>(0);
        return cells.GetRange(0, cells.Count - trailing);
    }

    public static string NormalizeHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return string.Empty;
        var s = hex.Trim().TrimStart('#').ToUpperInvariant();
        if (s.Length == 8) s = s[2..];
        return s;
    }

    public static string? ToSafeHex(string? hex)
    {
        var normalized = NormalizeHex(hex ?? string.Empty);
        return IsValidHexColor(normalized) ? normalized : null;
    }

    public static bool IsValidHexColor(string hex)
    {
        if (hex.Length != 6) return false;
        for (var i = 0; i < 6; i++)
        {
            var c = hex[i];
            var isDigit = c >= '0' && c <= '9';
            var isUpperHex = c >= 'A' && c <= 'F';
            if (!isDigit && !isUpperHex) return false;
        }
        return true;
    }
}
