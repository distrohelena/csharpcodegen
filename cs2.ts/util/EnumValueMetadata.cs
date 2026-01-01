namespace cs2.ts.util {
    /// <summary>
    /// Describes a reflected enum value entry.
    /// </summary>
    public sealed class EnumValueMetadata {
        /// <summary>
        /// Gets or sets the enum member name.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the enum member value.
        /// </summary>
        public object Value { get; set; }
    }
}
