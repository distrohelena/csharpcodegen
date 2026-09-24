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
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "CPPPInvokeTestAssembly",
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
