using cs2.core.Pipeline;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.ts {
    /// <summary>
    /// Conversion stage that overrides project preprocessor symbols with the converter's TypeScript set.
    /// </summary>
    internal sealed class TypeScriptPreprocessorFilterStage : IConversionStage {
        /// <summary>
        /// Holds the converter that owns the configured preprocessor symbol list.
        /// </summary>
        readonly TypeScriptCodeConverter owner;

        /// <summary>
        /// Initializes the stage with the converter that supplies preprocessor symbol settings.
        /// </summary>
        /// <param name="owner">The converter providing preprocessor symbol configuration.</param>
        public TypeScriptPreprocessorFilterStage(TypeScriptCodeConverter owner) {
            this.owner = owner;
        }

        /// <summary>
        /// Updates the project's parse options to use the converter's preprocessor symbols.
        /// </summary>
        /// <param name="session">The conversion session being processed.</param>
        public void Execute(ConversionSession session) {
            if (owner.IncludeProjectPreprocessorSymbols) {
                return;
            }

            if (session.Project.ParseOptions is not CSharpParseOptions parseOptions) {
                return;
            }

            CSharpParseOptions updated = parseOptions.WithPreprocessorSymbols(owner.PreprocessorSymbols);
            session.Project = session.Project.WithParseOptions(updated);
        }
    }
}
