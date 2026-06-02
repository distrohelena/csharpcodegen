namespace cs2.core {
    public class VariableType {
        /// <summary>
        /// Gets or sets the abstract source-side variable category.
        /// </summary>
        public VariableDataType Type { get; set; }

        /// <summary>
        /// Gets or sets the source type name associated with this variable shape.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Gets or sets the fully qualified source type name when Roslyn metadata can provide one.
        /// </summary>
        public string QualifiedTypeName { get; set; }

        /// <summary>
        /// Gets or sets tuple element labels or auxiliary type arguments associated with this variable shape.
        /// </summary>
        public List<VariableType> Args { get; set; }

        /// <summary>
        /// Gets or sets generic type arguments associated with this variable shape.
        /// </summary>
        public List<VariableType> GenericArgs { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the source type is a nullable value wrapper.
        /// </summary>
        public bool IsNullable { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the source type is an enum.
        /// </summary>
        public bool IsEnum { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the source type is a generic type parameter.
        /// </summary>
        public bool IsGenericParameter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the source type has value semantics in Roslyn metadata.
        /// </summary>
        public bool IsValueType { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the source type is one unsafe pointer indirection over the declared element type.
        /// </summary>
        public bool IsPointer { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the source type is passed or returned by mutable reference.
        /// </summary>
        public bool IsReference { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the source type is passed or returned by readonly reference.
        /// </summary>
        public bool IsConstReference { get; set; }

        /// <summary>
        /// Creates a new variable type descriptor.
        /// </summary>
        /// <param name="type">Abstract source-side variable category.</param>
        /// <param name="typeName">Source type name associated with this variable shape.</param>
        /// <param name="args">Tuple element labels or auxiliary type arguments.</param>
        /// <param name="genericArgs">Generic type arguments associated with this variable shape.</param>
        public VariableType(
            VariableDataType type = VariableDataType.Unknown,
            string typeName = null,
            List<VariableType>? args = null,
            List<VariableType>? genericArgs = null
        ) {
            Type = type;
            TypeName = typeName;
            QualifiedTypeName = typeName;

            if (args == null) {
                Args = new List<VariableType>();
            } else {
                Args = args;
            }

            if (genericArgs == null) {
                GenericArgs = new List<VariableType>();
            } else {
                GenericArgs = genericArgs;
            }
        }

        /// <summary>
        /// Clones a variable type descriptor, preserving semantic flags and nested type metadata.
        /// </summary>
        /// <param name="source">Source variable type descriptor to clone.</param>
        public VariableType(VariableType source) {
            Type = source.Type;
            TypeName = source.TypeName;
            QualifiedTypeName = source.QualifiedTypeName;
            Args = source.Args.ToList();
            GenericArgs = source.GenericArgs.ToList();
            IsNullable = source.IsNullable;
            IsEnum = source.IsEnum;
            IsGenericParameter = source.IsGenericParameter;
            IsValueType = source.IsValueType;
            IsPointer = source.IsPointer;
            IsReference = source.IsReference;
            IsConstReference = source.IsConstReference;
        }

        /// <summary>
        /// Formats the variable type for diagnostics and generated metadata output.
        /// </summary>
        /// <returns>The formatted type string.</returns>
        public override string ToString() {
            string genArgs = "";
            for (int i = 0; i < GenericArgs.Count; i++) {
                VariableType gen = GenericArgs[i];
                genArgs += gen.ToString();
                if (i != GenericArgs.Count - 1) {
                    genArgs += ", ";
                }
            }

            string renderedType;
            if (Type == VariableDataType.Object) {
                if (genArgs.Length > 0) {
                    renderedType = $"{TypeName}<{genArgs}>";
                } else {
                    renderedType = $"{TypeName}";
                }
            } else if (Type == VariableDataType.Tuple) {
                renderedType = $"[{genArgs}]";
            } else {
                if (genArgs.Length > 0) {
                    renderedType = $"{TypeName}<{genArgs}>";
                } else {
                    renderedType = TypeName;
                }
            }

            if (IsConstReference) {
                return $"const {renderedType}&";
            }

            if (IsReference) {
                return $"{renderedType}&";
            }

            return renderedType;
        }
    }
}
