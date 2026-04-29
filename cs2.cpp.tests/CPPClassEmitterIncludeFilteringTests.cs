using System.IO;
using cs2.core;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that class emission skips invalid include references collected during preprocessing.
/// </summary>
public class CPPClassEmitterIncludeFilteringTests {
    /// <summary>
    /// Ensures raw inference or unknown sentinel tokens do not become generated include directives.
    /// </summary>
    [Fact]
    public void Emit_WithInvalidReferencedClasses_SkipsInvalidIncludes() {
        CPPClassEmitter emitter = new CPPClassEmitter(CppProcessorTestHarness.CreateProcessor(), new CPPProgram(new CPPConversionRules()));
        ConversionClass conversionClass = new ConversionClass {
            Name = "IncludeCarrier",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.ReferencedClasses.Add("var");
        conversionClass.ReferencedClasses.Add("?");

        using StringWriter headerWriter = new StringWriter();
        using StringWriter sourceWriter = new StringWriter();

        emitter.Emit(conversionClass, headerWriter, sourceWriter);

        string header = headerWriter.ToString();

        Assert.DoesNotContain("#include \"var.hpp\"", header);
        Assert.DoesNotContain("#include \"?.hpp\"", header);
    }
}
