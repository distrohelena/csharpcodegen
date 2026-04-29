using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Provides C++-specific rendering helpers for abstract conversion variable types.
    /// </summary>
    public static class CPPVariableType {
        /// <summary>
        /// Resolves the referenced class name used by the current C++ backend layer lookup.
        /// </summary>
        /// <param name="varType">The source variable type.</param>
        /// <param name="program">The active conversion program.</param>
        /// <returns>The referenced class name when available.</returns>
        public static string GetTypeScriptType(this VariableType varType, ConversionProgram program) {
            return "";
        }

        /// <summary>
        /// Renders the full emitted C++ type token for a conversion variable type, including generic arguments.
        /// </summary>
        /// <param name="varType">The source variable type.</param>
        /// <param name="program">The active conversion program.</param>
        /// <returns>The formatted C++ type token.</returns>
        public static string ToCPPString(this VariableType varType, ConversionProgram program) {
            if (varType == null) {
                return string.Empty;
            }

            string typeName = varType.TypeName;
            if (string.IsNullOrWhiteSpace(typeName) && varType.Type == VariableDataType.Tuple) {
                typeName = "ValueTuple";
            }

            if (string.IsNullOrWhiteSpace(typeName)) {
                typeName = "object";
            }

            if (varType.GenericArgs.Count == 0) {
                return typeName;
            }

            string genericArguments = string.Join(", ", varType.GenericArgs.Select(argument => argument.ToCPPString(program)));
            return $"{typeName}<{genericArguments}>";
        }
    }
}
