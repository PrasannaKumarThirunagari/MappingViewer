using System.Text;
using System.Text.RegularExpressions;
using MappingViewer.Web.Models;

namespace MappingViewer.Web.Services;

/// <summary>
/// Classifies sheet rows using only <see cref="TableDetectionSettings"/> — no fixed columns or row indices.
/// </summary>
internal sealed class SheetTableDetector
{
    private readonly TableDetectionSettings _rules;
    private readonly HashSet<string> _headerColors;
    private readonly HashSet<string> _titleColors;

    /// <summary>
    /// Regex-mode label patterns (only populated when HeaderColumnLabelsAreRegexPatterns = true).
    /// </summary>
    private readonly List<Regex> _headerLabelRegexes;

    /// <summary>
    /// Plain-text mode label tokens — already case-folded and stripped of ALL whitespace.
    /// A row qualifies when any cell's similarly-normalized text contains one of these tokens.
    /// This is what the user-visible "ignore spaces, ignore case" matching uses.
    /// </summary>
    private readonly List<string> _headerLabelTokens;

    public SheetTableDetector(TableDetectionSettings rules)
    {
        _rules = rules;
        _headerColors = BuildColorSet(rules.HeaderBackgroundColors);
        _titleColors = BuildColorSet(rules.TableTitleBackgroundColors);

        if (rules.HeaderColumnLabelsAreRegexPatterns)
        {
            _headerLabelRegexes = CompileHeaderLabelRegexes(rules);
            _headerLabelTokens = new List<string>(0);
        }
        else
        {
            _headerLabelRegexes = new List<Regex>(0);
            _headerLabelTokens = BuildHeaderLabelTokens(rules);
        }
    }

    private static List<Regex> CompileHeaderLabelRegexes(TableDetectionSettings rules)
    {
        var list = new List<Regex>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var label in rules.HeaderColumnLabels)
        {
            if (string.IsNullOrWhiteSpace(label)) continue;
            var key = label.Trim();
            if (!seen.Add(key)) continue;

            try
            {
                list.Add(new Regex(key, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
            catch (ArgumentException)
            {
                // Invalid pattern — skip.
            }
        }

        return list;
    }

    private static List<string> BuildHeaderLabelTokens(TableDetectionSettings rules)
    {
        var list = new List<string>(rules.HeaderColumnLabels.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var label in rules.HeaderColumnLabels)
        {
            var token = StripForLabelMatch(label);
            if (token.Length == 0) continue;
            if (!seen.Add(token)) continue;
            list.Add(token);
        }
        return list;
    }

    /// <summary>
    /// Normalize text for label matching: lowercase + drop ALL whitespace.
    /// So "Data Type (Object)", "DataType(Object)", "data  type ( object )" all become
    /// the same token "datatype(object)". This is the basis of the whitespace- and
    /// case-insensitive header detection.
    /// </summary>
    public static string StripForLabelMatch(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>Legacy helper retained for any external callers — collapses runs of whitespace to single space.</summary>
    public static string NormalizeLabelText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    /// <summary>
    /// One pass over the row's cells. Computes filled count, dominant background
    /// colour, and whether every filled cell is bold — without intermediate LINQ
    /// allocations or GroupBy/OrderBy (those used to dominate per-row work on big sheets).
    /// </summary>
    public static SheetRowSnapshot AnalyzeRow(List<CellData> cells, int rowIndex)
    {
        int filledCount = 0;
        bool allFilledBold = true;
        int totalWithBg = 0;
        Dictionary<string, int>? bgCounts = null;

        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            bool nonEmpty = !string.IsNullOrWhiteSpace(c.Value);
            if (nonEmpty)
            {
                filledCount++;
                if (!c.Bold) allFilledBold = false;
            }
            if (c.BackgroundColor != null)
            {
                totalWithBg++;
                bgCounts ??= new Dictionary<string, int>(4, StringComparer.OrdinalIgnoreCase);
                bgCounts.TryGetValue(c.BackgroundColor, out var prev);
                bgCounts[c.BackgroundColor] = prev + 1;
            }
        }

        string? dominantBg = null;
        if (bgCounts != null)
        {
            int max = 0;
            foreach (var kv in bgCounts)
            {
                if (kv.Value > max) { max = kv.Value; dominantBg = kv.Key; }
            }
        }

        return new SheetRowSnapshot
        {
            RowIndex = rowIndex,
            Cells = cells,
            FilledCount = filledCount,
            DominantBackground = dominantBg,
            IsBlank = filledCount == 0 && totalWithBg == 0,
            AllFilledCellsBold = filledCount > 0 && allFilledBold
        };
    }

    /// <summary>Fast-path snapshot for a row we already know is empty — saves per-cell allocation.</summary>
    public static SheetRowSnapshot CreateBlankSnapshot(int rowIndex) =>
        new SheetRowSnapshot
        {
            RowIndex = rowIndex,
            Cells = EmptyCells,
            FilledCount = 0,
            DominantBackground = null,
            IsBlank = true,
            AllFilledCellsBold = false
        };

    private static readonly List<CellData> EmptyCells = new(0);

    public SheetRowKind Classify(SheetRowSnapshot row, SheetRowSnapshot? nextNonBlankRow)
    {
        if (row.IsBlank)
        {
            return SheetRowKind.Blank;
        }

        if (IsHeaderRow(row))
        {
            return SheetRowKind.Header;
        }

        if (IsTableTitleRow(row, nextNonBlankRow))
        {
            return SheetRowKind.TableTitle;
        }

        return SheetRowKind.Freeform;
    }

    public bool IsTableDataRow(SheetRowSnapshot row, int headerColumnCount)
    {
        if (headerColumnCount <= 0) return false;

        // Manual loop — this is called for every body row on every sheet, so
        // dropping the LINQ Take + Count saves a lot of allocations on big sheets.
        int limit = Math.Min(headerColumnCount, row.Cells.Count);
        int filled = 0;
        for (int i = 0; i < limit; i++)
        {
            if (!string.IsNullOrWhiteSpace(row.Cells[i].Value)) filled++;
        }
        int required = Math.Clamp(_rules.DataRowMinimumFilledCells, 1, headerColumnCount);
        return filled >= required;
    }

    public bool IsHeaderRow(SheetRowSnapshot row)
    {
        if (MatchesHeaderColumnLabels(row))
        {
            return true;
        }

        if (row.FilledCount < Math.Max(1, _rules.HeaderMinimumFilledCells))
        {
            return false;
        }

        var byColor = _rules.DetectHeaderByBackgroundColor
            && row.DominantBackground != null
            && _headerColors.Contains(row.DominantBackground);

        var byBold = _rules.DetectHeaderByBoldFont && row.AllFilledCellsBold;

        if (!_rules.DetectHeaderByBackgroundColor
            && !_rules.DetectHeaderByBoldFont
            && !_rules.DetectHeaderByColumnLabels)
        {
            return false;
        }

        return byColor || byBold;
    }

    private bool MatchesHeaderColumnLabels(SheetRowSnapshot row)
    {
        if (!_rules.DetectHeaderByColumnLabels) return false;
        if (_headerLabelRegexes.Count == 0 && _headerLabelTokens.Count == 0) return false;
        if (row.FilledCount == 0) return false;

        // Token mode (default) — case- and whitespace-insensitive by stripping both
        // sides. "Data Type (Object)" matches "DataType(Object)", "data type (object)",
        // "data  type ( object )" — they all normalize to "datatype(object)".
        if (_headerLabelTokens.Count > 0)
        {
            int matched = 0;
            // Track which configured tokens have already been seen this row.
            // _headerLabelTokens is small (typically 1-5 entries) so a stack-friendly
            // bool[] beats a HashSet.
            Span<bool> seen = _headerLabelTokens.Count <= 16
                ? stackalloc bool[_headerLabelTokens.Count]
                : new bool[_headerLabelTokens.Count];

            for (int i = 0; i < row.Cells.Count; i++)
            {
                var v = row.Cells[i].Value;
                if (string.IsNullOrWhiteSpace(v)) continue;
                var stripped = StripForLabelMatch(v);
                if (stripped.Length == 0) continue;

                for (int t = 0; t < _headerLabelTokens.Count; t++)
                {
                    if (seen[t]) continue;
                    var token = _headerLabelTokens[t];
                    if (stripped.Contains(token, StringComparison.Ordinal))
                    {
                        seen[t] = true;
                        matched++;
                        if (!_rules.RequireAllHeaderColumnLabels
                            && matched >= Math.Max(1, _rules.HeaderColumnLabelMinimumMatches))
                        {
                            return true;
                        }
                    }
                }
            }

            return _rules.RequireAllHeaderColumnLabels
                ? matched >= _headerLabelTokens.Count
                : matched >= Math.Max(1, _rules.HeaderColumnLabelMinimumMatches);
        }

        // Regex mode — explicit opt-in via HeaderColumnLabelsAreRegexPatterns.
        int regexMatched = 0;
        var required = Math.Max(1, _rules.HeaderColumnLabelMinimumMatches);
        for (int p = 0; p < _headerLabelRegexes.Count; p++)
        {
            var regex = _headerLabelRegexes[p];
            bool found = false;
            for (int i = 0; i < row.Cells.Count; i++)
            {
                var v = row.Cells[i].Value;
                if (string.IsNullOrWhiteSpace(v)) continue;
                if (regex.IsMatch(v)) { found = true; break; }
            }
            if (found)
            {
                regexMatched++;
                if (!_rules.RequireAllHeaderColumnLabels && regexMatched >= required) return true;
            }
            else if (_rules.RequireAllHeaderColumnLabels)
            {
                return false;
            }
        }
        return _rules.RequireAllHeaderColumnLabels
            ? regexMatched >= _headerLabelRegexes.Count
            : regexMatched >= required;
    }

    public bool IsTableTitleRow(SheetRowSnapshot row, SheetRowSnapshot? nextNonBlankRow)
    {
        if (row.FilledCount < Math.Max(1, _rules.TableTitleMinimumFilledCells))
        {
            return false;
        }

        if (row.FilledCount > Math.Max(_rules.TableTitleMinimumFilledCells, _rules.TableTitleMaximumFilledCells))
        {
            return false;
        }

        var byColor = _rules.DetectTableTitleByBackgroundColor
            && _titleColors.Count > 0
            && row.DominantBackground != null
            && _titleColors.Contains(row.DominantBackground);

        if (byColor)
        {
            return true;
        }

        if (!_rules.DetectTableTitleBeforeHeader || nextNonBlankRow == null)
        {
            return false;
        }

        return IsHeaderRow(nextNonBlankRow);
    }

    public string? PickTitleText(SheetRowSnapshot titleRow)
    {
        var filled = titleRow.Cells.Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToList();
        if (filled.Count == 0)
        {
            return null;
        }

        return string.Join(" ", filled.Select(c => c.Value));
    }

    public bool TitleUsesConfiguredColor(SheetRowSnapshot titleRow) =>
        titleRow.DominantBackground != null
        && _titleColors.Count > 0
        && _titleColors.Contains(titleRow.DominantBackground);

    private static HashSet<string> BuildColorSet(IEnumerable<string> colors) =>
        new(
            colors.Select(ExcelReaderService.ToSafeHex).OfType<string>(),
            StringComparer.OrdinalIgnoreCase);
}
