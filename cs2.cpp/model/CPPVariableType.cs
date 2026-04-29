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

            string typeName = ResolveCppTypeName(varType);

            if (string.IsNullOrWhiteSpace(typeName)) {
                typeName = "object";
            }

            if (varType.GenericArgs.Count == 0) {
                return typeName;
            }

            string genericArguments = string.Join(", ", varType.GenericArgs.Select(argument => argument.ToCPPString(program)));
            return $"{typeName}<{genericArguments}>";
        }

        /// <summary>
        /// Resolves the emitted C++ type token for a single abstract variable type without applying pointer decoration.
        /// </summary>
        /// <param name="varType">The source variable type.</param>
        /// <returns>The normalized C++ type token.</returns>
        static string ResolveCppTypeName(VariableType varType) {
            if (varType.Type == VariableDataType.Single) {
                return "float";
            }

            if (varType.Type == VariableDataType.Double) {
                return "double";
            }

            if (varType.Type == VariableDataType.UInt32) {
                return "uint32_t";
            }

            if (varType.Type == VariableDataType.Int32) {
                return "int32_t";
            }

            if (varType.Type == VariableDataType.UInt64) {
                return "uint64_t";
            }

            if (varType.Type == VariableDataType.Int64) {
                return "int64_t";
            }

            if (varType.Type == VariableDataType.Int8) {
                return "int8_t";
            }

            if (varType.Type == VariableDataType.UInt8) {
                return "uint8_t";
            }

            if (varType.Type == VariableDataType.Int16) {
                return "int16_t";
            }

            if (varType.Type == VariableDataType.UInt16) {
                return "uint16_t";
            }

            if (varType.Type == VariableDataType.Boolean) {
                return "bool";
            }

            if (varType.Type == VariableDataType.Char) {
                return "char";
            }

            if (varType.Type == VariableDataType.String) {
                return "std::string";
            }

            if (varType.Type == VariableDataType.List) {
                return "List";
            }

            if (varType.Type == VariableDataType.Dictionary) {
                return "Dictionary";
            }

            string typeName = varType.TypeName;

            if (string.IsNullOrWhiteSpace(typeName) && varType.Type == VariableDataType.Tuple) {
                typeName = "ValueTuple";
            }

            if (typeName == "string" || typeName == "String") {
                return "std::string";
            }

            if (typeName == "byte" || typeName == "Byte") {
                return "uint8_t";
            }

            if (typeName == "sbyte" || typeName == "SByte") {
                return "int8_t";
            }

            if (typeName == "short" || typeName == "Int16") {
                return "int16_t";
            }

            if (typeName == "ushort" || typeName == "UInt16") {
                return "uint16_t";
            }

            if (typeName == "int" || typeName == "Int32") {
                return "int32_t";
            }

            if (typeName == "uint" || typeName == "UInt32") {
                return "uint32_t";
            }

            if (typeName == "long" || typeName == "Int64") {
                return "int64_t";
            }

            if (typeName == "ulong" || typeName == "UInt64") {
                return "uint64_t";
            }

            if (typeName == "float" || typeName == "Single") {
                return "float";
            }

            if (typeName == "double" || typeName == "Double") {
                return "double";
            }

            if (typeName == "bool" || typeName == "Boolean") {
                return "bool";
            }

            if (typeName == "char" || typeName == "Char") {
                return "char";
            }

            if (typeName == "List") {
                return "List";
            }

            if (typeName == "Dictionary") {
                return "Dictionary";
            }

            return typeName;
        }
    }
}
