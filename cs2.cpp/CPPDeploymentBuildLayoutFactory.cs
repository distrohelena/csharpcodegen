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
        public static CPPDeploymentBuildLayout Create(CPPConversionOptions options) {
            ArgumentNullException.ThrowIfNull(options);

            string generatedSourceRoot = Path.Combine(options.DeploymentRoot, "GeneratedSource", options.TargetId);
            string intermediateRoot = Path.Combine(options.DeploymentRoot, "Intermediate", options.TargetId);
            string buildRoot = Path.Combine(options.DeploymentRoot, "Build");

            return new CPPDeploymentBuildLayout(options.DeploymentRoot, generatedSourceRoot, intermediateRoot, buildRoot);
        }
    }
}
