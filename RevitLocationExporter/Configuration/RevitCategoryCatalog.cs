using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitLocationExporter.Configuration
{
    /// <summary>
    /// Item representing a Revit category with display metadata.
    /// </summary>
    public class CategoryCatalogItem
    {
        public string DisplayName { get; set; }
        public string BuiltInCategoryName { get; set; }
        public string Discipline { get; set; }

        public CategoryCatalogItem(string displayName, string builtInCategoryName, string discipline)
        {
            DisplayName = displayName;
            BuiltInCategoryName = builtInCategoryName;
            Discipline = discipline;
        }

        public override string ToString() => $"{DisplayName} ({BuiltInCategoryName})";
    }

    /// <summary>
    /// Provides a catalog of standard Revit categories categorized by discipline.
    /// Used by the Trade Profile Builder UI to allow modelers to pick categories cleanly.
    /// </summary>
    public static class RevitCategoryCatalog
    {
        private static readonly List<CategoryCatalogItem> _catalog = new()
        {
            // ── Telecom & Low Voltage ──────────────────────────────────────────
            new("Fire Alarm Devices", "OST_FireAlarmDevices", "Telecom & Low Voltage"),
            new("Data Devices", "OST_DataDevices", "Telecom & Low Voltage"),
            new("Security Devices", "OST_SecurityDevices", "Telecom & Low Voltage"),
            new("Telephone Devices", "OST_TelephoneDevices", "Telecom & Low Voltage"),
            new("Audio Visual Devices", "OST_AudioVisualDevices", "Telecom & Low Voltage"),
            new("Communication Devices", "OST_CommunicationDevices", "Telecom & Low Voltage"),
            new("Cable Trays", "OST_CableTray", "Telecom & Low Voltage"),
            new("Cable Tray Fittings", "OST_CableTrayFitting", "Telecom & Low Voltage"),
            new("Conduits", "OST_Conduit", "Telecom & Low Voltage"),
            new("Conduit Fittings", "OST_ConduitFitting", "Telecom & Low Voltage"),

            // ── Electrical ─────────────────────────────────────────────────────
            new("Electrical Equipment", "OST_ElectricalEquipment", "Electrical"),
            new("Electrical Fixtures", "OST_ElectricalFixtures", "Electrical"),
            new("Lighting Fixtures", "OST_LightingFixtures", "Electrical"),
            new("Lighting Devices / Switches", "OST_LightingDevices", "Electrical"),
            new("Wires", "OST_Wire", "Electrical"),

            // ── Mechanical & HVAC ──────────────────────────────────────────────
            new("Ducts", "OST_DuctCurves", "Mechanical / HVAC"),
            new("Duct Fittings", "OST_DuctFitting", "Mechanical / HVAC"),
            new("Duct Accessories (Dampers)", "OST_DuctAccessory", "Mechanical / HVAC"),
            new("Air Terminals / Diffusers", "OST_DuctTerminal", "Mechanical / HVAC"),
            new("Mechanical Equipment (AHU/FCU/Fans)", "OST_MechanicalEquipment", "Mechanical / HVAC"),
            new("Duct Insulations", "OST_DuctInsulations", "Mechanical / HVAC"),
            new("Duct Liners", "OST_DuctLiners", "Mechanical / HVAC"),

            // ── Plumbing ───────────────────────────────────────────────────────
            new("Pipes", "OST_PipeCurves", "Plumbing"),
            new("Pipe Fittings", "OST_PipeFitting", "Plumbing"),
            new("Pipe Accessories (Valves)", "OST_PipeAccessory", "Plumbing"),
            new("Plumbing Fixtures", "OST_PlumbingFixtures", "Plumbing"),
            new("Pipe Insulations", "OST_PipeInsulations", "Plumbing"),

            // ── Fire Protection ────────────────────────────────────────────────
            new("Sprinklers", "OST_Sprinklers", "Fire Protection"),

            // ── Architecture ───────────────────────────────────────────────────
            new("Walls", "OST_Walls", "Architecture"),
            new("Doors", "OST_Doors", "Architecture"),
            new("Windows", "OST_Windows", "Architecture"),
            new("Floors", "OST_Floors", "Architecture"),
            new("Ceilings", "OST_Ceilings", "Architecture"),
            new("Roofs", "OST_Roofs", "Architecture"),
            new("Rooms", "OST_Rooms", "Architecture"),
            new("Curtain Panels", "OST_CurtainWallPanels", "Architecture"),
            new("Curtain Wall Mullions", "OST_CurtainWallMullions", "Architecture"),
            new("Stairs", "OST_Stairs", "Architecture"),
            new("Railings", "OST_Railings", "Architecture"),
            new("Columns (Architectural)", "OST_Columns", "Architecture"),

            // ── Structure ──────────────────────────────────────────────────────
            new("Structural Columns", "OST_StructuralColumns", "Structure"),
            new("Structural Framing (Beams/Trusses)", "OST_StructuralFraming", "Structure"),
            new("Structural Foundations", "OST_StructuralFoundation", "Structure"),
            new("Structural Rebar", "OST_Rebar", "Structure"),
            new("Structural Stiffeners", "OST_StructuralStiffener", "Structure"),

            // ── Specialty & Site ───────────────────────────────────────────────
            new("Generic Models", "OST_GenericModel", "Specialty & Site"),
            new("Specialty Equipment", "OST_SpecialityEquipment", "Specialty & Site"),
            new("Site", "OST_Site", "Specialty & Site"),
            new("Topography", "OST_Topography", "Specialty & Site")
        };

        public static IReadOnlyList<CategoryCatalogItem> AllCategories => _catalog;

        /// <summary>
        /// Validates if a string is a valid Revit BuiltInCategory enum value.
        /// </summary>
        public static bool IsValidBuiltInCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return false;
            return Enum.TryParse<BuiltInCategory>(categoryName.Trim(), out _);
        }

        /// <summary>
        /// Gets friendly display name for a BuiltInCategory string name, or returns the name itself.
        /// </summary>
        public static string GetFriendlyName(string builtInCategoryName)
        {
            var match = _catalog.FirstOrDefault(c =>
                c.BuiltInCategoryName.Equals(builtInCategoryName, StringComparison.OrdinalIgnoreCase));
            return match != null ? $"{match.DisplayName} ({match.BuiltInCategoryName})" : builtInCategoryName;
        }
    }
}
