using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitLocationExporter.Form;

namespace RevitLocationExporter.Commands
{
    /// <summary>
    /// Revit ribbon command that launches the interactive Trade Profile Manager & Builder.
    /// Allows users to create, customize, import, export, and manage discipline profiles.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TradeProfileManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dialog = new TradeProfileManagerDialog();
                dialog.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Trade Profile Manager Error", ex.Message);
                return Result.Failed;
            }
        }
    }
}
