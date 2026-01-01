namespace cs2.ts {
    /// <summary>
    /// Defines a runtime requirement entry used to load TypeScript symbols.
    /// </summary>
    public class TypeScriptRuntimeRequirementDefinition {
        /// <summary>
        /// Initializes a standard requirement definition.
        /// </summary>
        /// <param name="name">The C# type name to map.</param>
        /// <param name="path">The module path that provides the runtime symbol.</param>
        /// <param name="replacement">Optional replacement import identifier.</param>
        /// <param name="genericVoid">Whether generic arguments default to void when missing.</param>
        /// <param name="isType">Whether the import is type-only.</param>
        public TypeScriptRuntimeRequirementDefinition(string name, string path, string replacement = "", bool genericVoid = false, bool isType = false) {
            Name = name;
            Path = path;
            Replacement = replacement;
            GenericVoid = genericVoid;
            IsType = isType;
            IsGeneric = false;
        }

        /// <summary>
        /// Creates a generic requirement definition that expands to multiple arities.
        /// </summary>
        /// <param name="totalImports">The total number of generic arities to import.</param>
        /// <param name="start">The first arity that uses a numeric suffix.</param>
        /// <param name="voidReturn">Whether the void-return variant uses arity suffixes.</param>
        /// <param name="name">The C# type name to map.</param>
        /// <param name="path">The module path that provides the runtime symbol.</param>
        /// <param name="replacement">Optional replacement import identifier.</param>
        /// <returns>The generic requirement definition.</returns>
        public static TypeScriptRuntimeRequirementDefinition CreateGeneric(int totalImports, int start, bool voidReturn, string name, string path, string replacement = "") {
            return new TypeScriptRuntimeRequirementDefinition(name, path, replacement, totalImports, start, voidReturn);
        }

        /// <summary>
        /// Gets the C# type name mapped to the runtime symbol.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the module path that provides the runtime symbol.
        /// </summary>
        public string Path { get; private set; }

        /// <summary>
        /// Gets the replacement import identifier, if a rename is required.
        /// </summary>
        public string Replacement { get; private set; }

        /// <summary>
        /// Gets whether generic arguments default to void when unspecified.
        /// </summary>
        public bool GenericVoid { get; private set; }

        /// <summary>
        /// Gets whether the import is type-only.
        /// </summary>
        public bool IsType { get; private set; }

        /// <summary>
        /// Gets the total number of generic imports when the requirement is generic.
        /// </summary>
        public int GenericTotalImports { get; private set; }

        /// <summary>
        /// Gets the first arity that includes a numeric suffix when generic.
        /// </summary>
        public int GenericStart { get; private set; }

        /// <summary>
        /// Gets whether the void-return variant appends arity suffixes.
        /// </summary>
        public bool GenericVoidReturn { get; private set; }

        /// <summary>
        /// Gets whether this definition represents a generic requirement.
        /// </summary>
        public bool IsGeneric { get; private set; }

        /// <summary>
        /// Creates the runtime known-class instance from this definition.
        /// </summary>
        /// <returns>The instantiated known class descriptor.</returns>
        public TypeScriptKnownClass CreateKnownClass() {
            if (IsGeneric) {
                return new TypeScriptGenericKnownClass(GenericTotalImports, GenericStart, GenericVoidReturn, Name, Path, Replacement);
            }

            return new TypeScriptKnownClass(Name, Path, Replacement, GenericVoid, IsType);
        }

        /// <summary>
        /// Initializes a generic requirement definition.
        /// </summary>
        /// <param name="name">The C# type name to map.</param>
        /// <param name="path">The module path that provides the runtime symbol.</param>
        /// <param name="replacement">Optional replacement import identifier.</param>
        /// <param name="totalImports">The total number of generic arities to import.</param>
        /// <param name="start">The first arity that uses a numeric suffix.</param>
        /// <param name="voidReturn">Whether the void-return variant uses arity suffixes.</param>
        TypeScriptRuntimeRequirementDefinition(string name, string path, string replacement, int totalImports, int start, bool voidReturn) {
            Name = name;
            Path = path;
            Replacement = replacement;
            GenericVoid = false;
            IsType = true;
            GenericTotalImports = totalImports;
            GenericStart = start;
            GenericVoidReturn = voidReturn;
            IsGeneric = true;
        }
    }
}
