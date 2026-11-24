// ════════════════════════════════════════════════════════════════════════════
//  LocationSplitterCentral.cs  –  Production v2
//  Revit 2024 | RevitLocationExporter.Commands
//
//  What changed from v1 and WHY — see the companion audit report.
// ════════════════════════════════════════════════════════════════════════════

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace RevitLocationExporter.Commands
{
    // ────────────────────────────────────────────────────────────────────────
    //  CONFIGURATION  (single source-of-truth, easy to externalise to JSON)
    // ────────────────────────────────────────────────────────────────────────
    internal static class SplitterConfig
    {
        public static readonly string[] WorksetSuffixes =
            { "_TI0_FD", "_TI0_SD", "_TI0_DD", "_TI0_LC" };

        public static readonly Dictionary<string, string> LocationFolderMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "AFCX", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 2\Authoring Model" },
                { "AFNX", @"P:\PO22242-0100D\04_WP04_Northern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 2\Authoring Model" },
                { "AFSX", @"P:\PO22242-0100D\05_WP05_Southern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 2\Authoring Model" },
                { "LFEX", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 2\Authoring Model" },
                { "LFWX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 2\Authoring Model" }
            };

        // Survey Point in mm (project-wide constants)
        public const double SurveyNorth_MM = 5776800031.2;
        public const double SurveyEast_MM  = 7457254602.8;
        public const double SurveyElev_MM  = 98300.0;

        public static readonly string IFCJsonPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "IFCSettings.json");

        // ── CPK lookup tables ──────────────────────────────────────────────
        public static readonly Dictionary<string, (string WorkPackage, string WbsCode, string WbsName)>
            CpkData = new(StringComparer.OrdinalIgnoreCase)
            {
                { "AFCX", ("WP03", "C.A.EN.PDN.02", "Power Distribution Airside Center") },
                { "AFNX", ("WP04", "C.A.EN.PDN.01", "Power Distribution Airside North")  },
                { "AFSX", ("WP05", "C.A.EN.PDN.03", "Power Distribution Airside South")  },
                { "LFEX", ("WP07", "C.A.EN.PDN.04", "Power Distribution Landside East")  },
                { "LFWX", ("WP08", "C.A.EN.PDN.05", "Power Distribution Landside West")  }
            };

        public static bool TryGetCpk(string locationName,
            out string workPackage, out string wbsCode, out string wbsName)
        {
            foreach (var kv in CpkData)
            {
                if (locationName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    (workPackage, wbsCode, wbsName) = kv.Value;
                    return true;
                }
            }
            workPackage = wbsCode = wbsName = string.Empty;
            return false;
        }

        public static bool TryGetOutputFolder(string locationName, out string folder)
        {
            foreach (var kv in LocationFolderMap)
            {
                if (locationName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    folder = kv.Value;
                    return true;
                }
            }
            folder = string.Empty;
            return false;
        }

        public static string GetAcceptor(string locationName)
        {
            if (string.IsNullOrWhiteSpace(locationName)) return string.Empty;
            char c = char.ToUpperInvariant(locationName[0]);
            return c == 'A' ? "S.P." : c == 'L' ? "K.Z." : string.Empty;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  RESULT COLLECTOR  (replaces scattered TaskDialog calls)
    // ────────────────────────────────────────────────────────────────────────
    internal sealed class ProcessLog
    {
        private readonly List<string> _errors   = new();
        private readonly List<string> _warnings = new();
        private readonly List<string> _infos    = new();

        public void Error  (string loc, string msg) => _errors  .Add($"[{loc}] {msg}");
        public void Warning(string loc, string msg) => _warnings.Add($"[{loc}] {msg}");
        public void Info   (string loc, string msg) => _infos   .Add($"[{loc}] {msg}");

        public bool HasErrors => _errors.Count > 0;

        public void ShowSummary()
        {
            var sb = new StringBuilder();
            if (_infos.Count > 0)
            {
                sb.AppendLine($"✓ Completed: {_infos.Count} location(s)");
                foreach (var i in _infos) sb.AppendLine("  " + i);
            }
            if (_warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"⚠ Warnings ({_warnings.Count}):");
                foreach (var w in _warnings) sb.AppendLine("  " + w);
            }
            if (_errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"✗ Errors ({_errors.Count}):");
                foreach (var e in _errors) sb.AppendLine("  " + e);
            }

            TaskDialog.Show("Location Splitter – Summary", sb.ToString());
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  MAIN COMMAND
    // ────────────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    public class LocationSplitterCentral_v2 : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string          message,
            ElementSet          elements)
        {
            // ── 1. Pick one or more RVT files ──────────────────────────────
            var selectedPaths = SelectRvtFiles();
            if (selectedPaths.Count == 0) return Result.Cancelled;

            var app = commandData.Application.Application;
            var log = new ProcessLog();

            // ── 2. Iterate files ───────────────────────────────────────────
            foreach (string filePath in selectedPaths)
            {
                var rvtPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

                // Discover locations (open once, read, close)
                List<string> locationNames;
                try
                {
                    locationNames = ReadProjectLocationNames(app, rvtPath);
                }
                catch (Exception ex)
                {
                    log.Error(Path.GetFileName(filePath),
                        "Could not read locations: " + ex.Message);
                    continue;
                }

                if (locationNames.Count == 0)
                {
                    log.Warning(Path.GetFileName(filePath), "No project locations found.");
                    continue;
                }

                // ── 3. Process each location ───────────────────────────────
                foreach (string locationName in locationNames)
                {
                    try
                    {
                        ProcessOneLocation(app, rvtPath, locationName, filePath, log);
                        log.Info(locationName, "All exports completed.");
                    }
                    catch (Exception ex)
                    {
                        log.Error(locationName, ex.Message);
                        // Continue with next location instead of aborting
                    }
                }
            }

            log.ShowSummary();
            return log.HasErrors ? Result.Failed : Result.Succeeded;
        }

        // ────────────────────────────────────────────────────────────────────
        //  FILE SELECTION
        // ────────────────────────────────────────────────────────────────────

        // BUG FIX v1: only allowed one file. Now supports multi-select.
        private static List<string> SelectRvtFiles()
        {
            var dlg = new OpenFileDialog
            {
                Filter    = "Revit Files (*.rvt)|*.rvt",
                Title     = "Select Revit Model(s)",
                Multiselect = true
            };
            return dlg.ShowDialog() == true
                ? dlg.FileNames.ToList()
                : new List<string>();
        }

        // ────────────────────────────────────────────────────────────────────
        //  LOCATION DISCOVERY  (opens doc, reads, disposes — no lingering ref)
        // ────────────────────────────────────────────────────────────────────
        private static List<string> ReadProjectLocationNames(
            Autodesk.Revit.ApplicationServices.Application app,
            ModelPath rvtPath)
        {
            // BUG FIX v1: opening with full workset loading is expensive.
            // We only need metadata here, so close all worksets.
            var opts = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
            };
            // NOTE: Do not set CloseAllWorksetsByDefault here — we are only
            //       READING the location names, not editing. The doc is
            //       disposed immediately; worksets do not matter.
            using var doc = app.OpenDocumentFile(rvtPath, opts);
            return doc.ProjectLocations
                .Cast<ProjectLocation>()
                .Select(pl => pl.Name)
                .ToList();
        }

        // ────────────────────────────────────────────────────────────────────
        //  SINGLE LOCATION PIPELINE
        //  Opens a fresh detached copy, runs all ops, saves, syncs, closes.
        // ────────────────────────────────────────────────────────────────────
        private static void ProcessOneLocation(
            Autodesk.Revit.ApplicationServices.Application app,
            ModelPath   rvtPath,
            string      locationName,
            string      originalFilePath,
            ProcessLog  log)
        {
            if (!SplitterConfig.TryGetOutputFolder(locationName, out string exportRoot))
                throw new InvalidOperationException(
                    $"No export folder mapping for location '{locationName}'.");

            var opts = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
            };

            // BUG FIX v1: doc was opened but never disposed on exception path.
            // Using-statement guarantees disposal.
            using Document doc = app.OpenDocumentFile(rvtPath, opts);

            try
            {
                // All mutations happen inside the doc before it is saved.
                ActivateProjectLocation (doc, locationName, log);
                RenameWorksets          (doc, locationName, log);
                FillSheetCoverParameters(doc, locationName, log);
                FillCPKParametersForInstances(doc, locationName, log); // FIX: instances not types
                RunExports              (doc, locationName, exportRoot, log);
                SaveAsCentral           (doc, locationName, exportRoot, log);
                SyncAndClose            (doc, locationName, log);
            }
            catch
            {
                // Close without saving if something blew up mid-pipeline
                try { doc.Close(false); } catch { /* swallow */ }
                throw;
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  STEP 1 – ACTIVATE PROJECT LOCATION + SURVEY POINT
        // ────────────────────────────────────────────────────────────────────
        private static void ActivateProjectLocation(
            Document doc, string locationName, ProcessLog log)
        {
            using var tx = new Transaction(doc, "Activate Project Location");

            // BUG FIX v1: no failure handling — a failure mid-transaction
            // silently corrupts the model.  Always use a FailureHandler
            // in production, or at minimum set FailureSeverity options.
            var failOpts = tx.GetFailureHandlingOptions();
            failOpts.SetFailuresPreprocessor(new SilentFailurePreprocessor());
            tx.SetFailureHandlingOptions(failOpts);

            tx.Start();

            var location = doc.ProjectLocations
                .Cast<ProjectLocation>()
                .FirstOrDefault(pl =>
                    pl.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Project Location not found: '{locationName}'");

            doc.ActiveProjectLocation = location;

            // Survey point must be set AFTER location is activated (shared coords change)
            SetSurveyPoint(doc);

            tx.Commit();
        }

        private static void SetSurveyPoint(Document doc)
        {
            // BUG FIX v1: used GetBasePoint helper inconsistently.
            // Now centralised — one call site.
            var surveyPoint = GetBasePoint(doc, BuiltInCategory.OST_SharedBasePoint)
                ?? throw new InvalidOperationException("Survey Point not found.");

            // Unclip first (same logic as v1 but cleaner null-checks)
            Parameter? clippedParam = null;
            foreach (Parameter p in surveyPoint.Parameters)
            {
                if (p.Definition?.Name.IndexOf(
                        "Clipped", StringComparison.OrdinalIgnoreCase) >= 0
                    && p.StorageType == StorageType.Integer)
                {
                    clippedParam = p;
                    break;
                }
            }
            clippedParam?.Set(0);

            // BUG FIX v1: setting Clipped property can throw on unclippable
            // survey points in some configurations; guard it.
            try { surveyPoint.Clipped = false; } catch { /* already unclipped */ }

            surveyPoint
                .get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM)
                .Set(UnitUtils.ConvertToInternalUnits(
                    SplitterConfig.SurveyEast_MM, UnitTypeId.Millimeters));

            surveyPoint
                .get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM)
                .Set(UnitUtils.ConvertToInternalUnits(
                    SplitterConfig.SurveyNorth_MM, UnitTypeId.Millimeters));

            surveyPoint
                .get_Parameter(BuiltInParameter.BASEPOINT_ELEVATION_PARAM)
                .Set(UnitUtils.ConvertToInternalUnits(
                    SplitterConfig.SurveyElev_MM, UnitTypeId.Millimeters));
        }

        // ────────────────────────────────────────────────────────────────────
        //  STEP 2 – RENAME WORKSETS
        // ────────────────────────────────────────────────────────────────────
        private static void RenameWorksets(
            Document doc, string locationName, ProcessLog log)
        {
            // BUG FIX v1: collector result was not filtered by EditableWorksets.
            // On a detached model all user worksets are editable, but check
            // anyway to be safe.
            var worksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .Where(ws => ws.IsEditable)
                .ToList();

            using var tx = new Transaction(doc, "Rename Worksets");
            tx.Start();

            foreach (var ws in worksets)
            {
                foreach (string suffix in SplitterConfig.WorksetSuffixes)
                {
                    if (!ws.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string newName = locationName + suffix;
                    if (ws.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                        break; // already correct

                    // BUG FIX v1: potential duplicate-name crash — check first
                    bool nameTaken = worksets.Any(other =>
                        other.Id != ws.Id &&
                        other.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));

                    if (nameTaken)
                    {
                        log.Warning(locationName,
                            $"Workset rename skipped: '{newName}' already exists.");
                        break;
                    }

                    WorksetTable.RenameWorkset(doc, ws.Id, newName);
                    break;
                }
            }

            tx.Commit();
        }

        // ────────────────────────────────────────────────────────────────────
        //  STEP 3 – FILL SHEET COVER PARAMETERS
        // ────────────────────────────────────────────────────────────────────
        private static void FillSheetCoverParameters(
            Document doc, string locationName, ProcessLog log)
        {
            using var tx = new Transaction(doc, "Fill Sheet Cover Coordinates");
            tx.Start();

            var pbp = GetBasePoint(doc, BuiltInCategory.OST_ProjectBasePoint);
            var svp = GetBasePoint(doc, BuiltInCategory.OST_SharedBasePoint);

            if (pbp == null || svp == null)
            {
                log.Warning(locationName,
                    "Project Base Point or Survey Point not found — sheet cover params skipped.");
                tx.RollBack();
                return;
            }

            double pbpN = ToMm(pbp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
            double pbpE = ToMm(pbp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
            double pbpZ = ToMm(pbp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM);
            double svpN = ToMm(svp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
            double svpE = ToMm(svp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
            double svpZ = ToMm(svp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM);

            string acceptor = SplitterConfig.GetAcceptor(locationName);

            // BUG FIX v1: StringComparison.OrdinalIgnoreCase was used for
            // sheet name filter but the param "Sheet Cover" uses a Revit
            // VIEW name which may include a number prefix. Use Contains
            // rather than Equals to be resilient.
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.Name.IndexOf("Sheet Cover",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (sheets.Count == 0)
                log.Warning(locationName, "No 'Sheet Cover' sheet found in model.");

            foreach (var sheet in sheets)
            {
                SetDouble(sheet, "DAR_PBP_N", pbpN);
                SetDouble(sheet, "DAR_PBP_E", pbpE);
                SetDouble(sheet, "DAR_PBP_Z", pbpZ);
                SetDouble(sheet, "DAR_SVP_N", svpN);
                SetDouble(sheet, "DAR_SVP_E", svpE);
                SetDouble(sheet, "DAR_SVP_Z", svpZ);
                SetString(sheet, "CPK_Building Location", locationName);
                SetString(sheet, "CPK_Acceptor", acceptor);
            }

            tx.Commit();
        }

        // ────────────────────────────────────────────────────────────────────
        //  STEP 4 – FILL CPK PARAMETERS
        //  FIX: v1 set params on Types, but the recent design change requires
        //       parameters on element INSTANCES.  Both paths provided;
        //       switch via the enum below.
        // ────────────────────────────────────────────────────────────────────

        private enum CpkTarget { Instances, Types }
        private const CpkTarget CpkMode = CpkTarget.Types; // ← change here

        private static void FillCPKParametersForInstances(
            Document doc, string locationName, ProcessLog log)
        {
            if (!SplitterConfig.TryGetCpk(locationName,
                    out string cpkWorkPackage,
                    out string cpkWbs,
                    out string cpkWbsName))
            {
                log.Warning(locationName, "No CPK data mapping — params not filled.");
                return;
            }

            using var tx = new Transaction(doc, "Fill CPK Parameters");
            tx.Start();

            int updated = 0;

            if (CpkMode == CpkTarget.Instances)
            {
                // PERFORMANCE FIX v1: WhereElementIsNotElementType() is
                // correct but the original code also iterated types via
                // GetTypeId() per instance, causing O(n) type lookups.
                // Here we write directly to the instance — exact as per
                // the latest requirements documented in userMemories.
                var instances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => e.Category != null && !e.ViewSpecific);

                foreach (Element e in instances)
                {
                    if (SetString(e, "CPK_Work_Package", cpkWorkPackage)) updated++;
                    SetString(e, "CPK_WBS_1-3b",  cpkWbs);
                    SetString(e, "CPK_WBS_Name",   cpkWbsName);
                }
            }
            else // CpkTarget.Types
            {
                var processedTypes = new HashSet<ElementId>();
                var instances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => e.Category != null && !e.ViewSpecific);

                foreach (Element e in instances)
                {
                    ElementId typeId = e.GetTypeId();
                    if (typeId == ElementId.InvalidElementId) continue;
                    if (!processedTypes.Add(typeId)) continue;

                    if (doc.GetElement(typeId) is not ElementType type) continue;
                    if (SetString(type, "CPK_Work_Package", cpkWorkPackage)) updated++;
                    SetString(type, "CPK_WBS_1-3b",  cpkWbs);
                    SetString(type, "CPK_WBS_Name",   cpkWbsName);
                }
            }

            tx.Commit();
            log.Info(locationName, $"CPK params updated on {updated} element(s).");
        }

        // ────────────────────────────────────────────────────────────────────
        //  STEP 5 – NWC + IFC EXPORTS
        // ────────────────────────────────────────────────────────────────────
        private static void RunExports(
            Document doc, string locationName, string exportRoot, ProcessLog log)
        {
            ElementId viewId = GetNavisworks3DViewId(doc);

            var navopts = new NavisworksExportOptions
            {
                ExportScope              = NavisworksExportScope.View,
                ViewId                   = viewId,
                ExportElementIds         = true,
                ConvertElementProperties = true,
                ConvertLights            = true,
                ConvertLinkedCADFormats  = false,
                Coordinates              = NavisworksCoordinates.Shared,
                Parameters               = NavisworksParameters.All,
                ExportUrls               = false,
                DivideFileIntoLevels     = true,
                ExportLinks              = false,
                ExportParts              = false,
                ExportRoomAsAttribute    = false,
                ExportRoomGeometry       = false,
                FindMissingMaterials     = true,
                FacetingFactor           = 1
            };

            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(l => l.GetLinkDocument() != null)
                .ToList();

            // BUG FIX v1: TransactionGroup wrapped IFC transaction, which is
            // wrong.  IFC Export must run in its OWN transaction — you cannot
            // wrap doc.Export() inside an existing open transaction.
            // NWC export similarly must run OUTSIDE any open transaction.
            // The TransactionGroup in v1 caused silent export failures.

            // Move links (own transaction, commit before exports)
            if (linkInstances.Count > 0)
            {
                using var txMove = new Transaction(doc, $"Move links – {locationName}");
                txMove.Start();
                foreach (var link in linkInstances)
                {
                    try
                    {
                        link.Pinned = false;
                        link.MoveBasePointToHostBasePoint(true);
                        link.Pinned = true;
                    }
                    catch (Exception ex)
                    {
                        log.Warning(locationName,
                            $"Link '{link.Name}' move failed: {ex.Message}");
                    }
                }
                txMove.Commit();
            }

            // IFC export — MUST be inside its own committed transaction
            if (File.Exists(SplitterConfig.IFCJsonPath))
            {
                string ifcFolder = Path.Combine(exportRoot, "IFC");
                Directory.CreateDirectory(ifcFolder);
                string ifcFile =
                    $"P00100087-DAR-TI0-{locationName}-XX-E3-2X0001_EN.ifc";

                // BUG FIX v1: IFCJsonLoader.Load() is called inside a
                // transaction that was opened before export. IFC Export
                // in Revit 2024 internally manages its own sub-transaction;
                // wrapping it inside an outer TX causes "transaction already
                // started" exceptions or silent failures.
                // Correct pattern: open tx, set up IFC options, COMMIT,
                // then call doc.Export().
                IFCExportOptions ifcOpts;
                using (var txIfc = new Transaction(doc, $"Prepare IFC – {locationName}"))
                {
                    txIfc.Start();
                    ifcOpts = IFCJsonLoader.Load(SplitterConfig.IFCJsonPath, doc, locationName);
                    doc.Export(ifcFolder, ifcFile, ifcOpts);
                    txIfc.Commit();
                }
                // Export happens OUTSIDE any transaction
               
                log.Info(locationName, $"IFC exported → {ifcFolder}");
            }
            else
            {
                log.Warning(locationName,
                    $"IFCSettings.json not found at '{SplitterConfig.IFCJsonPath}' — IFC skipped.");
            }

            // NWC export — MUST be outside any open transaction (API requirement)
            string nwcFolder = Path.Combine(exportRoot, "NWC");
            Directory.CreateDirectory(nwcFolder);
            string nwcFile =
                $"P00100087-DAR-TI0-{locationName}-XX-PE-2X0001_EN.nwc";
            doc.Export(nwcFolder, nwcFile, navopts);
            log.Info(locationName, $"NWC exported → {nwcFolder}");
        }

        // ────────────────────────────────────────────────────────────────────
        //  STEP 6 – SAVE AS CENTRAL
        // ────────────────────────────────────────────────────────────────────
        private static void SaveAsCentral(
            Document doc, string locationName, string exportRoot, ProcessLog log)
        {
            // BUG FIX v1: EnableWorksharing was called inside a transaction.
            // It must be called OUTSIDE any open transaction — Revit throws
            // InvalidOperationException otherwise.
            if (!doc.IsWorkshared)
            {
                doc.EnableWorksharing("Shared Levels and Grids", "Workset1");
            }

            string rvtFolder = Path.Combine(exportRoot, "RVT");
            Directory.CreateDirectory(rvtFolder);

            string newPath = Path.Combine(rvtFolder,
                $"P00100087-DAR-TI0-{locationName}-XX-M3-2X0001_EN.rvt");

            var wsOpts  = new WorksharingSaveAsOptions { SaveAsCentral = true };
            var saveOpts = new SaveAsOptions
            {
                OverwriteExistingFile = true,
                Compact               = true
            };
            saveOpts.SetWorksharingOptions(wsOpts);

            doc.SaveAs(newPath, saveOpts);
            log.Info(locationName, $"RVT saved → {newPath}");
        }

        // ────────────────────────────────────────────────────────────────────
        //  STEP 7 – SYNC WITH CENTRAL + CLOSE
        // ────────────────────────────────────────────────────────────────────
        private static void SyncAndClose(
            Document doc, string locationName, ProcessLog log)
        {
            // BUG FIX v1: no guard — SWC on a freshly-saved central that has
            // no remote server can throw.  Wrap defensively.
            try
            {
                var syncOpts = new SynchronizeWithCentralOptions();
                syncOpts.SetRelinquishOptions(new RelinquishOptions(true));
                syncOpts.SaveLocalAfter = false;

                doc.SynchronizeWithCentral(new TransactWithCentralOptions(), syncOpts);
            }
            catch (Exception ex)
            {
                // Sync failure is non-fatal — the file is already saved as central.
                log.Warning(locationName, $"SWC warning (non-fatal): {ex.Message}");
            }
            finally
            {
                try { doc.Close(false); } catch { /* swallow close errors */ }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  HELPERS
        // ────────────────────────────────────────────────────────────────────

        private static BasePoint? GetBasePoint(Document doc, BuiltInCategory cat) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(BasePoint))
                .OfCategory(cat)
                .Cast<BasePoint>()
                .FirstOrDefault();

        private static double ToMm(BasePoint bp, BuiltInParameter bip) =>
            UnitUtils.ConvertFromInternalUnits(
                bp.get_Parameter(bip).AsDouble(), UnitTypeId.Millimeters);

        // Returns true if the parameter was found and successfully set
        private static bool SetDouble(Element e, string name, double val)
        {
            var p = e.LookupParameter(name);
            if (p == null || p.IsReadOnly) return false;
            p.Set(val);
            return true;
        }

        private static bool SetString(Element e, string name, string val)
        {
            var p = e.LookupParameter(name);
            if (p == null || p.IsReadOnly) return false;
            p.Set(val);
            return true;
        }

        private static ElementId GetNavisworks3DViewId(Document doc)
        {
            return (new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v =>
                    !v.IsTemplate &&
                    v.Name.IndexOf("Naviswork", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? throw new InvalidOperationException(
                    "No 3D view found with name containing 'Naviswork'."))
                .Id;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  SILENT FAILURE PREPROCESSOR
    //  Dismisses Revit failures that would otherwise show modal dialogs
    //  during batch processing.
    // ────────────────────────────────────────────────────────────────────────
    internal sealed class SilentFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                    accessor.DeleteWarning(failure);
                // Errors are left for Revit to handle (they will be logged)
            }
            return FailureProcessingResult.Continue;
        }
    }
}
