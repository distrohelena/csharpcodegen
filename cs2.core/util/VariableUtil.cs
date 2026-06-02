using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;
using System.Xml.Linq;

namespace cs2.core {
    public static class VariableUtil {
        private static VariableType HandleArrayType(SemanticModel semantic, ArrayTypeSyntax arrayType) {
            // Get the element type of the array (e.g., int, string, etc.)
            VariableType elementType = GetVarType(arrayType.ElementType, semantic);

            // Handle multidimensional arrays
            foreach (var rankSpecifier in arrayType.RankSpecifiers) {
                int dimensions = rankSpecifier.Rank; // Rank is the number of commas + 1
                for (int i = 0; i < dimensions; i++) {
                    // Wrap the element type in a List<> or Array-like TypeScript representation
                    VariableType arrayWrapper = new VariableType(VariableDataType.Array, "Array");
                    arrayWrapper.GenericArgs.Add(elementType);
                    elementType = arrayWrapper; // Nest the previous element type
                }
            }

            return elementType;
        }

        private static VariableType HandleTupleType(SemanticModel semantic, TupleTypeSyntax tupleType) {
            // Handle tuple types
            VariableType tupleWrapper = new VariableType(VariableDataType.Tuple);

            // Iterate over the elements in the tuple and get their types
            foreach (var element in tupleType.Elements) {
                VariableType elementType = GetVarType(element.Type, semantic);
                tupleWrapper.GenericArgs.Add(elementType);

                if (!string.IsNullOrWhiteSpace(element.Identifier.Text)) {
                    tupleWrapper.Args.Add(new VariableType(VariableDataType.Unknown, element.Identifier.Text));
                }
            }

            return tupleWrapper;
        }

        /// <summary>
        /// Creates a VariableType and applies Roslyn semantic flags that affect backend lowering.
        /// </summary>
        /// <param name="dataType">Abstract source-side variable category.</param>
        /// <param name="typeName">Source type name associated with the variable shape.</param>
        /// <param name="typeSymbol">Roslyn type symbol that supplies semantic flags.</param>
        /// <returns>The initialized VariableType.</returns>
        static VariableType CreateVariableType(VariableDataType dataType, string typeName, ITypeSymbol typeSymbol) {
            VariableType variableType = new VariableType(dataType, typeName);
            ApplyTypeSymbolMetadata(variableType, typeSymbol);
            return variableType;
        }

        /// <summary>
        /// Applies Roslyn semantic flags that affect backend pointer-versus-value lowering.
        /// </summary>
        /// <param name="variableType">Variable type metadata to update.</param>
        /// <param name="typeSymbol">Roslyn type symbol that supplies semantic flags.</param>
        static void ApplyTypeSymbolMetadata(VariableType variableType, ITypeSymbol typeSymbol) {
            if (variableType == null || typeSymbol == null) {
                return;
            }

            variableType.IsValueType = typeSymbol.IsValueType;
            variableType.QualifiedTypeName = NormalizeQualifiedTypeName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            if (typeSymbol.TypeKind == TypeKind.Enum) {
                variableType.IsEnum = true;
            }
        }

        /// <summary>
        /// Applies ref-kind metadata to one variable type so nested generic signatures can preserve by-reference shapes.
        /// </summary>
        /// <param name="variableType">Variable type metadata to update.</param>
        /// <param name="refKind">Roslyn ref-kind associated with the use site.</param>
        static void ApplyRefKindMetadata(VariableType variableType, RefKind refKind) {
            if (variableType == null) {
                return;
            }

            if (refKind == RefKind.Ref || refKind == RefKind.Out) {
                variableType.IsReference = true;
                variableType.IsConstReference = false;
                return;
            }

            if (refKind == RefKind.In || refKind == RefKind.RefReadOnlyParameter) {
                variableType.IsConstReference = true;
                variableType.IsReference = false;
            }
        }

        /// <summary>
        /// Normalizes Roslyn fully-qualified type display strings into a caller-stable lookup key by removing the global alias prefix.
        /// </summary>
        /// <param name="qualifiedTypeName">Fully-qualified Roslyn display string.</param>
        /// <returns>Normalized fully-qualified type name without a global alias prefix.</returns>
        static string NormalizeQualifiedTypeName(string qualifiedTypeName) {
            if (string.IsNullOrWhiteSpace(qualifiedTypeName)) {
                return string.Empty;
            }

            return qualifiedTypeName.StartsWith("global::", StringComparison.Ordinal)
                ? qualifiedTypeName["global::".Length..]
                : qualifiedTypeName;
        }

        private static VariableType HandleQualifiedName(QualifiedNameSyntax qualifiedName, ITypeSymbol resolvedTypeSymbol) {
            // Handle qualified name types, e.g., System.Collections.Generic.List
            string fullQualifiedName = GetQualifiedName(qualifiedName);

            // Parse the last part as the actual type and treat the previous part as a namespace/module.
            string typeName = qualifiedName.Right.ToString();

            VariableType baseType = CreateVariableType(GetVarDataType(typeName), typeName, resolvedTypeSymbol);
            baseType.TypeName = typeName;

            return baseType;
        }

        private static string GetQualifiedName(QualifiedNameSyntax qualifiedName) {
            // Recursively get the full qualified name
            if (qualifiedName.Left is QualifiedNameSyntax leftQualifiedName) {
                return GetQualifiedName(leftQualifiedName) + "." + qualifiedName.Right.ToString();
            }
            return qualifiedName.Left.ToString() + "." + qualifiedName.Right.ToString();
        }

        public static string ProcessAssignment(EqualsValueClauseSyntax equals) {
            string assigned;
            if (equals.Value is DefaultExpressionSyntax def) {
                // TODO: check if type is enum
                assigned = $"new {def.Type.ToString()}()";
            } else if (equals.Value is LiteralExpressionSyntax literal) {
                assigned = literal.Token.ToString();
            } else if (equals.Value is IdentifierNameSyntax identifier) {
                assigned = identifier.ToString();
            } else if (equals.Value is MemberAccessExpressionSyntax member) {
                assigned = member.ToString();
            } else if (equals.Value is PrefixUnaryExpressionSyntax unary) {
                assigned = unary.ToString();
            } else {
                assigned = equals.Value.ToString();
            }

            return $" = {assigned}";
        }

        public static VariableType GetVarType(string type) {
            string normalizedTypeName = type?.Trim() ?? string.Empty;

            if (normalizedTypeName.Length == 0) {
                return new VariableType(VariableDataType.Unknown);
            }

            if (TryStripPointerSuffix(normalizedTypeName, out string pointedTypeName)) {
                VariableType pointedType = GetVarType(pointedTypeName);
                pointedType.IsPointer = true;
                return pointedType;
            }

            if (normalizedTypeName.StartsWith("[", StringComparison.Ordinal) &&
                normalizedTypeName.EndsWith("]", StringComparison.Ordinal)) {
                string tupleArgumentText = normalizedTypeName[1..^1];
                VariableType tupleType = new VariableType(VariableDataType.Tuple);

                foreach (string tupleArgument in SplitGenericArgumentList(tupleArgumentText)) {
                    tupleType.GenericArgs.Add(GetVarType(tupleArgument));
                }

                return tupleType;
            }

            if (normalizedTypeName.StartsWith("(", StringComparison.Ordinal) &&
                normalizedTypeName.EndsWith(")", StringComparison.Ordinal)) {
                string tupleArgumentText = normalizedTypeName[1..^1];
                VariableType tupleType = new VariableType(VariableDataType.Tuple);

                foreach (string tupleElement in SplitTupleElementList(tupleArgumentText)) {
                    tupleType.GenericArgs.Add(GetVarType(StripTupleElementName(tupleElement)));
                }

                return tupleType;
            }

            if (normalizedTypeName.Length > 1 &&
                normalizedTypeName.EndsWith("?", StringComparison.Ordinal) &&
                !string.Equals(normalizedTypeName, "?", StringComparison.Ordinal)) {
                VariableType nullableBaseType = GetVarType(normalizedTypeName[..^1]);
                nullableBaseType.IsNullable = true;
                return nullableBaseType;
            }

            int genericStart = FindTopLevelGenericStart(normalizedTypeName);
            if (genericStart >= 0 && normalizedTypeName.EndsWith(">", StringComparison.Ordinal)) {
                string genericBaseName = normalizedTypeName[..genericStart].Trim();
                string genericArgumentText = normalizedTypeName[(genericStart + 1)..^1];
                string normalizedGenericBaseName = NormalizeLeafTypeName(genericBaseName);

                if ((normalizedGenericBaseName == "Nullable" || genericBaseName == "System.Nullable") &&
                    SplitGenericArgumentList(genericArgumentText).Take(2).Count() == 1) {
                    VariableType nullableBaseType = GetVarType(SplitGenericArgumentList(genericArgumentText).First());
                    nullableBaseType.IsNullable = true;
                    return nullableBaseType;
                }

                string leafTypeName = NormalizeLeafTypeName(genericBaseName);
                VariableType genericType = new VariableType(GetVarDataType(leafTypeName), leafTypeName);

                foreach (string genericArgument in SplitGenericArgumentList(genericArgumentText)) {
                    genericType.GenericArgs.Add(GetVarType(genericArgument));
                }

                return genericType;
            }

            return new VariableType(GetVarDataType(normalizedTypeName), normalizedTypeName);
        }

        static int FindTopLevelGenericStart(string typeName) {
            for (int index = 0; index < typeName.Length; index++) {
                if (typeName[index] == '<') {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Detects one top-level unsafe pointer suffix and returns the underlying pointed element type text.
        /// </summary>
        /// <param name="typeName">Source type name to inspect.</param>
        /// <param name="pointedTypeName">Underlying element type name when a pointer suffix is present.</param>
        /// <returns><c>true</c> when the type name ends with one top-level pointer suffix; otherwise <c>false</c>.</returns>
        static bool TryStripPointerSuffix(string typeName, out string pointedTypeName) {
            pointedTypeName = string.Empty;
            if (string.IsNullOrWhiteSpace(typeName) || !typeName.EndsWith("*", StringComparison.Ordinal)) {
                return false;
            }

            int genericDepth = 0;
            int tupleDepth = 0;
            int arrayDepth = 0;
            for (int index = 0; index < typeName.Length - 1; index++) {
                char currentCharacter = typeName[index];
                if (currentCharacter == '<') {
                    genericDepth++;
                    continue;
                }

                if (currentCharacter == '>') {
                    genericDepth--;
                    continue;
                }

                if (currentCharacter == '(') {
                    tupleDepth++;
                    continue;
                }

                if (currentCharacter == ')') {
                    tupleDepth--;
                    continue;
                }

                if (currentCharacter == '[') {
                    arrayDepth++;
                    continue;
                }

                if (currentCharacter == ']') {
                    arrayDepth--;
                }
            }

            if (genericDepth != 0 || tupleDepth != 0 || arrayDepth != 0) {
                return false;
            }

            pointedTypeName = typeName[..^1].TrimEnd();
            return pointedTypeName.Length > 0;
        }

        static string NormalizeLeafTypeName(string typeName) {
            int separatorIndex = typeName.LastIndexOf('.');
            if (separatorIndex < 0 || separatorIndex == typeName.Length - 1) {
                return typeName;
            }

            return typeName[(separatorIndex + 1)..];
        }

        static IEnumerable<string> SplitGenericArgumentList(string genericArgumentText) {
            List<string> genericArguments = new List<string>();
            int startIndex = 0;
            int genericDepth = 0;
            int tupleDepth = 0;
            int arrayDepth = 0;

            for (int index = 0; index < genericArgumentText.Length; index++) {
                char currentCharacter = genericArgumentText[index];

                if (currentCharacter == '<') {
                    genericDepth++;
                    continue;
                }

                if (currentCharacter == '>') {
                    genericDepth--;
                    continue;
                }

                if (currentCharacter == '(') {
                    tupleDepth++;
                    continue;
                }

                if (currentCharacter == ')') {
                    tupleDepth--;
                    continue;
                }

                if (currentCharacter == '[') {
                    arrayDepth++;
                    continue;
                }

                if (currentCharacter == ']') {
                    arrayDepth--;
                    continue;
                }

                if (currentCharacter == ',' && genericDepth == 0 && tupleDepth == 0 && arrayDepth == 0) {
                    genericArguments.Add(genericArgumentText[startIndex..index].Trim());
                    startIndex = index + 1;
                }
            }

            string trailingArgument = genericArgumentText[startIndex..].Trim();
            if (trailingArgument.Length > 0) {
                genericArguments.Add(trailingArgument);
            }

            return genericArguments;
        }

        static IEnumerable<string> SplitTupleElementList(string tupleElementText) {
            List<string> tupleElements = new List<string>();
            int startIndex = 0;
            int genericDepth = 0;
            int tupleDepth = 0;
            int arrayDepth = 0;

            for (int index = 0; index < tupleElementText.Length; index++) {
                char currentCharacter = tupleElementText[index];

                if (currentCharacter == '<') {
                    genericDepth++;
                    continue;
                }

                if (currentCharacter == '>') {
                    genericDepth--;
                    continue;
                }

                if (currentCharacter == '(') {
                    tupleDepth++;
                    continue;
                }

                if (currentCharacter == ')') {
                    tupleDepth--;
                    continue;
                }

                if (currentCharacter == '[') {
                    arrayDepth++;
                    continue;
                }

                if (currentCharacter == ']') {
                    arrayDepth--;
                    continue;
                }

                if (currentCharacter == ',' && genericDepth == 0 && tupleDepth == 0 && arrayDepth == 0) {
                    tupleElements.Add(tupleElementText[startIndex..index].Trim());
                    startIndex = index + 1;
                }
            }

            tupleElements.Add(tupleElementText[startIndex..].Trim());
            return tupleElements;
        }

        static string StripTupleElementName(string tupleElement) {
            int genericDepth = 0;
            int tupleDepth = 0;
            int arrayDepth = 0;

            for (int index = tupleElement.Length - 1; index >= 0; index--) {
                char currentCharacter = tupleElement[index];

                if (currentCharacter == '>') {
                    genericDepth++;
                    continue;
                }

                if (currentCharacter == '<') {
                    genericDepth--;
                    continue;
                }

                if (currentCharacter == ')') {
                    tupleDepth++;
                    continue;
                }

                if (currentCharacter == '(') {
                    tupleDepth--;
                    continue;
                }

                if (currentCharacter == ']') {
                    arrayDepth++;
                    continue;
                }

                if (currentCharacter == '[') {
                    arrayDepth--;
                    continue;
                }

                if (currentCharacter == ' ' && genericDepth == 0 && tupleDepth == 0 && arrayDepth == 0) {
                    return tupleElement[..index].Trim();
                }
            }

            return tupleElement.Trim();
        }

        public static VariableType GetVarType(TypeSyntax type, SemanticModel semantic) {
            if (type == null) {
                return new VariableType(VariableDataType.Unknown);
            }

            if (semantic == null ||
                !ReferenceEquals(type.SyntaxTree, semantic.SyntaxTree)) {
                return GetDetachedSyntaxVarType(type);
            }

            var typeInfo = semantic.GetTypeInfo(type);
            ITypeSymbol resolvedTypeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;

            if (resolvedTypeSymbol is IFunctionPointerTypeSymbol functionPointerTypeSymbol) {
                return GetVarType(functionPointerTypeSymbol);
            }

            if (resolvedTypeSymbol is ITypeParameterSymbol typeParameterSymbol) {
                VariableType genericParameterType = CreateVariableType(GetVarDataType(typeParameterSymbol.Name), typeParameterSymbol.Name, typeParameterSymbol);
                genericParameterType.IsGenericParameter = true;
                return genericParameterType;
            }

            if (resolvedTypeSymbol?.TypeKind == TypeKind.Enum) {
                VariableType enumType = CreateVariableType(GetVarDataType(resolvedTypeSymbol.Name), resolvedTypeSymbol.Name, resolvedTypeSymbol);
                enumType.IsEnum = true;
                return enumType;
            }

            if (type is PointerTypeSyntax pointerType) {
                VariableType pointedType = GetVarType(pointerType.ElementType, semantic);
                pointedType.IsPointer = true;
                return pointedType;
            }

            if (type is IdentifierNameSyntax) {
                IdentifierNameSyntax identifier = (IdentifierNameSyntax)type;

                string identifierName = identifier.ToString();

                if (identifierName == "var") {
                    ITypeSymbol inferredType = typeInfo.ConvertedType ?? typeInfo.Type;
                    if (inferredType != null) {
                        return GetVarType(inferredType);
                    }
                    string typeInfoName = typeInfo.Type?.Name ?? "object";
                    return CreateVariableType(GetVarDataType(typeInfoName), typeInfoName, resolvedTypeSymbol);
                } else {
                    if (resolvedTypeSymbol != null) {
                        return GetVarType(resolvedTypeSymbol);
                    }

                    return CreateVariableType(GetVarDataType(identifierName), identifierName, resolvedTypeSymbol);
                }
            } else if (type is GenericNameSyntax) {
                GenericNameSyntax generic = (GenericNameSyntax)type;

                string identifierName = generic.Identifier.ToString();

                if (identifierName == "Nullable" && generic.TypeArgumentList.Arguments.Count == 1) {
                    VariableType nullableBaseType = GetVarType(generic.TypeArgumentList.Arguments[0], semantic);
                    nullableBaseType.IsNullable = true;
                    return nullableBaseType;
                }

                if (resolvedTypeSymbol is INamedTypeSymbol resolvedNamedGenericTypeSymbol) {
                    return GetVarType(resolvedNamedGenericTypeSymbol);
                }

                VariableType baseType = CreateVariableType(GetVarDataType(identifierName), identifierName, resolvedTypeSymbol);

                foreach (var genType in generic.TypeArgumentList.Arguments) {
                    baseType.GenericArgs.Add(GetVarType(genType, semantic));
                }

                return baseType;
            } else if (type is PredefinedTypeSyntax) {
                PredefinedTypeSyntax predefined = (PredefinedTypeSyntax)type;

                string predefinedName = predefined.ToString();
                ITypeSymbol typeSymbol = semantic.GetTypeInfo(type).Type ?? resolvedTypeSymbol;
                SpecialType specialType = typeSymbol?.SpecialType ?? SpecialType.None;

                VariableDataType dataType;
                switch (predefinedName) {
                    case "float":
                        dataType = VariableDataType.Single;
                        break;
                    case "double":
                        dataType = VariableDataType.Double;
                        break;
                    case "decimal":
                        dataType = VariableDataType.Double;
                        break;
                    case "sbyte":
                        dataType = VariableDataType.Int8;
                        break;
                    case "byte":
                        dataType = VariableDataType.UInt8;
                        break;
                    case "short":
                        dataType = VariableDataType.Int16;
                        break;
                    case "ushort":
                        dataType = VariableDataType.UInt16;
                        break;
                    case "int":
                        dataType = VariableDataType.Int32;
                        break;
                    case "uint":
                        dataType = VariableDataType.UInt32;
                        break;
                    case "long":
                        dataType = VariableDataType.Int64;
                        break;
                    case "ulong":
                        dataType = VariableDataType.UInt64;
                        break;
                    case "bool":
                        dataType = VariableDataType.Boolean;
                        break;
                    case "char":
                        dataType = VariableDataType.Char;
                        break;
                    case "string":
                        dataType = VariableDataType.String;
                        break;
                    case "void":
                        dataType = VariableDataType.Void;
                        break;
                    case "object":
                        dataType = VariableDataType.Object;
                        return CreateVariableType(dataType, predefinedName, resolvedTypeSymbol);
                    default:
                        throw new ArgumentException();
                }

                string clrTypeName = GetClrTypeName(specialType);
                if (!string.IsNullOrEmpty(clrTypeName)) {
                    predefinedName = clrTypeName;
                }

                return CreateVariableType(dataType, predefinedName, resolvedTypeSymbol);
            } else if (type is ArrayTypeSyntax array) {
                // Handle array types, including jagged and multidimensional arrays
                VariableType arrType = HandleArrayType(semantic, array);

                return arrType;
            } else if (type is NullableTypeSyntax syntax) {
                VariableType baseType = GetVarType(syntax.ElementType, semantic);
                baseType.IsNullable = true;
                return baseType;
            } else if (type is TupleTypeSyntax tuple) {
                // Handle tuple types
                return HandleTupleType(semantic, tuple);
            } else if (type is QualifiedNameSyntax qualifiedName) {
                // Handle qualified name types
                if (resolvedTypeSymbol is INamedTypeSymbol resolvedQualifiedTypeSymbol) {
                    return GetVarType(resolvedQualifiedTypeSymbol);
                }

                return HandleQualifiedName(qualifiedName, resolvedTypeSymbol);
            } else {
                Debugger.Break();
            }

            return new VariableType(VariableDataType.Object);
        }

        static VariableType GetDetachedSyntaxVarType(TypeSyntax type) {
            if (type is PointerTypeSyntax pointerType) {
                VariableType pointedType = GetDetachedSyntaxVarType(pointerType.ElementType);
                pointedType.IsPointer = true;
                return pointedType;
            }

            if (type is NullableTypeSyntax nullableType) {
                VariableType nullableBaseType = GetDetachedSyntaxVarType(nullableType.ElementType);
                nullableBaseType.IsNullable = true;
                return nullableBaseType;
            }

            return GetVarType(type.ToString());
        }

        /// <summary>
        /// Resolves a VariableType from a Roslyn type symbol.
        /// </summary>
        /// <param name="typeSymbol">The Roslyn type symbol to convert.</param>
        /// <returns>The resolved VariableType.</returns>
        public static VariableType GetVarType(ITypeSymbol typeSymbol) {
            if (typeSymbol == null) {
                return new VariableType(VariableDataType.Object);
            }

            if (typeSymbol is IArrayTypeSymbol arraySymbol) {
                VariableType elementType = GetVarType(arraySymbol.ElementType);

                for (int i = 0; i < arraySymbol.Rank; i++) {
                    VariableType arrayWrapper = new VariableType(VariableDataType.Array, "Array");
                    arrayWrapper.GenericArgs.Add(elementType);
                    elementType = arrayWrapper;
                }

                return elementType;
            }

            if (typeSymbol is IPointerTypeSymbol pointerTypeSymbol) {
                VariableType pointedType = GetVarType(pointerTypeSymbol.PointedAtType);
                pointedType.IsPointer = true;
                return pointedType;
            }

            if (typeSymbol is IFunctionPointerTypeSymbol functionPointerTypeSymbol) {
                VariableType functionPointerType = CreateVariableType(VariableDataType.Callback, "FunctionPointer", functionPointerTypeSymbol);
                VariableType returnType = GetVarType(functionPointerTypeSymbol.Signature.ReturnType);
                if (functionPointerTypeSymbol.Signature.ReturnsByRefReadonly) {
                    returnType.IsConstReference = true;
                    returnType.IsReference = false;
                } else if (functionPointerTypeSymbol.Signature.ReturnsByRef) {
                    returnType.IsReference = true;
                    returnType.IsConstReference = false;
                }

                functionPointerType.GenericArgs.Add(returnType);

                foreach (IParameterSymbol functionPointerParameter in functionPointerTypeSymbol.Signature.Parameters) {
                    VariableType parameterType = GetVarType(functionPointerParameter.Type);
                    ApplyRefKindMetadata(parameterType, functionPointerParameter.RefKind);
                    functionPointerType.GenericArgs.Add(parameterType);
                }

                return functionPointerType;
            }

            if (typeSymbol is ITypeParameterSymbol parameterSymbol) {
                VariableType genericParameterType = new VariableType(VariableDataType.Object, parameterSymbol.Name);
                genericParameterType.IsGenericParameter = true;
                return genericParameterType;
            }

            if (typeSymbol is INamedTypeSymbol namedTypeSymbol) {
                if (namedTypeSymbol.IsTupleType) {
                    VariableType tupleType = new VariableType(VariableDataType.Tuple);
                    foreach (IFieldSymbol tupleElement in namedTypeSymbol.TupleElements) {
                        tupleType.GenericArgs.Add(GetVarType(tupleElement.Type));
                        tupleType.Args.Add(new VariableType(VariableDataType.Unknown, tupleElement.Name));
                    }

                    return tupleType;
                }

                if (namedTypeSymbol.Name == "Nullable" &&
                    namedTypeSymbol.ContainingNamespace?.ToDisplayString() == "System" &&
                    namedTypeSymbol.TypeArguments.Length == 1) {
                    VariableType nullableBaseType = GetVarType(namedTypeSymbol.TypeArguments[0]);
                    nullableBaseType.IsNullable = true;
                    return nullableBaseType;
                }

                string typeName = namedTypeSymbol.Name;
                VariableDataType dataType = GetVarDataType(typeName);

                if (namedTypeSymbol.SpecialType != SpecialType.None) {
                    switch (namedTypeSymbol.SpecialType) {
                        case SpecialType.System_Int16:
                            dataType = VariableDataType.Int16;
                            typeName = "Int16";
                            break;
                        case SpecialType.System_UInt16:
                            dataType = VariableDataType.UInt16;
                            typeName = "UInt16";
                            break;
                        case SpecialType.System_Int32:
                            dataType = VariableDataType.Int32;
                            typeName = "Int32";
                            break;
                        case SpecialType.System_UInt32:
                            dataType = VariableDataType.UInt32;
                            typeName = "UInt32";
                            break;
                        case SpecialType.System_Int64:
                            dataType = VariableDataType.Int64;
                            typeName = "Int64";
                            break;
                        case SpecialType.System_UInt64:
                            dataType = VariableDataType.UInt64;
                            typeName = "UInt64";
                            break;
                        case SpecialType.System_Single:
                            dataType = VariableDataType.Single;
                            typeName = "Single";
                            break;
                        case SpecialType.System_Double:
                            dataType = VariableDataType.Double;
                            typeName = "double";
                            break;
                        case SpecialType.System_SByte:
                            dataType = VariableDataType.Int8;
                            typeName = "sbyte";
                            break;
                        case SpecialType.System_Byte:
                            dataType = VariableDataType.UInt8;
                            typeName = "byte";
                            break;
                        case SpecialType.System_Boolean:
                            dataType = VariableDataType.Boolean;
                            typeName = "Boolean";
                            break;
                        case SpecialType.System_Char:
                            dataType = VariableDataType.Char;
                            typeName = "char";
                            break;
                        case SpecialType.System_String:
                            dataType = VariableDataType.String;
                            typeName = "string";
                            break;
                        case SpecialType.System_Object:
                            dataType = VariableDataType.Object;
                            typeName = "object";
                            break;
                        case SpecialType.System_Void:
                            dataType = VariableDataType.Void;
                            typeName = "void";
                            break;
                    }
                }

                VariableType baseType = CreateVariableType(dataType, typeName, namedTypeSymbol);
                if (namedTypeSymbol.TypeKind == TypeKind.Enum) {
                    baseType.IsEnum = true;
                }

                if (namedTypeSymbol.TypeArguments.Length > 0) {
                    foreach (var typeArgument in namedTypeSymbol.TypeArguments) {
                        baseType.GenericArgs.Add(GetVarType(typeArgument));
                    }
                }

                return baseType;
            }

            return CreateVariableType(VariableDataType.Object, typeSymbol.Name, typeSymbol);
        }

        static string GetClrTypeName(SpecialType specialType) {
            return specialType switch {
                SpecialType.System_Int16 => "Int16",
                SpecialType.System_UInt16 => "UInt16",
                SpecialType.System_Int32 => "Int32",
                SpecialType.System_UInt32 => "UInt32",
                SpecialType.System_Single => "Single",
                SpecialType.System_Decimal => "Decimal",
                SpecialType.System_Int64 => "Int64",
                SpecialType.System_UInt64 => "UInt64",
                SpecialType.System_Boolean => "Boolean",
                _ => null
            };
        }

        public static VariableDataType GetVarDataType(string txt) {
            if (!string.IsNullOrEmpty(txt) && txt.Contains("=>")) {
                return VariableDataType.Callback;
            }

            switch (txt) {
                case "int[]":
                case "char[]":
                case "byte[]":
                case "float[]":
                    return VariableDataType.Array;

                case "void":
                case "Void":
                    return VariableDataType.Void;
                case "bool":
                case "Boolean":
                    return VariableDataType.Boolean;
                case "float":
                case "Single":
                    return VariableDataType.Single;
                case "double":
                case "decimal":
                case "Decimal":
                case "Double":
                    return VariableDataType.Double;
                case "byte":
                case "Byte":
                    return VariableDataType.UInt8;
                case "sbyte":
                case "SByte":
                    return VariableDataType.Int8;
                case "short":
                case "Int16":
                    return VariableDataType.Int16;
                case "ushort":
                case "UInt16":
                    return VariableDataType.UInt16;
                case "int":
                case "Int32":
                    return VariableDataType.Int32;
                case "uint":
                case "UInt32":
                    return VariableDataType.UInt32;
                case "long":
                case "Int64":
                    return VariableDataType.Int64;
                case "ulong":
                case "UInt64":
                    return VariableDataType.UInt64;
                case "string":
                case "String":
                    return VariableDataType.String;
                case "char":
                case "Char":
                    return VariableDataType.Char;
                case "object":
                case "Object":
                case "nint":
                case "nuint":
                    return VariableDataType.Object;

                case "List":
                case "IReadOnlyList":
                case "ICollection":
                case "IReadOnlyCollection":
                case "IEnumerable":
                    return VariableDataType.List;
                case "Stack":
                case "IReadOnlySet":
                    return VariableDataType.Object;
                case "Dictionary":
                case "IDictionary":
                case "IReadOnlyDictionary":
                    return VariableDataType.Dictionary;


                default:
                    return VariableDataType.Object;
            }
        }
    }
}
