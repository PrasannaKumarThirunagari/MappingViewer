# Mapping Viewer

ASP.NET Core MVC (.NET 10) web app that reads `.xlsx` workbooks from a configured folder and displays each sheet as a tab with live, auto-filter style searching.

## Features

- Scans a configurable folder for `.xlsx` files (see `ExcelSettings:FolderPath` in `appsettings.json`).
- Reads workbooks with **EPPlus 7**.
- **Configurable table detection** — no hardcoded row numbers or column letters; rules live in `appsettings.json`.
- Free-form text above the first table and between tables is shown as description blocks.
- Each workbook opens in a viewer page where every sheet is a tab.
- **Global search box** filters rows across every column on the active sheet as you type.
- **Per-column filter inputs** under each header narrow results further (combined with AND).
- Sticky header + filter row, alternating row stripes, row counter footer.
- Click truncated cells to see the full value in a popup.

## Run

```bash
cd MappingViewer.Web
dotnet restore
dotnet run
```

Then open the URL shown in the console (default <http://localhost:5080>).

## Configuration

### Folder

```json
"ExcelSettings": {
  "FolderPath": "Data"
}
```

Relative paths resolve against the application content root. Use an absolute path (e.g. `"C:\\Mappings"`) for any folder on disk.

### Table detection (no hardcoded layout)

All layout rules are under `ExcelSettings:TableDetection`:

| Setting | Purpose |
|--------|---------|
| `HeaderBackgroundColors` | Hex colours that identify a **header** row (starts a new table). |
| `DetectHeaderByBackgroundColor` | Enable colour-based header detection. |
| `DetectHeaderByBoldFont` | Treat a row as header when every non-empty cell is **bold** (fallback if colours differ). |
| `HeaderMinimumFilledCells` | Minimum non-empty cells required on a header row (default `2`). |
| `DetectTableTitleBeforeHeader` | Title = sparse text row **immediately above** the next header row. |
| `TableTitleMinimumFilledCells` / `TableTitleMaximumFilledCells` | How many non-empty cells a title row may have (default `1`–`1`). Increase max for multi-cell titles. |
| `TableTitleBackgroundColors` | Optional title colours (e.g. brown bands). Leave `[]` for white/default titles. |
| `DataRowMinimumFilledCells` | Minimum non-empty cells for a **data** row within the table (default `2`). |
| `NeutralBackgroundColors` | Backgrounds not painted in the UI (white, automatic). |

**How a sheet is parsed (top to bottom):**

1. Rows before the first **header** → description block.
2. Optional **title** row (sparse text before a header) → table title.
3. **Header** row → column names; starts a new table.
4. Following **data** rows (enough filled cells) → table body until the next title/header.
5. Other rows after the first table → notes between tables, then the next table.

Example for mapping workbooks with grey headers and white single-line titles:

```json
"TableDetection": {
  "HeaderBackgroundColors": [ "BFBFBF", "A6A6A6", "808080" ],
  "DetectHeaderByBackgroundColor": true,
  "HeaderMinimumFilledCells": 2,
  "DetectTableTitleBeforeHeader": true,
  "TableTitleBackgroundColors": [],
  "TableTitleMinimumFilledCells": 1,
  "TableTitleMaximumFilledCells": 1,
  "DataRowMinimumFilledCells": 2
}
```

For a **single-column** table, set `HeaderMinimumFilledCells` and `DataRowMinimumFilledCells` to `1`.

## Project layout

```
MappingViewer.sln
MappingViewer.Web/
├── Controllers/HomeController.cs
├── Models/
│   ├── ExcelSettings.cs
│   ├── TableDetectionSettings.cs
│   └── SheetData.cs
├── Services/
│   ├── IExcelReaderService.cs
│   ├── ExcelReaderService.cs
│   ├── SheetTableDetector.cs
│   └── SheetRowSnapshot.cs
├── Views/ ...
├── wwwroot/ ...
├── Data/Sample.xlsx
└── appsettings.json
```

## Notes

- **EPPlus licensing:** EPPlus 7 is free for **personal, non-commercial** use (`LicenseContext.NonCommercial` in `Program.cs`). Commercial use requires a [paid EPPlus license](https://epplussoftware.com/).
