using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

namespace RevitLocationExporter.Core
{
    /// <summary>
    /// Bundles element instance/type parameters and sheet/titleblock parameters
    /// loaded from a single Excel workbook (2 tabs) or fallback CSV.
    /// </summary>
    public class LocationParameterBundle
    {
        public string SourcePath { get; set; } = string.Empty;
        public string ElementTabName { get; set; } = string.Empty;
        public string SheetTabName { get; set; } = string.Empty;
        public string ExportTabName { get; set; } = string.Empty;

        /// <summary>
        /// Key = Locator Code (e.g. AFNXAGNWA) -> Value = Dictionary of parameter names and values
        /// for model element instances and types (CPK device parameters).
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> ElementParameters { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Key = Locator Code (e.g. AFNXAGNWA) -> Value = Dictionary of parameter names and values
        /// for _PL sheets and titleblocks (Work_Package, Building Name, etc.).
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> SheetParameters { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Key = Locator Code (e.g. AFNXAGNWA) -> Value = Target export folder path (NWC, IFC, PDF, DWG, RVT).
        /// </summary>
        public Dictionary<string, string> ExportLocations { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public bool HasData => ElementParameters.Count > 0 || SheetParameters.Count > 0 || ExportLocations.Count > 0;

        public Dictionary<string, string>? GetElementParams(string locationName)
        {
            if (ElementParameters.TryGetValue(locationName, out var p)) return p;
            foreach (var kv in ElementParameters)
            {
                if (locationName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    kv.Key.IndexOf(locationName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return kv.Value;
                }
            }
            return null;
        }

        public Dictionary<string, string>? GetSheetParams(string locationName)
        {
            if (SheetParameters.TryGetValue(locationName, out var p)) return p;
            foreach (var kv in SheetParameters)
            {
                if (locationName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    kv.Key.IndexOf(locationName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return kv.Value;
                }
            }
            return null;
        }

        public string? GetExportLocation(string locationName)
        {
            if (string.IsNullOrWhiteSpace(locationName)) return null;
            if (ExportLocations.TryGetValue(locationName, out var p) && !string.IsNullOrWhiteSpace(p)) return p;
            foreach (var kv in ExportLocations)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                if (locationName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    kv.Key.IndexOf(locationName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return kv.Value;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Reads location parameters from Excel (.xlsx/.xlsm) using 2 tabs (Element_Params + Sheet_Params)
    /// or CSV fallback.
    /// Supports Option 2 (ClosedXML with AssemblyResolve) with automatic fallback to Option 1
    /// (Zero-dependency native OpenXml ZipArchive parser) if ClosedXML encounters missing DLLs or version conflicts.
    /// </summary>
    public static class LocationDataSheetReader
    {
        public static LocationParameterBundle ReadParameterBundle(string? filePath)
        {
            var bundle = new LocationParameterBundle { SourcePath = filePath ?? string.Empty };
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return bundle;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".xlsx" || ext == ".xlsm")
            {
                ReadExcelBundle(filePath!, bundle);
            }
            else
            {
                ReadCsvBundle(filePath!, bundle);
            }

            return bundle;
        }

        private static void ReadExcelBundle(string filePath, LocationParameterBundle bundle)
        {
            // Option 2 attempt: ClosedXML (works when dependencies resolve cleanly)
            try
            {
                ReadExcelBundleClosedXml(filePath, bundle);
                if (bundle.HasData)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClosedXML failed ({ex.Message}), falling back to native OpenXml zip parser.");
            }

            // Option 1 fallback: Zero-dependency native OpenXml parser (never conflicts with Revit)
            try
            {
                ReadExcelBundleNative(filePath, bundle);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Native Excel parser failed: {ex.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadExcelBundleClosedXml(string filePath, LocationParameterBundle bundle)
        {
            using var workbook = new XLWorkbook(filePath);
            var worksheets = workbook.Worksheets.ToList();
            if (worksheets.Count == 0) return;

            IXLWorksheet? elemWs = null;
            IXLWorksheet? sheetWs = null;
            IXLWorksheet? exportWs = null;

            if (worksheets.Count == 1)
            {
                // Single tab: used for element, sheet, and export location parameters
                elemWs = worksheets[0];
                sheetWs = worksheets[0];
                exportWs = worksheets[0];
            }
            else
            {
                // Multi-tab workbook: look for designated tabs by keyword
                elemWs = worksheets.FirstOrDefault(w =>
                    w.Name.IndexOf("element", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("instance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("cpk", StringComparison.OrdinalIgnoreCase) >= 0);

                sheetWs = worksheets.FirstOrDefault(w =>
                    w.Name.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("custom", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("drawing", StringComparison.OrdinalIgnoreCase) >= 0);

                exportWs = worksheets.FirstOrDefault(w =>
                    w.Name.IndexOf("export", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("folder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (w.Name.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     w.Name.IndexOf("param", StringComparison.OrdinalIgnoreCase) < 0));

                // Fallbacks if tab names are generic (e.g. Sheet1, Sheet2)
                elemWs ??= worksheets[0];
                sheetWs ??= (worksheets.Count > 1 ? worksheets[1] : worksheets[0]);
            }

            if (elemWs != null)
            {
                bundle.ElementTabName = elemWs.Name;
                bundle.ElementParameters = ReadClosedXmlWorksheet(elemWs);
            }

            if (sheetWs != null)
            {
                bundle.SheetTabName = sheetWs.Name;
                bundle.SheetParameters = ReadClosedXmlWorksheet(sheetWs);
            }

            if (exportWs != null)
            {
                bundle.ExportTabName = exportWs.Name;
                bundle.ExportLocations = ReadClosedXmlExportLocations(exportWs);
            }

            if (bundle.ExportLocations.Count == 0)
            {
                ExtractExportLocationsFromParamMaps(bundle.ElementParameters, bundle.ExportLocations);
                ExtractExportLocationsFromParamMaps(bundle.SheetParameters, bundle.ExportLocations);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Dictionary<string, string> ReadClosedXmlExportLocations(IXLWorksheet ws)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var firstRow = ws.FirstRowUsed();
            if (firstRow == null) return result;

            int locatorCol = -1;
            int folderCol = -1;

            foreach (var cell in firstRow.CellsUsed())
            {
                string header = cell.GetString().Trim();
                if (string.IsNullOrEmpty(header)) continue;
                int colNum = cell.Address.ColumnNumber;

                if (locatorCol == -1 && IsLocatorHeader(header))
                {
                    locatorCol = colNum;
                }
                else if (folderCol == -1 && IsFolderHeader(header))
                {
                    folderCol = colNum;
                }
            }

            if (locatorCol == -1 || folderCol == -1)
            {
                var cols = firstRow.CellsUsed().Select(c => c.Address.ColumnNumber).OrderBy(c => c).ToList();
                if (cols.Count >= 2)
                {
                    if (locatorCol == -1) locatorCol = cols[0];
                    if (folderCol == -1) folderCol = cols[1];
                }
            }

            if (locatorCol == -1 || folderCol == -1) return result;

            int lastRowNum = ws.LastRowUsed()?.RowNumber() ?? 0;
            for (int r = firstRow.RowNumber() + 1; r <= lastRowNum; r++)
            {
                var row = ws.Row(r);
                if (row.IsEmpty()) continue;

                string locator = row.Cell(locatorCol).GetString().Trim();
                string folder = row.Cell(folderCol).GetString().Trim();

                if (!string.IsNullOrEmpty(locator) && !string.IsNullOrEmpty(folder))
                {
                    result[locator] = folder;
                }
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Dictionary<string, Dictionary<string, string>> ReadClosedXmlWorksheet(IXLWorksheet ws)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var firstRow = ws.FirstRowUsed();
            if (firstRow == null) return result;

            var colMap = new Dictionary<int, string>();
            int locatorCol = -1;

            foreach (var cell in firstRow.CellsUsed())
            {
                string header = cell.GetString().Trim();
                if (string.IsNullOrEmpty(header)) continue;

                int colNum = cell.Address.ColumnNumber;
                colMap[colNum] = header;

                if (locatorCol == -1 && (
                    header.Equals("Locator Code", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("LocatorCode", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("Locator", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("Location", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("Building_Code", StringComparison.OrdinalIgnoreCase)))
                {
                    locatorCol = colNum;
                }
            }

            if (locatorCol == -1 && colMap.Count > 0)
            {
                locatorCol = colMap.Keys.Min();
            }

            int lastRowNum = ws.LastRowUsed()?.RowNumber() ?? 0;
            for (int r = firstRow.RowNumber() + 1; r <= lastRowNum; r++)
            {
                var row = ws.Row(r);
                if (row.IsEmpty()) continue;

                string locator = row.Cell(locatorCol).GetString().Trim();
                if (string.IsNullOrEmpty(locator)) continue;

                var paramMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in colMap)
                {
                    if (kv.Key == locatorCol) continue;
                    string val = row.Cell(kv.Key).GetString().Trim();
                    paramMap[kv.Value] = val;
                }

                result[locator] = paramMap;
            }

            return result;
        }

        #region Native OpenXml ZipArchive Parser (Option 1 - Zero External Dependencies)

        private static void ReadExcelBundleNative(string filePath, LocationParameterBundle bundle)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

            // 1. Load shared strings table if present
            var sharedStrings = new List<string>();
            var sstEntry = archive.GetEntry("xl/sharedStrings.xml");
            if (sstEntry != null)
            {
                using var sstStream = sstEntry.Open();
                var sstDoc = XDocument.Load(sstStream);
                XNamespace sstNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                foreach (var si in sstDoc.Descendants(sstNs + "si"))
                {
                    sharedStrings.Add(si.Value);
                }
            }

            // 2. Load relationships (rId -> worksheet file path)
            var relMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (relsEntry != null)
            {
                using var relsStream = relsEntry.Open();
                var relsDoc = XDocument.Load(relsStream);
                XNamespace pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
                foreach (var rel in relsDoc.Descendants(pkgRelNs + "Relationship"))
                {
                    string id = (string?)rel.Attribute("Id") ?? string.Empty;
                    string target = (string?)rel.Attribute("Target") ?? string.Empty;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                    {
                        target = target.Replace('\\', '/');
                        if (target.StartsWith("/")) target = target.Substring(1);
                        else if (!target.StartsWith("xl/")) target = "xl/" + target;
                        relMap[id] = target;
                    }
                }
            }

            // 3. Load sheets list from workbook.xml
            var wbEntry = archive.GetEntry("xl/workbook.xml");
            if (wbEntry == null) return;

            using var wbStream = wbEntry.Open();
            var wbDoc = XDocument.Load(wbStream);
            XNamespace wbNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            var sheetList = new List<(string Name, ZipArchiveEntry? Entry)>();
            int sheetIndex = 1;

            foreach (var s in wbDoc.Descendants(wbNs + "sheet"))
            {
                string name = (string?)s.Attribute("name") ?? $"Sheet{sheetIndex}";
                string? rId = (string?)s.Attribute(rNs + "id") ?? (string?)s.Attributes().FirstOrDefault(a => a.Name.LocalName == "id");

                string targetPath = string.Empty;
                if (!string.IsNullOrEmpty(rId) && relMap.TryGetValue(rId!, out var mapped))
                {
                    targetPath = mapped;
                }
                else
                {
                    targetPath = $"xl/worksheets/sheet{sheetIndex}.xml";
                }

                var entry = archive.GetEntry(targetPath);
                if (entry != null)
                {
                    sheetList.Add((name, entry));
                }
                sheetIndex++;
            }

            if (sheetList.Count == 0) return;

            // 4. Resolve element, sheet, and export worksheets
            (string Name, ZipArchiveEntry? Entry) elemSheet = default;
            (string Name, ZipArchiveEntry? Entry) sheetSheet = default;
            (string Name, ZipArchiveEntry? Entry) exportSheet = default;

            if (sheetList.Count == 1)
            {
                elemSheet = sheetList[0];
                sheetSheet = sheetList[0];
                exportSheet = sheetList[0];
            }
            else
            {
                elemSheet = sheetList.FirstOrDefault(w =>
                    w.Name.IndexOf("element", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("instance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("cpk", StringComparison.OrdinalIgnoreCase) >= 0);

                sheetSheet = sheetList.FirstOrDefault(w =>
                    w.Name.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("custom", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("drawing", StringComparison.OrdinalIgnoreCase) >= 0);

                exportSheet = sheetList.FirstOrDefault(w =>
                    w.Name.IndexOf("export", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("folder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (w.Name.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     w.Name.IndexOf("param", StringComparison.OrdinalIgnoreCase) < 0));

                if (elemSheet.Entry == null) elemSheet = sheetList[0];
                if (sheetSheet.Entry == null) sheetSheet = sheetList.Count > 1 ? sheetList[1] : sheetList[0];
            }

            if (elemSheet.Entry != null)
            {
                bundle.ElementTabName = elemSheet.Name;
                bundle.ElementParameters = ReadNativeWorksheet(elemSheet.Entry, sharedStrings);
            }

            if (sheetSheet.Entry != null)
            {
                bundle.SheetTabName = sheetSheet.Name;
                bundle.SheetParameters = ReadNativeWorksheet(sheetSheet.Entry, sharedStrings);
            }

            if (exportSheet.Entry != null)
            {
                bundle.ExportTabName = exportSheet.Name;
                bundle.ExportLocations = ReadNativeExportLocations(exportSheet.Entry, sharedStrings);
            }

            if (bundle.ExportLocations.Count == 0)
            {
                ExtractExportLocationsFromParamMaps(bundle.ElementParameters, bundle.ExportLocations);
                ExtractExportLocationsFromParamMaps(bundle.SheetParameters, bundle.ExportLocations);
            }
        }

        private static Dictionary<string, Dictionary<string, string>> ReadNativeWorksheet(
            ZipArchiveEntry sheetEntry, List<string> sharedStrings)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            using var stream = sheetEntry.Open();
            var doc = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var rows = doc.Descendants(ns + "row").ToList();
            if (rows.Count == 0) return result;

            var firstRow = rows[0];
            var colMap = new Dictionary<int, string>();
            int locatorCol = -1;

            foreach (var cell in firstRow.Elements(ns + "c"))
            {
                string cellRef = (string?)cell.Attribute("r") ?? string.Empty;
                int colNum = GetColumnNumber(cellRef);
                if (colNum <= 0) continue;

                string header = GetCellValue(cell, sharedStrings, ns).Trim();
                if (string.IsNullOrEmpty(header)) continue;

                colMap[colNum] = header;

                if (locatorCol == -1 && (
                    header.Equals("Locator Code", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("LocatorCode", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("Locator", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("Location", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("Building_Code", StringComparison.OrdinalIgnoreCase)))
                {
                    locatorCol = colNum;
                }
            }

            if (locatorCol == -1 && colMap.Count > 0)
            {
                locatorCol = colMap.Keys.Min();
            }

            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowCells = new Dictionary<int, string>();

                foreach (var cell in row.Elements(ns + "c"))
                {
                    string cellRef = (string?)cell.Attribute("r") ?? string.Empty;
                    int colNum = GetColumnNumber(cellRef);
                    if (colNum > 0)
                    {
                        rowCells[colNum] = GetCellValue(cell, sharedStrings, ns).Trim();
                    }
                }

                if (!rowCells.TryGetValue(locatorCol, out var locator) || string.IsNullOrEmpty(locator))
                    continue;

                var paramMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in colMap)
                {
                    if (kv.Key == locatorCol) continue;
                    rowCells.TryGetValue(kv.Key, out var val);
                    paramMap[kv.Value] = val ?? string.Empty;
                }

                result[locator] = paramMap;
            }

            return result;
        }

        private static int GetColumnNumber(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return 0;
            int col = 0;
            for (int i = 0; i < cellRef.Length; i++)
            {
                char c = cellRef[i];
                if (char.IsLetter(c))
                {
                    col = col * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
                }
                else
                {
                    break;
                }
            }
            return col;
        }

        private static string GetCellValue(XElement cell, List<string> sharedStrings, XNamespace ns)
        {
            string type = (string?)cell.Attribute("t") ?? string.Empty;

            if (type == "s")
            {
                var vElem = cell.Element(ns + "v");
                if (vElem != null && int.TryParse(vElem.Value, out int idx) && idx >= 0 && idx < sharedStrings.Count)
                {
                    return sharedStrings[idx];
                }
                return vElem?.Value ?? string.Empty;
            }

            if (type == "inlineStr")
            {
                var isElem = cell.Element(ns + "is");
                if (isElem != null)
                {
                    return isElem.Value;
                }
            }

            var v = cell.Element(ns + "v");
            if (v != null)
            {
                return v.Value;
            }

            return cell.Value ?? string.Empty;
        }

        private static Dictionary<string, string> ReadNativeExportLocations(
            ZipArchiveEntry sheetEntry, List<string> sharedStrings)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var stream = sheetEntry.Open();
            var doc = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var rows = doc.Descendants(ns + "row").ToList();
            if (rows.Count == 0) return result;

            var firstRow = rows[0];
            int locatorCol = -1;
            int folderCol = -1;

            foreach (var cell in firstRow.Elements(ns + "c"))
            {
                string cellRef = (string?)cell.Attribute("r") ?? string.Empty;
                int colNum = GetColumnNumber(cellRef);
                if (colNum <= 0) continue;

                string header = GetCellValue(cell, sharedStrings, ns).Trim();
                if (string.IsNullOrEmpty(header)) continue;

                if (locatorCol == -1 && IsLocatorHeader(header))
                    locatorCol = colNum;
                else if (folderCol == -1 && IsFolderHeader(header))
                    folderCol = colNum;
            }

            if (locatorCol == -1 || folderCol == -1)
            {
                var cols = firstRow.Elements(ns + "c")
                    .Select(c => GetColumnNumber((string?)c.Attribute("r") ?? ""))
                    .Where(c => c > 0)
                    .OrderBy(c => c)
                    .ToList();
                if (cols.Count >= 2)
                {
                    if (locatorCol == -1) locatorCol = cols[0];
                    if (folderCol == -1) folderCol = cols[1];
                }
            }

            if (locatorCol == -1 || folderCol == -1) return result;

            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                string locator = string.Empty;
                string folder = string.Empty;

                foreach (var cell in row.Elements(ns + "c"))
                {
                    string cellRef = (string?)cell.Attribute("r") ?? string.Empty;
                    int colNum = GetColumnNumber(cellRef);
                    if (colNum == locatorCol)
                        locator = GetCellValue(cell, sharedStrings, ns).Trim();
                    else if (colNum == folderCol)
                        folder = GetCellValue(cell, sharedStrings, ns).Trim();
                }

                if (!string.IsNullOrEmpty(locator) && !string.IsNullOrEmpty(folder))
                {
                    result[locator] = folder;
                }
            }

            return result;
        }

        #endregion

        private static void ReadCsvBundle(string filePath, LocationParameterBundle bundle)
        {
            var csvData = ReadCsv(filePath);
            bundle.ElementTabName = Path.GetFileName(filePath);
            bundle.SheetTabName = Path.GetFileName(filePath);
            bundle.ExportTabName = Path.GetFileName(filePath);
            bundle.ElementParameters = csvData;
            bundle.SheetParameters = csvData;
            ExtractExportLocationsFromParamMaps(csvData, bundle.ExportLocations);
        }

        public static Dictionary<string, Dictionary<string, string>> ReadCsv(string filePath)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length < 2) return result;

            string[] headers = SplitCsvLine(lines[0]);
            int locatorCol = -1;
            for (int i = 0; i < headers.Length; i++)
            {
                string h = headers[i].Trim();
                if (IsLocatorHeader(h))
                {
                    locatorCol = i;
                    break;
                }
            }
            if (locatorCol == -1) locatorCol = 0;

            for (int r = 1; r < lines.Length; r++)
            {
                string line = lines[r].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] values = SplitCsvLine(line);
                if (values.Length <= locatorCol) continue;

                string locator = values[locatorCol].Trim();
                if (string.IsNullOrEmpty(locator)) continue;

                var paramMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int col = 0; col < headers.Length && col < values.Length; col++)
                {
                    if (col == locatorCol) continue;
                    string headerName = headers[col].Trim();
                    if (!string.IsNullOrEmpty(headerName))
                        paramMap[headerName] = values[col].Trim();
                }

                result[locator] = paramMap;
            }

            return result;
        }

        public static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var sb = new StringBuilder();

            foreach (char c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }

            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        private static bool IsLocatorHeader(string header)
        {
            return header.Equals("Locator Code", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("LocatorCode", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Locator", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Location", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("LocationCode", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Building_Code", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Key", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Code", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFolderHeader(string header)
        {
            return header.Equals("Export_Folder", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("ExportFolder", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Export Location", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Export_Location", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("ExportLocation", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Output_Folder", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("OutputFolder", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Output_Path", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("OutputPath", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Folder", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("Destination", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("ExportPath", StringComparison.OrdinalIgnoreCase) ||
                   header.Equals("FolderPath", StringComparison.OrdinalIgnoreCase);
        }

        private static void ExtractExportLocationsFromParamMaps(
            Dictionary<string, Dictionary<string, string>> paramMaps,
            Dictionary<string, string> exportLocations)
        {
            if (paramMaps == null) return;
            foreach (var kv in paramMaps)
            {
                if (exportLocations.ContainsKey(kv.Key)) continue;
                foreach (var p in kv.Value)
                {
                    if (IsFolderHeader(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
                    {
                        exportLocations[kv.Key] = p.Value.Trim();
                        break;
                    }
                }
            }
        }
    }
}
