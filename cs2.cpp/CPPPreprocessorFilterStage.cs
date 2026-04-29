using cs2.core.Pipeline;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp {
    /// <summary>
    /// Applies the backend-owned C++ preprocessor symbol selection to the source project when requested.
    /// </summary>
    internal sealed class CPPPreprocessorFilterStage : IConversionStage {
        /// <summary>
        /// Holds the converter that owns the resolved symbol configuration.
        /// </summary>
        readonly CPPCodeConverter Owner;

        /// <summary>
        /// Initializes the preprocessor filter stage for the supplied converter.
        /// </summary>
        /// <param name="owner">Converter that provides the active symbol selection.</param>
        public CPPPreprocessorFilterStage(CPPCodeConverter owner) {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>
        /// Replaces project-defined parse symbols when the active option set requires backend-controlled filtering.
        /// </summary>
        /// <param name="session">The active conversion session.</param>
        public void Execute(ConversionSession session) {
            if (Owner.IncludeProjectPreprocessorSymbols) {
                return;
            }

            if (session.Project.ParseOptions is not CSharpParseOptions parseOptions) {
                return;
            }

            CSharpParseOptions updated = parseOptions.WithPreprocessorSymbols(Owner.PreprocessorSymbols);
            session.Project = session.Project.WithParseOptions(updated);
        }
    }
}
