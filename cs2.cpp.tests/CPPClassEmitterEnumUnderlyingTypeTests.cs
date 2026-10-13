using cs2.core;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests;

/// <summary>Verifies that enum declarations preserve C# integer width and signedness for strict native compilers.</summary>
public sealed class CPPClassEmitterEnumUnderlyingTypeTests {
    /// <summary>Checks every legal enum storage type, including constants outside the signed 32-bit range.</summary>
    [Theory]
    [InlineData("byte", "255", "uint8_t")]
    [InlineData("sbyte", "-128", "int8_t")]
    [InlineData("short", "-32768", "int16_t")]
    [InlineData("ushort", "65535", "uint16_t")]
    [InlineData("int", "-2147483648", "int32_t")]
    [InlineData("uint", "0xA0000000", "uint32_t")]
    [InlineData("long", "-9223372036854775808", "int64_t")]
    [InlineData("ulong", "0xffffffffffffffff", "uint64_t")]
    public void EmitPreservesEnumStorage(string managedType, string value, string nativeType) {
        var compilation = RoslynTestHelper.CreateCompilation($"public enum Layout : {managedType} {{ Value = {value} }}");
        var tree = compilation.SyntaxTrees.Single();
        var semantic = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>().Single();
        var model = new ConversionClass {
            Name = "Layout", DeclarationType = MemberDeclarationType.Enum,
            TypeSymbol = semantic.GetDeclaredSymbol(declaration), Semantic = semantic,
            EnumMembers = declaration.Members.Select(member => (object)member.ToString()).ToList()
        };
        var converter = new CPPCodeConverter(new CPPConversionRules(), CPPConversionOptions.CreateDefault());
        var emitter = new CPPClassEmitter(new CPPConversiorProcessor(converter), (CPPProgram)((ICPPConversionHost)converter).Program);
        using var header = new StringWriter();
        using var source = new StringWriter();
        emitter.Emit(model, header, source);
        Assert.Contains($"enum class Layout : {nativeType}", header.ToString());
        Assert.Contains($"Value = {value}", header.ToString());
    }
}
