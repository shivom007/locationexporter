using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using RevitLocationExporter.Core;

namespace RevitLocationExporter.Configuration
{
    /// <summary>Target element scope where a ParameterRule applies.</summary>
    public enum ParameterRuleTarget
    {
        Element,
        ElementInstance,
        ElementType,
        ElementBoth,
        Sheet,
        Titleblock,
        SheetCover,
        ProjectInfo
    }

    /// <summary>Controls how a ParameterRule writes values to Revit parameters.</summary>
    public enum ParameterFillMode
    {
        Overwrite,
        FillIfEmpty,
        SkipIfBlankResult,
        Append,
        Prepend,
        Clear
    }

    /// <summary>View search scope for sheet TextNote replacement rules.</summary>
    public enum TextNoteScope
    {
        SheetsOnly,
        SheetsAndPlacedViews,
        SheetsPlacedViewsAndLegends
    }

    /// <summary>
    /// Opt-in per-trade rule that updates TextNote contents on matching sheets/views
    /// (e.g. swapping cross-discipline drawing locator codes on TIU Fire sheets).
    /// </summary>
    public sealed class TextNoteRule
    {
        public bool Enabled { get; set; } = true;
        public string SheetFilter { get; set; } = "*";
        public TextNoteScope Scope { get; set; } = TextNoteScope.SheetsPlacedViewsAndLegends;
        public string NoteContains { get; set; } = string.Empty;
        public string FindPattern { get; set; } = string.Empty;
        public bool IsRegex { get; set; } = true;
        public string ReplaceWith { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;

        public TextNoteRule Clone() => new()
        {
            Enabled = this.Enabled,
            SheetFilter = this.SheetFilter,
            Scope = this.Scope,
            NoteContains = this.NoteContains,
            FindPattern = this.FindPattern,
            IsRegex = this.IsRegex,
            ReplaceWith = this.ReplaceWith,
            Notes = this.Notes
        };

        public static TextNoteRule CreateDefaultTiuInertGasRule() => new()
        {
            Enabled = true,
            SheetFilter = "*TIU*",
            Scope = TextNoteScope.SheetsPlacedViewsAndLegends,
            NoteContains = "INERT GAS SYSTEM",
            FindPattern = "P00100087-DAR-MIF-[A-Z0-9]+",
            IsRegex = true,
            ReplaceWith = @"=[ProjectNumber]&""-""&[Originator]&""-MIF-""&[Location]",
            Notes = "Update Mechanical Inert Gas drawing locator on TIU fire sheets"
        };
    }

    /// <summary>
    /// A configurable parameter assignment or Excel-like formula rule attached to a TradeProfile.
    /// </summary>
    public sealed class ParameterRule
    {
        public bool Enabled { get; set; } = true;
        public ParameterRuleTarget Target { get; set; } = ParameterRuleTarget.Element;
        public string Filter { get; set; } = "*";
        public string Condition { get; set; } = string.Empty;
        public string ParameterName { get; set; } = string.Empty;
        public string ValueOrFormula { get; set; } = string.Empty;
        public ParameterFillMode Mode { get; set; } = ParameterFillMode.Overwrite;
        public string Notes { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsFormula =>
            !string.IsNullOrWhiteSpace(ValueOrFormula) &&
            ValueOrFormula.TrimStart().StartsWith("=", StringComparison.Ordinal);

        [JsonIgnore]
        public bool HasCondition => !string.IsNullOrWhiteSpace(Condition);

        public ParameterRule Clone()
        {
            return new ParameterRule
            {
                Enabled = this.Enabled,
                Target = this.Target,
                Filter = this.Filter,
                Condition = this.Condition,
                ParameterName = this.ParameterName,
                ValueOrFormula = this.ValueOrFormula,
                Mode = this.Mode,
                Notes = this.Notes
            };
        }
    }

    /// <summary>Serializable DTO for Standards_Fill rows cached inside a TradeProfile.</summary>
    public sealed class StandardsFillRowDto
    {
        public string Locator { get; set; } = "*";
        public string Category { get; set; } = "*";
        public string FamilyType { get; set; } = "*";
        public string Parameter { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

        public StandardsFillRowDto Clone() => new()
        {
            Locator = this.Locator,
            Category = this.Category,
            FamilyType = this.FamilyType,
            Parameter = this.Parameter,
            Value = this.Value
        };

        public StandardsFillRow ToCoreRow() => new()
        {
            Locator = string.IsNullOrWhiteSpace(Locator) ? "*" : Locator,
            Category = string.IsNullOrWhiteSpace(Category) ? "*" : Category,
            FamilyType = string.IsNullOrWhiteSpace(FamilyType) ? "*" : FamilyType,
            Parameter = Parameter ?? string.Empty,
            Value = Value ?? string.Empty
        };

        public static StandardsFillRowDto FromCoreRow(StandardsFillRow row) => new()
        {
            Locator = row.Locator ?? "*",
            Category = row.Category ?? "*",
            FamilyType = row.FamilyType ?? "*",
            Parameter = row.Parameter ?? string.Empty,
            Value = row.Value ?? string.Empty
        };
    }

    /// <summary>
    /// Cached snapshot of an Excel workbook synced to a TradeProfile.
    /// Allows offline / lock-free execution when Excel is inaccessible, and lets the UI preview formulas.
    /// </summary>
    public sealed class SyncedExcelSnapshot
    {
        public Dictionary<string, Dictionary<string, string>> ElementParameters { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Dictionary<string, string>> SheetParameters { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Dictionary<string, string>> SheetNumberParameters { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> ExportLocations { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<StandardsFillRowDto> StandardsFillRows { get; set; } = new();

        /// <summary>Generic row tables for every synced Excel worksheet (enables XLOOKUP formulas).</summary>
        public Dictionary<string, List<Dictionary<string, string>>> LookupTables { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public void EnsureCaseInsensitive()
        {
            ElementParameters = NormalizeNested(ElementParameters);
            SheetParameters = NormalizeNested(SheetParameters);
            SheetNumberParameters = NormalizeNested(SheetNumberParameters);
            ExportLocations = ExportLocations != null
                ? new Dictionary<string, string>(ExportLocations, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            StandardsFillRows ??= new List<StandardsFillRowDto>();
            LookupTables = NormalizeLookupTables(LookupTables);
        }

        private static Dictionary<string, Dictionary<string, string>> NormalizeNested(
            Dictionary<string, Dictionary<string, string>>? src)
        {
            var dst = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (src == null) return dst;
            foreach (var kv in src)
            {
                dst[kv.Key] = kv.Value != null
                    ? new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            return dst;
        }

        private static Dictionary<string, List<Dictionary<string, string>>> NormalizeLookupTables(
            Dictionary<string, List<Dictionary<string, string>>>? src)
        {
            var dst = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
            if (src == null) return dst;
            foreach (var kv in src)
            {
                var list = new List<Dictionary<string, string>>();
                if (kv.Value != null)
                {
                    foreach (var row in kv.Value)
                    {
                        list.Add(row != null
                            ? new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                    }
                }
                dst[kv.Key] = list;
            }
            return dst;
        }

        public SyncedExcelSnapshot Clone()
        {
            return new SyncedExcelSnapshot
            {
                ElementParameters = NormalizeNested(ElementParameters),
                SheetParameters = NormalizeNested(SheetParameters),
                SheetNumberParameters = NormalizeNested(SheetNumberParameters),
                ExportLocations = new Dictionary<string, string>(
                    ExportLocations ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase),
                StandardsFillRows = (StandardsFillRows ?? new List<StandardsFillRowDto>())
                    .Select(r => r.Clone())
                    .ToList(),
                LookupTables = NormalizeLookupTables(LookupTables)
            };
        }

        public LocationParameterBundle ToLocationParameterBundle(string? sourcePath = null)
        {
            EnsureCaseInsensitive();
            return new LocationParameterBundle
            {
                SourcePath = sourcePath ?? string.Empty,
                ElementTabName = "Synced:Element_Params",
                SheetTabName = "Synced:Sheet_Params",
                SheetSpecificTabName = "Synced:Sheet_Specific_Params",
                ExportTabName = "Synced:Export_Locations",
                StandardsTabName = "Synced:Standards_Fill",
                ElementParameters = NormalizeNested(ElementParameters),
                SheetParameters = NormalizeNested(SheetParameters),
                SheetNumberParameters = NormalizeNested(SheetNumberParameters),
                ExportLocations = new Dictionary<string, string>(ExportLocations, StringComparer.OrdinalIgnoreCase),
                StandardsFillRows = StandardsFillRows.Select(r => r.ToCoreRow()).ToList(),
                LookupTables = NormalizeLookupTables(LookupTables)
            };
        }

        public static SyncedExcelSnapshot FromLocationParameterBundle(LocationParameterBundle bundle)
        {
            var snap = new SyncedExcelSnapshot
            {
                ElementParameters = NormalizeNested(bundle.ElementParameters),
                SheetParameters = NormalizeNested(bundle.SheetParameters),
                SheetNumberParameters = NormalizeNested(bundle.SheetNumberParameters),
                ExportLocations = new Dictionary<string, string>(bundle.ExportLocations, StringComparer.OrdinalIgnoreCase),
                StandardsFillRows = (bundle.StandardsFillRows ?? new List<StandardsFillRow>())
                    .Select(StandardsFillRowDto.FromCoreRow)
                    .ToList(),
                LookupTables = NormalizeLookupTables(bundle.LookupTables)
            };
            return snap;
        }
    }
}
