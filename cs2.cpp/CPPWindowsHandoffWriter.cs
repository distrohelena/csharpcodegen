namespace cs2.cpp {
    /// <summary>
    /// Writes a small handoff contract that lets native hosts consume target-scoped generated core output deterministically.
    /// </summary>
    public static class CPPWindowsHandoffWriter {
        /// <summary>
        /// Gets the generated handoff contract file name.
        /// </summary>
        public const string FileName = "helengine_windows_handoff.cmake";

        /// <summary>
        /// Builds the handoff contract text for a resolved deployment-root layout.
        /// </summary>
        /// <param name="layout">The resolved deployment-root layout.</param>
        /// <returns>The handoff contract source text.</returns>
        public static string Write(CPPDeploymentBuildLayout layout) {
            if (layout == null) {
                throw new ArgumentNullException(nameof(layout));
            }

            return BuildFileText(layout);
        }

        /// <summary>
        /// Writes the handoff contract into the generated output folder.
        /// </summary>
        /// <param name="outputFolder">Root output folder for the generated C++ project.</param>
        /// <param name="layout">The resolved deployment-root layout.</param>
        /// <returns>The emitted handoff contract path.</returns>
        public static string Write(string outputFolder, CPPDeploymentBuildLayout layout) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            if (layout == null) {
                throw new ArgumentNullException(nameof(layout));
            }

            Directory.CreateDirectory(outputFolder);

            string filePath = Path.Combine(outputFolder, FileName);
            File.WriteAllText(filePath, BuildFileText(layout));
            return filePath;
        }

        /// <summary>
        /// Builds the CMake handoff contract that describes the generated core layout expected by the Windows host.
        /// </summary>
        /// <param name="layout">The resolved deployment-root layout.</param>
        /// <returns>The CMake contract source text.</returns>
        static string BuildFileText(CPPDeploymentBuildLayout layout) {
            string intermediateRoot = layout.IntermediateRoot.Replace('\\', '/');

            return $$"""
set(HELENGINE_GENERATED_CORE_ROOT "${CMAKE_CURRENT_LIST_DIR}")
set(HELENGINE_NATIVE_INTERMEDIATE_ROOT "{{intermediateRoot}}")
set(HELENGINE_GENERATED_CONFIG_HEADER "${HELENGINE_GENERATED_CORE_ROOT}/helcpp_config.hpp")
set(HELENGINE_GENERATED_UNITY_SOURCE "${HELENGINE_GENERATED_CORE_ROOT}/helengine_core_unity.cpp")
set(HELENGINE_GENERATED_FEATURE_MANIFEST_HEADER "${HELENGINE_GENERATED_CORE_ROOT}/runtime/feature_manifest.hpp")
""";
        }
    }
}
