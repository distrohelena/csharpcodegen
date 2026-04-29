using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that the C++ processor test harness can construct core backend objects.
/// </summary>
public class CPPConversiorProcessorSmokeTests {
    /// <summary>
    /// Ensures the smoke-test harness can create a processor instance.
    /// </summary>
    [Fact]
    public void CreateProcessor_ReturnsInstance() {
        CPPConversiorProcessor processor = CppProcessorTestHarness.CreateProcessor();

        Assert.NotNull(processor);
    }
}
