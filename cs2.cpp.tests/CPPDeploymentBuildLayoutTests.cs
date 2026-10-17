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

    /// <summary>
    /// Ensures relative target subdirectories remain valid after normalizing parent components within their storage family.
    /// </summary>
    [Fact]
    public void Create_WithNestedRelativeTarget_KeepsBothTargetRootsInsideTheirStorageFamily() {
        string deploymentRoot = Path.Combine(Path.GetTempPath(), "deployment-layout");
        CPPConversionOptions options = new CPPConversionOptions {
            DeploymentRoot = deploymentRoot,
            TargetId = Path.Combine("family", "..", "family", "windows")
        };

        CPPDeploymentBuildLayout layout = CPPDeploymentBuildLayoutFactory.Create(options);

        Assert.Equal(Path.Combine(deploymentRoot, "GeneratedSource", "family", "windows"), layout.GeneratedSourceRoot);
        Assert.Equal(Path.Combine(deploymentRoot, "Intermediate", "family", "windows"), layout.IntermediateRoot);
    }

    /// <summary>
    /// Ensures a target cannot select its storage-family root, a parent directory, or a sibling with a similar prefix.
    /// </summary>
    [Theory]
    [InlineData(".")]
    [InlineData("./")]
    [InlineData("nested/../")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("../GeneratedSource-sibling/child")]
    public void Create_WithTargetEscapingStorageFamily_Throws(string targetId) {
        CPPConversionOptions options = new CPPConversionOptions {
            DeploymentRoot = Path.Combine(Path.GetTempPath(), "deployment-layout"),
            TargetId = targetId
        };

        Assert.Throws<ArgumentException>(() => CPPDeploymentBuildLayoutFactory.Create(options));
    }

    /// <summary>
    /// Ensures an absolute target path outside the deployment storage cannot redirect subsequent output cleanup.
    /// </summary>
    [Fact]
    public void Create_WithAbsoluteTargetOutsideDeployment_Throws() {
        CPPConversionOptions options = new CPPConversionOptions {
            DeploymentRoot = Path.Combine(Path.GetTempPath(), "deployment-layout"),
            TargetId = Path.Combine(Path.GetTempPath(), "outside-deployment")
        };

        Assert.Throws<ArgumentException>(() => CPPDeploymentBuildLayoutFactory.Create(options));
    }
}
