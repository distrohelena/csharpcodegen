namespace cs2.cpp {
    /// <summary>
    /// Writes a small handoff contract that lets native hosts consume target-scoped generated core output deterministically.
    /// </summary>
    public static class CPPWindowsHandoffWriter {
        /// <summary>
        /// Gets the generated handoff contract file name.
        /// </summary>
        public const string FileName = "generated_windows_handoff.cmake";

        /// <summary>
        /// Builds the handoff contract text for a resolved deployment-root layout.
        /// </summary>
        /// <param name="layout">The resolved deployment-root layout.</param>
        /// <param name="plan">P/Invoke plan whose imports and link libraries remain part of the handoff contract.</param>
        /// <returns>The handoff contract source text.</returns>
        public static string Write(CPPDeploymentBuildLayout layout, CPPPInvokePlan plan) {
            if (layout == null) {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null) {
                throw new ArgumentNullException(nameof(plan));
            }

            return BuildFileText(plan, layout);
        }

        /// <summary>
        /// Writes the handoff contract into the generated output folder.
        /// </summary>
        /// <param name="outputFolder">Root output folder for the generated C++ project.</param>
        /// <param name="plan">P/Invoke plan whose native-import translation unit and link libraries are published to the host.</param>
        /// <param name="layout">Optional deployment layout whose target-scoped intermediate root is published with the existing generated-file contract.</param>
        /// <returns>The emitted handoff contract path.</returns>
        public static string Write(string outputFolder, CPPPInvokePlan plan, CPPDeploymentBuildLayout layout = null) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            if (plan == null) {
                throw new ArgumentNullException(nameof(plan));
            }

            Directory.CreateDirectory(outputFolder);

            string filePath = Path.Combine(outputFolder, FileName);
            File.WriteAllText(filePath, BuildFileText(plan, layout));
            return filePath;
        }

        /// <summary>
        /// Builds the CMake handoff contract that describes the generated core layout expected by the Windows host,
        /// including the isolated native-import translation unit and the native libraries it must link against. Both
        /// native variables are empty strings when the plan has no native imports.
        /// </summary>
        /// <param name="plan">P/Invoke plan that decides the native-import variables.</param>
        /// <param name="layout">Optional deployment layout that supplies the native intermediate root.</param>
        /// <returns>The CMake contract source text.</returns>
        static string BuildFileText(CPPPInvokePlan plan, CPPDeploymentBuildLayout layout) {
            string nativeImportsSource = plan.HasNativeImports
                ? "${CPP_GENERATED_CORE_ROOT}/" + CPPNativeImportsWriter.FolderName + "/" + CPPNativeImportsWriter.SourceFileName
                : string.Empty;
            string nativeLinkLibraries = string.Join(";", plan.LinkLibraries);
            List<string> lines = new List<string> {
                "set(CPP_GENERATED_CORE_ROOT \"${CMAKE_CURRENT_LIST_DIR}\")",
                "set(CPP_GENERATED_CONFIG_HEADER \"${CPP_GENERATED_CORE_ROOT}/helcpp_config.hpp\")",
                "set(CPP_GENERATED_UNITY_SOURCE \"${CPP_GENERATED_CORE_ROOT}/generated_unity.cpp\")",
                "set(CPP_GENERATED_FEATURE_MANIFEST_HEADER \"${CPP_GENERATED_CORE_ROOT}/runtime/feature_manifest.hpp\")",
                "set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE \"" + nativeImportsSource + "\")",
                "set(CPP_GENERATED_NATIVE_LINK_LIBRARIES \"" + nativeLinkLibraries + "\")"
            };
            if (layout != null) {
                string intermediateRoot = layout.IntermediateRoot.Replace('\\', '/');
                lines.Insert(1, "set(CPP_NATIVE_INTERMEDIATE_ROOT \"" + intermediateRoot + "\")");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
