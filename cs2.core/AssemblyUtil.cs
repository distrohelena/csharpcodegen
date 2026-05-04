using System.Reflection;

namespace cs2.core {
    /// <summary>
    /// Resolves assembly-related paths used by converter runtime assets.
    /// </summary>
    public static class AssemblyUtil {
        /// <summary>
        /// Environment variable that can pin the runtime asset root for CLI-driven code generation.
        /// </summary>
        const string RuntimeRootEnvironmentVariable = "CS2_RUNTIME_ROOT";

        /// <summary>
        /// Gets the folder that contains the current entry assembly, or the base directory when no entry assembly is available.
        /// </summary>
        /// <returns>The startup folder used to locate converter runtime assets.</returns>
        public static string GetStartFolder() {
            string? configuredRuntimeRoot = Environment.GetEnvironmentVariable(RuntimeRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredRuntimeRoot)) {
                string normalizedRuntimeRoot = Path.GetFullPath(configuredRuntimeRoot);
                string? configuredStartFolder = ResolveRuntimeStartFolder(normalizedRuntimeRoot);
                if (!string.IsNullOrWhiteSpace(configuredStartFolder)) {
                    return configuredStartFolder;
                }
            }

            string? entryAssemblyLocation = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(entryAssemblyLocation)) {
                string? startFolder = FindRuntimeStartFolder(Path.GetDirectoryName(entryAssemblyLocation)!);
                if (!string.IsNullOrWhiteSpace(startFolder)) {
                    return startFolder;
                }
            }

            if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory)) {
                string? startFolder = FindRuntimeStartFolder(AppContext.BaseDirectory);
                if (!string.IsNullOrWhiteSpace(startFolder)) {
                    return startFolder;
                }
            }

            return AppContext.BaseDirectory;
        }

        /// <summary>
        /// Resolves the actual startup folder from one configured runtime root path.
        /// </summary>
        /// <param name="runtimeRoot">Configured runtime root path.</param>
        /// <returns>The folder that should contain the runtime templates.</returns>
        static string? ResolveRuntimeStartFolder(string runtimeRoot) {
            if (string.IsNullOrWhiteSpace(runtimeRoot)) {
                return null;
            }

            if (HasRuntimeTemplateContent(runtimeRoot)) {
                return Path.GetDirectoryName(runtimeRoot);
            }

            string directRuntimeFolder = Path.Combine(runtimeRoot, ".net.cpp");
            if (HasRuntimeTemplateContent(directRuntimeFolder)) {
                return runtimeRoot;
            }

            string siblingRuntimeFolder = Path.Combine(runtimeRoot, "cs2.cpp", ".net.cpp");
            if (HasRuntimeTemplateContent(siblingRuntimeFolder)) {
                return Path.Combine(runtimeRoot, "cs2.cpp");
            }

            return null;
        }

        /// <summary>
        /// Walks upward from one search root until a runtime template start folder is found.
        /// </summary>
        /// <param name="searchRoot">Starting directory used to probe for the runtime template folder.</param>
        /// <returns>The resolved runtime start folder when found; otherwise <c>null</c>.</returns>
        static string? FindRuntimeStartFolder(string searchRoot) {
            if (string.IsNullOrWhiteSpace(searchRoot)) {
                return null;
            }

            for (DirectoryInfo currentDirectory = new DirectoryInfo(searchRoot);
                currentDirectory != null;
                currentDirectory = currentDirectory.Parent) {
                string? resolvedStartFolder = ResolveRuntimeStartFolder(currentDirectory.FullName);
                if (!string.IsNullOrWhiteSpace(resolvedStartFolder)) {
                    return resolvedStartFolder;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether one runtime template directory contains the expected seed files.
        /// </summary>
        /// <param name="candidateRoot">Directory to inspect.</param>
        /// <returns><c>true</c> when the directory contains the runtime template sentinels; otherwise <c>false</c>.</returns>
        static bool HasRuntimeTemplateContent(string candidateRoot) {
            return Directory.Exists(candidateRoot) &&
                File.Exists(Path.Combine(candidateRoot, "system", "console.cpp")) &&
                File.Exists(Path.Combine(candidateRoot, "runtime", "native_string.hpp"));
        }
    }
}
