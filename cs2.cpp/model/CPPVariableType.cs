using cs2.core;

namespace cs2.cpp {
    public static class CPPVariableType {
        public static string GetTypeScriptType(this ConvertedVariableType varType, ConvertedProgram program) {
            string typeName = varType.TypeName;
            if (varType.GenericArgs.Count == 0) {
                if (program.TypeMap.TryGetValue(typeName, out var type)) {
                    typeName = type;
                }
            }

            KnownClass known = program.Requirements.Find(c => c.Name == typeName);
            if (known != null) {
                if (known is GenericKnownClass generic) {
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

        public static string ToTypeScriptString(this ConvertedVariableType varType, ConvertedProgram program) {
            string typeName = varType.TypeName;
            if (varType.GenericArgs.Count == 0) {
                if (program.TypeMap.TryGetValue(typeName, out var type)) {
                    typeName = type;
                }
            }

            KnownClass known = program.Requirements.Find(c => c.Name == typeName);
            if (known != null) {
                if (known is GenericKnownClass generic) {
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
                ConvertedVariableType gen = varType.GenericArgs[i];
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
