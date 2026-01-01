using System.Collections.Generic;

namespace cs2.ts.util {
    /// <summary>
    /// Describes a reflected property entry.
    /// </summary>
    public sealed class PropertyMetadata {
        /// <summary>
        /// Gets or sets the property name.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the property type name.
        /// </summary>
        public string Type { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets whether the property is public.
        /// </summary>
        public bool? IsPublic { get; set; }
        /// <summary>
        /// Gets or sets whether the property is static.
        /// </summary>
        public bool? IsStatic { get; set; }
        /// <summary>
        /// Gets or sets whether the property is readable.
        /// </summary>
        public bool? CanRead { get; set; }
        /// <summary>
        /// Gets or sets whether the property is writable.
        /// </summary>
        public bool? CanWrite { get; set; }
        /// <summary>
        /// Gets or sets attribute metadata for the property.
        /// </summary>
        public List<AttributeDataMetadata> Attributes { get; set; }
    }
}
