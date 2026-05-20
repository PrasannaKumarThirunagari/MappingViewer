using MappingViewer.Web.Models;
using Microsoft.Extensions.Options;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace MappingViewer.Web.Services;

/// <summary>
/// Reads .xlsx workbooks from a configured folder using EPPlus.
/// Table layout is driven entirely by <see cref="TableDetectionSettings"/> (no hardcoded rows/columns).
/// </summary>
public class ExcelReaderService : IExcelReaderService
{
    private readonly ExcelSettings _settings;
    private readonly TableDetectionSettings _detection;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ExcelReaderService> _logger;
    private readonly HashSet<string> _neutralColors;

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

        if (!_detection.DetectHeaderByBackgroundColor && !_detection.DetectHeaderByBoldFont)
        {
            _logger.LogWarning(
                "No header detection method is enabled. Enable DetectHeaderByBackgroundColor or DetectHeaderByBoldFont.");
        }

        if (_detection.DetectHeaderByBackgroundColor && _detection.HeaderBackgroundColors.Count == 0)
        {
            _logger.LogWarning("DetectHeaderByBackgroundColor is true but HeaderBackgroundColors is empty.");
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
        if (!File.Exists(full))
        {
            _logger.LogWarning("Workbook not found: {Path}", full);
            return null;
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
                    Headers = TrimTrailingBlanks(row.Cells)
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

        return sheet;
    }

    private Dictionary<int, SheetRowSnapshot> BuildRowCache(
        ExcelWorksheet ws,
        int rowCount,
        int colCount,
        SheetTableDetector detector)
    {
        var cache = new Dictionary<int, SheetRowSnapshot>(rowCount);
        for (int r = 1; r <= rowCount; r++)
        {
            var cells = ReadRow(ws, r, colCount);
            cache[r] = SheetTableDetector.AnalyzeRow(cells, r);
        }
        return cache;
    }

    private string? NeutralizeDisplayColor(string? hex) =>
        hex != null && IsNeutralDisplayColor(hex) ? null : hex;

    private static string GetCellDisplayText(ExcelRange cell)
    {
        if (!string.IsNullOrEmpty(cell.Text))
        {
            return cell.Text;
        }

        return cell.Value?.ToString() ?? string.Empty;
    }

    private List<CellData> ReadRow(ExcelWorksheet ws, int row, int colCount)
    {
        var list = new List<CellData>(colCount);
        for (int c = 1; c <= colCount; c++)
        {
            var cell = ws.Cells[row, c];
            list.Add(new CellData
            {
                Value = GetCellDisplayText(cell),
                BackgroundColor = NeutralizeDisplayColor(ResolveColor(cell.Style.Fill)),
                FontColor = ResolveFontColor(cell.Style.Font.Color),
                Bold = cell.Style.Font.Bold,
                Indent = cell.Style.Indent,
                WrapText = cell.Style.WrapText,
                HorizontalAlignment = cell.Style.HorizontalAlignment.ToString()
            });
        }
        return list;
    }

    private static List<CellData> TrimTrailingBlanks(List<CellData> cells)
    {
        var trimmed = new List<CellData>(cells);
        while (trimmed.Count > 0)
        {
            var last = trimmed[^1];
            if (string.IsNullOrWhiteSpace(last.Value) && last.BackgroundColor == null)
            {
                trimmed.RemoveAt(trimmed.Count - 1);
            }
            else break;
        }
        return trimmed;
    }

    private static string? ResolveColor(ExcelFill fill)
    {
        if (fill.PatternType == ExcelFillStyle.None) return null;
        return ResolveExcelColor(fill.BackgroundColor)
               ?? ResolveExcelColor(fill.PatternColor);
    }

    private static string? ResolveFontColor(ExcelColor color) => ResolveExcelColor(color);

    private static string? ResolveExcelColor(ExcelColor color)
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
            // EPPlus can throw on missing theme info; treat as no color.
        }
        return null;
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
