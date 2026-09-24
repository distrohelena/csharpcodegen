namespace cs2.cpp {
    /// <summary>
    /// Writes a compile-validation harness for generated C++ output.
    /// </summary>
    public static class CPPCompileHarnessWriter {
        /// <summary>
        /// Gets the generated unity translation unit file name.
        /// </summary>
        public const string UnityFileName = "generated_unity.cpp";

        /// <summary>
        /// Gets the generated GCC build script file name.
        /// </summary>
        public const string GccBuildScriptFileName = "build_gcc.sh";

        /// <summary>
        /// Gets the generated MSVC build script file name.
        /// </summary>
        public const string MsvcBuildScriptFileName = "build_msvc.bat";

        /// <summary>
        /// Gets the output-relative, forward-slash path of the isolated native-imports translation unit. It is excluded
        /// from the unity build because it declares raw native prototypes that must not share a translation unit with
        /// generated code, and is compiled by the build scripts as a separate object instead.
        /// </summary>
        const string NativeImportsRelativeSourcePath = CPPNativeImportsWriter.FolderName + "/" + CPPNativeImportsWriter.SourceFileName;

        /// <summary>
        /// Writes the compile-validation harness files for the generated output folder.
        /// </summary>
        /// <param name="outputFolder">The generated output folder.</param>
        /// <param name="options">The active conversion options.</param>
        /// <returns>The emitted compile-harness file paths.</returns>
        public static IReadOnlyList<string> Write(string outputFolder, CPPConversionOptions options) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            Directory.CreateDirectory(outputFolder);

            List<string> sourceFiles = Directory.GetFiles(outputFolder, "*.cpp", SearchOption.AllDirectories)
                .Where(path => !string.Equals(Path.GetFileName(path), UnityFileName, StringComparison.OrdinalIgnoreCase))
                .Where(path => !IsRuntimeMetadataAttributeSource(path))
                .Select(path => Path.GetRelativePath(outputFolder, path).Replace('\\', '/'))
                .Where(path => !string.Equals(path, NativeImportsRelativeSourcePath, StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            bool hasNativeImportsSource = File.Exists(Path.Combine(outputFolder, CPPNativeImportsWriter.FolderName, CPPNativeImportsWriter.SourceFileName));

            if (sourceFiles.Count == 0) {
                throw new InvalidOperationException("Cannot write a compile harness when the generated output folder does not contain any .cpp files.");
            }

            string unityPath = Path.Combine(outputFolder, UnityFileName);
            string gccScriptPath = Path.Combine(outputFolder, GccBuildScriptFileName);
            string msvcScriptPath = Path.Combine(outputFolder, MsvcBuildScriptFileName);

            File.WriteAllText(unityPath, BuildUnitySource(sourceFiles));
            File.WriteAllText(gccScriptPath, BuildGccScript(options, hasNativeImportsSource));
            File.WriteAllText(msvcScriptPath, BuildMsvcScript(options, hasNativeImportsSource));

            return new[] {
                unityPath,
                gccScriptPath,
                msvcScriptPath
            };
        }

        /// <summary>
        /// Builds the unity translation unit that includes every generated implementation file once.
        /// </summary>
        /// <param name="sourceFiles">The relative implementation file paths to include.</param>
        /// <returns>The unity translation unit source text.</returns>
        static string BuildUnitySource(IReadOnlyList<string> sourceFiles) {
            List<string> lines = new List<string> {
                "// Generated compile-validation unity translation unit.",
                string.Empty
            };

            foreach (string sourceFile in sourceFiles) {
                lines.Add($"#include \"{sourceFile}\"");
            }

            lines.Add(string.Empty);
            return string.Join(Environment.NewLine, lines);
        }

        static bool IsRuntimeMetadataAttributeSource(string path) {
            string fileName = Path.GetFileName(path);
            return string.Equals(fileName, "GeneratedRuntimeModuleManifestAttribute.cpp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "RuntimeFeatureRequirementAttribute.cpp", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds the GCC smoke-compile script for the generated unity translation unit, plus a second compile of the
        /// isolated native-imports translation unit when one was generated.
        /// </summary>
        /// <param name="options">The active conversion options.</param>
        /// <param name="hasNativeImportsSource">Whether the native-imports translation unit exists and must be compiled as a second object.</param>
        /// <returns>The shell script contents.</returns>
        static string BuildGccScript(CPPConversionOptions options, bool hasNativeImportsSource) {
            string languageStandard = ResolveGccLanguageStandard(options);
            List<string> lines = new List<string> {
                "#!/usr/bin/env bash",
                "set -euo pipefail",
                "SCRIPT_DIR=\"$(cd \"$(dirname \"${BASH_SOURCE[0]}\")\" && pwd)\"",
                "BUILD_DIR=\"$SCRIPT_DIR/build/gcc\"",
                "mkdir -p \"$BUILD_DIR\"",
                $"g++ -std={languageStandard} -I\"$SCRIPT_DIR\" -I\"$SCRIPT_DIR/runtime\" -c \"$SCRIPT_DIR/{UnityFileName}\" -o \"$BUILD_DIR/generated_unity.o\""
            };
            if (hasNativeImportsSource) {
                lines.Add($"g++ -std={languageStandard} -I\"$SCRIPT_DIR\" -I\"$SCRIPT_DIR/runtime\" -c \"$SCRIPT_DIR/{NativeImportsRelativeSourcePath}\" -o \"$BUILD_DIR/native_imports.o\"");
            }
            lines.Add(string.Empty);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Builds the MSVC smoke-compile script for the generated unity translation unit, plus a second compile of the
        /// isolated native-imports translation unit when one was generated.
        /// </summary>
        /// <param name="options">The active conversion options.</param>
        /// <param name="hasNativeImportsSource">Whether the native-imports translation unit exists and must be compiled as a second object.</param>
        /// <returns>The batch script contents.</returns>
        static string BuildMsvcScript(CPPConversionOptions options, bool hasNativeImportsSource) {
            string languageStandard = ResolveMsvcLanguageStandard(options);
            List<string> lines = new List<string> {
                "@echo off",
                "setlocal",
                "set SCRIPT_DIR=%~dp0",
                "set BUILD_DIR=%SCRIPT_DIR%build\\msvc",
                "if not exist \"%BUILD_DIR%\" mkdir \"%BUILD_DIR%\"",
                $"cl /nologo /std:{languageStandard} /EHsc /I\"%SCRIPT_DIR%.\" /I\"%SCRIPT_DIR%runtime\" /c \"%SCRIPT_DIR%{UnityFileName}\" /Fo\"%BUILD_DIR%\\generated_unity.obj\""
            };
            if (hasNativeImportsSource) {
                lines.Add($"cl /nologo /std:{languageStandard} /EHsc /I\"%SCRIPT_DIR%.\" /I\"%SCRIPT_DIR%runtime\" /c \"%SCRIPT_DIR%{NativeImportsRelativeSourcePath.Replace('/', '\\')}\" /Fo\"%BUILD_DIR%\\native_imports.obj\"");
            }
            lines.Add(string.Empty);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Resolves the GCC language-standard switch for the active compile harness.
        /// </summary>
        /// <param name="options">The active conversion options.</param>
        /// <returns>The GCC language-standard argument.</returns>
        static string ResolveGccLanguageStandard(CPPConversionOptions options) {
            return "c++20";
        }

        /// <summary>
        /// Resolves the MSVC language-standard switch for the active compile harness.
        /// </summary>
        /// <param name="options">The active conversion options.</param>
        /// <returns>The MSVC language-standard argument.</returns>
        static string ResolveMsvcLanguageStandard(CPPConversionOptions options) {
            return "c++20";
        }
    }
}
