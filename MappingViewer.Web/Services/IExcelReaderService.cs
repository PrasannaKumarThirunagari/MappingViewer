using MappingViewer.Web.Models;

namespace MappingViewer.Web.Services;

public interface IExcelReaderService
{
    /// <summary>List the .xlsx files found in the configured folder.</summary>
    IEnumerable<string> ListWorkbookFiles();

    /// <summary>Read every sheet of a single workbook.</summary>
    WorkbookData? ReadWorkbook(string fileName);

    /// <summary>Absolute path to the configured folder (created if missing).</summary>
    string GetFolderPath();

    /// <summary>True when a cell background should not be painted (white / automatic).</summary>
    bool IsNeutralDisplayColor(string? hex);
}
