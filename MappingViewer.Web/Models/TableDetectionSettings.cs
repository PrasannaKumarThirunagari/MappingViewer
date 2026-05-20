namespace MappingViewer.Web.Models;

/// <summary>
/// Configurable rules for finding headers, table titles, data rows, and table boundaries.
/// No row numbers or column letters are hardcoded — only counts and colours from config.
/// </summary>
public class TableDetectionSettings
{
    // ---- Header row (starts a new table) ----

    /// <summary>Hex background colours that mark a header row (e.g. grey bands).</summary>
    public List<string> HeaderBackgroundColors { get; set; } = new()
    {
        "BFBFBF", "A6A6A6", "808080", "D9D9D9", "C0C0C0"
    };

    /// <summary>Match header rows by configured background colours.</summary>
    public bool DetectHeaderByBackgroundColor { get; set; } = true;

    /// <summary>Match header rows when every non-empty cell is bold (optional fallback).</summary>
    public bool DetectHeaderByBoldFont { get; set; } = false;

    /// <summary>Minimum non-empty cells required to treat a row as a header.</summary>
    public int HeaderMinimumFilledCells { get; set; } = 2;

    // ---- Table title (optional row immediately before a header) ----

    /// <summary>Optional hex colours for title/separator rows (leave empty for white/default).</summary>
    public List<string> TableTitleBackgroundColors { get; set; } = new();

    /// <summary>Title row is the sparse text row directly above the next header row.</summary>
    public bool DetectTableTitleBeforeHeader { get; set; } = true;

    /// <summary>Title row must have at least this many non-empty cells.</summary>
    public int TableTitleMinimumFilledCells { get; set; } = 1;

    /// <summary>Title row must have at most this many non-empty cells (keeps titles separate from wide text blocks).</summary>
    public int TableTitleMaximumFilledCells { get; set; } = 1;

    /// <summary>Match title rows by configured title background colours.</summary>
    public bool DetectTableTitleByBackgroundColor { get; set; } = true;

    // ---- Data rows (belong to the current table until the next header/title) ----

    /// <summary>Minimum non-empty cells (within the header width) for a row to count as table data.</summary>
    public int DataRowMinimumFilledCells { get; set; } = 2;

    // ---- Display ----

    /// <summary>Background colours not painted in the UI (white, automatic, etc.).</summary>
    public List<string> NeutralBackgroundColors { get; set; } = new()
    {
        "FFFFFF", "FFFFFFFF", "000000", "FF000000"
    };
}
