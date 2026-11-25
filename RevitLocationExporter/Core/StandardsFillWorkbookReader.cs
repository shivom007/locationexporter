// ═══════════════════════════════════════════════════════════════════════════
//  StandardsFillWorkbookReader.cs  –  RevitLocationExporter.Core
//
//  Reads the "Standards_Fill" tab (rows, NOT keyed by Locator Code – one
//  locator can have many rows) using zero-dependency NativeWorksheet.
//
//  Columns (header names, any order, extra columns ignored):
//    Locator Code | Category | Family Type | Parameter | Value
//  Blank Locator/Category/Family Type = "*" (all).  Blank Value = row ignored.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitLocationExporter.Core
{
    public static class StandardsFillWorkbookReader
    {
        /// <summary>Tab picker: "Standards_Fill", "Model_Standards", ...</summary>
        public static bool IsStandardsTab(string tabName) =>
            tabName.IndexOf("standard", StringComparison.OrdinalIgnoreCase) >= 0;

        public static List<StandardsFillRow> FromNativeWorksheet(NativeWorksheet ws)
        {
            if (!ws.HasData || ws.MaxRow < 2 || ws.MaxCol < 1)
                return new List<StandardsFillRow>();

            int firstRow = ws.FirstNonEmptyRowNumber();
            if (firstRow == 0 || firstRow >= ws.MaxRow)
                return new List<StandardsFillRow>();

            var table = new List<IList<string>>();
            for (int r = firstRow; r <= ws.MaxRow; r++)
            {
                if (r > firstRow && ws.IsRowEmpty(r)) continue;
                var cells = new List<string>(ws.MaxCol);
                for (int c = 1; c <= ws.MaxCol; c++)
                    cells.Add(ws.GetEvaluatedText(r, c));
                table.Add(cells);
            }

            if (table.Count < 2) return new List<StandardsFillRow>();
            return StandardsFillReader.FromTable(table[0], table.Skip(1));
        }
    }
}
