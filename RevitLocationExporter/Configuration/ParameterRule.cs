using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using RevitLocationExporter.Core;

namespace RevitLocationExporter.Configuration
{
    /// <summary>Target element scope where a ParameterRule applies.</summary>
    public enum ParameterRuleTarget
    {
        Element,
        Sheet,
        Titleblock,
        SheetCover,
        ProjectInfo
    }

    /// <summary>Controls whether a ParameterRule overwrites existing values or only fills empty ones.</summary>
    public enum ParameterFillMode
    {
        Overwrite,
        FillIfEmpty
    }

    /// <summary>
    /// A configurable parameter assignment or Excel-like formula rule attached to a TradeProfile.
    /// </summary>
    public sealed class ParameterRule
    {
        public bool Enabled { get; set; } = true;
        public ParameterRuleTarget Target { get; set; } = ParameterRuleTarget.Element;
        public string Filter { get; set; } = "*";
        public string ParameterName { get; set; } = string.Empty;
        public string ValueOrFormula { get; set; } = string.Empty;
        public ParameterFillMode Mode { get; set; } = ParameterFillMode.Overwrite;
        public string Notes { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsFormula =>
            !string.IsNullOrWhiteSpace(ValueOrFormula) &&
            ValueOrFormula.TrimStart().StartsWith("=", StringComparison.Ordinal);

        public ParameterRule Clone()
        {
            return new ParameterRule
            {
                Enabled = this.Enabled,
                Target = this.Target,
                Filter = this.Filter,
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

        public void EnsureCaseInsensitive()
        {
            ElementParameters = NormalizeNested(ElementParameters);
            SheetParameters = NormalizeNested(SheetParameters);
            SheetNumberParameters = NormalizeNested(SheetNumberParameters);
            ExportLocations = ExportLocations != null
                ? new Dictionary<string, string>(ExportLocations, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            StandardsFillRows ??= new List<StandardsFillRowDto>();
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
                    .ToList()
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
                StandardsFillRows = StandardsFillRows.Select(r => r.ToCoreRow()).ToList()
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
                    .ToList()
            };
            return snap;
        }
    }
}
