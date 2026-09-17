using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.core.Pipeline {
    /// <summary>
    /// A document whose syntax tree, root and semantic model were resolved on a worker so the sequential preprocessing walk reuses those exact instances.
    /// </summary>
    public sealed class PreparedDocument {
        /// <summary>
        /// Initializes a prepared document from resolved Roslyn objects.
        /// </summary>
        /// <param name="document">Workspace document.</param>
        /// <param name="syntaxTree">Resolved syntax tree.</param>
        /// <param name="root">Resolved compilation unit root.</param>
        /// <param name="semanticModel">Resolved semantic model.</param>
        public PreparedDocument(Document document, SyntaxTree syntaxTree, CompilationUnitSyntax root, SemanticModel semanticModel) {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
            Root = root ?? throw new ArgumentNullException(nameof(root));
            SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        }

        /// <summary>
        /// Gets the workspace document.
        /// </summary>
        public Document Document { get; }

        /// <summary>
        /// Gets the resolved syntax tree.
        /// </summary>
        public SyntaxTree SyntaxTree { get; }

        /// <summary>
        /// Gets the resolved compilation unit root.
        /// </summary>
        public CompilationUnitSyntax Root { get; }

        /// <summary>
        /// Gets the resolved semantic model.
        /// </summary>
        public SemanticModel SemanticModel { get; }
    }
}
