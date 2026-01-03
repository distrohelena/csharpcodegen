namespace cs2.go.util {
    /// <summary>
    /// Describes a Go type mapping entry and its optional import metadata.
    /// </summary>
    public class GoTypeMapEntry {
        /// <summary>
        /// Initializes a new Go type map entry.
        /// </summary>
        /// <param name="goTypeName">The Go type name to emit.</param>
        /// <param name="importPath">The import path required for the type.</param>
        /// <param name="alias">Optional alias for the import.</param>
        public GoTypeMapEntry(string goTypeName, string importPath = "", string alias = "") {
            GoTypeName = goTypeName;
            ImportPath = importPath;
            Alias = alias;
        }

        /// <summary>
        /// Gets the Go type name to emit.
        /// </summary>
        public string GoTypeName { get; }
        /// <summary>
        /// Gets the import path required for the type.
        /// </summary>
        public string ImportPath { get; }
        /// <summary>
        /// Gets the optional alias for the import.
        /// </summary>
        public string Alias { get; }
    }
}
