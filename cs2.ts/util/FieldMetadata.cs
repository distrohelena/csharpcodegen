using System.Collections.Generic;

namespace cs2.ts.util
{
    /// <summary>
    /// Describes a reflected field entry.
    /// </summary>
    public sealed class FieldMetadata
    {
        /// <summary>
        /// Gets or sets the field name.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the field type name.
        /// </summary>
        public string Type { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets whether the field is public.
        /// </summary>
        public bool? IsPublic { get; set; }
        /// <summary>
        /// Gets or sets whether the field is static.
        /// </summary>
        public bool? IsStatic { get; set; }
        /// <summary>
        /// Gets or sets whether the field is init-only.
        /// </summary>
        public bool? IsInitOnly { get; set; }
        /// <summary>
        /// Gets or sets attribute metadata for the field.
        /// </summary>
        public List<AttributeDataMetadata> Attributes { get; set; }
    }
}
