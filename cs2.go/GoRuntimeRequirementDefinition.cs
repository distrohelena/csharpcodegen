namespace cs2.go {
    /// <summary>
    /// Defines a runtime requirement entry used to load Go type mappings.
    /// </summary>
    public class GoRuntimeRequirementDefinition {
        /// <summary>
        /// Initializes a standard requirement definition.
        /// </summary>
        /// <param name="name">The .NET type name to map.</param>
        /// <param name="goName">The Go type or helper name to emit.</param>
        /// <param name="importPath">The Go package import path.</param>
        /// <param name="alias">Optional import alias.</param>
        public GoRuntimeRequirementDefinition(string name, string goName, string importPath, string alias = "") {
            Name = name;
            GoName = goName;
            ImportPath = importPath;
            Alias = alias;
        }

        /// <summary>
        /// Gets the .NET type name mapped to Go.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Go type or helper identifier.
        /// </summary>
        public string GoName { get; }

        /// <summary>
        /// Gets the import path that provides the Go identifier.
        /// </summary>
        public string ImportPath { get; }

        /// <summary>
        /// Gets the optional import alias.
        /// </summary>
        public string Alias { get; }

        /// <summary>
        /// Creates the runtime known-class instance from this definition.
        /// </summary>
        /// <returns>The instantiated known class descriptor.</returns>
        public GoKnownClass CreateKnownClass() {
            return new GoKnownClass(Name, GoName, ImportPath, Alias);
        }
    }
}
