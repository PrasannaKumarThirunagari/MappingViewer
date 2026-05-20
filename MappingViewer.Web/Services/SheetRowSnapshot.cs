using MappingViewer.Web.Models;

namespace MappingViewer.Web.Services;

/// <summary>Cached analysis of one worksheet row (avoids re-reading cells during lookahead).</summary>
internal sealed class SheetRowSnapshot
{
    public required int RowIndex { get; init; }
    public required List<CellData> Cells { get; init; }
    public int FilledCount { get; init; }
    public string? DominantBackground { get; init; }
    public bool IsBlank { get; init; }
    public bool AllFilledCellsBold { get; init; }
}

/// <summary>How a row is classified when scanning a sheet top to bottom.</summary>
internal enum SheetRowKind
{
    Blank,
    Header,
    TableTitle,
    TableData,
    Freeform
}
