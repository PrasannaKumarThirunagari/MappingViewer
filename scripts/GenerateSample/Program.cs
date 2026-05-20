using OfficeOpenXml;
using OfficeOpenXml.Style;

ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

var outDir = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MappingViewer.Web", "Data");

Directory.CreateDirectory(outDir);
var path = Path.Combine(outDir, "Sample.xlsx");

using var package = new ExcelPackage();
AddSheet(package, "Employees", "Employee roster",
    new[] { "ID", "Name", "Department", "Hire Date" },
    new[]
    {
        new[] { "1", "Alice Chen", "Engineering", "2021-03-15" },
        new[] { "2", "Bob Martinez", "Finance", "2019-07-01" },
        new[] { "3", "Carol Singh", "HR", "2022-11-20" },
        new[] { "4", "David Kim", "Engineering", "2020-05-08" },
        new[] { "5", "Eva Novak", "Sales", "2023-01-10" }
    });

AddSheet(package, "Products", "Product catalog",
    new[] { "SKU", "Name", "Category", "Price" },
    new[]
    {
        new[] { "P-100", "Widget A", "Hardware", "19.99" },
        new[] { "P-101", "Widget B", "Hardware", "24.50" },
        new[] { "P-200", "Service Plan", "Services", "99.00" },
        new[] { "P-300", "Cable Kit", "Accessories", "12.75" }
    });

AddSheet(package, "Orders", "Recent orders",
    new[] { "Order ID", "Customer", "SKU", "Qty", "Status" },
    new[]
    {
        new[] { "O-5001", "Acme Corp", "P-100", "10", "Shipped" },
        new[] { "O-5002", "Globex", "P-200", "1", "Active" },
        new[] { "O-5003", "Initech", "P-300", "25", "Pending" }
    });

AddSheet(package, "Customers", "Customer list",
    new[] { "Code", "Company", "Country", "Contact" },
    new[]
    {
        new[] { "C-01", "Acme Corp", "USA", "Jane Doe" },
        new[] { "C-02", "Globex", "UK", "John Smith" },
        new[] { "C-03", "Initech", "USA", "Bill Lumbergh" }
    });

AddSheet(package, "Suppliers", "Supplier directory",
    new[] { "Code", "Supplier", "Lead Time (days)", "Rating" },
    new[]
    {
        new[] { "S-01", "Northwind Traders", "5", "A" },
        new[] { "S-02", "Contoso Parts", "7", "B" },
        new[] { "S-03", "Fabrikam Supply", "3", "A" }
    });

package.SaveAs(new FileInfo(path));
Console.WriteLine($"Wrote {path}");

static void AddSheet(ExcelPackage package, string name, string description,
    string[] headers, string[][] rows)
{
    var ws = package.Workbook.Worksheets.Add(name);

    ws.Cells[1, 1].Value = description;
    ws.Cells[1, 1].Style.Font.Italic = true;

    const int titleRow = 3;
    const int headerRow = 4;
    const int dataStart = 5;

    ws.Cells[titleRow, 1].Value = $"{name} table";
    StyleRow(ws, titleRow, 1, headers.Length, "C4A484");

    for (var c = 0; c < headers.Length; c++)
    {
        ws.Cells[headerRow, c + 1].Value = headers[c];
    }
    StyleRow(ws, headerRow, 1, headers.Length, "BFBFBF");

    for (var r = 0; r < rows.Length; r++)
    {
        for (var c = 0; c < rows[r].Length; c++)
        {
            ws.Cells[dataStart + r, c + 1].Value = rows[r][c];
        }
    }

    ws.Cells[ws.Dimension!.Address].AutoFitColumns();
}

static void StyleRow(ExcelWorksheet ws, int row, int colStart, int colCount, string hex)
{
    var color = ColorFromHex(hex);
    for (var c = colStart; c < colStart + colCount; c++)
    {
        var cell = ws.Cells[row, c];
        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cell.Style.Fill.BackgroundColor.SetColor(color);
    }
}

static System.Drawing.Color ColorFromHex(string hex)
{
    hex = hex.TrimStart('#');
    var r = Convert.ToInt32(hex[..2], 16);
    var g = Convert.ToInt32(hex[2..4], 16);
    var b = Convert.ToInt32(hex[4..6], 16);
    return System.Drawing.Color.FromArgb(r, g, b);
}
