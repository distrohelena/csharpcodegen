using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Builds Roslyn syntax trees and compilations for focused backend tests.
/// </summary>
public static class RoslynTestHelper {
    /// <summary>
    /// Parses a C# compilation unit from source text.
    /// </summary>
    /// <param name="source">Source text to parse.</param>
    /// <returns>The parsed compilation unit syntax.</returns>
    public static CompilationUnitSyntax ParseCompilationUnit(string source) {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        return (CompilationUnitSyntax)syntaxTree.GetRoot();
    }

    /// <summary>
    /// Creates a Roslyn compilation from source text using the current runtime assemblies.
    /// </summary>
    /// <param name="source">Source text to compile.</param>
    /// <param name="assemblyName">Optional assembly name for the synthetic compilation.</param>
    /// <param name="allowUnsafe">Enables parsing and binding of unsafe code constructs for focused diagnostics tests.</param>
    /// <param name="filePath">Optional file path applied to the generated syntax tree.</param>
    /// <returns>A C# compilation ready for semantic analysis.</returns>
    public static CSharpCompilation CreateCompilation(string source, string assemblyName = "CppBackendTests", bool allowUnsafe = false, string filePath = "") {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, SourceCodeKind.Regular);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, filePath);
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));
    }
}
