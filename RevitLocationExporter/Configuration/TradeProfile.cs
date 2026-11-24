using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;

namespace RevitLocationExporter.Configuration
{
    /// <summary>
    /// Encapsulates all trade-specific settings, rules, parameters, formulas, Excel sync snapshot,
    /// project metadata, sheet rules, and naming conventions.
    /// Supports any discipline (TI0, EL0, ME0, PL0, AR0, ST0, or user-created).
    /// </summary>
    public class TradeProfile
    {
        /// <summary>Trade abbreviation matching ISO 19650 filename segment 2 (e.g., "TI0", "EL0", "ME0", "PL0").</summary>
        public string TradeCode { get; set; } = "TI0";

        /// <summary>Friendly trade name (e.g., "Telecom", "Electrical", "Mechanical", "Plumbing").</summary>
        public string TradeName { get; set; } = "Telecom";

        /// <summary>Folder name inside WIP\EL\Models\ or WIP\{Discipline}\Models\.</summary>
        public string WorkPackageSubfolder { get; set; } = "Telecom - Building Package";

        /// <summary>Discipline folder code in path (e.g. "EL", "ME", "AR", "ST", "TL").</summary>
        public string DisciplineFolder { get; set; } = "TL";

        /// <summary>Project number token for this trade (e.g. "P00100087").</summary>
        public string ProjectNumber { get; set; } = "P00100087";

        /// <summary>Originator code token for this trade (e.g. "DAR").</summary>
        public string Originator { get; set; } = "DAR";

        /// <summary>Tokens used to identify worksets belonging to this trade when opening detached models.</summary>
        public List<string> WorksetMatchTokens { get; set; } = new() { "TI0" };

        /// <summary>Suffixes used to rename trade worksets (e.g., "_TI0_FA", "_TI0_SD").</summary>
        public List<string> WorksetSuffixes { get; set; } = new()
        {
            "_TI0_FA", "_TI0_SD", "_TI0_DD", "_TI0_LC"
        };

        /// <summary>Revit BuiltInCategory string names whose instances/types should have CPK parameters populated.</summary>
        public List<string> TargetCategoryNames { get; set; } = new()
        {
            "OST_FireAlarmDevices",
            "OST_DataDevices",
            "OST_SecurityDevices",
            "OST_TelephoneDevices",
            "OST_AudioVisualDevices",
            "OST_CommunicationDevices",
            "OST_CableTray",
            "OST_CableTrayFitting",
            "OST_Conduit",
            "OST_ConduitFitting"
        };

        /// <summary>Template for Central RVT filename. Tokens: {ProjectNumber}, {Originator}, {TradeCode}, {Location}.</summary>
        public string RvtFileNameTemplate { get; set; } = "{ProjectNumber}-{Originator}-{TradeCode}-{Location}-XX-M3-XX0001_PE";

        /// <summary>Template for IFC export filename.</summary>
        public string IfcFileNameTemplate { get; set; } = "{ProjectNumber}-{Originator}-{TradeCode}-{Location}-XX-E3-XX0001_PE";

        /// <summary>Template for NWC export filename.</summary>
        public string NwcFileNameTemplate { get; set; } = "{ProjectNumber}-{Originator}-{TradeCode}-{Location}-XX-PE-XX0001_PE";

        // ── Coordinates & Paths ──────────────────────────────────────────────
        public double SurveyNorthMm { get; set; } = 5776800031.2;
        public double SurveyEastMm { get; set; } = 7457254602.8;
        public double SurveyElevMm { get; set; } = 98300.0;

        public string IFCJsonPath { get; set; } = @"%USERPROFILE%\Desktop\IFCSettings.json";
        public string ModelCheckersFolder { get; set; } = "ModelCheckers";

        // ── Project Information Overrides ────────────────────────────────────
        public Dictionary<string, string> ProjectInfoData { get; set; } = CreateDefaultProjectInfo();

        // ── Sheet Processing Rules ───────────────────────────────────────────
        public string SheetIncludeToken { get; set; } = "_PL";
        public List<string> SheetDeletePatterns { get; set; } = new() { "210001_EN" };
        public string SheetCoverName { get; set; } = "Sheet Cover";
        public bool SyncWorkPackageCheckboxes { get; set; } = true;

        // ── Default Execution Toggles ────────────────────────────────────────
        public bool DefaultFillParameters { get; set; } = true;
        public bool DefaultAlignCoordinatesAndWorksets { get; set; } = true;
        public bool DefaultPurgeUnused { get; set; } = true;
        public bool DefaultExportNwc { get; set; } = true;
        public bool DefaultExportIfc { get; set; } = true;
        public bool DefaultSaveCentralRvt { get; set; } = true;
        public bool DefaultExportPdf { get; set; } = false;
        public bool DefaultExportDwg { get; set; } = false;
        public bool DefaultEnforceModelStandards { get; set; } = false;

        // ── Parameter & Formula Rules ────────────────────────────────────────
        public List<ParameterRule> ParameterRules { get; set; } = new();

        // ── Linked Excel Sync State ──────────────────────────────────────────
        public string LinkedExcelPath { get; set; } = string.Empty;
        public bool AutoSyncExcelOnRun { get; set; } = true;
        public DateTime? LastExcelSyncUtc { get; set; }
        public DateTime? LastExcelWriteTimeUtc { get; set; }
        public SyncedExcelSnapshot? SyncedData { get; set; }

        // Cached filter to avoid re-allocating on every query
        private ElementMulticategoryFilter? _categoryFilter;

        public static Dictionary<string, string> CreateDefaultProjectInfo() =>
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "Project Name",             "Preparation of design documentation for Airside, Landside and Utilities by the Master Civil Engineer (MCE)" },
                { "Project Number",           "P00100087" },
                { "Client Name",              "Centralny Port Komunikacyjny spółka z ograniczoną odpowiedzialnością" },
                { "Project Address",          "Aleje Jerozolimskie 142B, 02-305 Warsaw, Republic of Poland" },
                { "Organization Name",        "Dar Al-Handasah Consultants (Shair and Partners) s.a.l" },
                { "Organization Description", "MCE Contractor" },
                { "Author",                   "DAR" },
                { "Group_Leader",             "Meghanath Gatade" },
                { "Model_Coordinator",        "Anuj Mishra" },
                { "Project Status",           "Stage 3" }
            };

        /// <summary>Ensures collections and dictionaries are non-null and case-insensitive after JSON load.</summary>
        public void EnsureInitialized()
        {
            WorksetMatchTokens ??= new List<string>();
            WorksetSuffixes ??= new List<string>();
            TargetCategoryNames ??= new List<string>();
            SheetDeletePatterns ??= new List<string> { "210001_EN" };
            ParameterRules ??= new List<ParameterRule>();

            if (ProjectInfoData == null || ProjectInfoData.Count == 0)
            {
                ProjectInfoData = CreateDefaultProjectInfo();
            }
            else
            {
                ProjectInfoData = new Dictionary<string, string>(ProjectInfoData, StringComparer.OrdinalIgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(ProjectNumber)) ProjectNumber = "P00100087";
            if (string.IsNullOrWhiteSpace(Originator)) Originator = "DAR";
            if (string.IsNullOrWhiteSpace(SheetIncludeToken)) SheetIncludeToken = "_PL";
            if (string.IsNullOrWhiteSpace(SheetCoverName)) SheetCoverName = "Sheet Cover";
            if (string.IsNullOrWhiteSpace(IFCJsonPath)) IFCJsonPath = @"%USERPROFILE%\Desktop\IFCSettings.json";
            if (string.IsNullOrWhiteSpace(ModelCheckersFolder)) ModelCheckersFolder = "ModelCheckers";

            SyncedData?.EnsureCaseInsensitive();
        }

        public string ResolveIfcJsonPath()
        {
            string raw = string.IsNullOrWhiteSpace(IFCJsonPath)
                ? @"%USERPROFILE%\Desktop\IFCSettings.json"
                : IFCJsonPath.Trim();

            string expanded = Environment.ExpandEnvironmentVariables(raw);
            if (!Path.IsPathRooted(expanded))
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                expanded = Path.Combine(assemblyDir, expanded);
            }
            return expanded;
        }

        public string ResolveModelCheckersFolder()
        {
            string raw = string.IsNullOrWhiteSpace(ModelCheckersFolder)
                ? "ModelCheckers"
                : ModelCheckersFolder.Trim();

            string expanded = Environment.ExpandEnvironmentVariables(raw);
            if (!Path.IsPathRooted(expanded))
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                expanded = Path.Combine(assemblyDir, expanded);
            }
            return expanded;
        }

        public ExportExecutionOptions BuildDefaultExportOptions() => new()
        {
            FillParameters = DefaultFillParameters,
            AlignCoordinatesAndWorksets = DefaultAlignCoordinatesAndWorksets,
            PurgeUnused = DefaultPurgeUnused,
            ExportNwc = DefaultExportNwc,
            ExportIfc = DefaultExportIfc,
            SaveCentralRvt = DefaultSaveCentralRvt,
            ExportPdf = DefaultExportPdf,
            ExportDwg = DefaultExportDwg,
            EnforceModelStandards = DefaultEnforceModelStandards,
            ModelStandardsDryRun = false
        };

        /// <summary>
        /// Invalidates cached Revit filter. Call when TargetCategoryNames has been updated.
        /// </summary>
        public void InvalidateFilter()
        {
            _categoryFilter = null;
        }

        /// <summary>
        /// Creates or returns a cached ElementMulticategoryFilter for the configured categories.
        /// Returns null if no valid categories are configured.
        /// </summary>
        public ElementMulticategoryFilter? GetCategoryFilter()
        {
            if (_categoryFilter != null)
                return _categoryFilter;

            var validCategoryIds = new List<ElementId>();
            foreach (string catName in TargetCategoryNames)
            {
                if (Enum.TryParse<BuiltInCategory>(catName, out var bic))
                {
                    validCategoryIds.Add(new ElementId(bic));
                }
            }

            if (validCategoryIds.Count > 0)
                _categoryFilter = new ElementMulticategoryFilter(validCategoryIds);

            return _categoryFilter;
        }

        /// <summary>
        /// Checks if a workset name matches any configured token for this trade.
        /// </summary>
        public bool MatchesWorkset(string worksetName)
        {
            if (string.IsNullOrWhiteSpace(worksetName)) return false;
            foreach (string token in WorksetMatchTokens)
            {
                if (worksetName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Formats an output filename replacing tokens with actual values.
        /// </summary>
        public string FormatFileName(string template, string projectNumber, string originator, string location)
        {
            return (template ?? string.Empty)
                .Replace("{ProjectNumber}", projectNumber ?? string.Empty)
                .Replace("{Originator}", originator ?? string.Empty)
                .Replace("{TradeCode}", TradeCode ?? string.Empty)
                .Replace("{Location}", location ?? string.Empty);
        }

        /// <summary>
        /// Creates a deep clone of this trade profile.
        /// </summary>
        public TradeProfile Clone()
        {
            EnsureInitialized();
            return new TradeProfile
            {
                TradeCode = this.TradeCode,
                TradeName = this.TradeName,
                WorkPackageSubfolder = this.WorkPackageSubfolder,
                DisciplineFolder = this.DisciplineFolder,
                ProjectNumber = this.ProjectNumber,
                Originator = this.Originator,
                WorksetMatchTokens = new List<string>(this.WorksetMatchTokens ?? Enumerable.Empty<string>()),
                WorksetSuffixes = new List<string>(this.WorksetSuffixes ?? Enumerable.Empty<string>()),
                TargetCategoryNames = new List<string>(this.TargetCategoryNames ?? Enumerable.Empty<string>()),
                RvtFileNameTemplate = this.RvtFileNameTemplate,
                IfcFileNameTemplate = this.IfcFileNameTemplate,
                NwcFileNameTemplate = this.NwcFileNameTemplate,
                SurveyNorthMm = this.SurveyNorthMm,
                SurveyEastMm = this.SurveyEastMm,
                SurveyElevMm = this.SurveyElevMm,
                IFCJsonPath = this.IFCJsonPath,
                ModelCheckersFolder = this.ModelCheckersFolder,
                ProjectInfoData = new Dictionary<string, string>(
                    this.ProjectInfoData ?? CreateDefaultProjectInfo(),
                    StringComparer.OrdinalIgnoreCase),
                SheetIncludeToken = this.SheetIncludeToken,
                SheetDeletePatterns = new List<string>(this.SheetDeletePatterns ?? Enumerable.Empty<string>()),
                SheetCoverName = this.SheetCoverName,
                SyncWorkPackageCheckboxes = this.SyncWorkPackageCheckboxes,
                DefaultFillParameters = this.DefaultFillParameters,
                DefaultAlignCoordinatesAndWorksets = this.DefaultAlignCoordinatesAndWorksets,
                DefaultPurgeUnused = this.DefaultPurgeUnused,
                DefaultExportNwc = this.DefaultExportNwc,
                DefaultExportIfc = this.DefaultExportIfc,
                DefaultSaveCentralRvt = this.DefaultSaveCentralRvt,
                DefaultExportPdf = this.DefaultExportPdf,
                DefaultExportDwg = this.DefaultExportDwg,
                DefaultEnforceModelStandards = this.DefaultEnforceModelStandards,
                ParameterRules = (this.ParameterRules ?? new List<ParameterRule>())
                    .Select(r => r.Clone())
                    .ToList(),
                LinkedExcelPath = this.LinkedExcelPath,
                AutoSyncExcelOnRun = this.AutoSyncExcelOnRun,
                LastExcelSyncUtc = this.LastExcelSyncUtc,
                LastExcelWriteTimeUtc = this.LastExcelWriteTimeUtc,
                SyncedData = this.SyncedData?.Clone()
            };
        }

        public override string ToString()
        {
            return $"[{TradeCode}] {TradeName}";
        }
    }
}
