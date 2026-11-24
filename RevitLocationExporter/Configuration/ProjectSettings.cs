using System;
using System.Collections.Generic;
using System.IO;

namespace RevitLocationExporter.Configuration
{
    /// <summary>
    /// Global project configuration for CPK (P00100087).
    /// Holds shared Survey Point coordinates, default project metadata, and location directory mappings.
    /// </summary>
    public class ProjectSettings
    {
        public string ProjectNumber { get; set; } = "P00100087";
        public string Originator { get; set; } = "DAR";

        // Survey Point constants in mm
        public double SurveyNorthMm { get; set; } = 5776800031.2;
        public double SurveyEastMm { get; set; } = 7457254602.8;
        public double SurveyElevMm { get; set; } = 98300.0;

        public string IFCJsonPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "IFCSettings.json");

        public Dictionary<string, string> ProjectData { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Project Name",             "Preparation of design documentation for Airside, Landside and Utilities by the Master Civil Engineer (MCE)" },
            { "Project Number",           "P00100087" },
            { "Client Name",              "Centralny Port Komunikacyjny spółka z ograniczoną odpowiedzialnością" },
            { "Project Address",          "Aleje Jerozolimskie 142B, 02-305 Warsaw, Republic of Poland" },
            { "Organization Name",        "Dar Al-Handasah Consultants (Shair and Partners) s.a.l" },
            { "Organization Description", "MCE Contractor" },
            { "Author",                   "DAR" },
            { "Group_Leader",             "Meghanath Gatade" },
            { "Model_Coordinator",        "Anuj Mishra" },
            { "Project Status",        "Stage 3" }
        };

        public Dictionary<string, (string WorkPackage, string WbsCode, string WbsName)> CpkFallbackData { get; set; } =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "AFCX", ("WP03", "C.A.EN.PDN.02", "Power Distribution Airside Center") },
                { "AFNX", ("WP04", "C.A.EN.PDN.01", "Power Distribution Airside North")  },
                { "AFSX", ("WP05", "C.A.EN.PDN.03", "Power Distribution Airside South")  },
                { "LFEX", ("WP07", "C.A.EN.PDN.04", "Power Distribution Landside East")  },
                { "LFWX", ("WP08", "C.A.EN.PDN.05", "Power Distribution Landside West")  }
            };

        /// <summary>
        /// Base location directory map pointing to Authoring Model folders.
        /// </summary>
        public Dictionary<string, string> LocationFolderMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            { "AFNXAGNWA", @"P:\PO22242-0100D\04_WP04_Northern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP04\Authoring Model" },
            { "AFNXAGNEA", @"P:\PO22242-0100D\04_WP04_Northern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP04\Authoring Model" },
            { "AFCXAGSEA", @"P:\PO22242-0100D\05_WP05_Southern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP05\Authoring Model" },
            { "AFSXAGSWA", @"P:\PO22242-0100D\05_WP05_Southern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP05\Authoring Model" },
            { "LFWXCP48A", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP16\Authoring Model" },
            { "LFWXCP48D", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXCP48E", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXCP48F", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFEXCP48G", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFEXCP48H", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFEXCP48I", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFEXCP48J", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFWXCP48K", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "AFCXICXXX", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP60\Authoring Model" },
            { "AFCXICNTP", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP60\Authoring Model" },
            { "AFCXICGTP", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP60\Authoring Model" },
            { "AFCXISXXX", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP61\Authoring Model" },
            { "AFCXISNTP", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP61\Authoring Model" },
            { "AFCXISGTP", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP61\Authoring Model" },
            { "AFCXIFNTP", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP62\Authoring Model" },
            { "AFCXIFGTP", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP62\Authoring Model" },
            { "LFWXIDXXX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP63\Authoring Model" },
            { "LFWXCGXXX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP26\Authoring Model" },
            { "LFEXNGXXX", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP27\Authoring Model" },
            { "LFWXGCXXX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP28\Authoring Model" },
            { "LFEXSGXXX", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP29\Authoring Model" },
            { "LFEXHNXXX", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP24\Authoring Model" },
            { "LFEXHSXXX", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP23\Authoring Model" },
            { "LFWXGPXXX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP21\Authoring Model" },
            { "LFWXGH29A", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP07\Authoring Model" },
            { "LFEXGH29C", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXGH29D", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXGH29E", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFWXGH29F", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP07\Authoring Model" },
            { "LFWXGH29G", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP07\Authoring Model" },
            { "AFCXS1001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS1002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS1003", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS1004", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS1005", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS1006", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS1007", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS1008", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS1009", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFNXS1001", @"P:\PO22242-0100D\04_WP04_Northern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP15\Authoring Model" },
            { "AFNXS1002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFSXS1001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFSXS1002", @"P:\PO22242-0100D\05_WP05_Southern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP05\Authoring Model" },
            { "LFEXS1001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFWXS1001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFWXS1003", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS2001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFNXS2003", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFNXS2004", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFSXS2002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFSXS2004", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFEXS2001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFEXS2002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFEXS2003", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFWXS2001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFWXS2002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFWXS2003", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFWXS2004", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFWXS2005", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS2003", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS3001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS3002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFNXS3001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFNXS3002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFEXS3001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXS4001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "AFCXS4002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4003", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4004", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4005", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4006", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4009", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4010", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4011", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4012", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4013", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4015", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4016", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4025", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4026", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4027", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXS4028", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4003", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4004", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4005", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4006", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4007", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4008", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4009", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4010", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4011", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4012", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4013", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4014", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4015", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4016", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4017", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4018", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4019", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXS4020", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "AFCXS5001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP61\Authoring Model" },
            { "AFCXS5002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP61\Authoring Model" },
            { "AFNXS5001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP61\Authoring Model" },
            { "AFNXS5002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP61\Authoring Model" },
            { "AFSXS5001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP61\Authoring Model" },
            { "AFSXS5002", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP61\Authoring Model" },
            { "LFWXS5001", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP61\Authoring Model" },
            { "LFEXS6001", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXS6002", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXS6003", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXS6004", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXS6005", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXREECS", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFWXRWWCS", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP07\Authoring Model" },
            { "AFSXWECBB", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP13\Authoring Model" },
            { "AFSXWEHWB", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP13\Authoring Model" },
            { "AFSXWECTR", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP13\Authoring Model" },
            { "AFSXWEELS", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "AFSXWESTF", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP13\Authoring Model" },
            { "LFEXWCCBB", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP13\Authoring Model" },
            { "LFEXWCHWB", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP13\Authoring Model" },
            { "LFEXWCCTR", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP13\Authoring Model" },
            { "LFEXWCELS", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFEXWCSTF", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP13\Authoring Model" },
            { "LFWXWCCTR", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP12\Authoring Model" },
            { "LFWXWCCBB", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP12\Authoring Model" },
            { "LFWXWCHWB", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP12\Authoring Model" },
            { "LFWXWCSTF", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP12\Authoring Model" },
            { "LFWXWCELS", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "AFSXWCCBB", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP12\Authoring Model" },
            { "AFSXWCHWB", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP12\Authoring Model" },
            { "AFSXWCCTR", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP12\Authoring Model" },
            { "AFSXWCSTF", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP12\Authoring Model" },
            { "AFSXWCELS", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "LFWXWPXXX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP19\Authoring Model" },
            { "LFWXWSADB", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSPTB", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSBL1", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSBL2", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSLDU", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSMST", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSELS", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFWXWSCLB", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSRLP", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSSSP", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSSTB", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSTSP", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSWS1", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSWS2", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSWGH", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSIPS", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXWSTPP", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "AFCXWBXXX", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXAWXXX", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP11\Authoring Model" },
            { "AFCXAWBPS", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP11\Authoring Model" },
            { "SWGDHEXXX", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP47\Authoring Model" },
            { "LFEXHEELS", @"P:\PO22242-0100D\04_WP04_Northern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP48\Authoring Model" },
            { "SWGDIEXXX", @"P:\PO22242-0100D\04_WP04_Northern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP48\Authoring Model" },
            { "AFNXIEELS", @"C:\Users\Shiv OM\Desktop\CPK\WP04" },
            { "SWGDJEXXX", @"P:\PO22242-0100D\05_WP05_Southern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP49\Authoring Model" },
            { "AFSXJEELS", @"C:\Users\Shiv OM\Desktop\CPK\WP04" },
            { "AFSXJERHT", @"P:\PO22242-0100D\05_WP05_Southern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP49\Authoring Model" },
            { "SWGDKEXXX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP50\Authoring Model" },
            { "LFWXKEELS", @"P:\PO22242-0100D\04_WP04_Northern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP48\Authoring Model" },
            { "LFWXPTXXX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP19\Authoring Model" },
            { "LFWXNTXXX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "LFWXITXXX", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP18\Authoring Model" },
            { "AFCXFTXXX", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXEFGES", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "AFCXEFGEC", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP59\Authoring Model" },
            { "AFCXEFGEG", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP59\Authoring Model" },
            { "AFCXGVGNS", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "AFCXGVGNC", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXGVGNG", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXFIADB", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP17\Authoring Model" },
            { "AFCXFIOLL", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP17\Authoring Model" },
            { "AFCXFIFPS", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP17\Authoring Model" },
            { "AFCXFIMTW", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP17\Authoring Model" },
            { "AFCXFIELS", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP17\Authoring Model" },
            { "AFSXWFGWS", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP42\Authoring Model" },
            { "AFSXWFGWC", @"P:\PO22242-0100D\05_WP05_Southern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP58\Authoring Model" },
            { "AFSXWFGWG", @"P:\PO22242-0100D\05_WP05_Southern_Airfield\WIP\EL\Models\Telecom - Building Package\Stage 3\WP58\Authoring Model" },
            { "LFWXFFADB", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP16\Authoring Model" },
            { "LFWXFFSSN", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP16\Authoring Model" },
            { "LFWXFFMTW", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP16\Authoring Model" },
            { "LFWXFFOLL", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP16\Authoring Model" },
            { "LFWXFFRCP", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP16\Authoring Model" },
            { "LFWXFFRCO", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP16\Authoring Model" },
            { "LFWXFFHPS", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP16\Authoring Model" },
            { "LFWXFFPRR", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP16\Authoring Model" },
            { "LFWXFFFPS", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP16\Authoring Model" },
            { "LFEXPSS01", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXPSS02", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXPSS03", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXPSS04", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXPSS05", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "AFCXPSS06", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "AFCXPSS07", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "AFCXPSS08", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXPSS09", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXPSS10", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXPSS11", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "AFCXPSS12", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFWXPSS13", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFWXPSS14", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "AFSXPSS15", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "AFCXPSS16", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "LFWXPSS17", @"P:\PO22242-0100D\08_WP08_Landside_West_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP07\Authoring Model" },
            { "AFNXPSS18", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "AFNXPSS19", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "LFEXPSS20", @"P:\PO22242-0100D\07_WP07_Landside_East_Zone\WIP\EL\Models\Telecom - Building Package\Stage 3\WP08\Authoring Model" },
            { "AFCXPSGP1", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
            { "AFCXPSGP2", @"P:\PO22242-0100D\03_WP03_Central_Area\WIP\EL\Models\Telecom - Building Package\Stage 3\WP54\Authoring Model" },
        };

        public bool TryGetCpkFallback(string locationName,
            out string workPackage, out string wbsCode, out string wbsName)
        {
            foreach (var kv in CpkFallbackData)
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

        /// <summary>
        /// Resolves the output folder for a location, automatically substituting the trade subfolder and discipline folder.
        /// </summary>
        public bool TryGetOutputFolder(string locationName, TradeProfile trade, out string folder)
        {
            foreach (var kv in LocationFolderMap)
            {
                if (locationName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string basePath = kv.Value;

                    // Dynamically swap the trade package subfolder if different from "Telecom - Building Package"
                    if (!string.Equals(trade.WorkPackageSubfolder, "Telecom - Building Package", StringComparison.OrdinalIgnoreCase))
                    {
                        basePath = basePath.Replace(@"\Telecom - Building Package\", $@"\{trade.WorkPackageSubfolder}\");
                    }

                    // Dynamically swap the discipline folder if different from "EL"
                    if (!string.Equals(trade.DisciplineFolder, "EL", StringComparison.OrdinalIgnoreCase))
                    {
                        if (basePath.IndexOf(@"\WIP\EL\", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            basePath = basePath.Replace(@"\WIP\EL\", $@"\WIP\{trade.DisciplineFolder}\");
                        }
                        else if (basePath.IndexOf(@"\EL\", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            basePath = basePath.Replace(@"\EL\", $@"\{trade.DisciplineFolder}\");
                        }
                    }

                    // If the path points to an unreachable drive (e.g. remote P:\ on development laptop),
                    // automatically redirect to Desktop CPK development folder so exports never fail.
                    string root = Path.GetPathRoot(basePath);
                    if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
                    {
                        string desktopCpk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CPK");
                        if (Directory.Exists(desktopCpk))
                        {
                            string matchedWp = "WP04";
                            var match = System.Text.RegularExpressions.Regex.Match(basePath, @"(WP\d+)");
                            if (match.Success)
                            {
                                matchedWp = match.Groups[1].Value;
                            }
                            string devFolder = Path.Combine(desktopCpk, matchedWp);
                            if (!Directory.Exists(devFolder))
                            {
                                try { Directory.CreateDirectory(devFolder); } catch { }
                            }
                            folder = devFolder;
                            return true;
                        }
                    }

                    folder = basePath;
                    return true;
                }
            }

            folder = string.Empty;
            return false;
        }
    }
}
