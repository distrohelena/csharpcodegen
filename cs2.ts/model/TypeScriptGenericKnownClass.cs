namespace cs2.ts {
    /// <summary>
    /// Known class that requires importing multiple generic arities (e.g., Action0..Action16).
    /// </summary>
    public class TypeScriptGenericKnownClass : TypeScriptKnownClass {
        /// <summary>
        /// Initializes a generic known-class descriptor that spans multiple arity imports.
        /// </summary>
        /// <param name="total">The total number of generic arities to import.</param>
        /// <param name="start">The first arity that includes a numeric suffix.</param>
        /// <param name="voidReturn">Whether void-return variants append arity suffixes.</param>
        /// <param name="name">The C# type name to map.</param>
        /// <param name="path">The module path that provides the runtime symbol.</param>
        /// <param name="replacement">Optional replacement import identifier.</param>
        public TypeScriptGenericKnownClass(int total, int start, bool voidReturn, string name, string path, string replacement = "")
            : base(name, path, replacement, false, true) {
            TotalImports = total;
            Start = start;
            VoidReturn = voidReturn;
        }

        /// <summary>
        /// Gets or sets the first arity that includes a numeric suffix.
        /// </summary>
        public int Start { get; set; }
        /// <summary>
        /// Gets or sets the total number of imports generated for this type.
        /// </summary>
        public int TotalImports { get; set; }
        /// <summary>
        /// Gets or sets whether the void-return variant uses arity suffixes.
        /// </summary>
        public bool VoidReturn { get; set; }
    }
}
