namespace cs2.cpp {
    /// <summary>
    /// Describes the derived target-scoped output folders owned by a deployment-root build.
    /// </summary>
    public class CPPDeploymentBuildLayout {
        /// <summary>
        /// Initializes a new deployment-build layout.
        /// </summary>
        /// <param name="deploymentRoot">The user-selected deployment-root folder.</param>
        /// <param name="generatedSourceRoot">The target-scoped generated source root.</param>
        /// <param name="intermediateRoot">The target-scoped native intermediate root.</param>
        /// <param name="buildRoot">The shared final build root.</param>
        public CPPDeploymentBuildLayout(string deploymentRoot, string generatedSourceRoot, string intermediateRoot, string buildRoot) {
            DeploymentRoot = deploymentRoot;
            GeneratedSourceRoot = generatedSourceRoot;
            IntermediateRoot = intermediateRoot;
            BuildRoot = buildRoot;
        }

        /// <summary>
        /// Gets the user-selected deployment-root folder.
        /// </summary>
        public string DeploymentRoot { get; }

        /// <summary>
        /// Gets the target-scoped generated source root.
        /// </summary>
        public string GeneratedSourceRoot { get; }

        /// <summary>
        /// Gets the target-scoped native intermediate root.
        /// </summary>
        public string IntermediateRoot { get; }

        /// <summary>
        /// Gets the shared final build root.
        /// </summary>
        public string BuildRoot { get; }
    }
}
