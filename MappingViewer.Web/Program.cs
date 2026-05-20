using MappingViewer.Web.Models;
using MappingViewer.Web.Services;
using OfficeOpenXml;

var builder = WebApplication.CreateBuilder(args);

// EPPlus 7: free for personal / non-commercial use (Polyform Noncommercial license).
// Commercial redistribution requires a paid EPPlus license — see https://epplussoftware.com/
ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

builder.Services.Configure<ExcelSettings>(
    builder.Configuration.GetSection("ExcelSettings"));

builder.Services.AddSingleton<IExcelReaderService, ExcelReaderService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
