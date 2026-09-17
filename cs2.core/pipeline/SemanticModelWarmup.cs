using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.core.Pipeline {
    /// <summary>
    /// Resolves a document's Roslyn objects and forces the declaration and body binding the preprocessor will query, so that work happens on a worker thread.
    /// </summary>
    public static class SemanticModelWarmup {
        /// <summary>
        /// Resolves the tree, root and semantic model for one document.
        /// </summary>
        /// <param name="document">Workspace document.</param>
        /// <returns>The prepared document, or null when Roslyn returns no tree, model or compilation-unit root.</returns>
        public static PreparedDocument Prepare(Document document) {
            if (document == null) {
                throw new ArgumentNullException(nameof(document));
            }

            SyntaxTree syntaxTree = AsyncUtil.RunSync(() => document.GetSyntaxTreeAsync());
            if (syntaxTree == null) {
                return null;
            }

            SemanticModel semanticModel = AsyncUtil.RunSync(() => document.GetSemanticModelAsync());
            if (semanticModel == null) {
                return null;
            }

            CompilationUnitSyntax root = AsyncUtil.RunSync(() => syntaxTree.GetRootAsync()) as CompilationUnitSyntax;
            if (root == null) {
                return null;
            }

            return new PreparedDocument(document, syntaxTree, root, semanticModel);
        }

        /// <summary>
        /// Binds every member declaration and each executable body once so later sequential queries hit the model's caches.
        /// </summary>
        /// <param name="prepared">Document to warm.</param>
        /// <remarks>
        /// Warming is an optimization only: it queries nodes the sequential preprocessing walk may never reach, so a Roslyn failure on one of those nodes must not fail a conversion that would otherwise succeed. Each member is therefore warmed independently and any exception it raises is swallowed, leaving that member cold; if the sequential walk does query the same node it raises the same failure there, exactly as it did before warm-up existed.
        /// </remarks>
        public static void Warm(PreparedDocument prepared) {
            if (prepared == null) {
                throw new ArgumentNullException(nameof(prepared));
            }

            SemanticModel semanticModel = prepared.SemanticModel;
            foreach (MemberDeclarationSyntax member in prepared.Root.DescendantNodes().OfType<MemberDeclarationSyntax>()) {
                try {
                    WarmMember(semanticModel, member);
                } catch (Exception) {
                    // Warm-up is best effort; see the remarks on this method.
                }
            }
        }

        /// <summary>
        /// Binds one member declaration and the first expression of its body, if it has one.
        /// </summary>
        /// <param name="semanticModel">Semantic model that caches the bound nodes.</param>
        /// <param name="member">Member declaration to bind.</param>
        static void WarmMember(SemanticModel semanticModel, MemberDeclarationSyntax member) {
            if (member is FieldDeclarationSyntax field) {
                foreach (VariableDeclaratorSyntax declarator in field.Declaration.Variables) {
                    semanticModel.GetDeclaredSymbol(declarator);
                }
            } else if (member is not BaseNamespaceDeclarationSyntax) {
                semanticModel.GetDeclaredSymbol(member);
            }

            SyntaxNode body = GetExecutableBody(member);
            if (body == null) {
                return;
            }

            ExpressionSyntax firstExpression = body.DescendantNodesAndSelf().OfType<ExpressionSyntax>().FirstOrDefault();
            if (firstExpression != null) {
                semanticModel.GetTypeInfo(firstExpression);
            }
        }

        /// <summary>
        /// Returns the block or expression body of an executable member, or null for declarations without code.
        /// </summary>
        /// <param name="member">Member declaration to inspect.</param>
        /// <returns>The body node when present.</returns>
        static SyntaxNode GetExecutableBody(MemberDeclarationSyntax member) {
            if (member is BaseMethodDeclarationSyntax method) {
                return (SyntaxNode)method.Body ?? method.ExpressionBody;
            }
            if (member is PropertyDeclarationSyntax property) {
                return (SyntaxNode)property.ExpressionBody ?? property.AccessorList;
            }
            if (member is IndexerDeclarationSyntax indexer) {
                return (SyntaxNode)indexer.ExpressionBody ?? indexer.AccessorList;
            }

            return null;
        }
    }
}
