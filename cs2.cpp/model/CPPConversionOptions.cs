using System;

namespace cs2.cpp {
    /// <summary>
    /// Defines the target and reporting options for a C++ conversion run.
    /// </summary>
    public class CPPConversionOptions {
        /// <summary>
        /// Gets or sets the compiler profile that shapes compiler-specific output.
        /// </summary>
        public CPPCompilerProfile CompilerProfile { get; set; } = CPPCompilerProfile.CreateMsvc();

        /// <summary>
        /// Gets or sets the platform profile that shapes platform-specific output.
        /// </summary>
        public CPPPlatformProfile PlatformProfile { get; set; } = CPPPlatformProfile.CreateWindowsHeadless();

        /// <summary>
        /// Gets or sets the runtime profile that constrains generated helper usage.
        /// </summary>
        public CPPRuntimeProfile RuntimeProfile { get; set; } = CPPRuntimeProfile.CreateStlLite();

        /// <summary>
        /// Gets or sets whether unsupported constructs should be recorded in the conversion report.
        /// </summary>
        public bool CollectDiagnostics { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the converter should stop on the first reported error.
        /// </summary>
        public bool FailOnError { get; set; }

        /// <summary>
        /// Gets or sets whether project-defined preprocessor symbols remain enabled in addition to the backend symbols.
        /// </summary>
        public bool IncludeProjectDefinedPreprocessorSymbols { get; set; }

        /// <summary>
        /// Gets or sets the additional preprocessor symbols injected into the C++ conversion pipeline.
        /// </summary>
        public IReadOnlyList<string> AdditionalPreprocessorSymbols { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets whether the converter should write a serialized conversion report alongside generated output.
        /// </summary>
        public bool WriteConversionReport { get; set; }

        /// <summary>
        /// Gets or sets the explicit feature profile that controls optional subsystem pruning.
        /// </summary>
        public CPPBuildFeatureProfile BuildFeatureProfile { get; set; } = CPPBuildFeatureProfile.CreateDefault();

        /// <summary>
        /// Gets or sets whether native runtime metadata should be loaded during converter construction.
        /// </summary>
        public bool LoadNativeRuntimeMetadata { get; set; } = true;

        /// <summary>
        /// Gets or sets an optional mirror folder that receives a fresh copy of the generated core for the Windows host repository.
        /// </summary>
        public string WindowsHandoffOutputFolder { get; set; } = string.Empty;

        /// <summary>
        /// Creates the default option set for the first Windows headless milestone.
        /// </summary>
        /// <returns>The default conversion options.</returns>
        public static CPPConversionOptions CreateDefault() {
            return new CPPConversionOptions();
        }
    }
}
