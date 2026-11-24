using Autodesk.Revit.DB;

namespace RevitLocationExporter.Core
{
    /// <summary>
    /// Revit failures preprocessor that automatically deletes warnings so batch operations don't halt.
    /// </summary>
    public sealed class SilentFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                    accessor.DeleteWarning(failure);
            }
            return FailureProcessingResult.Continue;
        }
    }
}
