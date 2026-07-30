using cs2.core;
using Microsoft.CodeAnalysis;
using System.Text;

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

            string emittedTypeName = GetBaseEmittedTypeName(conversionClass);
            AssertNoEmittedTypeNameCollision(conversionClass, emittedTypeName);
            return emittedTypeName;
        }

        /// <summary>
        /// Resolves the generated file stem for one converted class, using a qualified collision-safe stem when another emitted type collides on case-insensitive filesystems.
        /// </summary>
        /// <param name="conversionClass">The converted class whose generated file stem is needed.</param>
        /// <param name="program">Program model used to detect emitted-name collisions.</param>
        /// <returns>The generated file stem used for emitted headers and sources.</returns>
        public static string GetEmittedFileStem(this ConversionClass conversionClass, ConversionProgram program) {
            string emittedTypeName = conversionClass.GetEmittedTypeName();
            if (conversionClass == null || program == null) {
                return emittedTypeName;
            }

            string collisionSafeFileStem = TryResolveCollisionSafeFileStem(conversionClass, program, emittedTypeName);
            if (!string.IsNullOrWhiteSpace(collisionSafeFileStem)) {
                return collisionSafeFileStem;
            }

            AssertNoEmittedFileStemCollision(conversionClass, program, emittedTypeName);
            return emittedTypeName;
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

            string normalizedQualifiedTypeName = NormalizeQualifiedTypeName(typeName);
            if (!string.IsNullOrWhiteSpace(normalizedQualifiedTypeName) &&
                (normalizedQualifiedTypeName.Contains('.', StringComparison.Ordinal) ||
                 normalizedQualifiedTypeName.Contains('+', StringComparison.Ordinal))) {
                ConversionClass? qualifiedMatch = FindGeneratedClassByQualifiedTypeName(program, normalizedQualifiedTypeName);
                if (qualifiedMatch != null) {
                    return qualifiedMatch;
                }
            }

            string normalizedTypeName = NormalizeLeafTypeName(typeName);
            string lookupKey = BuildNameAndArityLookupKey(normalizedTypeName, genericArgumentCount);
            Dictionary<string, ConversionClass> lookup = program.GetGeneratedClassLookupByNameAndArity(GetNameAndArityLookupKey);
            if (lookup.TryGetValue(lookupKey, out ConversionClass conversionClass)) {
                return conversionClass;
            }

            if (TryStripGeneratedAritySuffix(normalizedTypeName, genericArgumentCount, out string strippedTypeName)) {
                string strippedLookupKey = BuildNameAndArityLookupKey(strippedTypeName, genericArgumentCount);
                if (lookup.TryGetValue(strippedLookupKey, out conversionClass)) {
                    return conversionClass;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a generated converted class by qualified source type identity when Roslyn metadata is available, falling back to leaf-name lookup otherwise.
        /// </summary>
        /// <param name="program">Program model that contains generated classes.</param>
        /// <param name="variableType">Source variable type metadata to resolve.</param>
        /// <returns>The matching generated class when found; otherwise, <c>null</c>.</returns>
        public static ConversionClass? FindGeneratedClass(this ConversionProgram program, VariableType variableType) {
            if (program == null || variableType == null) {
                return null;
            }

            ConversionClass? qualifiedMatch = FindGeneratedClassByQualifiedTypeName(program, variableType.QualifiedTypeName);
            if (qualifiedMatch != null) {
                return qualifiedMatch;
            }

            ConversionClass? exactMatch = program.FindGeneratedClass(variableType.TypeName, variableType.GenericArgs.Count);
            if (exactMatch != null) {
                return exactMatch;
            }

            return FindGeneratedClassByCapturedOuterArity(program, variableType.TypeName, variableType.GenericArgs.Count);
        }

        /// <summary>
        /// Finds a generated converted class from Roslyn type metadata so nested-type identity is preserved across generic outer scopes.
        /// </summary>
        /// <param name="program">Program model that contains generated classes.</param>
        /// <param name="typeSymbol">Roslyn type metadata to resolve.</param>
        /// <returns>The matching generated class when found; otherwise, <c>null</c>.</returns>
        public static ConversionClass? FindGeneratedClass(this ConversionProgram program, INamedTypeSymbol typeSymbol) {
            if (program == null || typeSymbol == null) {
                return null;
            }

            string qualifiedTypeName = NormalizeQualifiedTypeName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            ConversionClass? qualifiedMatch = FindGeneratedClassByQualifiedTypeName(program, qualifiedTypeName);
            if (qualifiedMatch != null) {
                return qualifiedMatch;
            }

            ConversionClass? exactMatch = program.FindGeneratedClass(typeSymbol.Name, typeSymbol.TypeArguments.Length);
            if (exactMatch != null) {
                return exactMatch;
            }

            return FindGeneratedClassByCapturedOuterArity(program, typeSymbol.Name, typeSymbol.TypeArguments.Length);
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
            IReadOnlyList<VariableType> effectiveGenericArguments = GetEffectiveGenericArguments(varType, program);

            if (string.IsNullOrWhiteSpace(typeName)) {
                typeName = "object";
            }

            string renderedType = typeName;
            if (effectiveGenericArguments.Count > 0) {
                string genericArguments = string.Join(", ", effectiveGenericArguments.Select(argument => argument.ToCPPString(program)));
                renderedType = $"{typeName}<{genericArguments}>";
            }

            if (varType.IsConstReference) {
                return $"const {renderedType}&";
            }

            if (varType.IsReference) {
                return $"{renderedType}&";
            }

            return renderedType;
        }

        /// <summary>
        /// Resolves the emitted C++ type token for a single abstract variable type without applying pointer decoration.
        /// </summary>
        /// <param name="varType">The source variable type.</param>
        /// <returns>The normalized C++ type token.</returns>
        static string ResolveCppTypeName(VariableType varType, ConversionProgram program) {
            if (varType.Type == VariableDataType.Void) {
                return "void";
            }

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
                if (string.Equals(varType.TypeName, "IReadOnlyList", StringComparison.Ordinal) ||
                    string.Equals(varType.TypeName, "IReadOnlyCollection", StringComparison.Ordinal) ||
                    string.Equals(varType.QualifiedTypeName, "System.Collections.Generic.IReadOnlyList", StringComparison.Ordinal) ||
                    string.Equals(varType.QualifiedTypeName, "System.Collections.Generic.IReadOnlyCollection", StringComparison.Ordinal)) {
                    return "IReadOnlyList";
                }

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

            if (typeName == "void" || typeName == "Void") {
                return "void";
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

            if (typeName == "nint" || typeName == "IntPtr" || typeName == "System.IntPtr") {
                return "intptr_t";
            }

            if (typeName == "nuint" || typeName == "UIntPtr" || typeName == "System.UIntPtr") {
                return "uintptr_t";
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

            string runtimeTypeName = ResolveRuntimeCppTypeName(varType);
            if (!string.IsNullOrWhiteSpace(runtimeTypeName)) {
                return runtimeTypeName;
            }

            if (TryResolveConfiguredTypeRemap(varType, program, out string remappedTypeName)) {
                return VariableUtil.GetVarType(remappedTypeName).ToCPPString(program);
            }

            ConversionClass? generatedClass = program.FindGeneratedClass(varType);
            if (generatedClass != null) {
                return generatedClass.GetEmittedTypeName();
            }

            if (TryResolveDirectExternalGeneratedTypeName(varType, out string emittedExternalTypeName)) {
                return emittedExternalTypeName;
            }

            if (!string.IsNullOrWhiteSpace(typeName) && typeName.Contains('.', StringComparison.Ordinal)) {
                return NormalizeLeafTypeName(typeName);
            }

            return typeName;
        }

        static bool TryResolveConfiguredTypeRemap(VariableType varType, ConversionProgram program, out string remappedTypeName) {
            remappedTypeName = string.Empty;
            if (varType == null || program?.TypeMap == null || program.TypeMap.Count == 0) {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(varType.QualifiedTypeName) &&
                program.TypeMap.TryGetValue(varType.QualifiedTypeName, out remappedTypeName) &&
                !string.Equals(remappedTypeName, varType.QualifiedTypeName, StringComparison.Ordinal)) {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(varType.TypeName) &&
                program.TypeMap.TryGetValue(varType.TypeName, out remappedTypeName) &&
                !string.Equals(remappedTypeName, varType.TypeName, StringComparison.Ordinal)) {
                return true;
            }

            remappedTypeName = string.Empty;
            return false;
        }

        /// <summary>
        /// Resolves the include-path stem for one configured type remap, preserving canonical generated file stems when the remapped target lives outside the current conversion graph.
        /// </summary>
        /// <param name="varType">Source type reference that may be remapped.</param>
        /// <param name="program">Program model that owns configured type remaps.</param>
        /// <param name="includePath">Resolved include-path stem when a remap applies.</param>
        /// <returns>True when a configured remap supplied the include-path stem; otherwise false.</returns>
        public static bool TryResolveConfiguredTypeRemapIncludePath(this VariableType varType, ConversionProgram program, out string includePath) {
            includePath = string.Empty;
            if (!TryResolveConfiguredTypeRemap(varType, program, out string remappedTypeName)) {
                return false;
            }

            VariableType remappedType = VariableUtil.GetVarType(remappedTypeName);
            string runtimeTypeName = ResolveRuntimeCppTypeName(remappedType);
            if (!string.IsNullOrWhiteSpace(runtimeTypeName)) {
                includePath = runtimeTypeName;
                return true;
            }

            ConversionClass? generatedClass = program.FindGeneratedClass(remappedType);
            if (generatedClass != null) {
                includePath = generatedClass.GetEmittedFileStem(program);
                return true;
            }

            includePath = NormalizeLeafTypeName(remappedType.TypeName);
            return !string.IsNullOrWhiteSpace(includePath);
        }

        /// <summary>
        /// Resolves emitted names for shared runtime generic surfaces that are not generated classes, preserving the C++ runtime contract when those types appear in nested generic positions.
        /// </summary>
        /// <param name="varType">The type reference being rendered.</param>
        /// <returns>The runtime C++ type name when the reference maps to a shared runtime surface; otherwise, an empty string.</returns>
        static string ResolveRuntimeCppTypeName(VariableType varType) {
            if (varType == null) {
                return string.Empty;
            }

            bool hasGenericArguments = varType.GenericArgs != null && varType.GenericArgs.Count > 0;
            if (MatchesRuntimeType(varType, "Array", "System.Array")) {
                return "Array";
            }

            if (MatchesRuntimeType(varType, "Span", "System.Span")) {
                return "Span";
            }

            if (MatchesRuntimeType(varType, "ReadOnlySpan", "System.ReadOnlySpan")) {
                return "ReadOnlySpan";
            }

            if (MatchesRuntimeType(varType, "HashSet", "System.Collections.Generic.HashSet")) {
                return "HashSet";
            }

            if (MatchesRuntimeType(varType, "List", "System.Collections.Generic.List")) {
                return "List";
            }

            if (MatchesRuntimeType(varType, "Dictionary", "System.Collections.Generic.Dictionary")) {
                return "Dictionary";
            }

            if (MatchesRuntimeType(varType, "Stack", "System.Collections.Generic.Stack")) {
                return "Stack";
            }

            if (MatchesRuntimeType(varType, "ValueTuple", "System.ValueTuple")) {
                return "ValueTuple";
            }

            if (MatchesRuntimeType(varType, "Action", "System.Action")) {
                return "Action";
            }

            if (MatchesRuntimeType(varType, "Func", "System.Func")) {
                return "Func";
            }

            if (MatchesRuntimeType(varType, "FunctionPointer", "FunctionPointer")) {
                return "FunctionPointer";
            }

            if (MatchesRuntimeType(varType, "Nullable", "System.Nullable")) {
                return "Nullable";
            }

            if (MatchesRuntimeType(varType, "ValueTuple", "System.ValueTuple")) {
                return "ValueTuple";
            }

            if (MatchesRuntimeType(varType, "Stack", "System.Collections.Generic.Stack")) {
                return "Stack";
            }

            if (MatchesRuntimeType(varType, "Vector", "System.Numerics.Vector")) {
                return hasGenericArguments ? "Vector_1" : "Vector";
            }

            if (MatchesRuntimeType(varType, "Vector128", "System.Runtime.Intrinsics.Vector128")) {
                return hasGenericArguments ? "Vector128_1" : "Vector128";
            }

            if (MatchesRuntimeType(varType, "Vector256", "System.Runtime.Intrinsics.Vector256")) {
                return hasGenericArguments ? "Vector256" : "Vector256";
            }

            if (MatchesRuntimeType(varType, "Vector512", "System.Runtime.Intrinsics.Vector512")) {
                return hasGenericArguments ? "Vector512" : "Vector512";
            }

            if (MatchesRuntimeType(varType, "KeyValuePair", "System.Collections.Generic.KeyValuePair")) {
                return "KeyValuePair";
            }

            if (MatchesRuntimeType(varType, "EqualityComparer", "System.Collections.Generic.EqualityComparer")) {
                return "EqualityComparer";
            }

            if (MatchesRuntimeType(varType, "IEnumerator", "System.Collections.Generic.IEnumerator")) {
                return "IEnumerator";
            }

            if (MatchesRuntimeType(varType, "IEnumerable", "System.Collections.Generic.IEnumerable")) {
                return "IEnumerable";
            }

            return string.Empty;
        }

        /// <summary>
        /// Determines whether a variable type refers to one shared runtime type, matching both short and qualified managed names while tolerating already-normalized emitted aliases.
        /// </summary>
        /// <param name="varType">The type reference to inspect.</param>
        /// <param name="shortTypeName">The short managed type name.</param>
        /// <param name="qualifiedTypeName">The fully qualified managed type name.</param>
        /// <returns><c>true</c> when the reference targets the runtime type; otherwise, <c>false</c>.</returns>
        static bool MatchesRuntimeType(VariableType varType, string shortTypeName, string qualifiedTypeName) {
            if (varType == null) {
                return false;
            }

            string typeName = NormalizeLeafTypeName(varType.TypeName);
            int genericArgumentCount = varType.GenericArgs?.Count ?? 0;
            string normalizedQualifiedTypeName = NormalizeQualifiedTypeName(varType.QualifiedTypeName);
            if (string.Equals(typeName, shortTypeName, StringComparison.Ordinal) ||
                string.Equals(typeName, qualifiedTypeName, StringComparison.Ordinal) ||
                string.Equals(normalizedQualifiedTypeName, qualifiedTypeName, StringComparison.Ordinal)) {
                return true;
            }

            if (TryStripGeneratedAritySuffix(typeName, genericArgumentCount, out string strippedTypeName) &&
                (string.Equals(strippedTypeName, shortTypeName, StringComparison.Ordinal) ||
                 string.Equals(strippedTypeName, qualifiedTypeName, StringComparison.Ordinal))) {
                return true;
            }

            return TryStripQualifiedGeneratedAritySuffix(normalizedQualifiedTypeName, genericArgumentCount, out string strippedQualifiedTypeName) &&
                string.Equals(strippedQualifiedTypeName, qualifiedTypeName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Attempts to remove one generated arity suffix from the leaf segment of a qualified type identity while preserving any namespace or containing-type prefix.
        /// </summary>
        /// <param name="qualifiedTypeName">Qualified type name that may contain a generated arity suffix on its leaf segment.</param>
        /// <param name="genericArgumentCount">Expected generic arity used to validate the generated suffix.</param>
        /// <param name="strippedQualifiedTypeName">Qualified type name without the generated arity suffix when one was removed.</param>
        /// <returns>True when the suffix was removed; otherwise false.</returns>
        static bool TryStripQualifiedGeneratedAritySuffix(string qualifiedTypeName, int genericArgumentCount, out string strippedQualifiedTypeName) {
            strippedQualifiedTypeName = string.Empty;
            if (string.IsNullOrWhiteSpace(qualifiedTypeName)) {
                return false;
            }

            int separatorIndex = Math.Max(
                qualifiedTypeName.LastIndexOf('.'),
                qualifiedTypeName.LastIndexOf('+'));
            string prefix = separatorIndex >= 0 ? qualifiedTypeName[..(separatorIndex + 1)] : string.Empty;
            string leafTypeName = separatorIndex >= 0 ? qualifiedTypeName[(separatorIndex + 1)..] : qualifiedTypeName;
            if (!TryStripGeneratedAritySuffix(leafTypeName, genericArgumentCount, out string strippedLeafTypeName)) {
                return false;
            }

            strippedQualifiedTypeName = prefix + strippedLeafTypeName;
            return true;
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
        /// Resolves the generic arguments that should be rendered at one use site, recovering captured outer generic parameters for nested generated types.
        /// </summary>
        /// <param name="varType">Source variable type metadata to inspect.</param>
        /// <param name="program">Program model that contains generated classes.</param>
        /// <returns>Explicit generic arguments when present; otherwise implicit captured generic parameters for the matched generated type.</returns>
        static IReadOnlyList<VariableType> GetEffectiveGenericArguments(VariableType varType, ConversionProgram program) {
            ConversionClass? generatedClass = program.FindGeneratedClass(varType);
            if (generatedClass?.GenericArgs == null || generatedClass.GenericArgs.Count == 0) {
                if (varType.GenericArgs != null && varType.GenericArgs.Count > 0) {
                    return varType.GenericArgs;
                }

                return Array.Empty<VariableType>();
            }

            if (varType.GenericArgs != null && varType.GenericArgs.Count > 0) {
                int implicitArgumentCount = generatedClass.GenericArgs.Count - varType.GenericArgs.Count;
                if (implicitArgumentCount <= 0) {
                    return varType.GenericArgs;
                }

                List<VariableType> effectiveGenericArguments = generatedClass.GenericArgs
                    .Take(implicitArgumentCount)
                    .Select(CreateImplicitGenericArgument)
                    .ToList();
                effectiveGenericArguments.AddRange(varType.GenericArgs);
                return effectiveGenericArguments;
            }

            if (generatedClass.TypeSymbol?.ContainingType == null) {
                return Array.Empty<VariableType>();
            }

            return generatedClass.GenericArgs
                .Select(CreateImplicitGenericArgument)
                .ToList();
        }

        /// <summary>
        /// Resolves the emitted type token for one unresolved external managed generic so references to generated classes from other projects keep the backend arity suffix convention.
        /// </summary>
        /// <param name="varType">Referenced type metadata to inspect.</param>
        /// <param name="emittedTypeName">Receives the emitted type token when one can be inferred safely.</param>
        /// <returns>True when one external generated type name was inferred; otherwise false.</returns>
        static bool TryResolveDirectExternalGeneratedTypeName(VariableType varType, out string emittedTypeName) {
            emittedTypeName = string.Empty;
            if (varType == null || varType.GenericArgs == null || varType.GenericArgs.Count == 0) {
                return false;
            }

            string qualifiedTypeName = NormalizeQualifiedTypeName(varType.QualifiedTypeName);
            string sourceTypeName = !string.IsNullOrWhiteSpace(qualifiedTypeName)
                ? qualifiedTypeName
                : varType.TypeName;
            if (string.IsNullOrWhiteSpace(sourceTypeName)) {
                return false;
            }

            if (sourceTypeName.StartsWith("System.", StringComparison.Ordinal) ||
                sourceTypeName.StartsWith("Microsoft.", StringComparison.Ordinal)) {
                return false;
            }

            string leafTypeName = NormalizeLeafTypeName(varType.TypeName);
            if (string.IsNullOrWhiteSpace(leafTypeName)) {
                return false;
            }

            if (TryStripGeneratedAritySuffix(leafTypeName, varType.GenericArgs.Count, out string strippedLeafTypeName)) {
                emittedTypeName = $"{strippedLeafTypeName}_{varType.GenericArgs.Count}";
                return true;
            }

            emittedTypeName = $"{leafTypeName}_{varType.GenericArgs.Count}";
            return true;
        }

        /// <summary>
        /// Removes one trailing generated arity suffix when a referenced type token already uses the emitted-name convention.
        /// </summary>
        /// <param name="typeName">Leaf type token that may already end with one generated arity suffix.</param>
        /// <param name="genericArgumentCount">Generic arity currently being rendered.</param>
        /// <param name="strippedTypeName">Receives the unsuffixed type token when one matching suffix was removed.</param>
        /// <returns>True when one matching generated arity suffix was removed; otherwise false.</returns>
        static bool TryStripGeneratedAritySuffix(string typeName, int genericArgumentCount, out string strippedTypeName) {
            strippedTypeName = string.Empty;
            if (string.IsNullOrWhiteSpace(typeName) || genericArgumentCount <= 0) {
                return false;
            }

            string suffix = "_" + genericArgumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!typeName.EndsWith(suffix, StringComparison.Ordinal) || typeName.Length <= suffix.Length) {
                return false;
            }

            strippedTypeName = typeName[..^suffix.Length];
            return !string.IsNullOrWhiteSpace(strippedTypeName);
        }

        /// <summary>
        /// Finds one generated nested type whose emitted arity is larger than the explicit source syntax because it captures generic parameters from an outer containing type.
        /// </summary>
        /// <param name="program">Program model that contains generated classes.</param>
        /// <param name="typeName">Leaf source type name to resolve.</param>
        /// <param name="minimumGenericArgumentCount">Minimum explicit generic argument count observed at the use site.</param>
        /// <returns>The best matching generated class when one nested captured-arity candidate can be determined; otherwise, <c>null</c>.</returns>
        static ConversionClass? FindGeneratedClassByCapturedOuterArity(ConversionProgram program, string typeName, int minimumGenericArgumentCount) {
            if (program == null || string.IsNullOrWhiteSpace(typeName)) {
                return null;
            }

            string normalizedTypeName = NormalizeLeafTypeName(typeName);
            List<ConversionClass> candidates = program.Classes
                .Where(candidate => string.Equals(candidate.Name, normalizedTypeName, StringComparison.Ordinal) &&
                    GetGenericArity(candidate) > minimumGenericArgumentCount &&
                    candidate.TypeSymbol?.ContainingType != null)
                .OrderBy(GetGenericArity)
                .ToList();

            if (candidates.Count == 0) {
                return null;
            }

            int lowestArity = GetGenericArity(candidates[0]);
            List<ConversionClass> lowestArityCandidates = candidates
                .Where(candidate => GetGenericArity(candidate) == lowestArity)
                .ToList();
            return lowestArityCandidates.Count == 1
                ? lowestArityCandidates[0]
                : null;
        }

        /// <summary>
        /// Finds one generated class whose Roslyn qualified type identity matches the supplied source type name.
        /// </summary>
        /// <param name="program">Program model that contains generated classes.</param>
        /// <param name="qualifiedTypeName">Qualified source type identity to resolve.</param>
        /// <returns>The matching generated class when found; otherwise, <c>null</c>.</returns>
        static ConversionClass? FindGeneratedClassByQualifiedTypeName(ConversionProgram program, string qualifiedTypeName) {
            if (program == null || string.IsNullOrWhiteSpace(qualifiedTypeName)) {
                return null;
            }

            string normalizedQualifiedTypeName = NormalizeQualifiedTypeName(qualifiedTypeName);
            Dictionary<string, ConversionClass> lookup = program.GetQualifiedGeneratedClassLookup(GetNormalizedQualifiedTypeName);
            return lookup.TryGetValue(normalizedQualifiedTypeName, out ConversionClass conversionClass)
                ? conversionClass
                : null;
        }

        /// <summary>
        /// Creates one implicit generic parameter placeholder so nested generated types can render captured outer generic arguments.
        /// </summary>
        /// <param name="genericParameterName">Generic parameter name declared by the matched generated type.</param>
        /// <returns>Variable type metadata for one compile-time generic parameter.</returns>
        static VariableType CreateImplicitGenericArgument(string genericParameterName) {
            return new VariableType(VariableDataType.Unknown, genericParameterName) {
                QualifiedTypeName = genericParameterName,
                IsGenericParameter = true
            };
        }

        /// <summary>
        /// Collapses a namespace-qualified type name to the leaf symbol name used by generated class metadata.
        /// </summary>
        /// <param name="typeName">Source type name to normalize.</param>
        /// <returns>The leaf symbol name when qualified; otherwise, the original name.</returns>
        static string NormalizeLeafTypeName(string typeName) {
            if (string.IsNullOrWhiteSpace(typeName)) {
                return string.Empty;
            }

            int separatorIndex = typeName.LastIndexOf('.');
            if (separatorIndex < 0 || separatorIndex == typeName.Length - 1) {
                return typeName;
            }

            return typeName[(separatorIndex + 1)..];
        }

        /// <summary>
        /// Normalizes one fully-qualified Roslyn type identity by removing the global alias prefix used in symbol display strings.
        /// </summary>
        /// <param name="qualifiedTypeName">Qualified source type identity to normalize.</param>
        /// <returns>Qualified source type identity without a global alias prefix.</returns>
        static string NormalizeQualifiedTypeName(string qualifiedTypeName) {
            if (string.IsNullOrWhiteSpace(qualifiedTypeName)) {
                return string.Empty;
            }

            return qualifiedTypeName.StartsWith("global::", StringComparison.Ordinal)
                ? qualifiedTypeName["global::".Length..]
                : qualifiedTypeName;
        }

        /// <summary>
        /// Resolves one stable qualified-type lookup key for a generated class so repeated Roslyn symbol scans are avoided during backend type resolution.
        /// </summary>
        /// <param name="conversionClass">Generated class whose qualified identity is needed.</param>
        /// <returns>Normalized qualified identity when available; otherwise an empty string.</returns>
        static string GetNormalizedQualifiedTypeName(ConversionClass conversionClass) {
            if (conversionClass?.TypeSymbol == null) {
                return string.Empty;
            }

            return NormalizeQualifiedTypeName(conversionClass.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        /// <summary>
        /// Resolves one stable lookup key for one generated class using its leaf source name and generic arity so repeated linear scans are avoided during backend type resolution.
        /// </summary>
        /// <param name="conversionClass">Generated class whose lookup key is needed.</param>
        /// <returns>Name-and-arity lookup key for the generated class.</returns>
        static string GetNameAndArityLookupKey(ConversionClass conversionClass) {
            if (conversionClass == null) {
                return string.Empty;
            }

            return BuildNameAndArityLookupKey(conversionClass.Name, GetGenericArity(conversionClass));
        }

        /// <summary>
        /// Builds one stable lookup key from one normalized leaf type name and one generic arity.
        /// </summary>
        /// <param name="typeName">Normalized leaf type name.</param>
        /// <param name="genericArgumentCount">Declared or referenced generic arity.</param>
        /// <returns>Name-and-arity lookup key.</returns>
        static string BuildNameAndArityLookupKey(string typeName, int genericArgumentCount) {
            return $"{typeName}|{genericArgumentCount}";
        }

        /// <summary>
        /// Throws when two generated classes would emit the same C++ type identifier.
        /// </summary>
        /// <param name="conversionClass">Converted class whose emitted type name is being validated.</param>
        /// <param name="emittedTypeName">Resolved emitted type name before emission.</param>
        static void AssertNoEmittedTypeNameCollision(ConversionClass conversionClass, string emittedTypeName) {
            if (conversionClass?.Program == null || string.IsNullOrWhiteSpace(emittedTypeName)) {
                return;
            }

            HashSet<string> collisions = conversionClass.Program.GetBaseEmittedTypeNameCollisions(GetBaseEmittedTypeName);
            if (!collisions.Contains(emittedTypeName)) {
                return;
            }

            ConversionClass collidingClass = conversionClass.Program.Classes.FirstOrDefault(candidate =>
                candidate != null &&
                !ReferenceEquals(candidate, conversionClass) &&
                !candidate.IsNative &&
                string.Equals(GetBaseEmittedTypeName(candidate), emittedTypeName, StringComparison.Ordinal));
            if (collidingClass == null) {
                return;
            }

            throw new InvalidOperationException(
                $"Generated C++ type name collision for '{emittedTypeName}' between '{DescribeConversionClass(conversionClass)}' and '{DescribeConversionClass(collidingClass)}'.");
        }

        /// <summary>
        /// Throws when two generated classes would emit the same header/source file stem on case-insensitive filesystems.
        /// </summary>
        /// <param name="conversionClass">Converted class whose emitted file stem is being validated.</param>
        /// <param name="program">Program model that owns the generated classes.</param>
        /// <param name="emittedTypeName">Resolved emitted type name before file emission.</param>
        static void AssertNoEmittedFileStemCollision(
            ConversionClass conversionClass,
            ConversionProgram program,
            string emittedTypeName) {
            if (conversionClass == null || program == null || string.IsNullOrWhiteSpace(emittedTypeName)) {
                return;
            }

            if (program is CPPProgram cppProgram &&
                cppProgram.ReachableGeneratedTypesByFileStem.TryGetValue(emittedTypeName, out List<ConversionClass> collidingTypes)) {
                ConversionClass collidingReachableType = collidingTypes.FirstOrDefault(candidate =>
                    candidate != null &&
                    !ReferenceEquals(candidate, conversionClass) &&
                    !candidate.IsNative);
                if (collidingReachableType == null) {
                    return;
                }

                throw new InvalidOperationException(
                    $"Generated C++ file stem collision for '{emittedTypeName}' between '{DescribeConversionClass(conversionClass)}' and '{DescribeConversionClass(collidingReachableType)}'.");
            }

            ConversionClass collidingClass = program.Classes.FirstOrDefault(candidate =>
                candidate != null &&
                !ReferenceEquals(candidate, conversionClass) &&
                !candidate.IsNative &&
                string.Equals(candidate.GetEmittedTypeName(), emittedTypeName, StringComparison.OrdinalIgnoreCase));
            if (collidingClass == null) {
                return;
            }

            throw new InvalidOperationException(
                $"Generated C++ file stem collision for '{emittedTypeName}' between '{DescribeConversionClass(conversionClass)}' and '{DescribeConversionClass(collidingClass)}'.");
        }

        /// <summary>
        /// Attempts to resolve one case-insensitive collision-safe file stem from the qualified source type identity.
        /// </summary>
        /// <param name="conversionClass">Converted class whose generated file stem is being resolved.</param>
        /// <param name="program">Program model that owns the generated classes.</param>
        /// <param name="emittedTypeName">Resolved emitted type name before file emission.</param>
        /// <returns>Qualified collision-safe file stem when one is required and available; otherwise an empty string.</returns>
        static string TryResolveCollisionSafeFileStem(
            ConversionClass conversionClass,
            ConversionProgram program,
            string emittedTypeName) {
            if (!HasEmittedFileStemCollision(conversionClass, program, emittedTypeName)) {
                return string.Empty;
            }
            if (IsPrimaryCaseInsensitiveFileStemOwner(conversionClass, program, emittedTypeName)) {
                return emittedTypeName;
            }

            string qualifiedFileStem = BuildQualifiedCollisionSafeFileStem(conversionClass);
            if (string.IsNullOrWhiteSpace(qualifiedFileStem) ||
                string.Equals(qualifiedFileStem, emittedTypeName, StringComparison.OrdinalIgnoreCase)) {
                return string.Empty;
            }

            return qualifiedFileStem;
        }

        /// <summary>
        /// Returns whether the supplied generated class currently collides with another emitted file stem on a case-insensitive filesystem.
        /// </summary>
        /// <param name="conversionClass">Converted class whose generated file stem is being evaluated.</param>
        /// <param name="program">Program model that owns the generated classes.</param>
        /// <param name="emittedTypeName">Resolved emitted type name before file emission.</param>
        /// <returns>True when another generated class would reuse the same case-insensitive file stem.</returns>
        static bool HasEmittedFileStemCollision(
            ConversionClass conversionClass,
            ConversionProgram program,
            string emittedTypeName) {
            if (conversionClass == null || program == null || string.IsNullOrWhiteSpace(emittedTypeName)) {
                return false;
            }

            if (program is CPPProgram cppProgram &&
                cppProgram.ReachableGeneratedTypesByFileStem.TryGetValue(emittedTypeName, out List<ConversionClass> collidingTypes)) {
                return collidingTypes.Any(candidate =>
                    candidate != null &&
                    !ReferenceEquals(candidate, conversionClass) &&
                    !candidate.IsNative);
            }

            return program.Classes.Any(candidate =>
                candidate != null &&
                !ReferenceEquals(candidate, conversionClass) &&
                !candidate.IsNative &&
                string.Equals(candidate.GetEmittedTypeName(), emittedTypeName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns whether the supplied generated class should keep the canonical unqualified file stem within one case-insensitive collision group.
        /// </summary>
        /// <param name="conversionClass">Converted class whose generated file stem ownership is being evaluated.</param>
        /// <param name="program">Program model that owns the generated classes.</param>
        /// <param name="emittedTypeName">Resolved emitted type name before file emission.</param>
        /// <returns>True when the class should retain the original unqualified emitted file stem; otherwise false.</returns>
        static bool IsPrimaryCaseInsensitiveFileStemOwner(
            ConversionClass conversionClass,
            ConversionProgram program,
            string emittedTypeName) {
            if (conversionClass == null || program == null || string.IsNullOrWhiteSpace(emittedTypeName)) {
                return false;
            }

            if (program is CPPProgram cppProgram &&
                cppProgram.ReachableGeneratedTypesByFileStem.TryGetValue(emittedTypeName, out List<ConversionClass> collidingTypes)) {
                ConversionClass primaryReachableType = collidingTypes
                    .Where(candidate => candidate != null && !candidate.IsNative)
                    .OrderByDescending(candidate => IsPreferredCanonicalFileStem(candidate.GetEmittedTypeName()))
                    .ThenByDescending(candidate => candidate.GetEmittedTypeName(), StringComparer.Ordinal)
                    .ThenBy(candidate => DescribeConversionClass(candidate), StringComparer.Ordinal)
                    .FirstOrDefault();
                return ReferenceEquals(primaryReachableType, conversionClass);
            }

            ConversionClass primaryClass = program.Classes
                .Where(candidate =>
                    candidate != null &&
                    !candidate.IsNative &&
                    string.Equals(candidate.GetEmittedTypeName(), emittedTypeName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => IsPreferredCanonicalFileStem(candidate.GetEmittedTypeName()))
                .ThenByDescending(candidate => candidate.GetEmittedTypeName(), StringComparer.Ordinal)
                .ThenBy(candidate => DescribeConversionClass(candidate), StringComparer.Ordinal)
                .FirstOrDefault();
            return ReferenceEquals(primaryClass, conversionClass);
        }

        /// <summary>
        /// Returns whether one emitted type name should keep the canonical unqualified file stem when a case-insensitive collision exists.
        /// </summary>
        /// <param name="emittedTypeName">Emitted type name under evaluation.</param>
        /// <returns>True when the emitted type name is already all lowercase and should retain the canonical stem.</returns>
        static bool IsPreferredCanonicalFileStem(string emittedTypeName) {
            if (string.IsNullOrWhiteSpace(emittedTypeName)) {
                return false;
            }

            for (int index = 0; index < emittedTypeName.Length; index++) {
                char character = emittedTypeName[index];
                if (char.IsLetter(character) && char.IsUpper(character)) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds one qualified case-insensitive-safe file stem from the source type identity.
        /// </summary>
        /// <param name="conversionClass">Converted class whose qualified source identity should be encoded into the generated file stem.</param>
        /// <returns>Sanitized qualified file stem when the source type exposes a qualified identity; otherwise an empty string.</returns>
        static string BuildQualifiedCollisionSafeFileStem(ConversionClass conversionClass) {
            string qualifiedTypeName = GetNormalizedQualifiedTypeName(conversionClass);
            if (string.IsNullOrWhiteSpace(qualifiedTypeName)) {
                return string.Empty;
            }

            return SanitizeQualifiedFileStem(qualifiedTypeName);
        }

        /// <summary>
        /// Sanitizes one qualified source type identity into a stable file-system-safe generated file stem.
        /// </summary>
        /// <param name="qualifiedTypeName">Qualified source type identity to sanitize.</param>
        /// <returns>Stable sanitized file stem derived from the qualified source type identity.</returns>
        static string SanitizeQualifiedFileStem(string qualifiedTypeName) {
            if (string.IsNullOrWhiteSpace(qualifiedTypeName)) {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(qualifiedTypeName.Length);
            bool previousWasUnderscore = false;
            for (int index = 0; index < qualifiedTypeName.Length; index++) {
                char character = qualifiedTypeName[index];
                if (char.IsLetterOrDigit(character)) {
                    builder.Append(character);
                    previousWasUnderscore = false;
                } else if (!previousWasUnderscore) {
                    builder.Append('_');
                    previousWasUnderscore = true;
                }
            }

            while (builder.Length > 0 && builder[^1] == '_') {
                builder.Length--;
            }

            return builder.ToString();
        }

        /// <summary>
        /// Resolves the emitted type name without namespace collision qualification so collision detection can compare the shared base identifier.
        /// </summary>
        /// <param name="conversionClass">Converted class whose base emitted name is needed.</param>
        /// <returns>Unqualified emitted type name composed from the source name and generic arity.</returns>
        static string GetBaseEmittedTypeName(ConversionClass conversionClass) {
            if (conversionClass == null) {
                return string.Empty;
            }

            string configuredEmittedTypeName = GetConfiguredEmittedTypeName(conversionClass);
            if (!string.IsNullOrWhiteSpace(configuredEmittedTypeName)) {
                return configuredEmittedTypeName;
            }

            if (!string.IsNullOrWhiteSpace(conversionClass.CodeGenRename)) {
                return conversionClass.CodeGenRename;
            }

            string emittedLeafTypeName = conversionClass.GenericArgs == null || conversionClass.GenericArgs.Count == 0
                ? conversionClass.Name
                : $"{conversionClass.Name}_{conversionClass.GenericArgs.Count}";
            if (conversionClass.TypeSymbol?.ContainingType == null) {
                return emittedLeafTypeName;
            }

            string containingTypePrefix = BuildContainingTypePrefix(conversionClass.TypeSymbol.ContainingType);
            if (string.IsNullOrWhiteSpace(containingTypePrefix)) {
                return emittedLeafTypeName;
            }

            return $"{containingTypePrefix}_{emittedLeafTypeName}";
        }

        /// <summary>
        /// Resolves one configured emitted type name override for a generated source class when the conversion run supplied a matching type remap.
        /// </summary>
        /// <param name="conversionClass">Generated source class whose emitted name is being resolved.</param>
        /// <returns>Configured emitted leaf type name, or an empty string when no configured override exists.</returns>
        static string GetConfiguredEmittedTypeName(ConversionClass conversionClass) {
            if (conversionClass?.Program?.TypeMap == null || conversionClass.TypeSymbol == null) {
                return string.Empty;
            }

            string qualifiedSourceTypeName = conversionClass.TypeSymbol.ToDisplayString();
            if (conversionClass.Program.TypeMap.TryGetValue(qualifiedSourceTypeName, out string configuredQualifiedTargetTypeName) &&
                !string.IsNullOrWhiteSpace(configuredQualifiedTargetTypeName)) {
                return NormalizeLeafTypeName(configuredQualifiedTargetTypeName);
            }

            if (conversionClass.Program.TypeMap.TryGetValue(conversionClass.Name, out string configuredLeafTargetTypeName) &&
                !string.IsNullOrWhiteSpace(configuredLeafTargetTypeName)) {
                return NormalizeLeafTypeName(configuredLeafTargetTypeName);
            }

            return string.Empty;
        }

        /// <summary>
        /// Describes one conversion class using its fully qualified source type name when available.
        /// </summary>
        /// <param name="conversionClass">Converted class to describe.</param>
        /// <returns>Fully qualified source type name when Roslyn metadata is available; otherwise the converted class name.</returns>
        static string DescribeConversionClass(ConversionClass conversionClass) {
            if (conversionClass?.TypeSymbol != null) {
                return conversionClass.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty, StringComparison.Ordinal);
            }

            return conversionClass?.Name ?? string.Empty;
        }

        /// <summary>
        /// Builds a stable underscore-delimited emitted-name prefix from the containing type chain of one nested source type.
        /// </summary>
        /// <param name="containingTypeSymbol">Containing source type for the nested declaration being emitted.</param>
        /// <returns>Containing-type emitted-name prefix, or an empty string when the declaration is top-level.</returns>
        static string BuildContainingTypePrefix(INamedTypeSymbol containingTypeSymbol) {
            if (containingTypeSymbol == null) {
                return string.Empty;
            }

            string parentPrefix = BuildContainingTypePrefix(containingTypeSymbol.ContainingType);
            string currentSegment = containingTypeSymbol.Arity > 0
                ? $"{containingTypeSymbol.Name}_{containingTypeSymbol.Arity}"
                : containingTypeSymbol.Name;
            if (string.IsNullOrWhiteSpace(parentPrefix)) {
                return currentSegment;
            }

            return $"{parentPrefix}_{currentSegment}";
        }
    }
}
