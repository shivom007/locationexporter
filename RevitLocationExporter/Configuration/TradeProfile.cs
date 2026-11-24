using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitLocationExporter.Configuration
{
    /// <summary>
    /// Encapsulates all trade-specific settings, rules, and naming conventions.
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

        // Cached filter to avoid re-allocating on every query
        private ElementMulticategoryFilter? _categoryFilter;

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
            return template
                .Replace("{ProjectNumber}", projectNumber)
                .Replace("{Originator}", originator)
                .Replace("{TradeCode}", TradeCode)
                .Replace("{Location}", location);
        }

        /// <summary>
        /// Creates a deep clone of this trade profile.
        /// </summary>
        public TradeProfile Clone()
        {
            return new TradeProfile
            {
                TradeCode = this.TradeCode,
                TradeName = this.TradeName,
                WorkPackageSubfolder = this.WorkPackageSubfolder,
                DisciplineFolder = this.DisciplineFolder,
                WorksetMatchTokens = new List<string>(this.WorksetMatchTokens ?? Enumerable.Empty<string>()),
                WorksetSuffixes = new List<string>(this.WorksetSuffixes ?? Enumerable.Empty<string>()),
                TargetCategoryNames = new List<string>(this.TargetCategoryNames ?? Enumerable.Empty<string>()),
                RvtFileNameTemplate = this.RvtFileNameTemplate,
                IfcFileNameTemplate = this.IfcFileNameTemplate,
                NwcFileNameTemplate = this.NwcFileNameTemplate
            };
        }

        public override string ToString()
        {
            return $"[{TradeCode}] {TradeName}";
        }
    }
}
