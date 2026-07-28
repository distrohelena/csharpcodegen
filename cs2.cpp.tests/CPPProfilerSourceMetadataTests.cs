using System.Reflection;
using cs2.core;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that generated C++ member models retain the identity of the maintained C# declarations from which they originated.
/// </summary>
public class CPPProfilerSourceMetadataTests {
    /// <summary>
    /// Ensures ordinary methods, constructors, properties, operators, and indexers retain their maintained source locations.
    /// </summary>
    [Fact]
    public void PreProcessExpression_PreservesMaintainedSourceIdentityForGeneratedMembers() {
        ConversionClass conversionClass = ConvertIdentitySubject();

        AssertSourceLocation(
            conversionClass.Functions.Single(function => function.Name == "Sample"),
            "ProfilerSourceIdentityTests",
            "IdentitySubject.Sample(int)",
            3);
        AssertSourceLocation(
            conversionClass.Functions.Single(function => function.IsConstructor),
            "ProfilerSourceIdentityTests",
            "IdentitySubject.IdentitySubject()",
            2);
        AssertSourceLocation(
            conversionClass.Functions.Single(function => function.Name == "operator+"),
            "ProfilerSourceIdentityTests",
            "IdentitySubject.operator +(IdentitySubject, IdentitySubject)",
            6);
        AssertSourceLocation(
            conversionClass.Functions.Single(function => function.Name == "get_Item"),
            "ProfilerSourceIdentityTests",
            "IdentitySubject.this[int]",
            7);
        AssertSourceLocation(
            conversionClass.Functions.Single(function => function.Name == "set_Item"),
            "ProfilerSourceIdentityTests",
            "IdentitySubject.this[int]",
            7);

        AssertSourceLocation(
            conversionClass.Variables.Single(variable => variable.Name == "ExpressionValue"),
            "ProfilerSourceIdentityTests",
            "IdentitySubject.ExpressionValue",
            4);
        AssertSourceLocation(
            conversionClass.Variables.Single(variable => variable.Name == "AutoValue"),
            "ProfilerSourceIdentityTests",
            "IdentitySubject.AutoValue",
            5);
    }

    /// <summary>
    /// Ensures synthesized property accessors inherit their maintained C# property identity without creating a replacement source location.
    /// </summary>
    [Fact]
    public void CreateAccessors_PropertyMetadataFlowsToSynthesizedGetterAndSetter() {
        ConversionClass conversionClass = ConvertIdentitySubject();
        ConversionVariable property = conversionClass.Variables.Single(variable => variable.Name == "AutoValue");
        CPPClassEmitter emitter = new CPPClassEmitter(CppProcessorTestHarness.CreateProcessor(), new CPPProgram(new CPPConversionRules()));

        ConversionFunction getter = CreatePropertyAccessor(emitter, "CreateGetter", property);
        ConversionFunction setter = CreatePropertyAccessor(emitter, "CreateSetter", property);

        Assert.Same(property.SourceLocation, getter.SourceLocation);
        Assert.Same(property.SourceLocation, setter.SourceLocation);
    }

    /// <summary>
    /// Ensures maintained source-location values reject a non-empty source path without a valid one-based line number.
    /// </summary>
    [Fact]
    public void ConversionSourceLocation_RejectsPathWithoutOneBasedLineNumber() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionSourceLocation(
            "ProfilerSourceIdentityTests",
            "IdentitySubject.Sample(int)",
            "C:\\source\\IdentitySubject.cs",
            0));
    }

    /// <summary>
    /// Converts the focused source fixture through the shared preprocessing model.
    /// </summary>
    /// <returns>The converted class containing maintained source metadata.</returns>
    static ConversionClass ConvertIdentitySubject() {
        const string source = """
public class IdentitySubject {
    public IdentitySubject() { }
    public int Sample(int amount) { return amount; }
    public int ExpressionValue => 5;
    public int AutoValue { get; set; }
    public static IdentitySubject operator +(IdentitySubject left, IdentitySubject right) { return left; }
    public int this[int index] { get { return index; } set { } }
}
""";
        string filePath = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", "IdentitySubject.cs");
        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source, "ProfilerSourceIdentityTests", filePath: filePath);
        SemanticModel semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        ClassDeclarationSyntax declaration = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        ConversionProgram program = new ConversionProgram(new CPPConversionRules());
        ConversionContext context = new ConversionContext(program);

        ConversionPreProcessor.PreProcessExpression(semanticModel, context, declaration);

        return Assert.Single(program.Classes);
    }

    /// <summary>
    /// Invokes one property-accessor synthesis method so its copied metadata can be asserted independently of emitted text.
    /// </summary>
    /// <param name="emitter">Emitter that owns the accessor synthesis logic.</param>
    /// <param name="methodName">The private accessor-synthesis method name.</param>
    /// <param name="property">Maintained C# property metadata to lower.</param>
    /// <returns>The synthesized accessor function model.</returns>
    static ConversionFunction CreatePropertyAccessor(CPPClassEmitter emitter, string methodName, ConversionVariable property) {
        MethodInfo method = typeof(CPPClassEmitter).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Unable to find {methodName} on {nameof(CPPClassEmitter)}.");
        return (ConversionFunction)(method.Invoke(emitter, new object[] { property })
            ?? throw new InvalidOperationException($"{methodName} returned no accessor model."));
    }

    /// <summary>
    /// Asserts the stable maintained declaration identity carried by a converted function.
    /// </summary>
    /// <param name="function">Function whose maintained source location is under test.</param>
    /// <param name="assemblyName">Expected compilation assembly name.</param>
    /// <param name="maintainedSymbol">Expected stable C# symbol display.</param>
    /// <param name="lineNumber">Expected one-based declaration line.</param>
    static void AssertSourceLocation(ConversionFunction function, string assemblyName, string maintainedSymbol, int lineNumber) {
        Assert.NotNull(function.SourceLocation);
        Assert.Equal(assemblyName, function.SourceLocation.AssemblyName);
        Assert.Equal(maintainedSymbol, function.SourceLocation.MaintainedSymbol);
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", "IdentitySubject.cs")), function.SourceLocation.FilePath);
        Assert.Equal(lineNumber, function.SourceLocation.LineNumber);
        Assert.Equal(function.SourceMethodKey, function.SourceLocation.MaintainedSymbol);
    }

    /// <summary>
    /// Asserts the stable maintained declaration identity carried by a converted property.
    /// </summary>
    /// <param name="variable">Property whose maintained source location is under test.</param>
    /// <param name="assemblyName">Expected compilation assembly name.</param>
    /// <param name="maintainedSymbol">Expected stable C# symbol display.</param>
    /// <param name="lineNumber">Expected one-based declaration line.</param>
    static void AssertSourceLocation(ConversionVariable variable, string assemblyName, string maintainedSymbol, int lineNumber) {
        Assert.NotNull(variable.SourceLocation);
        Assert.Equal(assemblyName, variable.SourceLocation.AssemblyName);
        Assert.Equal(maintainedSymbol, variable.SourceLocation.MaintainedSymbol);
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", "IdentitySubject.cs")), variable.SourceLocation.FilePath);
        Assert.Equal(lineNumber, variable.SourceLocation.LineNumber);
    }
}
