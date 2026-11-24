using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using RevitLocationExporter.Configuration;

namespace RevitLocationExporter.Core
{
    /// <summary>Summary returned after syncing a TradeProfile with an Excel workbook.</summary>
    public sealed class ExcelSyncReport
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ConfigSettingsUpdated { get; set; }
        public int ParameterRulesCount { get; set; }
        public int ElementLocationsCount { get; set; }
        public int SheetLocationsCount { get; set; }
        public int SheetSpecificCount { get; set; }
        public int StandardsFillCount { get; set; }
        public int ExportLocationsCount { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Synchronizes TradeProfile settings, parameter/formula rules, and location data tables
    /// with a linked Master Excel workbook, and exports cleanly formatted Excel templates.
    /// </summary>
    public static class TradeExcelSyncService
    {
        /// <summary>Returns true if the linked Excel file exists and has been modified since the last sync.</summary>
        public static bool IsExcelModifiedSinceSync(TradeProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.LinkedExcelPath))
                return false;

            if (!File.Exists(profile.LinkedExcelPath))
                return false;

            if (profile.LastExcelWriteTimeUtc == null)
                return true;

            DateTime currentWrite = File.GetLastWriteTimeUtc(profile.LinkedExcelPath);
            return currentWrite > profile.LastExcelWriteTimeUtc.Value.AddSeconds(1);
        }

        /// <summary>
        /// Resolves the LocationParameterBundle for a batch export run.
        /// Uses one-off override if supplied; otherwise uses the TradeProfile's linked Excel
        /// (auto-syncing if modified) and cached snapshot.
        /// </summary>
        public static LocationParameterBundle? ResolveBundleForRun(
            TradeProfile profile,
            string? oneOffOverridePath,
            ProcessLog? log = null)
        {
            if (!string.IsNullOrWhiteSpace(oneOffOverridePath))
            {
                if (!File.Exists(oneOffOverridePath))
                {
                    log?.Warning(profile.TradeCode, $"Override Excel file not found: '{oneOffOverridePath}' — falling back to Trade Profile.");
                }
                else
                {
                    try
                    {
                        var overrideBundle = LocationDataSheetReader.ReadParameterBundle(oneOffOverridePath);
                        log?.Info("Config", $"Loaded one-off parameter file '{Path.GetFileName(oneOffOverridePath)}'.");
                        return overrideBundle;
                    }
                    catch (Exception ex)
                    {
                        log?.Warning("Config", $"Failed to read override file '{oneOffOverridePath}': {ex.Message}");
                    }
                }
            }

            if (profile.AutoSyncExcelOnRun &&
                !string.IsNullOrWhiteSpace(profile.LinkedExcelPath) &&
                File.Exists(profile.LinkedExcelPath))
            {
                if (profile.SyncedData == null || IsExcelModifiedSinceSync(profile))
                {
                    var report = SyncFromExcel(profile, profile.LinkedExcelPath);
                    if (report.Success)
                    {
                        log?.Info(profile.TradeCode,
                            $"Auto-synced from '{Path.GetFileName(profile.LinkedExcelPath)}': {report.Message}");
                        try { TradeConfigManager.Instance.SaveToJson(); } catch { }
                    }
                    else
                    {
                        log?.Warning(profile.TradeCode,
                            $"Auto-sync warning for '{Path.GetFileName(profile.LinkedExcelPath)}': {report.Message}");
                    }
                }
                else
                {
                    log?.Info(profile.TradeCode,
                        $"Using synced Excel snapshot from '{Path.GetFileName(profile.LinkedExcelPath)}'.");
                }
            }
            else if (profile.SyncedData != null)
            {
                log?.Info(profile.TradeCode, "Using cached Excel snapshot stored in Trade Profile.");
            }

            return profile.SyncedData?.ToLocationParameterBundle(profile.LinkedExcelPath);
        }

        /// <summary>
        /// Reads Trade_Config, Parameter_Rules, and all location/sheet/standards data tabs
        /// from the given Excel workbook into the supplied TradeProfile.
        /// </summary>
        public static ExcelSyncReport SyncFromExcel(TradeProfile profile, string excelPath)
        {
            var report = new ExcelSyncReport();

            if (profile == null)
            {
                report.Message = "Trade profile is null.";
                return report;
            }

            if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
            {
                report.Message = $"Excel file not found: '{excelPath}'.";
                return report;
            }

            try
            {
                profile.EnsureInitialized();

                // 1. Read Trade_Config and Parameter_Rules via ClosedXML (FileShare.ReadWrite so Excel can stay open)
                using (var fs = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var workbook = new XLWorkbook(fs))
                {
                    var configWs = workbook.Worksheets.FirstOrDefault(w =>
                        w.Name.Equals("Trade_Config", StringComparison.OrdinalIgnoreCase) ||
                        w.Name.Equals("TradeConfig", StringComparison.OrdinalIgnoreCase));

                    if (configWs != null)
                    {
                        report.ConfigSettingsUpdated = ReadTradeConfigWorksheet(configWs, profile, report.Warnings);
                    }

                    var rulesWs = workbook.Worksheets.FirstOrDefault(w =>
                        w.Name.Equals("Parameter_Rules", StringComparison.OrdinalIgnoreCase) ||
                        w.Name.Equals("ParameterRules", StringComparison.OrdinalIgnoreCase));

                    if (rulesWs != null)
                    {
                        profile.ParameterRules = ReadParameterRulesWorksheet(rulesWs, report.Warnings);
                        report.ParameterRulesCount = profile.ParameterRules.Count;
                    }
                    else
                    {
                        report.ParameterRulesCount = profile.ParameterRules.Count;
                    }
                }

                // 2. Read all data tabs (Element_Params, Sheet_Params, Sheet_Specific_Params, Standards_Fill, Export_Locations)
                var bundle = LocationDataSheetReader.ReadParameterBundle(excelPath);
                profile.SyncedData = SyncedExcelSnapshot.FromLocationParameterBundle(bundle);

                report.ElementLocationsCount = bundle.ElementParameters.Count;
                report.SheetLocationsCount = bundle.SheetParameters.Count;
                report.SheetSpecificCount = bundle.SheetNumberParameters.Count;
                report.StandardsFillCount = bundle.StandardsFillRows.Count;
                report.ExportLocationsCount = bundle.ExportLocations.Count;

                // 3. Record sync metadata
                profile.LinkedExcelPath = excelPath;
                profile.LastExcelSyncUtc = DateTime.UtcNow;
                profile.LastExcelWriteTimeUtc = File.GetLastWriteTimeUtc(excelPath);
                profile.InvalidateFilter();

                report.Success = true;
                report.Message =
                    $"{report.ConfigSettingsUpdated} config item(s), {report.ParameterRulesCount} parameter rule(s), " +
                    $"{report.ElementLocationsCount} element location(s), {report.SheetLocationsCount} sheet location(s), " +
                    $"{report.SheetSpecificCount} sheet-specific row(s), {report.ExportLocationsCount} export destination(s).";
            }
            catch (Exception ex)
            {
                report.Success = false;
                report.Message = $"Sync failed: {ex.Message}";
            }

            return report;
        }

        private static int ReadTradeConfigWorksheet(IXLWorksheet ws, TradeProfile profile, List<string> warnings)
        {
            var firstRow = ws.FirstRowUsed();
            if (firstRow == null) return 0;

            int settingCol = 1;
            int valueCol = 2;

            foreach (var cell in firstRow.CellsUsed())
            {
                string h = cell.GetString().Trim();
                if (h.Equals("Setting", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("Key", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    settingCol = cell.Address.ColumnNumber;
                else if (h.Equals("Value", StringComparison.OrdinalIgnoreCase))
                    valueCol = cell.Address.ColumnNumber;
            }

            int updated = 0;
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

            for (int r = firstRow.RowNumber() + 1; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                if (row.IsEmpty()) continue;

                string key = LocationDataSheetReader.ReadCellTextSafe(row.Cell(settingCol));
                if (string.IsNullOrWhiteSpace(key)) continue;

                string val = LocationDataSheetReader.ReadCellTextSafe(row.Cell(valueCol));

                if (key.StartsWith("ProjectInfo:", StringComparison.OrdinalIgnoreCase))
                {
                    string piKey = key.Substring("ProjectInfo:".Length).Trim();
                    if (!string.IsNullOrEmpty(piKey))
                    {
                        profile.ProjectInfoData[piKey] = val;
                        updated++;
                    }
                    continue;
                }

                switch (key.Trim().ToLowerInvariant())
                {
                    case "tradecode":
                        if (string.IsNullOrWhiteSpace(profile.TradeCode))
                        {
                            profile.TradeCode = val.ToUpperInvariant();
                            updated++;
                        }
                        else if (!string.IsNullOrWhiteSpace(val) &&
                                 !val.Equals(profile.TradeCode, StringComparison.OrdinalIgnoreCase))
                        {
                            warnings.Add($"Trade_Config TradeCode is '{val}', kept current profile code '{profile.TradeCode}'.");
                        }
                        break;
                    case "tradename":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.TradeName = val; updated++; }
                        break;
                    case "disciplinefolder":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.DisciplineFolder = val; updated++; }
                        break;
                    case "workpackagesubfolder":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.WorkPackageSubfolder = val; updated++; }
                        break;
                    case "projectnumber":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.ProjectNumber = val; updated++; }
                        break;
                    case "originator":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.Originator = val; updated++; }
                        break;
                    case "worksetmatchtokens":
                        profile.WorksetMatchTokens = SplitCsv(val);
                        updated++;
                        break;
                    case "worksetsuffixes":
                        profile.WorksetSuffixes = SplitCsv(val);
                        updated++;
                        break;
                    case "targetcategories":
                        profile.TargetCategoryNames = SplitCsv(val);
                        profile.InvalidateFilter();
                        updated++;
                        break;
                    case "rvtfilenametemplate":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.RvtFileNameTemplate = val; updated++; }
                        break;
                    case "ifcfilenametemplate":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.IfcFileNameTemplate = val; updated++; }
                        break;
                    case "nwcfilenametemplate":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.NwcFileNameTemplate = val; updated++; }
                        break;
                    case "surveynorthmm":
                        if (TryParseDouble(val, out double sn)) { profile.SurveyNorthMm = sn; updated++; }
                        else warnings.Add($"Invalid SurveyNorthMm value '{val}'.");
                        break;
                    case "surveyeastmm":
                        if (TryParseDouble(val, out double se)) { profile.SurveyEastMm = se; updated++; }
                        else warnings.Add($"Invalid SurveyEastMm value '{val}'.");
                        break;
                    case "surveyelevmm":
                        if (TryParseDouble(val, out double sz)) { profile.SurveyElevMm = sz; updated++; }
                        else warnings.Add($"Invalid SurveyElevMm value '{val}'.");
                        break;
                    case "sheetincludetoken":
                        profile.SheetIncludeToken = val;
                        updated++;
                        break;
                    case "sheetdeletepatterns":
                        profile.SheetDeletePatterns = SplitCsv(val);
                        updated++;
                        break;
                    case "sheetcovername":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.SheetCoverName = val; updated++; }
                        break;
                    case "syncworkpackagecheckboxes":
                        if (TryParseBool(val, out bool swp)) { profile.SyncWorkPackageCheckboxes = swp; updated++; }
                        break;
                    case "ifcjsonpath":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.IFCJsonPath = val; updated++; }
                        break;
                    case "modelcheckersfolder":
                        if (!string.IsNullOrWhiteSpace(val)) { profile.ModelCheckersFolder = val; updated++; }
                        break;
                    case "defaultfillparameters":
                        if (TryParseBool(val, out bool b1)) { profile.DefaultFillParameters = b1; updated++; }
                        break;
                    case "defaultaligncoordinatesandworksets":
                        if (TryParseBool(val, out bool b2)) { profile.DefaultAlignCoordinatesAndWorksets = b2; updated++; }
                        break;
                    case "defaultpurgeunused":
                        if (TryParseBool(val, out bool b3)) { profile.DefaultPurgeUnused = b3; updated++; }
                        break;
                    case "defaultexportnwc":
                        if (TryParseBool(val, out bool b4)) { profile.DefaultExportNwc = b4; updated++; }
                        break;
                    case "defaultexportifc":
                        if (TryParseBool(val, out bool b5)) { profile.DefaultExportIfc = b5; updated++; }
                        break;
                    case "defaultsavecentralrvt":
                        if (TryParseBool(val, out bool b6)) { profile.DefaultSaveCentralRvt = b6; updated++; }
                        break;
                    case "defaultexportpdf":
                        if (TryParseBool(val, out bool b7)) { profile.DefaultExportPdf = b7; updated++; }
                        break;
                    case "defaultexportdwg":
                        if (TryParseBool(val, out bool b8)) { profile.DefaultExportDwg = b8; updated++; }
                        break;
                    case "defaultenforcemodelstandards":
                        if (TryParseBool(val, out bool b9)) { profile.DefaultEnforceModelStandards = b9; updated++; }
                        break;
                }
            }

            return updated;
        }

        private static List<ParameterRule> ReadParameterRulesWorksheet(IXLWorksheet ws, List<string> warnings)
        {
            var rules = new List<ParameterRule>();
            var firstRow = ws.FirstRowUsed();
            if (firstRow == null) return rules;

            int enabledCol = -1, targetCol = -1, filterCol = -1, paramCol = -1, valCol = -1, modeCol = -1, notesCol = -1;

            foreach (var cell in firstRow.CellsUsed())
            {
                string h = cell.GetString().Trim().ToLowerInvariant();
                int col = cell.Address.ColumnNumber;
                if (h == "enabled" || h == "on") enabledCol = col;
                else if (h == "target" || h == "scope") targetCol = col;
                else if (h == "filter" || h == "category") filterCol = col;
                else if (h == "parameter" || h == "parametername" || h == "param") paramCol = col;
                else if (h.Contains("value") || h.Contains("formula")) valCol = col;
                else if (h == "mode" || h == "overwrite") modeCol = col;
                else if (h == "notes" || h == "note" || h == "description") notesCol = col;
            }

            if (paramCol == -1) paramCol = 4;
            if (valCol == -1) valCol = 5;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            for (int r = firstRow.RowNumber() + 1; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                if (row.IsEmpty()) continue;

                string paramName = LocationDataSheetReader.ReadCellTextSafe(row.Cell(paramCol));
                if (string.IsNullOrWhiteSpace(paramName)) continue;

                // For Value_Or_Formula, read verbatim text; if user stored it as Excel formula, recover "=" + FormulaA1
                var valCell = row.Cell(valCol);
                string valueOrFormula = valCell.HasFormula
                    ? "=" + (valCell.FormulaA1 ?? string.Empty).Trim()
                    : (valCell.GetString() ?? string.Empty).Trim();

                bool enabled = true;
                if (enabledCol > 0)
                {
                    string enStr = LocationDataSheetReader.ReadCellTextSafe(row.Cell(enabledCol));
                    if (!string.IsNullOrWhiteSpace(enStr) && TryParseBool(enStr, out bool b))
                        enabled = b;
                }

                var target = ParameterRuleTarget.Element;
                if (targetCol > 0)
                {
                    string tStr = LocationDataSheetReader.ReadCellTextSafe(row.Cell(targetCol)).Replace(" ", "");
                    if (!string.IsNullOrWhiteSpace(tStr) && Enum.TryParse<ParameterRuleTarget>(tStr, true, out var parsedT))
                        target = parsedT;
                    else if (!string.IsNullOrWhiteSpace(tStr))
                        warnings.Add($"Parameter_Rules row {r}: unrecognized Target '{tStr}', defaulting to Element.");
                }

                string filter = filterCol > 0
                    ? LocationDataSheetReader.ReadCellTextSafe(row.Cell(filterCol))
                    : "*";
                if (string.IsNullOrWhiteSpace(filter)) filter = "*";

                var mode = ParameterFillMode.Overwrite;
                if (modeCol > 0)
                {
                    string mStr = LocationDataSheetReader.ReadCellTextSafe(row.Cell(modeCol)).Replace(" ", "");
                    if (!string.IsNullOrWhiteSpace(mStr))
                    {
                        if (mStr.Equals("FillIfEmpty", StringComparison.OrdinalIgnoreCase) ||
                            mStr.Equals("IfEmpty", StringComparison.OrdinalIgnoreCase))
                            mode = ParameterFillMode.FillIfEmpty;
                        else if (Enum.TryParse<ParameterFillMode>(mStr, true, out var parsedM))
                            mode = parsedM;
                    }
                }

                string notes = notesCol > 0
                    ? LocationDataSheetReader.ReadCellTextSafe(row.Cell(notesCol))
                    : string.Empty;

                if (valueOrFormula.StartsWith("=", StringComparison.Ordinal) &&
                    !ExcelFormulaEngine.Validate(valueOrFormula, out string? ferr, out _))
                {
                    warnings.Add($"Parameter_Rules row {r} ({paramName}): formula syntax warning — {ferr}");
                }

                rules.Add(new ParameterRule
                {
                    Enabled = enabled,
                    Target = target,
                    Filter = filter,
                    ParameterName = paramName,
                    ValueOrFormula = valueOrFormula,
                    Mode = mode,
                    Notes = notes
                });
            }

            return rules;
        }

        // ── Excel Template Export ────────────────────────────────────────────

        public static void ExportToExcelTemplate(TradeProfile profile, string targetPath)
        {
            profile.EnsureInitialized();

            using var wb = new XLWorkbook();

            // 1. Trade_Config
            var wsConfig = wb.AddWorksheet("Trade_Config");
            var configRows = new List<string[]>
            {
                new[] { "TradeCode", profile.TradeCode, "ISO 19650 filename segment 2 trade abbreviation" },
                new[] { "TradeName", profile.TradeName, "Friendly trade display name" },
                new[] { "DisciplineFolder", profile.DisciplineFolder, "Discipline code used in folder paths" },
                new[] { "WorkPackageSubfolder", profile.WorkPackageSubfolder, "Work package subfolder under Models" },
                new[] { "ProjectNumber", profile.ProjectNumber, "Project number token" },
                new[] { "Originator", profile.Originator, "Originator code token" },
                new[] { "WorksetMatchTokens", string.Join(", ", profile.WorksetMatchTokens), "Comma-separated tokens to identify trade worksets" },
                new[] { "WorksetSuffixes", string.Join(", ", profile.WorksetSuffixes), "Comma-separated suffixes renamed per location" },
                new[] { "TargetCategories", string.Join(", ", profile.TargetCategoryNames), "Revit BuiltInCategory names targeted for parameter fill" },
                new[] { "RvtFileNameTemplate", profile.RvtFileNameTemplate, "Central RVT output filename template" },
                new[] { "IfcFileNameTemplate", profile.IfcFileNameTemplate, "IFC export filename template" },
                new[] { "NwcFileNameTemplate", profile.NwcFileNameTemplate, "NWC export filename template" },
                new[] { "SurveyNorthMm", profile.SurveyNorthMm.ToString(CultureInfo.InvariantCulture), "Shared Survey Point Northing (mm)" },
                new[] { "SurveyEastMm", profile.SurveyEastMm.ToString(CultureInfo.InvariantCulture), "Shared Survey Point Easting (mm)" },
                new[] { "SurveyElevMm", profile.SurveyElevMm.ToString(CultureInfo.InvariantCulture), "Shared Survey Point Elevation (mm)" },
                new[] { "SheetIncludeToken", profile.SheetIncludeToken, "Sheet number token identifying template sheets" },
                new[] { "SheetDeletePatterns", string.Join(", ", profile.SheetDeletePatterns), "Comma-separated patterns of obsolete sheets to remove" },
                new[] { "SheetCoverName", profile.SheetCoverName, "Name match for Sheet Cover" },
                new[] { "SyncWorkPackageCheckboxes", profile.SyncWorkPackageCheckboxes ? "TRUE" : "FALSE", "Sync WPxx checkboxes on titleblocks" },
                new[] { "IFCJsonPath", profile.IFCJsonPath, "Path to IFC export configuration JSON" },
                new[] { "ModelCheckersFolder", profile.ModelCheckersFolder, "Root folder containing Model Checker XMLs" }
            };
            foreach (var kv in profile.ProjectInfoData)
            {
                configRows.Add(new[] { $"ProjectInfo:{kv.Key}", kv.Value, "Project Information parameter" });
            }
            WriteStyledSheet(wsConfig, new[] { "Setting", "Value", "Notes" }, configRows, textCols: new[] { 2 });

            // 2. Parameter_Rules
            var wsRules = wb.AddWorksheet("Parameter_Rules");
            var rulesRows = new List<string[]>();
            if (profile.ParameterRules.Count > 0)
            {
                foreach (var r in profile.ParameterRules)
                {
                    rulesRows.Add(new[]
                    {
                        r.Enabled ? "TRUE" : "FALSE",
                        r.Target.ToString(),
                        string.IsNullOrWhiteSpace(r.Filter) ? "*" : r.Filter,
                        r.ParameterName,
                        r.ValueOrFormula,
                        r.Mode.ToString(),
                        r.Notes
                    });
                }
            }
            else
            {
                rulesRows.Add(new[] { "TRUE", "Element", "*", "CPK_Stage", "Projekt Budowlany", "FillIfEmpty", "Static default value" });
                rulesRows.Add(new[] { "TRUE", "Element", "*", "CPK_WBS_Short", "=LEFT(RIGHT([CPK_WBS_5],2),1)&\".\"&RIGHT([CPK_WBS_5],1)", "Overwrite", "Example dependent formula (81 -> 8.1)" });
            }
            WriteStyledSheet(
                wsRules,
                new[] { "Enabled", "Target", "Filter", "Parameter", "Value_Or_Formula", "Mode", "Notes" },
                rulesRows,
                textCols: new[] { 5 });
            wsRules.Range("A2:A200").CreateDataValidation().List("\"TRUE,FALSE\"", true);
            wsRules.Range("B2:B200").CreateDataValidation().List("\"Element,Sheet,Titleblock,SheetCover,ProjectInfo\"", true);
            wsRules.Range("F2:F200").CreateDataValidation().List("\"Overwrite,FillIfEmpty\"", true);

            // 3. Element_Params
            var wsElem = wb.AddWorksheet("Element_Params");
            WriteKeyedMapSheet(
                wsElem,
                "Locator Code",
                profile.SyncedData?.ElementParameters,
                fallbackHeaders: new[] { "Locator Code", "CPK_WBS_Name", "CPK_WBS_1-3", "CPK_WBS_4", "CPK_WBS_5" },
                fallbackRows: new List<string[]>
                {
                    new[] { "AFNXIEELS", "AGL Substations Main Midfield North West", "C.A.AI.NVL.10", "C.A.AI.NVL.10.CON", "C.A.AI.NVL.10.CON.81" }
                });

            // 4. Sheet_Params
            var wsSheet = wb.AddWorksheet("Sheet_Params");
            WriteKeyedMapSheet(
                wsSheet,
                "Locator Code",
                profile.SyncedData?.SheetParameters,
                fallbackHeaders: new[] { "Locator Code", "Work_Package", "Building Name", "CPK_Location_Code", "CPK_RVT_File_Name" },
                fallbackRows: new List<string[]>
                {
                    new[] { "AFNXIEELS", "WP48", "AGL Substations Main Midfield North West", "AFNXIEELS", $"{profile.ProjectNumber}-{profile.Originator}-{profile.TradeCode}-AFNXIEELS-XX-M3-XX0001_PE" }
                });

            // 5. Sheet_Specific_Params
            var wsSpec = wb.AddWorksheet("Sheet_Specific_Params");
            WriteKeyedMapSheet(
                wsSpec,
                "Sheet Number",
                profile.SyncedData?.SheetNumberParameters,
                fallbackHeaders: new[] { "Sheet Number", "Sheet Name", "Work_Package", "Revision", "Drawn By" },
                fallbackRows: new List<string[]>
                {
                    new[] { $"{profile.ProjectNumber}-{profile.Originator}-{profile.TradeCode}-AFNXIEELS-00-DR-0001_PL", "Main Midfield NW - Plan", "WP48", "P01", profile.Originator }
                });

            // 6. Standards_Fill
            var wsStd = wb.AddWorksheet("Standards_Fill");
            var stdRows = (profile.SyncedData?.StandardsFillRows ?? new List<StandardsFillRowDto>())
                .Select(r => new[] { r.Locator, r.Category, r.FamilyType, r.Parameter, r.Value })
                .ToList();
            if (stdRows.Count == 0)
            {
                stdRows.Add(new[] { "*", "*", "*", "CPK_Stage", "Projekt Budowlany" });
            }
            WriteStyledSheet(wsStd, new[] { "Locator Code", "Category", "Family Type", "Parameter", "Value" }, stdRows);

            // 7. Export_Locations
            var wsExp = wb.AddWorksheet("Export_Locations");
            var expRows = (profile.SyncedData?.ExportLocations ?? new Dictionary<string, string>())
                .Select(kv => new[] { kv.Key, kv.Value, string.Empty })
                .ToList();
            if (expRows.Count == 0)
            {
                expRows.Add(new[] { "AFNXIEELS", @"C:\Export\WP04", "Sample export destination" });
            }
            WriteStyledSheet(wsExp, new[] { "Locator Code", "Export_Folder", "Description" }, expRows);

            wb.SaveAs(targetPath);
        }

        private static void WriteKeyedMapSheet(
            IXLWorksheet ws,
            string keyHeader,
            Dictionary<string, Dictionary<string, string>>? data,
            string[] fallbackHeaders,
            List<string[]> fallbackRows)
        {
            if (data == null || data.Count == 0)
            {
                WriteStyledSheet(ws, fallbackHeaders, fallbackRows);
                return;
            }

            var dynamicCols = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rowDict in data.Values)
            {
                foreach (var k in rowDict.Keys)
                {
                    if (seen.Add(k)) dynamicCols.Add(k);
                }
            }

            var headers = new List<string> { keyHeader };
            headers.AddRange(dynamicCols);

            var rows = new List<string[]>();
            foreach (var kv in data)
            {
                var arr = new string[headers.Count];
                arr[0] = kv.Key;
                for (int c = 0; c < dynamicCols.Count; c++)
                {
                    kv.Value.TryGetValue(dynamicCols[c], out var v);
                    arr[c + 1] = v ?? string.Empty;
                }
                rows.Add(arr);
            }

            WriteStyledSheet(ws, headers.ToArray(), rows);
        }

        private static void WriteStyledSheet(IXLWorksheet ws, string[] headers, List<string[]> rows, int[]? textCols = null)
        {
            var textColSet = new HashSet<int>(textCols ?? Array.Empty<int>());
            var headerBg = XLColor.FromHtml("#1F2937");
            var zebraBg = XLColor.FromHtml("#F8FAFC");
            var borderColor = XLColor.FromHtml("#D1D5DB");

            ws.SheetView.FreezeRows(1);
            ws.Row(1).Height = 22;

            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.BackgroundColor = headerBg;
                cell.Style.Font.FontName = "Segoe UI";
                cell.Style.Font.FontSize = 10;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = borderColor;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            for (int r = 0; r < rows.Count; r++)
            {
                int excelRow = r + 2;
                ws.Row(excelRow).Height = 20;
                var rowData = rows[r];

                for (int c = 0; c < headers.Length; c++)
                {
                    var cell = ws.Cell(excelRow, c + 1);
                    string val = c < rowData.Length ? (rowData[c] ?? string.Empty) : string.Empty;
                    if (textColSet.Contains(c + 1))
                    {
                        cell.Style.NumberFormat.Format = "@";
                    }
                    cell.Value = val;
                    if (r % 2 == 1) cell.Style.Fill.BackgroundColor = zebraBg;
                    cell.Style.Font.FontName = textColSet.Contains(c + 1) ? "Consolas" : "Segoe UI";
                    cell.Style.Font.FontSize = 9.5;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = borderColor;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }
            }

            int lastRow = Math.Max(1, rows.Count + 1);
            ws.Range(1, 1, lastRow, headers.Length).SetAutoFilter();

            for (int c = 1; c <= headers.Length; c++)
            {
                int maxLen = headers[c - 1].Length;
                foreach (var rowData in rows)
                {
                    if (c - 1 < rowData.Length && rowData[c - 1] != null)
                        maxLen = Math.Max(maxLen, rowData[c - 1].Length);
                }
                ws.Column(c).Width = Math.Min(Math.Max(maxLen + 4, 12), 72);
            }
        }

        // ── Parsing Helpers ──────────────────────────────────────────────────

        private static List<string> SplitCsv(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim())
                      .Where(s => s.Length > 0)
                      .ToList();
        }

        private static bool TryParseDouble(string raw, out double result) =>
            double.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

        private static bool TryParseBool(string raw, out bool result)
        {
            string s = (raw ?? string.Empty).Trim();
            if (s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || s.Equals("YES", StringComparison.OrdinalIgnoreCase) || s == "1")
            {
                result = true;
                return true;
            }
            if (s.Equals("FALSE", StringComparison.OrdinalIgnoreCase) || s.Equals("NO", StringComparison.OrdinalIgnoreCase) || s == "0")
            {
                result = false;
                return true;
            }
            result = false;
            return false;
        }
    }
}
