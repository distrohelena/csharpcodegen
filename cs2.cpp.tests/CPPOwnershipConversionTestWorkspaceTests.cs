using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies ownership conversion fixtures release their repository-owned scratch directories.
/// </summary>
public sealed class CPPOwnershipConversionTestWorkspaceTests {
    /// <summary>
    /// Ensures disposing a completed conversion removes all generated fixture artifacts.
    /// </summary>
    [Fact]
    public void Dispose_WithCompletedConversion_RemovesWorkspaceDirectory() {
        CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(Dispose_WithCompletedConversion_RemovesWorkspaceDirectory),
            """
            public sealed class Fixture {
            }
            """);
        string workspacePath = output.WorkspacePath;

        Assert.True(Directory.Exists(workspacePath));

        output.Dispose();

        Assert.False(Directory.Exists(workspacePath));
    }
}
