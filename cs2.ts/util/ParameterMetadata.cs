using System.Collections.Generic;

namespace cs2.ts.util {
    /// <summary>
    /// Describes a reflected parameter entry.
    /// </summary>
    public sealed class ParameterMetadata {
        /// <summary>
        /// Gets or sets the parameter name.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the parameter type name.
        /// </summary>
        public string Type { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets whether the parameter has a default value.
        /// </summary>
        public bool? HasDefault { get; set; }
        /// <summary>
        /// Gets or sets the default value when one is defined.
        /// </summary>
        public object DefaultValue { get; set; }
        /// <summary>
        /// Gets or sets attribute metadata for the parameter.
        /// </summary>
        public List<AttributeDataMetadata> Attributes { get; set; }
    }
}
