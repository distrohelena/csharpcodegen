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
        /// Gets or sets the relative path from the generated TypeScript file to the runtime helper root.
        /// </summary>
        public string RuntimeImportPath { get; set; } = string.Empty;

        /// <summary>
        /// When true, retain preprocessor symbols defined in the source project in addition to the TypeScript ones.
        /// </summary>
        public bool IncludeProjectDefinedPreprocessorSymbols { get; set; }

        /// <summary>
        /// When true, emit a strict tsconfig alongside generated output.
        /// </summary>
        public bool EmitStrictTsConfig { get; set; }

        /// <summary>
        /// Extra runtime requirements a driver supplies on top of the built-in catalog. Each maps a C#
        /// type name to a hand-written runtime module so the emitter IMPORTS it (as a native class)
        /// instead of generating a body. Use for project-specific runtime types the shared catalog
        /// must stay neutral about — e.g. a browser's noble-backed post-quantum crypto. A path that
        /// does not start with <c>./</c> is emitted verbatim, so it can point outside the runtime base.
        /// </summary>
        public List<TypeScriptRuntimeRequirementDefinition> AdditionalRuntimeRequirements { get; set; } = new();

        /// <summary>
        /// Creates a shallow copy so that callers can tweak options without mutating shared instances.
        /// </summary>
        /// <returns>A copy of the current options instance.</returns>
        public TypeScriptConversionOptions Clone() {
            return new TypeScriptConversionOptions {
                Reflection = Reflection.Clone(),
                AdditionalPreprocessorSymbols = new List<string>(AdditionalPreprocessorSymbols),
                RuntimeImportPath = RuntimeImportPath,
                IncludeProjectDefinedPreprocessorSymbols = IncludeProjectDefinedPreprocessorSymbols,
                EmitStrictTsConfig = EmitStrictTsConfig,
                AdditionalRuntimeRequirements = new List<TypeScriptRuntimeRequirementDefinition>(AdditionalRuntimeRequirements)
            };
        }
    }
}
