namespace MappingViewer.Web.Models;

/// <summary>
/// Bound from the "ExcelSettings" section of appsettings.json.
/// </summary>
public class ExcelSettings
{
    /// <summary>Absolute path or a path relative to the application's content root.</summary>
    public string FolderPath { get; set; } = "Data";

    /// <summary>Rules for detecting headers, titles, data rows, and the next table — fully configurable.</summary>
    public TableDetectionSettings TableDetection { get; set; } = new();
}
