using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp {
    /// <summary>
    /// Scans compilations for explicit phase-one feature roots.
    /// </summary>
    public static class CPPFeatureScanner {
        /// <summary>
        /// Scans a compilation and returns the detected feature roots.
        /// </summary>
        /// <param name="compilation">The compilation to inspect.</param>
        /// <returns>The explicit feature roots detected in the compilation.</returns>
        public static IReadOnlyList<CPPFeatureUsageRoot> Scan(CSharpCompilation compilation) {
            List<CPPFeatureUsageRoot> roots = new List<CPPFeatureUsageRoot>();
            HashSet<string> seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
                SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
                SyntaxNode rootNode = syntaxTree.GetRoot();

                foreach (TypeSyntax typeSyntax in rootNode.DescendantNodes().OfType<TypeSyntax>()) {
                    ITypeSymbol typeSymbol = semanticModel.GetTypeInfo(typeSyntax).Type;
                    if (!CPPFeatureCatalog.TryGetFeatures(typeSymbol, out CPPFeatureKind[] features)) {
                        continue;
                    }

                    foreach (CPPFeatureKind feature in features) {
                        string seenKey = feature + "|" + typeSymbol.ToDisplayString();
                        if (!seenKeys.Add(seenKey)) {
                            continue;
                        }

                        roots.Add(new CPPFeatureUsageRoot {
                            Feature = feature,
                            RootId = typeSymbol.ToDisplayString(),
                            SourceKind = "TypeReference",
                        });
                    }
                }
            }

            return roots;
        }
    }
}
