using MappingViewer.Web.Models;
using MappingViewer.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace MappingViewer.Web.Controllers;

public class HomeController : Controller
{
    private readonly IExcelReaderService _excel;

    public HomeController(IExcelReaderService excel)
    {
        _excel = excel;
    }

    /// <summary>
    /// Landing page: lists every .xlsx in the configured folder.
    /// </summary>
    public IActionResult Index()
    {
        var files = _excel.ListWorkbookFiles().ToList();
        ViewBag.FolderPath = _excel.GetFolderPath();
        return View(files);
    }

    /// <summary>
    /// Viewer: shows every sheet of a single workbook as a tab.
    /// </summary>
    public IActionResult Viewer(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return RedirectToAction(nameof(Index));
        }

        var safeName = Path.GetFileName(file);
        if (!safeName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(Index));
        }

        var workbook = _excel.ReadWorkbook(safeName);
        if (workbook == null)
        {
            TempData["Error"] = $"Could not open '{safeName}'. Check that the file exists in the folder and is a valid .xlsx workbook.";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.WorkbookFiles = _excel.ListWorkbookFiles().ToList();
        ViewBag.CurrentFile = safeName;
        ViewData["ViewerLayout"] = true;

        return View(workbook);
    }

    public IActionResult Error() => View();
}
