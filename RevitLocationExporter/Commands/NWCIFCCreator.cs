using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace RevitLocationExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class NWCIFCCreator : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = commandData.Application.Application;

            List<string>? rvtFiles = SelectRvtFiles();
            if (rvtFiles == null || rvtFiles.Count == 0)
                return Result.Cancelled;

            int successCount = 0;
            int skippedCount = 0;
            int errorCount = 0;
            var errorMessages = new List<string>();

            foreach (string filePath in rvtFiles)
            {
                Document? doc = null;
                try
                {
                    OpenOptions openOptions = new OpenOptions
                    {
                        DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets,
                        Audit = false
                    };
                    openOptions.SetOpenWorksetsConfiguration(
                        new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets));

                    ModelPath rvtPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
                    doc = app.OpenDocumentFile(rvtPath, openOptions);

                    CreateNavisworksAndIFCViews(doc, out bool viewsCreated, out string statusMessage);

                    if (viewsCreated)
                    {
                        SaveAsCentral(doc, filePath);
                        successCount++;
                    }
                    else
                    {
                        skippedCount++;
                        if (!string.IsNullOrEmpty(statusMessage))
                            errorMessages.Add($"{Path.GetFileName(filePath)}: {statusMessage}");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    errorMessages.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
                }
                finally
                {
                    // Always close the document — never sync, since it is a detached copy
                    try { doc?.Close(false); } catch { }
                }
            }

            string summary = $"Processing complete.\n" +
                             $"Successfully processed: {successCount}\n" +
                             $"Skipped (no source view): {skippedCount}\n" +
                             $"Errors: {errorCount}";

            if (errorMessages.Any())
            {
                summary += "\n\nDetails:\n" + string.Join("\n", errorMessages.Take(10));
                if (errorMessages.Count > 10)
                    summary += $"\n...and {errorMessages.Count - 10} more.";
            }

            TaskDialog.Show("NWC/IFC View Creator", summary);
            return Result.Succeeded;
        }

        /// <summary>
        /// Creates EXPORT-NWC and EXPORT-IFC views by duplicating a view whose
        /// name contains "Naviswork". Existing export views are deleted first.
        /// </summary>
        private static void CreateNavisworksAndIFCViews(
            Document doc,
            out bool success,
            out string message)
        {
            success = false;
            message = string.Empty;

            // Find source view whose name contains "Naviswork"
            View3D sourceView = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v =>
                    !v.IsTemplate &&
                    v.Name.IndexOf("Naviswork", StringComparison.OrdinalIgnoreCase) >= 0);

            if (sourceView == null)
            {
                message = "No 3D view with a name containing 'Naviswork' was found.";
                return;
            }

            // Step 1 — Delete existing export views
            using (Transaction txDelete = new Transaction(doc, "Delete old export views"))
            {
                txDelete.Start();
                DeleteViewIfExists(doc, "EXPORT-NWC");
                DeleteViewIfExists(doc, "EXPORT-IFC");
                txDelete.Commit();
            }

            // Step 2 — Duplicate source view to create both export views
            using (Transaction txCreate = new Transaction(doc, "Create export views"))
            {
                txCreate.Start();

                ElementId nwcId = sourceView.Duplicate(ViewDuplicateOption.Duplicate);
                View3D? nwcView = doc.GetElement(nwcId) as View3D;
                if (nwcView != null) nwcView.Name = "EXPORT-NWC";

                ElementId ifcId = sourceView.Duplicate(ViewDuplicateOption.Duplicate);
                View3D? ifcView = doc.GetElement(ifcId) as View3D;
                if (ifcView != null) ifcView.Name = "EXPORT-IFC";

                txCreate.Commit();
            }

            success = true;
            message = "Views created successfully.";
        }

        /// <summary>
        /// Deletes a non-template 3D view by exact name if it exists.
        /// Silently skips deletion if the view is placed on a sheet or otherwise locked.
        /// </summary>
        private static void DeleteViewIfExists(Document doc, string viewName)
        {
            View3D existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v =>
                    !v.IsTemplate &&
                    v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
                return;

            try
            {
                doc.Delete(existing.Id);
            }
            catch (Exception)
            {
                // View is likely placed on a sheet — leave it in place and continue
            }
        }

        /// <summary>
        /// Enables worksharing if needed, then saves the document as a new central
        /// file at the original path, overwriting it.
        /// NOTE: The document must already be detached from its previous central
        /// (opened with DetachAndPreserveWorksets) before calling this method.
        /// EnableWorksharing manages its own internal transaction and must NOT be
        /// called inside an explicit Transaction.
        /// </summary>
        private static void SaveAsCentral(Document doc, string originalPath)
        {
            if (!doc.IsWorkshared)
            {
                // EnableWorksharing is a document-level operation that commits its
                // own transaction internally — do not wrap it in a Transaction block.
                doc.EnableWorksharing("Shared Levels and Grids", "Workset1");
            }

            var saveOpts = new SaveAsOptions
            {
                OverwriteExistingFile = true,
                Compact = true
            };
            saveOpts.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });

            doc.SaveAs(originalPath, saveOpts);
        }

        /// <summary>
        /// Opens a WPF file dialog to allow multi-selection of .rvt files.
        /// </summary>
        private static List<string>? SelectRvtFiles()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Revit Files to Process",
                Filter = "Revit Files (*.rvt)|*.rvt",
                Multiselect = true
            };

            return dlg.ShowDialog() == true ? dlg.FileNames.ToList() : null;
        }
    }
}