namespace RevitLocationExporter.Configuration
{
    /// <summary>
    /// User-selected operations to execute for each location in the pipeline.
    /// Controlled via checkboxes in the LocationSelectionDialog.
    /// </summary>
    public class ExportExecutionOptions
    {
        // ── Data & Setup ─────────────────────────────────────────────
        /// <summary>Populate Sheet Cover coordinates, CPK device/element parameters, and Project Information.</summary>
        public bool FillParameters { get; set; } = true;

        /// <summary>Activate ProjectLocation, align Survey Point, and rename worksets with trade suffixes.</summary>
        public bool AlignCoordinatesAndWorksets { get; set; } = true;

        /// <summary>Iteratively purge unused elements from the model.</summary>
        public bool PurgeUnused { get; set; } = true;

        // ── 3D Model Exports ─────────────────────────────────────────
        /// <summary>Export Navisworks .nwc file.</summary>
        public bool ExportNwc { get; set; } = true;

        /// <summary>Export Industry Foundation Classes .ifc file via IFCSettings.json.</summary>
        public bool ExportIfc { get; set; } = true;

        /// <summary>Save as a workshared Central model (.rvt) in the destination folder.</summary>
        public bool SaveCentralRvt { get; set; } = true;

        // ── 2D Sheet Exports ─────────────────────────────────────────
        /// <summary>Export matching location sheets to PDF.</summary>
        public bool ExportPdf { get; set; } = false;

        /// <summary>Export matching location sheets to DWG.</summary>
        public bool ExportDwg { get; set; } = false;

        /// Fill missing parameters / fix worksets from the Model Checker check-sets.
        public bool EnforceModelStandards { get; set; } = false;

        /// true = evaluate and write the CSV report, but roll back all changes.
        public bool ModelStandardsDryRun { get; set; } = false;

        /// <summary>Returns true if at least one operation is enabled.</summary>
        public bool HasAnyOperationSelected =>
            FillParameters ||
            AlignCoordinatesAndWorksets ||
            PurgeUnused ||
            ExportNwc ||
            ExportIfc ||
            SaveCentralRvt ||
            ExportPdf ||
            ExportDwg;
    }
}
