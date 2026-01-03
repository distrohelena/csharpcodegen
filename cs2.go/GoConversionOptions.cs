using System.Collections.Generic;

namespace cs2.go {
    /// <summary>
    /// Options that customize how C# is converted to Go.
    /// </summary>
    public sealed class GoConversionOptions {
        /// <summary>
        /// Provides a shared default options instance.
        /// </summary>
        public static GoConversionOptions Default { get; } = new GoConversionOptions();

        /// <summary>
        /// Gets or sets the package name used for generated Go files.
        /// </summary>
        public string PackageName { get; set; } = "main";

        /// <summary>
        /// Additional preprocessor symbols treated as defined during Go conversion.
        /// </summary>
        public List<string> AdditionalPreprocessorSymbols { get; set; } = new();

        /// <summary>
        /// When true, retain preprocessor symbols defined in the source project in addition to the Go ones.
        /// </summary>
        public bool IncludeProjectDefinedPreprocessorSymbols { get; set; }

        /// <summary>
        /// Creates a shallow copy so callers can tweak options without mutating shared instances.
        /// </summary>
        /// <returns>A copy of the current options instance.</returns>
        public GoConversionOptions Clone() {
            return new GoConversionOptions {
                PackageName = PackageName,
                AdditionalPreprocessorSymbols = new List<string>(AdditionalPreprocessorSymbols),
                IncludeProjectDefinedPreprocessorSymbols = IncludeProjectDefinedPreprocessorSymbols
            };
        }
    }
}
