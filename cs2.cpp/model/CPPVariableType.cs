using cs2.core;
using Microsoft.CodeAnalysis;

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

            string emittedTypeName = conversionClass.GenericArgs == null || conversionClass.GenericArgs.Count == 0
                ? conversionClass.Name
                : $"{conversionClass.Name}_{conversionClass.GenericArgs.Count}";

            string qualifiedPrefix = BuildQualifiedFileStemPrefix(conversionClass);
            if (!ShouldQualifyEmittedTypeName(conversionClass, emittedTypeName, qualifiedPrefix)) {
                return emittedTypeName;
            }

            return $"{qualifiedPrefix}_{emittedTypeName}";
        }

        /// <summary>
        /// Resolves the generated file stem for one converted class, adding a namespace-derived prefix only when another emitted type collides on case-insensitive filesystems.
        /// </summary>
        /// <param name="conversionClass">The converted class whose generated file stem is needed.</param>
        /// <param name="program">Program model used to detect emitted-name collisions.</param>
        /// <returns>The generated file stem used for emitted headers and sources.</returns>
        public static string GetEmittedFileStem(this ConversionClass conversionClass, ConversionProgram program) {
            string emittedTypeName = conversionClass.GetEmittedTypeName();
            if (conversionClass == null || program == null) {
                return emittedTypeName;
            }

            string qualifiedPrefix = BuildQualifiedFileStemPrefix(conversionClass);
            if (!ShouldQualifyFileStem(conversionClass, program, emittedTypeName, qualifiedPrefix)) {
                return emittedTypeName;
            }

            return $"{qualifiedPrefix}_{emittedTypeName}";
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
            return lookup.TryGetValue(lookupKey, out ConversionClass conversionClass)
                ? conversionClass
                : null;
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
        /// Resolves emitted names for shared runtime generic surfaces that are not generated classes, preserving the C++ runtime contract when those types appear in nested generic positions.
        /// </summary>
        /// <param name="varType">The type reference being rendered.</param>
        /// <returns>The runtime C++ type name when the reference maps to a shared runtime surface; otherwise, an empty string.</returns>
        static string ResolveRuntimeCppTypeName(VariableType varType) {
            if (varType == null) {
                return string.Empty;
            }

            bool hasGenericArguments = varType.GenericArgs != null && varType.GenericArgs.Count > 0;
            if (MatchesRuntimeType(varType, "Span", "System.Span")) {
                return "Span";
            }

            if (MatchesRuntimeType(varType, "ReadOnlySpan", "System.ReadOnlySpan")) {
                return "ReadOnlySpan";
            }

            if (MatchesRuntimeType(varType, "Vector", "System.Numerics.Vector")) {
                return hasGenericArguments ? "Vector_1" : "Vector";
            }

            if (MatchesRuntimeType(varType, "Vector128", "System.Runtime.Intrinsics.Vector128")) {
                return hasGenericArguments ? "Vector128_1" : "Vector128";
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
            string normalizedQualifiedTypeName = NormalizeQualifiedTypeName(varType.QualifiedTypeName);
            return string.Equals(typeName, shortTypeName, StringComparison.Ordinal) ||
                string.Equals(typeName, qualifiedTypeName, StringComparison.Ordinal) ||
                string.Equals(normalizedQualifiedTypeName, qualifiedTypeName, StringComparison.Ordinal);
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
        /// Determines whether one emitted file stem must include its namespace prefix so the same source type stays stable across independently converted project graphs on case-insensitive filesystems.
        /// </summary>
        /// <param name="conversionClass">Converted class whose emitted file stem is being resolved.</param>
        /// <param name="program">Program model used to inspect reachable generated classes.</param>
        /// <param name="emittedTypeName">Resolved emitted type name for the class.</param>
        /// <param name="qualifiedPrefix">Namespace-derived file stem prefix for the class.</param>
        /// <returns>True when the generated file stem should include the namespace-derived prefix; otherwise false.</returns>
        static bool ShouldQualifyFileStem(
            ConversionClass conversionClass,
            ConversionProgram program,
            string emittedTypeName,
            string qualifiedPrefix) {
            if (conversionClass == null || program == null || string.IsNullOrWhiteSpace(emittedTypeName) || string.IsNullOrWhiteSpace(qualifiedPrefix)) {
                return false;
            }

            bool hasCaseInsensitiveCollision = program.Classes.Any(candidate =>
                !ReferenceEquals(candidate, conversionClass) &&
                !candidate.IsNative &&
                string.Equals(candidate.GetEmittedTypeName(), emittedTypeName, StringComparison.OrdinalIgnoreCase));
            if (hasCaseInsensitiveCollision) {
                return true;
            }

            return StartsWithLowercaseLetter(emittedTypeName);
        }

        /// <summary>
        /// Determines whether one emitted C++ type name must include its namespace-derived prefix to avoid global identifier collisions.
        /// </summary>
        /// <param name="conversionClass">Converted class whose emitted type name is being resolved.</param>
        /// <param name="emittedTypeName">Resolved emitted type name before collision qualification.</param>
        /// <param name="qualifiedPrefix">Namespace-derived prefix for the converted class.</param>
        /// <returns>True when the emitted C++ type name should include the namespace-derived prefix; otherwise false.</returns>
        static bool ShouldQualifyEmittedTypeName(
            ConversionClass conversionClass,
            string emittedTypeName,
            string qualifiedPrefix) {
            if (conversionClass?.Program == null || string.IsNullOrWhiteSpace(emittedTypeName) || string.IsNullOrWhiteSpace(qualifiedPrefix)) {
                return false;
            }

            HashSet<string> collisions = conversionClass.Program.GetBaseEmittedTypeNameCollisions(GetBaseEmittedTypeName);
            return collisions.Contains(emittedTypeName);
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

            return conversionClass.GenericArgs == null || conversionClass.GenericArgs.Count == 0
                ? conversionClass.Name
                : $"{conversionClass.Name}_{conversionClass.GenericArgs.Count}";
        }

        /// <summary>
        /// Determines whether one emitted type name begins with a lowercase ASCII letter, which is the naming shape most likely to collide with a PascalCase sibling when generated by a separate project pass on Windows.
        /// </summary>
        /// <param name="emittedTypeName">Resolved emitted type name to inspect.</param>
        /// <returns>True when the emitted type name begins with a lowercase ASCII letter; otherwise false.</returns>
        static bool StartsWithLowercaseLetter(string emittedTypeName) {
            if (string.IsNullOrWhiteSpace(emittedTypeName)) {
                return false;
            }

            return emittedTypeName[0] >= 'a' && emittedTypeName[0] <= 'z';
        }

        /// <summary>
        /// Builds a namespace-derived prefix that keeps generated file names distinct when emitted type names collide by case only.
        /// </summary>
        /// <param name="conversionClass">Converted class whose source namespace should be encoded.</param>
        /// <returns>Sanitized namespace-derived prefix, or an empty string when no namespace is available.</returns>
        static string BuildQualifiedFileStemPrefix(ConversionClass conversionClass) {
            if (conversionClass?.TypeSymbol == null) {
                return string.Empty;
            }

            string namespaceName = conversionClass.TypeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            string namespacePrefix = string.IsNullOrWhiteSpace(namespaceName) || namespaceName == "<global namespace>"
                ? string.Empty
                : namespaceName
                .Replace("global::", string.Empty, StringComparison.Ordinal)
                .Replace('.', '_')
                .Replace(':', '_');

            string containingTypePrefix = BuildContainingTypePrefix(conversionClass.TypeSymbol.ContainingType);
            if (string.IsNullOrWhiteSpace(namespacePrefix)) {
                return containingTypePrefix;
            }

            if (string.IsNullOrWhiteSpace(containingTypePrefix)) {
                return namespacePrefix;
            }

            return $"{namespacePrefix}_{containingTypePrefix}";
        }

        /// <summary>
        /// Builds a stable underscore-delimited prefix from the containing type chain of a nested source type.
        /// </summary>
        /// <param name="containingTypeSymbol">Containing source type for the converted nested declaration.</param>
        /// <returns>Sanitized containing-type prefix, or an empty string when the declaration is top-level.</returns>
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
