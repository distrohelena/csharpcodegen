using System.Collections.Generic;
using cs2.ts.util;

namespace cs2.ts {
    /// <summary>
    /// Options that customize how C# is converted to TypeScript, including optional reflection output.
    /// </summary>
    public sealed class TypeScriptConversionOptions {
        /// <summary>
        /// Provides a shared default options instance.
        /// </summary>
        public static TypeScriptConversionOptions Default { get; } = new TypeScriptConversionOptions();

        /// <summary>
        /// Controls reflection emission for the TypeScript backend, including enablement and runtime identifiers.
        /// </summary>
        public ReflectionOptions Reflection { get; set; } = new ReflectionOptions();

        /// <summary>
        /// Additional preprocessor symbols that should be treated as defined during TypeScript conversion.
        /// </summary>
        public List<string> AdditionalPreprocessorSymbols { get; set; } = new();

        /// <summary>
        /// When true, retain preprocessor symbols defined in the source project in addition to the TypeScript ones.
        /// </summary>
        public bool IncludeProjectDefinedPreprocessorSymbols { get; set; }

        /// <summary>
        /// Creates a shallow copy so that callers can tweak options without mutating shared instances.
        /// </summary>
        /// <returns>A copy of the current options instance.</returns>
        public TypeScriptConversionOptions Clone() {
            return new TypeScriptConversionOptions {
                Reflection = Reflection.Clone(),
                AdditionalPreprocessorSymbols = new List<string>(AdditionalPreprocessorSymbols),
                IncludeProjectDefinedPreprocessorSymbols = IncludeProjectDefinedPreprocessorSymbols
            };
        }
    }
}
