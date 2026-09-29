using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Globalization;

namespace cs2.ts {
    /// <summary>Allocates reproducible temporary bindings without colliding with identifiers from the source compilation.</summary>
    public sealed class TypeScriptTemporaryNameAllocator {
        /// <summary>Names already declared in each compilation, including other files of partial types.</summary>
        readonly Dictionary<Compilation, HashSet<string>> SourceIdentifiers = new();
        /// <summary>Monotonic suffix shared by every allocation in this conversion instance.</summary>
        ulong NextIdentifier;

        /// <summary>Allocates one unique generated identifier, skipping all conflicting source identifiers.</summary>
        /// <param name="semantic">Compilation whose source names must remain unshadowed.</param>
        /// <param name="prefix">Generator-owned identifier prefix describing the temporary's role.</param>
        /// <returns>A stable name for the same conversion inputs and traversal order.</returns>
        public string Allocate(SemanticModel semantic, string prefix) {
            if (semantic == null) {
                throw new ArgumentNullException(nameof(semantic));
            } else if (string.IsNullOrWhiteSpace(prefix)) {
                throw new ArgumentException("A generated identifier prefix is required.", nameof(prefix));
            }
            if (!SourceIdentifiers.TryGetValue(semantic.Compilation, out HashSet<string> identifiers)) {
                identifiers = new HashSet<string>(StringComparer.Ordinal);
                foreach (SyntaxTree tree in semantic.Compilation.SyntaxTrees) {
                    foreach (SyntaxToken token in tree.GetRoot().DescendantTokens()) {
                        if (token.IsKind(SyntaxKind.IdentifierToken)) {
                            identifiers.Add(token.ValueText);
                        }
                    }
                }
                SourceIdentifiers.Add(semantic.Compilation, identifiers);
            }
            string name;
            do {
                NextIdentifier = checked(NextIdentifier + 1);
                name = prefix + NextIdentifier.ToString(CultureInfo.InvariantCulture);
            } while (identifiers.Contains(name));
            return name;
        }
    }
}
