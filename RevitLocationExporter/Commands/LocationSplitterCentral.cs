using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace RevitLocationExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class LocationSplitterCentral : IExternalCommand
    {
        private static readonly string[] WorksetSuffixes =
        {
            "_TI0_FD", "_TI0_SD", "_TI0_DD", "_TI0_LC"
        };

        private static readonly Dictionary<string, string> LocationFolderMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "AFCX", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 2\Authoring Model" },
                { "AFNX", @"P:\PO22242-0100D\04_WP04_Northern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 2\Authoring Model" },
                { "AFSX", @"P:\PO22242-0100D\05_WP05_Southern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 2\Authoring Model" },
                { "LFEX", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 2\Authoring Model" },
                { "LFWX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 2\Authoring Model" }
            };

        private string? _selectedPath;

        private const double NORTH_MM = 5776800031.2;
        private const double EAST_MM = 7457254602.8;
        private const double ELEV_MM = 98300.0;

        public string IFCJsonPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "IFCSettings.json");

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var app = uiApp.Application;

            _selectedPath = SelectRvtFile();
            if (string.IsNullOrEmpty(_selectedPath))
                return Result.Cancelled;

            var rvtPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(_selectedPath);

            var locationNames = ReadProjectLocations(app, rvtPath);
            if (locationNames.Count == 0)
            {
                TaskDialog.Show("Location Splitter", "No project locations found.");
                return Result.Cancelled;
            }

            foreach (string locationName in locationNames)
            {
                try
                {
                    ProcessLocation(app, rvtPath, locationName, _selectedPath!);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Location Splitter – Error",
                        $"Location: {locationName}\n\n{ex.Message}");
                }
            }

            TaskDialog.Show("Location Splitter", "All locations processed successfully.");
            return Result.Succeeded;
        }

        // ─────────────────────────────────────────────────────────────────────

        private static string? SelectRvtFile()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Revit Files (*.rvt)|*.rvt",
                Title = "Select Revit Model"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private static List<string> ReadProjectLocations(
            Autodesk.Revit.ApplicationServices.Application app, ModelPath rvtPath)
        {
            var opts = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
            };
            using var doc = app.OpenDocumentFile(rvtPath, opts);
            return doc.ProjectLocations
                .Cast<ProjectLocation>()
                .Select(pl => pl.Name)
                .ToList();
        }

        private void ProcessLocation(
            Autodesk.Revit.ApplicationServices.Application app,
            ModelPath rvtPath,
            string locationName,
            string path)
        {
            var opts = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
            };

            using var doc = app.OpenDocumentFile(rvtPath, opts);
            ActivateProjectLocation(doc, locationName);
            RenameWorksets(doc, locationName);
            FillSheetCoverParameters(doc, locationName);
            FillCPKParametersForModel_Types(doc, locationName);
            NWCIFCexports(doc, locationName);
            SaveAsCentral(doc, path, locationName);
            SyncAndClose(doc);
        }

        // ─────────────────────────────────────────────────────────────────────
        // NWC / IFC Export
        // ─────────────────────────────────────────────────────────────────────

        private void NWCIFCexports(Document doc, string locationName)
        {
            ElementId id = GetNavisworks3DViewId(doc);

            var navopts = new NavisworksExportOptions
            {
                ExportScope = NavisworksExportScope.View,
                ViewId = id,
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

            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(l => l.GetLinkDocument() != null)
                .ToList();

            string exportPath = GetOutputFolder(locationName);

            using (var tg = new TransactionGroup(doc, $"Export {locationName}"))
            {
                tg.Start();

                using (var tx = new Transaction(doc, $"Move links for {locationName}"))
                {
                    tx.Start();
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
                            TaskDialog.Show("Link Move Error", $"{link.Name}\n{ex.Message}");
                        }
                    }
                    tx.Commit();
                }

                using (var tx = new Transaction(doc, $"Export IFC for {locationName}"))
                {
                    tx.Start();
                    if (!string.IsNullOrEmpty(IFCJsonPath) && File.Exists(IFCJsonPath))
                    {
                        var ifcOpts = IFCJsonLoader.Load(IFCJsonPath, doc, locationName);
                        string ifcFile = $"P00100087-DAR-TI0-{locationName}-XX-E3-2X0001_EN.ifc";
                        string ifcFolder = Path.Combine(exportPath, "IFC");
                        Directory.CreateDirectory(ifcFolder);
                        doc.Export(ifcFolder, ifcFile, ifcOpts);
                    }
                    tx.Commit();
                }

                tg.Assimilate();
            }

            ExportNwc(doc, exportPath, locationName, navopts);
        }

        private static void ExportNwc(Document doc, string path, string locationName,
            NavisworksExportOptions navopts)
        {
            string nwcFolder = Path.Combine(path, "NWC");
            string nwcFile = $"P00100087-DAR-TI0-{locationName}-XX-PE-2X0001_EN.nwc";
            Directory.CreateDirectory(nwcFolder);
            doc.Export(nwcFolder, nwcFile, navopts);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Activate Location
        // ─────────────────────────────────────────────────────────────────────

        private void ActivateProjectLocation(Document doc, string locationName)
        {
            using var tx = new Transaction(doc, "Activate Project Location");
            tx.Start();

            var location = doc.ProjectLocations
                .Cast<ProjectLocation>()
                .FirstOrDefault(pl =>
                    pl.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Project Location not found: {locationName}");

            doc.ActiveProjectLocation = location;
            SetSurveyPoint(doc);
            tx.Commit();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Worksets
        // ─────────────────────────────────────────────────────────────────────

        private static void RenameWorksets(Document doc, string locationName)
        {
            var worksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToList();

            using var tx = new Transaction(doc, "Rename Worksets");
            tx.Start();

            foreach (var ws in worksets)
            {
                foreach (string suffix in WorksetSuffixes)
                {
                    if (!ws.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string newName = locationName + suffix;
                    if (!ws.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                        WorksetTable.RenameWorkset(doc, ws.Id, newName);

                    break;
                }
            }

            tx.Commit();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Save / Sync
        // ─────────────────────────────────────────────────────────────────────

        private static void SaveAsCentral(Document doc, string originalPath, string locationName)
        {
            if (!doc.IsWorkshared)
            {
                using var tx = new Transaction(doc, "Enable Worksharing");
                tx.Start();
                doc.EnableWorksharing("Shared Levels and Grids", "Workset1");
                tx.Commit();
            }

            string exportPath = GetOutputFolderStatic(locationName);
            string rvtFolder = Path.Combine(exportPath, "RVT");
            Directory.CreateDirectory(rvtFolder);

            string newPath = Path.Combine(rvtFolder,
                $"P00100087-DAR-TI0-{locationName}-XX-M3-2X0001_EN.rvt");

            var saveOpts = new SaveAsOptions { OverwriteExistingFile = true, Compact = true };
            saveOpts.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
            doc.SaveAs(newPath, saveOpts);
        }

        private static void SyncAndClose(Document doc)
        {
            var syncOpts = new SynchronizeWithCentralOptions();
            var transOpts = new TransactWithCentralOptions();
            syncOpts.SetRelinquishOptions(new RelinquishOptions(true));
            syncOpts.SaveLocalAfter = false;
            doc.SynchronizeWithCentral(transOpts, syncOpts);
            doc.Close(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Survey Point
        // ─────────────────────────────────────────────────────────────────────

        private void SetSurveyPoint(Document doc)
        {
            var surveyPoint = new FilteredElementCollector(doc)
                .OfClass(typeof(BasePoint))
                .OfCategory(BuiltInCategory.OST_SharedBasePoint)
                .Cast<BasePoint>()
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Survey Point not found.");

            double north = UnitUtils.ConvertToInternalUnits(NORTH_MM, UnitTypeId.Millimeters);
            double east = UnitUtils.ConvertToInternalUnits(EAST_MM, UnitTypeId.Millimeters);
            double elev = UnitUtils.ConvertToInternalUnits(ELEV_MM, UnitTypeId.Millimeters);

            foreach (Parameter p in surveyPoint.Parameters)
            {
                if (p.Definition != null &&
                    p.Definition.Name.IndexOf("Clipped", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    p.StorageType == StorageType.Integer)
                {
                    p.Set(0);
                    break;
                }
            }

            surveyPoint.Clipped = false;
            surveyPoint.get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM).Set(east);
            surveyPoint.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM).Set(north);
            surveyPoint.get_Parameter(BuiltInParameter.BASEPOINT_ELEVATION_PARAM).Set(elev);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Sheet Cover Parameters
        // ─────────────────────────────────────────────────────────────────────

        private void FillSheetCoverParameters(Document doc, string locationName)
        {
            using var tx = new Transaction(doc, "Fill Sheet Cover Coordinates");
            tx.Start();

            var pbp = GetBasePoint(doc, BuiltInCategory.OST_ProjectBasePoint)
                ?? throw new InvalidOperationException("Project Base Point not found.");
            var svp = GetBasePoint(doc, BuiltInCategory.OST_SharedBasePoint)
                ?? throw new InvalidOperationException("Survey Point not found.");

            double pbpN = ToMm(pbp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
            double pbpE = ToMm(pbp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
            double pbpZ = ToMm(pbp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM);
            double svpN = ToMm(svp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM);
            double svpE = ToMm(svp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM);
            double svpZ = ToMm(svp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM);

            string acceptor = GetAcceptor(locationName);

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.Name.Equals("Sheet Cover", StringComparison.OrdinalIgnoreCase))
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
                SetString(sheet, "CPK_Acceptor", acceptor);
            }

            tx.Commit();
        }

        private void FillCPKParametersForModel_Types(Document doc, string locationName)
        {
            string cpkWorkPackage = GetCPKWorkPackage(locationName);
            string cpkWbs = GetCPKWbsCode(locationName);
            string cpkWbsName = GetCPKWbsName(locationName);

            var instances = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();
            var processedTypes = new HashSet<ElementId>();

            using var tx = new Transaction(doc, "Fill CPK Parameters (TYPES)");
            tx.Start();

            foreach (Element e in instances)
            {
                if (e.Category == null || e.ViewSpecific) continue;

                ElementId typeId = e.GetTypeId();
                if (typeId == ElementId.InvalidElementId) continue;
                if (processedTypes.Contains(typeId)) continue;

                var type = doc.GetElement(typeId) as ElementType;
                if (type == null) continue;

                processedTypes.Add(typeId);
                SetString(type, "CPK_Work_Package", cpkWorkPackage);
                SetString(type, "CPK_WBS_1-3b", cpkWbs);
                SetString(type, "CPK_WBS_Name", cpkWbsName);
            }

            tx.Commit();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static BasePoint? GetBasePoint(Document doc, BuiltInCategory cat) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(BasePoint))
                .OfCategory(cat)
                .Cast<BasePoint>()
                .FirstOrDefault();

        private static double ToMm(BasePoint bp, BuiltInParameter bip) =>
            UnitUtils.ConvertFromInternalUnits(bp.get_Parameter(bip).AsDouble(), UnitTypeId.Millimeters);

        private static string GetAcceptor(string loc)
        {
            if (string.IsNullOrWhiteSpace(loc)) return string.Empty;
            char c = char.ToUpper(loc[0]);
            return c == 'A' ? "S.P." : c == 'L' ? "K.Z." : string.Empty;
        }

        private static void SetDouble(Element e, string name, double val)
        {
            var p = e.LookupParameter(name);
            if (p != null && !p.IsReadOnly) p.Set(val);
        }

        private static void SetString(Element e, string name, string val)
        {
            var p = e.LookupParameter(name);
            if (p != null && !p.IsReadOnly) p.Set(val);
        }

        private string GetOutputFolder(string locationName) =>
            GetOutputFolderStatic(locationName);

        private static string GetOutputFolderStatic(string locationName)
        {
            string? key = LocationFolderMap.Keys
                .FirstOrDefault(k =>
                    locationName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

            return key != null
                ? LocationFolderMap[key]
                : throw new InvalidOperationException(
                    $"No export folder mapping found for location: {locationName}");
        }

        private static ElementId GetNavisworks3DViewId(Document doc)
        {
            var view = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v =>
                    !v.IsTemplate &&
                    v.Name.IndexOf("Naviswork", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? throw new InvalidOperationException(
                    "No 3D view found with name containing 'Naviswork'.");

            return view.Id;
        }

        private static string GetCPKWorkPackage(string loc)
        {
            string n = loc.ToUpperInvariant();
            if (n.StartsWith("AFCX")) return "WP03";
            if (n.StartsWith("AFNX")) return "WP04";
            if (n.StartsWith("AFSX")) return "WP05";
            if (n.StartsWith("LFEX")) return "WP07";
            if (n.StartsWith("LFWX")) return "WP08";
            return string.Empty;
        }

        private static string GetCPKWbsCode(string loc)
        {
            string n = loc.ToUpperInvariant();
            if (n.StartsWith("AFCX")) return "C.A.EN.PDN.02";
            if (n.StartsWith("AFNX")) return "C.A.EN.PDN.01";
            if (n.StartsWith("AFSX")) return "C.A.EN.PDN.03";
            if (n.StartsWith("LFEX")) return "C.A.EN.PDN.04";
            if (n.StartsWith("LFWX")) return "C.A.EN.PDN.05";
            return string.Empty;
        }

        private static string GetCPKWbsName(string loc)
        {
            string n = loc.ToUpperInvariant();
            if (n.StartsWith("AFCX")) return "Power Distribution Airside Center";
            if (n.StartsWith("AFNX")) return "Power Distribution Airside North";
            if (n.StartsWith("AFSX")) return "Power Distribution Airside South";
            if (n.StartsWith("LFEX")) return "Power Distribution Landside East";
            if (n.StartsWith("LFWX")) return "Power Distribution Landside West";
            return string.Empty;
        }
    }
}