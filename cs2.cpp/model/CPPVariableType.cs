using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Provides C++-specific rendering helpers for abstract conversion variable types.
    /// </summary>
    public static class CPPVariableType {
        /// <summary>
        /// Resolves the stable emitted C++ type name for a converted class, adding an arity suffix for generic declarations.
        /// </summary>
        /// <param name="conversionClass">The converted class whose emitted name is needed.</param>
        /// <returns>The stable emitted C++ type name.</returns>
        public static string GetEmittedTypeName(this ConversionClass conversionClass) {
            if (conversionClass == null) {
                return string.Empty;
            }

            if (conversionClass.GenericArgs == null || conversionClass.GenericArgs.Count == 0) {
                return conversionClass.Name;
            }

            return $"{conversionClass.Name}_{conversionClass.GenericArgs.Count}";
        }

        /// <summary>
        /// Finds a generated converted class by source type name and generic arity so generic and non-generic declarations remain distinct.
        /// </summary>
        /// <param name="program">Program model that contains generated classes.</param>
        /// <param name="typeName">Source type name to resolve.</param>
        /// <param name="genericArgumentCount">Generic arity required by the reference.</param>
        /// <returns>The matching generated class when found; otherwise, <c>null</c>.</returns>
        public static ConversionClass? FindGeneratedClass(this ConversionProgram program, string typeName, int genericArgumentCount) {
            if (program == null || string.IsNullOrWhiteSpace(typeName)) {
                return null;
            }

            string normalizedTypeName = NormalizeLeafTypeName(typeName);
            return program.Classes.FirstOrDefault(candidate =>
                !candidate.IsNative &&
                string.Equals(candidate.Name, normalizedTypeName, StringComparison.Ordinal) &&
                GetGenericArity(candidate) == genericArgumentCount);
        }

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

            string typeName = ResolveCppTypeName(varType, program);

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
        static string ResolveCppTypeName(VariableType varType, ConversionProgram program) {
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

            ConversionClass? generatedClass = program.FindGeneratedClass(typeName, varType.GenericArgs.Count);
            if (generatedClass != null) {
                return generatedClass.GetEmittedTypeName();
            }

            if (!string.IsNullOrWhiteSpace(typeName) && typeName.Contains('.', StringComparison.Ordinal)) {
                return NormalizeLeafTypeName(typeName);
            }

            return typeName;
        }

        /// <summary>
        /// Resolves the declared generic arity for a converted class.
        /// </summary>
        /// <param name="conversionClass">The converted class to inspect.</param>
        /// <returns>The number of declared generic type parameters.</returns>
        static int GetGenericArity(ConversionClass conversionClass) {
            return conversionClass.GenericArgs?.Count ?? 0;
        }

        /// <summary>
        /// Collapses a namespace-qualified type name to the leaf symbol name used by generated class metadata.
        /// </summary>
        /// <param name="typeName">Source type name to normalize.</param>
        /// <returns>The leaf symbol name when qualified; otherwise, the original name.</returns>
        static string NormalizeLeafTypeName(string typeName) {
            int separatorIndex = typeName.LastIndexOf('.');
            if (separatorIndex < 0 || separatorIndex == typeName.Length - 1) {
                return typeName;
            }

            return typeName[(separatorIndex + 1)..];
        }
    }
}
