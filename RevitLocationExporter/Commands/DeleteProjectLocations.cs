using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitLocationExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class DeleteProjectLocations : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // Get the set of project locations
                ProjectLocationSet projectLocations = doc.ProjectLocations;

                if (projectLocations.Size <= 1)
                {
                    message = "Cannot delete all project locations. At least one must remain.";
                    return Result.Cancelled;
                }

                // Get the current active project location
                ProjectLocation activeLocation = doc.ActiveProjectLocation;
                if (activeLocation == null)
                {
                    message = "No active project location found.";
                    return Result.Failed;
                }

                // Collect locations to delete (avoid modifying collection during iteration)
                var locationsToDelete = new List<ElementId>();
                foreach (ProjectLocation location in projectLocations)
                {
                    if (location.Id != activeLocation.Id)
                    {
                        locationsToDelete.Add(location.Id);
                    }
                }

                // Delete in a transaction
                using (Transaction trans = new Transaction(doc, "Delete Project Locations"))
                {
                    trans.Start();
                    doc.Delete(locationsToDelete);
                    trans.Commit();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}