//// ════════════════════════════════════════════════════════════════════════════
////  LocationExporterCommand.cs – Decoupled, Multi-Trade, Granular Options (v6)
////  Revit 2024 | RevitLocationExporter.Commands
////
////  - Fully configurable for ALL trades (TI0, EL0, ME0, PL0, AR0, ST0, etc.)
////  - Granular operation checkboxes: Parameter filling, 3D Exports, 2D Sheets, Purge
////  - Native PDF & DWG sheet exports
////  - Trade profiles and project settings externalized to TradeConfigManager
//// ════════════════════════════════════════════════════════════════════════════

//using Autodesk.Revit.Attributes;
//using Autodesk.Revit.DB;
//using Autodesk.Revit.UI;
//using Microsoft.Win32;
//using RevitLocationExporter.Configuration;
//using RevitLocationExporter.Core;
//using RevitLocationExporter.Form;
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Text;
//using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

//namespace RevitLocationExporter.Commands
//{
//    [Transaction(TransactionMode.Manual)]
//    [Regeneration(RegenerationOption.Manual)]
//    public class LocationExporterCommand : IExternalCommand
//    {
//        public Result Execute(
//            ExternalCommandData commandData,
//            ref string message,
//            ElementSet elements)
//        {
//            var log = new ProcessLog();

//            // ── 1. Select RVT files ────────────────────────────────────────────
//            var selectedPaths = SelectRvtFiles();
//            if (selectedPaths.Count == 0) return Result.Cancelled;

//            // ── 2. Select Excel or CSV (Approach C2: 2 tabs) ───────────────────
//            LocationParameterBundle? paramBundle = null;
//            string? paramFilePath = SelectParameterFile();

//            if (!string.IsNullOrEmpty(paramFilePath))
//            {
//                try
//                {
//                    paramBundle = LocationDataSheetReader.ReadParameterBundle(paramFilePath!);
//                    if (!paramBundle.HasData)
//                    {
//                        TaskDialog.Show("Warning",
//                            "The selected parameter file contained no data rows — falling back to config.");
//                    }
//                    else
//                    {
//                        var infoParts = new List<string>();
//                        if (paramBundle.ElementParameters.Count > 0)
//                            infoParts.Add($"{paramBundle.ElementParameters.Count} element location(s)");
//                        if (paramBundle.SheetParameters.Count > 0)
//                            infoParts.Add($"{paramBundle.SheetParameters.Count} sheet location(s)");
//                        if (paramBundle.ExportLocations.Count > 0)
//                            infoParts.Add($"{paramBundle.ExportLocations.Count} export destination(s)");

//                        log.Info("Config", $"Loaded parameter workbook: {string.Join(", ", infoParts)}.");
//                    }
//                }
//                catch (Exception ex)
//                {
//                    TaskDialog.Show("Parameter File Error",
//                        $"Failed to read parameter file:\n{ex.Message}\n\nFalling back to built-in config.");
//                }
//            }

//            var app = commandData.Application.Application;
//            var configMgr = TradeConfigManager.Instance;
//            var settings = configMgr.Settings;

//            // ── 3. Discovery pass: read all project locations and worksets without full open ─
//            var fileLocationMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
//            var discoveredWorksetNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
//            var discoveryErrors = new List<string>();

//            foreach (string filePath in selectedPaths)
//            {
//                string fileName = Path.GetFileName(filePath);
//                var rvtPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

//                try
//                {
//                    List<string> names = ReadProjectLocationNames(app, rvtPath);

//                    if (names.Count == 0)
//                    {
//                        discoveryErrors.Add($"{fileName}: No project locations found — file will be skipped.");
//                    }
//                    else
//                    {
//                        fileLocationMap[filePath] = names;

//                        try
//                        {
//                            IList<WorksetPreview> wsList = WorksharingUtils.GetUserWorksetInfo(rvtPath);
//                            foreach (var ws in wsList)
//                            {
//                                if (!string.IsNullOrWhiteSpace(ws.Name))
//                                    discoveredWorksetNames.Add(ws.Name);
//                            }
//                        }
//                        catch
//                        {
//                            // Non-workshared file or preview unavailable
//                        }
//                    }
//                }
//                catch (Exception ex)
//                {
//                    discoveryErrors.Add($"{fileName}: Could not read locations — {ex.Message}");
//                }
//            }

//            if (discoveryErrors.Count > 0)
//                TaskDialog.Show("Discovery Warnings", string.Join("\n", discoveryErrors));

//            if (fileLocationMap.Count == 0)
//            {
//                TaskDialog.Show("Nothing to Process",
//                    "No project locations were found in any of the selected files.");
//                return Result.Cancelled;
//            }

//            // ── 4. Show location & options selection dialog ──────────────────
//            var selDlg = new LocationSelectionDialog(fileLocationMap, discoveredWorksetNames);
//            if (selDlg.ShowDialog() != true) return Result.Cancelled;

//            var selectedLocations = selDlg.SelectedLocations;
//            var worksetOptions = selDlg.WorksetOptions;
//            var options = selDlg.Options;

//            if (selectedLocations.Count == 0)
//            {
//                TaskDialog.Show("Cancelled", "No locations were selected.");
//                return Result.Cancelled;
//            }

//            // ── 5. Process selected locations per file ────────────────────────
//            var updater = new ProjectInfoUpdater();

//            try
//            {
//                foreach (var fileKv in selectedLocations)
//                {
//                    string filePath = fileKv.Key;
//                    IReadOnlyList<string> locationsToProcess = fileKv.Value;
//                    if (locationsToProcess.Count == 0) continue;

//                    string fileName = Path.GetFileName(filePath);
//                    var rvtPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

//                    // Dynamically resolve Trade for this file (e.g. TI0, EL0, ME0, PL0, AR0, ST0)
//                    TradeProfile trade = selDlg.SelectedTradeOverride ?? configMgr.DetectTrade(fileName);
//                    log.Info(fileName, $"Identified trade: {trade.TradeName} ({trade.TradeCode})");

//                    foreach (string locationName in locationsToProcess)
//                    {
//                        // Resolve parameter sets for this location (Element params vs Sheet params)
//                        Dictionary<string, string>? elementParams = null;
//                        Dictionary<string, string>? sheetParams = null;

//                        if (paramBundle != null)
//                        {
//                            elementParams = paramBundle.GetElementParams(locationName);
//                            sheetParams = paramBundle.GetSheetParams(locationName);

//                            if (elementParams == null && sheetParams == null)
//                                log.Warning(locationName,
//                                    "No spreadsheet row matched this location in element or sheet tabs — using built-in config.");
//                        }

//                        string? customExportFolder = paramBundle?.GetExportLocation(locationName);

//                        try
//                        {
//                            ProcessOneLocation(app, rvtPath, locationName, elementParams, sheetParams, customExportFolder, worksetOptions, log, updater, trade, settings, options);
//                            log.Info(locationName, "All requested steps completed.");
//                        }
//                        catch (Exception ex)
//                        {
//                            log.Error(locationName, $"[{fileName}] {ex.Message}");
//                        }
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                TaskDialog.Show("Unexpected Error", $"An unexpected error occurred:\n{ex.Message}");
//                return Result.Failed;
//            }

//            log.ShowSummary();
//            return log.HasErrors ? Result.Failed : Result.Succeeded;
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  LOCATION PIPELINE (OPTION-AWARE)
//        // ════════════════════════════════════════════════════════════════════

//        private static void ProcessOneLocation(
//            Autodesk.Revit.ApplicationServices.Application app,
//            ModelPath rvtPath,
//            string locationName,
//            Dictionary<string, string>? elementParams,
//            Dictionary<string, string>? sheetParams,
//            string? customExportFolder,
//            WorksetSelectionOptions worksetOptions,
//            ProcessLog log,
//            ProjectInfoUpdater updater,
//            TradeProfile trade,
//            ProjectSettings settings,
//            ExportExecutionOptions options)
//        {
//            bool needsExportFolder = options.SaveCentralRvt || options.ExportNwc || options.ExportIfc || options.ExportPdf || options.ExportDwg;
//            string exportRoot = string.Empty;

//            if (!string.IsNullOrWhiteSpace(customExportFolder))
//            {
//                exportRoot = customExportFolder!.Trim();
//                log.Info(locationName, $"Export folder loaded from spreadsheet: '{exportRoot}'");
//            }
//            else if (needsExportFolder && !settings.TryGetOutputFolder(locationName, trade, out exportRoot))
//            {
//                throw new InvalidOperationException($"No export folder mapping for location '{locationName}' under trade '{trade.TradeName}'.");
//            }

//            if (!string.IsNullOrEmpty(exportRoot))
//            {
//                log.Info(locationName, $"Export destination folder: {exportRoot}");
//            }

//            // Open worksets: configure based on global workset settings applied to all models
//            var opts = new OpenOptions
//            {
//                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets,
//                Audit = false
//            };

//            try
//            {
//                IList<WorksetPreview> allWorksets = WorksharingUtils.GetUserWorksetInfo(rvtPath);
//                if (allWorksets.Count > 0)
//                {
//                    var wsConfig = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);

//                    if (worksetOptions.OpenAllWorksets)
//                    {
//                        wsConfig.Open(allWorksets.Select(w => w.Id).ToList());
//                        log.Info(trade.TradeCode, $"Opened all {allWorksets.Count} worksets.");
//                    }
//                    else
//                    {
//                        var idsToOpen = allWorksets
//                            .Where(w => worksetOptions.ShouldOpenWorkset(w.Name, trade))
//                            .Select(w => w.Id)
//                            .ToList();

//                        if (idsToOpen.Count > 0)
//                        {
//                            wsConfig.Open(idsToOpen);
//                            log.Info(trade.TradeCode, $"Opened {idsToOpen.Count} of {allWorksets.Count} worksets (global config).");
//                        }
//                        else
//                        {
//                            log.Warning(trade.TradeCode, "No worksets matched global workset configuration — opening model with all user worksets closed.");
//                        }
//                    }

//                    opts.SetOpenWorksetsConfiguration(wsConfig);
//                }
//            }
//            catch (Exception ex)
//            {
//                log.Warning(trade.TradeCode, $"Could not configure worksets before opening: {ex.Message}");
//            }

//            using Document doc = app.OpenDocumentFile(rvtPath, opts);
//            try
//            {
//                SetPinnedStateForReferenceElements(doc, false);

//                // 1. Coordinates & Workset Renaming
//                if (options.AlignCoordinatesAndWorksets)
//                {
//                    ActivateProjectLocation(doc, locationName, log, settings);
//                    RenameWorksets(doc, locationName, log, trade);
//                }

//                // 2. Parameter Population
//                if (options.FillParameters)
//                {
//                    FillSheetCoverParameters(doc, locationName, log);
//                    FillCpkParameters(doc, locationName, elementParams, log, trade, settings);
//                    UpdateProjectInfo(doc, updater, settings.ProjectData, locationName, sheetParams, trade, settings);
//                }

//                // Update template '_PL' sheets (Sheet Number, Work Package, Titleblock Checkboxes & Parameters)
//                List<ViewSheet> plSheets = new();
//                if (options.FillParameters || options.ExportPdf || options.ExportDwg || options.SaveCentralRvt)
//                {
//                    plSheets = UpdatePlSheets(doc, locationName, sheetParams, log);
//                }

//                // 3. Purge Unused
//                if (options.PurgeUnused)
//                {
//                    PurgeUnused(doc, locationName, log);
//                }

//                // 4. 3D Model Exports (NWC, IFC)
//                if (options.ExportNwc || options.ExportIfc)
//                {
//                    Run3DExports(doc, locationName, exportRoot, log, trade, settings, options);
//                }

//                // 5. 2D Sheet Exports (PDF, DWG)
//                if (options.ExportPdf || options.ExportDwg)
//                {
//                    RunSheetExports(doc, locationName, exportRoot, log, options, plSheets);
//                }

//                SetPinnedStateForReferenceElements(doc, true);

//                // 6. Save Central RVT Model
//                if (options.SaveCentralRvt)
//                {
//                    SaveAsCentral(doc, locationName, exportRoot, log, trade, settings);
//                }

//                doc.Close(false);
//            }
//            catch
//            {
//                try { doc.Close(false); } catch { }
//                throw;
//            }
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  STEP 1 – ACTIVATE PROJECT LOCATION & SURVEY POINT
//        // ════════════════════════════════════════════════════════════════════

//        private static void ActivateProjectLocation(
//            Document doc,
//            string locationName,
//            ProcessLog log,
//            ProjectSettings settings)
//        {
//            using var tx = new Transaction(doc, "Activate Project Location");
//            var failOpts = tx.GetFailureHandlingOptions();
//            failOpts.SetFailuresPreprocessor(new SilentFailurePreprocessor());
//            tx.SetFailureHandlingOptions(failOpts);
//            tx.Start();

//            var location = doc.ProjectLocations
//                .Cast<ProjectLocation>()
//                .FirstOrDefault(pl => pl.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase))
//                ?? throw new InvalidOperationException($"Project Location not found: '{locationName}'");

//            doc.ActiveProjectLocation = location;
//            SetSurveyPoint(doc, settings);

//            tx.Commit();
//        }

//        private static void SetSurveyPoint(Document doc, ProjectSettings settings)
//        {
//            var sp = GetBasePoint(doc, BuiltInCategory.OST_SharedBasePoint)
//                ?? throw new InvalidOperationException("Survey Point element not found.");

//            foreach (Parameter p in sp.Parameters)
//            {
//                if (p.Definition?.Name.IndexOf("Clipped", StringComparison.OrdinalIgnoreCase) >= 0
//                    && p.StorageType == StorageType.Integer)
//                {
//                    p.Set(0);
//                    break;
//                }
//            }
//            try { sp.Clipped = false; } catch { }

//            sp.get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM)
//              .Set(UnitUtils.ConvertToInternalUnits(settings.SurveyEastMm, UnitTypeId.Millimeters));
//            sp.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM)
//              .Set(UnitUtils.ConvertToInternalUnits(settings.SurveyNorthMm, UnitTypeId.Millimeters));
//            sp.get_Parameter(BuiltInParameter.BASEPOINT_ELEVATION_PARAM)
//              .Set(UnitUtils.ConvertToInternalUnits(settings.SurveyElevMm, UnitTypeId.Millimeters));
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  STEP 2 – RENAME WORKSETS (TRADE-AWARE)
//        // ════════════════════════════════════════════════════════════════════

//        private static void RenameWorksets(
//            Document doc,
//            string locationName,
//            ProcessLog log,
//            TradeProfile trade)
//        {
//            var worksets = new FilteredWorksetCollector(doc)
//                .OfKind(WorksetKind.UserWorkset)
//                .ToWorksets()
//                .Where(ws => ws.IsEditable)
//                .ToList();

//            using var tx = new Transaction(doc, "Rename Worksets");
//            tx.Start();

//            foreach (var ws in worksets)
//            {
//                foreach (string suffix in trade.WorksetSuffixes)
//                {
//                    if (!ws.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
//                        continue;

//                    string newName = locationName + suffix;
//                    if (ws.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
//                        break;

//                    bool nameTaken = worksets.Any(other =>
//                        other.Id != ws.Id &&
//                        other.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));

//                    if (nameTaken)
//                    {
//                        log.Warning(locationName, $"Workset rename skipped: '{newName}' already exists.");
//                        break;
//                    }

//                    WorksetTable.RenameWorkset(doc, ws.Id, newName);
//                    break;
//                }
//            }

//            tx.Commit();
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  STEP 3 – SHEET COVER PARAMETERS
//        // ════════════════════════════════════════════════════════════════════

//        private static void FillSheetCoverParameters(Document doc, string locationName, ProcessLog log)
//        {
//            var sheets = new FilteredElementCollector(doc)
//                .OfClass(typeof(ViewSheet))
//                .Cast<ViewSheet>()
//                .Where(s => s.Name.IndexOf("Sheet Cover", StringComparison.OrdinalIgnoreCase) >= 0)
//                .ToList();

//            if (sheets.Count == 0)
//            {
//                log.Warning(locationName, "No 'Sheet Cover' sheet found — coordinate params skipped.");
//                return;
//            }

//            IList<ElementId> allRevisionIds = Revision.GetAllRevisionIds(doc);
//            if (allRevisionIds.Count > 0 && sheets.Count > 0)
//            {
//                using var txRev = new Transaction(doc, "Add All Revisions to Sheets");
//                txRev.Start();
//                foreach (ViewSheet sheet in sheets)
//                {
//                    ICollection<ElementId> current = sheet.GetAdditionalRevisionIds();
//                    foreach (ElementId revId in allRevisionIds)
//                        if (!current.Contains(revId))
//                            current.Add(revId);
//                    sheet.SetAdditionalRevisionIds(current);
//                }
//                txRev.Commit();
//            }

//            using var tx = new Transaction(doc, "Fill Sheet Cover Coordinates");
//            tx.Start();

//            var pbp = GetBasePoint(doc, BuiltInCategory.OST_ProjectBasePoint);
//            var svp = GetBasePoint(doc, BuiltInCategory.OST_SharedBasePoint);

//            if (pbp == null || svp == null)
//            {
//                log.Warning(locationName, "Base Point or Survey Point not found — sheet cover params skipped.");
//                tx.RollBack();
//                return;
//            }

//            double pbpN = ToMm(pbp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
//            double pbpE = ToMm(pbp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
//            double pbpZ = ToMm(pbp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM);
//            double svpN = ToMm(svp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
//            double svpE = ToMm(svp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
//            double svpZ = ToMm(svp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM);

//            foreach (var sheet in sheets)
//            {
//                SetDouble(sheet, "DAR_PBP_N", pbpN);
//                SetDouble(sheet, "DAR_PBP_E", pbpE);
//                SetDouble(sheet, "DAR_PBP_Z", pbpZ);
//                SetDouble(sheet, "DAR_SVP_N", svpN);
//                SetDouble(sheet, "DAR_SVP_E", svpE);
//                SetDouble(sheet, "DAR_SVP_Z", svpZ);
//                SetString(sheet, "CPK_Building Location", locationName);
//            }

//            tx.Commit();
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  STEP 4 – PROJECT INFO & CPK PARAMETERS (TRADE-AWARE)
//        // ════════════════════════════════════════════════════════════════════

//        public static void UpdateProjectInfo(
//            Document doc,
//            ProjectInfoUpdater updater,
//            Dictionary<string, string> projectData,
//            string locationName,
//            Dictionary<string, string>? paramSet,
//            TradeProfile trade,
//            ProjectSettings settings)
//        {
//            string buildingName = string.Empty;
//            string workPackage = string.Empty;

//            if (paramSet != null)
//            {
//                if (paramSet.TryGetValue("Building Name", out var bName) ||
//                    paramSet.TryGetValue("BuildingName", out bName))
//                {
//                    buildingName = bName;
//                }

//                if (paramSet.TryGetValue("Work_Package", out var wp) ||
//                    paramSet.TryGetValue("WorkPackage", out wp) ||
//                    paramSet.TryGetValue("CPK_WorkPackage", out wp) ||
//                    paramSet.TryGetValue("WP", out wp))
//                {
//                    workPackage = wp;
//                }
//            }

//            if (string.IsNullOrEmpty(buildingName))
//            {
//                try { buildingName = LocatorLookup.GetName(locationName); } catch { }
//            }

//            string rvtFileName = trade.FormatFileName(trade.RvtFileNameTemplate, settings.ProjectNumber, settings.Originator, locationName);

//            var dynamic = new List<(string, object)>
//            {
//                ("Building Name",   buildingName),
//                ("Building_Code",   locationName),
//                ("CPK_WorkPackage", workPackage),
//                ("Work_Package",    workPackage),
//                ("File_Name",       rvtFileName)
//            };
//            updater.UpdateProjectInformation(doc, projectData, dynamic);
//        }

//        private static void FillCpkParameters(
//            Document doc,
//            string locationName,
//            Dictionary<string, string>? csvParamSet,
//            ProcessLog log,
//            TradeProfile trade,
//            ProjectSettings settings)
//        {
//            var categoryFilter = trade.GetCategoryFilter();
//            if (categoryFilter == null)
//            {
//                log.Warning(locationName, $"No target categories configured for trade '{trade.TradeName}' — CPK parameter update skipped.");
//                return;
//            }

//            using var tx = new Transaction(doc, "Fill CPK Parameters");
//            tx.Start();

//            var groups = new Dictionary<ElementId, (ElementType Type, List<Element> Instances)>();
//            var orphans = new List<Element>();

//            foreach (Element e in new FilteredElementCollector(doc)
//                                      .WhereElementIsNotElementType()
//                                      .WherePasses(categoryFilter))
//            {
//                if (e.ViewSpecific) continue;

//                ElementId tid = e.GetTypeId();
//                if (tid == ElementId.InvalidElementId) { orphans.Add(e); continue; }

//                if (groups.TryGetValue(tid, out var g)) { g.Instances.Add(e); continue; }

//                if (doc.GetElement(tid) is ElementType t)
//                    groups[tid] = (t, new List<Element> { e });
//                else
//                    orphans.Add(e);
//            }

//            if (groups.Count == 0 && orphans.Count == 0)
//            {
//                log.Info(locationName, $"No {trade.TradeName} device elements found — nothing to update.");
//                tx.RollBack();
//                return;
//            }

//            if (csvParamSet is { Count: > 0 })
//            {
//                int instWrites = 0, typeWrites = 0, failures = 0;

//                foreach (var (type, instances) in groups.Values)
//                {
//                    Element representative = instances[0];
//                    foreach (var kvp in csvParamSet)
//                    {
//                        var instParam = representative.LookupParameter(kvp.Key);
//                        if (instParam != null && !instParam.IsReadOnly)
//                        {
//                            foreach (Element inst in instances)
//                            {
//                                var p = inst.LookupParameter(kvp.Key);
//                                if (p == null || p.IsReadOnly) continue;
//                                try { SetParamValue(p, kvp.Value); instWrites++; }
//                                catch { failures++; }
//                            }
//                            continue;
//                        }

//                        var typeParam = type.LookupParameter(kvp.Key);
//                        if (typeParam != null && !typeParam.IsReadOnly)
//                        {
//                            try { SetParamValue(typeParam, kvp.Value); typeWrites++; }
//                            catch { failures++; }
//                        }
//                    }
//                }

//                foreach (Element orphan in orphans)
//                {
//                    foreach (var kvp in csvParamSet)
//                    {
//                        var p = orphan.LookupParameter(kvp.Key);
//                        if (p == null || p.IsReadOnly) continue;
//                        try { SetParamValue(p, kvp.Value); instWrites++; }
//                        catch { failures++; }
//                    }
//                }

//                if (failures > 0)
//                    log.Warning(locationName, $"{failures} value(s) failed to convert and were skipped.");

//                log.Info(locationName,
//                    $"CPK params (CSV, {csvParamSet.Count} column(s)): {instWrites} instance value(s), {typeWrites} type value(s) written.");
//            }
//            else
//            {
//                if (!settings.TryGetCpkFallback(locationName, out string workPackage, out string wbsCode, out string wbsName))
//                {
//                    log.Warning(locationName, "No CPK config entry found — params not filled.");
//                    tx.RollBack();
//                    return;
//                }

//                int updated = 0;
//                foreach (var (type, _) in groups.Values)
//                {
//                    if (SetString(type, "CPK_Work_Package", workPackage)) updated++;
//                    SetString(type, "CPK_Stage", "Projekt Budowlany");
//                }

//                log.Info(locationName, $"CPK params (config fallback) updated on {updated} {trade.TradeName} element type(s).");
//            }

//            tx.Commit();
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  STEP 5 – PURGE UNUSED
//        // ════════════════════════════════════════════════════════════════════

//        private static void PurgeUnused(Document doc, string locationName, ProcessLog log)
//        {
//            int totalPurged = 0;
//            try
//            {
//                using Transaction t = new Transaction(doc, "Purge Unused");
//                t.Start();

//                ISet<ElementId> unusedIds = doc.GetUnusedElements(new HashSet<ElementId>());
//                int iterations = 0;

//                while (unusedIds.Any())
//                {
//                    doc.Delete(unusedIds);
//                    totalPurged += unusedIds.Count;
//                    iterations++;
//                    unusedIds = doc.GetUnusedElements(new HashSet<ElementId>());
//                }

//                t.Commit();
//                log.Info(locationName, $"Purged {totalPurged} unused element(s) across {iterations} pass(es).");
//            }
//            catch (Exception ex)
//            {
//                log.Error(locationName, $"Purge failed: {ex.Message}");
//            }
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  STEP 6 – 3D EXPORTS (NWC + IFC)
//        // ════════════════════════════════════════════════════════════════════

//        private static void Run3DExports(
//            Document doc,
//            string locationName,
//            string exportRoot,
//            ProcessLog log,
//            TradeProfile trade,
//            ProjectSettings settings,
//            ExportExecutionOptions options)
//        {
//            Dictionary<string, ElementId> exportViewIds = CreateExportViews(doc, locationName, log);

//            try
//            {
//                // ── IFC Export ─────────────────────────────────────────────
//                if (options.ExportIfc)
//                {
//                    if (File.Exists(settings.IFCJsonPath))
//                    {
//                        string ifcFolder = Path.Combine(exportRoot, "IFC");
//                        Directory.CreateDirectory(ifcFolder);

//                        string ifcFileName = trade.FormatFileName(trade.IfcFileNameTemplate, settings.ProjectNumber, settings.Originator, locationName);
//                        string ifcFullPath = Path.Combine(ifcFolder, ifcFileName + ".ifc");

//                        IFCExportOptions ifcOpts;
//                        using (var txIfc = new Transaction(doc, $"IFC Export Setup – {locationName}"))
//                        {
//                            txIfc.Start();
//                            ifcOpts = IFCJsonLoader.Load(settings.IFCJsonPath, doc, locationName);

//                            if (exportViewIds.TryGetValue("IFC", out ElementId ifcViewId) && ifcViewId != ElementId.InvalidElementId)
//                                ifcOpts.FilterViewId = ifcViewId;

//                            doc.Export(ifcFolder, ifcFileName, ifcOpts);
//                            txIfc.Commit();
//                        }
//                        log.Info(locationName, $"IFC exported → {ifcFullPath}");
//                    }
//                    else
//                    {
//                        log.Warning(locationName, $"IFCSettings.json not found at '{settings.IFCJsonPath}' — IFC export skipped.");
//                    }
//                }

//                // ── NWC Export ─────────────────────────────────────────────
//                if (options.ExportNwc)
//                {
//                    if (!exportViewIds.TryGetValue("NWC", out ElementId nwcViewId) || nwcViewId == ElementId.InvalidElementId)
//                        throw new InvalidOperationException($"[{locationName}] NWC export view could not be created — export aborted.");

//                    string nwcFolder = Path.Combine(exportRoot, "NWC");
//                    Directory.CreateDirectory(nwcFolder);

//                    string nwcFileName = trade.FormatFileName(trade.NwcFileNameTemplate, settings.ProjectNumber, settings.Originator, locationName);
//                    string nwcFullPath = Path.Combine(nwcFolder, nwcFileName + ".nwc");

//                    var navOpts = new NavisworksExportOptions
//                    {
//                        ExportScope = NavisworksExportScope.View,
//                        ViewId = nwcViewId,
//                        ExportElementIds = true,
//                        ConvertElementProperties = true,
//                        ConvertLights = true,
//                        ConvertLinkedCADFormats = false,
//                        Coordinates = NavisworksCoordinates.Shared,
//                        Parameters = NavisworksParameters.All,
//                        ExportUrls = false,
//                        DivideFileIntoLevels = true,
//                        ExportLinks = false,
//                        ExportParts = false,
//                        ExportRoomAsAttribute = false,
//                        ExportRoomGeometry = false,
//                        FindMissingMaterials = true,
//                        FacetingFactor = 1
//                    };

//                    doc.Export(nwcFolder, nwcFileName, navOpts);
//                    log.Info(locationName, $"NWC exported → {nwcFullPath}");
//                }
//            }
//            finally
//            {
//                DeleteExportViews(doc, locationName);
//            }
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  STEP 6B – UPDATE TEMPLATE '_PL' SHEETS & TITLEBLOCKS
//        // ════════════════════════════════════════════════════════════════════

//        private static List<ViewSheet> UpdatePlSheets(
//            Document doc,
//            string locationName,
//            Dictionary<string, string>? paramSet,
//            ProcessLog log)
//        {
//            var titleParts = doc.Title.Split('-');
//            if (titleParts.Length < 4)
//            {
//                log.Warning(locationName, $"Document title '{doc.Title}' does not follow hyphenated convention (expected >= 4 segments). Sheet processing skipped.");
//                return new List<ViewSheet>();
//            }

//            var nativeModelLocator = titleParts[3].Trim();

//            // Extract Work Package from spreadsheet if present
//            string targetWp = string.Empty;
//            if (paramSet != null)
//            {
//                if (paramSet.TryGetValue("Work_Package", out var wp) ||
//                    paramSet.TryGetValue("WorkPackage", out wp) ||
//                    paramSet.TryGetValue("CPK_WorkPackage", out wp) ||
//                    paramSet.TryGetValue("WP", out wp))
//                {
//                    targetWp = wp?.Trim() ?? string.Empty;
//                }
//            }

//            using var tx = new Transaction(doc, $"Update PL Sheets for {locationName}");
//            tx.Start();

//            // 1. Delete other sheets that are NOT nativeModelLocator BEFORE modifying anything
//            var allSheets = new FilteredElementCollector(doc)
//                .OfClass(typeof(ViewSheet))
//                .Cast<ViewSheet>()
//                .Where(s => !s.IsPlaceholder)
//                .ToList();

//            var sheetsToDelete = new List<ViewSheet>();
//            foreach (var s in allSheets)
//            {
//                // Preserve Sheet Cover
//                if (s.Name.IndexOf("Sheet Cover", StringComparison.OrdinalIgnoreCase) >= 0)
//                    continue;

//                // Delete sheets containing "210001_EN"
//                if (s.SheetNumber.IndexOf("210001_EN", StringComparison.OrdinalIgnoreCase) >= 0 ||
//                    s.Name.IndexOf("210001_EN", StringComparison.OrdinalIgnoreCase) >= 0)
//                {
//                    sheetsToDelete.Add(s);
//                    continue;
//                }

//                string[] parts = s.SheetNumber.Split('-');
//                if (parts.Length >= 4 && !parts[3].Equals(nativeModelLocator, StringComparison.OrdinalIgnoreCase))
//                {
//                    sheetsToDelete.Add(s);
//                }
//            }

//            if (sheetsToDelete.Count > 0)
//            {
//                int deletedCount = 0;
//                foreach (var s in sheetsToDelete)
//                {
//                    try
//                    {
//                        doc.Delete(s.Id);
//                        deletedCount++;
//                    }
//                    catch (Exception ex)
//                    {
//                        log.Warning(locationName, $"Could not delete obsolete sheet '{s.SheetNumber}': {ex.Message}");
//                    }
//                }
//                log.Info(locationName, $"Deleted {deletedCount} sheet(s) not matching native locator '{nativeModelLocator}' or containing '210001_EN'.");
//            }

//            // 2. Collect template '_PL' sheets matching nativeModelLocator to update
//            var plSheets = new FilteredElementCollector(doc)
//                .OfClass(typeof(ViewSheet))
//                .Cast<ViewSheet>()
//                .Where(s => !s.IsPlaceholder && s.SheetNumber.IndexOf("_PL", StringComparison.OrdinalIgnoreCase) >= 0)
//                .Where(s =>
//                {
//                    string[] parts = s.SheetNumber.Split('-');
//                    return parts.Length >= 4 && parts[3].Equals(nativeModelLocator, StringComparison.OrdinalIgnoreCase);
//                })
//                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
//                .ToList();

//            if (plSheets.Count == 0)
//            {
//                log.Warning(locationName, $"No template sheets containing '_PL' matching native locator '{nativeModelLocator}' were found in the model.");
//                tx.Commit();
//                return plSheets;
//            }

//            int renamedCount = 0;
//            int tbUpdatedCount = 0;

//            foreach (var sheet in plSheets)
//            {
//                try
//                {
//                    // 1. Rename Sheet Number: replace segment 3 (the locator code) with target locationName
//                    string oldNum = sheet.SheetNumber;
//                    string[] parts = oldNum.Split('-');
//                    if (parts.Length >= 4)
//                    {
//                        parts[3] = locationName;
//                        string newNum = string.Join("-", parts);
//                        if (!string.Equals(oldNum, newNum, StringComparison.OrdinalIgnoreCase))
//                        {
//                            sheet.SheetNumber = newNum;
//                            renamedCount++;
//                        }
//                    }
//                    else
//                    {
//                        log.Warning(locationName, $"Sheet '{oldNum}' does not have standard hyphenated format (expected >= 4 segments).");
//                    }

//                    // 2. Update Sheet Name to Work Package if specified
//                    if (!string.IsNullOrEmpty(targetWp) && !string.Equals(sheet.Name, targetWp, StringComparison.OrdinalIgnoreCase))
//                    {
//                        sheet.Name = targetWp;
//                    }

//                    // 3. Update Titleblock instances on this sheet
//                    var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
//                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
//                        .WhereElementIsNotElementType()
//                        .ToList();

//                    foreach (var tb in titleBlocks)
//                    {
//                        // 3a. Toggle Work Package Yes/No visibility checkboxes (e.g. WP04, WP05, WP11, etc.)
//                        if (!string.IsNullOrEmpty(targetWp))
//                        {
//                            foreach (Parameter p in tb.Parameters)
//                            {
//                                if (p.IsReadOnly || p.StorageType != StorageType.Integer) continue;
//                                string pName = p.Definition.Name.Trim();

//                                if (System.Text.RegularExpressions.Regex.IsMatch(pName, @"^WP\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
//                                {
//                                    int desiredVal = pName.Equals(targetWp, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
//                                    if (p.AsInteger() != desiredVal)
//                                    {
//                                        p.Set(desiredVal);
//                                    }
//                                }
//                            }
//                        }

//                        // 3b. Map spreadsheet parameter values onto Titleblock
//                        if (paramSet != null)
//                        {
//                            foreach (var kvp in paramSet)
//                            {
//                                var p = tb.LookupParameter(kvp.Key);
//                                if (p != null && !p.IsReadOnly)
//                                {
//                                    SetParamValue(p, kvp.Value);
//                                }
//                            }
//                        }

//                        tbUpdatedCount++;
//                    }

//                    // 4. Map spreadsheet parameter values onto Sheet
//                    if (paramSet != null)
//                    {
//                        foreach (var kvp in paramSet)
//                        {
//                            var p = sheet.LookupParameter(kvp.Key);
//                            if (p != null && !p.IsReadOnly)
//                            {
//                                SetParamValue(p, kvp.Value);
//                            }
//                        }
//                    }
//                }
//                catch (Exception ex)
//                {
//                    log.Warning(locationName, $"Failed updating sheet '{sheet.SheetNumber}': {ex.Message}");
//                }
//            }

//            tx.Commit();
//            log.Info(locationName, $"Updated {plSheets.Count} '_PL' sheet(s) ({renamedCount} renamed, {tbUpdatedCount} titleblock(s) synced to WP '{targetWp}').");

//            return plSheets;
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  STEP 7 – 2D SHEET EXPORTS (PDF + DWG)
//        // ════════════════════════════════════════════════════════════════════

//        private static void RunSheetExports(
//            Document doc,
//            string locationName,
//            string exportRoot,
//            ProcessLog log,
//            ExportExecutionOptions options,
//            List<ViewSheet>? plSheets = null)
//        {
//            var sheets = new List<ViewSheet>();
//            if (plSheets != null && plSheets.Count > 0)
//            {
//                sheets.AddRange(plSheets);
//            }

//            // Include Sheet Cover sheets if present
//            //var coverSheets = new FilteredElementCollector(doc)
//            //    .OfClass(typeof(ViewSheet))
//            //    .Cast<ViewSheet>()
//            //    .Where(s => !s.IsPlaceholder && s.Name.IndexOf("Sheet Cover", StringComparison.OrdinalIgnoreCase) >= 0)
//            //    .ToList();

//            //foreach (var cs in coverSheets)
//            //{
//            //    if (!sheets.Any(s => s.Id == cs.Id))
//            //    {
//            //        sheets.Insert(0, cs);
//            //    }
//            //}

//            if (sheets.Count == 0)
//            {
//                sheets = GetLocationSheets(doc, locationName);
//            }

//            if (sheets.Count == 0)
//            {
//                log.Warning(locationName, $"No sheets found matching location '{locationName}' — sheet exports skipped.");
//                return;
//            }

//            // ── PDF Export ─────────────────────────────────────────────
//            if (options.ExportPdf)
//            {
//                string pdfFolder = Path.Combine(exportRoot, "PDF");
//                Directory.CreateDirectory(pdfFolder);

//                try
//                {
//                    int exportedCount = 0;
//                    foreach (var sheet in sheets)
//                    {
//                        string safeSheetNum = string.Join("_", sheet.SheetNumber.Split(Path.GetInvalidFileNameChars())).Trim();
//                        if (string.IsNullOrEmpty(safeSheetNum))
//                            safeSheetNum = "Sheet_" + sheet.Id.Value;

//                        string targetPdfPath = Path.Combine(pdfFolder, safeSheetNum + ".pdf");
//                        if (File.Exists(targetPdfPath))
//                        {
//                            try { File.Delete(targetPdfPath); } catch { }
//                        }

//                        var beforeFiles = new HashSet<string>(Directory.GetFiles(pdfFolder, "*.pdf"), StringComparer.OrdinalIgnoreCase);

//                        var (paperFormat, orientation) = DetectPaperSize(sheet);

//                        var pdfOpts = new PDFExportOptions
//                        {
//                            FileName = safeSheetNum,
//                            Combine = true, // Combine=true with 1 view enforces exact FileName without Revit naming-rule prefixing
//                            ColorDepth = ColorDepthType.Color,
//                            ExportQuality = PDFExportQualityType.DPI300,
//                            PaperFormat = paperFormat,
//                            PaperOrientation = orientation,
//                            PaperPlacement = PaperPlacementType.LowerLeft,
//                            OriginOffsetX = 0.0,
//                            OriginOffsetY = 0.0,
//                            ZoomType = ZoomType.Zoom,
//                            ZoomPercentage = 100,
//                            HideUnreferencedViewTags = true,
//                            HideReferencePlane = true,
//                            HideCropBoundaries = true
//                        };

//                        doc.Export(pdfFolder, new List<ElementId> { sheet.Id }, pdfOpts);

//                        // If Revit generated a file with a naming-rule name (e.g. Sheet-WP49.pdf), rename it to <safeSheetNum>.pdf
//                        if (!File.Exists(targetPdfPath))
//                        {
//                            var afterFiles = Directory.GetFiles(pdfFolder, "*.pdf");
//                            var newlyCreated = afterFiles.FirstOrDefault(f => !beforeFiles.Contains(f));
//                            if (newlyCreated != null && File.Exists(newlyCreated))
//                            {
//                                try
//                                {
//                                    File.Move(newlyCreated, targetPdfPath);
//                                }
//                                catch (Exception exMove)
//                                {
//                                    log.Warning(locationName, $"Could not rename '{Path.GetFileName(newlyCreated)}' to '{safeSheetNum}.pdf': {exMove.Message}");
//                                }
//                            }
//                        }

//                        exportedCount++;
//                        log.Info(locationName, $"Exported PDF: {safeSheetNum}.pdf [{paperFormat}, {orientation}, LowerLeft, 100%]");
//                    }
//                    log.Info(locationName, $"PDF export complete: {exportedCount} sheet(s) → {pdfFolder}");
//                }
//                catch (Exception ex)
//                {
//                    log.Error(locationName, $"PDF export failed: {ex.Message}");
//                }
//            }

//            // ── DWG Export ─────────────────────────────────────────────
//            if (options.ExportDwg)
//            {
//                string dwgFolder = Path.Combine(exportRoot, "DWG");
//                Directory.CreateDirectory(dwgFolder);

//                try
//                {
//                    var dwgOpts = new DWGExportOptions
//                    {
//                        ACAPreference = ACAObjectPreference.Geometry,
//                        ExportOfSolids = SolidGeometry.Polymesh,
//                        MergedViews = true,
//                        FileVersion = ACADVersion.R2018,
//                        LayerMapping = "AIA",
//                        TargetUnit = ExportUnit.Millimeter,
//                        HideUnreferenceViewTags = true,
//                        HideReferencePlane = true
//                    };

//                    int exportedDwgCount = 0;
//                    foreach (var sheet in sheets)
//                    {
//                        string safeSheetNum = string.Join("_", sheet.SheetNumber.Split(Path.GetInvalidFileNameChars())).Trim();
//                        if (string.IsNullOrEmpty(safeSheetNum))
//                            safeSheetNum = "Sheet_" + sheet.Id.Value;

//                        doc.Export(dwgFolder, safeSheetNum, new List<ElementId> { sheet.Id }, dwgOpts);
//                        exportedDwgCount++;
//                    }
//                    log.Info(locationName, $"DWG export complete: {exportedDwgCount} sheet(s) → {dwgFolder}");
//                }
//                catch (Exception ex)
//                {
//                    log.Error(locationName, $"DWG export failed: {ex.Message}");
//                }
//            }
//        }

//        private static List<ViewSheet> GetLocationSheets(Document doc, string locationName)
//        {
//            return new FilteredElementCollector(doc)
//                .OfClass(typeof(ViewSheet))
//                .Cast<ViewSheet>()
//                .Where(s => !s.IsPlaceholder)
//                .Where(s => s.SheetNumber.IndexOf(locationName, StringComparison.OrdinalIgnoreCase) >= 0)
//                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
//                .ToList();
//        }

//        /// <summary>
//        /// Detects standard paper format and orientation from the sheet's Titleblock instance.
//        /// Falls back to sheet outline or titleblock name hints.
//        /// </summary>
//        private static (ExportPaperFormat format, PageOrientationType orientation) DetectPaperSize(ViewSheet sheet)
//        {
//            Document doc = sheet.Document;

//            // 1. Try to find Titleblock instance on sheet
//            var titleBlock = new FilteredElementCollector(doc, sheet.Id)
//                .OfCategory(BuiltInCategory.OST_TitleBlocks)
//                .WhereElementIsNotElementType()
//                .FirstOrDefault();

//            double widthMm = 0;
//            double heightMm = 0;

//            if (titleBlock != null)
//            {
//                var pW = titleBlock.get_Parameter(BuiltInParameter.SHEET_WIDTH);
//                var pH = titleBlock.get_Parameter(BuiltInParameter.SHEET_HEIGHT);

//                if (pW != null && pH != null)
//                {
//                    widthMm = UnitUtils.ConvertFromInternalUnits(pW.AsDouble(), UnitTypeId.Millimeters);
//                    heightMm = UnitUtils.ConvertFromInternalUnits(pH.AsDouble(), UnitTypeId.Millimeters);
//                }

//                if (widthMm <= 1 || heightMm <= 1)
//                {
//                    var bbox = titleBlock.get_BoundingBox(sheet);
//                    if (bbox != null)
//                    {
//                        widthMm = UnitUtils.ConvertFromInternalUnits(bbox.Max.X - bbox.Min.X, UnitTypeId.Millimeters);
//                        heightMm = UnitUtils.ConvertFromInternalUnits(bbox.Max.Y - bbox.Min.Y, UnitTypeId.Millimeters);
//                    }
//                }
//            }

//            if (widthMm <= 1 || heightMm <= 1)
//            {
//                var outline = sheet.Outline;
//                if (outline != null)
//                {
//                    widthMm = UnitUtils.ConvertFromInternalUnits(outline.Max.U - outline.Min.U, UnitTypeId.Millimeters);
//                    heightMm = UnitUtils.ConvertFromInternalUnits(outline.Max.V - outline.Min.V, UnitTypeId.Millimeters);
//                }
//            }

//            bool isLandscape = widthMm >= heightMm;
//            var orientation = isLandscape ? PageOrientationType.Landscape : PageOrientationType.Portrait;

//            double longSide = Math.Max(widthMm, heightMm);
//            double shortSide = Math.Min(widthMm, heightMm);

//            bool Matches(double targetLong, double targetShort) =>
//                Math.Abs(longSide - targetLong) <= 30 && Math.Abs(shortSide - targetShort) <= 30;

//            // ISO Formats
//            if (Matches(1189, 841)) return (ExportPaperFormat.ISO_A0, orientation);
//            if (Matches(841, 594))  return (ExportPaperFormat.ISO_A1, orientation);
//            if (Matches(594, 420))  return (ExportPaperFormat.ISO_A2, orientation);
//            if (Matches(420, 297))  return (ExportPaperFormat.ISO_A3, orientation);
//            if (Matches(297, 210))  return (ExportPaperFormat.ISO_A4, orientation);

//            if (Matches(1000, 707)) return (ExportPaperFormat.ISO_B1, orientation);
//            if (Matches(707, 500))  return (ExportPaperFormat.ISO_B2, orientation);
//            if (Matches(500, 353))  return (ExportPaperFormat.ISO_B3, orientation);
//            if (Matches(353, 250))  return (ExportPaperFormat.ISO_B4, orientation);

//            // Architectural Formats
//            if (Matches(1219, 914)) return (ExportPaperFormat.ARCH_E, orientation);
//            if (Matches(1067, 762)) return (ExportPaperFormat.ARCH_E1, orientation);
//            if (Matches(914, 610))  return (ExportPaperFormat.ARCH_D, orientation);
//            if (Matches(610, 457))  return (ExportPaperFormat.ARCH_C, orientation);
//            if (Matches(457, 305))  return (ExportPaperFormat.ARCH_B, orientation);
//            if (Matches(305, 229))  return (ExportPaperFormat.ARCH_A, orientation);

//            // ANSI Formats
//            if (Matches(1118, 864)) return (ExportPaperFormat.ANSI_E, orientation);
//            if (Matches(864, 559))  return (ExportPaperFormat.ANSI_D, orientation);
//            if (Matches(559, 432))  return (ExportPaperFormat.ANSI_C, orientation);
//            if (Matches(432, 279))  return (ExportPaperFormat.ANSI_B, orientation);
//            if (Matches(279, 216))  return (ExportPaperFormat.ANSI_A, orientation);

//            // Name hints
//            if (titleBlock != null)
//            {
//                string name = titleBlock.Name + " " + (doc.GetElement(titleBlock.GetTypeId())?.Name ?? "");
//                if (name.IndexOf("A0", StringComparison.OrdinalIgnoreCase) >= 0) return (ExportPaperFormat.ISO_A0, orientation);
//                if (name.IndexOf("A1", StringComparison.OrdinalIgnoreCase) >= 0) return (ExportPaperFormat.ISO_A1, orientation);
//                if (name.IndexOf("A2", StringComparison.OrdinalIgnoreCase) >= 0) return (ExportPaperFormat.ISO_A2, orientation);
//                if (name.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0) return (ExportPaperFormat.ISO_A3, orientation);
//                if (name.IndexOf("A4", StringComparison.OrdinalIgnoreCase) >= 0) return (ExportPaperFormat.ISO_A4, orientation);
//            }

//            return (ExportPaperFormat.Default, orientation);
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  STEP 8 – SAVE AS CENTRAL
//        // ════════════════════════════════════════════════════════════════════

//        private static void SaveAsCentral(
//            Document doc,
//            string locationName,
//            string exportRoot,
//            ProcessLog log,
//            TradeProfile trade,
//            ProjectSettings settings)
//        {
//            if (!doc.IsWorkshared)
//            {
//                try
//                {
//                    doc.EnableWorksharing("Shared Levels and Grids", "Workset1");
//                }
//                catch (Exception ex)
//                {
//                    log.Warning(locationName, $"EnableWorksharing failed (proceeding with SaveAs): {ex.Message}");
//                }
//            }

//            string rvtFolder = Path.Combine(exportRoot, "RVT");
//            Directory.CreateDirectory(rvtFolder);

//            string rvtFileName = trade.FormatFileName(trade.RvtFileNameTemplate, settings.ProjectNumber, settings.Originator, locationName) + ".rvt";
//            string newPath = Path.Combine(rvtFolder, rvtFileName);

//            var saveOpts = new SaveAsOptions
//            {
//                OverwriteExistingFile = true,
//                Compact = false
//            };
//            saveOpts.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });

//            doc.SaveAs(newPath, saveOpts);
//            log.Info(locationName, $"RVT saved → {newPath}");
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  VIEW HELPERS
//        // ════════════════════════════════════════════════════════════════════

//        private static Dictionary<string, ElementId> CreateExportViews(
//            Document doc,
//            string locationName,
//            ProcessLog log)
//        {
//            var created = new Dictionary<string, ElementId>();

//            var all3D = new FilteredElementCollector(doc)
//                .OfClass(typeof(View3D))
//                .Cast<View3D>()
//                .Where(v => !v.IsTemplate)
//                .ToList();

//            View3D? exactNwc = all3D.FirstOrDefault(v => v.Name.Equals("EXPORT-NWC", StringComparison.OrdinalIgnoreCase));
//            View3D? exactIfc = all3D.FirstOrDefault(v => v.Name.Equals("EXPORT-IFC", StringComparison.OrdinalIgnoreCase));
//            View3D? sourceView = all3D.FirstOrDefault(v => v.Name.IndexOf("Naviswork", StringComparison.OrdinalIgnoreCase) >= 0);

//            if (sourceView != null)
//            {
//                using var tx = new Transaction(doc, "Create Export Views");
//                tx.Start();

//                if (exactNwc != null) { try { doc.Delete(exactNwc.Id); } catch { } }
//                if (exactIfc != null) { try { doc.Delete(exactIfc.Id); } catch { } }

//                try
//                {
//                    if (doc.GetElement(sourceView.Duplicate(ViewDuplicateOption.Duplicate)) is View3D nwcView)
//                    {
//                        nwcView.Name = "EXPORT-NWC";
//                        created["NWC"] = nwcView.Id;
//                    }
//                }
//                catch (Exception ex) { log.Warning(locationName, $"NWC view creation failed: {ex.Message}"); }

//                try
//                {
//                    if (doc.GetElement(sourceView.Duplicate(ViewDuplicateOption.Duplicate)) is View3D ifcView)
//                    {
//                        ifcView.Name = "EXPORT-IFC";
//                        created["IFC"] = ifcView.Id;
//                    }
//                }
//                catch (Exception ex) { log.Warning(locationName, $"IFC view creation failed: {ex.Message}"); }

//                tx.Commit();
//                log.Info(locationName, $"Export views freshly duplicated from '{sourceView.Name}'.");
//            }
//            else
//            {
//                log.Warning(locationName, "No 'Naviswork' source view found. Searching for existing export views.");

//                if (exactNwc != null)
//                {
//                    created["NWC"] = exactNwc.Id;
//                    log.Info(locationName, "Using pre-existing exact view: 'EXPORT-NWC'.");
//                }
//                else
//                {
//                    View3D? fuzzyNwc = all3D.FirstOrDefault(v => v.Name.IndexOf("NWC", StringComparison.OrdinalIgnoreCase) >= 0);
//                    if (fuzzyNwc != null)
//                    {
//                        created["NWC"] = fuzzyNwc.Id;
//                        log.Info(locationName, $"Using fuzzy match NWC view: '{fuzzyNwc.Name}'.");
//                    }
//                    else log.Warning(locationName, "No existing NWC view found.");
//                }

//                if (exactIfc != null)
//                {
//                    created["IFC"] = exactIfc.Id;
//                    log.Info(locationName, "Using pre-existing exact view: 'EXPORT-IFC'.");
//                }
//                else
//                {
//                    View3D? fuzzyIfc = all3D.FirstOrDefault(v => v.Name.IndexOf("IFC", StringComparison.OrdinalIgnoreCase) >= 0);
//                    if (fuzzyIfc != null)
//                    {
//                        created["IFC"] = fuzzyIfc.Id;
//                        log.Info(locationName, $"Using fuzzy match IFC view: '{fuzzyIfc.Name}'.");
//                    }
//                    else log.Warning(locationName, "No existing IFC view found.");
//                }
//            }

//            return created;
//        }

//        private static void DeleteExportViews(Document doc, string locationName)
//        {
//            View3D sourceView = new FilteredElementCollector(doc)
//                .OfClass(typeof(View3D))
//                .Cast<View3D>()
//                .FirstOrDefault(v =>
//                    !v.IsTemplate &&
//                    v.Name.IndexOf("Naviswork", StringComparison.OrdinalIgnoreCase) >= 0);

//            if (sourceView == null) return;

//            try
//            {
//                using var tx = new Transaction(doc, "Delete Export Views");
//                tx.Start();
//                try { doc.Delete(sourceView.Id); } catch { }
//                tx.Commit();
//            }
//            catch (Exception ex)
//            {
//                TaskDialog.Show("Cleanup Warning", $"[{locationName}] Could not delete export views: {ex.Message}");
//            }
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  UI & FILE SELECTION HELPERS
//        // ════════════════════════════════════════════════════════════════════

//        private static List<string> SelectRvtFiles()
//        {
//            var dlg = new OpenFileDialog
//            {
//                Filter = "Revit Files (*.rvt)|*.rvt",
//                Title = "Select Revit Model(s)",
//                Multiselect = true
//            };
//            return dlg.ShowDialog() == true ? dlg.FileNames.ToList() : new List<string>();
//        }

//        private static string? SelectParameterFile()
//        {
//            var dlg = new OpenFileDialog
//            {
//                Title = "Select Location Sheet Parameters (Excel or CSV) - Cancel to skip",
//                Filter = "Excel or CSV Files (*.xlsx;*.csv)|*.xlsx;*.csv|Excel Files (*.xlsx)|*.xlsx|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
//            };
//            return dlg.ShowDialog() == true ? dlg.FileName : null;
//        }

//        internal static Dictionary<string, Dictionary<string, string>> ReadCsv(string csvPath)
//        {
//            return LocationDataSheetReader.ReadCsv(csvPath);
//        }

//        private static List<string> ReadProjectLocationNames(
//            Autodesk.Revit.ApplicationServices.Application app,
//            ModelPath rvtPath)
//        {
//            var opts = new OpenOptions
//            {
//                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
//            };
//            opts.SetOpenWorksetsConfiguration(
//                new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets));

//            using var doc = app.OpenDocumentFile(rvtPath, opts);
//            return doc.ProjectLocations
//                .Cast<ProjectLocation>()
//                .Select(pl => pl.Name)
//                .ToList();
//        }

//        public static void SetPinnedStateForReferenceElements(Document doc, bool pinned)
//        {
//            using (Transaction t = new Transaction(doc, pinned ? "Pin reference elements" : "Unpin reference elements"))
//            {
//                t.Start();

//                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>();
//                foreach (Level lvl in levels) if (lvl.Pinned != pinned) lvl.Pinned = pinned;

//                var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>();
//                foreach (Grid g in grids) if (g.Pinned != pinned) g.Pinned = pinned;

//                var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>();
//                foreach (RevitLinkInstance link in links) if (link.Pinned != pinned) link.Pinned = pinned;

//                var basePoints = new FilteredElementCollector(doc).OfClass(typeof(BasePoint)).Cast<BasePoint>();
//                foreach (BasePoint bp in basePoints) if (bp.Pinned != pinned) bp.Pinned = pinned;

//                t.Commit();
//            }
//        }

//        // ════════════════════════════════════════════════════════════════════
//        //  PARAMETER & UTILITY HELPERS
//        // ════════════════════════════════════════════════════════════════════

//        private static BasePoint? GetBasePoint(Document doc, BuiltInCategory cat) =>
//            new FilteredElementCollector(doc)
//                .OfClass(typeof(BasePoint))
//                .OfCategory(cat)
//                .Cast<BasePoint>()
//                .FirstOrDefault();

//        private static double ToMm(BasePoint bp, BuiltInParameter bip) =>
//            UnitUtils.ConvertFromInternalUnits(
//                bp.get_Parameter(bip).AsDouble(), UnitTypeId.Millimeters);

//        private static bool SetDouble(Element e, string name, double val)
//        {
//            var p = e.LookupParameter(name);
//            if (p == null || p.IsReadOnly) return false;
//            p.Set(val);
//            return true;
//        }

//        private static bool SetString(Element e, string name, string val)
//        {
//            var p = e.LookupParameter(name);
//            if (p == null || p.IsReadOnly) return false;
//            p.Set(val);
//            return true;
//        }

//        private static void SetParamValue(Parameter param, string raw)
//        {
//            switch (param.StorageType)
//            {
//                case StorageType.String:
//                    param.Set(raw);
//                    break;
//                case StorageType.Double:
//                    if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
//                        param.Set(d);
//                    break;
//                case StorageType.Integer:
//                    if (int.TryParse(raw, out int i))
//                        param.Set(i);
//                    break;
//            }
//        }
//    }
//}


// ════════════════════════════════════════════════════════════════════════════
//  LocationExporterCommand.cs – Decoupled, Multi-Trade, Granular Options (v7)
//  Revit 2024 | RevitLocationExporter.Commands
//
//  - Fully configurable for ALL trades (TI0, EL0, ME0, PL0, AR0, ST0, etc.)
//  - Granular operation checkboxes: Parameter filling, 3D Exports, 2D Sheets, Purge
//  - Native PDF & DWG sheet exports
//  - Trade profiles and project settings externalized to TradeConfigManager
//  - v7: per-sheet parameter overrides keyed by Sheet Number (Sheet_Specific_Params tab)
//        merged over common per-location values (Sheet_Params tab)
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;


namespace RevitLocationExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LocationExporterCommand : IExternalCommand
    {


        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ExecuteCore(commandData, ref message, elements);
            }
            catch (Exception ex)
            {
                string dump = ex.ToString();   // full stack trace incl. inner exceptions
                string path = Path.Combine(Path.GetTempPath(), "RevitLocationExporter_crash.txt");
                try { File.WriteAllText(path, dump); } catch { }
                TaskDialog.Show("RevitLocationExporter – crash", $"{ex.Message}\n\nFull trace saved to:\n{path}\n\n{dump}");
                message = ex.Message;
                return Result.Failed;
            }
        }

        public Result ExecuteCore(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var log = new ProcessLog();

            // Re-read the Model Checker XMLs on every run (picks up edited check-sets without restarting Revit)
            ModelCheckerSetLoader.ClearCache();

            // ── 1. Select RVT files ────────────────────────────────────────────
            var selectedPaths = SelectRvtFiles();
            if (selectedPaths.Count == 0) return Result.Cancelled;

            var app = commandData.Application.Application;
            var configMgr = TradeConfigManager.Instance;
            var settings = configMgr.Settings;

            // ── 2. Discovery pass: read all project locations and worksets without full open ─
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

            // ── 3. Show location, trade profile & Excel sync dialog ───────────
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

            // ── 4. Process selected locations per file (Automatic Hybrid Pipeline) ──
            var updater = new ProjectInfoUpdater();

            try
            {
                foreach (var fileKv in selectedLocations)
                {
                    string filePath = fileKv.Key;
                    IReadOnlyList<string> locationsToProcess = fileKv.Value;
                    if (locationsToProcess.Count == 0) continue;

                    string fileName = Path.GetFileName(filePath);
                    string[] fileTitleParts = Path.GetFileNameWithoutExtension(filePath).Split('-');
                    string nativeModelLocator = fileTitleParts.Length >= 4 ? fileTitleParts[3].Trim() : string.Empty;
                    var rvtPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

                    // Dynamically resolve Trade for this file (e.g. TI0, EL0, ME0, PL0, AR0, ST0)
                    TradeProfile trade = selDlg.SelectedTradeOverride ?? configMgr.DetectTrade(fileName);
                    trade.EnsureInitialized();
                    log.Info(fileName, $"Identified trade: {trade.TradeName} ({trade.TradeCode})");

                    // Resolve synced Excel bundle from Trade Profile (auto-syncs if Excel changed) or one-off override
                    LocationParameterBundle? paramBundle =
                        TradeExcelSyncService.ResolveBundleForRun(trade, selDlg.OneOffExcelOverridePath, log);
                    ExcelFormulaEngine.TableLookupFunc? tableLookup =
                        paramBundle?.LookupTables is { Count: > 0 }
                            ? ExcelFormulaEngine.CreateTableLookup(paramBundle.LookupTables)
                            : null;

                    string effProjNum = !string.IsNullOrWhiteSpace(trade.ProjectNumber) ? trade.ProjectNumber : settings.ProjectNumber;
                    string effOrig = !string.IsNullOrWhiteSpace(trade.Originator) ? trade.Originator : settings.Originator;

                    // Pre-resolve export folder & location data up front
                    bool needsExportFolder = options.SaveCentralRvt || options.ExportNwc || options.ExportIfc || options.ExportPdf || options.ExportDwg;
                    var locContexts = new List<(string Name, Dictionary<string, string>? ElemParams, Dictionary<string, string>? SheetParams, string ExportRoot, Dictionary<string, string> Tokens)>();

                    foreach (string locationName in locationsToProcess)
                    {
                        Dictionary<string, string>? elementParams = null;
                        Dictionary<string, string>? sheetParams = null;

                        if (paramBundle != null)
                        {
                            elementParams = paramBundle.GetElementParams(locationName);
                            sheetParams = paramBundle.GetSheetParams(locationName);

                            if (elementParams == null && sheetParams == null && trade.ParameterRules.Count == 0)
                                log.Warning(locationName,
                                    "No spreadsheet row matched this location in element or sheet tabs — using built-in config.");
                        }

                        string? customExportFolder = paramBundle?.GetExportLocation(locationName);
                        string exportRoot = string.Empty;
                        try
                        {
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
                                log.Info(locationName, $"Export destination folder: {exportRoot}");
                        }
                        catch (Exception ex)
                        {
                            log.Error(locationName, $"[{fileName}] {ex.Message}");
                            continue;
                        }

                        var builtInTokens = BuildLocationTokens(locationName, trade, effProjNum, effOrig);
                        locContexts.Add((locationName, elementParams, sheetParams, exportRoot, builtInTokens));
                    }

                    if (locContexts.Count == 0) continue;

                    // Compute WorksetConfiguration ONCE per source file
                    OpenOptions openOpts = BuildOpenOptions(rvtPath, worksetOptions, trade, log);

                    if (locContexts.Count == 1)
                    {
                        // Single location: direct detached open once
                        var ctx = locContexts[0];
                        try
                        {
                            using Document doc = app.OpenDocumentFile(rvtPath, openOpts);
                            try
                            {
                                RunLocationStepsOnOpenDoc(doc, rvtPath, nativeModelLocator, ctx.Name, ctx.ElemParams, ctx.SheetParams,
                                    paramBundle, ctx.ExportRoot, log, updater, trade, settings, options,
                                    effProjNum, effOrig, ctx.Tokens, tableLookup, alreadyPreCleaned: false);
                                doc.Close(false);
                                log.Info(ctx.Name, "All requested steps completed.");
                            }
                            catch
                            {
                                try { doc.Close(false); } catch { }
                                throw;
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(ctx.Name, $"[{fileName}] {ex.Message}");
                        }
                    }
                    else if (!options.SaveCentralRvt)
                    {
                        // Hybrid Fast Path (2+ locations, SaveCentralRvt OFF):
                        // Open heavy model ONCE, commit one-time pre-clean baseline, then isolate each location via TransactionGroup.RollBack()
                        log.Info(fileName, $"Hybrid In-Memory mode: opening once for {locContexts.Count} location(s) (zero re-opens).");
                        try
                        {
                            using Document doc = app.OpenDocumentFile(rvtPath, openOpts);
                            try
                            {
                                PreCleanDocument(doc, nativeModelLocator, log, trade, options);

                                foreach (var ctx in locContexts)
                                {
                                    using var tg = new TransactionGroup(doc, $"Export Location – {ctx.Name}");
                                    tg.Start();
                                    try
                                    {
                                        RunLocationStepsOnOpenDoc(doc, rvtPath, nativeModelLocator, ctx.Name, ctx.ElemParams, ctx.SheetParams,
                                            paramBundle, ctx.ExportRoot, log, updater, trade, settings, options,
                                            effProjNum, effOrig, ctx.Tokens, tableLookup, alreadyPreCleaned: true);
                                        log.Info(ctx.Name, "All requested steps completed.");
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(ctx.Name, $"[{fileName}] {ex.Message}");
                                    }
                                    finally
                                    {
                                        try { if (tg.HasStarted()) tg.RollBack(); } catch { }
                                    }
                                }

                                doc.Close(false);
                            }
                            catch
                            {
                                try { doc.Close(false); } catch { }
                                throw;
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(fileName, $"Failed in-memory batch open for '{fileName}': {ex.Message}");
                        }
                    }
                    else
                    {
                        // Hybrid Staged Path (2+ locations, SaveCentralRvt ON):
                        // Open heavy model ONCE -> pre-clean & compact to fast local SSD temp copy -> extract each location cleanly
                        string tempStageDir = Path.Combine(Path.GetTempPath(), $"RevitLocStage_{Guid.NewGuid():N}");
                        Directory.CreateDirectory(tempStageDir);
                        string stagedPath = Path.Combine(tempStageDir, fileName);
                        ModelPath stagedModelPath;
                        OpenOptions stagedOpenOpts;

                        log.Info(fileName, $"Hybrid Staged mode: pre-cleaning and compacting '{fileName}' once for {locContexts.Count} central RVT export(s).");
                        try
                        {
                            using (Document srcDoc = app.OpenDocumentFile(rvtPath, openOpts))
                            {
                                try
                                {
                                    PreCleanDocument(srcDoc, nativeModelLocator, log, trade, options);

                                    var stageSaveOpts = new SaveAsOptions
                                    {
                                        OverwriteExistingFile = true,
                                        Compact = true
                                    };
                                    if (srcDoc.IsWorkshared)
                                    {
                                        stageSaveOpts.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
                                    }
                                    srcDoc.SaveAs(stagedPath, stageSaveOpts);
                                    stagedModelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(stagedPath);
                                    stagedOpenOpts = BuildOpenOptions(stagedModelPath, worksetOptions, trade, log, quiet: true);
                                    srcDoc.Close(false);
                                }
                                catch
                                {
                                    try { srcDoc.Close(false); } catch { }
                                    throw;
                                }
                            }

                            foreach (var ctx in locContexts)
                            {
                                try
                                {
                                    using Document doc = app.OpenDocumentFile(stagedModelPath, stagedOpenOpts);
                                    try
                                    {
                                        RunLocationStepsOnOpenDoc(doc, stagedModelPath, nativeModelLocator, ctx.Name, ctx.ElemParams, ctx.SheetParams,
                                            paramBundle, ctx.ExportRoot, log, updater, trade, settings, options,
                                            effProjNum, effOrig, ctx.Tokens, tableLookup, alreadyPreCleaned: true);
                                        doc.Close(false);
                                        log.Info(ctx.Name, "All requested steps completed.");
                                    }
                                    catch
                                    {
                                        try { doc.Close(false); } catch { }
                                        throw;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error(ctx.Name, $"[{fileName}] {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(fileName, $"Failed staged extraction for '{fileName}': {ex.Message}");
                        }
                        finally
                        {
                            try { if (Directory.Exists(tempStageDir)) Directory.Delete(tempStageDir, true); } catch { }
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

        private static Dictionary<string, string> BuildLocationTokens(
            string locationName, TradeProfile trade, string projectNumber, string originator)
        {
            string rvtName = trade.FormatFileName(trade.RvtFileNameTemplate, projectNumber, originator, locationName);
            string ifcName = trade.FormatFileName(trade.IfcFileNameTemplate, projectNumber, originator, locationName);
            string nwcName = trade.FormatFileName(trade.NwcFileNameTemplate, projectNumber, originator, locationName);
            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Location",      locationName },
                { "Locator Code",  locationName },
                { "LocatorCode",   locationName },
                { "TradeCode",     trade.TradeCode },
                { "TradeName",     trade.TradeName },
                { "ProjectNumber", projectNumber },
                { "Originator",    originator },
                { "RvtFileName",   rvtName },
                { "IfcFileName",   ifcName },
                { "NwcFileName",   nwcName },
                { "Today",         today },
                { "Date",          today },
                { "UserName",      Environment.UserName }
            };
        }

        private static OpenOptions BuildOpenOptions(
            ModelPath rvtPath,
            WorksetSelectionOptions worksetOptions,
            TradeProfile trade,
            ProcessLog log,
            bool quiet = false)
        {
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
                        if (!quiet) log.Info(trade.TradeCode, $"Opened all {allWorksets.Count} worksets.");
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
                            if (!quiet) log.Info(trade.TradeCode, $"Opened {idsToOpen.Count} of {allWorksets.Count} worksets (global config).");
                        }
                        else if (!quiet)
                        {
                            log.Warning(trade.TradeCode, "No worksets matched global workset configuration — opening model with all user worksets closed.");
                        }
                    }

                    opts.SetOpenWorksetsConfiguration(wsConfig);
                }
            }
            catch (Exception ex)
            {
                if (!quiet) log.Warning(trade.TradeCode, $"Could not configure worksets before opening: {ex.Message}");
            }

            return opts;
        }

        private static Transaction StartSilentTransaction(Document doc, string name)
        {
            var tx = new Transaction(doc, name);
            var failOpts = tx.GetFailureHandlingOptions();
            failOpts.SetFailuresPreprocessor(new SilentFailurePreprocessor());
            tx.SetFailureHandlingOptions(failOpts);
            tx.Start();
            return tx;
        }

        private static void PreCleanDocument(
            Document doc,
            string nativeModelLocator,
            ProcessLog log,
            TradeProfile trade,
            ExportExecutionOptions options)
        {
            bool willUpdateSheets = options.FillParameters && (options.ExportPdf || options.ExportDwg ||
                trade.ParameterRules.Any(r => r.Enabled && (r.Target == ParameterRuleTarget.Sheet || r.Target == ParameterRuleTarget.Titleblock)) ||
                trade.TextNoteRules.Any(r => r.Enabled));

            if (willUpdateSheets && !string.IsNullOrEmpty(nativeModelLocator))
            {
                DeleteObsoleteSheets(doc, nativeModelLocator, "PreClean", log, trade);
            }

            if (options.PurgeUnused)
            {
                PurgeUnused(doc, "PreClean", log);
            }
        }

        private static int DeleteObsoleteSheets(
            Document doc,
            string nativeModelLocator,
            string logKey,
            ProcessLog log,
            TradeProfile trade)
        {
            string coverName = string.IsNullOrWhiteSpace(trade.SheetCoverName) ? "Sheet Cover" : trade.SheetCoverName;
            var deletePatterns = (trade.SheetDeletePatterns ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            var allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            var sheetsToDelete = new List<ViewSheet>();
            foreach (var s in allSheets)
            {
                if (s.Name.IndexOf(coverName, StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (deletePatterns.Any(pat =>
                    s.SheetNumber.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Name.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0))
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

            if (sheetsToDelete.Count == 0) return 0;

            using var tx = StartSilentTransaction(doc, "Delete Obsolete Sheets");
            int deletedCount = 0;
            try
            {
                var deleted = doc.Delete(sheetsToDelete.Select(s => s.Id).ToList());
                deletedCount = sheetsToDelete.Count;
            }
            catch
            {
                foreach (var s in sheetsToDelete)
                {
                    try { doc.Delete(s.Id); deletedCount++; }
                    catch (Exception ex)
                    {
                        log.Warning(logKey, $"Could not delete obsolete sheet '{s.SheetNumber}': {ex.Message}");
                    }
                }
            }
            tx.Commit();

            if (deletedCount > 0)
                log.Info(logKey, $"Deleted {deletedCount} obsolete sheet(s) not matching native locator '{nativeModelLocator}' or delete rules.");

            return deletedCount;
        }

        // ════════════════════════════════════════════════════════════════════
        //  LOCATION PIPELINE (OPTION-AWARE)
        // ════════════════════════════════════════════════════════════════════

        private static void RunLocationStepsOnOpenDoc(
            Document doc,
            ModelPath rvtPath,
            string nativeModelLocator,
            string locationName,
            Dictionary<string, string>? elementParams,
            Dictionary<string, string>? sheetParams,
            LocationParameterBundle? paramBundle,
            string exportRoot,
            ProcessLog log,
            ProjectInfoUpdater updater,
            TradeProfile trade,
            ProjectSettings settings,
            ExportExecutionOptions options,
            string effProjNum,
            string effOrig,
            Dictionary<string, string> builtInTokens,
            ExcelFormulaEngine.TableLookupFunc? tableLookup,
            bool alreadyPreCleaned)
        {
            SetPinnedStateForReferenceElements(doc, false);

            // 1. Coordinates & Workset Renaming
            if (options.AlignCoordinatesAndWorksets)
            {
                ActivateProjectLocation(doc, locationName, log, trade);
                RenameWorksets(doc, locationName, log, trade);
            }

            // 2. Parameter Population
            if (options.FillParameters)
            {
                FillSheetCoverParameters(doc, locationName, log, trade, elementParams, sheetParams, builtInTokens, tableLookup);
                FillCpkParameters(doc, locationName, elementParams, log, trade, settings, builtInTokens, tableLookup);
                UpdateProjectInfo(doc, updater, settings.ProjectData, locationName, sheetParams, elementParams,
                                  log, trade, effProjNum, effOrig, builtInTokens, tableLookup);
            }

            // Update template '_PL' sheets (Sheet Number, Work Package, Titleblock Checkboxes, Parameters & TextNote Rules)
            List<ViewSheet> plSheets = new();
            if (options.FillParameters && (options.ExportPdf || options.ExportDwg ||
                trade.ParameterRules.Any(r => r.Enabled && (r.Target == ParameterRuleTarget.Sheet || r.Target == ParameterRuleTarget.Titleblock)) ||
                trade.TextNoteRules.Any(r => r.Enabled)))
            {
                plSheets = UpdatePlSheets(doc, locationName, sheetParams, elementParams, paramBundle, log, trade,
                                          builtInTokens, tableLookup, nativeModelLocator, alreadyPreCleaned);
            }

            if (options.FillParameters && options.EnforceModelStandards)
            {
                EnforceModelStandards(doc, rvtPath, locationName, elementParams, sheetParams,
                                      paramBundle, exportRoot, log, trade, effProjNum, effOrig, options);
            }

            // 3. Purge Unused (skip when already run during PreCleanDocument)
            if (options.PurgeUnused && !alreadyPreCleaned)
            {
                PurgeUnused(doc, locationName, log);
            }

            // 4. 3D Model Exports (NWC, IFC)
            if (options.ExportNwc || options.ExportIfc)
            {
                Run3DExports(doc, locationName, exportRoot, log, trade, effProjNum, effOrig, options);
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
                SaveAsCentral(doc, locationName, exportRoot, log, trade, effProjNum, effOrig);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 1 – ACTIVATE PROJECT LOCATION & SURVEY POINT
        // ════════════════════════════════════════════════════════════════════

        private static void ActivateProjectLocation(
            Document doc,
            string locationName,
            ProcessLog log,
            TradeProfile trade)
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
            SetSurveyPoint(doc, trade);

            tx.Commit();
        }

        private static void SetSurveyPoint(Document doc, TradeProfile trade)
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
              .Set(UnitUtils.ConvertToInternalUnits(trade.SurveyEastMm, UnitTypeId.Millimeters));
            sp.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM)
              .Set(UnitUtils.ConvertToInternalUnits(trade.SurveyNorthMm, UnitTypeId.Millimeters));
            sp.get_Parameter(BuiltInParameter.BASEPOINT_ELEVATION_PARAM)
              .Set(UnitUtils.ConvertToInternalUnits(trade.SurveyElevMm, UnitTypeId.Millimeters));
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

        private static void FillSheetCoverParameters(
            Document doc,
            string locationName,
            ProcessLog log,
            TradeProfile trade,
            Dictionary<string, string>? elementParams,
            Dictionary<string, string>? sheetParams,
            Dictionary<string, string> builtInTokens,
            ExcelFormulaEngine.TableLookupFunc? tableLookup)
        {
            string coverName = string.IsNullOrWhiteSpace(trade.SheetCoverName) ? "Sheet Cover" : trade.SheetCoverName;
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.Name.IndexOf(coverName, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (sheets.Count == 0)
            {
                log.Warning(locationName, $"No '{coverName}' sheet found — coordinate params skipped.");
                return;
            }

            IList<ElementId> allRevisionIds = Revision.GetAllRevisionIds(doc);
            var pbp = GetBasePoint(doc, BuiltInCategory.OST_ProjectBasePoint);
            var svp = GetBasePoint(doc, BuiltInCategory.OST_SharedBasePoint);

            if (pbp == null || svp == null)
            {
                log.Warning(locationName, "Base Point or Survey Point not found — sheet cover params skipped.");
                return;
            }

            using var tx = StartSilentTransaction(doc, "Fill Sheet Cover Revisions & Coordinates");

            if (allRevisionIds.Count > 0)
            {
                foreach (ViewSheet sheet in sheets)
                {
                    ICollection<ElementId> current = sheet.GetAdditionalRevisionIds();
                    bool added = false;
                    foreach (ElementId revId in allRevisionIds)
                    {
                        if (!current.Contains(revId))
                        {
                            current.Add(revId);
                            added = true;
                        }
                    }
                    if (added) sheet.SetAdditionalRevisionIds(current);
                }
            }

            double pbpN = ToMm(pbp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
            double pbpE = ToMm(pbp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
            double pbpZ = ToMm(pbp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM);
            double svpN = ToMm(svp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
            double svpE = ToMm(svp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
            double svpZ = ToMm(svp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM);

            var coverRules = trade.ParameterRules
                .Where(r => r.Enabled && r.Target == ParameterRuleTarget.SheetCover)
                .ToList();

            foreach (var sheet in sheets)
            {
                SetDouble(sheet, "DAR_PBP_N", pbpN);
                SetDouble(sheet, "DAR_PBP_E", pbpE);
                SetDouble(sheet, "DAR_PBP_Z", pbpZ);
                SetDouble(sheet, "DAR_SVP_N", svpN);
                SetDouble(sheet, "DAR_SVP_E", svpE);
                SetDouble(sheet, "DAR_SVP_Z", svpZ);
                SetString(sheet, "CPK_Building Location", locationName);

                if (coverRules.Count > 0)
                {
                    var matchingRules = coverRules.Where(r => MatchesElementRuleFilter(doc, sheet, null, r.Filter)).ToList();
                    if (matchingRules.Count > 0)
                    {
                        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            { "DAR_PBP_N", pbpN.ToString(CultureInfo.InvariantCulture) },
                            { "DAR_PBP_E", pbpE.ToString(CultureInfo.InvariantCulture) },
                            { "DAR_PBP_Z", pbpZ.ToString(CultureInfo.InvariantCulture) },
                            { "DAR_SVP_N", svpN.ToString(CultureInfo.InvariantCulture) },
                            { "DAR_SVP_E", svpE.ToString(CultureInfo.InvariantCulture) },
                            { "DAR_SVP_Z", svpZ.ToString(CultureInfo.InvariantCulture) },
                            { "Sheet Number", sheet.SheetNumber },
                            { "SheetNumber", sheet.SheetNumber },
                            { "Sheet Name", sheet.Name },
                            { "SheetName", sheet.Name }
                        };
                        var resolved = ExcelFormulaEngine.ResolveRulesInOrder(
                            matchingRules,
                            key => LookupCombinedValue(key, extra, sheetParams, elementParams, sheet, null, builtInTokens),
                            tableLookup,
                            out var warnings);
                        foreach (var w in warnings) log.Warning(locationName, w);
                        foreach (var kv in resolved)
                        {
                            TryApplyRuleParam(sheet, kv.Key, kv.Value.Value, kv.Value.Rule.Mode);
                        }
                    }
                }
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
            Dictionary<string, string>? sheetParams,
            Dictionary<string, string>? elementParams,
            ProcessLog log,
            TradeProfile trade,
            string effProjNum,
            string effOrig,
            Dictionary<string, string> builtInTokens,
            ExcelFormulaEngine.TableLookupFunc? tableLookup = null)
        {
            var mergedProjectData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (projectData != null)
            {
                foreach (var kv in projectData) mergedProjectData[kv.Key] = kv.Value;
            }
            if (trade.ProjectInfoData != null)
            {
                foreach (var kv in trade.ProjectInfoData) mergedProjectData[kv.Key] = kv.Value;
            }
            if (!string.IsNullOrWhiteSpace(effProjNum)) mergedProjectData["Project Number"] = effProjNum;
            if (!string.IsNullOrWhiteSpace(effOrig)) mergedProjectData["Author"] = effOrig;

            string buildingName = string.Empty;

            if (sheetParams != null)
            {
                if (sheetParams.TryGetValue("Building Name", out var bName) ||
                    sheetParams.TryGetValue("BuildingName", out bName))
                {
                    buildingName = bName;
                }
            }

            string workPackage = ResolveWorkPackage(sheetParams);

            if (string.IsNullOrEmpty(buildingName))
            {
                try { buildingName = LocatorLookup.GetName(locationName); } catch { }
            }

            string rvtFileName = trade.FormatFileName(trade.RvtFileNameTemplate, effProjNum, effOrig, locationName);

            var dynamic = new List<(string, object)>
            {
                ("Building Name",   buildingName),
                ("Building_Code",   locationName),
                ("CPK_WorkPackage", workPackage),
                ("Work_Package",    workPackage),
                ("File_Name",       rvtFileName)
            };
            updater.UpdateProjectInformation(doc, mergedProjectData, dynamic);

            var piRules = trade.ParameterRules
                .Where(r => r.Enabled && r.Target == ParameterRuleTarget.ProjectInfo)
                .ToList();

            if (piRules.Count > 0 && doc.ProjectInformation != null)
            {
                var dynDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in dynamic) dynDict[k] = Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;

                var resolved = ExcelFormulaEngine.ResolveRulesInOrder(
                    piRules,
                    key => LookupCombinedValue(key, mergedProjectData, dynDict, sheetParams, doc.ProjectInformation, null, builtInTokens, elementParams),
                    tableLookup,
                    out var warnings);
                foreach (var w in warnings) log.Warning(locationName, w);

                if (resolved.Count > 0)
                {
                    using var tx = StartSilentTransaction(doc, "Apply Trade ProjectInfo Rules");
                    foreach (var kv in resolved)
                    {
                        TryApplyRuleParam(doc.ProjectInformation, kv.Key, kv.Value.Value, kv.Value.Rule.Mode);
                    }
                    tx.Commit();
                }
            }
        }

        private static bool IsElementRuleTarget(ParameterRuleTarget t) =>
            t == ParameterRuleTarget.Element ||
            t == ParameterRuleTarget.ElementInstance ||
            t == ParameterRuleTarget.ElementType ||
            t == ParameterRuleTarget.ElementBoth;

        private static readonly HashSet<string> InstanceVaryingMetaTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "Level", "Workset", "PhaseCreated", "Phase", "ElementId", "RoomName", "RoomNumber", "SpaceName"
        };

        private static bool HasInstanceVaryingTokens(
            ExcelFormulaEngine.CompiledRuleSet compiled,
            IList<ParameterRule> rules,
            Dictionary<string, string>? csvParamSet,
            Dictionary<string, string> builtInTokens,
            Element representative)
        {
            if (compiled.AllReferencedTokens.Count == 0) return false;

            var ruleOutputNames = new HashSet<string>(
                rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.ParameterName))
                     .Select(r => r.ParameterName.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (string tok in compiled.AllReferencedTokens)
            {
                if (InstanceVaryingMetaTokens.Contains(tok))
                    return true;

                // Self-referencing instance rule or existing instance parameter override must read per instance
                if ((csvParamSet == null || !csvParamSet.ContainsKey(tok)) &&
                    !builtInTokens.ContainsKey(tok) &&
                    !ruleOutputNames.Contains(tok) &&
                    representative.LookupParameter(tok) != null)
                {
                    return true;
                }

                // Rule references its own prior instance value
                if (ruleOutputNames.Contains(tok) && representative.LookupParameter(tok) != null)
                {
                    foreach (var r in rules)
                    {
                        if (r.Enabled && string.Equals(r.ParameterName.Trim(), tok, StringComparison.OrdinalIgnoreCase))
                        {
                            var rRefs = ExcelFormulaEngine.ExtractReferences(r.ValueOrFormula);
                            if (rRefs.Any(rr => string.Equals(rr, tok, StringComparison.OrdinalIgnoreCase)))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void FillCpkParameters(
            Document doc,
            string locationName,
            Dictionary<string, string>? csvParamSet,
            ProcessLog log,
            TradeProfile trade,
            ProjectSettings settings,
            Dictionary<string, string> builtInTokens,
            ExcelFormulaEngine.TableLookupFunc? tableLookup)
        {
            var categoryFilter = trade.GetCategoryFilter();
            if (categoryFilter == null)
            {
                log.Warning(locationName, $"No target categories configured for trade '{trade.TradeName}' — CPK parameter update skipped.");
                return;
            }

            var elementRules = trade.ParameterRules
                .Where(r => r.Enabled && IsElementRuleTarget(r.Target))
                .ToList();

            using var tx = StartSilentTransaction(doc, "Fill CPK Parameters");

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

            int instWrites = 0, typeWrites = 0, failures = 0;

            if (csvParamSet is { Count: > 0 })
            {
                foreach (var (type, instances) in groups.Values)
                {
                    Element representative = instances[0];
                    foreach (var kvp in csvParamSet)
                    {
                        var instAccessor = FastParamAccessor.Resolve(representative, kvp.Key);
                        var repParam = instAccessor.Get(representative);
                        if (repParam != null && !repParam.IsReadOnly)
                        {
                            foreach (Element inst in instances)
                            {
                                var p = instAccessor.Get(inst);
                                if (p == null || p.IsReadOnly) continue;
                                try { if (SetParamValueIfChanged(p, kvp.Value)) instWrites++; }
                                catch { failures++; }
                            }
                            continue;
                        }

                        var typeParam = type.LookupParameter(kvp.Key);
                        if (typeParam != null && !typeParam.IsReadOnly)
                        {
                            try { if (SetParamValueIfChanged(typeParam, kvp.Value)) typeWrites++; }
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
                        try { if (SetParamValueIfChanged(p, kvp.Value)) instWrites++; }
                        catch { failures++; }
                    }
                }

                log.Info(locationName,
                    $"CPK params (CSV, {csvParamSet.Count} column(s)): {instWrites} instance value(s), {typeWrites} type value(s) updated.");
            }
            else if (elementRules.Count == 0)
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

            // Apply Trade Profile Parameter & Formula Rules (Element scopes with AST compile + constant hoisting + GUID cache)
            if (elementRules.Count > 0)
            {
                int ruleInstWrites = 0, ruleTypeWrites = 0;
                var loggedWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (type, instances) in groups.Values)
                {
                    Element representative = instances[0];
                    var matchingRules = elementRules
                        .Where(r => MatchesElementRuleFilter(doc, representative, type, r.Filter))
                        .ToList();

                    if (matchingRules.Count == 0) continue;

                    var compiled = ExcelFormulaEngine.CompileRules(matchingRules);
                    var repResolved = compiled.EvaluateMap(
                        key => LookupCombinedValue(key, csvParamSet, null, null, representative, type, builtInTokens),
                        out var repWarnings,
                        tableLookup);
                    foreach (var w in repWarnings)
                    {
                        if (loggedWarnings.Add(w)) log.Warning(locationName, w);
                    }

                    var instRuleTargets = new List<(string ParamName, FastParamAccessor Accessor)>();
                    var instRulesSubset = new List<ParameterRule>();

                    foreach (var r in matchingRules)
                    {
                        string pName = r.ParameterName.Trim();
                        if (string.IsNullOrEmpty(pName)) continue;

                        bool hasRepValue = repResolved.TryGetValue(pName, out var rv);
                        bool targetType = r.Target == ParameterRuleTarget.ElementType || r.Target == ParameterRuleTarget.ElementBoth;
                        bool targetInst = r.Target == ParameterRuleTarget.ElementInstance || r.Target == ParameterRuleTarget.ElementBoth;

                        if (r.Target == ParameterRuleTarget.Element)
                        {
                            var repP = representative.LookupParameter(pName);
                            if (repP != null && !repP.IsReadOnly)
                                targetInst = true;
                            else
                                targetType = true;
                        }

                        if (targetType && hasRepValue)
                        {
                            try
                            {
                                if (TryApplyRuleParam(type, pName, rv.Value, rv.Rule.Mode))
                                    ruleTypeWrites++;
                            }
                            catch { failures++; }
                        }

                        if (targetInst)
                        {
                            var acc = FastParamAccessor.Resolve(representative, pName);
                            var pSample = acc.Get(representative);
                            if (pSample != null && !pSample.IsReadOnly)
                            {
                                instRulesSubset.Add(r);
                                if (!instRuleTargets.Any(x => x.ParamName.Equals(pName, StringComparison.OrdinalIgnoreCase)))
                                    instRuleTargets.Add((pName, acc));
                            }
                        }
                    }

                    if (instRulesSubset.Count == 0) continue;

                    bool hoistable = !HasInstanceVaryingTokens(compiled, matchingRules, csvParamSet, builtInTokens, representative) &&
                                     !matchingRules.Any(r => r.HasCondition);

                    if (hoistable)
                    {
                        foreach (Element inst in instances)
                        {
                            foreach (var (pName, acc) in instRuleTargets)
                            {
                                if (!repResolved.TryGetValue(pName, out var rv)) continue;
                                try
                                {
                                    var p = acc.Get(inst);
                                    if (TryApplyRuleParam(inst, pName, rv.Value, rv.Rule.Mode, p))
                                        ruleInstWrites++;
                                }
                                catch { failures++; }
                            }
                        }
                    }
                    else
                    {
                        foreach (Element inst in instances)
                        {
                            var instResolved = compiled.EvaluateMap(
                                key => LookupCombinedValue(key, csvParamSet, null, null, inst, type, builtInTokens),
                                out var instWarnings,
                                tableLookup);
                            foreach (var w in instWarnings)
                            {
                                if (loggedWarnings.Add(w)) log.Warning(locationName, w);
                            }

                            foreach (var (pName, acc) in instRuleTargets)
                            {
                                if (!instResolved.TryGetValue(pName, out var rv)) continue;
                                try
                                {
                                    var p = acc.Get(inst);
                                    if (TryApplyRuleParam(inst, pName, rv.Value, rv.Rule.Mode, p))
                                        ruleInstWrites++;
                                }
                                catch { failures++; }
                            }
                        }
                    }
                }

                foreach (Element orphan in orphans)
                {
                    var matchingRules = elementRules
                        .Where(r => r.Target != ParameterRuleTarget.ElementType &&
                                    MatchesElementRuleFilter(doc, orphan, null, r.Filter))
                        .ToList();
                    if (matchingRules.Count == 0) continue;

                    var orphanResolved = ExcelFormulaEngine.ResolveRulesInOrder(
                        matchingRules,
                        key => LookupCombinedValue(key, csvParamSet, null, null, orphan, null, builtInTokens),
                        tableLookup,
                        out var orphanWarnings);
                    foreach (var w in orphanWarnings)
                    {
                        if (loggedWarnings.Add(w)) log.Warning(locationName, w);
                    }

                    foreach (var kv in orphanResolved)
                    {
                        try
                        {
                            if (TryApplyRuleParam(orphan, kv.Key, kv.Value.Value, kv.Value.Rule.Mode))
                                ruleInstWrites++;
                        }
                        catch { failures++; }
                    }
                }

                log.Info(locationName,
                    $"Trade parameter rules ({elementRules.Count} active): {ruleInstWrites} instance value(s), {ruleTypeWrites} type value(s) updated.");
            }

            if (failures > 0)
                log.Warning(locationName, $"{failures} value(s) failed to convert and were skipped.");

            tx.Commit();
        }

        // ════════════════════════════════════════════════════════════════════
        //  STEP 5 – PURGE UNUSED
        // ════════════════════════════════════════════════════════════════════

        private static void PurgeUnused(Document doc, string locationName, ProcessLog log)
        {
            const int MaxPurgePasses = 5;
            int totalPurged = 0;
            try
            {
                using var t = StartSilentTransaction(doc, "Purge Unused");

                ISet<ElementId> unusedIds = doc.GetUnusedElements(new HashSet<ElementId>());
                HashSet<ElementId>? previousUnused = null;
                int iterations = 0;

                while (unusedIds.Count > 0 && iterations < MaxPurgePasses)
                {
                    if (previousUnused != null && previousUnused.SetEquals(unusedIds))
                        break;

                    var deleted = doc.Delete(unusedIds);
                    if (deleted.Count == 0)
                        break;

                    totalPurged += unusedIds.Count;
                    iterations++;
                    previousUnused = new HashSet<ElementId>(unusedIds);
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
            string effProjNum,
            string effOrig,
            ExportExecutionOptions options)
        {
            Dictionary<string, ElementId> exportViewIds = CreateExportViews(doc, locationName, log);

            try
            {
                // ── IFC Export ─────────────────────────────────────────────
                if (options.ExportIfc)
                {
                    string ifcJsonPath = trade.ResolveIfcJsonPath();
                    if (File.Exists(ifcJsonPath))
                    {
                        string ifcFolder = Path.Combine(exportRoot, "IFC");
                        Directory.CreateDirectory(ifcFolder);

                        string ifcFileName = trade.FormatFileName(trade.IfcFileNameTemplate, effProjNum, effOrig, locationName);
                        string ifcFullPath = Path.Combine(ifcFolder, ifcFileName + ".ifc");

                        IFCExportOptions ifcOpts;
                        using (var txIfc = new Transaction(doc, $"IFC Export Setup – {locationName}"))
                        {
                            txIfc.Start();
                            ifcOpts = IFCJsonLoader.Load(ifcJsonPath, doc, locationName);

                            if (exportViewIds.TryGetValue("IFC", out ElementId ifcViewId) && ifcViewId != ElementId.InvalidElementId)
                                ifcOpts.FilterViewId = ifcViewId;

                            doc.Export(ifcFolder, ifcFileName, ifcOpts);
                            txIfc.Commit();
                        }
                        log.Info(locationName, $"IFC exported → {ifcFullPath}");
                    }
                    else
                    {
                        log.Warning(locationName, $"IFCSettings.json not found at '{ifcJsonPath}' — IFC export skipped.");
                    }
                }

                // ── NWC Export ─────────────────────────────────────────────
                if (options.ExportNwc)
                {
                    if (!exportViewIds.TryGetValue("NWC", out ElementId nwcViewId) || nwcViewId == ElementId.InvalidElementId)
                        throw new InvalidOperationException($"[{locationName}] NWC export view could not be created — export aborted.");

                    string nwcFolder = Path.Combine(exportRoot, "NWC");
                    Directory.CreateDirectory(nwcFolder);

                    string nwcFileName = trade.FormatFileName(trade.NwcFileNameTemplate, effProjNum, effOrig, locationName);
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
        //  Effective values per sheet = Sheet_Params (common, per location)
        //                               overridden by Sheet_Specific_Params (per Sheet Number)
        // ════════════════════════════════════════════════════════════════════

        private static List<ViewSheet> UpdatePlSheets(
            Document doc,
            string locationName,
            Dictionary<string, string>? paramSet,
            Dictionary<string, string>? elementParams,
            LocationParameterBundle? paramBundle,
            ProcessLog log,
            TradeProfile trade,
            Dictionary<string, string> builtInTokens,
            ExcelFormulaEngine.TableLookupFunc? tableLookup,
            string? nativeModelLocatorOverride = null,
            bool alreadyPreCleaned = false)
        {
            string nativeModelLocator = !string.IsNullOrWhiteSpace(nativeModelLocatorOverride)
                ? nativeModelLocatorOverride!.Trim()
                : string.Empty;

            if (string.IsNullOrEmpty(nativeModelLocator))
            {
                var titleParts = doc.Title.Split('-');
                if (titleParts.Length < 4)
                {
                    log.Warning(locationName, $"Document title '{doc.Title}' does not follow hyphenated convention (expected >= 4 segments). Sheet processing skipped.");
                    return new List<ViewSheet>();
                }
                nativeModelLocator = titleParts[3].Trim();
            }

            string includeToken = trade.SheetIncludeToken ?? string.Empty;

            var sheetRules = trade.ParameterRules
                .Where(r => r.Enabled && r.Target == ParameterRuleTarget.Sheet)
                .ToList();
            var tbRules = trade.ParameterRules
                .Where(r => r.Enabled && r.Target == ParameterRuleTarget.Titleblock)
                .ToList();

            // ── 1. Delete obsolete sheets (skipped if PreCleanDocument already ran) ──
            if (!alreadyPreCleaned)
            {
                DeleteObsoleteSheets(doc, nativeModelLocator, locationName, log, trade);
            }

            // ── 2. Collect template sheets matching nativeModelLocator ──
            var plSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s =>
                {
                    string[] parts = s.SheetNumber.Split('-');
                    return parts.Length >= 4 && parts[3].Equals(nativeModelLocator, StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (plSheets.Count == 0)
            {
                log.Warning(locationName, $"No template sheets matching native locator '{nativeModelLocator}' were found in the model.");
                return plSheets;
            }

            // Pre-group ALL titleblocks in document once instead of running a FilteredElementCollector per sheet
            var titleBlocksBySheet = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .Where(tb => tb.OwnerViewId != ElementId.InvalidElementId)
                .GroupBy(tb => tb.OwnerViewId)
                .ToDictionary(g => g.Key, g => g.ToList());

            using var tx = StartSilentTransaction(doc, $"Update PL Sheets for {locationName}");

            int renamedCount = 0;
            int tbUpdatedCount = 0;
            int specificHits = 0;
            var unmatchedSpecificKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sheet in plSheets)
            {
                var typeWpAssigned = new Dictionary<ElementId, int>();

                string originalNum = sheet.SheetNumber;   // capture BEFORE rename (fallback lookup key)
                try
                {
                    // 1. Rename Sheet Number: replace segment 3 (the locator code) with target locationName
                    string[] parts = originalNum.Split('-');
                    if (parts.Length >= 4)
                    {
                        parts[3] = locationName;
                        string newNum = string.Join("-", parts);
                        if (!string.Equals(originalNum, newNum, StringComparison.OrdinalIgnoreCase))
                        {
                            sheet.SheetNumber = newNum;
                            renamedCount++;
                        }
                    }
                    else
                    {
                        log.Warning(locationName, $"Sheet '{originalNum}' does not have standard hyphenated format (expected >= 4 segments).");
                    }

                    // 2. Effective parameters = common (Sheet_Params) + per-sheet overrides (Sheet_Specific_Params)
                    var specific = paramBundle?.GetSheetNumberParams(sheet.SheetNumber, originalNum);
                    if (specific != null) specificHits++;

                    var effective = MergeParams(paramSet, specific);
                    string sheetWp = ResolveWorkPackage(effective);

                    // 3. Sheet Name: explicit 'Sheet Name' column > Work Package (previous behaviour)
                    string? desiredName = null;
                    if (effective != null && effective.TryGetValue("Sheet Name", out var sn) && !string.IsNullOrWhiteSpace(sn))
                        desiredName = sn.Trim();
                    else if (!string.IsNullOrEmpty(sheetWp))
                        desiredName = sheetWp;

                    if (desiredName != null && !string.Equals(sheet.Name, desiredName, StringComparison.Ordinal))
                        sheet.Name = desiredName;

                    var appliedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // 4. Titleblock instances pre-grouped for this sheet
                    if (!titleBlocksBySheet.TryGetValue(sheet.Id, out var titleBlocks))
                        titleBlocks = new List<Element>();

                    string[] sheetSegs = sheet.SheetNumber.Split('-');
                    string sysCode = sheetSegs.Length >= 3 ? sheetSegs[2].Trim() : string.Empty;
                    string lvlCode = sheetSegs.Length >= 5 ? sheetSegs[4].Trim() : string.Empty;
                    string drwSeq = sheetSegs.Length >= 7 ? sheetSegs[6].Trim() : string.Empty;

                    string currRev = string.Empty;
                    string currRevDate = string.Empty;
                    ElementId currRevId = sheet.GetCurrentRevision();
                    if (currRevId != ElementId.InvalidElementId && doc.GetElement(currRevId) is Revision revObj)
                    {
                        currRev = revObj.RevisionNumber ?? string.Empty;
                        currRevDate = revObj.RevisionDate ?? string.Empty;
                    }

                    var sheetMetaTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "Sheet Number",        sheet.SheetNumber },
                        { "SheetNumber",         sheet.SheetNumber },
                        { "Sheet Name",          sheet.Name },
                        { "SheetName",           sheet.Name },
                        { "WorkPackage",         sheetWp },
                        { "Work_Package",        sheetWp },
                        { "DisciplineCode",      sysCode },
                        { "SystemCode",          sysCode },
                        { "LevelCode",           lvlCode },
                        { "DrawingSeq",          drwSeq },
                        { "CurrentRevision",     currRev },
                        { "CurrentRevisionDate", currRevDate }
                    };

                    foreach (var tb in titleBlocks)
                    {
                        // 4a. Work Package checkboxes: ON for this sheet's WP, OFF for all others
                        if (trade.SyncWorkPackageCheckboxes)
                        {
                            if (!string.IsNullOrEmpty(sheetWp))
                            {
                                SyncWorkPackageCheckboxes(doc, tb, sheetWp, sheet.SheetNumber, locationName, log, typeWpAssigned);
                            }
                            else
                            {
                                log.Warning(locationName,
                                    $"[{sheet.SheetNumber}] No Work_Package resolved (Sheet_Params / Sheet_Specific_Params) — native WP checkbox left as-is.");
                            }
                        }

                        // 4b. Map effective values onto Titleblock
                        if (effective != null)
                        {
                            foreach (var kvp in effective)
                            {
                                if (ReservedSheetKeys.Contains(kvp.Key)) continue;
                                if (TryApplyParam(tb, kvp.Key, kvp.Value)) appliedKeys.Add(kvp.Key);
                            }
                        }

                        // 4c. Apply Trade Profile Titleblock rules
                        if (tbRules.Count > 0)
                        {
                            ElementType? tbType = doc.GetElement(tb.GetTypeId()) as ElementType;
                            var matchingTb = tbRules.Where(r => MatchesElementRuleFilter(doc, tb, tbType, r.Filter)).ToList();
                            if (matchingTb.Count > 0)
                            {
                                var resolvedTb = ExcelFormulaEngine.ResolveRulesInOrder(
                                    matchingTb,
                                    key => LookupCombinedValue(key, sheetMetaTokens, effective, elementParams, tb, tbType, builtInTokens, null, sheet),
                                    tableLookup,
                                    out var tbWarnings);
                                foreach (var w in tbWarnings) log.Warning(locationName, w);
                                foreach (var kv in resolvedTb)
                                {
                                    if (TryApplyRuleParam(tb, kv.Key, kv.Value.Value, kv.Value.Rule.Mode))
                                        appliedKeys.Add(kv.Key);
                                }
                            }
                        }

                        tbUpdatedCount++;
                    }

                    // 5. Map effective values onto Sheet
                    if (effective != null)
                    {
                        foreach (var kvp in effective)
                        {
                            if (ReservedSheetKeys.Contains(kvp.Key)) continue;
                            if (TryApplyParam(sheet, kvp.Key, kvp.Value)) appliedKeys.Add(kvp.Key);
                        }
                    }

                    // 5b. Apply Trade Profile Sheet rules
                    if (sheetRules.Count > 0)
                    {
                        var matchingSheetRules = sheetRules.Where(r => MatchesElementRuleFilter(doc, sheet, null, r.Filter)).ToList();
                        if (matchingSheetRules.Count > 0)
                        {
                            var resolvedSheet = ExcelFormulaEngine.ResolveRulesInOrder(
                                matchingSheetRules,
                                key => LookupCombinedValue(key, sheetMetaTokens, effective, elementParams, sheet, null, builtInTokens),
                                tableLookup,
                                out var sWarnings);
                            foreach (var w in sWarnings) log.Warning(locationName, w);
                            foreach (var kv in resolvedSheet)
                            {
                                if (kv.Key.Equals("Sheet Name", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!string.IsNullOrWhiteSpace(kv.Value.Value) &&
                                        (kv.Value.Rule.Mode == ParameterFillMode.Overwrite || string.IsNullOrWhiteSpace(sheet.Name)))
                                    {
                                        sheet.Name = kv.Value.Value;
                                        appliedKeys.Add(kv.Key);
                                    }
                                    continue;
                                }
                                if (TryApplyRuleParam(sheet, kv.Key, kv.Value.Value, kv.Value.Rule.Mode))
                                    appliedKeys.Add(kv.Key);
                            }
                        }
                    }

                    // 6. Track per-sheet columns that matched nothing (header ≠ Revit parameter name)
                    if (specific != null)
                    {
                        foreach (var k in specific.Keys)
                        {
                            if (!ReservedSheetKeys.Contains(k) && !appliedKeys.Contains(k))
                                unmatchedSpecificKeys.Add(k);
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Warning(locationName, $"Failed updating sheet '{originalNum}': {ex.Message}");
                }
            }

            // 7. Apply opt-in per-trade TextNote replacement rules across updated sheets/views/legends
            if (trade.TextNoteRules.Any(r => r.Enabled))
            {
                TextNoteRuleProcessor.ApplyToSheets(
                    doc, plSheets, trade.TextNoteRules, nativeModelLocator,
                    locationName, paramSet, elementParams, builtInTokens, log);
            }

            tx.Commit();

            log.Info(locationName,
                $"Updated {plSheets.Count} sheet(s): {renamedCount} renamed, {tbUpdatedCount} titleblock(s) synced, " +
                $"{specificHits} sheet(s) received sheet-specific values.");

            if (unmatchedSpecificKeys.Count > 0)
            {
                log.Warning(locationName,
                    $"Sheet-specific column(s) not found (or read-only) on sheet/titleblock: {string.Join(", ", unmatchedSpecificKeys)}");
            }

            // Spreadsheet rows for this location that matched no sheet (typo in Sheet Number?)
            if (paramBundle != null && paramBundle.SheetNumberParameters.Count > 0)
            {
                var processed = new HashSet<string>(plSheets.Select(s => s.SheetNumber), StringComparer.OrdinalIgnoreCase);
                var orphanRows = paramBundle.SheetNumberParameters.Keys
                    .Where(k =>
                    {
                        string[] p = k.Split('-');
                        return p.Length >= 4
                            && p[3].Equals(locationName, StringComparison.OrdinalIgnoreCase)
                            && !processed.Contains(k);
                    })
                    .ToList();

                if (orphanRows.Count > 0)
                    log.Warning(locationName, $"Sheet_Specific_Params row(s) with no matching sheet: {string.Join(", ", orphanRows)}");
            }

            if (string.IsNullOrWhiteSpace(includeToken))
                return plSheets;

            return plSheets.Where(s => s.SheetNumber.IndexOf(includeToken, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }


        private static void EnforceModelStandards(
            Document doc, ModelPath rvtPath, string locationName,
            Dictionary<string, string>? elementParams, Dictionary<string, string>? sheetParams,
            LocationParameterBundle? paramBundle, string exportRoot, ProcessLog log,
            TradeProfile trade, string effProjNum, string effOrig, ExportExecutionOptions options)
        {
            string mcFolder = trade.ResolveModelCheckersFolder();
            var rules = ModelCheckerSetLoader.GetForTrade(trade.TradeCode, log, mcFolder);
            if (rules.Count == 0)
            {
                log.Info(locationName, $"No Model Checker sets in '{mcFolder}' (_ALL / {trade.TradeCode}) — standards step skipped.");
                return;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (elementParams != null) foreach (var kv in elementParams) values[kv.Key] = kv.Value;
            if (sheetParams != null) foreach (var kv in sheetParams) if (!values.ContainsKey(kv.Key)) values[kv.Key] = kv.Value;
            values["Locator Code"] = locationName;

            string reportFolder = !string.IsNullOrWhiteSpace(exportRoot)
                ? exportRoot
                : Path.GetDirectoryName(ModelPathUtils.ConvertModelPathToUserVisiblePath(rvtPath)) ?? string.Empty;

            var ctx = new StandardsContext
            {
                LocationCode = locationName,
                RvtFileName = trade.FormatFileName(trade.RvtFileNameTemplate, effProjNum, effOrig, locationName),
                ReportFolder = reportFolder,
                ApplyFixes = !options.ModelStandardsDryRun,
                LocationValues = values,
                FillRows = paramBundle?.StandardsFillRows ?? new List<StandardsFillRow>()
            };

            ModelStandardsEnforcer.Run(doc, rules, ctx, log, SetParamValue);   // reuses your SetParamValue
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
            if (Matches(841, 594)) return (ExportPaperFormat.ISO_A1, orientation);
            if (Matches(594, 420)) return (ExportPaperFormat.ISO_A2, orientation);
            if (Matches(420, 297)) return (ExportPaperFormat.ISO_A3, orientation);
            if (Matches(297, 210)) return (ExportPaperFormat.ISO_A4, orientation);

            if (Matches(1000, 707)) return (ExportPaperFormat.ISO_B1, orientation);
            if (Matches(707, 500)) return (ExportPaperFormat.ISO_B2, orientation);
            if (Matches(500, 353)) return (ExportPaperFormat.ISO_B3, orientation);
            if (Matches(353, 250)) return (ExportPaperFormat.ISO_B4, orientation);

            // Architectural Formats
            if (Matches(1219, 914)) return (ExportPaperFormat.ARCH_E, orientation);
            if (Matches(1067, 762)) return (ExportPaperFormat.ARCH_E1, orientation);
            if (Matches(914, 610)) return (ExportPaperFormat.ARCH_D, orientation);
            if (Matches(610, 457)) return (ExportPaperFormat.ARCH_C, orientation);
            if (Matches(457, 305)) return (ExportPaperFormat.ARCH_B, orientation);
            if (Matches(305, 229)) return (ExportPaperFormat.ARCH_A, orientation);

            // ANSI Formats
            if (Matches(1118, 864)) return (ExportPaperFormat.ANSI_E, orientation);
            if (Matches(864, 559)) return (ExportPaperFormat.ANSI_D, orientation);
            if (Matches(559, 432)) return (ExportPaperFormat.ANSI_C, orientation);
            if (Matches(432, 279)) return (ExportPaperFormat.ANSI_B, orientation);
            if (Matches(279, 216)) return (ExportPaperFormat.ANSI_A, orientation);

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
            string effProjNum,
            string effOrig)
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

            string rvtFileName = trade.FormatFileName(trade.RvtFileNameTemplate, effProjNum, effOrig, locationName) + ".rvt";
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

            View3D? exactNwc = all3D.FirstOrDefault(v => v.Name.Equals("Export-NWC", StringComparison.OrdinalIgnoreCase));
            View3D? exactIfc = all3D.FirstOrDefault(v => v.Name.Equals("Export-IFC", StringComparison.OrdinalIgnoreCase));
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
                        nwcView.Name = "Export-NWC";
                        created["NWC"] = nwcView.Id;
                    }
                }
                catch (Exception ex) { log.Warning(locationName, $"NWC view creation failed: {ex.Message}"); }

                try
                {
                    if (doc.GetElement(sourceView.Duplicate(ViewDuplicateOption.Duplicate)) is View3D ifcView)
                    {
                        ifcView.Name = "Export-IFC";
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

        /// <remarks>
        /// NOTE (unchanged behaviour): this deletes the 'Naviswork' SOURCE view, not the
        /// EXPORT-NWC / EXPORT-IFC duplicates. Verify this is intended.
        /// </remarks>
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
        //  SHEET-PARAMETER MERGE HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Keys handled explicitly (or identifiers) – never written as generic parameters.</summary>
        private static readonly HashSet<string> ReservedSheetKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "Locator Code", "LocatorCode", "Sheet Number", "SheetNumber", "Sheet_Number", "Sheet No", "Sheet Name"
        };

        private static readonly string[] WorkPackageKeys = { "Work_Package", "WorkPackage", "CPK_WorkPackage", "WP" };

        private static string ResolveWorkPackage(Dictionary<string, string>? p)
        {
            if (p == null) return string.Empty;
            foreach (var k in WorkPackageKeys)
            {
                if (p.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return string.Empty;
        }

        // "WP04", "WP4", "WP_04", "WP 04", "WP08_West Landside Zone" → 4 / 8
        private static readonly Regex WpCodeRegex =
            new(@"^\s*WP[\s_\-]*0*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static int? ParseWpNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = WpCodeRegex.Match(text);
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : (int?)null;
        }

        private static bool IsYesNo(Parameter p)
        {
            if (p.StorageType != StorageType.Integer) return false;
            try { return p.Definition.GetDataType() == SpecTypeId.Boolean.YesNo; }
            catch { return true; }   // older/odd definitions: fall back to "integer = yes/no"
        }

        /// <summary>
        /// Turns ON the checkbox matching the sheet's WP and OFF every other WPxx checkbox,
        /// on both titleblock INSTANCE and TYPE parameters.
        /// </summary>
        private static void SyncWorkPackageCheckboxes(
            Document doc,
            Element titleBlock,
            string sheetWp,
            string sheetNumber,
            string locationName,
            ProcessLog log,
            Dictionary<ElementId, int> typeWpAssigned)
        {
            int? targetWp = ParseWpNumber(sheetWp);
            if (targetWp == null)
            {
                log.Warning(locationName,
                    $"[{sheetNumber}] Work package '{sheetWp}' is not in 'WPnn' form — WP checkboxes left unchanged.");
                return;
            }

            var candidates = new List<(Parameter Param, bool IsType)>();
            foreach (Parameter p in titleBlock.Parameters)
                candidates.Add((p, false));

            Element? tbType = doc.GetElement(titleBlock.GetTypeId());
            if (tbType != null)
                foreach (Parameter p in tbType.Parameters)
                    candidates.Add((p, true));

            int found = 0, turnedOn = 0, turnedOff = 0;
            var readOnly = new List<string>();

            foreach (var (p, isType) in candidates)
            {
                if (!IsYesNo(p)) continue;

                string pName = p.Definition.Name.Trim();
                int? paramWp = ParseWpNumber(pName);
                if (paramWp == null) continue;

                found++;
                int desired = paramWp == targetWp ? 1 : 0;

                if (p.IsReadOnly)
                {
                    if (p.AsInteger() != desired)
                        readOnly.Add($"{pName}{(isType ? " (type)" : "")}");
                    continue;
                }

                if (p.AsInteger() != desired)
                {
                    p.Set(desired);
                    if (desired == 1) turnedOn++; else turnedOff++;
                }
            }

            // Type parameters are shared by every sheet using this titleblock type
            if (tbType != null && candidates.Any(c => c.IsType && IsYesNo(c.Param) && ParseWpNumber(c.Param.Definition.Name) != null))
            {
                if (typeWpAssigned.TryGetValue(tbType.Id, out int previousWp) && previousWp != targetWp)
                    log.Warning(locationName,
                        $"[{sheetNumber}] WP checkboxes are TYPE parameters of '{tbType.Name}' — sheet WP{targetWp:00} overrides WP{previousWp:00} set by another sheet. Use separate titleblock types or instance parameters.");
                typeWpAssigned[tbType.Id] = targetWp.Value;
            }

            if (found == 0)
                log.Warning(locationName, $"[{sheetNumber}] No Yes/No 'WPxx' parameters found on titleblock (instance or type).");
            else if (!candidates.Any(c => IsYesNo(c.Param) && ParseWpNumber(c.Param.Definition.Name) == targetWp))
                log.Warning(locationName, $"[{sheetNumber}] Titleblock has no checkbox for WP{targetWp:00} — all WP checkboxes turned off.");

            if (readOnly.Count > 0)
                log.Warning(locationName,
                    $"[{sheetNumber}] Read-only WP checkbox(es) could not be changed (formula-driven or locked): {string.Join(", ", readOnly)}");
        }



        /// <summary>Common values first, then per-sheet values override them.</summary>
        private static Dictionary<string, string>? MergeParams(
            Dictionary<string, string>? common,
            Dictionary<string, string>? specific)
        {
            if (common == null && specific == null) return null;

            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (common != null)
                foreach (var kv in common) merged[kv.Key] = kv.Value;
            if (specific != null)
                foreach (var kv in specific) merged[kv.Key] = kv.Value;
            return merged;
        }

        private readonly struct FastParamAccessor
        {
            public readonly Guid? SharedGuid;
            public readonly BuiltInParameter BuiltIn;
            public readonly string Name;

            private FastParamAccessor(Guid? sharedGuid, BuiltInParameter builtIn, string name)
            {
                SharedGuid = sharedGuid;
                BuiltIn = builtIn;
                Name = name;
            }

            public static FastParamAccessor Resolve(Element sampleElem, string paramName)
            {
                var p = sampleElem.LookupParameter(paramName);
                if (p != null)
                {
                    if (p.IsShared)
                    {
                        try { return new FastParamAccessor(p.GUID, BuiltInParameter.INVALID, paramName); }
                        catch { }
                    }
                    if (p.Definition is InternalDefinition id && id.BuiltInParameter != BuiltInParameter.INVALID)
                        return new FastParamAccessor(null, id.BuiltInParameter, paramName);
                }
                return new FastParamAccessor(null, BuiltInParameter.INVALID, paramName);
            }

            public Parameter? Get(Element e)
            {
                if (SharedGuid.HasValue) return e.get_Parameter(SharedGuid.Value);
                if (BuiltIn != BuiltInParameter.INVALID) return e.get_Parameter(BuiltIn);
                return e.LookupParameter(Name);
            }
        }

        /// <summary>Writes a value if the parameter exists and is editable. "&lt;CLEAR&gt;" writes an empty value.</summary>
        private static bool TryApplyParam(Element e, string name, string raw)
        {
            var p = e.LookupParameter(name);
            if (p == null || p.IsReadOnly) return false;

            string value = raw.Equals("<CLEAR>", StringComparison.OrdinalIgnoreCase) ? string.Empty : raw;
            SetParamValueIfChanged(p, value);
            return true;
        }

        private static bool TryApplyRuleParam(
            Element e,
            string name,
            string raw,
            ParameterFillMode mode,
            Parameter? preResolvedParam = null)
        {
            var p = preResolvedParam ?? e.LookupParameter(name);
            if (p == null || p.IsReadOnly) return false;

            string finalVal;
            switch (mode)
            {
                case ParameterFillMode.Clear:
                    finalVal = string.Empty;
                    break;

                case ParameterFillMode.FillIfEmpty:
                    if (!IsParameterEmpty(p)) return false;
                    finalVal = raw.Equals("<CLEAR>", StringComparison.OrdinalIgnoreCase) ? string.Empty : raw;
                    break;

                case ParameterFillMode.SkipIfBlankResult:
                    if (string.IsNullOrWhiteSpace(raw)) return false;
                    finalVal = raw.Equals("<CLEAR>", StringComparison.OrdinalIgnoreCase) ? string.Empty : raw;
                    break;

                case ParameterFillMode.Append:
                {
                    if (string.IsNullOrEmpty(raw)) return false;
                    string existing = ReadParameterRawString(p);
                    finalVal = string.IsNullOrEmpty(existing) ? raw : existing + raw;
                    break;
                }

                case ParameterFillMode.Prepend:
                {
                    if (string.IsNullOrEmpty(raw)) return false;
                    string existing = ReadParameterRawString(p);
                    finalVal = string.IsNullOrEmpty(existing) ? raw : raw + existing;
                    break;
                }

                default: // Overwrite
                    finalVal = raw.Equals("<CLEAR>", StringComparison.OrdinalIgnoreCase) ? string.Empty : raw;
                    break;
            }

            return SetParamValueIfChanged(p, finalVal);
        }

        private static string ReadParameterRawString(Parameter p)
        {
            if (!p.HasValue) return string.Empty;
            return p.StorageType switch
            {
                StorageType.String => p.AsString() ?? string.Empty,
                StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString(CultureInfo.InvariantCulture),
                _ => string.Empty
            };
        }

        private static bool IsParameterEmpty(Parameter p)
        {
            if (!p.HasValue) return true;
            return p.StorageType switch
            {
                StorageType.String => string.IsNullOrWhiteSpace(p.AsString()),
                StorageType.ElementId => p.AsElementId() == ElementId.InvalidElementId,
                _ => false
            };
        }

        private static bool MatchesElementRuleFilter(Document doc, Element elem, ElementType? type, string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter) || filter!.Trim() == "*")
                return true;

            string bicName = string.Empty;
            string catName = elem.Category?.Name ?? string.Empty;
            if (elem.Category != null)
            {
                try
                {
                    var bic = (BuiltInCategory)elem.Category.Id.Value;
                    bicName = bic.ToString();
                }
                catch { }
            }

            string famName = type?.FamilyName ?? string.Empty;
            string typeName = type?.Name ?? elem.Name ?? string.Empty;
            string famType = string.IsNullOrEmpty(famName) ? typeName : $"{famName} : {typeName}";

            string[] tokens = filter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawToken in tokens)
            {
                string pat = rawToken.Trim();
                if (pat.Length == 0 || pat == "*") return true;

                if (MatchesPattern(bicName, pat) ||
                    MatchesPattern(catName, pat) ||
                    MatchesPattern(famName, pat) ||
                    MatchesPattern(typeName, pat) ||
                    MatchesPattern(famType, pat))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesPattern(string value, string pattern)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0)
            {
                string rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                return Regex.IsMatch(value, rx, RegexOptions.IgnoreCase);
            }
            return value.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                   value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string? LookupCombinedValue(
            string key,
            IDictionary<string, string>? primaryDict,
            IDictionary<string, string>? secondaryDict,
            IDictionary<string, string>? tertiaryDict,
            Element? primaryElem,
            ElementType? elemType,
            IDictionary<string, string>? builtInTokens,
            IDictionary<string, string>? quaternaryDict = null,
            Element? secondaryElem = null)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            if (primaryDict != null && primaryDict.TryGetValue(key, out var v1)) return v1;
            if (secondaryDict != null && secondaryDict.TryGetValue(key, out var v2)) return v2;
            if (tertiaryDict != null && tertiaryDict.TryGetValue(key, out var v3)) return v3;
            if (quaternaryDict != null && quaternaryDict.TryGetValue(key, out var v4)) return v4;

            if (primaryElem != null)
            {
                string? rv = ReadRevitParamAsString(primaryElem, elemType, key);
                if (rv != null) return rv;
            }

            if (secondaryElem != null)
            {
                string? rv2 = ReadRevitParamAsString(secondaryElem, null, key);
                if (rv2 != null) return rv2;
            }

            if (builtInTokens != null && builtInTokens.TryGetValue(key, out var tok)) return tok;

            return null;
        }

        private static string? ReadRevitParamAsString(Element elem, ElementType? type, string paramName)
        {
            if (paramName.Equals("Category", StringComparison.OrdinalIgnoreCase))
                return elem.Category?.Name;
            if (paramName.Equals("Family", StringComparison.OrdinalIgnoreCase))
                return type?.FamilyName;
            if (paramName.Equals("Type", StringComparison.OrdinalIgnoreCase))
                return type?.Name ?? elem.Name;
            if (paramName.Equals("Family and Type", StringComparison.OrdinalIgnoreCase))
                return type != null ? $"{type.FamilyName}: {type.Name}" : elem.Name;
            if (paramName.Equals("ElementId", StringComparison.OrdinalIgnoreCase))
                return elem.Id.Value.ToString(CultureInfo.InvariantCulture);
            if (paramName.Equals("ModelTitle", StringComparison.OrdinalIgnoreCase))
                return elem.Document.Title;
            if (paramName.Equals("Workset", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (elem.WorksetId != WorksetId.InvalidWorksetId)
                        return elem.Document.GetWorksetTable().GetWorkset(elem.WorksetId)?.Name;
                }
                catch { }
                return null;
            }
            if (paramName.Equals("PhaseCreated", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("Phase", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (elem.CreatedPhaseId != ElementId.InvalidElementId)
                        return elem.Document.GetElement(elem.CreatedPhaseId)?.Name;
                }
                catch { }
                return null;
            }
            if (paramName.Equals("RoomName", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("RoomNumber", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (elem is FamilyInstance fi && fi.Room != null)
                        return paramName.Equals("RoomNumber", StringComparison.OrdinalIgnoreCase) ? fi.Room.Number : fi.Room.Name;
                }
                catch { }
                return null;
            }
            if (paramName.Equals("SpaceName", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (elem is FamilyInstance fiSpace && fiSpace.Space != null)
                        return fiSpace.Space.Name;
                }
                catch { }
                return null;
            }

            Parameter? p = elem.LookupParameter(paramName) ?? type?.LookupParameter(paramName);
            if ((p == null || !p.HasValue) && paramName.Equals("Level", StringComparison.OrdinalIgnoreCase))
            {
                if (elem.LevelId != ElementId.InvalidElementId)
                    return elem.Document.GetElement(elem.LevelId)?.Name;
                return null;
            }
            if (p == null || !p.HasValue) return null;

            return p.StorageType switch
            {
                StorageType.String => p.AsString(),
                StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString(CultureInfo.InvariantCulture),
                StorageType.ElementId => p.AsValueString()
                    ?? elem.Document.GetElement(p.AsElementId())?.Name
                    ?? p.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
                _ => null
            };
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
            if (p.HasValue && Math.Abs(p.AsDouble() - val) < 1e-9) return false;
            p.Set(val);
            return true;
        }

        private static bool SetString(Element e, string name, string val)
        {
            var p = e.LookupParameter(name);
            if (p == null || p.IsReadOnly) return false;
            if (string.Equals(p.AsString() ?? string.Empty, val, StringComparison.Ordinal)) return false;
            p.Set(val);
            return true;
        }

        /// Line separator written into Revit multi-line text parameters.
        /// Assumed "\r\n" – verify with the check below and change to "\n" or "\r" if needed.
        private const string RevitLineBreak = "\r\n";

        private static string NormalizeLineBreaks(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            string s = raw
                .Replace("<BR>", "\n").Replace("<br>", "\n")    // token typed in Excel/CSV
                .Replace("\r\n", "\n").Replace("\r", "\n");     // Alt+Enter / CHAR(10) / mixed

            if (s.IndexOf('\n') < 0) return s;                   // single-line → unchanged

            var lines = s.Split('\n')
                         .Select(l => l.Trim())
                         .Where(l => l.Length > 0);              // drop empty lines from trailing breaks
            return string.Join(RevitLineBreak, lines);
        }

        private static readonly Regex UnitSuffixRegex = new(
            @"^\s*([+-]?\d+(?:[.,]\d+)?)\s*(mm|cm|m|ft|in|deg|m2|m3)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool TryParseDoubleWithUnit(string raw, out double internalVal)
        {
            string s = (raw ?? string.Empty).Trim();
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out internalVal))
                return true;

            var m = UnitSuffixRegex.Match(s);
            if (m.Success &&
                double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
            {
                string unit = m.Groups[2].Value.ToLowerInvariant();
                ForgeTypeId? unitId = unit switch
                {
                    "mm" => UnitTypeId.Millimeters,
                    "cm" => UnitTypeId.Centimeters,
                    "m" => UnitTypeId.Meters,
                    "ft" => UnitTypeId.Feet,
                    "in" => UnitTypeId.Inches,
                    "deg" => UnitTypeId.Degrees,
                    "m2" => UnitTypeId.SquareMeters,
                    "m3" => UnitTypeId.CubicMeters,
                    _ => null
                };
                if (unitId != null)
                {
                    internalVal = UnitUtils.ConvertToInternalUnits(num, unitId);
                    return true;
                }
            }

            internalVal = 0;
            return false;
        }

        private static void SetParamValue(Parameter param, string raw) =>
            SetParamValueIfChanged(param, raw);

        private static bool SetParamValueIfChanged(Parameter param, string raw)
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                {
                    string normalized = NormalizeLineBreaks(raw);
                    string current = param.AsString() ?? string.Empty;
                    if (string.Equals(current, normalized, StringComparison.Ordinal))
                        return false;
                    param.Set(normalized);
                    return true;
                }

                case StorageType.Double:
                {
                    if (TryParseDoubleWithUnit(raw, out double d))
                    {
                        if (param.HasValue && Math.Abs(param.AsDouble() - d) < 1e-9)
                            return false;
                        param.Set(d);
                        return true;
                    }
                    return false;
                }

                case StorageType.Integer:
                {
                    string v = (raw ?? string.Empty).Trim();
                    if (v.Equals("Yes", StringComparison.OrdinalIgnoreCase) || v.Equals("True", StringComparison.OrdinalIgnoreCase))
                        v = "1";
                    else if (v.Equals("No", StringComparison.OrdinalIgnoreCase) || v.Equals("False", StringComparison.OrdinalIgnoreCase))
                        v = "0";

                    if (int.TryParse(v, out int i))
                    {
                        if (param.HasValue && param.AsInteger() == i)
                            return false;
                        param.Set(i);
                        return true;
                    }
                    return false;
                }
            }
            return false;
        }

    }
}
