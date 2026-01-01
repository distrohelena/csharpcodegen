using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace cs2.ts.util {
    /// <summary>
    /// Formats generated TypeScript output using the TypeScript language service.
    /// </summary>
    public static class TypeScriptCodeFormatter {
        /// <summary>
        /// Provides candidate node executable names to probe.
        /// </summary>
        static readonly string[] NodeExecutableNames = new[] { "node", "node.exe" };

        /// <summary>
        /// Formats TypeScript output using the TypeScript language service.
        /// </summary>
        /// <param name="source">The raw TypeScript source.</param>
        /// <param name="runtimeDir">The .net.ts runtime directory used to resolve typescript.</param>
        /// <param name="logger">Optional logger for warnings.</param>
        /// <returns>The formatted source.</returns>
        public static string Format(string source, string runtimeDir, Action<string> logger) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }

            if (TryFormatWithTypeScript(source, runtimeDir, logger, out string formatted)) {
                return formatted;
            }

            throw new InvalidOperationException("TypeScript formatter failed; ensure node and the typescript module are available.");
        }

        /// <summary>
        /// Attempts to format the output using the TypeScript language service.
        /// </summary>
        /// <param name="source">The raw TypeScript source.</param>
        /// <param name="runtimeDir">The .net.ts runtime directory used to resolve typescript.</param>
        /// <param name="logger">Optional logger for warnings.</param>
        /// <param name="formatted">Outputs the formatted source.</param>
        /// <returns>True when formatting succeeded.</returns>
        static bool TryFormatWithTypeScript(string source, string runtimeDir, Action<string> logger, out string formatted) {
            formatted = string.Empty;

            string typescriptDir = ResolveTypeScriptDirectory(runtimeDir);
            if (string.IsNullOrWhiteSpace(typescriptDir)) {
                logger?.Invoke("Warning: TypeScript formatter skipped because the typescript module was not found.");
                return false;
            }

            string nodePath = ResolveNodePath(runtimeDir);
            if (string.IsNullOrWhiteSpace(nodePath)) {
                logger?.Invoke("Warning: TypeScript formatter skipped because node was not found.");
                return false;
            }

            string nodeModulesPath = Directory.GetParent(typescriptDir)?.FullName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nodeModulesPath) || !Directory.Exists(nodeModulesPath)) {
                return false;
            }

            string scriptPath = EnsureFormatScript();
            if (string.IsNullOrWhiteSpace(scriptPath)) {
                return false;
            }

            string inputPath = Path.Combine(Path.GetTempPath(), $"cs2.ts.format.{Guid.NewGuid():N}.ts");
            string outputPath = Path.Combine(Path.GetTempPath(), $"cs2.ts.format.{Guid.NewGuid():N}.out.ts");
            try {
                File.WriteAllText(inputPath, source);

                string workingDirectory = ResolveWorkingDirectory(runtimeDir, nodeModulesPath);
                ProcessStartInfo startInfo = new ProcessStartInfo {
                    FileName = nodePath,
                    Arguments = $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                AppendNodePath(startInfo, nodeModulesPath);

                using Process process = Process.Start(startInfo);
                if (process == null) {
                    logger?.Invoke("Warning: failed to start TypeScript formatter.");
                    return false;
                }

                string outputError = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(outputError)) {
                    logger?.Invoke(outputError);
                }

                if (process.ExitCode != 0) {
                    logger?.Invoke($"Warning: TypeScript formatter exited with code {process.ExitCode}.");
                    return false;
                }

                if (!File.Exists(outputPath)) {
                    return false;
                }

                formatted = File.ReadAllText(outputPath);
                if (string.IsNullOrWhiteSpace(formatted)) {
                    return false;
                }

                return true;
            } catch {
                return false;
            } finally {
                TryDeleteTempFile(inputPath);
                TryDeleteTempFile(outputPath);
            }
        }

        /// <summary>
        /// Ensures the Node formatting script exists and returns its path.
        /// </summary>
        /// <returns>The script path when available.</returns>
        static string EnsureFormatScript() {
            string scriptPath = Path.Combine(Path.GetTempPath(), "cs2.ts.format.js");
            string scriptContents = GetScriptContents();

            if (File.Exists(scriptPath)) {
                string existing = File.ReadAllText(scriptPath);
                if (string.Equals(existing, scriptContents, StringComparison.Ordinal)) {
                    return scriptPath;
                }
            }

            try {
                File.WriteAllText(scriptPath, scriptContents);
                return scriptPath;
            } catch {
                return string.Empty;
            }
        }

        /// <summary>
        /// Returns the formatting script contents.
        /// </summary>
        /// <returns>The script contents.</returns>
        static string GetScriptContents() {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("const fs = require('fs');");
            builder.AppendLine("const ts = require('typescript');");
            builder.AppendLine("const inputPath = process.argv[2];");
            builder.AppendLine("const outputPath = process.argv[3];");
            builder.AppendLine("if (!inputPath || !outputPath) { process.stderr.write('Missing input/output path.'); process.exit(1); }");
            builder.AppendLine("const input = fs.readFileSync(inputPath, 'utf8');");
            builder.AppendLine("const fileName = inputPath;");
            builder.AppendLine("const formatSettings = ts.getDefaultFormatCodeSettings();");
            builder.AppendLine("formatSettings.indentSize = 4;");
            builder.AppendLine("formatSettings.tabSize = 4;");
            builder.AppendLine("formatSettings.convertTabsToSpaces = true;");
            builder.AppendLine("formatSettings.newLineCharacter = ts.sys.newLine;");
            builder.AppendLine("const host = {");
            builder.AppendLine("  getScriptFileNames: () => [fileName],");
            builder.AppendLine("  getScriptVersion: () => '0',");
            builder.AppendLine("  getScriptSnapshot: (name) => name === fileName ? ts.ScriptSnapshot.fromString(input) : undefined,");
            builder.AppendLine("  getCurrentDirectory: () => process.cwd(),");
            builder.AppendLine("  getCompilationSettings: () => ({}),");
            builder.AppendLine("  getDefaultLibFileName: (opts) => ts.getDefaultLibFilePath(opts),");
            builder.AppendLine("  fileExists: ts.sys.fileExists,");
            builder.AppendLine("  readFile: ts.sys.readFile,");
            builder.AppendLine("  readDirectory: ts.sys.readDirectory");
            builder.AppendLine("};");
            builder.AppendLine("const service = ts.createLanguageService(host, ts.createDocumentRegistry());");
            builder.AppendLine("const edits = service.getFormattingEditsForDocument(fileName, formatSettings);");
            builder.AppendLine("const output = ts.textChanges.applyChanges(input, edits);");
            builder.AppendLine("fs.writeFileSync(outputPath, output, 'utf8');");
            return builder.ToString();
        }

        /// <summary>
        /// Attempts to delete a temporary file, ignoring failures.
        /// </summary>
        /// <param name=\"path\">The file path to delete.</param>
        static void TryDeleteTempFile(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch {
            }
        }

        /// <summary>
        /// Resolves a usable TypeScript module directory.
        /// </summary>
        /// <param name="runtimeDir">The runtime directory to search from.</param>
        /// <returns>The resolved typescript module directory or empty.</returns>
        static string ResolveTypeScriptDirectory(string runtimeDir) {
            string configured = Environment.GetEnvironmentVariable("CS2_TS_TYPESCRIPT_DIR");
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) {
                return configured;
            }

            string direct = TryResolveTypeScriptDirectory(runtimeDir);
            if (!string.IsNullOrWhiteSpace(direct)) {
                return direct;
            }

            if (string.IsNullOrWhiteSpace(runtimeDir)) {
                return string.Empty;
            }

            DirectoryInfo current = new DirectoryInfo(runtimeDir);
            int depth = 0;
            while (current != null && depth < 6) {
                string candidate = Path.Combine(current.FullName, ".net.ts", "node_modules", "typescript");
                if (Directory.Exists(candidate)) {
                    return candidate;
                }

                candidate = Path.Combine(current.FullName, "node_modules", "typescript");
                if (Directory.Exists(candidate)) {
                    return candidate;
                }

                current = current.Parent;
                depth++;
            }

            return string.Empty;
        }

        /// <summary>
        /// Attempts to resolve a TypeScript module directory from a base path.
        /// </summary>
        /// <param name="runtimeDir">The base path to probe.</param>
        /// <returns>The typescript module directory or empty.</returns>
        static string TryResolveTypeScriptDirectory(string runtimeDir) {
            if (string.IsNullOrWhiteSpace(runtimeDir)) {
                return string.Empty;
            }

            string candidate = Path.Combine(runtimeDir, "node_modules", "typescript");
            if (Directory.Exists(candidate)) {
                return candidate;
            }

            return string.Empty;
        }

        /// <summary>
        /// Resolves the node executable to use for formatting.
        /// </summary>
        /// <param name="runtimeDir">The runtime directory to search from.</param>
        /// <returns>The node executable path or empty.</returns>
        static string ResolveNodePath(string runtimeDir) {
            string configured = Environment.GetEnvironmentVariable("CS2_TS_NODE");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) {
                return configured;
            }

            string pathNode = FindNodeOnPath();
            if (!string.IsNullOrWhiteSpace(pathNode)) {
                return pathNode;
            }

            if (string.IsNullOrWhiteSpace(runtimeDir)) {
                return string.Empty;
            }

            DirectoryInfo current = new DirectoryInfo(runtimeDir);
            int depth = 0;
            while (current != null && depth < 6) {
                string nodeRoot = Path.Combine(current.FullName, "node");
                string bundled = FindBundledNode(nodeRoot);
                if (!string.IsNullOrWhiteSpace(bundled)) {
                    return bundled;
                }

                current = current.Parent;
                depth++;
            }

            return string.Empty;
        }

        /// <summary>
        /// Finds a node executable on the PATH.
        /// </summary>
        /// <returns>The node executable path or empty.</returns>
        static string FindNodeOnPath() {
            string pathValue = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathValue)) {
                return string.Empty;
            }

            string[] segments = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++) {
                string segment = segments[i].Trim();
                if (segment.Length == 0) {
                    continue;
                }

                for (int j = 0; j < NodeExecutableNames.Length; j++) {
                    string candidate = Path.Combine(segment, NodeExecutableNames[j]);
                    if (File.Exists(candidate)) {
                        return candidate;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Finds a bundled node executable within a node root directory.
        /// </summary>
        /// <param name="nodeRoot">The node root directory.</param>
        /// <returns>The node executable path or empty.</returns>
        static string FindBundledNode(string nodeRoot) {
            if (string.IsNullOrWhiteSpace(nodeRoot) || !Directory.Exists(nodeRoot)) {
                return string.Empty;
            }

            string direct = TryResolveNodeInDirectory(nodeRoot);
            if (!string.IsNullOrWhiteSpace(direct)) {
                return direct;
            }

            string[] candidates = Directory.GetDirectories(nodeRoot);
            for (int i = 0; i < candidates.Length; i++) {
                string resolved = TryResolveNodeInDirectory(candidates[i]);
                if (!string.IsNullOrWhiteSpace(resolved)) {
                    return resolved;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Attempts to resolve node within a candidate directory.
        /// </summary>
        /// <param name="candidateDir">The directory to probe.</param>
        /// <returns>The node executable path or empty.</returns>
        static string TryResolveNodeInDirectory(string candidateDir) {
            if (string.IsNullOrWhiteSpace(candidateDir) || !Directory.Exists(candidateDir)) {
                return string.Empty;
            }

            for (int i = 0; i < NodeExecutableNames.Length; i++) {
                string direct = Path.Combine(candidateDir, NodeExecutableNames[i]);
                if (File.Exists(direct)) {
                    return direct;
                }
            }

            string binDir = Path.Combine(candidateDir, "bin");
            for (int i = 0; i < NodeExecutableNames.Length; i++) {
                string binCandidate = Path.Combine(binDir, NodeExecutableNames[i]);
                if (File.Exists(binCandidate)) {
                    return binCandidate;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Resolves the working directory for the formatter process.
        /// </summary>
        /// <param name="runtimeDir">The requested runtime directory.</param>
        /// <param name="nodeModulesPath">The resolved node modules path.</param>
        /// <returns>The working directory.</returns>
        static string ResolveWorkingDirectory(string runtimeDir, string nodeModulesPath) {
            if (!string.IsNullOrWhiteSpace(runtimeDir) && Directory.Exists(runtimeDir)) {
                return runtimeDir;
            }

            string nodeModulesParent = Directory.GetParent(nodeModulesPath)?.FullName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(nodeModulesParent) && Directory.Exists(nodeModulesParent)) {
                return nodeModulesParent;
            }

            return Directory.GetCurrentDirectory();
        }

        /// <summary>
        /// Ensures the node process can resolve node_modules when the runtime location differs.
        /// </summary>
        /// <param name="startInfo">The process start info.</param>
        /// <param name="nodeModulesPath">The resolved node modules path.</param>
        static void AppendNodePath(ProcessStartInfo startInfo, string nodeModulesPath) {
            if (startInfo == null || string.IsNullOrWhiteSpace(nodeModulesPath)) {
                return;
            }

            if (startInfo.Environment.TryGetValue("NODE_PATH", out string existingNodePath)) {
                if (string.IsNullOrWhiteSpace(existingNodePath)) {
                    startInfo.Environment["NODE_PATH"] = nodeModulesPath;
                } else if (!existingNodePath.Contains(nodeModulesPath, StringComparison.OrdinalIgnoreCase)) {
                    startInfo.Environment["NODE_PATH"] = nodeModulesPath + Path.PathSeparator + existingNodePath;
                }
            } else {
                startInfo.Environment["NODE_PATH"] = nodeModulesPath;
            }
        }
    }
}
