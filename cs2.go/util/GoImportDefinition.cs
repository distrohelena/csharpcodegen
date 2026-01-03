namespace cs2.go.util {
    /// <summary>
    /// Represents a Go import entry with an optional alias.
    /// </summary>
    public class GoImportDefinition {
        /// <summary>
        /// Initializes a new import definition.
        /// </summary>
        /// <param name="path">The import path.</param>
        /// <param name="alias">The optional alias.</param>
        public GoImportDefinition(string path, string alias = "") {
            Path = path;
            Alias = alias;
        }

        /// <summary>
        /// Gets the import path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the optional alias for the import.
        /// </summary>
        public string Alias { get; }
    }
}
