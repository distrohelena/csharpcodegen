using cs2.core;

namespace cs2.cpp {
    public static class CPPVariableType {
        public static string GetTypeScriptType(this ConvertedVariableType varType, ConvertedProgram program) {
            return "";
        }

        public static string ToCPPString(this ConvertedVariableType varType, ConvertedProgram program) {
            string typeName = varType.TypeName;

            return typeName;
        }
    }
}
