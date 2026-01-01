using System.Collections.Generic;

namespace cs2.ts.util {
    /// <summary>
    /// Describes metadata for a reflected type in the TypeScript runtime.
    /// </summary>
    public sealed class TypeMetadata {
        /// <summary>
        /// Gets or sets the simple type name.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the namespace that contains the type.
        /// </summary>
        public string Namespace { get; set; }
        /// <summary>
        /// Gets or sets the fully-qualified type name.
        /// </summary>
        public string FullName { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the assembly name for the type.
        /// </summary>
        public string Assembly { get; set; }
        /// <summary>
        /// Gets or sets the runtime type identifier, when provided.
        /// </summary>
        public string TypeId { get; set; }
        /// <summary>
        /// Gets or sets whether the type is a class.
        /// </summary>
        public bool? IsClass { get; set; }
        /// <summary>
        /// Gets or sets whether the type is an interface.
        /// </summary>
        public bool? IsInterface { get; set; }
        /// <summary>
        /// Gets or sets whether the type is a struct.
        /// </summary>
        public bool? IsStruct { get; set; }
        /// <summary>
        /// Gets or sets whether the type is an enum.
        /// </summary>
        public bool? IsEnum { get; set; }
        /// <summary>
        /// Gets or sets whether the type is an array.
        /// </summary>
        public bool? IsArray { get; set; }
        /// <summary>
        /// Gets or sets whether the type is generic.
        /// </summary>
        public bool? IsGeneric { get; set; }
        /// <summary>
        /// Gets or sets the generic arity when the type is generic.
        /// </summary>
        public int? GenericArity { get; set; }
        /// <summary>
        /// Gets or sets the full name of the base type.
        /// </summary>
        public string BaseType { get; set; }
        /// <summary>
        /// Gets or sets the full names of implemented interfaces.
        /// </summary>
        public string[] Interfaces { get; set; }
        /// <summary>
        /// Gets or sets reflected attribute data for the type.
        /// </summary>
        public List<AttributeDataMetadata> Attributes { get; set; }
        /// <summary>
        /// Gets or sets reflected field metadata entries.
        /// </summary>
        public FieldMetadata[] Fields { get; set; }
        /// <summary>
        /// Gets or sets reflected property metadata entries.
        /// </summary>
        public PropertyMetadata[] Properties { get; set; }
        /// <summary>
        /// Gets or sets reflected method metadata entries.
        /// </summary>
        public MethodMetadata[] Methods { get; set; }
        /// <summary>
        /// Gets or sets reflected constructor metadata entries.
        /// </summary>
        public MethodMetadata[] Constructors { get; set; }
        /// <summary>
        /// Gets or sets enum value metadata entries when the type is an enum.
        /// </summary>
        public EnumValueMetadata[] EnumValues { get; set; }
        /// <summary>
        /// Gets or sets the element type name when the type is an array.
        /// </summary>
        public string ElementType { get; set; }
    }
}
