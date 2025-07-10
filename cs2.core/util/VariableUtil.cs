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
            }

            return tupleWrapper;
        }

        private static VariableType HandleQualifiedName(QualifiedNameSyntax qualifiedName) {
            // Handle qualified name types, e.g., System.Collections.Generic.List
            string fullQualifiedName = GetQualifiedName(qualifiedName);

            // Parse the last part as the actual type and treat the previous part as a namespace/module.
            string typeName = qualifiedName.Right.ToString();

            VariableType baseType = new VariableType(GetVarDataType(typeName), typeName);
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
            } else if (equals.Value is PrefixUnaryExpressionSyntax unary) {
                assigned = unary.ToString();
            } else {
                Debugger.Break();
                throw new NotSupportedException();
            }

            return $" = {assigned}";
        }

        public static VariableType GetVarType(string type) {
            VariableType baseType = new VariableType(GetVarDataType(type), type);

            return baseType;
        }

        public static VariableType GetVarType(TypeSyntax type, SemanticModel semantic) {
            var typeInfo = semantic.GetTypeInfo(type);

            if (type is IdentifierNameSyntax) {
                IdentifierNameSyntax identifier = (IdentifierNameSyntax)type;

                string identifierName = identifier.ToString();

                if (identifierName == "var") {
                    string typeInfoName = typeInfo.Type.Name;
                    return new VariableType(GetVarDataType(typeInfoName), typeInfoName);
                } else {
                    return new VariableType(GetVarDataType(identifierName), identifierName);
                }
            } else if (type is GenericNameSyntax) {
                GenericNameSyntax generic = (GenericNameSyntax)type;

                string identifierName = generic.Identifier.ToString();
                VariableType baseType = new VariableType(GetVarDataType(identifierName), identifierName);

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
                        dataType = VariableDataType.Single;
                        break;
                    case "double":
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
                        return new VariableType(dataType, predefinedName);
                    default:
                        throw new ArgumentException();
                }

                string clrTypeName = GetClrTypeName(specialType);
                if (!string.IsNullOrEmpty(clrTypeName)) {
                    predefinedName = clrTypeName;
                }

                return new VariableType(dataType, predefinedName);
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
                return HandleQualifiedName(qualifiedName);
            } else {
                Debugger.Break();
            }

            return new VariableType(VariableDataType.Object);
        }

        static string GetClrTypeName(SpecialType specialType) {
            return specialType switch {
                SpecialType.System_Int16 => "Int16",
                SpecialType.System_UInt16 => "UInt16",
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
                    return VariableDataType.Single;
                case "double":
                    return VariableDataType.Double;
                case "byte":
                    return VariableDataType.UInt8;
                case "sbyte":
                    return VariableDataType.Int8;
                case "short":
                    return VariableDataType.Int16;
                case "ushort":
                    return VariableDataType.UInt16;
                case "int":
                    return VariableDataType.Int32;
                case "uint":
                    return VariableDataType.UInt32;
                case "long":
                    return VariableDataType.Int64;
                case "ulong":
                    return VariableDataType.UInt64;
                case "string":
                    return VariableDataType.String;
                case "char":
                    return VariableDataType.Char;

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
