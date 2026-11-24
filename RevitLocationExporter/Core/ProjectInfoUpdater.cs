using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;

namespace RevitLocationExporter.Core
{
    public class ProjectInfoUpdater
    {
        // ─────────────────────────────────────────────────────────────
        // Main entry point: handles both the static dictionary AND
        // any runtime-resolved (dynamic) parameters you build separately.
        // ─────────────────────────────────────────────────────────────
        public bool UpdateProjectInformation(
            Document doc,
            Dictionary<string, string> projectData,
            IEnumerable<(string paramName, object paramValue)>? dynamicParams = null)
        {
            ProjectInfo projInfo = doc.ProjectInformation;
            if (projInfo == null) return false;

            using (Transaction trans = new Transaction(doc, "Update Project Information"))
            {
                try
                {
                    trans.Start();

                    // 1. Apply dictionary-sourced parameters (string values)
                    if (projectData != null)
                    {
                        foreach (var kvp in projectData)
                            ApplyParameter(projInfo, kvp.Key, kvp.Value);
                    }

                    // 2. Apply dynamic parameters (typed values — your runtime data)
                    if (dynamicParams != null)
                    {
                        foreach (var (name, value) in dynamicParams)
                            ApplyParameter(projInfo, name, value);
                    }

                    trans.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    if (trans.GetStatus() == TransactionStatus.Started)
                        trans.RollBack();

                    System.Diagnostics.Debug.WriteLine(
                        $"[ProjectInfoUpdater] Transaction failed: {ex.Message}");
                    return false;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Overload for BuiltInParameter — locale-safe for known fields
        // e.g. BuiltInParameter.PROJECT_NUMBER, CLIENT_NAME, etc.
        // ─────────────────────────────────────────────────────────────
        public bool UpdateProjectInformation(
            Document doc,
            Dictionary<BuiltInParameter, string> builtInData)
        {
            ProjectInfo projInfo = doc.ProjectInformation;
            if (projInfo == null) return false;

            using (Transaction trans = new Transaction(doc, "Update Project Information"))
            {
                try
                {
                    trans.Start();

                    foreach (var kvp in builtInData)
                    {
                        Parameter param = projInfo.get_Parameter(kvp.Key);
                        if (param != null && !param.IsReadOnly)
                            SetTypedValue(param, kvp.Value);
                    }

                    trans.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    if (trans.GetStatus() == TransactionStatus.Started)
                        trans.RollBack();

                    System.Diagnostics.Debug.WriteLine(
                        $"[ProjectInfoUpdater] BuiltIn update failed: {ex.Message}");
                    return false;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Core: resolve parameter by name then dispatch to typed setter
        // ─────────────────────────────────────────────────────────────
        private void ApplyParameter(ProjectInfo projInfo, string paramName, object value)
        {
            Parameter param = projInfo.LookupParameter(paramName);

            if (param == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ProjectInfoUpdater] Parameter not found: '{paramName}'");
                return;
            }
            if (param.IsReadOnly)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ProjectInfoUpdater] Parameter is read-only: '{paramName}'");
                return;
            }

            SetTypedValue(param, value);
        }

        // ─────────────────────────────────────────────────────────────
        // Typed setter — respects StorageType to avoid runtime exceptions
        // ─────────────────────────────────────────────────────────────
        private void SetTypedValue(Parameter param, object value)
        {
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        param.Set(value?.ToString() ?? string.Empty);
                        break;

                    case StorageType.Double:
                        // Value should already be in Revit internal units.
                        // If you're passing display units, convert first with
                        // UnitUtils.ConvertToInternalUnits(val, forgeTypeId).
                        if (value is double d)
                            param.Set(d);
                        else if (double.TryParse(value?.ToString(), out double parsed))
                            param.Set(parsed);
                        else
                            System.Diagnostics.Debug.WriteLine(
                                $"[ProjectInfoUpdater] Cannot convert '{value}' to Double for '{param.Definition.Name}'");
                        break;

                    case StorageType.Integer:
                        if (value is int i)
                            param.Set(i);
                        else if (int.TryParse(value?.ToString(), out int parsedInt))
                            param.Set(parsedInt);
                        else
                            System.Diagnostics.Debug.WriteLine(
                                $"[ProjectInfoUpdater] Cannot convert '{value}' to Integer for '{param.Definition.Name}'");
                        break;

                    case StorageType.ElementId:
                        if (value is ElementId eid)
                            param.Set(eid);
                        else if (value is int rawId)
                            param.Set(new ElementId((long)rawId));
                        else
                            System.Diagnostics.Debug.WriteLine(
                                $"[ProjectInfoUpdater] Cannot resolve ElementId from '{value}' for '{param.Definition.Name}'");
                        break;

                    default:
                        System.Diagnostics.Debug.WriteLine(
                            $"[ProjectInfoUpdater] Unhandled StorageType '{param.StorageType}' for '{param.Definition.Name}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ProjectInfoUpdater] Failed setting '{param.Definition.Name}': {ex.Message}");
            }
        }
    }
}