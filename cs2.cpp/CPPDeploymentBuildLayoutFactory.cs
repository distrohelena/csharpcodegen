using System.IO;

namespace cs2.cpp {
    /// <summary>
    /// Creates target-scoped deployment-root output layouts for C++ conversion runs.
    /// </summary>
    public static class CPPDeploymentBuildLayoutFactory {
        /// <summary>
        /// Derives generated-source, intermediate, and shared build roots from the supplied conversion options.
        /// </summary>
        /// <param name="options">The conversion options that contain the deployment-root contract.</param>
        /// <returns>The derived deployment-build layout.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the target would escape either generated-source or intermediate storage.</exception>
        public static CPPDeploymentBuildLayout Create(CPPConversionOptions options) {
            ArgumentNullException.ThrowIfNull(options);

            string deploymentRoot = Path.GetFullPath(options.DeploymentRoot);
            string generatedSourceRoot = ResolveTargetRoot(deploymentRoot, "GeneratedSource", options.TargetId);
            string intermediateRoot = ResolveTargetRoot(deploymentRoot, "Intermediate", options.TargetId);
            string buildRoot = Path.Combine(deploymentRoot, "Build");

            return new CPPDeploymentBuildLayout(deploymentRoot, generatedSourceRoot, intermediateRoot, buildRoot);
        }

        /// <summary>
        /// Resolves a target directory and rejects roots or traversals that would let output cleanup reach outside its storage family.
        /// </summary>
        /// <param name="deploymentRoot">Absolute deployment root that owns all target output.</param>
        /// <param name="folderName">Storage family whose descendants may be owned by this target.</param>
        /// <param name="targetId">Caller-provided target identifier, including valid relative subdirectories.</param>
        /// <returns>The absolute target directory, strictly beneath the selected storage family.</returns>
        static string ResolveTargetRoot(string deploymentRoot, string folderName, string targetId) {
            string storageRoot = Path.Combine(deploymentRoot, folderName);
            string targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(storageRoot, targetId)));
            StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!targetRoot.StartsWith(storageRoot + Path.DirectorySeparatorChar, pathComparison)) {
                throw new ArgumentException($"TargetId must resolve strictly beneath '{storageRoot}'.", nameof(targetId));
            }

            return targetRoot;
        }
    }
}
