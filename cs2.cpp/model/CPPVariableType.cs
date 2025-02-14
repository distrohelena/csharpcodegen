using cs2.core;

namespace cs2.cpp {
    public static class CPPVariableType {
        public static string GetTypeScriptType(this VariableType varType, ConversionProgram program) {
            return "";
        }

        public static string ToCPPString(this VariableType varType, ConversionProgram program) {
            string typeName = varType.TypeName;

            return typeName;
        }
    }
}
