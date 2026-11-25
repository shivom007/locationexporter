// ═══════════════════════════════════════════════════════════════════════════
//  NativeXlsxEngine.cs  –  RevitLocationExporter.Core
//
//  Zero-dependency OpenXML (.xlsx / .xlsm) reader and writer built purely on
//  .NET Framework 4.8 System.IO.Compression + System.Xml.Linq.
//  Eliminates ClosedXML / DocumentFormat.OpenXml DLL version collisions
//  across Revit add-ins loaded into the same AppDomain.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RevitLocationExporter.Core
{
    public sealed class NativeCell
    {
        public string RawValue { get; set; } = string.Empty;
        public string Formula { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool HasFormula => !string.IsNullOrWhiteSpace(Formula);
    }

    public sealed class NativeWorkbook
    {
        private readonly Dictionary<string, NativeWorksheet> _byName =
            new(StringComparer.OrdinalIgnoreCase);

        public List<NativeWorksheet> Sheets { get; } = new();

        internal void AddSheet(NativeWorksheet sheet)
        {
            Sheets.Add(sheet);
            _byName[sheet.Name] = sheet;
        }

        public NativeWorksheet? GetSheet(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _byName.TryGetValue(name.Trim(), out var ws) ? ws : null;
        }
    }

    public sealed class NativeWorksheet
    {
        private static readonly Regex CellRefInFormulaRegex = new(
            @"""(?:[^""]|"""")*""|(?<![A-Za-z0-9_!])((?:'(?:[^']|'')+'|[A-Za-z0-9_]+)!)?(\$?)([A-Za-z]{1,3})(\$?)(\d+)(?![A-Za-z0-9_\(])",
            RegexOptions.Compiled);

        public string Name { get; }
        public NativeWorkbook Workbook { get; }
        public SortedDictionary<int, Dictionary<int, NativeCell>> Rows { get; } = new();
        public int MaxRow { get; internal set; }
        public int MaxCol { get; internal set; }
        public bool HasData => Rows.Count > 0;

        public NativeWorksheet(string name, NativeWorkbook workbook)
        {
            Name = name;
            Workbook = workbook;
        }

        internal void SetCell(int row, int col, NativeCell cell)
        {
            if (row <= 0 || col <= 0) return;
            if (!Rows.TryGetValue(row, out var rowDict))
            {
                rowDict = new Dictionary<int, NativeCell>();
                Rows[row] = rowDict;
            }
            rowDict[col] = cell;
            if (row > MaxRow) MaxRow = row;
            if (col > MaxCol) MaxCol = col;
        }

        public NativeCell? GetCell(int row, int col)
        {
            if (Rows.TryGetValue(row, out var rowDict) && rowDict.TryGetValue(col, out var cell))
                return cell;
            return null;
        }

        public int FirstNonEmptyRowNumber()
        {
            foreach (var kv in Rows)
            {
                if (!IsRowEmpty(kv.Key))
                    return kv.Key;
            }
            return 0;
        }

        public bool IsRowEmpty(int row)
        {
            if (!Rows.TryGetValue(row, out var rowDict) || rowDict.Count == 0)
                return true;

            foreach (var cell in rowDict.Values)
            {
                if (cell.HasFormula || !string.IsNullOrWhiteSpace(cell.RawValue))
                    return false;
            }
            return true;
        }

        public IEnumerable<int> GetUsedColumnsInRow(int row)
        {
            if (!Rows.TryGetValue(row, out var rowDict))
                return Enumerable.Empty<int>();

            return rowDict.Keys.OrderBy(k => k);
        }

        /// <summary>
        /// Reads cell text safely; evaluates uncached/error formulas via ExcelFormulaEngine.
        /// </summary>
        public string GetEvaluatedText(int row, int col, HashSet<string>? visitedCells = null)
        {
            var cell = GetCell(row, col);
            if (cell == null) return string.Empty;

            if (!cell.HasFormula)
                return (cell.RawValue ?? string.Empty).Trim();

            if (!string.Equals(cell.DataType, "e", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(cell.RawValue))
            {
                return cell.RawValue.Trim();
            }

            visitedCells ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string cycleKey = $"{Name}!{row}:{col}";
            if (visitedCells.Add(cycleKey))
            {
                try
                {
                    string normalized = "=" + CellRefInFormulaRegex.Replace(cell.Formula, m =>
                    {
                        if (!m.Groups[3].Success) return m.Value; // Quoted string literal
                        if (!TryParseColumnLetters(m.Groups[3].Value, out int colIdx) || colIdx > 16384)
                            return m.Value;
                        if (!int.TryParse(m.Groups[5].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rIdx) || rIdx < 1 || rIdx > 1048576)
                            return m.Value;

                        string sheetPrefix = m.Groups[1].Value;
                        string colLetters = m.Groups[3].Value.ToUpperInvariant();
                        return string.IsNullOrEmpty(sheetPrefix)
                            ? $"[{colLetters}{rIdx}]"
                            : $"[{sheetPrefix}{colLetters}{rIdx}]";
                    });

                    if (ExcelFormulaEngine.TryEvaluate(
                        normalized,
                        refName => ResolveReference(refName, visitedCells),
                        out string evaluated,
                        out _))
                    {
                        return evaluated.Trim();
                    }
                }
                finally
                {
                    visitedCells.Remove(cycleKey);
                }
            }

            return string.Equals(cell.DataType, "e", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : (cell.RawValue ?? string.Empty).Trim();
        }

        /// <summary>
        /// Preserves formula text prefixed with '=' when present; otherwise returns verbatim text.
        /// Used for Parameter_Rules and TextNote_Rules columns.
        /// </summary>
        public string GetFormulaOrText(int row, int col)
        {
            var cell = GetCell(row, col);
            if (cell == null) return string.Empty;

            if (cell.HasFormula)
                return "=" + cell.Formula.TrimStart('=').Trim();

            return (cell.RawValue ?? string.Empty).Trim();
        }

        private string ResolveReference(string refName, HashSet<string> visitedCells)
        {
            if (string.IsNullOrWhiteSpace(refName)) return string.Empty;

            int bang = refName.LastIndexOf('!');
            if (bang > 0)
            {
                string sheetName = refName.Substring(0, bang).Trim();
                if (sheetName.Length >= 2 && sheetName[0] == '\'' && sheetName[sheetName.Length - 1] == '\'')
                    sheetName = sheetName.Substring(1, sheetName.Length - 2).Replace("''", "'");

                string addr = refName.Substring(bang + 1).Trim();
                if (TryParseCellAddress(addr, out int r, out int c))
                {
                    return Workbook.GetSheet(sheetName)?.GetEvaluatedText(r, c, visitedCells) ?? string.Empty;
                }
                return string.Empty;
            }

            if (TryParseCellAddress(refName.Trim(), out int localR, out int localC))
                return GetEvaluatedText(localR, localC, visitedCells);

            return string.Empty;
        }

        public static bool TryParseCellAddress(string addr, out int row, out int col)
        {
            row = 0;
            col = 0;
            if (string.IsNullOrWhiteSpace(addr)) return false;

            string s = addr.Replace("$", string.Empty).Trim();
            int i = 0;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            if (i == 0 || i >= s.Length) return false;

            string colPart = s.Substring(0, i);
            string rowPart = s.Substring(i);

            return TryParseColumnLetters(colPart, out col) &&
                   int.TryParse(rowPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out row) &&
                   row > 0;
        }

        public static bool TryParseColumnLetters(string letters, out int col)
        {
            col = 0;
            if (string.IsNullOrEmpty(letters)) return false;

            int n = 0;
            foreach (char ch in letters)
            {
                if (ch is < 'A' or > 'Z' && ch is < 'a' or > 'z') return false;
                n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            col = n;
            return col > 0;
        }

        public static string GetColumnLetter(int col)
        {
            if (col <= 0) return "A";
            var sb = new StringBuilder();
            int n = col;
            while (n > 0)
            {
                int rem = (n - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                n = (n - 1) / 26;
            }
            return sb.ToString();
        }
    }

    public sealed class NativeSheetSpec
    {
        public string Name { get; set; } = "Sheet1";
        public string[] Headers { get; set; } = Array.Empty<string>();
        public List<string[]> Rows { get; set; } = new();
        public HashSet<int> TextColumns { get; set; } = new();
        public List<(string SqRef, string ListValues)> DataValidations { get; set; } = new();
    }

    public static class NativeXlsxEngine
    {
        private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        private static readonly Regex SharedFormulaShiftRegex = new(
            @"""(?:[^""]|"""")*""|(?<![A-Za-z0-9_])(\$?)([A-Za-z]{1,3})(\$?)(\d+)(?![A-Za-z0-9_\(])",
            RegexOptions.Compiled);

        // ── Read OOXML Workbook ──────────────────────────────────────────────

        public static NativeWorkbook Open(string filePath)
        {
            var workbook = new NativeWorkbook();

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

            // 1. Shared strings table
            var sharedStrings = new List<string>();
            var sstEntry = FindEntry(archive, "xl/sharedStrings.xml");
            if (sstEntry != null)
            {
                using var sstStream = sstEntry.Open();
                var sstDoc = XDocument.Load(sstStream, LoadOptions.PreserveWhitespace);
                foreach (var si in sstDoc.Descendants(MainNs + "si"))
                {
                    sharedStrings.Add(ExtractRichOrPlainText(si));
                }
            }

            // 2. Workbook relationships (rId -> worksheet zip path)
            var relMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var relsEntry = FindEntry(archive, "xl/_rels/workbook.xml.rels");
            if (relsEntry != null)
            {
                using var relsStream = relsEntry.Open();
                var relsDoc = XDocument.Load(relsStream);
                foreach (var rel in relsDoc.Descendants(PkgRelNs + "Relationship"))
                {
                    string id = (string?)rel.Attribute("Id") ?? string.Empty;
                    string target = (string?)rel.Attribute("Target") ?? string.Empty;
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target)) continue;

                    target = target.Replace('\\', '/');
                    if (target.StartsWith("/", StringComparison.Ordinal))
                        target = target.Substring(1);
                    else if (!target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                        target = "xl/" + target;

                    relMap[id] = target;
                }
            }

            // 3. Worksheet definitions in workbook order
            var wbEntry = FindEntry(archive, "xl/workbook.xml");
            if (wbEntry == null) return workbook;

            using var wbStream = wbEntry.Open();
            var wbDoc = XDocument.Load(wbStream);

            int fallbackIdx = 1;
            foreach (var sheetElem in wbDoc.Descendants(MainNs + "sheet"))
            {
                string name = (string?)sheetElem.Attribute("name") ?? $"Sheet{fallbackIdx}";
                string? rId = (string?)sheetElem.Attribute(RelNs + "id")
                    ?? (string?)sheetElem.Attributes().FirstOrDefault(a => a.Name.LocalName == "id");

                string targetPath = !string.IsNullOrEmpty(rId) && relMap.TryGetValue(rId!, out var mapped)
                    ? mapped
                    : $"xl/worksheets/sheet{fallbackIdx}.xml";

                var entry = FindEntry(archive, targetPath);
                if (entry != null)
                {
                    var ws = ParseWorksheet(name, workbook, entry, sharedStrings);
                    workbook.AddSheet(ws);
                }

                fallbackIdx++;
            }

            return workbook;
        }

        private static ZipArchiveEntry? FindEntry(ZipArchive archive, string targetPath)
        {
            string normalized = targetPath.Replace('\\', '/').TrimStart('/');
            return archive.GetEntry(normalized)
                ?? archive.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName.Replace('\\', '/').TrimStart('/'), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static string ExtractRichOrPlainText(XElement container)
        {
            var sb = new StringBuilder();
            foreach (var child in container.Elements())
            {
                string local = child.Name.LocalName;
                if (local == "t")
                {
                    sb.Append(child.Value);
                }
                else if (local == "r")
                {
                    foreach (var rt in child.Elements())
                    {
                        if (rt.Name.LocalName == "t")
                            sb.Append(rt.Value);
                    }
                }
            }

            return sb.Length > 0 ? sb.ToString() : string.Empty;
        }

        private static NativeWorksheet ParseWorksheet(
            string name,
            NativeWorkbook workbook,
            ZipArchiveEntry sheetEntry,
            List<string> sharedStrings)
        {
            var ws = new NativeWorksheet(name, workbook);
            var sharedFormulas = new Dictionary<string, (string Formula, int MasterRow, int MasterCol)>(StringComparer.OrdinalIgnoreCase);

            using var stream = sheetEntry.Open();
            var doc = XDocument.Load(stream, LoadOptions.PreserveWhitespace);

            int prevRow = 0;
            foreach (var rowElem in doc.Descendants(MainNs + "row"))
            {
                string? rowAttr = (string?)rowElem.Attribute("r");
                int rowNum = int.TryParse(rowAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRow) && parsedRow > 0
                    ? parsedRow
                    : prevRow + 1;
                prevRow = rowNum;

                int prevCol = 0;
                foreach (var c in rowElem.Elements(MainNs + "c"))
                {
                    string cellRef = (string?)c.Attribute("r") ?? string.Empty;
                    int cellRow = rowNum;
                    int colNum;

                    if (!string.IsNullOrEmpty(cellRef) && NativeWorksheet.TryParseCellAddress(cellRef, out int addrRow, out int addrCol))
                    {
                        cellRow = addrRow;
                        colNum = addrCol;
                    }
                    else
                    {
                        colNum = prevCol + 1;
                    }
                    prevCol = colNum;

                    string dataType = (string?)c.Attribute("t") ?? string.Empty;
                    string rawValue = ExtractCellRawValue(c, dataType, sharedStrings);
                    string formula = ExtractCellFormula(c, cellRow, colNum, sharedFormulas);

                    if (!string.IsNullOrEmpty(rawValue) || !string.IsNullOrEmpty(formula))
                    {
                        ws.SetCell(cellRow, colNum, new NativeCell
                        {
                            DataType = dataType,
                            RawValue = rawValue,
                            Formula = formula
                        });
                    }
                }
            }

            return ws;
        }

        private static string ExtractCellRawValue(XElement cellElem, string dataType, List<string> sharedStrings)
        {
            if (dataType == "s")
            {
                var vElem = cellElem.Element(MainNs + "v");
                if (vElem != null &&
                    int.TryParse(vElem.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) &&
                    idx >= 0 && idx < sharedStrings.Count)
                {
                    return sharedStrings[idx];
                }
                return vElem?.Value ?? string.Empty;
            }

            if (dataType == "inlineStr")
            {
                var isElem = cellElem.Element(MainNs + "is");
                return isElem != null ? ExtractRichOrPlainText(isElem) : string.Empty;
            }

            if (dataType == "b")
            {
                string bVal = cellElem.Element(MainNs + "v")?.Value?.Trim() ?? string.Empty;
                return bVal == "1" ? "TRUE" : bVal == "0" ? "FALSE" : bVal;
            }

            // Important: never fall back to cellElem.Value because it concatenates <f> + <v>
            return cellElem.Element(MainNs + "v")?.Value ?? string.Empty;
        }

        private static string ExtractCellFormula(
            XElement cellElem,
            int rowNum,
            int colNum,
            Dictionary<string, (string Formula, int MasterRow, int MasterCol)> sharedFormulas)
        {
            var fElem = cellElem.Element(MainNs + "f");
            if (fElem == null) return string.Empty;

            string fType = (string?)fElem.Attribute("t") ?? string.Empty;
            string si = (string?)fElem.Attribute("si") ?? string.Empty;
            string fText = (fElem.Value ?? string.Empty).Trim();

            if (fType.Equals("shared", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(si))
            {
                if (!string.IsNullOrWhiteSpace(fText))
                {
                    sharedFormulas[si] = (fText, rowNum, colNum);
                    return fText;
                }

                if (sharedFormulas.TryGetValue(si, out var master))
                {
                    return ShiftFormulaReferences(master.Formula, rowNum - master.MasterRow, colNum - master.MasterCol);
                }
            }

            return fText;
        }

        private static string ShiftFormulaReferences(string formula, int deltaRow, int deltaCol)
        {
            if (deltaRow == 0 && deltaCol == 0) return formula;

            return SharedFormulaShiftRegex.Replace(formula, m =>
            {
                if (!m.Groups[2].Success) return m.Value; // Quoted string literal
                if (!NativeWorksheet.TryParseColumnLetters(m.Groups[2].Value, out int colIdx) || colIdx > 16384)
                    return m.Value;
                if (!int.TryParse(m.Groups[4].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rowIdx) || rowIdx < 1 || rowIdx > 1048576)
                    return m.Value;

                string colDollar = m.Groups[1].Value;
                string rowDollar = m.Groups[3].Value;

                int newCol = string.IsNullOrEmpty(colDollar) ? Math.Max(1, colIdx + deltaCol) : colIdx;
                int newRow = string.IsNullOrEmpty(rowDollar) ? Math.Max(1, rowIdx + deltaRow) : rowIdx;

                return $"{colDollar}{NativeWorksheet.GetColumnLetter(newCol)}{rowDollar}{newRow}";
            });
        }

        // ── Write Styled OOXML Workbook ──────────────────────────────────────

        public static void WriteWorkbook(string targetPath, IList<NativeSheetSpec> sheets)
        {
            if (sheets == null || sheets.Count == 0)
                throw new ArgumentException("Workbook must contain at least one sheet.", nameof(sheets));

            string? dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

            var sheetNames = EnsureUniqueSheetNames(sheets);

            WriteXmlEntry(archive, "[Content_Types].xml", BuildContentTypesDoc(sheetNames.Count));
            WriteXmlEntry(archive, "_rels/.rels", BuildRootRelsDoc());
            WriteXmlEntry(archive, "xl/workbook.xml", BuildWorkbookDoc(sheetNames));
            WriteXmlEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelsDoc(sheetNames.Count));
            WriteXmlEntry(archive, "xl/styles.xml", BuildStylesDoc());

            for (int i = 0; i < sheets.Count; i++)
            {
                WriteXmlEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildWorksheetDoc(sheets[i], isFirstTab: i == 0));
            }
        }

        private static List<string> EnsureUniqueSheetNames(IList<NativeSheetSpec> sheets)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>(sheets.Count);

            foreach (var spec in sheets)
            {
                string raw = SanitizeXmlText(spec.Name);
                foreach (char bad in new[] { '\\', '/', '?', '*', '[', ']', ':' })
                    raw = raw.Replace(bad, '_');
                raw = raw.Trim();
                if (string.IsNullOrEmpty(raw)) raw = "Sheet";
                if (raw.Length > 31) raw = raw.Substring(0, 31);

                string unique = raw;
                int suffix = 2;
                while (!used.Add(unique))
                {
                    string sfx = $"_{suffix++}";
                    int maxBase = Math.Max(1, 31 - sfx.Length);
                    unique = (raw.Length > maxBase ? raw.Substring(0, maxBase) : raw) + sfx;
                }
                names.Add(unique);
            }

            return names;
        }

        private static void WriteXmlEntry(ZipArchive archive, string entryPath, XDocument doc)
        {
            var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
            using var stream = entry.Open();
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = false
            };
            using var writer = XmlWriter.Create(stream, settings);
            doc.Save(writer);
        }

        private static XDocument BuildContentTypesDoc(int sheetCount)
        {
            XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
            var types = new XElement(ct + "Types",
                new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));

            for (int i = 1; i <= sheetCount; i++)
            {
                types.Add(new XElement(ct + "Override",
                    new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
            }

            return new XDocument(new XDeclaration("1.0", "utf-8", null), types);
        }

        private static XDocument BuildRootRelsDoc()
        {
            var root = new XElement(PkgRelNs + "Relationships",
                new XElement(PkgRelNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml")));

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        private static XDocument BuildWorkbookDoc(IList<string> sheetNames)
        {
            var sheetsElem = new XElement(MainNs + "sheets");
            for (int i = 0; i < sheetNames.Count; i++)
            {
                sheetsElem.Add(new XElement(MainNs + "sheet",
                    new XAttribute("name", sheetNames[i]),
                    new XAttribute("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute(RelNs + "id", $"rId{i + 1}")));
            }

            var wb = new XElement(MainNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", RelNs.NamespaceName),
                sheetsElem);

            return new XDocument(new XDeclaration("1.0", "utf-8", null), wb);
        }

        private static XDocument BuildWorkbookRelsDoc(int sheetCount)
        {
            var root = new XElement(PkgRelNs + "Relationships");
            for (int i = 1; i <= sheetCount; i++)
            {
                root.Add(new XElement(PkgRelNs + "Relationship",
                    new XAttribute("Id", $"rId{i}"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", $"worksheets/sheet{i}.xml")));
            }

            root.Add(new XElement(PkgRelNs + "Relationship",
                new XAttribute("Id", $"rId{sheetCount + 1}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                new XAttribute("Target", "styles.xml")));

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        private static XDocument BuildStylesDoc()
        {
            XElement MakeBorder(bool withOutline) =>
                withOutline
                    ? new XElement(MainNs + "border",
                        new XElement(MainNs + "left", new XAttribute("style", "thin"), new XElement(MainNs + "color", new XAttribute("rgb", "FFD1D5DB"))),
                        new XElement(MainNs + "right", new XAttribute("style", "thin"), new XElement(MainNs + "color", new XAttribute("rgb", "FFD1D5DB"))),
                        new XElement(MainNs + "top", new XAttribute("style", "thin"), new XElement(MainNs + "color", new XAttribute("rgb", "FFD1D5DB"))),
                        new XElement(MainNs + "bottom", new XAttribute("style", "thin"), new XElement(MainNs + "color", new XAttribute("rgb", "FFD1D5DB"))),
                        new XElement(MainNs + "diagonal"))
                    : new XElement(MainNs + "border",
                        new XElement(MainNs + "left"),
                        new XElement(MainNs + "right"),
                        new XElement(MainNs + "top"),
                        new XElement(MainNs + "bottom"),
                        new XElement(MainNs + "diagonal"));

            var fonts = new XElement(MainNs + "fonts", new XAttribute("count", "3"),
                new XElement(MainNs + "font",
                    new XElement(MainNs + "sz", new XAttribute("val", "9.5")),
                    new XElement(MainNs + "name", new XAttribute("val", "Segoe UI"))),
                new XElement(MainNs + "font",
                    new XElement(MainNs + "b"),
                    new XElement(MainNs + "sz", new XAttribute("val", "10")),
                    new XElement(MainNs + "color", new XAttribute("rgb", "FFFFFFFF")),
                    new XElement(MainNs + "name", new XAttribute("val", "Segoe UI"))),
                new XElement(MainNs + "font",
                    new XElement(MainNs + "sz", new XAttribute("val", "9.5")),
                    new XElement(MainNs + "name", new XAttribute("val", "Consolas"))));

            // Mandatory Excel built-in fills 0 (none) and 1 (gray125) before custom fills 2 and 3
            var fills = new XElement(MainNs + "fills", new XAttribute("count", "4"),
                new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "gray125"))),
                new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "solid"),
                    new XElement(MainNs + "fgColor", new XAttribute("rgb", "FF1F2937")))),
                new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "solid"),
                    new XElement(MainNs + "fgColor", new XAttribute("rgb", "FFF8FAFC")))));

            var borders = new XElement(MainNs + "borders", new XAttribute("count", "2"),
                MakeBorder(withOutline: false),
                MakeBorder(withOutline: true));

            var cellStyleXfs = new XElement(MainNs + "cellStyleXfs", new XAttribute("count", "1"),
                new XElement(MainNs + "xf",
                    new XAttribute("numFmtId", "0"),
                    new XAttribute("fontId", "0"),
                    new XAttribute("fillId", "0"),
                    new XAttribute("borderId", "0")));

            XElement MakeXf(int numFmtId, int fontId, int fillId, int borderId, bool applyNumFmt = false, bool applyFill = false)
            {
                var xf = new XElement(MainNs + "xf",
                    new XAttribute("numFmtId", numFmtId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("fontId", fontId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("fillId", fillId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("borderId", borderId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("xfId", "0"),
                    new XAttribute("applyFont", "1"),
                    new XAttribute("applyBorder", "1"),
                    new XAttribute("applyAlignment", "1"));
                if (applyNumFmt) xf.Add(new XAttribute("applyNumberFormat", "1"));
                if (applyFill) xf.Add(new XAttribute("applyFill", "1"));
                xf.Add(new XElement(MainNs + "alignment", new XAttribute("vertical", "center")));
                return xf;
            }

            // Style IDs: 0=Normal, 1=Header, 2=Zebra, 3=Text(@), 4=Text(@)+Zebra
            var cellXfs = new XElement(MainNs + "cellXfs", new XAttribute("count", "5"),
                MakeXf(0, 0, 0, 1),
                MakeXf(0, 1, 2, 1, applyFill: true),
                MakeXf(0, 0, 3, 1, applyFill: true),
                MakeXf(49, 2, 0, 1, applyNumFmt: true),
                MakeXf(49, 2, 3, 1, applyNumFmt: true, applyFill: true));

            var cellStyles = new XElement(MainNs + "cellStyles", new XAttribute("count", "1"),
                new XElement(MainNs + "cellStyle",
                    new XAttribute("name", "Normal"),
                    new XAttribute("xfId", "0"),
                    new XAttribute("builtinId", "0")));

            var root = new XElement(MainNs + "styleSheet",
                fonts, fills, borders, cellStyleXfs, cellXfs, cellStyles);

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        private static XDocument BuildWorksheetDoc(NativeSheetSpec spec, bool isFirstTab)
        {
            int colCount = Math.Max(1, spec.Headers.Length);
            int totalRows = spec.Rows.Count + 1;

            var ws = new XElement(MainNs + "worksheet");

            // 1. sheetViews (freeze header row 1)
            var sheetView = new XElement(MainNs + "sheetView", new XAttribute("workbookViewId", "0"));
            if (isFirstTab) sheetView.Add(new XAttribute("tabSelected", "1"));
            sheetView.Add(new XElement(MainNs + "pane",
                new XAttribute("ySplit", "1"),
                new XAttribute("topLeftCell", "A2"),
                new XAttribute("activePane", "bottomLeft"),
                new XAttribute("state", "frozen")));
            ws.Add(new XElement(MainNs + "sheetViews", sheetView));

            // 2. cols (auto-fit column widths)
            var cols = new XElement(MainNs + "cols");
            for (int c = 1; c <= spec.Headers.Length; c++)
            {
                int maxLen = (spec.Headers[c - 1] ?? string.Empty).Length;
                foreach (var rowData in spec.Rows)
                {
                    if (c - 1 < rowData.Length && rowData[c - 1] != null)
                        maxLen = Math.Max(maxLen, rowData[c - 1].Length);
                }
                double width = Math.Min(Math.Max(maxLen + 4, 12), 72);
                cols.Add(new XElement(MainNs + "col",
                    new XAttribute("min", c.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("max", c.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("width", width.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("customWidth", "1")));
            }
            if (cols.HasElements) ws.Add(cols);

            // 3. sheetData
            var sheetData = new XElement(MainNs + "sheetData");

            var headerRow = new XElement(MainNs + "row",
                new XAttribute("r", "1"),
                new XAttribute("ht", "22"),
                new XAttribute("customHeight", "1"));
            for (int c = 0; c < spec.Headers.Length; c++)
            {
                headerRow.Add(BuildInlineCell($"{NativeWorksheet.GetColumnLetter(c + 1)}1", 1, spec.Headers[c] ?? string.Empty));
            }
            sheetData.Add(headerRow);

            for (int r = 0; r < spec.Rows.Count; r++)
            {
                int excelRow = r + 2;
                bool isZebra = r % 2 == 1;
                var rowData = spec.Rows[r];
                var rowElem = new XElement(MainNs + "row",
                    new XAttribute("r", excelRow.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("ht", "20"),
                    new XAttribute("customHeight", "1"));

                for (int c = 0; c < spec.Headers.Length; c++)
                {
                    int col1 = c + 1;
                    bool isTextCol = spec.TextColumns.Contains(col1);
                    int styleId = isTextCol
                        ? (isZebra ? 4 : 3)
                        : (isZebra ? 2 : 0);

                    string val = c < rowData.Length ? (rowData[c] ?? string.Empty) : string.Empty;
                    rowElem.Add(BuildInlineCell($"{NativeWorksheet.GetColumnLetter(col1)}{excelRow}", styleId, val));
                }
                sheetData.Add(rowElem);
            }
            ws.Add(sheetData);

            // 4. autoFilter
            string lastCol = NativeWorksheet.GetColumnLetter(colCount);
            ws.Add(new XElement(MainNs + "autoFilter",
                new XAttribute("ref", $"A1:{lastCol}{Math.Max(1, totalRows)}")));

            // 5. dataValidations
            if (spec.DataValidations.Count > 0)
            {
                var dvs = new XElement(MainNs + "dataValidations",
                    new XAttribute("count", spec.DataValidations.Count.ToString(CultureInfo.InvariantCulture)));
                foreach (var (sqRef, listValues) in spec.DataValidations)
                {
                    string f1 = listValues.StartsWith("\"", StringComparison.Ordinal)
                        ? listValues
                        : $"\"{listValues}\"";

                    dvs.Add(new XElement(MainNs + "dataValidation",
                        new XAttribute("type", "list"),
                        new XAttribute("allowBlank", "1"),
                        new XAttribute("showInputMessage", "1"),
                        new XAttribute("showErrorMessage", "1"),
                        new XAttribute("sqref", sqRef),
                        new XElement(MainNs + "formula1", f1)));
                }
                ws.Add(dvs);
            }

            return new XDocument(new XDeclaration("1.0", "utf-8", null), ws);
        }

        private static XElement BuildInlineCell(string cellRef, int styleId, string text)
        {
            string clean = SanitizeXmlText(text);
            return new XElement(MainNs + "c",
                new XAttribute("r", cellRef),
                new XAttribute("s", styleId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("t", "inlineStr"),
                new XElement(MainNs + "is",
                    new XElement(MainNs + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"),
                        clean)));
        }

        private static string SanitizeXmlText(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input!.Length);
            foreach (char ch in input)
            {
                if (ch == '\t' || ch == '\n' || ch == '\r' || ch >= 0x20)
                    sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
