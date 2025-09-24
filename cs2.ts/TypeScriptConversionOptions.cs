using System.Collections.Generic;
using cs2.ts.util;

namespace cs2.ts {
    /// <summary>
    /// Options that customize how C# is converted to TypeScript, including optional reflection output.
    /// </summary>
    public sealed class TypeScriptConversionOptions {
        public static TypeScriptConversionOptions Default { get; } = new TypeScriptConversionOptions();

        /// <summary>
        /// Enables generation of reflection metadata and registration calls.
        /// </summary>
        public bool EnableReflection { get; set; } = true;

        /// <summary>
        /// When true, register calls are emitted as private static fields to cache Type metadata.
        /// When false, metadata registrations are appended after the declaration via function calls.
        /// </summary>
        public bool UseStaticReflectionCache { get; set; } = true;

        /// <summary>
        /// Allows overriding the low-level reflection emission knobs (field names, import identifiers, etc.).
        /// </summary>
        public TypeScriptReflectionOptions Reflection { get; set; } = new TypeScriptReflectionOptions { EnableReflection = true };

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
        public TypeScriptConversionOptions Clone() {
            return new TypeScriptConversionOptions {
                EnableReflection = EnableReflection,
                UseStaticReflectionCache = UseStaticReflectionCache,
                Reflection = Reflection.Clone(),
                AdditionalPreprocessorSymbols = new List<string>(AdditionalPreprocessorSymbols),
                IncludeProjectDefinedPreprocessorSymbols = IncludeProjectDefinedPreprocessorSymbols
            };
        }
    }
}
