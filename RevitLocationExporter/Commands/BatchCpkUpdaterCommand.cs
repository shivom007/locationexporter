using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using RevitLocationExporter.Form;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace RevitLocationExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchCpkUpdaterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = commandData.Application.Application;

            var rvtFiles = SelectRvtFiles();
            if (rvtFiles == null || rvtFiles.Count == 0)
                return Result.Cancelled;

            var csvFile = SelectCsvFile();
            if (string.IsNullOrEmpty(csvFile))
                return Result.Cancelled;

            Dictionary<string, Dictionary<string, string>> csvData;
            try
            {
                csvData = ReadCsv(csvFile!);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Batch CPK Updater", $"Failed to read CSV:\n{ex.Message}");
                return Result.Failed;
            }

            if (csvData.Count == 0)
            {
                TaskDialog.Show("Batch CPK Updater", "CSV file contains no data rows.");
                return Result.Cancelled;
            }

            var progress = new ProgressWindow();
            progress.Show();

            int total = rvtFiles.Count;
            int current = 0;
            var errors = new List<string>();

            foreach (string filePath in rvtFiles)
            {
                current++;
                string fileName = Path.GetFileName(filePath);

                progress.UpdateProgress(fileName, current, total,
                    $"Processing file {current} of {total}: {fileName}");

                System.Windows.Application.Current?.Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Render,
                    new Action(() => { }));

                try
                {
                    string locatorCode = GetLocatorCode(filePath);

                    if (!csvData.TryGetValue(locatorCode, out var paramValues))
                    {
                        errors.Add($"{fileName}: LocatorCode '{locatorCode}' not found in CSV — skipped.");
                        continue;
                    }

                    ProcessFile(app, filePath, paramValues, errors);
                }
                catch (Exception ex)
                {
                    errors.Add($"{fileName}: {ex.Message}");
                }
            }

            progress.Close();

            string summary = $"Done. {total} file(s) attempted.";
            if (errors.Count > 0)
                summary += $"\n\n{errors.Count} issue(s):\n" + string.Join("\n", errors);

            TaskDialog.Show("Batch CPK Updater", summary);
            return Result.Succeeded;
        }

        // ─────────────────────────────────────────────────────────────────────
        // File / CSV selection
        // ─────────────────────────────────────────────────────────────────────

        private static List<string>? SelectRvtFiles()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Revit Files",
                Filter = "Revit Files (*.rvt)|*.rvt",
                Multiselect = true
            };
            return dlg.ShowDialog() == true ? dlg.FileNames.ToList() : null;
        }

        private static string? SelectCsvFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select CSV Parameter File",
                Filter = "CSV Files (*.csv)|*.csv"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // LocatorCode — parts[3] of dash-separated file name
        // ─────────────────────────────────────────────────────────────────────

        private static string GetLocatorCode(string filePath)
        {
            var parts = Path.GetFileNameWithoutExtension(filePath).Split('-');
            if (parts.Length < 4)
                throw new InvalidOperationException(
                    $"File name does not match expected format: {Path.GetFileName(filePath)}");
            return parts[3];
        }

        // ─────────────────────────────────────────────────────────────────────
        // CSV parsing
        // ─────────────────────────────────────────────────────────────────────

        internal static Dictionary<string, Dictionary<string, string>> ReadCsv(string csvPath)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2) return result;

            var headers = SplitCsvLine(lines[0]);
            int columnCount = headers.Length;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var values = SplitCsvLine(line);
                if (values.Length == 0 || string.IsNullOrWhiteSpace(values[0])) continue;

                string locator = values[0].Trim();
                var paramMap = new Dictionary<string, string>(StringComparer.Ordinal);

                for (int col = 1; col < columnCount && col < values.Length; col++)
                {
                    string name = headers[col].Trim();
                    if (!string.IsNullOrEmpty(name))
                        paramMap[name] = values[col].Trim();
                }

                result[locator] = paramMap;
            }

            return result;
        }

        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var sb = new System.Text.StringBuilder();

            foreach (char c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }

            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Per-file processing
        // ─────────────────────────────────────────────────────────────────────

        private static void ProcessFile(
            Autodesk.Revit.ApplicationServices.Application app,
            string filePath,
            Dictionary<string, string> paramValues,
            List<string> errors)
        {
            var openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets,
                Audit = false
            };
            openOptions.SetOpenWorksetsConfiguration(
                new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets));

            Document? doc = null;
            try
            {
                var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
                doc = app.OpenDocumentFile(modelPath, openOptions);

                if (doc == null)
                {
                    errors.Add($"{Path.GetFileName(filePath)}: Could not open.");
                    return;
                }
                if (doc.IsReadOnly)
                {
                    errors.Add($"{Path.GetFileName(filePath)}: Read-only — skipped.");
                    return;
                }

                FillCpkParametersForInstances(doc, paramValues);

                string outputPath = BuildOutputPath(filePath);
                var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };

                if (doc.IsWorkshared)
                    saveOpts.SetWorksharingOptions(
                        new WorksharingSaveAsOptions { SaveAsCentral = true });

                doc.SaveAs(outputPath, saveOpts);
            }
            finally
            {
                doc?.Close(false);
            }
        }

        private static string BuildOutputPath(string original)
        {
            string dir = Path.GetDirectoryName(original) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(original);
            return Path.Combine(dir, name + ".rvt");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: set parameters directly on every INSTANCE
        // No type lookup — each instance is updated individually
        // ─────────────────────────────────────────────────────────────────────

        private static void FillCpkParametersForInstances(
            Document doc, Dictionary<string, string> paramValues)
        {
            var instances = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();

            using var tx = new Transaction(doc, "Fill CPK Parameters (Instances)");
            tx.Start();

            foreach (Element instance in instances)
            {
                // Skip elements with no category or view-specific elements
                if (instance.Category == null) continue;
                if (instance.ViewSpecific) continue;

                SetParameters(instance, paramValues);
            }

            tx.Commit();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Parameter helpers
        // ─────────────────────────────────────────────────────────────────────

        private static void SetParameters(Element instance, Dictionary<string, string> paramValues)
        {
            foreach (var kvp in paramValues)
            {
                var param = instance.LookupParameter(kvp.Key);
                if (param == null || param.IsReadOnly) continue;
                try { SetValue(param, kvp.Value); } catch { }
            }
        }

        private static void SetValue(Parameter param, string raw)
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(raw);
                    break;

                case StorageType.Double:
                    if (double.TryParse(raw,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double d))
                        param.Set(d);
                    break;

                case StorageType.Integer:
                    if (int.TryParse(raw, out int i))
                        param.Set(i);
                    break;

                case StorageType.ElementId:
                    // Not settable from a CSV string value — intentionally skipped
                    break;
            }
        }
    }
}