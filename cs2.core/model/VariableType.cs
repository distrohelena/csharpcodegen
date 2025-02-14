namespace cs2.core {
    public class VariableType {
        public VariableDataType Type { get; set; }
        public string TypeName { get; set; }
        public List<VariableType> Args { get; set; }
        public List<VariableType> GenericArgs { get; set; }
        public bool IsNullable { get; set; }

        public VariableType(
            VariableDataType type = VariableDataType.Unknown,
            string typeName = null,
            List<VariableType>? args = null,
            List<VariableType>? genericArgs = null
        ) {
            Type = type;
            TypeName = typeName;

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

        public override string ToString() {
            string genArgs = "";
            for (int i = 0; i < GenericArgs.Count; i++) {
                VariableType gen = GenericArgs[i];
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
