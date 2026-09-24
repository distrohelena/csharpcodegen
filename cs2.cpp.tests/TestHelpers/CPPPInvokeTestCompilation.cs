using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Builds small in-memory Roslyn compilations for P/Invoke lowering tests, referencing the full trusted platform
/// assembly set so fixtures can freely use BCL types such as <c>System.Runtime.InteropServices</c>.
/// </summary>
public static class CPPPInvokeTestCompilation {
    /// <summary>
    /// Compiles the given source into an unsafe-enabled, fully referenced compilation and asserts it is error-free.
    /// </summary>
    /// <param name="source">C# source text to compile.</param>
    /// <returns>The resulting compilation.</returns>
    /// <exception cref="InvalidOperationException">The compilation produced one or more error diagnostics.</exception>
    public static CSharpCompilation Create(string source) {
        return Create(source, Array.Empty<MetadataReference>());
    }

    /// <summary>
    /// Compiles the given source into an unsafe-enabled, fully referenced compilation that also references the given
    /// extra assemblies, and asserts it is error-free.
    /// </summary>
    /// <param name="source">C# source text to compile.</param>
    /// <param name="additionalReferences">Extra metadata references, for example one built by <see cref="CreateReference(string)"/>.</param>
    /// <returns>The resulting compilation.</returns>
    /// <exception cref="InvalidOperationException">The compilation produced one or more error diagnostics.</exception>
    public static CSharpCompilation Create(string source, IEnumerable<MetadataReference> additionalReferences) {
        return Create("CPPPInvokeTestAssembly", source, additionalReferences);
    }

    /// <summary>
    /// Compiles the given source into a separate assembly and returns it as a metadata reference, so tests can exercise
    /// declarations that reach the analyzer from a referenced binary rather than from source.
    /// </summary>
    /// <param name="source">C# source text of the referenced assembly.</param>
    /// <returns>A metadata reference to the emitted assembly image.</returns>
    /// <exception cref="InvalidOperationException">The source does not compile or cannot be emitted.</exception>
    public static MetadataReference CreateReference(string source) {
        CSharpCompilation compilation = Create("CPPPInvokeTestReference", source, Array.Empty<MetadataReference>());
        using MemoryStream image = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = compilation.Emit(image);
        if (!emitResult.Success) {
            throw new InvalidOperationException("Test reference emission failed: " + string.Join(Environment.NewLine, emitResult.Diagnostics));
        }
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    /// <summary>
    /// Compiles the given source into a named, unsafe-enabled compilation referencing the trusted platform assemblies
    /// plus the given extra references, and asserts it is error-free.
    /// </summary>
    /// <param name="assemblyName">Name of the compiled assembly.</param>
    /// <param name="source">C# source text to compile.</param>
    /// <param name="additionalReferences">Extra metadata references.</param>
    /// <returns>The resulting compilation.</returns>
    /// <exception cref="InvalidOperationException">The compilation produced one or more error diagnostics.</exception>
    static CSharpCompilation Create(string assemblyName, string source, IEnumerable<MetadataReference> additionalReferences) {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Concat(additionalReferences);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        List<Diagnostic> errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0) {
            throw new InvalidOperationException("Test compilation failed: " + string.Join(Environment.NewLine, errors));
        }

        return compilation;
    }

    /// <summary>
    /// Finds a method symbol by its declaring type's full name and its own name.
    /// </summary>
    /// <param name="compilation">Compilation to search.</param>
    /// <param name="typeName">Fully qualified name of the declaring type (for example <c>Native.Probe</c>).</param>
    /// <param name="methodName">Name of the method to find.</param>
    /// <returns>The matching method symbol.</returns>
    /// <exception cref="InvalidOperationException">The type or method could not be found.</exception>
    public static IMethodSymbol GetMethod(CSharpCompilation compilation, string typeName, string methodName) {
        INamedTypeSymbol type = compilation.GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException("Type not found: " + typeName);
        IMethodSymbol method = type.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault()
            ?? throw new InvalidOperationException("Method not found: " + typeName + "." + methodName);
        return method;
    }
}
