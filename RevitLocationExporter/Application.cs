using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RevitLocationExporter
{
    /// <summary>
    /// Revit external application — registers the ribbon tab and all command buttons.
    /// Implements IExternalApplication directly (no Nice3point base class).
    /// </summary>
    public class Application : IExternalApplication
    {
        private static string _assemblyPath = string.Empty;
        private static bool _resolverRegistered = false;

        public Result OnStartup(UIControlledApplication application)
        {
            _assemblyPath = Assembly.GetExecutingAssembly().Location;
            RegisterAssemblyResolver();

            try
            {
                CreateRibbon(application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("RevitLocationExporter — Startup Error", ex.Message);
                return Result.Failed;
            }
        }

        private static void RegisterAssemblyResolver()
        {
            if (_resolverRegistered) return;
            _resolverRegistered = true;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    string folder = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                    if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder)) return null;

                    string simpleName = new AssemblyName(args.Name).Name;
                    if (string.IsNullOrEmpty(simpleName)) return null;

                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (string.Equals(asm.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                        {
                            return asm;
                        }
                    }

                    string candidate = System.IO.Path.Combine(folder, simpleName + ".dll");
                    if (System.IO.File.Exists(candidate))
                    {
                        return Assembly.LoadFrom(candidate);
                    }
                }
                catch
                {
                    // Ignore resolution errors here so standard fallback continues
                }
                return null;
            };
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Ribbon
        // ─────────────────────────────────────────────────────────────────────

        private void CreateRibbon(UIControlledApplication application)
        {
            const string tabName = "RevitLocationExporter";
            const string panelName = "Commands";

            application.CreateRibbonTab(tabName);
            var panel = application.CreateRibbonPanel(tabName, panelName);

            // 1. Primary command: Location Exporter
            panel.AddItem(Button<Commands.LocationExporterCommand>(
                "LocationExporterCommand", "Location\nExporter", "LocationExporter"));

            // 2. Central Model Location Splitter
            panel.AddItem(Button<Commands.LocationSplitterCentral>(
                "LocationSplitter", "Location\nSplitter", "LocationSplitter"));

            // 3. Batch CPK Updater
            panel.AddItem(Button<Commands.BatchCpkUpdaterCommand>(
                "BatchCPK", "Batch CPK\nUpdater", "BatchCPK"));

            // 4. Batch Coordinate Manager
            panel.AddItem(Button<Commands.BatchCoordinateManagerCommand>(
                "BatchCoordinateManager", "Batch Coordinate\nManager", "BatchCoordinateManager"));

            // 5. Delete Locations Tool
            panel.AddItem(Button<Commands.DeleteProjectLocations>(
                "DeleteLocations", "Delete Locations", "DeleteLocations"));

            // 6. Trade Profiles Manager & Builder
            panel.AddItem(Button<Commands.TradeProfileManagerCommand>(
                "TradeProfileManager", "Trade\nProfiles", "TradeProfiles"));
        }

        private PushButtonData Button<T>(string internalName, string label, string? iconBaseName = null)
            where T : IExternalCommand
        {
            string baseName = string.IsNullOrEmpty(iconBaseName) ? internalName : iconBaseName!;
            return new PushButtonData(
                internalName,
                label,
                _assemblyPath,
                typeof(T).FullName ?? typeof(T).Name)
            {
                Image = Icon($"{baseName}_16") ?? Icon("RibbonIcon16"),
                LargeImage = Icon($"{baseName}_32") ?? Icon("RibbonIcon32")
            };
        }

        private static BitmapImage? Icon(string name)
        {
            try
            {
                string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
                string uriPath = $"pack://application:,,,/{assemblyName};component/Resources/Icons/{name}.png";
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(uriPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
