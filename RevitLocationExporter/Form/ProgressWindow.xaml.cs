using Autodesk.Revit.UI;
using System.Windows;
using System.Windows.Threading;

namespace RevitLocationExporter.Form
{


    /// <summary>
    /// Modeless WPF progress window.
    ///
    /// THREADING NOTE
    /// ==============
    /// Revit's IExternalCommand.Execute() runs on the main UI thread.
    /// The WPF window is created and updated on that same thread, so plain
    /// property assignment is safe.  We pump the Dispatcher between file
    /// operations (in the command loop) so the window can repaint without
    /// requiring a background thread.
    /// </summary>
    public partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Updates all visible progress indicators.
        /// Must be called from the UI thread (safe when called from Execute()).
        /// </summary>
        /// <param name="fileName">Short file name currently being processed.</param>
        /// <param name="current">1-based current file index.</param>
        /// <param name="total">Total number of files.</param>
        /// <param name="statusMessage">Free-text status line.</param>
        public void UpdateProgress(string fileName, int current, int total, string statusMessage)
        {
            FileNameText.Text = fileName;
            StatusText.Text   = statusMessage;

            // Convert to percentage for the ProgressBar
            MainProgressBar.Maximum = total > 0 ? total : 1;
            MainProgressBar.Value   = current;

            // Force a synchronous render pass so the user sees each update
            // without us needing a background thread.
            Dispatcher.Invoke(DispatcherPriority.Render, new System.Action(() => { }));
        }
    }
}

