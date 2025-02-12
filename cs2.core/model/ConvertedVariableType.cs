namespace cs2.core {
    public class ConvertedVariableType {
        public VariableDataType Type { get; set; }
        public string TypeName { get; set; }
        public List<ConvertedVariableType> Args { get; set; }
        public List<ConvertedVariableType> GenericArgs { get; set; }
        public bool IsNullable { get; set; }

        public ConvertedVariableType(
            VariableDataType type = VariableDataType.Unknown,
            string typeName = null,
            List<ConvertedVariableType>? args = null,
            List<ConvertedVariableType>? genericArgs = null
        ) {
            Type = type;
            TypeName = typeName;

            if (args == null) {
                Args = new List<ConvertedVariableType>();
            } else {
                Args = args;
            }

            if (genericArgs == null) {
                GenericArgs = new List<ConvertedVariableType>();
            } else {
                GenericArgs = genericArgs;
            }
        }

        public override string ToString() {
            string genArgs = "";
            for (int i = 0; i < GenericArgs.Count; i++) {
                ConvertedVariableType gen = GenericArgs[i];
                genArgs += gen.ToString();
                if (i != GenericArgs.Count - 1) {
                    genArgs += ", ";
                }
            }

            if (Type == VariableDataType.Object) {
                if (genArgs.Length > 0) {
                    return $"{TypeName}<{genArgs}>";
                }
                return $"{TypeName}";
            } else if (Type == VariableDataType.Tuple) {
                return $"[{genArgs}]";
            } else {
                if (genArgs.Length > 0) {
                    return $"{TypeName}<{genArgs}>";
                }
                return TypeName;
            }
        }
    }
}
