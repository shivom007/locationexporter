using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitLocationExporter.Core
{
    public class LocatorLookup
    {
        private static readonly Dictionary<string, string> _data =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded = false;
        private static readonly string DefaultCsvPath =
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads",
        "LocatorBuildingName.csv");

        static LocatorLookup() {
            try
            {
                LoadFromCsv();
            }
            catch (Exception)
            {
                // Since static constructors fail silently or crash the app, 
                // it's good practice to handle errors here.
                _loaded = false;
            }
        } 

        public static void LoadFromCsv(string? csvPath = null)
        {
            if (string.IsNullOrWhiteSpace(csvPath))
                csvPath ??= DefaultCsvPath;

            if (!File.Exists(csvPath))
                throw new FileNotFoundException("CSV file not found.", csvPath);

            _data.Clear();

            string[] lines = File.ReadAllLines(csvPath);

            for (int i = 1; i < lines.Length; i++) // skip header
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                string[] parts = lines[i].Split('\t'); // tab-separated from your file

                if (parts.Length < 2)
                    parts = lines[i].Split(','); // fallback CSV

                if (parts.Length < 2)
                    continue;

                string locator = parts[0].Trim();
                string name = parts[1].Trim();

                if (!string.IsNullOrWhiteSpace(locator))
                    _data[locator] = name;
            }

            _loaded = true;
        }

        public static string GetName(string locatorCode)
        {
            if (!_loaded)
                return "Lookup Not Loaded";

            if (string.IsNullOrWhiteSpace(locatorCode))
                return "Invalid Locator";

            return _data.TryGetValue(locatorCode.Trim(), out string value)
                ? value
                : "Locator Not Found";
        }

        public static bool TryGetName(string locatorCode, out string value)
        {
            value = "";

            if (!_loaded || string.IsNullOrWhiteSpace(locatorCode))
                return false;

            return _data.TryGetValue(locatorCode.Trim(), out value);
        }
    }
}
