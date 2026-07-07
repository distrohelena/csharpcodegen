using cs2.core;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies generated C++ file stems remain unique on case-insensitive filesystems when distinct source types only differ by namespace or identifier casing.
/// </summary>
public sealed class CPPVariableTypeFileStemTests {
    /// <summary>
    /// Ensures two reachable generated types named <c>int2</c> and <c>Int2</c> do not crash file-stem resolution and instead receive distinct deterministic stems.
    /// </summary>
    [Fact]
    public void GetEmittedFileStem_WhenReachableTypesOnlyDifferByCase_UsesQualifiedCollisionSafeStem() {
        string source = """
            namespace helengine {
                public struct int2 {
                }
            }

            namespace BepuUtilities {
                public struct Int2 {
                }
            }
            """;

        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        Microsoft.CodeAnalysis.INamedTypeSymbol helengineInt2Symbol = compilation.GetTypeByMetadataName("helengine.int2")
            ?? throw new InvalidOperationException("Expected Roslyn symbol for helengine.int2.");
        Microsoft.CodeAnalysis.INamedTypeSymbol bepuInt2Symbol = compilation.GetTypeByMetadataName("BepuUtilities.Int2")
            ?? throw new InvalidOperationException("Expected Roslyn symbol for BepuUtilities.Int2.");
        ConversionClass helengineInt2 = CreateConversionClass(helengineInt2Symbol, program);
        ConversionClass bepuInt2 = CreateConversionClass(bepuInt2Symbol, program);
        program.Classes.Add(helengineInt2);
        program.Classes.Add(bepuInt2);
        program.SetReachableGeneratedTypes(program.Classes);

        string helengineFileStem = helengineInt2.GetEmittedFileStem(program);
        string bepuFileStem = bepuInt2.GetEmittedFileStem(program);

        Assert.Equal("int2", helengineFileStem);
        Assert.Equal("BepuUtilities_Int2", bepuFileStem);
    }

    /// <summary>
    /// Creates one generated conversion class wrapper for the supplied Roslyn type symbol.
    /// </summary>
    /// <param name="typeSymbol">Roslyn type symbol represented by the generated class.</param>
    /// <param name="program">Conversion program that owns the generated class.</param>
    /// <returns>Configured generated class metadata.</returns>
    static ConversionClass CreateConversionClass(Microsoft.CodeAnalysis.INamedTypeSymbol typeSymbol, CPPProgram program) {
        return new ConversionClass {
            Name = typeSymbol.Name,
            Program = program ?? throw new ArgumentNullException(nameof(program)),
            TypeSymbol = typeSymbol,
            DeclarationType = MemberDeclarationType.Class,
            IsValueType = true
        };
    }
}
