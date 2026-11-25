using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace RevitLocationExporter
{
    public static class IFCJsonLoader
    {
        public static IFCExportOptions Load(string jsonPath, Document doc, string locationName)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("IFC JSON file not found.", jsonPath);

            // Parse using Revit built-in Newtonsoft.Json
            JObject j = JObject.Parse(File.ReadAllText(jsonPath));

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

            if (j.TryGetValue("SpaceBoundaries", out JToken? v2) && v2.Type != JTokenType.Null)
                o.SpaceBoundaryLevel = v2.Value<int>();

            if (j.TryGetValue("SplitWallsAndColumns", out JToken? v3) && v3.Type != JTokenType.Null)
                o.WallAndColumnSplitting = v3.Value<bool>();


            if (j.TryGetValue("ExportBaseQuantities", out JToken? v6) && v6.Type != JTokenType.Null)
                o.ExportBaseQuantities = v6.Value<bool>();

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

                if (kv.Value != null && kv.Value.Type != JTokenType.Null)
                    o.AddOption(key, JsonConvert.SerializeObject(kv.Value, Formatting.None));
                
            }

            return o;
        }
        
    }
}
