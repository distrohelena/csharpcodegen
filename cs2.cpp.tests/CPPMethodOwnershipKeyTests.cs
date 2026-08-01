using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies stable method identities used to propagate ownership summaries across compilation boundaries.
/// </summary>
public sealed class CPPMethodOwnershipKeyTests {
    /// <summary>
    /// Ensures overload parameter types participate in ownership method identities.
    /// </summary>
    [Fact]
    public void Create_WithOverloads_ProducesDistinctKeys() {
        CSharpCompilation compilation = CreateKeyCompilation();
        IMethodSymbol[] methods = ResolveMethods(compilation, "Run");

        string integerKey = CPPMethodOwnershipKey.Create(methods.Single(method => method.Parameters[0].Type.SpecialType == SpecialType.System_Int32));
        string stringKey = CPPMethodOwnershipKey.Create(methods.Single(method => method.Parameters[0].Type.SpecialType == SpecialType.System_String));

        Assert.NotEqual(integerKey, stringKey);
        Assert.StartsWith("OwnershipKeys|", integerKey, StringComparison.Ordinal);
        Assert.StartsWith("OwnershipKeys|", stringKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures constructed generic methods resolve to the same ownership identity as their original definition.
    /// </summary>
    [Fact]
    public void Create_WithConstructedGenericMethod_UsesOriginalDefinition() {
        CSharpCompilation compilation = CreateKeyCompilation();
        IMethodSymbol genericDefinition = ResolveMethods(compilation, "Echo").Single();
        IMethodSymbol constructedMethod = genericDefinition.Construct(compilation.GetSpecialType(SpecialType.System_Int32));

        Assert.Equal(CPPMethodOwnershipKey.Create(genericDefinition), CPPMethodOwnershipKey.Create(constructedMethod));
    }

    /// <summary>
    /// Ensures source and metadata views of the same method resolve to one cross-project ownership identity.
    /// </summary>
    [Fact]
    public void Create_WithSourceAndMetadataSymbols_ProducesSameKey() {
        CSharpCompilation sourceCompilation = OwnershipRoslynTestHelper.CreateCompilation("""
            public sealed class SharedFactory {
                public object Create(int count) {
                    return null;
                }
            }
            """, assemblyName: "OwnershipMetadata");
        IMethodSymbol sourceMethod = ResolveMethods(sourceCompilation, "Create").Single();
        using MemoryStream assemblyStream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = sourceCompilation.Emit(assemblyStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        MetadataReference sourceReference = MetadataReference.CreateFromImage(assemblyStream.ToArray());
        CSharpCompilation consumerCompilation = CSharpCompilation.Create(
            "OwnershipConsumer",
            references: sourceCompilation.References.Append(sourceReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        INamedTypeSymbol metadataType = consumerCompilation.GetTypeByMetadataName("SharedFactory")
            ?? throw new InvalidOperationException("Metadata fixture type did not resolve.");
        IMethodSymbol metadataMethod = metadataType.GetMembers("Create").OfType<IMethodSymbol>().Single();

        Assert.Equal(CPPMethodOwnershipKey.Create(sourceMethod), CPPMethodOwnershipKey.Create(metadataMethod));
    }

    /// <summary>
    /// Ensures function-pointer signatures receive stable intrinsic identities even though Roslyn gives them no containing assembly.
    /// </summary>
    [Fact]
    public void Create_WithFunctionPointerSignature_UsesIntrinsicIdentity() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            public unsafe sealed class Fixture {
                public void Run(delegate*<void*, int, void> callback) {
                }
            }
            """, assemblyName: "FunctionPointerKeys");
        IMethodSymbol method = ResolveMethods(compilation, "Run").Single();
        IFunctionPointerTypeSymbol functionPointer = Assert.IsAssignableFrom<IFunctionPointerTypeSymbol>(method.Parameters[0].Type);

        string firstKey = CPPMethodOwnershipKey.Create(functionPointer.Signature);
        string secondKey = CPPMethodOwnershipKey.Create(functionPointer.Signature);

        Assert.Equal(firstKey, secondKey);
        Assert.StartsWith("<function-pointer>|", firstKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates source containing overloaded and generic methods for stable identity checks.
    /// </summary>
    /// <returns>The semantic compilation used by the key tests.</returns>
    static CSharpCompilation CreateKeyCompilation() {
        return OwnershipRoslynTestHelper.CreateCompilation("""
            public sealed class Fixture {
                public void Run(int value) {
                }

                public void Run(string value) {
                }

                public T Echo<T>(T value) {
                    return value;
                }
            }
            """, assemblyName: "OwnershipKeys");
    }

    /// <summary>
    /// Resolves every source method with one requested name.
    /// </summary>
    /// <param name="compilation">Compilation that owns the method declarations.</param>
    /// <param name="methodName">Method identifier to select.</param>
    /// <returns>Resolved method symbols in source order.</returns>
    static IMethodSymbol[] ResolveMethods(CSharpCompilation compilation, string methodName) {
        SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();
        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
        return syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(declaration => string.Equals(declaration.Identifier.Text, methodName, StringComparison.Ordinal))
            .Select(declaration => semanticModel.GetDeclaredSymbol(declaration)
                ?? throw new InvalidOperationException($"Method '{methodName}' did not resolve."))
            .ToArray();
    }
}
