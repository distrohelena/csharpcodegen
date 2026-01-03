using cs2.core.Pipeline;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.go.pipeline {
    /// <summary>
    /// Conversion stage that overrides project preprocessor symbols with the converter's Go set.
    /// </summary>
    internal sealed class GoPreprocessorFilterStage : IConversionStage {
        /// <summary>
        /// Holds the converter that owns the configured preprocessor symbol list.
        /// </summary>
        readonly GoCodeConverter Owner;

        /// <summary>
        /// Initializes the stage with the converter that supplies preprocessor symbol settings.
        /// </summary>
        /// <param name="owner">The converter providing preprocessor symbol configuration.</param>
        public GoPreprocessorFilterStage(GoCodeConverter owner) {
            Owner = owner;
        }

        /// <summary>
        /// Updates the project's parse options to use the converter's preprocessor symbols.
        /// </summary>
        /// <param name="session">The conversion session being processed.</param>
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
