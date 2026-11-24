// ═══════════════════════════════════════════════════════════════════════════
//  StandardsFillWorkbookReader.cs  –  RevitLocationExporter.Core
//
//  Reads the "Standards_Fill" tab (rows, NOT keyed by Locator Code – one
//  locator can have many rows).  Used by LocationDataSheetReader for both the
//  ClosedXML path and the native (zip/XML) fallback path.
//
//  Columns (header names, any order, extra columns ignored):
//    Locator Code | Category | Family Type | Parameter | Value
//  Blank Locator/Category/Family Type = "*" (all).  Blank Value = row ignored.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace RevitLocationExporter.Core
{
    public static class StandardsFillWorkbookReader
    {
        /// <summary>Tab picker: "Standards_Fill", "Model_Standards", ...</summary>
        public static bool IsStandardsTab(string tabName) =>
            tabName.IndexOf("standard", StringComparison.OrdinalIgnoreCase) >= 0;

        // ── ClosedXML path ────────────────────────────────────────────────
        public static List<StandardsFillRow> FromClosedXml(IXLWorksheet ws)
        {
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow < 2 || lastCol < 1) return new List<StandardsFillRow>();

            var table = new List<IList<string>>();
            for (int r = 1; r <= lastRow; r++)
            {
                var cells = new List<string>(lastCol);
                for (int c = 1; c <= lastCol; c++)
                    cells.Add(ws.Cell(r, c).GetFormattedString() ?? "");
                table.Add(cells);
            }
            return StandardsFillReader.FromTable(table[0], table.Skip(1));
        }

        // ── Native (no ClosedXML) path ────────────────────────────────────
        public static List<StandardsFillRow> FromNativeEntry(ZipArchiveEntry sheetEntry, IList<string> sharedStrings)
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XDocument xd;
            using (var s = sheetEntry.Open()) xd = XDocument.Load(s);

            var table = new List<IList<string>>();
            foreach (XElement row in xd.Descendants(ns + "row"))
            {
                var cells = new List<string>();
                foreach (XElement c in row.Elements(ns + "c"))
                {
                    string r = (string?)c.Attribute("r") ?? "";
                    int col = r.Length > 0 ? ColIndex(r) : cells.Count;
                    while (cells.Count < col) cells.Add("");

                    string t = (string?)c.Attribute("t") ?? "";
                    string v = (string?)c.Element(ns + "v") ?? "";
                    string text =
                        t == "s" && int.TryParse(v, out int si) && si >= 0 && si < sharedStrings.Count ? sharedStrings[si] :
                        t == "inlineStr" ? string.Concat(c.Descendants(ns + "t").Select(x => x.Value)) :
                        v;

                    if (col < cells.Count) cells[col] = text; else cells.Add(text);
                }
                table.Add(cells);
            }

            if (table.Count < 2) return new List<StandardsFillRow>();
            return StandardsFillReader.FromTable(table[0], table.Skip(1));
        }

        private static int ColIndex(string cellRef)
        {
            int n = 0;
            foreach (char ch in cellRef)
            {
                if (!char.IsLetter(ch)) break;
                n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return Math.Max(0, n - 1);
        }
    }
}
