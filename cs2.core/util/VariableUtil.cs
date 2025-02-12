using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;

namespace cs2.core {
    public static class VariableUtil {
        private static ConvertedVariableType HandleArrayType(SemanticModel semantic, ArrayTypeSyntax arrayType) {
            // Get the element type of the array (e.g., int, string, etc.)
            ConvertedVariableType elementType = GetVarType(arrayType.ElementType, semantic);

            // Handle multidimensional arrays
            foreach (var rankSpecifier in arrayType.RankSpecifiers) {
                int dimensions = rankSpecifier.Rank; // Rank is the number of commas + 1
                for (int i = 0; i < dimensions; i++) {
                    // Wrap the element type in a List<> or Array-like TypeScript representation
                    ConvertedVariableType arrayWrapper = new ConvertedVariableType(VariableDataType.Array, "Array");
                    arrayWrapper.GenericArgs.Add(elementType);
                    elementType = arrayWrapper; // Nest the previous element type
                }
            }

            return elementType;
        }

        private static ConvertedVariableType HandleTupleType(SemanticModel semantic, TupleTypeSyntax tupleType) {
            // Handle tuple types
            ConvertedVariableType tupleWrapper = new ConvertedVariableType(VariableDataType.Tuple);

            // Iterate over the elements in the tuple and get their types
            foreach (var element in tupleType.Elements) {
                ConvertedVariableType elementType = GetVarType(element.Type, semantic);
                tupleWrapper.GenericArgs.Add(elementType);
            }

            return tupleWrapper;
        }

        private static ConvertedVariableType HandleQualifiedName(QualifiedNameSyntax qualifiedName) {
            // Handle qualified name types, e.g., System.Collections.Generic.List
            string fullQualifiedName = GetQualifiedName(qualifiedName);

            // Parse the last part as the actual type and treat the previous part as a namespace/module.
            string typeName = qualifiedName.Right.ToString();

            ConvertedVariableType baseType = new ConvertedVariableType(GetVarDataType(typeName), typeName);
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
            } else if (equals.Value is MemberAccessExpressionSyntax member) {
                assigned = member.ToString();
            } else {
                Debugger.Break();
                throw new NotSupportedException();
            }

            return $" = {assigned}";
        }

        public static ConvertedVariableType GetVarType(string type) {
            ConvertedVariableType baseType = new ConvertedVariableType(GetVarDataType(type), type);

            return baseType;
        }

        public static ConvertedVariableType GetVarType(TypeSyntax type, SemanticModel semantic) {
            if (type is IdentifierNameSyntax) {
                IdentifierNameSyntax identifier = (IdentifierNameSyntax)type;

                string identifierName = identifier.ToString();
                ConvertedVariableType baseType = new ConvertedVariableType(GetVarDataType(identifierName), identifierName);

                return baseType;
            } else if (type is GenericNameSyntax) {
                GenericNameSyntax generic = (GenericNameSyntax)type;

                string identifierName = generic.Identifier.ToString();
                ConvertedVariableType baseType = new ConvertedVariableType(GetVarDataType(identifierName), identifierName);

                foreach (var genType in generic.TypeArgumentList.Arguments) {
                    baseType.GenericArgs.Add(GetVarType(genType, semantic));
                }

                return baseType;
            } else if (type is PredefinedTypeSyntax) {
                PredefinedTypeSyntax predefined = (PredefinedTypeSyntax)type;

                string predefinedName = predefined.ToString();
                var typeSymbol = semantic.GetTypeInfo(type).Type;
                var specialType = typeSymbol.SpecialType;

                VariableDataType dataType;
                switch (predefinedName) {
                    case "float":
                    case "double":
                    case "sbyte":
                    case "byte":
                    case "short":
                    case "ushort":
                    case "int":
                    case "uint":
                    case "long":
                    case "ulong":
                        dataType = VariableDataType.Number;
                        break;
                    case "bool":
                        dataType = VariableDataType.Boolean;
                        break;
                    case "char":
                        dataType = VariableDataType.String;
                        break;
                    case "string":
                        dataType = VariableDataType.String;
                        break;
                    case "void":
                        dataType = VariableDataType.Void;
                        break;
                    case "object":
                        dataType = VariableDataType.Object;
                        return new ConvertedVariableType(dataType, predefinedName);
                    default:
                        throw new ArgumentException();
                }

                string clrTypeName = GetClrTypeName(specialType);
                if (!string.IsNullOrEmpty(clrTypeName)) {
                    predefinedName = clrTypeName;
                }

                return new ConvertedVariableType(dataType, predefinedName);
            } else if (type is ArrayTypeSyntax array) {
                // Handle array types, including jagged and multidimensional arrays
                ConvertedVariableType arrType = HandleArrayType(semantic, array);

                if (arrType.ToString() == "Array<byte>") {
                    return new ConvertedVariableType(VariableDataType.Object, "Uint8Array");
                }

                return arrType;
            } else if (type is NullableTypeSyntax syntax) {
                ConvertedVariableType baseType = GetVarType(syntax.ElementType, semantic);
                baseType.IsNullable = true;
                return baseType;
            } else if (type is TupleTypeSyntax tuple) {
                // Handle tuple types
                return HandleTupleType(semantic, tuple);
            } else if (type is QualifiedNameSyntax qualifiedName) {
                // Handle qualified name types
                return HandleQualifiedName(qualifiedName);
            } else {
                Debugger.Break();
            }

            return new ConvertedVariableType(VariableDataType.Object);
        }

        static string GetClrTypeName(SpecialType specialType) {
            return specialType switch {
                SpecialType.System_Int32 => "Int32",
                SpecialType.System_UInt32 => "UInt32",
                SpecialType.System_Single => "Single",
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
                    return VariableDataType.Void;
                case "bool":
                    return VariableDataType.Boolean;
                case "float":
                case "double":
                case "byte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                    return VariableDataType.Number;
                case "string":
                case "char":
                    return VariableDataType.String;

                case "List":
                    return VariableDataType.List;
                case "Dictionary":
                    return VariableDataType.Dictionary;


                default:
                    return VariableDataType.Object;
            }
        }
    }
}
