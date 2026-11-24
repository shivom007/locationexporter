using System;
using System.Collections.Generic;

namespace RevitLocationExporter.Configuration
{
    /// <summary>
    /// Global workset configuration applied across all selected models during processing.
    /// </summary>
    public class WorksetSelectionOptions
    {
        /// <summary>If true, all user worksets in every model will be opened.</summary>
        public bool OpenAllWorksets { get; set; } = false;

        /// <summary>If true, worksets matching the model's active trade profile are automatically opened.</summary>
        public bool OpenTradeWorksets { get; set; } = true;

        /// <summary>Explicit workset names chosen by the user to open across all models.</summary>
        public HashSet<string> SelectedWorksetNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Evaluates whether a workset with the specified name should be opened for a given trade profile.
        /// </summary>
        public bool ShouldOpenWorkset(string worksetName, TradeProfile trade)
        {
            if (string.IsNullOrWhiteSpace(worksetName)) return false;
            if (OpenAllWorksets) return true;
            if (OpenTradeWorksets && trade != null && trade.MatchesWorkset(worksetName)) return true;
            if (SelectedWorksetNames.Contains(worksetName)) return true;
            return false;
        }
    }
}
