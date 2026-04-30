using System.IO;
using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies deployment-root layout derivation for target-scoped generated builds.
/// </summary>
public class CPPDeploymentBuildLayoutTests {
    /// <summary>
    /// Ensures the generator derives generated-source, intermediate, and shared build roots from the deployment root and target id.
    /// </summary>
    [Fact]
    public void Create_WhenDeploymentRootAndTargetIdProvided_DerivesGeneratedSourceIntermediateAndBuildPaths() {
        CPPConversionOptions options = new CPPConversionOptions {
            DeploymentRoot = @"C:\Builds\MyGame",
            TargetId = "windows-directx"
        };

        CPPDeploymentBuildLayout layout = CPPDeploymentBuildLayoutFactory.Create(options);

        Assert.Equal(Path.Combine(options.DeploymentRoot, "GeneratedSource", "windows-directx"), layout.GeneratedSourceRoot);
        Assert.Equal(Path.Combine(options.DeploymentRoot, "Intermediate", "windows-directx"), layout.IntermediateRoot);
        Assert.Equal(Path.Combine(options.DeploymentRoot, "Build"), layout.BuildRoot);
    }
}
