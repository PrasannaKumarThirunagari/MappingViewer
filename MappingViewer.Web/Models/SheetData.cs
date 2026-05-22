namespace MappingViewer.Web.Models;

/// <summary>
/// A single cell: its text plus optional background and font colors (hex without the '#').
/// </summary>
public class CellData
{
    public string Value { get; set; } = string.Empty;
    public string? BackgroundColor { get; set; }
    public string? FontColor { get; set; }
    public bool Bold { get; set; }

    /// <summary>Excel indent level (0 = none). Each level adds left padding in the UI.</summary>
    public int Indent { get; set; }

    /// <summary>When true, cell text may wrap to multiple lines (matches Excel wrap text).</summary>
    public bool WrapText { get; set; }

    /// <summary>Excel horizontal alignment name (e.g. Left, Center, Right).</summary>
    public string? HorizontalAlignment { get; set; }
}

/// <summary>
/// One logical table inside a sheet: an optional title row,
/// a header row, and the data rows that follow.
/// </summary>
public class TableData
{
    public string? Title { get; set; }
    public string? TitleBackgroundColor { get; set; }
    public string? TitleFontColor { get; set; }

    public List<CellData> Headers { get; set; } = new();
    public List<List<CellData>> Rows { get; set; } = new();
}

/// <summary>
/// One rendered section on a sheet: free-form rows and/or a single data table.
/// </summary>
public class SheetBlock
{
    /// <summary>Non-tabular rows (description notes, text between tables).</summary>
    public List<List<CellData>>? FreeformRows { get; set; }

    public TableData? Table { get; set; }
}

/// <summary>
/// A worksheet, after parsing: a free-form description block (rows above the first
/// detected table) followed by zero or more tables and optional notes between tables.
/// </summary>
public class SheetData
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Rows before the first detected table — rendered as-is, no filtering.</summary>
    public List<List<CellData>> Description { get; set; } = new();

    /// <summary>Ordered content: free-form notes and tables as they appear on the sheet.</summary>
    public List<SheetBlock> Blocks { get; set; } = new();

    /// <summary>All tables on the sheet (convenience for tab counts and filters).</summary>
    public List<TableData> Tables => Blocks
        .Where(b => b.Table != null)
        .Select(b => b.Table!)
        .ToList();

    /// <summary>True when at least one structured table was detected on this sheet.</summary>
    public bool HasDetectedTables => Blocks.Any(b => b.Table != null);

    /// <summary>
    /// When no table is detected, every non-blank row is stored here for direct grid display.
    /// </summary>
    public List<List<CellData>> RawSheetRows { get; set; } = new();

    /// <summary>Column count for raw grid display (sheet width when no tables).</summary>
    public int RawDisplayColumnCount
    {
        get
        {
            var max = MaxColumns;
            foreach (var row in RawSheetRows)
            {
                if (row.Count > max) max = row.Count;
            }
            return Math.Max(max, 1);
        }
    }

    /// <summary>Widest header row in the sheet — used to size the description block columns.</summary>
    public int MaxColumns { get; set; }
}

/// <summary>One workbook (file) and all its parsed sheets.</summary>
public class WorkbookData
{
    public string FileName { get; set; } = string.Empty;
    public List<SheetData> Sheets { get; set; } = new();
}
