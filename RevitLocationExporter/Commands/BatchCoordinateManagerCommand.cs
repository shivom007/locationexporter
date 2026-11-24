using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace RevitLocationExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class BatchCoordinateManagerCommand : IExternalCommand
    {
        private Logger _logger = null!;
        private string _linkDirectory = string.Empty;
        private int _successCount = 0;
        private int _failCount = 0;
        private readonly StringBuilder _summary = new StringBuilder();

        // ===================================================================
        // MAIN EXECUTE
        // ===================================================================
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Autodesk.Revit.ApplicationServices.Application app = uiApp.Application;

            // 1. Select host models
            string[]? selectedFiles = GetFilesToProcess();
            if (selectedFiles == null || selectedFiles.Length == 0)
                return Result.Cancelled;

            // 2. Select directory containing A00 links
            _linkDirectory = GetDirectory("Select Directory containing Target Architectural Links (A00)") ?? string.Empty;
            if (string.IsNullOrEmpty(_linkDirectory))
                return Result.Cancelled;

            // 3. Initialize logger (Desktop)
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"BatchCoordReset_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            _logger = new Logger(logPath);

            // 4. Subscribe application-level suppression for silent batch execution
            uiApp.DialogBoxShowing += OnDialogBoxShowing;
            app.FailuresProcessing += OnFailuresProcessing;

            try
            {
                _logger.Log("=== BATCH PROCESSING STARTED ===");

                foreach (string filePath in selectedFiles)
                {
                    ProcessFile(app, filePath);
                }

                _logger.Log("=== BATCH PROCESSING COMPLETED ===");
                _logger.Log($"Successful: {_successCount}, Failed: {_failCount}");
            }
            finally
            {
                // Always unsubscribe to avoid leaking handlers across commands
                uiApp.DialogBoxShowing -= OnDialogBoxShowing;
                app.FailuresProcessing -= OnFailuresProcessing;
                _logger?.Dispose();
            }

            // 5. Final summary dialog
            TaskDialog.Show("Batch Coordinate Reset — Complete",
                $"Processing finished.\n\n" +
                $"Successful: {_successCount}\n" +
                $"Failed:     {_failCount}\n\n" +
                $"Log saved to:\n{logPath}\n\n" +
                $"Details:\n{_summary}");

            return Result.Succeeded;
        }

        // ===================================================================
        // SINGLE FILE WORKFLOW
        // ===================================================================
        private void ProcessFile(Autodesk.Revit.ApplicationServices.Application app, string filePath)
        {
            Document? doc = null;
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            _logger.Log($"\n--- PROCESSING: {fileName} ---");

            try
            {
                // ---------------------------------------------------------
                // Parse naming early to fail fast
                // ---------------------------------------------------------
                string siteName = ExtractSiteName(fileName);
                string? targetLinkName = ReconstructTargetLinkName(fileName);

                if (targetLinkName == null)
                {
                    _logger.Log("ERROR: Host file name does not match expected naming convention (7 dash-separated segments with underscore in final segment).");
                    RecordResult(fileName, "Invalid naming format");
                    _failCount++;
                    return;
                }

                string targetLinkPath = Path.Combine(_linkDirectory, targetLinkName + ".rvt");
                if (!File.Exists(targetLinkPath))
                {
                    _logger.Log($"ERROR: Target architectural link not found: {targetLinkPath}");
                    RecordResult(fileName, "Missing target A00 file");
                    _failCount++;
                    return;
                }

                // ---------------------------------------------------------
                // 1. OPEN DETACHED FROM CENTRAL
                // ---------------------------------------------------------
                doc = OpenModelSafely(app, filePath);
                if (doc == null)
                {
                    RecordResult(fileName, "Open failed");
                    _failCount++;
                    return;
                }

                if (!doc.IsWorkshared)
                {
                    _logger.Log("ERROR: Document is not workshared. Cannot save as Central.");
                    RecordResult(fileName, "Not workshared");
                    _failCount++;
                    return;
                }

                // ---------------------------------------------------------
                // 2. RESET SHARED COORDINATES
                // ---------------------------------------------------------
                ResetSharedCoordinates(doc);

                // ---------------------------------------------------------
                // 3. IDENTIFY A00 ARCHITECTURAL LINK
                // ---------------------------------------------------------
                RevitLinkType? a00LinkType = FindA00LinkType(doc);
                if (a00LinkType == null)
                {
                    _logger.Log("ERROR: No existing architectural link containing 'A00' found.");
                    RecordResult(fileName, "Missing A00 link");
                    _failCount++;
                    return;
                }
                _logger.Log($"Found A00 link type: {a00LinkType.Name}");

                // ---------------------------------------------------------
                // 4. RELOAD ARCHITECTURAL LINK
                // ---------------------------------------------------------
                ReloadArchitecturalLink(doc, a00LinkType, targetLinkPath);

                // Validate instance and transform after reload
                RevitLinkInstance linkInstance = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast < RevitLinkInstance > ()
                    .FirstOrDefault(x => x.GetTypeId() == a00LinkType.Id);

                if (linkInstance == null)
                {
                    _logger.Log("ERROR: Link instance not found after reload.");
                    RecordResult(fileName, "Missing link instance");
                    _failCount++;
                    return;
                }

                Transform totalTransform = linkInstance.GetTotalTransform();
                if (totalTransform == null || !totalTransform.IsConformal)
                {
                    _logger.Log("ERROR: Link transform is invalid after reload.");
                    RecordResult(fileName, "Invalid link transform");
                    _failCount++;
                    return;
                }

                // ---------------------------------------------------------
                // 5. VALIDATE & ATTEMPT TO ACTIVATE SHARED SITE
                // ---------------------------------------------------------
                ValidateAndActivateSharedSite(linkInstance, siteName);

                // ---------------------------------------------------------
                // 6. ACQUIRE SHARED COORDINATES
                // ---------------------------------------------------------
                AcquireCoordinatesSafe(doc, linkInstance);

                // ---------------------------------------------------------
                // 7. SAVE AS CENTRAL (overwrite original)
                // ---------------------------------------------------------
                SaveAsCentral(doc, filePath);

                _logger.Log("SUCCESS: File processed and saved as Central.");
                RecordResult(fileName, "Success");
                _successCount++;
            }
            catch (Exception ex)
            {
                _logger.Log($"CRITICAL ERROR: {ex.Message}");
                if (ex.StackTrace != null)
                    _logger.Log(ex.StackTrace);
                RecordResult(fileName, $"Exception: {ex.Message}");
                _failCount++;
            }
            finally
            {
                // ---------------------------------------------------------
                // 8. CLOSE & DISPOSE
                // ---------------------------------------------------------
                if (doc != null && doc.IsValidObject)
                {
                    try
                    {
                        // false = do not save again (already saved via SaveAs)
                        doc.Close(false);
                        _logger.Log("Document closed.");
                    }
                    catch (Exception closeEx)
                    {
                        _logger.Log($"Warning during document close: {closeEx.Message}");
                    }
                }
            }
        }

        // ===================================================================
        // WORKFLOW METHODS
        // ===================================================================

        private Document? OpenModelSafely(Autodesk.Revit.ApplicationServices.Application app, string filePath)
        {
            try
            {
                ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
                OpenOptions opt = new OpenOptions
                {
                    DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets,
                    AllowOpeningLocalByWrongUser = true,
                    Audit = false
                };

                // Close all worksets by default; open only those containing "A00"
                WorksetConfiguration wc = new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);
                

                opt.SetOpenWorksetsConfiguration(wc);
                _logger.Log("Opening model detached (A00 worksets only if workshared)...");

                return app.OpenDocumentFile(mp, opt);
            }
            catch (Exception ex)
            {
                _logger.Log($"ERROR opening model: {ex.Message}");
                return null;
            }
        }

        private void ResetSharedCoordinates(Document doc)
        {
            using (Transaction t = new Transaction(doc, "Reset Shared Coordinates"))
            {
                ConfigureFailureHandling(t);
                t.Start();
                try
                {
                    // REQUIRES REVIT 2021.1+
                    doc.ResetSharedCoordinates();
                    t.Commit();
                    _logger.Log("SUCCESS: Shared coordinates reset to identity (origin, no rotation, relationships cleared).");
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    _logger.Log($"ERROR: ResetSharedCoordinates failed: {ex.Message}");
                    throw;
                }
            }
        }

        private string ExtractSiteName(string fileName)
        {
            string[] parts = fileName.Split('-');
            return parts.Length > 3 ? parts[3] : "UNKNOWN_SITE";
        }

        private string? ReconstructTargetLinkName(string fileName)
        {
            try
            {
                // Expected: 7 dash-separated segments, final segment contains underscore
                // Example: P00100087-DAR-TI0-AFCXS1001-XX-M3-2X0001_EN
                string[] parts = fileName.Split('-');
                if (parts.Length != 7)
                    return null;

                string[] suffixParts = parts[6].Split('_');
                if (suffixParts.Length != 2)
                    return null;

                // Apply strict naming transformations
                parts[2] = "A00";          // Sequence 3
                suffixParts[0] = "XX0001"; // Sequence 7 (before underscore)
                suffixParts[1] = "PE";     // Sequence 8 (after underscore)
                parts[6] = string.Join("_", suffixParts);

                return string.Join("-", parts);
            }
            catch
            {
                return null;
            }
        }

        private RevitLinkType? FindA00LinkType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkType))
                .Cast < RevitLinkType > ()
                .FirstOrDefault(lt => !lt.IsNestedLink
                    && lt.Name.IndexOf("A00", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ReloadArchitecturalLink(Document doc, RevitLinkType linkType, string targetPath)
        {
            // Defensive: warn if link sits on a closed workset
            if (doc.IsWorkshared)
            {
                WorksetTable wsTable = doc.GetWorksetTable();
                Workset linkWorkset = wsTable.GetWorkset(linkType.WorksetId);
                if (linkWorkset != null && !linkWorkset.IsOpen)
                {
                    _logger.Log($"WARNING: Link type is on closed workset '{linkWorkset.Name}'. Reload may fail.");
                }
            }

            ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(targetPath);

            using (Transaction t = new Transaction(doc, "Reload A00 Link"))
            {
                ConfigureFailureHandling(t);
                t.Start();
                try
                {
                    // null WorksetConfiguration lets the link load with its default workset rules
                    var loadResult = linkType.LoadFrom(mp, null);

                    if (loadResult.LoadResult != LinkLoadResultType.LinkLoaded)
                    {
                        throw new InvalidOperationException(
                            $"LinkLoadResult was '{loadResult.LoadResult}'. Target: {targetPath}");
                    }

                    t.Commit();
                    _logger.Log($"SUCCESS: Link reloaded from {targetPath}");
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    _logger.Log($"ERROR: Link reload failed: {ex.Message}");
                    throw;
                }
            }
        }

        private void ValidateAndActivateSharedSite(RevitLinkInstance linkInstance, string targetSite)
        {
            Document linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null || linkDoc.IsReadOnly)
            {
                _logger.Log("Skipping site activation: Link is null or Read-Only.");
                return;
            }

            ProjectLocation targetLocation = linkDoc.ProjectLocations
                .Cast<ProjectLocation>()
                .FirstOrDefault(loc => loc.Name.Equals(targetSite, StringComparison.Ordinal));

            if (targetLocation != null)
            {
                // Check if it's already active to avoid unnecessary transactions
                if (linkDoc.ActiveProjectLocation.Id == targetLocation.Id) return;

                using (Transaction t = new Transaction(linkDoc, "Activate Site"))
                {
                    t.Start();
                    linkDoc.ActiveProjectLocation = targetLocation;
                    t.Commit();
                }
            }
        }

        private void AcquireCoordinatesSafe(Document doc, RevitLinkInstance linkInstance)
        {
            using (Transaction t = new Transaction(doc, "Acquire Coordinates"))
            {
                ConfigureFailureHandling(t);
                t.Start();
                try
                {
                    doc.AcquireCoordinates(linkInstance.Id);
                    t.Commit();
                    _logger.Log("SUCCESS: Coordinates acquired from linked model.");
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    _logger.Log($"ERROR: AcquireCoordinates failed: {ex.Message}");
                    throw;
                }
            }
        }

        private void SaveAsCentral(Document doc, string originalFilePath)
        {
            try
            {
                ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(originalFilePath);
                SaveAsOptions opt = new SaveAsOptions
                {
                    OverwriteExistingFile = true,
                    Compact = true
                };

                if (doc.IsWorkshared)
                {
                    WorksharingSaveAsOptions wsOpt = new WorksharingSaveAsOptions
                    {
                        SaveAsCentral = true,
                        ClearTransmitted = true
                    };
                    opt.SetWorksharingOptions(wsOpt);
                }

                _logger.Log($"Saving as Central: {originalFilePath}");
                doc.SaveAs(mp, opt);
                _logger.Log("SUCCESS: Saved as Central model.");
            }
            catch (Exception ex)
            {
                _logger.Log($"ERROR: SaveAsCentral failed: {ex.Message}");
                throw;
            }
        }

        private void ConfigureFailureHandling(Transaction t)
        {
            FailureHandlingOptions fho = t.GetFailureHandlingOptions();
            fho.SetFailuresPreprocessor(new WarningSwallower());
            fho.SetClearAfterRollback(true);
            fho.SetForcedModalHandling(false);
            t.SetFailureHandlingOptions(fho);
        }

        private void RecordResult(string fileName, string result)
        {
            _summary.AppendLine($"{fileName}: {result}");
        }

        // ===================================================================
        // UI HELPERS
        // ===================================================================
        private string[]? GetFilesToProcess()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Revit Files (*.rvt)|*.rvt";
                ofd.Multiselect = true;
                ofd.Title = "Select Host Revit Models";
                IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (ofd.ShowDialog(new WindowHandle(hwnd)) == DialogResult.OK)
                    return ofd.FileNames;
            }
            return null;
        }

        private string? GetDirectory(string title)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = title;
                fbd.ShowNewFolderButton = false;
                IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (fbd.ShowDialog(new WindowHandle(hwnd)) == DialogResult.OK)
                    return fbd.SelectedPath;
            }
            return null;
        }

        // ===================================================================
        // APPLICATION-LEVEL EVENT HANDLERS (Silent Execution)
        // ===================================================================
        private void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            if (e is TaskDialogShowingEventArgs td)
                td.OverrideResult((int)TaskDialogResult.Ok);
            else
                e.OverrideResult(1); // Dismiss generic dialogs with OK
        }

        private void OnFailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            FailuresAccessor accessor = e.GetFailuresAccessor();
            foreach (FailureMessageAccessor fma in accessor.GetFailureMessages())
            {
                if (fma.GetSeverity() == FailureSeverity.Warning)
                    accessor.DeleteWarning(fma);
            }
            e.SetProcessingResult(FailureProcessingResult.Continue);
        }
    }

    // =======================================================================
    // SUPPORT CLASSES
    // =======================================================================

    public class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor fma in failuresAccessor.GetFailureMessages())
            {
                if (fma.GetSeverity() == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(fma);
            }
            // Let errors propagate normally (transaction will roll back)
            return FailureProcessingResult.Continue;
        }
    }

    public class Logger : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();

        public Logger(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _writer = new StreamWriter(filePath, false, Encoding.UTF8);
        }

        public void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            lock (_lock)
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
        }
    }

    public class WindowHandle : IWin32Window
    {
        public IntPtr Handle { get; }
        public WindowHandle(IntPtr handle) { Handle = handle; }
    }
}