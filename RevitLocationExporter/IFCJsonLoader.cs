using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitLocationExporter
{
    public static class IFCJsonLoader
    {
        public static IFCExportOptions Load(string jsonPath, Document doc, string locationName)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("IFC JSON file not found.", jsonPath);

            // Parse using System.Text.Json
            JsonObject j = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsObject();

            IFCExportOptions o = new IFCExportOptions();

            View3D ifcView = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v =>
                    !v.IsTemplate &&
                    v.Name.IndexOf("IFC", StringComparison.OrdinalIgnoreCase) >= 0)
                .FirstOrDefault();

            if (ifcView == null)
                throw new InvalidOperationException(
                    "No 3D view found with name containing 'Naviswork'.");

            // ==========================
            // DIRECT API MAPPINGS
            // ==========================


            o.FileVersion = IFCVersion.IFC2x3CV2;

            if (j.TryGetPropertyValue("SpaceBoundaries", out JsonNode? v2))
                o.SpaceBoundaryLevel = v2!.GetValue<int>();

            if (j.TryGetPropertyValue("SplitWallsAndColumns", out JsonNode? v3))
                o.WallAndColumnSplitting = v3!.GetValue<bool>();


            if (j.TryGetPropertyValue("ExportBaseQuantities", out JsonNode? v6))
                o.ExportBaseQuantities = v6!.GetValue<bool>();

            o.FilterViewId = ifcView.Id;

            o.AddOption("SelectedSite", locationName);
          

            // ==================================
            // ADD ALL OTHER JSON KEYS AS OPTIONS
            // ==================================
            foreach (var kv in j)
            {
                string key = kv.Key;

                if (key is "IFCVersion" or "SpaceBoundaries" or
                    "SplitWallsAndColumns" or "ExportBaseQuantities")
                    continue;

                if (kv.Value != null)
                    o.AddOption(key, kv.Value.ToJsonString());
                
            }

            return o;
        }
        
    }
}
