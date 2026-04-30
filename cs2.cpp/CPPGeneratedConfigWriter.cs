namespace cs2.cpp {
    /// <summary>
    /// Writes generated configuration headers that describe the active backend profiles and runtime requirements.
    /// </summary>
    public static class CPPGeneratedConfigWriter {
        /// <summary>
        /// Gets the default generated config file name.
        /// </summary>
        public const string DefaultFileName = "helcpp_config.hpp";

        /// <summary>
        /// Writes the generated config header for the active conversion run.
        /// </summary>
        /// <param name="outputFolder">Output folder that receives the config header.</param>
        /// <param name="options">Active conversion options.</param>
        /// <param name="registrar">Runtime requirement registrar for the active conversion run.</param>
        /// <param name="buildUsageReport">Resolved feature decisions for the active build.</param>
        /// <returns>The full path to the generated config header.</returns>
        public static string Write(string outputFolder, CPPConversionOptions options, CPPRuntimeRequirementRegistrar registrar, CPPBuildUsageReport buildUsageReport = null) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            if (registrar == null) {
                throw new ArgumentNullException(nameof(registrar));
            }

            Directory.CreateDirectory(outputFolder);

            string filePath = Path.Combine(outputFolder, DefaultFileName);
            List<string> lines = new List<string> {
                "#pragma once",
                string.Empty,
                "#define HE_CPP_GENERATED_CONFIG 1",
                $"#define {options.CompilerProfile.DefineName} 1",
                $"#define {options.PlatformProfile.DefineName} 1",
                $"#define {options.RuntimeProfile.DefineName} 1",
                $"#define HE_CPP_USE_STD_STRING {ToDefineValue(options.RuntimeProfile.UseStdString)}",
                $"#define HE_CPP_USE_STD_VECTOR {ToDefineValue(options.RuntimeProfile.UseStdVector)}",
                $"#define HE_CPP_USE_STD_UNORDERED_MAP {ToDefineValue(options.RuntimeProfile.UseStdUnorderedMap)}",
                $"#define HE_CPP_USE_EXCEPTIONS {ToDefineValue(options.RuntimeProfile.UseExceptions)}",
                $"#define HE_CPP_USE_RTTI {ToDefineValue(options.RuntimeProfile.UseRtti)}",
                $"#define HE_CPP_PLATFORM_IS_LITTLE_ENDIAN {ToDefineValue(options.PlatformProfile.IsLittleEndian)}",
                $"#define HE_CPP_PLATFORM_IS_WINDOWS_HOST {ToDefineValue(options.PlatformProfile.IsWindowsHost)}"
            };

            AppendFeatureDefines(lines, buildUsageReport ?? new CPPBuildUsageReport());

            foreach (CPPRuntimeRequirementDefinition requirement in registrar.RegisteredRequirements.OrderBy(requirement => requirement.Name, StringComparer.Ordinal)) {
                lines.Add($"#define {requirement.ConfigDefineName} 1");
            }

            File.WriteAllText(filePath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            return filePath;
        }

        static int ToDefineValue(bool value) {
            return value ? 1 : 0;
        }

        static void AppendFeatureDefines(List<string> lines, CPPBuildUsageReport buildUsageReport) {
            foreach (CPPFeatureKind feature in Enum.GetValues<CPPFeatureKind>()) {
                bool enabled = buildUsageReport.GetDecision(feature).Enabled;
                lines.Add($"#define HE_CPP_FEATURE_{feature.ToString().ToUpperInvariant()} {ToDefineValue(enabled)}");
            }
        }
    }
}
