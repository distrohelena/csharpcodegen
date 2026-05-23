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
        /// <param name="featureCatalog">External feature catalog that defines the type-root detection rules.</param>
        /// <returns>The explicit feature roots detected in the compilation.</returns>
        public static IReadOnlyList<CPPFeatureUsageRoot> Scan(CSharpCompilation compilation, CPPExternalFeatureCatalog featureCatalog) {
            if (compilation == null) {
                throw new ArgumentNullException(nameof(compilation));
            }

            List<CPPFeatureUsageRoot> roots = new List<CPPFeatureUsageRoot>();
            HashSet<string> seenKeys = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<string>> ruleMap = BuildRuleMap(featureCatalog ?? CPPExternalFeatureCatalog.Empty);

            foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
                SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
                SyntaxNode rootNode = syntaxTree.GetRoot();

                foreach (TypeSyntax typeSyntax in rootNode.DescendantNodes().OfType<TypeSyntax>()) {
                    ITypeSymbol typeSymbol = semanticModel.GetTypeInfo(typeSyntax).Type;
                    if (typeSymbol == null) {
                        continue;
                    }

                    string typeName = typeSymbol.ToDisplayString();
                    if (!ruleMap.TryGetValue(typeName, out IReadOnlyList<string> featureIds)) {
                        continue;
                    }

                    foreach (string featureId in featureIds) {
                        string seenKey = featureId + "|" + typeName;
                        if (!seenKeys.Add(seenKey)) {
                            continue;
                        }

                        roots.Add(new CPPFeatureUsageRoot {
                            FeatureId = featureId,
                            RootId = typeName,
                            SourceKind = "TypeReference",
                        });
                    }
                }
            }

            return roots;
        }

        static Dictionary<string, IReadOnlyList<string>> BuildRuleMap(CPPExternalFeatureCatalog featureCatalog) {
            Dictionary<string, IReadOnlyList<string>> ruleMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (CPPExternalFeatureRootRule rootRule in featureCatalog.RootRules) {
                ruleMap[rootRule.TypeName] = rootRule.FeatureIds;
            }

            return ruleMap;
        }
    }
}
