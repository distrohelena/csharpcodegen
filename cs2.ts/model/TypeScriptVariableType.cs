using cs2.core;

namespace cs2.ts {
    public static class TypeScriptVariableType {
        public static string GetTypeScriptType(this VariableType varType, TypeScriptProgram program) {
            string typeName = varType.TypeName;
            if (varType.GenericArgs.Count == 0) {
                if (program.TypeMap.TryGetValue(typeName, out var type)) {
                    typeName = type;
                }
            }

            TypeScriptKnownClass known = program.Requirements.Find(c => c.Name == typeName);
            if (known != null) {
                if (known is TypeScriptGenericKnownClass generic) {
                    if (generic.VoidReturn) {
                        typeName += varType.GenericArgs.Count;
                    } else {
                        if (varType.GenericArgs.Count > generic.Start) {
                            typeName += varType.GenericArgs.Count;
                        }
                    }
                }
            }
            return typeName;
        }

        public static string GetTypeScriptTypeNoGeneric(this VariableType varType, TypeScriptProgram program) {
            string typeName = varType.TypeName;
            if (varType.GenericArgs.Count == 0) {
                if (program.TypeMap.TryGetValue(typeName, out var type)) {
                    typeName = type;
                }
            }

            int generic = typeName.IndexOf("<");
            if (generic != -1) {
                typeName = typeName.Substring(0, generic);
            }

            return typeName;
        }

        public static string ToTypeScriptStringNoAsync(this VariableType varType, TypeScriptProgram program) {
            string value = ToTypeScriptString(varType, program);

            if (value.StartsWith("Promise<")) {
                value = value.Remove(0, 8);
                value = value.Remove(value.Length - 1);
            }

            return value;
        }

        public static string ToTypeScriptString(this VariableType varType, TypeScriptProgram program) {
            string typeName = varType.TypeName;
            if (varType.GenericArgs.Count == 0) {
                if (program.TypeMap.TryGetValue(typeName, out var type)) {
                    typeName = type;
                }
            }

            TypeScriptKnownClass known = program.Requirements.Find(c => c.Name == typeName);
            if (known != null) {
                if (known is TypeScriptGenericKnownClass generic) {
                    if (generic.VoidReturn) {
                        typeName += varType.GenericArgs.Count;
                    } else {
                        if (varType.GenericArgs.Count > generic.Start) {
                            typeName += varType.GenericArgs.Count;
                        }
                    }
                }

                if (known.GenericVoid && varType.GenericArgs.Count == 0) {
                    typeName += "<void>";
                }
            }

            string genArgs = "";
            for (int i = 0; i < varType.GenericArgs.Count; i++) {
                VariableType gen = varType.GenericArgs[i];
                if (gen.Type == VariableDataType.UInt8 &&
                    varType.GenericArgs.Count == 1 &&
                    varType.Type == VariableDataType.Array) {
                    return "Uint8Array";
                }

                genArgs += gen.ToTypeScriptString(program);
                if (i != varType.GenericArgs.Count - 1) {
                    genArgs += ", ";
                }
            }

            if (varType.Type == VariableDataType.Object) {
                if (genArgs.Length > 0) {
                    return $"{typeName}<{genArgs}>";
                }
                return $"{typeName}";
            } else if (varType.Type == VariableDataType.Tuple) {
                return $"[{genArgs}]";
            } else {
                if (genArgs.Length > 0) {
                    return $"{typeName}<{genArgs}>";
                }
                return typeName;
            }
        }
    }
}
