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

    public SheetTableDetector(TableDetectionSettings rules)
    {
        _rules = rules;
        _headerColors = BuildColorSet(rules.HeaderBackgroundColors);
        _titleColors = BuildColorSet(rules.TableTitleBackgroundColors);
    }

    public static SheetRowSnapshot AnalyzeRow(List<CellData> cells, int rowIndex)
    {
        var filled = cells.Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToList();
        var filledCount = filled.Count;

        var bgs = filled
            .Where(c => c.BackgroundColor != null)
            .Select(c => c.BackgroundColor!)
            .ToList();
        if (bgs.Count == 0)
        {
            bgs = cells.Where(c => c.BackgroundColor != null).Select(c => c.BackgroundColor!).ToList();
        }

        string? dominantBg = null;
        if (bgs.Count > 0)
        {
            dominantBg = bgs
                .GroupBy(b => b, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First().Key;
        }

        var allFilledBold = filledCount > 0 && filled.All(c => c.Bold);

        return new SheetRowSnapshot
        {
            RowIndex = rowIndex,
            Cells = cells,
            FilledCount = filledCount,
            DominantBackground = dominantBg,
            IsBlank = filledCount == 0 && bgs.Count == 0,
            AllFilledCellsBold = allFilledBold
        };
    }

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
        if (headerColumnCount <= 0)
        {
            return false;
        }

        var span = row.Cells.Take(headerColumnCount);
        var filled = span.Count(c => !string.IsNullOrWhiteSpace(c.Value));
        var required = Math.Clamp(_rules.DataRowMinimumFilledCells, 1, headerColumnCount);
        return filled >= required;
    }

    public bool IsHeaderRow(SheetRowSnapshot row)
    {
        if (row.FilledCount < Math.Max(1, _rules.HeaderMinimumFilledCells))
        {
            return false;
        }

        var byColor = _rules.DetectHeaderByBackgroundColor
            && row.DominantBackground != null
            && _headerColors.Contains(row.DominantBackground);

        var byBold = _rules.DetectHeaderByBoldFont && row.AllFilledCellsBold;

        if (!_rules.DetectHeaderByBackgroundColor && !_rules.DetectHeaderByBoldFont)
        {
            return false;
        }

        return byColor || byBold;
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
