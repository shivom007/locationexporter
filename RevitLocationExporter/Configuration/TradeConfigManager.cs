using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitLocationExporter.Configuration
{
    /// <summary>
    /// Singleton manager that loads, discovers, customizes, and persists TradeProfiles and ProjectSettings.
    /// Supports user-built custom profiles saved to %AppData%\RevitLocationExporter\trades.json.
    /// </summary>
    public class TradeConfigManager
    {
        private static readonly Lazy<TradeConfigManager> _instance =
            new Lazy<TradeConfigManager>(() => new TradeConfigManager());

        public static TradeConfigManager Instance => _instance.Value;

        public ProjectSettings Settings { get; private set; } = new();

        public Dictionary<string, TradeProfile> Trades { get; private set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The currently active/selected trade code (e.g. "TI0", "ME0", "EL0").</summary>
        public string ActiveTradeCode { get; set; } = "TI0";

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            opts.Converters.Add(new JsonStringEnumConverter());
            return opts;
        }

        public TradeConfigManager()
        {
            RegisterBuiltInTrades();
            TryLoadFromJson();
        }

        /// <summary>Restores built-in default trade profiles (preserving any extra custom trades).</summary>
        public void ResetBuiltInTrades()
        {
            RegisterBuiltInTrades();
        }

        private void RegisterBuiltInTrades()
        {
            // 1. Telecom (Default / Original TI0)
            Trades["TI0"] = new TradeProfile
            {
                TradeCode = "TI0",
                TradeName = "Telecom",
                DisciplineFolder = "EL",
                WorkPackageSubfolder = "Telecom - Building Package",
                WorksetMatchTokens = new() { "TI0" },
                WorksetSuffixes = new() { "_TI0_FA", "_TI0_SD", "_TI0_DD", "_TI0_LC" },
                TargetCategoryNames = new()
                {
                    "OST_FireAlarmDevices", "OST_DataDevices", "OST_SecurityDevices",
                    "OST_TelephoneDevices", "OST_AudioVisualDevices", "OST_CommunicationDevices",
                    "OST_CableTray", "OST_CableTrayFitting", "OST_Conduit", "OST_ConduitFitting"
                }
            };

            // 2. Electrical (Power / Lighting)
            Trades["EL0"] = new TradeProfile
            {
                TradeCode = "EL0",
                TradeName = "Electrical",
                DisciplineFolder = "EL",
                WorkPackageSubfolder = "Electrical - Building Package",
                WorksetMatchTokens = new() { "EL0", "EL" },
                WorksetSuffixes = new() { "_EL0_PWR", "_EL0_LGT", "_EL0_EAR", "_EL0_SEC" },
                TargetCategoryNames = new()
                {
                    "OST_ElectricalEquipment", "OST_ElectricalFixtures", "OST_LightingFixtures",
                    "OST_LightingDevices", "OST_CableTray", "OST_CableTrayFitting", "OST_Conduit", "OST_ConduitFitting"
                }
            };

            // 3. Mechanical / HVAC
            Trades["ME0"] = new TradeProfile
            {
                TradeCode = "ME0",
                TradeName = "Mechanical",
                DisciplineFolder = "ME",
                WorkPackageSubfolder = "Mechanical - Building Package",
                WorksetMatchTokens = new() { "ME0", "HV0", "ME" },
                WorksetSuffixes = new() { "_ME0_SUP", "_ME0_RET", "_ME0_EXH", "_ME0_EQP" },
                TargetCategoryNames = new()
                {
                    "OST_DuctCurves", "OST_DuctFitting", "OST_DuctAccessory",
                    "OST_DuctTerminal", "OST_MechanicalEquipment"
                }
            };

            // 4. Plumbing / Public Health
            Trades["PL0"] = new TradeProfile
            {
                TradeCode = "PL0",
                TradeName = "Plumbing",
                DisciplineFolder = "PL",
                WorkPackageSubfolder = "Plumbing - Building Package",
                WorksetMatchTokens = new() { "PL0", "PL" },
                WorksetSuffixes = new() { "_PL0_DOM", "_PL0_SAN", "_PL0_DRN", "_PL0_EQP" },
                TargetCategoryNames = new()
                {
                    "OST_PipeCurves", "OST_PipeFitting", "OST_PipeAccessory",
                    "OST_PlumbingFixtures", "OST_MechanicalEquipment"
                }
            };

            // 5. Fire Protection
            Trades["FP0"] = new TradeProfile
            {
                TradeCode = "FP0",
                TradeName = "Fire Protection",
                DisciplineFolder = "FP",
                WorkPackageSubfolder = "Fire Protection - Building Package",
                WorksetMatchTokens = new() { "FP0", "FP" },
                WorksetSuffixes = new() { "_FP0_SPK", "_FP0_HYD", "_FP0_EQP" },
                TargetCategoryNames = new()
                {
                    "OST_Sprinklers", "OST_PipeCurves", "OST_PipeFitting", "OST_PipeAccessory"
                }
            };

            // 6. Architecture
            Trades["AR0"] = new TradeProfile
            {
                TradeCode = "AR0",
                TradeName = "Architecture",
                DisciplineFolder = "AR",
                WorkPackageSubfolder = "Architecture - Building Package",
                WorksetMatchTokens = new() { "AR0", "A00", "AR" },
                WorksetSuffixes = new() { "_AR0_WAL", "_AR0_FLR", "_AR0_ROF", "_AR0_DOOR" },
                TargetCategoryNames = new()
                {
                    "OST_Walls", "OST_Doors", "OST_Windows", "OST_Floors", "OST_Ceilings", "OST_Rooms"
                }
            };

            // 7. Structure
            Trades["ST0"] = new TradeProfile
            {
                TradeCode = "ST0",
                TradeName = "Structure",
                DisciplineFolder = "ST",
                WorkPackageSubfolder = "Structure - Building Package",
                WorksetMatchTokens = new() { "ST0", "S00", "ST" },
                WorksetSuffixes = new() { "_ST0_COL", "_ST0_BEA", "_ST0_FOU", "_ST0_SLB" },
                TargetCategoryNames = new()
                {
                    "OST_StructuralColumns", "OST_StructuralFraming", "OST_StructuralFoundation", "OST_Floors"
                }
            };
        }

        /// <summary>
        /// Returns the standard config path in %AppData%\RevitLocationExporter\trades.json.
        /// </summary>
        public static string GetConfigFilePath()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RevitLocationExporter");

            if (!Directory.Exists(appData))
                Directory.CreateDirectory(appData);

            return Path.Combine(appData, "trades.json");
        }

        /// <summary>
        /// Attempts to load user custom trades or overrides from trades.json.
        /// Searches:
        /// 1. User AppData RevitLocationExporter folder (highest priority)
        /// 2. Directory of the running assembly
        /// 3. User Desktop
        /// </summary>
        public void TryLoadFromJson()
        {
            string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string[] searchPaths =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitLocationExporter", "trades.json"),
                assemblyDir != null ? Path.Combine(assemblyDir, "trades.json") : "",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "trades.json")
            };

            foreach (string path in searchPaths)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;

                try
                {
                    string json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    using JsonDocument doc = JsonDocument.Parse(json);
                    JsonElement root = doc.RootElement;

                    var jsonOpts = CreateJsonOptions();

                    if (root.TryGetProperty("Trades", out JsonElement tradesElement))
                    {
                        foreach (JsonProperty tradeProp in tradesElement.EnumerateObject())
                        {
                            var profile = JsonSerializer.Deserialize<TradeProfile>(tradeProp.Value.GetRawText(), jsonOpts);

                            if (profile != null)
                            {
                                if (string.IsNullOrWhiteSpace(profile.TradeCode))
                                    profile.TradeCode = tradeProp.Name;

                                profile.EnsureInitialized();
                                Trades[profile.TradeCode] = profile;
                            }
                        }
                    }

                    if (root.TryGetProperty("ActiveTradeCode", out JsonElement activeTradeProp))
                    {
                        string? activeCode = activeTradeProp.GetString();
                        if (!string.IsNullOrWhiteSpace(activeCode) && Trades.ContainsKey(activeCode!))
                        {
                            ActiveTradeCode = activeCode!;
                        }
                    }

                    if (root.TryGetProperty("DefaultProject", out JsonElement defProj))
                        Settings.ProjectNumber = defProj.GetString() ?? Settings.ProjectNumber;

                    if (root.TryGetProperty("DefaultOriginator", out JsonElement defOrig))
                        Settings.Originator = defOrig.GetString() ?? Settings.Originator;

                    break; // Loaded successfully from first available configuration file
                }
                catch
                {
                    // Non-fatal, fallback to built-ins
                }
            }
        }

        /// <summary>
        /// Saves all current TradeProfiles and active trade to JSON (%AppData% by default).
        /// </summary>
        public void SaveToJson(string? targetFilePath = null)
        {
            string filePath = targetFilePath ?? GetConfigFilePath();
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var rootObj = new
            {
                DefaultProject = Settings.ProjectNumber,
                DefaultOriginator = Settings.Originator,
                ActiveTradeCode = ActiveTradeCode,
                Trades = Trades
            };

            string json = JsonSerializer.Serialize(rootObj, CreateJsonOptions());
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Exports a single trade profile to a standalone JSON file.
        /// </summary>
        public void ExportProfileToJson(string tradeCode, string filePath)
        {
            if (string.IsNullOrWhiteSpace(tradeCode) || !Trades.TryGetValue(tradeCode, out var profile))
                throw new ArgumentException($"Trade profile '{tradeCode}' not found.");

            string json = JsonSerializer.Serialize(profile, CreateJsonOptions());
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Imports one or more trade profiles from a JSON file and persists to AppData.
        /// Supports both full config files (with a "Trades" object) and single TradeProfile JSON files.
        /// </summary>
        public bool ImportFromJson(string filePath, out string? errorMessage)
        {
            errorMessage = null;
            if (!File.Exists(filePath))
            {
                errorMessage = "Selected file does not exist.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                var jsonOpts = CreateJsonOptions();

                // Case 1: Multi-trade config format
                if (root.TryGetProperty("Trades", out JsonElement tradesElement))
                {
                    foreach (JsonProperty tradeProp in tradesElement.EnumerateObject())
                    {
                        var profile = JsonSerializer.Deserialize<TradeProfile>(tradeProp.Value.GetRawText(), jsonOpts);

                        if (profile != null)
                        {
                            if (string.IsNullOrWhiteSpace(profile.TradeCode))
                                profile.TradeCode = tradeProp.Name;

                            AddOrUpdateTrade(profile);
                        }
                    }

                    if (root.TryGetProperty("ActiveTradeCode", out JsonElement activeTradeProp))
                    {
                        string? activeCode = activeTradeProp.GetString();
                        if (!string.IsNullOrWhiteSpace(activeCode) && Trades.ContainsKey(activeCode!))
                            ActiveTradeCode = activeCode!;
                    }

                    SaveToJson();
                    return true;
                }

                // Case 2: Single TradeProfile format
                var singleProfile = JsonSerializer.Deserialize<TradeProfile>(json, jsonOpts);

                if (singleProfile != null && !string.IsNullOrWhiteSpace(singleProfile.TradeCode))
                {
                    AddOrUpdateTrade(singleProfile);
                    ActiveTradeCode = singleProfile.TradeCode;
                    SaveToJson();
                    return true;
                }

                errorMessage = "Invalid JSON structure. Could not find trade profile definition.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to parse JSON file:\n{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Adds or updates a trade profile in the collection.
        /// </summary>
        public void AddOrUpdateTrade(TradeProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.TradeCode))
                throw new ArgumentException("TradeCode cannot be empty.");

            profile.TradeCode = profile.TradeCode.Trim().ToUpperInvariant();
            profile.EnsureInitialized();
            profile.InvalidateFilter();
            Trades[profile.TradeCode] = profile;
        }

        /// <summary>
        /// Deletes a trade profile from the collection.
        /// </summary>
        public bool DeleteTrade(string tradeCode)
        {
            if (string.IsNullOrWhiteSpace(tradeCode)) return false;

            if (Trades.Remove(tradeCode))
            {
                if (string.Equals(ActiveTradeCode, tradeCode, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveTradeCode = Trades.Keys.FirstOrDefault() ?? "TI0";
                }
                SaveToJson();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the currently active trade profile.
        /// </summary>
        public TradeProfile GetActiveTrade()
        {
            if (!string.IsNullOrWhiteSpace(ActiveTradeCode) && Trades.TryGetValue(ActiveTradeCode, out var profile))
                return profile;

            return Trades.Values.FirstOrDefault() ?? Trades["TI0"];
        }

        /// <summary>
        /// Sets the active trade profile by code and persists the selection.
        /// </summary>
        public void SetActiveTrade(string tradeCode)
        {
            if (!string.IsNullOrWhiteSpace(tradeCode) && Trades.ContainsKey(tradeCode))
            {
                ActiveTradeCode = tradeCode;
                SaveToJson();
            }
        }

        /// <summary>
        /// Detects trade from an ISO 19650 filename.
        /// Example: "P00100087-DAR-TI0-AFCXS1001-XX-M3-2X0001_EN.rvt"
        /// Segment 2 is "TI0" -> matches Trades["TI0"].
        /// If unknown, falls back to active trade.
        /// </summary>
        public TradeProfile DetectTrade(string filePathOrName)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePathOrName);
            string[] parts = fileName.Split('-');

            if (parts.Length >= 3)
            {
                string tradeCandidate = parts[2].Trim();
                if (Trades.TryGetValue(tradeCandidate, out var profile))
                {
                    return profile;
                }

                // Check prefix match (e.g. "EL" matching "EL0")
                foreach (var kvp in Trades)
                {
                    if (tradeCandidate.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                        kvp.Key.StartsWith(tradeCandidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Value;
                    }
                }
            }

            // Fallback to active trade
            return GetActiveTrade();
        }

        /// <summary>
        /// Gets a trade profile by code, or returns the active trade profile.
        /// </summary>
        public TradeProfile GetTrade(string tradeCode)
        {
            return Trades.TryGetValue(tradeCode, out var profile) ? profile : GetActiveTrade();
        }
    }
}
