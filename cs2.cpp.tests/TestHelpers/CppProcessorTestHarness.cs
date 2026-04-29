using cs2.cpp;

namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Creates minimal backend fixtures for processor-focused C++ conversion tests.
/// </summary>
public static class CppProcessorTestHarness {
    /// <summary>
    /// Creates a processor instance for tests that exercise syntax lowering directly.
    /// </summary>
    /// <returns>A processor ready for focused syntax conversion tests.</returns>
    public static CPPConversiorProcessor CreateProcessor() {
        return new CPPConversiorProcessor(null);
    }
}
