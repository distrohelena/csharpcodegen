using System.Collections.Generic;

namespace cs2.ts.util
{
    /// <summary>
    /// Describes a reflected method entry.
    /// </summary>
    public sealed class MethodMetadata
    {
        /// <summary>
        /// Gets or sets the method name.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets whether the method is public.
        /// </summary>
        public bool? IsPublic { get; set; }
        /// <summary>
        /// Gets or sets whether the method is static.
        /// </summary>
        public bool? IsStatic { get; set; }
        /// <summary>
        /// Gets or sets whether the method is abstract.
        /// </summary>
        public bool? IsAbstract { get; set; }
        /// <summary>
        /// Gets or sets whether the method is virtual.
        /// </summary>
        public bool? IsVirtual { get; set; }
        /// <summary>
        /// Gets or sets the method return type name.
        /// </summary>
        public string ReturnType { get; set; }
        /// <summary>
        /// Gets or sets the reflected parameter metadata.
        /// </summary>
        public ParameterMetadata[] Parameters { get; set; }
        /// <summary>
        /// Gets or sets attribute metadata for the method.
        /// </summary>
        public List<AttributeDataMetadata> Attributes { get; set; }
        /// <summary>
        /// Gets or sets the method signature string used by the runtime.
        /// </summary>
        public string Signature { get; set; }
    }
}
