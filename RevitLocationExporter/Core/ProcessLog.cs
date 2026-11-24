using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.UI;

namespace RevitLocationExporter.Core
{
    /// <summary>
    /// Thread-safe logger and summary aggregator for batch export operations.
    /// </summary>
    public sealed class ProcessLog
    {
        private readonly List<string> _errors = new();
        private readonly List<string> _warnings = new();
        private readonly List<string> _infos = new();

        public void Error(string loc, string msg) => _errors.Add($"[{loc}] {msg}");
        public void Warning(string loc, string msg) => _warnings.Add($"[{loc}] {msg}");
        public void Info(string loc, string msg) => _infos.Add($"[{loc}] {msg}");

        public bool HasErrors => _errors.Count > 0;

        public void ShowSummary()
        {
            var sb = new StringBuilder();

            if (_infos.Count > 0)
            {
                sb.AppendLine($"✓ Completed: {_infos.Count} operation(s)");
                foreach (var i in _infos) sb.AppendLine("  " + i);
            }
            if (_warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"⚠ Warnings ({_warnings.Count}):");
                foreach (var w in _warnings) sb.AppendLine("  " + w);
            }
            if (_errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"✗ Errors ({_errors.Count}):");
                foreach (var e in _errors) sb.AppendLine("  " + e);
            }

            TaskDialog.Show("Location Exporter – Summary", sb.ToString());
        }
    }
}
