// ════════════════════════════════════════════════════════════════════════════
//  LocationExporterCommand.cs – Decoupled, Multi-Trade, Granular Options (v6)
//  Revit 2024 | RevitLocationExporter.Commands
//
//  - Fully configurable for ALL trades (TI0, EL0, ME0, PL0, AR0, ST0, etc.)
//  - Granular operation checkboxes: Parameter filling, 3D Exports, 2D Sheets, Purge
//  - Native PDF & DWG sheet exports
//  - Trade profiles and project settings externalized to TradeConfigManager
// ════════════════════════════════════════════════════════════════════════════

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using RevitLocationExporter.Configuration;
using RevitLocationExporter.Core;
using RevitLocationExporter.Form;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace RevitLocationExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LocationExporterCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var log = new ProcessLog();

            // ── 1. Select RVT files ────────────────────────────────────────────
            var selectedPaths = SelectRvtFiles();
            if (selectedPaths.Count == 0) return Result.Cancelled;

            // ── 2. Select Excel or CSV (Approach C2: 2 tabs) ───────────────────
            LocationParameterBundle? paramBundle = null;
            string? paramFilePath = SelectParameterFile();

            if (!string.IsNullOrEmpty(paramFilePath))
            {
                try
                {
                    paramBundle = LocationDataSheetReader.ReadParameterBundle(paramFilePath!);
                    if (!paramBundle.HasData)
                    {
                        TaskDialog.Show("Warning",
                            "The selected parameter file contained no data rows — falling back to config.");
                    }
                    else
                    {
                        var infoParts = new List<string>();
                        if (paramBundle.ElementParameters.Count > 0)
                            infoParts.Add($"{paramBundle.ElementParameters.Count} element location(s)");
                        if (paramBundle.SheetParameters.Count > 0)
                            infoParts.Add($"{paramBundle.SheetParameters.Count} sheet location(s)");
                        if (paramBundle.ExportLocations.Count > 0)
                            infoParts.Add($"{paramBundle.ExportLocations.Count} export destination(s)");

                        log.Info("Config", $"Loaded parameter workbook: {string.Join(", ", infoParts)}.");
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Parameter File Error",
                        $"Failed to read parameter file:\n{ex.Message}\n\nFalling back to built-in config.");
                }
            }

            var app = commandData.Application.Application;
            var configMgr = TradeConfigManager.Instance;
            var settings = configMgr.Settings;

            // ── 3. Discovery pass: read all project locations and worksets without full open ─
            var fileLocationMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var discoveredWorksetNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var discoveryErrors = new List<string>();

            foreach (string filePath in selectedPaths)
            {
                string fileName = Path.GetFileName(filePath);
                var rvtPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

                try
                {
                    List<string> names = ReadProjectLocationNames(app, rvtPath);

                    if (names.Count == 0)
                    {
                        discoveryErrors.Add($"{fileName}: No project locations found — file will be skipped.");
                    }
                    else
                    {
                        fileLocationMap[filePath] = names;

                        try
                        {
                            IList<WorksetPreview> wsList = WorksharingUtils.GetUserWorksetInfo(rvtPath);
                            foreach (var ws in wsList)
                            {
                                if (!string.IsNullOrWhiteSpace(ws.Name))
                                    discoveredWorksetNames.Add(ws.Name);
                            }
                        }
                        catch
                        {
                            // Non-workshared file or preview unavailable
                        }
                    }
                }
                catch (Exception ex)
                {
                    discoveryErrors.Add($"{fileName}: Could not read locations — {ex.Message}");
                }
            }

            if (discoveryErrors.Count > 0)
                TaskDialog.Show("Discovery Warnings", string.Join("\n", discoveryErrors));

            if (fileLocationMap.Count == 0)
            {
                TaskDialog.Show("Nothing to Process",
                    "No project locations were found in any of the selected files.");
                return Result.Cancelled;
            }

            // ── 4. Show location & options selection dialog ──────────────────
            var selDlg = new LocationSelectionDialog(fileLocationMap, discoveredWorksetNames);
            if (selDlg.ShowDialog() != true) return Result.Cancelled;

            var selectedLocations = selDlg.SelectedLocations;
            var worksetOptions = selDlg.WorksetOptions;
            var options = selDlg.Options;

            if (selectedLocations.Count == 0)
            {
                TaskDialog.Show("Cancelled", "No locations were selected.");
                return Result.Cancelled;
            }

            // ── 5. Process selected locations per file ────────────────────────
            var updater = new ProjectInfoUpdater();

            try
            {
                foreach (var fileKv in selectedLocations)
                {
                    string filePath = fileKv.Key;
                    IReadOnlyList<string> locationsToProcess = fileKv.Value;
                    if (locationsToProcess.Count == 0) continue;

                    string fileName = Path.GetFileName(filePath);
                    var rvtPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

                    // Dynamically resolve Trade for this file (e.g. TI0, EL0, ME0, PL0, AR0, ST0)
                    TradeProfile trade = selDlg.SelectedTradeOverride ?? configMgr.DetectTrade(fileName);
                    log.Info(fileName, $"Identified trade: {trade.TradeName} ({trade.TradeCode})");

                    foreach (string locationName in locationsToProcess)
                    {
                        // Resolve parameter sets for this location (Element params vs Sheet params)
                        Dictionary<string, string>? elementParams = null;
                        Dictionary<string, string>? sheetParams = null;

                        if (paramBundle != null)
                        {
                            elementParams = paramBundle.GetElementParams(locationName);
                            sheetParams = paramBundle.GetSheetParams(locationName);

                            if (elementParams == null && sheetParams == null)
                                log.Warning(locationName,
                                    "No spreadsheet row matched this location in element or sheet tabs — using built-in config.");
                        }

                        string? customExportFolder = paramBundle?.GetExportLocation(locationName);

                        try
                        {
                            ProcessOneLocation(app, rvtPath, locationName, elementParams, sheetParams, customExportFolder, worksetOptions, log, updater, trade, settings, options);
                            log.Info(locationName, "All requested steps completed.");
                        }
                        catch (Exception ex)
                        {
                            log.Error(locationName, $"[{fileName}] {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Unexpected Error", $"An unexpected error occurred:\n{ex.Message}");
                return Result.Failed;
            }

            log.ShowSummary();
            return log.HasErrors ? Result.Failed : Result.Succeeded;
        }

        // ════════════════════════════════════════════════════════════════════
        //  LOCATION PIPELINE (OPTION-AWARE)
        // ════════════════════════════════════════════════════════════════════

        private static void ProcessOneLocation(
            Autodesk.Revit.ApplicationServices.Application app,
            ModelPath rvtPath,
            string locationName,
            Dictionary<string, string>? elementParams,
            Dictionary<string, string>? sheetParams,
            string? customExportFolder,
            WorksetSelectionOptions worksetOptions,
            ProcessLog log,
            ProjectInfoUpdater updater,
            TradeProfile trade,
            ProjectSettings settings,
            ExportExecutionOptions options)
        {
            bool needsExportFolder = options.SaveCentralRvt || options.ExportNwc || options.ExportIfc || options.ExportPdf || options.ExportDwg;
            string exportRoot = string.Empty;

            if (!string.IsNullOrWhiteSpace(customExportFolder))
            {
                exportRoot = customExportFolder!.Trim();
                log.Info(locationName, $"Export folder loaded from spreadsheet: '{exportRoot}'");
            }
            else if (needsExportFolder && !settings.TryGetOutputFolder(locationName, trade, out exportRoot))
            {
                throw new InvalidOperationException($"No export folder mapping for location '{locationName}' under trade '{trade.TradeName}'.");
            }

            if (!string.IsNullOrEmpty(exportRoot))
            {
                log.Info(locationName, $"Export destination folder: {exportRoot}");
            }

            // Open worksets: configure based on global workset settings applied to all models
            var opts = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets,
                Audit = false
            };

            try
            {
                IList<WorksetPreview> allWorksets = WorksharingUtils.GetUserWorksetInfo(rvtPath);
                if (allWorksets.Count > 0)
                {
                    var wsConfig = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);

                    if (worksetOptions.OpenAllWorksets)
                    {
                        wsConfig.Open(allWorksets.Select(w => w.Id).ToList());
                        log.Info(trade.TradeCode, $"Opened all {allWorksets.Count} worksets.");
                    }
                    else
                    {
                        var idsToOpen = allWorksets
                            .Where(w => worksetOptions.ShouldOpenWorkset(w.Name, trade))
                            .Select(w => w.Id)
                            .ToList();

                        if (idsToOpen.Count > 0)
                        {
                            wsConfig.Open(idsToOpen);
                            log.Info(trade.TradeCode, $"Opened {idsToOpen.Count} of {allWorksets.Count} worksets (global config).");
                        }
                        else
                        {
                            log.Warning(trade.TradeCode, "No worksets matched global workset configuration — opening model with all user worksets closed.");
                        }
                    }

                    opts.SetOpenWorksetsConfiguration(wsConfig);
                }
            }
            catch (Exception ex)
            {
                log.Warning(trade.TradeCode, $"Could not configure worksets before opening: {ex.Message}");
            }

            using Document doc = app.OpenDocumentFile(rvtPath, opts);
            try
            {
                SetPinnedStateForReferenceElements(doc, false);

                // 1. Coordinates & Workset Renaming
                if (options.AlignCoordinatesAndWorksets)
                {
                    ActivateProjectLocation(doc, locationName, log, settings);
                    RenameWorksets(doc, locationName, log, trade);
                }

                // 2. Parameter Population
                if (options.FillParameters)
                {
                    FillSheetCoverParameters(doc, locationName, log);
                    FillCpkParameters(doc, locationName, elementParams, log, trade, settings);
                    UpdateProjectInfo(doc, updater, settings.ProjectData, locationName, sheetParams, trade, settings);
                }

                // Update template '_PL' sheets (Sheet Number, Work Package, Titleblock Checkboxes & Parameters)
                List<ViewSheet> plSheets = new();
                if (options.FillParameters || options.ExportPdf || options.ExportDwg || options.SaveCentralRvt)
                {
                    plSheets = UpdatePlSheets(doc, locationName, sheetParams, log);
                }

                // 3. Purge Unused
                if (options.PurgeUnused)
                {
                    PurgeUnused(doc, locationName, log);
                }

                // 4. 3D Model Exports (NWC, IFC)
                if (options.ExportNwc || options.ExportIfc)
                {
                    Run3DExports(doc, locationName, exportRoot, log, trade, settings, options);
                }

                // 5. 2D Sheet Exports (PDF, DWG)
                if (options.ExportPdf || options.ExportDwg)
                {
                    RunSheetExports(doc, locationName, exportRoot, log, options, plSheets);
                }

                SetPinnedStateForReferenceElements(doc, true);

                // 6. Save Central RVT Model
                if (options.SaveCentralRvt)
                {
                    SaveAsCentral(doc, locationName, exportRoot, log, trade, settings);
                }

                doc.Close(false);
            }
            catch
            {
                try { doc.Close(false); } catch { }
                throw;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 1 – ACTIVATE PROJECT LOCATION & SURVEY POINT
        // ════════════════════════════════════════════════════════════════════

        private static void ActivateProjectLocation(
            Document doc,
            string locationName,
            ProcessLog log,
            ProjectSettings settings)
        {
            using var tx = new Transaction(doc, "Activate Project Location");
            var failOpts = tx.GetFailureHandlingOptions();
            failOpts.SetFailuresPreprocessor(new SilentFailurePreprocessor());
            tx.SetFailureHandlingOptions(failOpts);
            tx.Start();

            var location = doc.ProjectLocations
                .Cast<ProjectLocation>()
                .FirstOrDefault(pl => pl.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Project Location not found: '{locationName}'");

            doc.ActiveProjectLocation = location;
            SetSurveyPoint(doc, settings);

            tx.Commit();
        }

        private static void SetSurveyPoint(Document doc, ProjectSettings settings)
        {
            var sp = GetBasePoint(doc, BuiltInCategory.OST_SharedBasePoint)
                ?? throw new InvalidOperationException("Survey Point element not found.");

            foreach (Parameter p in sp.Parameters)
            {
                if (p.Definition?.Name.IndexOf("Clipped", StringComparison.OrdinalIgnoreCase) >= 0
                    && p.StorageType == StorageType.Integer)
                {
                    p.Set(0);
                    break;
                }
            }
            try { sp.Clipped = false; } catch { }

            sp.get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM)
              .Set(UnitUtils.ConvertToInternalUnits(settings.SurveyEastMm, UnitTypeId.Millimeters));
            sp.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM)
              .Set(UnitUtils.ConvertToInternalUnits(settings.SurveyNorthMm, UnitTypeId.Millimeters));
            sp.get_Parameter(BuiltInParameter.BASEPOINT_ELEVATION_PARAM)
              .Set(UnitUtils.ConvertToInternalUnits(settings.SurveyElevMm, UnitTypeId.Millimeters));
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 2 – RENAME WORKSETS (TRADE-AWARE)
        // ════════════════════════════════════════════════════════════════════

        private static void RenameWorksets(
            Document doc,
            string locationName,
            ProcessLog log,
            TradeProfile trade)
        {
            var worksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .Where(ws => ws.IsEditable)
                .ToList();

            using var tx = new Transaction(doc, "Rename Worksets");
            tx.Start();

            foreach (var ws in worksets)
            {
                foreach (string suffix in trade.WorksetSuffixes)
                {
                    if (!ws.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string newName = locationName + suffix;
                    if (ws.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                        break;

                    bool nameTaken = worksets.Any(other =>
                        other.Id != ws.Id &&
                        other.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));

                    if (nameTaken)
                    {
                        log.Warning(locationName, $"Workset rename skipped: '{newName}' already exists.");
                        break;
                    }

                    WorksetTable.RenameWorkset(doc, ws.Id, newName);
                    break;
                }
            }

            tx.Commit();
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 3 – SHEET COVER PARAMETERS
        // ════════════════════════════════════════════════════════════════════

        private static void FillSheetCoverParameters(Document doc, string locationName, ProcessLog log)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.Name.IndexOf("Sheet Cover", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (sheets.Count == 0)
            {
                log.Warning(locationName, "No 'Sheet Cover' sheet found — coordinate params skipped.");
                return;
            }

            IList<ElementId> allRevisionIds = Revision.GetAllRevisionIds(doc);
            if (allRevisionIds.Count > 0 && sheets.Count > 0)
            {
                using var txRev = new Transaction(doc, "Add All Revisions to Sheets");
                txRev.Start();
                foreach (ViewSheet sheet in sheets)
                {
                    ICollection<ElementId> current = sheet.GetAdditionalRevisionIds();
                    foreach (ElementId revId in allRevisionIds)
                        if (!current.Contains(revId))
                            current.Add(revId);
                    sheet.SetAdditionalRevisionIds(current);
                }
                txRev.Commit();
            }

            using var tx = new Transaction(doc, "Fill Sheet Cover Coordinates");
            tx.Start();

            var pbp = GetBasePoint(doc, BuiltInCategory.OST_ProjectBasePoint);
            var svp = GetBasePoint(doc, BuiltInCategory.OST_SharedBasePoint);

            if (pbp == null || svp == null)
            {
                log.Warning(locationName, "Base Point or Survey Point not found — sheet cover params skipped.");
                tx.RollBack();
                return;
            }

            double pbpN = ToMm(pbp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
            double pbpE = ToMm(pbp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
            double pbpZ = ToMm(pbp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM);
            double svpN = ToMm(svp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
            double svpE = ToMm(svp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
            double svpZ = ToMm(svp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM);

            foreach (var sheet in sheets)
            {
                SetDouble(sheet, "DAR_PBP_N", pbpN);
                SetDouble(sheet, "DAR_PBP_E", pbpE);
                SetDouble(sheet, "DAR_PBP_Z", pbpZ);
                SetDouble(sheet, "DAR_SVP_N", svpN);
                SetDouble(sheet, "DAR_SVP_E", svpE);
                SetDouble(sheet, "DAR_SVP_Z", svpZ);
                SetString(sheet, "CPK_Building Location", locationName);
            }

            tx.Commit();
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 4 – PROJECT INFO & CPK PARAMETERS (TRADE-AWARE)
        // ════════════════════════════════════════════════════════════════════

        public static void UpdateProjectInfo(
            Document doc,
            ProjectInfoUpdater updater,
            Dictionary<string, string> projectData,
            string locationName,
            Dictionary<string, string>? paramSet,
            TradeProfile trade,
            ProjectSettings settings)
        {
            string buildingName = string.Empty;
            string workPackage = string.Empty;

            if (paramSet != null)
            {
                if (paramSet.TryGetValue("Building Name", out var bName) ||
                    paramSet.TryGetValue("BuildingName", out bName))
                {
                    buildingName = bName;
                }

                if (paramSet.TryGetValue("Work_Package", out var wp) ||
                    paramSet.TryGetValue("WorkPackage", out wp) ||
                    paramSet.TryGetValue("CPK_WorkPackage", out wp) ||
                    paramSet.TryGetValue("WP", out wp))
                {
                    workPackage = wp;
                }
            }

            if (string.IsNullOrEmpty(buildingName))
            {
                try { buildingName = LocatorLookup.GetName(locationName); } catch { }
            }

            string rvtFileName = trade.FormatFileName(trade.RvtFileNameTemplate, settings.ProjectNumber, settings.Originator, locationName);

            var dynamic = new List<(string, object)>
            {
                ("Building Name",   buildingName),
                ("Building_Code",   locationName),
                ("CPK_WorkPackage", workPackage),
                ("Work_Package",    workPackage),
                ("File_Name",       rvtFileName)
            };
            updater.UpdateProjectInformation(doc, projectData, dynamic);
        }

        private static void FillCpkParameters(
            Document doc,
            string locationName,
            Dictionary<string, string>? csvParamSet,
            ProcessLog log,
            TradeProfile trade,
            ProjectSettings settings)
        {
            var categoryFilter = trade.GetCategoryFilter();
            if (categoryFilter == null)
            {
                log.Warning(locationName, $"No target categories configured for trade '{trade.TradeName}' — CPK parameter update skipped.");
                return;
            }

            using var tx = new Transaction(doc, "Fill CPK Parameters");
            tx.Start();

            var groups = new Dictionary<ElementId, (ElementType Type, List<Element> Instances)>();
            var orphans = new List<Element>();

            foreach (Element e in new FilteredElementCollector(doc)
                                      .WhereElementIsNotElementType()
                                      .WherePasses(categoryFilter))
            {
                if (e.ViewSpecific) continue;

                ElementId tid = e.GetTypeId();
                if (tid == ElementId.InvalidElementId) { orphans.Add(e); continue; }

                if (groups.TryGetValue(tid, out var g)) { g.Instances.Add(e); continue; }

                if (doc.GetElement(tid) is ElementType t)
                    groups[tid] = (t, new List<Element> { e });
                else
                    orphans.Add(e);
            }

            if (groups.Count == 0 && orphans.Count == 0)
            {
                log.Info(locationName, $"No {trade.TradeName} device elements found — nothing to update.");
                tx.RollBack();
                return;
            }

            if (csvParamSet is { Count: > 0 })
            {
                int instWrites = 0, typeWrites = 0, failures = 0;

                foreach (var (type, instances) in groups.Values)
                {
                    Element representative = instances[0];
                    foreach (var kvp in csvParamSet)
                    {
                        var instParam = representative.LookupParameter(kvp.Key);
                        if (instParam != null && !instParam.IsReadOnly)
                        {
                            foreach (Element inst in instances)
                            {
                                var p = inst.LookupParameter(kvp.Key);
                                if (p == null || p.IsReadOnly) continue;
                                try { SetParamValue(p, kvp.Value); instWrites++; }
                                catch { failures++; }
                            }
                            continue;
                        }

                        var typeParam = type.LookupParameter(kvp.Key);
                        if (typeParam != null && !typeParam.IsReadOnly)
                        {
                            try { SetParamValue(typeParam, kvp.Value); typeWrites++; }
                            catch { failures++; }
                        }
                    }
                }

                foreach (Element orphan in orphans)
                {
                    foreach (var kvp in csvParamSet)
                    {
                        var p = orphan.LookupParameter(kvp.Key);
                        if (p == null || p.IsReadOnly) continue;
                        try { SetParamValue(p, kvp.Value); instWrites++; }
                        catch { failures++; }
                    }
                }

                if (failures > 0)
                    log.Warning(locationName, $"{failures} value(s) failed to convert and were skipped.");

                log.Info(locationName,
                    $"CPK params (CSV, {csvParamSet.Count} column(s)): {instWrites} instance value(s), {typeWrites} type value(s) written.");
            }
            else
            {
                if (!settings.TryGetCpkFallback(locationName, out string workPackage, out string wbsCode, out string wbsName))
                {
                    log.Warning(locationName, "No CPK config entry found — params not filled.");
                    tx.RollBack();
                    return;
                }

                int updated = 0;
                foreach (var (type, _) in groups.Values)
                {
                    if (SetString(type, "CPK_Work_Package", workPackage)) updated++;
                    SetString(type, "CPK_Stage", "Projekt Budowlany");
                }

                log.Info(locationName, $"CPK params (config fallback) updated on {updated} {trade.TradeName} element type(s).");
            }

            tx.Commit();
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 5 – PURGE UNUSED
        // ════════════════════════════════════════════════════════════════════

        private static void PurgeUnused(Document doc, string locationName, ProcessLog log)
        {
            int totalPurged = 0;
            try
            {
                using Transaction t = new Transaction(doc, "Purge Unused");
                t.Start();

                ISet<ElementId> unusedIds = doc.GetUnusedElements(new HashSet<ElementId>());
                int iterations = 0;

                while (unusedIds.Any())
                {
                    doc.Delete(unusedIds);
                    totalPurged += unusedIds.Count;
                    iterations++;
                    unusedIds = doc.GetUnusedElements(new HashSet<ElementId>());
                }

                t.Commit();
                log.Info(locationName, $"Purged {totalPurged} unused element(s) across {iterations} pass(es).");
            }
            catch (Exception ex)
            {
                log.Error(locationName, $"Purge failed: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 6 – 3D EXPORTS (NWC + IFC)
        // ════════════════════════════════════════════════════════════════════

        private static void Run3DExports(
            Document doc,
            string locationName,
            string exportRoot,
            ProcessLog log,
            TradeProfile trade,
            ProjectSettings settings,
            ExportExecutionOptions options)
        {
            Dictionary<string, ElementId> exportViewIds = CreateExportViews(doc, locationName, log);

            try
            {
                // ── IFC Export ─────────────────────────────────────────────
                if (options.ExportIfc)
                {
                    if (File.Exists(settings.IFCJsonPath))
                    {
                        string ifcFolder = Path.Combine(exportRoot, "IFC");
                        Directory.CreateDirectory(ifcFolder);

                        string ifcFileName = trade.FormatFileName(trade.IfcFileNameTemplate, settings.ProjectNumber, settings.Originator, locationName);
                        string ifcFullPath = Path.Combine(ifcFolder, ifcFileName + ".ifc");

                        IFCExportOptions ifcOpts;
                        using (var txIfc = new Transaction(doc, $"IFC Export Setup – {locationName}"))
                        {
                            txIfc.Start();
                            ifcOpts = IFCJsonLoader.Load(settings.IFCJsonPath, doc, locationName);

                            if (exportViewIds.TryGetValue("IFC", out ElementId ifcViewId) && ifcViewId != ElementId.InvalidElementId)
                                ifcOpts.FilterViewId = ifcViewId;

                            doc.Export(ifcFolder, ifcFileName, ifcOpts);
                            txIfc.Commit();
                        }
                        log.Info(locationName, $"IFC exported → {ifcFullPath}");
                    }
                    else
                    {
                        log.Warning(locationName, $"IFCSettings.json not found at '{settings.IFCJsonPath}' — IFC export skipped.");
                    }
                }

                // ── NWC Export ─────────────────────────────────────────────
                if (options.ExportNwc)
                {
                    if (!exportViewIds.TryGetValue("NWC", out ElementId nwcViewId) || nwcViewId == ElementId.InvalidElementId)
                        throw new InvalidOperationException($"[{locationName}] NWC export view could not be created — export aborted.");

                    string nwcFolder = Path.Combine(exportRoot, "NWC");
                    Directory.CreateDirectory(nwcFolder);

                    string nwcFileName = trade.FormatFileName(trade.NwcFileNameTemplate, settings.ProjectNumber, settings.Originator, locationName);
                    string nwcFullPath = Path.Combine(nwcFolder, nwcFileName + ".nwc");

                    var navOpts = new NavisworksExportOptions
                    {
                        ExportScope = NavisworksExportScope.View,
                        ViewId = nwcViewId,
                        ExportElementIds = true,
                        ConvertElementProperties = true,
                        ConvertLights = true,
                        ConvertLinkedCADFormats = false,
                        Coordinates = NavisworksCoordinates.Shared,
                        Parameters = NavisworksParameters.All,
                        ExportUrls = false,
                        DivideFileIntoLevels = true,
                        ExportLinks = false,
                        ExportParts = false,
                        ExportRoomAsAttribute = false,
                        ExportRoomGeometry = false,
                        FindMissingMaterials = true,
                        FacetingFactor = 1
                    };

                    doc.Export(nwcFolder, nwcFileName, navOpts);
                    log.Info(locationName, $"NWC exported → {nwcFullPath}");
                }
            }
            finally
            {
                DeleteExportViews(doc, locationName);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 6B – UPDATE TEMPLATE '_PL' SHEETS & TITLEBLOCKS
        // ════════════════════════════════════════════════════════════════════

        private static List<ViewSheet> UpdatePlSheets(
            Document doc,
            string locationName,
            Dictionary<string, string>? paramSet,
            ProcessLog log)
        {
            var titleParts = doc.Title.Split('-');
            if (titleParts.Length < 4)
            {
                log.Warning(locationName, $"Document title '{doc.Title}' does not follow hyphenated convention (expected >= 4 segments). Sheet processing skipped.");
                return new List<ViewSheet>();
            }

            var nativeModelLocator = titleParts[3].Trim();

            // Extract Work Package from spreadsheet if present
            string targetWp = string.Empty;
            if (paramSet != null)
            {
                if (paramSet.TryGetValue("Work_Package", out var wp) ||
                    paramSet.TryGetValue("WorkPackage", out wp) ||
                    paramSet.TryGetValue("CPK_WorkPackage", out wp) ||
                    paramSet.TryGetValue("WP", out wp))
                {
                    targetWp = wp?.Trim() ?? string.Empty;
                }
            }

            using var tx = new Transaction(doc, $"Update PL Sheets for {locationName}");
            tx.Start();

            // 1. Delete other sheets that are NOT nativeModelLocator BEFORE modifying anything
            var allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            var sheetsToDelete = new List<ViewSheet>();
            foreach (var s in allSheets)
            {
                // Preserve Sheet Cover
                if (s.Name.IndexOf("Sheet Cover", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                // Delete sheets containing "210001_EN"
                if (s.SheetNumber.IndexOf("210001_EN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Name.IndexOf("210001_EN", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sheetsToDelete.Add(s);
                    continue;
                }

                string[] parts = s.SheetNumber.Split('-');
                if (parts.Length >= 4 && !parts[3].Equals(nativeModelLocator, StringComparison.OrdinalIgnoreCase))
                {
                    sheetsToDelete.Add(s);
                }
            }

            if (sheetsToDelete.Count > 0)
            {
                int deletedCount = 0;
                foreach (var s in sheetsToDelete)
                {
                    try
                    {
                        doc.Delete(s.Id);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        log.Warning(locationName, $"Could not delete obsolete sheet '{s.SheetNumber}': {ex.Message}");
                    }
                }
                log.Info(locationName, $"Deleted {deletedCount} sheet(s) not matching native locator '{nativeModelLocator}' or containing '210001_EN'.");
            }

            // 2. Collect template '_PL' sheets matching nativeModelLocator to update
            var plSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && s.SheetNumber.IndexOf("_PL", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(s =>
                {
                    string[] parts = s.SheetNumber.Split('-');
                    return parts.Length >= 4 && parts[3].Equals(nativeModelLocator, StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (plSheets.Count == 0)
            {
                log.Warning(locationName, $"No template sheets containing '_PL' matching native locator '{nativeModelLocator}' were found in the model.");
                tx.Commit();
                return plSheets;
            }

            int renamedCount = 0;
            int tbUpdatedCount = 0;

            foreach (var sheet in plSheets)
            {
                try
                {
                    // 1. Rename Sheet Number: replace segment 3 (the locator code) with target locationName
                    string oldNum = sheet.SheetNumber;
                    string[] parts = oldNum.Split('-');
                    if (parts.Length >= 4)
                    {
                        parts[3] = locationName;
                        string newNum = string.Join("-", parts);
                        if (!string.Equals(oldNum, newNum, StringComparison.OrdinalIgnoreCase))
                        {
                            sheet.SheetNumber = newNum;
                            renamedCount++;
                        }
                    }
                    else
                    {
                        log.Warning(locationName, $"Sheet '{oldNum}' does not have standard hyphenated format (expected >= 4 segments).");
                    }

                    // 2. Update Sheet Name to Work Package if specified
                    if (!string.IsNullOrEmpty(targetWp) && !string.Equals(sheet.Name, targetWp, StringComparison.OrdinalIgnoreCase))
                    {
                        sheet.Name = targetWp;
                    }

                    // 3. Update Titleblock instances on this sheet
                    var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsNotElementType()
                        .ToList();

                    foreach (var tb in titleBlocks)
                    {
                        // 3a. Toggle Work Package Yes/No visibility checkboxes (e.g. WP04, WP05, WP11, etc.)
                        if (!string.IsNullOrEmpty(targetWp))
                        {
                            foreach (Parameter p in tb.Parameters)
                            {
                                if (p.IsReadOnly || p.StorageType != StorageType.Integer) continue;
                                string pName = p.Definition.Name.Trim();

                                if (System.Text.RegularExpressions.Regex.IsMatch(pName, @"^WP\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                                {
                                    int desiredVal = pName.Equals(targetWp, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                                    if (p.AsInteger() != desiredVal)
                                    {
                                        p.Set(desiredVal);
                                    }
                                }
                            }
                        }

                        // 3b. Map spreadsheet parameter values onto Titleblock
                        if (paramSet != null)
                        {
                            foreach (var kvp in paramSet)
                            {
                                var p = tb.LookupParameter(kvp.Key);
                                if (p != null && !p.IsReadOnly)
                                {
                                    SetParamValue(p, kvp.Value);
                                }
                            }
                        }

                        tbUpdatedCount++;
                    }

                    // 4. Map spreadsheet parameter values onto Sheet
                    if (paramSet != null)
                    {
                        foreach (var kvp in paramSet)
                        {
                            var p = sheet.LookupParameter(kvp.Key);
                            if (p != null && !p.IsReadOnly)
                            {
                                SetParamValue(p, kvp.Value);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Warning(locationName, $"Failed updating sheet '{sheet.SheetNumber}': {ex.Message}");
                }
            }

            tx.Commit();
            log.Info(locationName, $"Updated {plSheets.Count} '_PL' sheet(s) ({renamedCount} renamed, {tbUpdatedCount} titleblock(s) synced to WP '{targetWp}').");

            return plSheets;
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 7 – 2D SHEET EXPORTS (PDF + DWG)
        // ════════════════════════════════════════════════════════════════════

        private static void RunSheetExports(
            Document doc,
            string locationName,
            string exportRoot,
            ProcessLog log,
            ExportExecutionOptions options,
            List<ViewSheet>? plSheets = null)
        {
            var sheets = new List<ViewSheet>();
            if (plSheets != null && plSheets.Count > 0)
            {
                sheets.AddRange(plSheets);
            }

            // Include Sheet Cover sheets if present
            //var coverSheets = new FilteredElementCollector(doc)
            //    .OfClass(typeof(ViewSheet))
            //    .Cast<ViewSheet>()
            //    .Where(s => !s.IsPlaceholder && s.Name.IndexOf("Sheet Cover", StringComparison.OrdinalIgnoreCase) >= 0)
            //    .ToList();

            //foreach (var cs in coverSheets)
            //{
            //    if (!sheets.Any(s => s.Id == cs.Id))
            //    {
            //        sheets.Insert(0, cs);
            //    }
            //}

            if (sheets.Count == 0)
            {
                sheets = GetLocationSheets(doc, locationName);
            }

            if (sheets.Count == 0)
            {
                log.Warning(locationName, $"No sheets found matching location '{locationName}' — sheet exports skipped.");
                return;
            }

            // ── PDF Export ─────────────────────────────────────────────
            if (options.ExportPdf)
            {
                string pdfFolder = Path.Combine(exportRoot, "PDF");
                Directory.CreateDirectory(pdfFolder);

                try
                {
                    int exportedCount = 0;
                    foreach (var sheet in sheets)
                    {
                        string safeSheetNum = string.Join("_", sheet.SheetNumber.Split(Path.GetInvalidFileNameChars())).Trim();
                        if (string.IsNullOrEmpty(safeSheetNum))
                            safeSheetNum = "Sheet_" + sheet.Id.Value;

                        string targetPdfPath = Path.Combine(pdfFolder, safeSheetNum + ".pdf");
                        if (File.Exists(targetPdfPath))
                        {
                            try { File.Delete(targetPdfPath); } catch { }
                        }

                        var beforeFiles = new HashSet<string>(Directory.GetFiles(pdfFolder, "*.pdf"), StringComparer.OrdinalIgnoreCase);

                        var (paperFormat, orientation) = DetectPaperSize(sheet);

                        var pdfOpts = new PDFExportOptions
                        {
                            FileName = safeSheetNum,
                            Combine = true, // Combine=true with 1 view enforces exact FileName without Revit naming-rule prefixing
                            ColorDepth = ColorDepthType.Color,
                            ExportQuality = PDFExportQualityType.DPI300,
                            PaperFormat = paperFormat,
                            PaperOrientation = orientation,
                            PaperPlacement = PaperPlacementType.LowerLeft,
                            OriginOffsetX = 0.0,
                            OriginOffsetY = 0.0,
                            ZoomType = ZoomType.Zoom,
                            ZoomPercentage = 100,
                            HideUnreferencedViewTags = true,
                            HideReferencePlane = true,
                            HideCropBoundaries = true
                        };

                        doc.Export(pdfFolder, new List<ElementId> { sheet.Id }, pdfOpts);

                        // If Revit generated a file with a naming-rule name (e.g. Sheet-WP49.pdf), rename it to <safeSheetNum>.pdf
                        if (!File.Exists(targetPdfPath))
                        {
                            var afterFiles = Directory.GetFiles(pdfFolder, "*.pdf");
                            var newlyCreated = afterFiles.FirstOrDefault(f => !beforeFiles.Contains(f));
                            if (newlyCreated != null && File.Exists(newlyCreated))
                            {
                                try
                                {
                                    File.Move(newlyCreated, targetPdfPath);
                                }
                                catch (Exception exMove)
                                {
                                    log.Warning(locationName, $"Could not rename '{Path.GetFileName(newlyCreated)}' to '{safeSheetNum}.pdf': {exMove.Message}");
                                }
                            }
                        }

                        exportedCount++;
                        log.Info(locationName, $"Exported PDF: {safeSheetNum}.pdf [{paperFormat}, {orientation}, LowerLeft, 100%]");
                    }
                    log.Info(locationName, $"PDF export complete: {exportedCount} sheet(s) → {pdfFolder}");
                }
                catch (Exception ex)
                {
                    log.Error(locationName, $"PDF export failed: {ex.Message}");
                }
            }

            // ── DWG Export ─────────────────────────────────────────────
            if (options.ExportDwg)
            {
                string dwgFolder = Path.Combine(exportRoot, "DWG");
                Directory.CreateDirectory(dwgFolder);

                try
                {
                    var dwgOpts = new DWGExportOptions
                    {
                        ACAPreference = ACAObjectPreference.Geometry,
                        ExportOfSolids = SolidGeometry.Polymesh,
                        MergedViews = true,
                        FileVersion = ACADVersion.R2018,
                        LayerMapping = "AIA",
                        TargetUnit = ExportUnit.Millimeter,
                        HideUnreferenceViewTags = true,
                        HideReferencePlane = true
                    };

                    int exportedDwgCount = 0;
                    foreach (var sheet in sheets)
                    {
                        string safeSheetNum = string.Join("_", sheet.SheetNumber.Split(Path.GetInvalidFileNameChars())).Trim();
                        if (string.IsNullOrEmpty(safeSheetNum))
                            safeSheetNum = "Sheet_" + sheet.Id.Value;

                        doc.Export(dwgFolder, safeSheetNum, new List<ElementId> { sheet.Id }, dwgOpts);
                        exportedDwgCount++;
                    }
                    log.Info(locationName, $"DWG export complete: {exportedDwgCount} sheet(s) → {dwgFolder}");
                }
                catch (Exception ex)
                {
                    log.Error(locationName, $"DWG export failed: {ex.Message}");
                }
            }
        }

        private static List<ViewSheet> GetLocationSheets(Document doc, string locationName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .Where(s => s.SheetNumber.IndexOf(locationName, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Detects standard paper format and orientation from the sheet's Titleblock instance.
        /// Falls back to sheet outline or titleblock name hints.
        /// </summary>
        private static (ExportPaperFormat format, PageOrientationType orientation) DetectPaperSize(ViewSheet sheet)
        {
            Document doc = sheet.Document;

            // 1. Try to find Titleblock instance on sheet
            var titleBlock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            double widthMm = 0;
            double heightMm = 0;

            if (titleBlock != null)
            {
                var pW = titleBlock.get_Parameter(BuiltInParameter.SHEET_WIDTH);
                var pH = titleBlock.get_Parameter(BuiltInParameter.SHEET_HEIGHT);

                if (pW != null && pH != null)
                {
                    widthMm = UnitUtils.ConvertFromInternalUnits(pW.AsDouble(), UnitTypeId.Millimeters);
                    heightMm = UnitUtils.ConvertFromInternalUnits(pH.AsDouble(), UnitTypeId.Millimeters);
                }

                if (widthMm <= 1 || heightMm <= 1)
                {
                    var bbox = titleBlock.get_BoundingBox(sheet);
                    if (bbox != null)
                    {
                        widthMm = UnitUtils.ConvertFromInternalUnits(bbox.Max.X - bbox.Min.X, UnitTypeId.Millimeters);
                        heightMm = UnitUtils.ConvertFromInternalUnits(bbox.Max.Y - bbox.Min.Y, UnitTypeId.Millimeters);
                    }
                }
            }

            if (widthMm <= 1 || heightMm <= 1)
            {
                var outline = sheet.Outline;
                if (outline != null)
                {
                    widthMm = UnitUtils.ConvertFromInternalUnits(outline.Max.U - outline.Min.U, UnitTypeId.Millimeters);
                    heightMm = UnitUtils.ConvertFromInternalUnits(outline.Max.V - outline.Min.V, UnitTypeId.Millimeters);
                }
            }

            bool isLandscape = widthMm >= heightMm;
            var orientation = isLandscape ? PageOrientationType.Landscape : PageOrientationType.Portrait;

            double longSide = Math.Max(widthMm, heightMm);
            double shortSide = Math.Min(widthMm, heightMm);

            bool Matches(double targetLong, double targetShort) =>
                Math.Abs(longSide - targetLong) <= 30 && Math.Abs(shortSide - targetShort) <= 30;

            // ISO Formats
            if (Matches(1189, 841)) return (ExportPaperFormat.ISO_A0, orientation);
            if (Matches(841, 594))  return (ExportPaperFormat.ISO_A1, orientation);
            if (Matches(594, 420))  return (ExportPaperFormat.ISO_A2, orientation);
            if (Matches(420, 297))  return (ExportPaperFormat.ISO_A3, orientation);
            if (Matches(297, 210))  return (ExportPaperFormat.ISO_A4, orientation);

            if (Matches(1000, 707)) return (ExportPaperFormat.ISO_B1, orientation);
            if (Matches(707, 500))  return (ExportPaperFormat.ISO_B2, orientation);
            if (Matches(500, 353))  return (ExportPaperFormat.ISO_B3, orientation);
            if (Matches(353, 250))  return (ExportPaperFormat.ISO_B4, orientation);

            // Architectural Formats
            if (Matches(1219, 914)) return (ExportPaperFormat.ARCH_E, orientation);
            if (Matches(1067, 762)) return (ExportPaperFormat.ARCH_E1, orientation);
            if (Matches(914, 610))  return (ExportPaperFormat.ARCH_D, orientation);
            if (Matches(610, 457))  return (ExportPaperFormat.ARCH_C, orientation);
            if (Matches(457, 305))  return (ExportPaperFormat.ARCH_B, orientation);
            if (Matches(305, 229))  return (ExportPaperFormat.ARCH_A, orientation);

            // ANSI Formats
            if (Matches(1118, 864)) return (ExportPaperFormat.ANSI_E, orientation);
            if (Matches(864, 559))  return (ExportPaperFormat.ANSI_D, orientation);
            if (Matches(559, 432))  return (ExportPaperFormat.ANSI_C, orientation);
            if (Matches(432, 279))  return (ExportPaperFormat.ANSI_B, orientation);
            if (Matches(279, 216))  return (ExportPaperFormat.ANSI_A, orientation);

            // Name hints
            if (titleBlock != null)
            {
                string name = titleBlock.Name + " " + (doc.GetElement(titleBlock.GetTypeId())?.Name ?? "");
                if (name.IndexOf("A0", StringComparison.OrdinalIgnoreCase) >= 0) return (ExportPaperFormat.ISO_A0, orientation);
                if (name.IndexOf("A1", StringComparison.OrdinalIgnoreCase) >= 0) return (ExportPaperFormat.ISO_A1, orientation);
                if (name.IndexOf("A2", StringComparison.OrdinalIgnoreCase) >= 0) return (ExportPaperFormat.ISO_A2, orientation);
                if (name.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0) return (ExportPaperFormat.ISO_A3, orientation);
                if (name.IndexOf("A4", StringComparison.OrdinalIgnoreCase) >= 0) return (ExportPaperFormat.ISO_A4, orientation);
            }

            return (ExportPaperFormat.Default, orientation);
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 8 – SAVE AS CENTRAL
        // ════════════════════════════════════════════════════════════════════

        private static void SaveAsCentral(
            Document doc,
            string locationName,
            string exportRoot,
            ProcessLog log,
            TradeProfile trade,
            ProjectSettings settings)
        {
            if (!doc.IsWorkshared)
            {
                try
                {
                    doc.EnableWorksharing("Shared Levels and Grids", "Workset1");
                }
                catch (Exception ex)
                {
                    log.Warning(locationName, $"EnableWorksharing failed (proceeding with SaveAs): {ex.Message}");
                }
            }

            string rvtFolder = Path.Combine(exportRoot, "RVT");
            Directory.CreateDirectory(rvtFolder);

            string rvtFileName = trade.FormatFileName(trade.RvtFileNameTemplate, settings.ProjectNumber, settings.Originator, locationName) + ".rvt";
            string newPath = Path.Combine(rvtFolder, rvtFileName);

            var saveOpts = new SaveAsOptions
            {
                OverwriteExistingFile = true,
                Compact = false
            };
            saveOpts.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });

            doc.SaveAs(newPath, saveOpts);
            log.Info(locationName, $"RVT saved → {newPath}");
        }

        // ════════════════════════════════════════════════════════════════════
        //  VIEW HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static Dictionary<string, ElementId> CreateExportViews(
            Document doc,
            string locationName,
            ProcessLog log)
        {
            var created = new Dictionary<string, ElementId>();

            var all3D = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v => !v.IsTemplate)
                .ToList();

            View3D? exactNwc = all3D.FirstOrDefault(v => v.Name.Equals("EXPORT-NWC", StringComparison.OrdinalIgnoreCase));
            View3D? exactIfc = all3D.FirstOrDefault(v => v.Name.Equals("EXPORT-IFC", StringComparison.OrdinalIgnoreCase));
            View3D? sourceView = all3D.FirstOrDefault(v => v.Name.IndexOf("Naviswork", StringComparison.OrdinalIgnoreCase) >= 0);

            if (sourceView != null)
            {
                using var tx = new Transaction(doc, "Create Export Views");
                tx.Start();

                if (exactNwc != null) { try { doc.Delete(exactNwc.Id); } catch { } }
                if (exactIfc != null) { try { doc.Delete(exactIfc.Id); } catch { } }

                try
                {
                    if (doc.GetElement(sourceView.Duplicate(ViewDuplicateOption.Duplicate)) is View3D nwcView)
                    {
                        nwcView.Name = "EXPORT-NWC";
                        created["NWC"] = nwcView.Id;
                    }
                }
                catch (Exception ex) { log.Warning(locationName, $"NWC view creation failed: {ex.Message}"); }

                try
                {
                    if (doc.GetElement(sourceView.Duplicate(ViewDuplicateOption.Duplicate)) is View3D ifcView)
                    {
                        ifcView.Name = "EXPORT-IFC";
                        created["IFC"] = ifcView.Id;
                    }
                }
                catch (Exception ex) { log.Warning(locationName, $"IFC view creation failed: {ex.Message}"); }

                tx.Commit();
                log.Info(locationName, $"Export views freshly duplicated from '{sourceView.Name}'.");
            }
            else
            {
                log.Warning(locationName, "No 'Naviswork' source view found. Searching for existing export views.");

                if (exactNwc != null)
                {
                    created["NWC"] = exactNwc.Id;
                    log.Info(locationName, "Using pre-existing exact view: 'EXPORT-NWC'.");
                }
                else
                {
                    View3D? fuzzyNwc = all3D.FirstOrDefault(v => v.Name.IndexOf("NWC", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (fuzzyNwc != null)
                    {
                        created["NWC"] = fuzzyNwc.Id;
                        log.Info(locationName, $"Using fuzzy match NWC view: '{fuzzyNwc.Name}'.");
                    }
                    else log.Warning(locationName, "No existing NWC view found.");
                }

                if (exactIfc != null)
                {
                    created["IFC"] = exactIfc.Id;
                    log.Info(locationName, "Using pre-existing exact view: 'EXPORT-IFC'.");
                }
                else
                {
                    View3D? fuzzyIfc = all3D.FirstOrDefault(v => v.Name.IndexOf("IFC", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (fuzzyIfc != null)
                    {
                        created["IFC"] = fuzzyIfc.Id;
                        log.Info(locationName, $"Using fuzzy match IFC view: '{fuzzyIfc.Name}'.");
                    }
                    else log.Warning(locationName, "No existing IFC view found.");
                }
            }

            return created;
        }

        private static void DeleteExportViews(Document doc, string locationName)
        {
            View3D sourceView = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v =>
                    !v.IsTemplate &&
                    v.Name.IndexOf("Naviswork", StringComparison.OrdinalIgnoreCase) >= 0);

            if (sourceView == null) return;

            try
            {
                using var tx = new Transaction(doc, "Delete Export Views");
                tx.Start();
                try { doc.Delete(sourceView.Id); } catch { }
                tx.Commit();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Cleanup Warning", $"[{locationName}] Could not delete export views: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  UI & FILE SELECTION HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static List<string> SelectRvtFiles()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Revit Files (*.rvt)|*.rvt",
                Title = "Select Revit Model(s)",
                Multiselect = true
            };
            return dlg.ShowDialog() == true ? dlg.FileNames.ToList() : new List<string>();
        }

        private static string? SelectParameterFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Location Sheet Parameters (Excel or CSV) - Cancel to skip",
                Filter = "Excel or CSV Files (*.xlsx;*.csv)|*.xlsx;*.csv|Excel Files (*.xlsx)|*.xlsx|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        internal static Dictionary<string, Dictionary<string, string>> ReadCsv(string csvPath)
        {
            return LocationDataSheetReader.ReadCsv(csvPath);
        }

        private static List<string> ReadProjectLocationNames(
            Autodesk.Revit.ApplicationServices.Application app,
            ModelPath rvtPath)
        {
            var opts = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
            };
            opts.SetOpenWorksetsConfiguration(
                new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets));

            using var doc = app.OpenDocumentFile(rvtPath, opts);
            return doc.ProjectLocations
                .Cast<ProjectLocation>()
                .Select(pl => pl.Name)
                .ToList();
        }

        public static void SetPinnedStateForReferenceElements(Document doc, bool pinned)
        {
            using (Transaction t = new Transaction(doc, pinned ? "Pin reference elements" : "Unpin reference elements"))
            {
                t.Start();

                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>();
                foreach (Level lvl in levels) if (lvl.Pinned != pinned) lvl.Pinned = pinned;

                var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>();
                foreach (Grid g in grids) if (g.Pinned != pinned) g.Pinned = pinned;

                var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>();
                foreach (RevitLinkInstance link in links) if (link.Pinned != pinned) link.Pinned = pinned;

                var basePoints = new FilteredElementCollector(doc).OfClass(typeof(BasePoint)).Cast<BasePoint>();
                foreach (BasePoint bp in basePoints) if (bp.Pinned != pinned) bp.Pinned = pinned;

                t.Commit();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  PARAMETER & UTILITY HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static BasePoint? GetBasePoint(Document doc, BuiltInCategory cat) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(BasePoint))
                .OfCategory(cat)
                .Cast<BasePoint>()
                .FirstOrDefault();

        private static double ToMm(BasePoint bp, BuiltInParameter bip) =>
            UnitUtils.ConvertFromInternalUnits(
                bp.get_Parameter(bip).AsDouble(), UnitTypeId.Millimeters);

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

        private static void SetParamValue(Parameter param, string raw)
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(raw);
                    break;
                case StorageType.Double:
                    if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
                        param.Set(d);
                    break;
                case StorageType.Integer:
                    if (int.TryParse(raw, out int i))
                        param.Set(i);
                    break;
            }
        }
    }
}
