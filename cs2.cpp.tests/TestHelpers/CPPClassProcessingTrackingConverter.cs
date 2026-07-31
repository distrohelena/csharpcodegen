using cs2.core;
using cs2.cpp;

namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Records whether the C++ pipeline reached class processing while preserving ordinary converter behavior.
/// </summary>
public sealed class CPPClassProcessingTrackingConverter : CPPCodeConverter {
    /// <summary>
    /// Initializes a tracking converter without external runtime metadata tooling.
    /// </summary>
    public CPPClassProcessingTrackingConverter()
        : base(new CPPConversionRules(), CreateOptions()) {
    }

    /// <summary>
    /// Gets whether at least one source class entered the lowering processor.
    /// </summary>
    public bool ClassProcessingStarted { get; private set; }

    /// <summary>
    /// Records class-processing entry and delegates to the production C++ lowering implementation.
    /// </summary>
    /// <param name="conversionClass">Source class currently entering C++ lowering.</param>
    /// <param name="program">Conversion program that owns the class.</param>
    protected override void ProcessClass(ConversionClass conversionClass, ConversionProgram program) {
        ClassProcessingStarted = true;
        base.ProcessClass(conversionClass, program);
    }

    /// <summary>
    /// Creates focused converter options that avoid loading external native runtime metadata.
    /// </summary>
    /// <returns>Options suitable for pipeline-order tests.</returns>
    static CPPConversionOptions CreateOptions() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        return options;
    }
}
