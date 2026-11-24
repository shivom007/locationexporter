// ═══════════════════════════════════════════════════════════════════════════
//  ModelStandardsEnforcer.cs  –  RevitLocationExporter.Core
//
//  Reads Autodesk Model Checker check-sets (MCSettings XML) and, BEFORE purge /
//  export, makes the model satisfy the checks that can be satisfied
//  automatically:
//
//    1. RequiredParam  : "Parameter X Defined + HasNoValue"  -> fill X
//    2. EnforceValue   : "Parameter X NotEqual 'V'" (Project Information) -> set V
//    3. Workset        : "Workset Name DoesNotContain/NotEqual 'W'" -> move element
//    4. Pattern        : "WildCardNoMatch '<regex>'" -> report only (never renamed)
//
//  Fill-value priority for a missing parameter:
//    a) Standards_Fill tab rows (Locator / Category / Family-Type pattern)
//    b) location values from Element_Params + Sheet_Params (with aliases)
//    c) values derived from the element itself (category, level, length ...)
//    d) nothing found -> reported as MISSING (never filled with dummy text)
//
//  Every action is written to <export>\<RvtFileName>_ModelStandards.csv
//
//  Target: Revit 2024 API (.NET 4.8, C# 8+ / LangVersion latest)
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

namespace RevitLocationExporter.Core
{
    public enum StandardsRuleKind { RequiredParam, EnforceValue, Workset, Pattern }

    public sealed class StandardsRule
    {
        public StandardsRuleKind Kind;
        public string CheckSet = "";
        public string Section = "";
        public string CheckName = "";
        public string Parameter = "";          // param name or BuiltInParameter name
        public string Value = "";              // EnforceValue: required value / Workset: token / Pattern: regex
        public string PatternTarget = "";      // Pattern: "Parameter" | "Family" | "Type"
        public bool WorksetExact;              // Workset: NotEqual (exact) vs DoesNotContain
        public List<string> AllowedWorksets = new List<string>();
        public List<string> TypeNameContains = new List<string>(); // link rules (ELEM_TYPE_PARAM Contains)
        public bool? IsElementType;            // null = not specified
        public HashSet<BuiltInCategory> Categories = new HashSet<BuiltInCategory>();
        public List<string> ExcludeWorksetContains = new List<string>();

        public string Key => $"{Kind}|{Parameter}|{Value}|{IsElementType}|{string.Join(",", Categories.OrderBy(c => (int)c))}|{string.Join(",", AllowedWorksets)}|{string.Join(",", TypeNameContains)}";
    }

    public sealed class StandardsFillRow
    {
        public string Locator = "*";
        public string Category = "*";
        public string FamilyType = "*";
        public string Parameter = "";
        public string Value = "";
        public int Specificity =>
            (Locator == "*" ? 0 : 4) + (Category == "*" ? 0 : 2) + (FamilyType == "*" ? 0 : 1);
    }

    public sealed class StandardsContext
    {
        public string LocationCode = "";
        public string RvtFileName = "";
        public string ReportFolder = "";
        public bool ApplyFixes = true;   // false = report only (dry run)
        public IDictionary<string, string> LocationValues =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<StandardsFillRow> FillRows = new List<StandardsFillRow>();
    }

    public sealed class StandardsFinding
    {
        public string CheckSet = "", CheckName = "", Status = "", ElementId = "", Category = "",
                      ElementName = "", Parameter = "", OldValue = "", NewValue = "", Source = "";
    }

    // ───────────────────────────────────────────────────────────────────────
    //  LOADER
    // ───────────────────────────────────────────────────────────────────────
    public static class ModelCheckerSetLoader
    {
        /// <summary>
        /// Loads ModelCheckers\_ALL\*.xml plus ModelCheckers\&lt;TradeCode&gt;\*.xml.
        /// Headings/sections named "&lt;--Unused--&gt;" and unchecked items are ignored.
        /// </summary>
        public static List<StandardsRule> LoadForTrade(string rootFolder, string tradeCode, ProcessLog log)
        {
            var files = new List<string>();
            foreach (string sub in new[] { "_ALL", tradeCode ?? "" })
            {
                if (string.IsNullOrWhiteSpace(sub)) continue;
                string dir = Path.Combine(rootFolder, sub);
                if (Directory.Exists(dir))
                    files.AddRange(Directory.GetFiles(dir, "*.xml"));
            }

            var rules = new Dictionary<string, StandardsRule>();
            foreach (string f in files)
            {
                try
                {
                    int before = rules.Count;
                    foreach (var r in Load(f))
                        if (!rules.ContainsKey(r.Key)) rules[r.Key] = r;
                    log.Info(tradeCode, $"Model Checker set '{Path.GetFileName(f)}': {rules.Count - before} enforceable rule(s).");
                }
                catch (Exception ex)
                {
                    log.Warning(tradeCode, $"Could not read Model Checker set '{Path.GetFileName(f)}': {ex.Message}");
                }
            }
            return rules.Values.ToList();
        }

        private static readonly Dictionary<string, List<StandardsRule>> Cache =
            new Dictionary<string, List<StandardsRule>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Default folder: &lt;add-in dll folder&gt;\ModelCheckers</summary>
        public static string DefaultFolder =>
            Path.Combine(Path.GetDirectoryName(typeof(ModelCheckerSetLoader).Assembly.Location) ?? "", "ModelCheckers");

        /// <summary>Loads once per trade code for the whole batch run.</summary>
        public static List<StandardsRule> GetForTrade(string tradeCode, ProcessLog log, string? folder = null)
        {
            string key = (folder ?? DefaultFolder) + "|" + tradeCode;
            if (!Cache.TryGetValue(key, out var rules))
            {
                rules = LoadForTrade(folder ?? DefaultFolder, tradeCode, log);
                Cache[key] = rules;
            }
            return rules;
        }

        public static void ClearCache() => Cache.Clear();

        public static IEnumerable<StandardsRule> Load(string xmlPath)
        {
            XDocument xd = XDocument.Load(xmlPath);
            XElement root = xd.Root ?? throw new InvalidDataException("Empty XML.");
            string setName = (string?)root.Attribute("Name") ?? Path.GetFileNameWithoutExtension(xmlPath);

            foreach (XElement heading in root.Elements("Heading"))
            {
                if (IsUnused(heading, "HeadingText")) continue;
                foreach (var (section, check) in WalkSections(heading.Elements("Section")))
                {
                    if (!IsTrue(check.Attribute("IsChecked"))) continue;
                    if (!string.Equals((string?)check.Attribute("CheckType"), "Custom", StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (var rule in ParseCheck(setName, section, check))
                        yield return rule;
                }
            }
        }

        private static IEnumerable<(string Section, XElement Check)> WalkSections(IEnumerable<XElement> sections)
        {
            foreach (XElement s in sections)
            {
                if (IsUnused(s, "SectionName")) continue;
                string name = ((string?)s.Attribute("SectionName") ?? "").Trim();
                foreach (XElement c in s.Elements("Check")) yield return (name, c);
                foreach (var nested in WalkSections(s.Elements("Section"))) yield return nested;
            }
        }

        private static bool IsUnused(XElement e, string attr) =>
            !IsTrue(e.Attribute("IsChecked")) ||
            (((string?)e.Attribute(attr)) ?? "").IndexOf("Unused", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsTrue(XAttribute? a) =>
            a == null || !string.Equals(a.Value, "False", StringComparison.OrdinalIgnoreCase);

        private sealed class F { public string Op = "", Cat = "", Prop = "", Cond = "", Val = ""; }

        private static IEnumerable<StandardsRule> ParseCheck(string setName, string section, XElement check)
        {
            string checkName = ((string?)check.Attribute("CheckName") ?? "").Trim();
            var fs = check.Elements("Filter").Select(x => new F
            {
                Op = (string?)x.Attribute("Operator") ?? "",
                Cat = (string?)x.Attribute("Category") ?? "",
                Prop = ((string?)x.Attribute("Property") ?? "").Trim(),
                Cond = (string?)x.Attribute("Condition") ?? "",
                Val = (string?)x.Attribute("Value") ?? ""
            }).ToList();

            // Checks filtered by API class / APIParameter / Redundant are Model-Checker-only (report side)
            if (fs.Any(f => f.Cat == "APIType" || f.Cat == "Redundant" || f.Cat == "View")) yield break;

            var cats = new HashSet<BuiltInCategory>();
            foreach (var f in fs.Where(f => f.Cat == "Category" && f.Cond == "Included" && f.Op != "Exclude"))
                if (Enum.TryParse(f.Prop, out BuiltInCategory bic)) cats.Add(bic);
            if (cats.Count == 0) yield break;

            bool? isType = null;
            var t = fs.FirstOrDefault(f => f.Cat == "TypeOrInstance");
            if (t != null) isType = string.Equals(t.Val, "True", StringComparison.OrdinalIgnoreCase);

            var excludeWs = fs.Where(f => f.Op == "Exclude" && f.Cond == "Contains" &&
                                          (f.Cat == "Workset" || (f.Cat == "Parameter" && f.Prop == "Workset")))
                              .Select(f => f.Val).Distinct().ToList();

            var pf = fs.Where(f => f.Cat == "Parameter" && f.Op != "Exclude" && f.Prop != "Workset").ToList();

            StandardsRule New(StandardsRuleKind k) => new StandardsRule
            {
                Kind = k, CheckSet = setName, Section = section, CheckName = checkName,
                Categories = new HashSet<BuiltInCategory>(cats), IsElementType = isType,
                ExcludeWorksetContains = new List<string>(excludeWs)
            };

            // 1) Required parameters (HasNoValue). Prefer the parameter named like the check.
            var empties = pf.Where(f => f.Cond == "HasNoValue" && IsTrueStr(f.Val)).Select(f => f.Prop).Distinct().ToList();
            var enforce = pf.Where(f => f.Cond == "NotEqual" && cats.Contains(BuiltInCategory.OST_ProjectInformation)).ToList();

            if (enforce.Count > 0)
            {
                foreach (var f in enforce)
                {
                    var r = New(StandardsRuleKind.EnforceValue); r.Parameter = f.Prop; r.Value = f.Val; yield return r;
                }
            }
            else if (empties.Count > 0)
            {
                var primary = empties.Where(p => string.Equals(p, checkName, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (string p in primary.Count > 0 ? primary : empties)
                {
                    var r = New(StandardsRuleKind.RequiredParam); r.Parameter = p; yield return r;
                }
            }

            // 2) Pattern checks (report only)
            foreach (var f in fs.Where(f => f.Cond == "WildCardNoMatch" && f.Op != "Exclude" &&
                                            (f.Cat == "Parameter" || f.Cat == "Family" || f.Cat == "Type")))
            {
                var r = New(StandardsRuleKind.Pattern);
                r.PatternTarget = f.Cat; r.Parameter = f.Prop; r.Value = f.Val;
                yield return r;
            }

            // 3) Workset placement by category (optionally by link type name)
            var wsF = fs.Where(f => f.Cat == "Workset" && f.Op == "And" &&
                                    (f.Cond == "DoesNotContain" || f.Cond == "NotEqual")).ToList();
            if (wsF.Count > 0 && pf.All(f => f.Prop == "ELEM_TYPE_PARAM" && f.Cond == "Contains"))
            {
                var r = New(StandardsRuleKind.Workset);
                r.WorksetExact = wsF.All(f => f.Cond == "NotEqual");
                r.AllowedWorksets = wsF.Select(f => f.Val).Distinct().ToList();
                r.TypeNameContains = pf.Select(f => f.Val).Distinct().ToList();
                yield return r;
            }
        }

        private static bool IsTrueStr(string s) => !string.Equals(s, "False", StringComparison.OrdinalIgnoreCase);
    }

    // ───────────────────────────────────────────────────────────────────────
    //  FILL TAB READER  (rows from Excel tab "Standards_Fill")
    // ───────────────────────────────────────────────────────────────────────
    public static class StandardsFillReader
    {
        /// <summary>Build rows from a generic table: header row + data rows (already read by the Excel reader).</summary>
        public static List<StandardsFillRow> FromTable(IList<string> headers, IEnumerable<IList<string>> rows)
        {
            int Ix(params string[] names) =>
                headers.Select((h, i) => (h: (h ?? "").Trim(), i))
                       .Where(x => names.Any(n => string.Equals(x.h, n, StringComparison.OrdinalIgnoreCase)))
                       .Select(x => x.i).DefaultIfEmpty(-1).First();

            int iLoc = Ix("Locator Code", "Locator"), iCat = Ix("Category"), iFt = Ix("Family Type", "Family:Type", "Type");
            int iPar = Ix("Parameter"), iVal = Ix("Value");
            var result = new List<StandardsFillRow>();
            if (iPar < 0 || iVal < 0) return result;

            string Cell(IList<string> r, int i) => i >= 0 && i < r.Count ? (r[i] ?? "").Trim() : "";

            foreach (var r in rows)
            {
                string p = Cell(r, iPar), v = Cell(r, iVal);
                if (p.Length == 0 || v.Length == 0) continue;            // blank value = ignored
                result.Add(new StandardsFillRow
                {
                    Locator = Blank(Cell(r, iLoc)), Category = Blank(Cell(r, iCat)),
                    FamilyType = Blank(Cell(r, iFt)), Parameter = p, Value = v
                });
            }
            return result;
        }
        private static string Blank(string s) => s.Length == 0 ? "*" : s;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  ENFORCER
    // ───────────────────────────────────────────────────────────────────────
    public static class ModelStandardsEnforcer
    {
        // Spreadsheet column  ->  model parameter aliases
        private static readonly Dictionary<string, string[]> Aliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["CPK_Work_Package"] = new[] { "CPK_Work_Package", "Work_Package", "CPK_WorkPackage", "WorkPackage", "WP" },
                ["CPK_WBS_1-3b"]     = new[] { "CPK_WBS_1-3b", "CPK_WBS_1-3" },
                ["CPK_WBS_1-3"]      = new[] { "CPK_WBS_1-3", "CPK_WBS_1-3b" },
                ["CPK_Location_Code"] = new[] { "CPK_Location_Code", "Locator Code" },
            };

        public static List<StandardsFinding> Run(
            Document doc,
            IReadOnlyCollection<StandardsRule> rules,
            StandardsContext ctx,
            ProcessLog log,
            Action<Parameter, string> setParamValue)
        {
            var findings = new List<StandardsFinding>();
            if (rules.Count == 0) return findings;

            var userWorksets = doc.IsWorkshared
                ? new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToList()
                : new List<Workset>();

            using (var tx = new Transaction(doc, "Enforce Model Checker Standards"))
            {
                tx.Start();
                var typeDone = new HashSet<string>();

                foreach (var rule in rules)
                {
                    try
                    {
                        switch (rule.Kind)
                        {
                            case StandardsRuleKind.EnforceValue:
                                DoEnforceValue(doc, rule, ctx, findings, setParamValue); break;
                            case StandardsRuleKind.RequiredParam:
                                DoRequired(doc, rule, ctx, findings, setParamValue, typeDone); break;
                            case StandardsRuleKind.Workset:
                                DoWorkset(doc, rule, ctx, findings, userWorksets); break;
                            case StandardsRuleKind.Pattern:
                                DoPattern(doc, rule, findings); break;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Warning(ctx.LocationCode, $"Standards rule '{rule.CheckName}' failed: {ex.Message}");
                    }
                }

                if (ctx.ApplyFixes) tx.Commit(); else tx.RollBack();
            }

            // Summary
            foreach (var g in findings.GroupBy(f => f.Status).OrderBy(g => g.Key))
                log.Info(ctx.LocationCode, $"Model standards: {g.Count()} × {g.Key}");
            foreach (var g in findings.Where(f => f.Status == "MISSING")
                                      .GroupBy(f => f.Parameter).OrderByDescending(g => g.Count()).Take(15))
                log.Warning(ctx.LocationCode, $"Model standards: '{g.Key}' still empty on {g.Count()} element(s) — add a Standards_Fill row.");

            WriteReport(ctx, findings, log);
            return findings;
        }

        // ── Collectors ────────────────────────────────────────────────────
        private static IEnumerable<Element> Collect(Document doc, StandardsRule rule)
        {
            var col = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(rule.Categories.ToList()));
            col = rule.IsElementType == true ? col.WhereElementIsElementType() : col.WhereElementIsNotElementType();

            foreach (Element e in col)
            {
                if (e is View v && v.IsTemplate) continue;   // matches "Exclude View Template" in the check-sets
                if (rule.ExcludeWorksetContains.Count > 0 && doc.IsWorkshared)
                {
                    string ws = WorksetName(doc, e);
                    if (rule.ExcludeWorksetContains.Any(x => ws.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;
                }
                yield return e;
            }
        }

        // ── 1. Project-information fixed values ──────────────────────────
        private static void DoEnforceValue(Document doc, StandardsRule rule, StandardsContext ctx,
                                           List<StandardsFinding> findings, Action<Parameter, string> set)
        {
            Element pi = doc.ProjectInformation;
            Parameter? p = GetParam(pi, rule.Parameter);
            if (p == null) { findings.Add(F(rule, pi, "NOT_DEFINED", "", "", "")); return; }

            string current = ValueText(p);
            // An explicit Standards_Fill / spreadsheet value wins over the check-set value
            string target = ResolveConfigured(doc, pi, rule.Parameter, ctx, out string src) ?? rule.Value;
            if (src.Length == 0) src = "Check-set value";

            if (string.Equals(current, target, StringComparison.Ordinal)) return;
            if (p.IsReadOnly) { findings.Add(F(rule, pi, "READ_ONLY", current, target, src)); return; }

            set(p, target);
            findings.Add(F(rule, pi, "FILLED", current, target, src));
        }

        // ── 2. Required (non-empty) parameters ───────────────────────────
        private static void DoRequired(Document doc, StandardsRule rule, StandardsContext ctx,
                                       List<StandardsFinding> findings, Action<Parameter, string> set,
                                       HashSet<string> typeDone)
        {
            foreach (Element e in Collect(doc, rule))
            {
                Element owner = e;
                Parameter? p = GetParam(e, rule.Parameter);
                if (p == null && rule.IsElementType != true && doc.GetElement(e.GetTypeId()) is ElementType et)
                {
                    p = GetParam(et, rule.Parameter);
                    owner = et;
                }
                if (p == null) continue;                    // "Defined" filter fails -> Model Checker skips it too
                if (HasValue(p)) continue;

                // Type parameter shared by many instances: handle once
                if (owner is ElementType && !typeDone.Add($"{owner.Id}|{rule.Parameter}")) continue;

                if (p.IsReadOnly) { findings.Add(F(rule, owner, "READ_ONLY", "", "", "")); continue; }

                string? text = ResolveConfigured(doc, e, rule.Parameter, ctx, out string src);
                if (text != null)
                {
                    set(p, ExpandTokens(doc, e, text, ctx));
                    findings.Add(F(rule, owner, "FILLED", "", ValueText(p), src));
                    continue;
                }

                if (TryDerive(doc, e, rule.Parameter, ctx, p, set, out string derivedSrc))
                {
                    findings.Add(F(rule, owner, "FILLED", "", ValueText(p), derivedSrc));
                    continue;
                }

                findings.Add(F(rule, owner, "MISSING", "", "", "No source value"));
            }
        }

        // ── 3. Workset placement ─────────────────────────────────────────
        private static void DoWorkset(Document doc, StandardsRule rule, StandardsContext ctx,
                                      List<StandardsFinding> findings, List<Workset> userWorksets)
        {
            if (!doc.IsWorkshared) return;

            foreach (Element e in Collect(doc, rule))
            {
                if (rule.TypeNameContains.Count > 0)
                {
                    string typeName = e.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "";
                    if (!rule.TypeNameContains.Any(t => typeName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;
                }

                string current = WorksetName(doc, e);
                bool ok = rule.WorksetExact
                    ? rule.AllowedWorksets.Any(a => string.Equals(current, a, StringComparison.OrdinalIgnoreCase))
                    : rule.AllowedWorksets.Any(a => current.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0);
                if (ok) continue;

                // Exact rules (NotEqual): first allowed name that exists, in check-set order.
                // Token rules (DoesNotContain): the workset name must be unique, otherwise report.
                var candidates = rule.WorksetExact
                    ? rule.AllowedWorksets
                          .Select(a => userWorksets.FirstOrDefault(w => string.Equals(w.Name, a, StringComparison.OrdinalIgnoreCase)))
                          .Where(w => w != null).Take(1).Cast<Workset>().ToList()
                    : userWorksets.Where(w => rule.AllowedWorksets.Any(a =>
                          w.Name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

                if (candidates.Count != 1)
                {
                    findings.Add(F(rule, e, candidates.Count == 0 ? "WORKSET_NOT_FOUND" : "WORKSET_AMBIGUOUS",
                        current, string.Join(" | ", rule.AllowedWorksets), $"{candidates.Count} candidate workset(s)"));
                    continue;
                }

                Parameter? wp = e.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (wp == null || wp.IsReadOnly)
                {
                    findings.Add(F(rule, e, "READ_ONLY", current, candidates[0].Name, "Workset parameter locked (group/host?)"));
                    continue;
                }
                wp.Set(candidates[0].Id.IntegerValue);
                findings.Add(F(rule, e, "MOVED", current, candidates[0].Name, "Model Checker workset rule"));
            }
        }

        // ── 4. Naming / format patterns (report only) ────────────────────
        private static void DoPattern(Document doc, StandardsRule rule, List<StandardsFinding> findings)
        {
            Regex rx;
            try { rx = new Regex(rule.Value, RegexOptions.CultureInvariant); }
            catch { return; }

            var seen = new HashSet<string>();
            foreach (Element e in Collect(doc, rule))
            {
                string text;
                if (rule.PatternTarget == "Family")
                    text = (e as FamilyInstance)?.Symbol?.FamilyName
                           ?? (doc.GetElement(e.GetTypeId()) as ElementType)?.FamilyName ?? "";
                else if (rule.PatternTarget == "Type")
                    text = doc.GetElement(e.GetTypeId())?.Name ?? "";
                else
                {
                    var p = GetParam(e, rule.Parameter) ??
                            (doc.GetElement(e.GetTypeId()) is ElementType t ? GetParam(t, rule.Parameter) : null);
                    if (p == null) continue;
                    text = ValueText(p);
                }
                if (rx.IsMatch(text)) continue;
                if (!seen.Add(text)) continue;   // one line per distinct bad value
                findings.Add(F(rule, e, "PATTERN_FAIL", text, rule.Value, "Report only — rename manually"));
            }
        }

        // ── Value resolution ─────────────────────────────────────────────
        private static string? ResolveConfigured(Document doc, Element e, string param, StandardsContext ctx, out string source)
        {
            source = "";
            string cat = e.Category != null ? e.Category.Name : "";
            string bic = e.Category != null ? e.Category.BuiltInCategory.ToString() : "";
            string famType = FamilyTypeName(doc, e);

            var row = ctx.FillRows
                .Where(r => string.Equals(r.Parameter, param, StringComparison.OrdinalIgnoreCase))
                .Where(r => r.Locator == "*" || string.Equals(r.Locator, ctx.LocationCode, StringComparison.OrdinalIgnoreCase))
                .Where(r => r.Category == "*" || string.Equals(r.Category, cat, StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(r.Category, bic, StringComparison.OrdinalIgnoreCase))
                .Where(r => r.FamilyType == "*" || Glob(r.FamilyType, famType))
                .OrderByDescending(r => r.Specificity)
                .FirstOrDefault();
            if (row != null) { source = "Standards_Fill"; return row.Value; }

            var keys = Aliases.TryGetValue(param, out var a) ? a : new[] { param };
            foreach (string k in keys)
                if (ctx.LocationValues.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                { source = $"Spreadsheet '{k}'"; return v; }

            if (string.Equals(param, "CPK_RVT_File_Name", StringComparison.OrdinalIgnoreCase) && ctx.RvtFileName.Length > 0)
            { source = "Computed RVT file name"; return ctx.RvtFileName; }

            return null;
        }

        private static bool TryDerive(Document doc, Element e, string param, StandardsContext ctx,
                                      Parameter target, Action<Parameter, string> set, out string source)
        {
            source = "";
            switch (param.ToUpperInvariant())
            {
                case "CPK_ELEMENT_ID":
                    set(target, e.Id.Value.ToString(CultureInfo.InvariantCulture)); source = "Derived: ElementId"; return true;
                case "CPK_CATEGORY":
                    if (e.Category == null) return false;
                    set(target, e.Category.Name); source = "Derived: Category"; return true;
                case "CPK_FAMILY_TYPE_NAME":
                    string ft = FamilyTypeName(doc, e);
                    if (ft.Length == 0) return false;
                    set(target, ft); source = "Derived: Family : Type"; return true;
                case "CPK_LEVEL":
                    string lv = LevelName(doc, e);
                    if (lv.Length == 0) return false;
                    set(target, lv); source = "Derived: Level"; return true;
                case "CPK_MATERIAL":
                    string m = MaterialName(doc, e);
                    if (m.Length == 0) return false;
                    set(target, m); source = "Derived: Material"; return true;
                case "CPK_LENGTH":   return CopyFrom(e, target, set, out source, BuiltInParameter.CURVE_ELEM_LENGTH);
                case "CPK_WIDTH":    return CopyFrom(e, target, set, out source, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM, BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                case "CPK_HEIGHT":   return CopyFrom(e, target, set, out source, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                case "CPK_DIAMETER": return CopyFrom(e, target, set, out source, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                case "CPK_AREA":     return CopyFrom(e, target, set, out source, BuiltInParameter.HOST_AREA_COMPUTED);
                case "CPK_VOLUME":   return CopyFrom(e, target, set, out source, BuiltInParameter.HOST_VOLUME_COMPUTED);
                case "CPK_TOP_ELEVATION":
                    return CopyFrom(e, target, set, out source, BuiltInParameter.RBS_CTC_TOP_ELEVATION)
                        || BoxElevation(doc, e, target, set, top: true, out source);
                case "CPK_BOTTOM_ELEVATION":
                    return CopyFrom(e, target, set, out source, BuiltInParameter.RBS_CTC_BOTTOM_ELEVATION)
                        || BoxElevation(doc, e, target, set, top: false, out source);
                default:
                    return false;  // Stage, PAL, Description, Notes, Fire rating, CPK_E_* ... -> Standards_Fill
            }
        }

        private static bool CopyFrom(Element e, Parameter target, Action<Parameter, string> set,
                                     out string source, params BuiltInParameter[] bips)
        {
            source = "";
            foreach (var bip in bips)
            {
                Parameter? s = e.get_Parameter(bip);
                if (s == null || !s.HasValue || s.StorageType != StorageType.Double) continue;

                if (target.StorageType == StorageType.Double &&
                    target.Definition.GetDataType() == s.Definition.GetDataType())
                    target.Set(s.AsDouble());
                else if (target.StorageType == StorageType.Double)
                    target.SetValueString(s.AsValueString());
                else
                    set(target, s.AsValueString());

                source = $"Derived: {bip}";
                return true;
            }
            return false;
        }

        /// <summary>Bounding-box Z relative to the Project Base Point elevation (ASSUMPTION — confirm datum).</summary>
        private static bool BoxElevation(Document doc, Element e, Parameter target, Action<Parameter, string> set,
                                         bool top, out string source)
        {
            source = "";
            BoundingBoxXYZ? bb = e.get_BoundingBox(null);
            if (bb == null) return false;
            double z = (top ? bb.Max.Z : bb.Min.Z) - BasePoint.GetProjectBasePoint(doc).Position.Z;

            if (target.StorageType == StorageType.Double) target.Set(z);
            else set(target, UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, z, false));
            source = "Derived: bounding box vs Project Base Point";
            return true;
        }

        private static string ExpandTokens(Document doc, Element e, string v, StandardsContext ctx) =>
            v.Replace("{Location}", ctx.LocationCode)
             .Replace("{FileName}", ctx.RvtFileName)
             .Replace("{Category}", e.Category?.Name ?? "")
             .Replace("{FamilyType}", FamilyTypeName(doc, e))
             .Replace("{Level}", LevelName(doc, e))
             .Replace("{ElementId}", e.Id.Value.ToString(CultureInfo.InvariantCulture));

        // ── Helpers ──────────────────────────────────────────────────────
        private static Parameter? GetParam(Element e, string name)
        {
            if (Enum.TryParse(name, out BuiltInParameter bip) && Enum.IsDefined(typeof(BuiltInParameter), bip))
            {
                var p = e.get_Parameter(bip);
                if (p != null) return p;
            }
            return e.LookupParameter(name);
        }

        private static bool HasValue(Parameter p)
        {
            if (!p.HasValue) return false;
            switch (p.StorageType)
            {
                case StorageType.String:    return !string.IsNullOrWhiteSpace(p.AsString());
                case StorageType.ElementId: return p.AsElementId() != ElementId.InvalidElementId;
                default:                    return true;
            }
        }

        private static string ValueText(Parameter p) =>
            p.StorageType == StorageType.String ? (p.AsString() ?? "") : (p.AsValueString() ?? "");

        private static string WorksetName(Document doc, Element e)
        {
            try { return doc.GetWorksetTable().GetWorkset(e.WorksetId)?.Name ?? ""; }
            catch { return ""; }
        }

        private static string FamilyTypeName(Document doc, Element e)
        {
            if (e is FamilyInstance fi && fi.Symbol != null) return $"{fi.Symbol.FamilyName} : {fi.Symbol.Name}";
            if (e is ElementType et) return $"{et.FamilyName} : {et.Name}";
            if (doc.GetElement(e.GetTypeId()) is ElementType t) return $"{t.FamilyName} : {t.Name}";
            return "";
        }

        private static string LevelName(Document doc, Element e)
        {
            if (doc.GetElement(e.LevelId) is Level l) return l.Name;
            foreach (var bip in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.RBS_START_LEVEL_PARAM,
                                        BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM })
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId && doc.GetElement(p.AsElementId()) is Level lv)
                    return lv.Name;
            }
            return "";
        }

        private static string MaterialName(Document doc, Element e)
        {
            var sm = e.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
            if (sm != null && doc.GetElement(sm.AsElementId()) is Material m1) return m1.Name;
            try
            {
                var ids = e.GetMaterialIds(false);
                if (ids.Count == 1 && doc.GetElement(ids.First()) is Material m2) return m2.Name;
            }
            catch { }
            return "";   // none or several materials -> leave for Standards_Fill
        }

        private static bool Glob(string pattern, string text) =>
            Regex.IsMatch(text, "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                          RegexOptions.IgnoreCase);

        private static StandardsFinding F(StandardsRule r, Element e, string status, string oldV, string newV, string src) =>
            new StandardsFinding
            {
                CheckSet = r.CheckSet, CheckName = r.CheckName, Status = status,
                ElementId = e.Id.Value.ToString(CultureInfo.InvariantCulture),
                Category = e.Category?.Name ?? (e is ElementType ? "Type" : ""),
                ElementName = e.Name ?? "", Parameter = r.Kind == StandardsRuleKind.Workset ? "Workset" : r.Parameter,
                OldValue = oldV, NewValue = newV, Source = src
            };

        private static void WriteReport(StandardsContext ctx, List<StandardsFinding> findings, ProcessLog log)
        {
            if (string.IsNullOrWhiteSpace(ctx.ReportFolder)) return;
            try
            {
                Directory.CreateDirectory(ctx.ReportFolder);
                string name = (ctx.RvtFileName.Length > 0 ? ctx.RvtFileName : ctx.LocationCode) + "_ModelStandards.csv";
                string path = Path.Combine(ctx.ReportFolder, name);

                var sb = new StringBuilder();
                sb.AppendLine("Mode,CheckSet,Check,Status,ElementId,Category,Element,Parameter,OldValue,NewValue,Source");
                string mode = ctx.ApplyFixes ? "APPLIED" : "DRY-RUN";
                foreach (var f in findings)
                    sb.AppendLine(string.Join(",", new[] { mode, f.CheckSet, f.CheckName, f.Status, f.ElementId, f.Category,
                        f.ElementName, f.Parameter, f.OldValue, f.NewValue, f.Source }.Select(Csv)));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                log.Info(ctx.LocationCode, $"Model standards report: {path}");
            }
            catch (Exception ex)
            {
                log.Warning(ctx.LocationCode, $"Could not write model standards report: {ex.Message}");
            }
        }

        private static string Csv(string s) =>
            s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
