using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace cs2.ts.util {
    /// <summary>
    /// Formats generated TypeScript output using the TypeScript 7 native language server.
    /// </summary>
    public static class TypeScriptCodeFormatter {
        /// <summary>
        /// Names an environment variable that may point directly at a TypeScript 7 native executable.
        /// </summary>
        public const string ExecutableEnvironmentVariable = "CS2_TS_TSGO";

        /// <summary>
        /// The maximum time one formatting session may take; generated files reach several megabytes.
        /// </summary>
        static readonly TimeSpan FormatTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Serializes dependency installation so concurrent formatting calls do not run npm in parallel.
        /// </summary>
        static readonly object InstallLock = new object();

        /// <summary>
        /// Formats TypeScript output using the TypeScript 7 language server.
        /// </summary>
        /// <param name="source">The raw TypeScript source.</param>
        /// <param name="runtimeDir">The .net.ts runtime directory whose node_modules hosts the TypeScript 7 package.</param>
        /// <param name="logger">Optional logger for installation output and formatter stderr.</param>
        /// <returns>The formatted source.</returns>
        /// <exception cref="InvalidOperationException">Thrown when TypeScript 7 is unavailable or formatting fails.</exception>
        public static string Format(string source, string runtimeDir, Action<string> logger) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }

            string executable = ResolveLanguageServerExecutable(runtimeDir);
            if (string.IsNullOrWhiteSpace(executable)) {
                executable = InstallAndResolveExecutable(runtimeDir, logger);
            }
            if (string.IsNullOrWhiteSpace(executable)) {
                throw new InvalidOperationException(
                    $"TypeScript formatter failed; the TypeScript 7 native executable ({GetPlatformPackageName()}) was not found from '{runtimeDir}'. "
                    + $"Run npm install in the .net.ts runtime folder or set {ExecutableEnvironmentVariable}.");
            }

            string workingDirectory = !string.IsNullOrWhiteSpace(runtimeDir) && Directory.Exists(runtimeDir) ? runtimeDir : Path.GetTempPath();
            return TypeScriptLanguageServerFormatter.Format(executable, source, workingDirectory, FormatTimeout, logger);
        }

        /// <summary>
        /// Resolves the TypeScript 7 native executable from the override variable or the runtime's node_modules.
        /// </summary>
        /// <param name="runtimeDir">The runtime directory to search from; parents are probed as well.</param>
        /// <returns>The executable path, or empty when none is installed.</returns>
        public static string ResolveLanguageServerExecutable(string runtimeDir) {
            string configured = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) {
                return configured;
            }

            if (string.IsNullOrWhiteSpace(runtimeDir)) {
                return string.Empty;
            }

            DirectoryInfo current = new DirectoryInfo(runtimeDir);
            int depth = 0;
            while (current != null && depth < 6) {
                string resolved = TryResolveInNodeModules(Path.Combine(current.FullName, "node_modules"));
                if (resolved.Length == 0) {
                    resolved = TryResolveInNodeModules(Path.Combine(current.FullName, ".net.ts", "node_modules"));
                }
                if (resolved.Length > 0) {
                    return resolved;
                }

                current = current.Parent;
                depth++;
            }

            return string.Empty;
        }

        /// <summary>
        /// Returns the npm platform package that carries the native executable, e.g. <c>typescript-win32-x64</c>.
        /// </summary>
        /// <returns>The platform package name without its <c>@typescript</c> scope.</returns>
        public static string GetPlatformPackageName() {
            return GetPlatformPackageName(RuntimeInformation.OSArchitecture);
        }

        /// <summary>
        /// Returns the native executable file name for the current operating system.
        /// </summary>
        /// <returns>The executable file name.</returns>
        public static string GetExecutableName() {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "tsc.exe" : "tsc";
        }

        /// <summary>
        /// Installs the runtime's npm dependencies once and resolves the executable again.
        /// </summary>
        /// <param name="runtimeDir">The runtime directory holding package.json.</param>
        /// <param name="logger">Optional logger for npm output.</param>
        /// <returns>The executable path, or empty when installation did not provide it.</returns>
        static string InstallAndResolveExecutable(string runtimeDir, Action<string> logger) {
            if (string.IsNullOrWhiteSpace(runtimeDir) || !File.Exists(Path.Combine(runtimeDir, "package.json"))) {
                return string.Empty;
            }

            lock (InstallLock) {
                string executable = ResolveLanguageServerExecutable(runtimeDir);
                if (!string.IsNullOrWhiteSpace(executable)) {
                    return executable;
                }

                logger?.Invoke($"-- TypeScript 7 formatter not installed in '{runtimeDir}'; running npm install");
                TypeScriptRuntimeMetadata.InstallDependencies(new TypeScriptRuntimeMetadataRequest {
                    RuntimeDirectory = runtimeDir,
                    EnsureDependencies = true,
                    ThrowOnError = true,
                    ForwardOutput = true,
                    InstallTimeoutMinutes = 3,
                    Logger = logger
                });
                return ResolveLanguageServerExecutable(runtimeDir);
            }
        }

        /// <summary>
        /// Probes a node_modules folder for the platform package, hoisted or nested under the TypeScript 7 alias.
        /// </summary>
        /// <param name="nodeModules">The node_modules folder.</param>
        /// <returns>The executable path, or empty.</returns>
        static string TryResolveInNodeModules(string nodeModules) {
            if (!Directory.Exists(nodeModules)) {
                return string.Empty;
            }

            List<string> packageNames = new List<string> { GetPlatformPackageName(RuntimeInformation.OSArchitecture) };
            string processPackage = GetPlatformPackageName(RuntimeInformation.ProcessArchitecture);
            if (!packageNames.Contains(processPackage)) {
                packageNames.Add(processPackage);
            }

            string[] scopeFolders = new[] {
                Path.Combine(nodeModules, "@typescript"),
                Path.Combine(nodeModules, TypeScriptRuntimeMetadata.TypeScript7PackageFolder, "node_modules", "@typescript")
            };
            for (int i = 0; i < scopeFolders.Length; i++) {
                for (int j = 0; j < packageNames.Count; j++) {
                    string candidate = Path.Combine(scopeFolders[i], packageNames[j], "lib", GetExecutableName());
                    if (File.Exists(candidate)) {
                        return candidate;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Builds the platform package name for an architecture using npm's platform and arch spellings.
        /// </summary>
        /// <param name="architecture">The architecture to name.</param>
        /// <returns>The platform package name.</returns>
        static string GetPlatformPackageName(Architecture architecture) {
            string platform;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                platform = "win32";
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                platform = "darwin";
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD)) {
                platform = "freebsd";
            } else {
                platform = "linux";
            }

            string arch = architecture switch {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                Architecture.X86 => "ia32",
                _ => architecture.ToString().ToLowerInvariant()
            };
            return $"typescript-{platform}-{arch}";
        }
    }
}
