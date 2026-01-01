using System.Collections.Generic;

namespace cs2.ts.util
{
    /// <summary>
    /// Represents reflected attribute metadata for a type or member.
    /// </summary>
    public sealed class AttributeDataMetadata
    {
        /// <summary>
        /// Gets or sets the attribute type name.
        /// </summary>
        public string Type { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the positional constructor arguments.
        /// </summary>
        public List<object> CtorArgs { get; set; }
        /// <summary>
        /// Gets or sets the named constructor arguments.
        /// </summary>
        public Dictionary<string, object> NamedArgs { get; set; }
    }
}
