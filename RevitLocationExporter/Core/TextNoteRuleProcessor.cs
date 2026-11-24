using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using RevitLocationExporter.Configuration;
using View = Autodesk.Revit.DB.View;

namespace RevitLocationExporter.Core
{
    /// <summary>
    /// Evaluates and applies opt-in per-trade TextNote replacement rules on matching sheets,
    /// placed views, and legends without touching unrelated notes or other disciplines.
    /// </summary>
    public static class TextNoteRuleProcessor
    {
        public static bool MatchesSheetFilter(string sheetNumber, string sheetName, string? sheetFilter)
        {
            if (string.IsNullOrWhiteSpace(sheetFilter) || sheetFilter!.Trim() == "*")
                return true;

            string[] tokens = sheetFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in tokens)
            {
                string pat = raw.Trim();
                if (pat.Length == 0 || pat == "*") return true;

                if (MatchesPattern(sheetNumber ?? string.Empty, pat) ||
                    MatchesPattern(sheetName ?? string.Empty, pat))
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
            return value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Evaluates one TextNoteRule against a piece of text.
        /// Returns true only if the text matched and changed.
        /// </summary>
        public static bool ApplyRuleToText(
            TextNoteRule rule,
            string originalText,
            Func<string, string?> lookup,
            out string updatedText,
            out string? error)
        {
            updatedText = originalText ?? string.Empty;
            error = null;

            if (rule == null || !rule.Enabled || string.IsNullOrEmpty(originalText) || string.IsNullOrWhiteSpace(rule.FindPattern))
                return false;

            if (!string.IsNullOrWhiteSpace(rule.NoteContains) &&
                originalText!.IndexOf(rule.NoteContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            // Expand [Token] placeholders ONLY when lookup resolves to a non-empty value,
            // preserving regex character classes like [A-Z0-9].
            string expandedFind = Regex.Replace(
                rule.FindPattern,
                @"\[([^\[\]]+)\]",
                m =>
                {
                    string key = m.Groups[1].Value.Trim();
                    string? val = lookup?.Invoke(key);
                    if (string.IsNullOrEmpty(val)) return m.Value;
                    return rule.IsRegex ? Regex.Escape(val!) : val!;
                });

            string replacement = rule.ReplaceWith ?? string.Empty;
            if (replacement.TrimStart().StartsWith("=", StringComparison.Ordinal))
            {
                if (!ExcelFormulaEngine.TryEvaluate(replacement, lookup ?? (_ => null), out string evaluated, out string? ferr))
                {
                    error = ferr;
                    return false;
                }
                replacement = evaluated;
            }

            string regexPattern = rule.IsRegex ? expandedFind : Regex.Escape(expandedFind);

            try
            {
                string candidate = Regex.Replace(
                    originalText,
                    regexPattern,
                    _ => replacement,
                    RegexOptions.IgnoreCase);

                if (string.Equals(candidate, originalText, StringComparison.Ordinal))
                    return false;

                updatedText = candidate;
                return true;
            }
            catch (ArgumentException ex)
            {
                error = $"Invalid regex pattern '{regexPattern}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Applies enabled TextNoteRules across matching sheets (must be called inside an active Revit Transaction).
        /// </summary>
        public static int ApplyToSheets(
            Document doc,
            IList<ViewSheet> sheets,
            IList<TextNoteRule>? rules,
            string nativeLocator,
            string locationName,
            Dictionary<string, string>? sheetParams,
            Dictionary<string, string>? elementParams,
            Dictionary<string, string> builtInTokens,
            ProcessLog log)
        {
            if (rules == null || sheets == null || sheets.Count == 0)
                return 0;

            var enabledRules = rules.Where(r => r != null && r.Enabled && !string.IsNullOrWhiteSpace(r.FindPattern)).ToList();
            if (enabledRules.Count == 0)
                return 0;

            int totalUpdated = 0;
            var updatedNoteIds = new HashSet<ElementId>();
            var loggedGroupWarnings = new HashSet<ElementId>();

            foreach (ViewSheet sheet in sheets)
            {
                var sheetMatchingRules = enabledRules
                    .Where(r => MatchesSheetFilter(sheet.SheetNumber, sheet.Name, r.SheetFilter))
                    .ToList();

                if (sheetMatchingRules.Count == 0)
                    continue;

                string? LookupForSheet(string key)
                {
                    if (key.Equals("NativeLocator", StringComparison.OrdinalIgnoreCase)) return nativeLocator;
                    if (key.Equals("Sheet Number", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("SheetNumber", StringComparison.OrdinalIgnoreCase)) return sheet.SheetNumber;
                    if (key.Equals("Sheet Name", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("SheetName", StringComparison.OrdinalIgnoreCase)) return sheet.Name;

                    if (sheetParams != null && sheetParams.TryGetValue(key, out var sv)) return sv;
                    if (elementParams != null && elementParams.TryGetValue(key, out var ev)) return ev;
                    if (builtInTokens != null && builtInTokens.TryGetValue(key, out var bv)) return bv;
                    return null;
                }

                // Collect views needed by any matching rule on this sheet
                bool needPlaced = sheetMatchingRules.Any(r =>
                    r.Scope == TextNoteScope.SheetsAndPlacedViews ||
                    r.Scope == TextNoteScope.SheetsPlacedViewsAndLegends);
                bool needLegends = sheetMatchingRules.Any(r =>
                    r.Scope == TextNoteScope.SheetsPlacedViewsAndLegends);

                var candidateViews = new List<(ElementId ViewId, bool IsLegend, bool IsSheetSelf)>
                {
                    (sheet.Id, false, true)
                };

                if (needPlaced)
                {
                    try
                    {
                        foreach (ElementId placedId in sheet.GetAllPlacedViews())
                        {
                            if (doc.GetElement(placedId) is View pv)
                            {
                                bool isLegend = pv.ViewType == ViewType.Legend;
                                if (isLegend && !needLegends) continue;
                                candidateViews.Add((placedId, isLegend, false));
                            }
                        }
                    }
                    catch
                    {
                        // Ignore if placed views cannot be queried
                    }
                }

                foreach (var (viewId, isLegend, isSheetSelf) in candidateViews)
                {
                    var notes = new FilteredElementCollector(doc, viewId)
                        .OfClass(typeof(TextNote))
                        .Cast<TextNote>()
                        .ToList();

                    if (notes.Count == 0) continue;

                    string viewLabel = (doc.GetElement(viewId) as View)?.Name ?? sheet.SheetNumber;

                    foreach (TextNote tn in notes)
                    {
                        if (updatedNoteIds.Contains(tn.Id))
                            continue;

                        string originalText;
                        try { originalText = tn.Text; }
                        catch { continue; }

                        string workingText = originalText;
                        bool anyRuleMatched = false;

                        foreach (var rule in sheetMatchingRules)
                        {
                            if (!isSheetSelf && rule.Scope == TextNoteScope.SheetsOnly)
                                continue;
                            if (isLegend && rule.Scope != TextNoteScope.SheetsPlacedViewsAndLegends)
                                continue;

                            if (ApplyRuleToText(rule, workingText, LookupForSheet, out string nextText, out string? err))
                            {
                                workingText = nextText;
                                anyRuleMatched = true;
                            }
                            else if (!string.IsNullOrEmpty(err))
                            {
                                log.Warning(locationName, $"[{sheet.SheetNumber}] TextNote rule error: {err}");
                            }
                        }

                        if (!anyRuleMatched)
                            continue;

                        if (tn.GroupId != ElementId.InvalidElementId)
                        {
                            if (loggedGroupWarnings.Add(tn.Id))
                            {
                                log.Warning(locationName,
                                    $"[{sheet.SheetNumber}] Skipped TextNote in '{viewLabel}' because it belongs to a Revit Group.");
                            }
                            continue;
                        }

                        try
                        {
                            tn.Text = workingText;
                            updatedNoteIds.Add(tn.Id);
                            totalUpdated++;
                            log.Info(locationName,
                                $"[{sheet.SheetNumber}] Updated TextNote in '{viewLabel}': '{CompactPreview(originalText)}' → '{CompactPreview(workingText)}'");
                        }
                        catch (Exception ex)
                        {
                            log.Warning(locationName,
                                $"[{sheet.SheetNumber}] Could not update TextNote in '{viewLabel}': {ex.Message}");
                        }
                    }
                }
            }

            if (totalUpdated > 0)
            {
                log.Info(locationName, $"TextNote rules updated {totalUpdated} note(s).");
            }

            return totalUpdated;
        }

        private static string CompactPreview(string s)
        {
            string flat = (s ?? string.Empty).Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= 80 ? flat : "..." + flat.Substring(flat.Length - 77);
        }
    }
}
