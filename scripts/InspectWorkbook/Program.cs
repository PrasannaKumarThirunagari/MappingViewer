using System.IO;
using MappingViewer.Web.Models;
using MappingViewer.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OfficeOpenXml;

ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

var path = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "MappingViewer.Web", "Data", "Sample.xlsx");
path = Path.GetFullPath(path);

var settings = Options.Create(new ExcelSettings { TableDetection = new TableDetectionSettings() });
var webRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));
var service = new ExcelReaderService(settings, new HostEnv(webRoot), LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ExcelReaderService>());
var wb = service.ReadWorkbook(Path.GetFileName(path));

if (wb == null)
{
    Console.WriteLine("Failed to read workbook.");
    return 1;
}

foreach (var sheet in wb.Sheets)
{
    Console.WriteLine($"=== {sheet.Name} ===");
    Console.WriteLine($"  Description rows: {sheet.Description.Count}");
    Console.WriteLine($"  Blocks: {sheet.Blocks.Count}");
    foreach (var block in sheet.Blocks)
    {
        if (block.FreeformRows != null)
        {
            Console.WriteLine($"  [Notes] {block.FreeformRows.Count} row(s):");
            foreach (var row in block.FreeformRows)
            {
                Console.WriteLine($"    - {string.Join(" | ", row.Select(c => c.Value))}");
            }
        }
        if (block.Table != null)
        {
            var t = block.Table;
            Console.WriteLine($"  [Table] Title='{t.Title}' headers={t.Headers.Count} dataRows={t.Rows.Count} titleBg={t.TitleBackgroundColor ?? "(none)"}");
        }
    }
}

return 0;

internal sealed class HostEnv : IWebHostEnvironment
{
    public HostEnv(string contentRoot)
    {
        ContentRootPath = contentRoot;
        ContentRootFileProvider = new NullFileProvider();
        WebRootFileProvider = new NullFileProvider();
    }

    public string ApplicationName { get; set; } = "Inspect";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; }
    public string EnvironmentName { get; set; } = "Development";
}
