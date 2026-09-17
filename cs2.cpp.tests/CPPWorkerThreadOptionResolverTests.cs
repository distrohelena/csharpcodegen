using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies how the worker thread count is resolved from generic platform options.
/// </summary>
public sealed class CPPWorkerThreadOptionResolverTests {
    /// <summary>
    /// Builds options carrying one raw worker-thread value.
    /// </summary>
    static CPPConversionOptions CreateOptions(string rawValue) {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        if (rawValue != null) {
            options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                { CPPCodegenOptionNames.WorkerThreads, rawValue }
            };
        }

        return options;
    }

    /// <summary>
    /// Absent option means one worker per logical processor.
    /// </summary>
    [Fact]
    public void Resolve_WithoutOption_UsesProcessorCount() {
        Assert.Equal(Environment.ProcessorCount, CPPWorkerThreadOptionResolver.Resolve(CreateOptions(null)));
    }

    /// <summary>
    /// Explicit positive values are honored verbatim.
    /// </summary>
    [Theory]
    [InlineData("1", 1)]
    [InlineData("4", 4)]
    [InlineData(" 12 ", 12)]
    public void Resolve_WithPositiveValue_ReturnsValue(string rawValue, int expected) {
        Assert.Equal(expected, CPPWorkerThreadOptionResolver.Resolve(CreateOptions(rawValue)));
    }

    /// <summary>
    /// Zero, negatives and non-numeric text are rejected instead of silently defaulting.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("many")]
    [InlineData("")]
    public void Resolve_WithInvalidValue_Throws(string rawValue) {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => CPPWorkerThreadOptionResolver.Resolve(CreateOptions(rawValue)));
        Assert.Contains("codegen-worker-threads", failure.Message);
    }

    /// <summary>
    /// Null options are rejected.
    /// </summary>
    [Fact]
    public void Resolve_WithNullOptions_Throws() {
        Assert.Throws<ArgumentNullException>(() => CPPWorkerThreadOptionResolver.Resolve(null));
    }
}
