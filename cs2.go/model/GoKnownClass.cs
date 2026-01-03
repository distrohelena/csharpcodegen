namespace cs2.go {
    /// <summary>
    /// Represents a known Go type or helper mapped from a .NET type name.
    /// </summary>
    public class GoKnownClass {
        /// <summary>
        /// Initializes a new known-class mapping.
        /// </summary>
        /// <param name="name">The .NET type name to map.</param>
        /// <param name="goName">The Go type or helper name to emit.</param>
        /// <param name="importPath">The Go import path required for the type.</param>
        /// <param name="alias">Optional import alias for the package.</param>
        public GoKnownClass(string name, string goName, string importPath = "", string alias = "") {
            Name = name;
            GoName = goName;
            ImportPath = importPath;
            Alias = alias;
        }

        /// <summary>
        /// Gets or sets the .NET type name mapped to Go.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Gets or sets the Go type or helper identifier.
        /// </summary>
        public string GoName { get; set; }
        /// <summary>
        /// Gets or sets the import path that provides the Go identifier.
        /// </summary>
        public string ImportPath { get; set; }
        /// <summary>
        /// Gets or sets the optional import alias for the Go package.
        /// </summary>
        public string Alias { get; set; }

        /// <summary>
        /// Returns a readable label for diagnostics.
        /// </summary>
        /// <returns>The label for the known class.</returns>
        public override string ToString() {
            return $"{Name} -> {GoName}";
        }
    }
}
