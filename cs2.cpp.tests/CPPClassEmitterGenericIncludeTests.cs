using System.IO;
using cs2.core;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that generic type parameters do not become generated include directives.
/// </summary>
public class CPPClassEmitterGenericIncludeTests {
    /// <summary>
    /// Ensures referenced generic placeholders are treated as local type parameters, not external headers.
    /// </summary>
    [Fact]
    public void Emit_WithReferencedGenericArgument_SkipsGenericInclude() {
        CPPClassEmitter emitter = new CPPClassEmitter(CppProcessorTestHarness.CreateProcessor(), new CPPProgram(new CPPConversionRules()));
        ConversionClass conversionClass = new ConversionClass {
            Name = "GenericCarrier",
            DeclarationType = MemberDeclarationType.Class,
            GenericArgs = new List<string> { "TAsset" }
        };

        conversionClass.ReferencedClasses.Add("TAsset");

        using StringWriter headerWriter = new StringWriter();
        using StringWriter sourceWriter = new StringWriter();

        emitter.Emit(conversionClass, headerWriter, sourceWriter);

        string header = headerWriter.ToString();

        Assert.DoesNotContain("#include \"TAsset.hpp\"", header);
    }
}
