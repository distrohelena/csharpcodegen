using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace cs2.core {
    public class ConversionPreProcessor {
        /// <summary>
        /// Determines whether a symbol is annotated to force async emission in TypeScript.
        /// </summary>
        /// <param name="symbol">Symbol to inspect for attributes.</param>
        /// <returns>True when the TypeScript async attribute is present.</returns>
        static bool HasTypeScriptAsyncAttribute(ISymbol symbol) {
            if (symbol == null) {
                return false;
            }

            foreach (AttributeData attribute in symbol.GetAttributes()) {
                INamedTypeSymbol attributeType = attribute.AttributeClass;
                if (attributeType == null) {
                    continue;
                }

                string name = attributeType.Name;
                if (string.Equals(name, "TypeScriptAsyncAttribute", StringComparison.Ordinal) ||
                    string.Equals(name, "TypeScriptAsync", StringComparison.Ordinal)) {
                    return true;
                }
            }

            return false;
        }

        static bool TryResolveNativeFreeFunctionMetadata(IMethodSymbol methodSymbol, out string functionName, out string includePath) {
            functionName = string.Empty;
            includePath = string.Empty;
            if (methodSymbol == null) {
                return false;
            }

            foreach (AttributeData attribute in methodSymbol.GetAttributes()) {
                INamedTypeSymbol attributeType = attribute.AttributeClass;
                if (attributeType == null) {
                    continue;
                }

                string attributeName = attributeType.Name;
                if (!string.Equals(attributeName, "NativeFreeFunctionAttribute", StringComparison.Ordinal) &&
                    !string.Equals(attributeName, "NativeFreeFunction", StringComparison.Ordinal)) {
                    continue;
                }

                if (attribute.ConstructorArguments.Length >= 1) {
                    functionName = attribute.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
                }
                if (attribute.ConstructorArguments.Length >= 2) {
                    includePath = attribute.ConstructorArguments[1].Value?.ToString() ?? string.Empty;
                }

                return !string.IsNullOrWhiteSpace(functionName);
            }

            return false;
        }

        static bool TryResolveNativeFreeFunctionMetadata(MethodDeclarationSyntax methodDeclaration, out string functionName, out string includePath) {
            functionName = string.Empty;
            includePath = string.Empty;
            if (methodDeclaration == null) {
                return false;
            }

            foreach (AttributeListSyntax attributeList in methodDeclaration.AttributeLists) {
                foreach (AttributeSyntax attribute in attributeList.Attributes) {
                    string attributeName = attribute.Name.ToString();
                    if (!string.Equals(attributeName, "NativeFreeFunction", StringComparison.Ordinal) &&
                        !string.Equals(attributeName, "NativeFreeFunctionAttribute", StringComparison.Ordinal)) {
                        continue;
                    }

                    if (attribute.ArgumentList?.Arguments.Count >= 1) {
                        functionName = TryReadStringLiteral(attribute.ArgumentList.Arguments[0].Expression);
                    }
                    if (attribute.ArgumentList?.Arguments.Count >= 2) {
                        includePath = TryReadStringLiteral(attribute.ArgumentList.Arguments[1].Expression);
                    }

                    return !string.IsNullOrWhiteSpace(functionName);
                }
            }

            return false;
        }

        static string TryReadStringLiteral(ExpressionSyntax expression) {
            if (expression is LiteralExpressionSyntax literalExpression &&
                literalExpression.IsKind(SyntaxKind.StringLiteralExpression)) {
                return literalExpression.Token.ValueText;
            }

            return string.Empty;
        }

        static bool TryGetStructLayout(INamedTypeSymbol typeSymbol, out LayoutKind layoutKind, out int pack, out int size) {
            layoutKind = LayoutKind.Auto;
            pack = 0;
            size = 0;
            if (typeSymbol == null) {
                return false;
            }

            foreach (AttributeData attribute in typeSymbol.GetAttributes()) {
                if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.Runtime.InteropServices.StructLayoutAttribute", StringComparison.Ordinal)) {
                    continue;
                }

                if (attribute.ConstructorArguments.Length >= 1 &&
                    attribute.ConstructorArguments[0].Value is int layoutKindValue) {
                    layoutKind = (LayoutKind)layoutKindValue;
                }

                foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments) {
                    if (string.Equals(namedArgument.Key, "Pack", StringComparison.Ordinal) &&
                        namedArgument.Value.Value is int packValue) {
                        pack = packValue;
                    } else if (string.Equals(namedArgument.Key, "Size", StringComparison.Ordinal) &&
                        namedArgument.Value.Value is int sizeValue) {
                        size = sizeValue;
                    }
                }

                return true;
            }

            return false;
        }

        static bool TryGetExplicitLayoutOffset(IFieldSymbol fieldSymbol, out int offset) {
            offset = 0;
            if (fieldSymbol == null) {
                return false;
            }

            foreach (AttributeData attribute in fieldSymbol.GetAttributes()) {
                if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.Runtime.InteropServices.FieldOffsetAttribute", StringComparison.Ordinal)) {
                    continue;
                }

                if (attribute.ConstructorArguments.Length >= 1 &&
                    attribute.ConstructorArguments[0].Value is int explicitOffset) {
                    offset = explicitOffset;
                    return true;
                }
            }

            return false;
        }

        private static void ProcessClassDeclaration(SemanticModel semantic, ClassDeclarationSyntax classDecl, ConversionContext context) {
            List<string> outerGenericArgs = CloneCurrentGenericArguments(context);

            // declare class
            var cl = StartOrReuseTypeDeclarationClass(semantic, classDecl, context);
            cl.Name = classDecl.Identifier.ToString();

            if (cl.Name == "ApplicationPacket") {
                //Debugger.Break();
            }

            bool isStatic;
            bool isOverride;
            MemberAccessType access;
            MemberDeclarationType classType;
            MemberUtil.GetModifiers(classDecl.Modifiers, out isStatic, out isOverride, out access, out classType);

            cl.DeclarationType = classType;
            cl.IsValueType = false;
            cl.Semantic = semantic;
            cl.TypeSymbol = semantic.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (TryGetStructLayout(cl.TypeSymbol, out LayoutKind classLayoutKind, out int classLayoutPack, out int classLayoutSize)) {
                cl.HasExplicitLayout = classLayoutKind == LayoutKind.Explicit;
                cl.HasSequentialStructLayout = classLayoutKind == LayoutKind.Sequential;
                cl.SequentialStructLayoutPack = classLayoutPack;
                cl.SequentialStructLayoutSize = classLayoutSize;
            }

            ApplyTypeGenericArguments(cl, outerGenericArgs, classDecl.TypeParameterList);

            if (cl.Name == "WebSocketClientMessageHandler") {
                //Debugger.Break();
            }

            foreach (MemberDeclarationSyntax memberSyntax in classDecl.Members) {
                PreProcessExpression(semantic, context, memberSyntax);
            }

            if (classDecl.BaseList != null) {
                foreach (var baseType in classDecl.BaseList.ChildNodes()) {
                    if (baseType is SimpleBaseTypeSyntax) {
                        SimpleBaseTypeSyntax baseSyntax = (SimpleBaseTypeSyntax)baseType;

                        var type = VariableUtil.GetVarType(baseSyntax.Type!, semantic);
                        if (!cl.Extensions.Contains(type.TypeName)) {
                            cl.Extensions.Add(type.TypeName);
                        }
                    } else {
                        Debugger.Break();
                    }
                }
            }


            context.PopClass();
        }

        private static void ProcessStructDeclaration(SemanticModel semantic, StructDeclarationSyntax structDecl, ConversionContext context) {
            List<string> outerGenericArgs = CloneCurrentGenericArguments(context);

            // declare class
            var cl = StartOrReuseTypeDeclarationClass(semantic, structDecl, context);
            cl.Name = structDecl.Identifier.ToString();

            bool isStatic;
            bool isOverride;
            MemberAccessType access;
            MemberDeclarationType classType;
            MemberUtil.GetModifiers(structDecl.Modifiers, out isStatic, out isOverride, out access, out classType);

            cl.DeclarationType = classType;
            cl.IsValueType = true;
            cl.Semantic = semantic;
            cl.TypeSymbol = semantic.GetDeclaredSymbol(structDecl) as INamedTypeSymbol;
            if (TryGetStructLayout(cl.TypeSymbol, out LayoutKind structLayoutKind, out int structLayoutPack, out int structLayoutSize)) {
                cl.HasExplicitLayout = structLayoutKind == LayoutKind.Explicit;
                cl.HasSequentialStructLayout = structLayoutKind == LayoutKind.Sequential;
                cl.SequentialStructLayoutPack = structLayoutPack;
                cl.SequentialStructLayoutSize = structLayoutSize;
            }

            ApplyTypeGenericArguments(cl, outerGenericArgs, structDecl.TypeParameterList);

            foreach (MemberDeclarationSyntax memberSyntax in structDecl.Members) {
                PreProcessExpression(semantic, context, memberSyntax);
            }

            if (structDecl.BaseList != null) {
                foreach (var baseType in structDecl.BaseList.ChildNodes()) {
                    if (baseType is SimpleBaseTypeSyntax) {
                        SimpleBaseTypeSyntax baseSyntax = (SimpleBaseTypeSyntax)baseType;

                        var type = VariableUtil.GetVarType(baseSyntax.Type!, semantic);
                        if (!cl.Extensions.Contains(type.TypeName)) {
                            cl.Extensions.Add(type.TypeName);
                        }
                    } else {
                        Debugger.Break();
                    }
                }
            }


            context.PopClass();
        }

        private static void ProcessConstructorDeclaration(SemanticModel semantic, ConstructorDeclarationSyntax constructor, ConversionContext context) {
            bool isStatic;
            bool isOverride;
            MemberAccessType accessType;
            MemberDeclarationType type;
            MemberUtil.GetModifiers(constructor.Modifiers, out isStatic, out isOverride, out accessType, out type);

            ConversionFunction func = context.StartFn();
            func.Semantic = semantic;
            func.IsStatic = isStatic;
            func.AccessType = accessType;
            func.IsConstructor = true;
            func.Name = context.CurrentClass.Name;

            List<ConversionVariable> inParams = new List<ConversionVariable>();

            foreach (ParameterSyntax inParam in constructor.ParameterList.ChildNodes()) {
                ConversionVariable v = new ConversionVariable();
                v.Semantic = semantic;
                v.Name = inParam.Identifier.ToString();
                v.VarType = VariableUtil.GetVarType(inParam.Type, semantic);

                if (inParam.Default != null) {
                    v.DefaultValue = VariableUtil.ProcessAssignment(inParam.Default);
                }

                ParameterModifier modifier = ParameterModifier.None;
                foreach (var mod in inParam.Modifiers) {
                    switch (mod.Kind()) {
                        case SyntaxKind.InKeyword:
                            modifier |= ParameterModifier.In;
                            break;
                        case SyntaxKind.OutKeyword:
                            modifier |= ParameterModifier.Out;
                            break;
                        case SyntaxKind.RefKeyword:
                            modifier |= ParameterModifier.Ref;
                            break;
                        case SyntaxKind.ParamsKeyword:
                            modifier |= ParameterModifier.Params;
                            break;
                        case SyntaxKind.ThisKeyword:
                            modifier |= ParameterModifier.This;
                            break;
                    }
                }
                v.Modifier = modifier;

                inParams.Add(v);
            }
            func.InParameters = inParams;

            var block = constructor.Body;
            if (block != null) {
                func.RawBlock = block;
            }

            func.ConstructorInitializer = constructor.Initializer;
        }

        private static void ProcessMethodDeclaration(SemanticModel semantic, MethodDeclarationSyntax method, ConversionContext context) {
            string name = method.Identifier.ToString();
            IMethodSymbol methodSymbol = semantic.GetDeclaredSymbol(method) as IMethodSymbol;
            if (ShouldSkipExplicitInterfaceMethod(method, methodSymbol)) {
                return;
            }

            bool isStatic;
            bool isOverride;
            MemberAccessType access;
            MemberDeclarationType type;
            MemberUtil.GetModifiers(method.Modifiers, out isStatic, out isOverride, out access, out type);

            int sharedName = context.CurrentClass.Functions.Count(f => f.Name == name);
            string mappedName = name;
            if (sharedName > 0) {
                mappedName += sharedName + 1;
            }

            ConversionFunction func = context.StartFn();
            func.Semantic = semantic;
            func.IsStatic = isStatic;
            func.IsOverride = isOverride;
            func.AccessType = access;
            func.Name = name;
            func.Remap = mappedName;
            func.DeclarationType = type;
            func.IsAsync = MemberUtil.IsAsync(method.Modifiers);
            if (HasTypeScriptAsyncAttribute(methodSymbol) ||
                HasTypeScriptAsyncAttribute(methodSymbol?.ContainingType)) {
                func.IsAsync = true;
            }
            if (TryResolveNativeFreeFunctionMetadata(methodSymbol, out string nativeFreeFunctionName, out string nativeFreeFunctionIncludePath)) {
                func.NativeFreeFunctionName = nativeFreeFunctionName;
                func.NativeFreeFunctionIncludePath = nativeFreeFunctionIncludePath;
            } else if (TryResolveNativeFreeFunctionMetadata(method, out nativeFreeFunctionName, out nativeFreeFunctionIncludePath)) {
                func.NativeFreeFunctionName = nativeFreeFunctionName;
                func.NativeFreeFunctionIncludePath = nativeFreeFunctionIncludePath;
            }

            ApplyFunctionReturnType(method.ReturnType, semantic, func);

            if (context.CurrentClass.Name == "Node" &&
                name == "TryGetEdge") {
                //Debugger.Break();
            }

            foreach (ParameterSyntax inParam in method.ParameterList.ChildNodes()) {
                ConversionVariable v = new ConversionVariable();
                v.Semantic = semantic;
                v.Name = inParam.Identifier.ToString();
                v.VarType = VariableUtil.GetVarType(inParam.Type!, semantic);

                if (inParam.Default != null) {
                    v.DefaultValue = VariableUtil.ProcessAssignment(inParam.Default);
                }

                ParameterModifier modifier = ParameterModifier.None;
                foreach (var mod in inParam.Modifiers) {
                    switch (mod.Kind()) {
                        case SyntaxKind.InKeyword:
                            modifier |= ParameterModifier.In;
                            break;
                        case SyntaxKind.OutKeyword:
                            modifier |= ParameterModifier.Out;
                            break;
                        case SyntaxKind.RefKeyword:
                            modifier |= ParameterModifier.Ref;
                            break;
                        case SyntaxKind.ParamsKeyword:
                            modifier |= ParameterModifier.Params;
                            break;
                        case SyntaxKind.ThisKeyword:
                            modifier |= ParameterModifier.This;
                            break;
                    }
                }
                v.Modifier = modifier;

                func.InParameters.Add(v);
            }

            func.GenericParameters = method.TypeParameterList?
                .Parameters
                .Select(param => param.Identifier.Text)
                .ToList();

            if (method.Body != null) {
                func.RawBlock = method.Body;

                PreProcessExpression(semantic, context, method.Body);
            } else if (method.ExpressionBody != null) {
                if (context.CurrentClass.DeclarationType == MemberDeclarationType.Class) {
                    func.ArrowExpression = method.ExpressionBody;

                    PreProcessExpression(semantic, context, method.ExpressionBody);
                }
            }
        }

        private static void ProcessOperatorDeclaration(SemanticModel semantic, OperatorDeclarationSyntax operatorDeclaration, ConversionContext context) {
            bool isStatic;
            bool isOverride;
            MemberAccessType access;
            MemberDeclarationType type;
            MemberUtil.GetModifiers(operatorDeclaration.Modifiers, out isStatic, out isOverride, out access, out type);

            ConversionFunction func = context.StartFn();
            func.Semantic = semantic;
            func.IsStatic = true;
            func.AccessType = access;
            func.Name = $"operator{operatorDeclaration.OperatorToken.Text}";
            func.DeclarationType = type;
            ApplyFunctionReturnType(operatorDeclaration.ReturnType, semantic, func);

            foreach (ParameterSyntax parameter in operatorDeclaration.ParameterList.Parameters) {
                ConversionVariable variable = new ConversionVariable();
                variable.Semantic = semantic;
                variable.Name = parameter.Identifier.ToString();
                variable.VarType = VariableUtil.GetVarType(parameter.Type!, semantic);
                func.InParameters.Add(variable);
            }

            if (operatorDeclaration.Body != null) {
                func.RawBlock = operatorDeclaration.Body;
                PreProcessExpression(semantic, context, operatorDeclaration.Body);
            } else if (operatorDeclaration.ExpressionBody != null) {
                func.ArrowExpression = operatorDeclaration.ExpressionBody;
                PreProcessExpression(semantic, context, operatorDeclaration.ExpressionBody);
            }
        }

        private static void ProcessConversionOperatorDeclaration(SemanticModel semantic, ConversionOperatorDeclarationSyntax conversionOperatorDeclaration, ConversionContext context) {
            bool isStatic;
            bool isOverride;
            MemberAccessType access;
            MemberDeclarationType type;
            MemberUtil.GetModifiers(conversionOperatorDeclaration.Modifiers, out isStatic, out isOverride, out access, out type);

            IMethodSymbol methodSymbol = semantic.GetDeclaredSymbol(conversionOperatorDeclaration) as IMethodSymbol;

            ConversionFunction func = context.StartFn();
            func.Semantic = semantic;
            func.IsStatic = true;
            func.AccessType = access;
            func.Name = GetConversionOperatorFunctionName(
                conversionOperatorDeclaration.ImplicitOrExplicitKeyword.Kind(),
                methodSymbol?.ReturnType ?? semantic.GetTypeInfo(conversionOperatorDeclaration.Type).Type);
            func.DeclarationType = type;
            ApplyFunctionReturnType(conversionOperatorDeclaration.Type, semantic, func);

            foreach (ParameterSyntax parameter in conversionOperatorDeclaration.ParameterList.Parameters) {
                ConversionVariable variable = new ConversionVariable();
                variable.Semantic = semantic;
                variable.Name = parameter.Identifier.ToString();
                variable.VarType = VariableUtil.GetVarType(parameter.Type!, semantic);
                func.InParameters.Add(variable);
            }

            if (conversionOperatorDeclaration.Body != null) {
                func.RawBlock = conversionOperatorDeclaration.Body;
                PreProcessExpression(semantic, context, conversionOperatorDeclaration.Body);
            } else if (conversionOperatorDeclaration.ExpressionBody != null) {
                func.ArrowExpression = conversionOperatorDeclaration.ExpressionBody;
                PreProcessExpression(semantic, context, conversionOperatorDeclaration.ExpressionBody);
            }
        }

        private static string GetConversionOperatorFunctionName(SyntaxKind operatorKind, ITypeSymbol returnTypeSymbol) {
            string operatorPrefix = operatorKind == SyntaxKind.ImplicitKeyword ? "op_Implicit_to_" : "op_Explicit_to_";
            string targetName = returnTypeSymbol?.OriginalDefinition?.MetadataName ?? returnTypeSymbol?.MetadataName ?? returnTypeSymbol?.Name ?? "Unknown";
            return operatorPrefix + targetName.Replace('`', '_');
        }

        private static void ProcessDelegateDeclaration(SemanticModel semantic, DelegateDeclarationSyntax delegateDecl, ConversionContext context) {
            string name = delegateDecl.Identifier.ToString();

            int sharedName = context.Program.Classes.Count(f => f.Name == name);
            string mappedName = name;
            if (sharedName > 0) {
                mappedName += sharedName + 1;
            }

            var cl = context.StartClass();
            cl.Name = name;

            bool isStatic;
            bool isOverride;
            MemberAccessType access;
            MemberDeclarationType classType;
            MemberUtil.GetModifiers(delegateDecl.Modifiers, out isStatic, out isOverride, out access, out classType);

            cl.DeclarationType = MemberDeclarationType.Delegate;
            cl.Semantic = semantic;
            cl.TypeSymbol = semantic.GetDeclaredSymbol(delegateDecl) as INamedTypeSymbol;

            ConversionFunction func = context.StartFn();
            func.Semantic = semantic;
            func.IsStatic = isStatic;
            func.AccessType = access;
            func.Name = "delegate";
            func.Remap = mappedName;
            ApplyFunctionReturnType(delegateDecl.ReturnType, semantic, func);

            func.GenericParameters = delegateDecl.TypeParameterList?
                .Parameters
                .Select(param => param.Identifier.Text)
                .ToList();

            List<ConversionVariable> parameters = new List<ConversionVariable>();
            AddParameterList(semantic, delegateDecl.ParameterList.Parameters, parameters);
            func.InParameters = parameters;

            context.PopClass();
        }

        /// <summary>
        /// Resolves the function return type while preserving by-ref return semantics required by native signature emission.
        /// </summary>
        /// <param name="returnTypeSyntax">Roslyn return type syntax node.</param>
        /// <param name="semantic">Semantic model used to resolve the underlying return type.</param>
        /// <param name="function">Function model receiving the resolved return metadata.</param>
        static void ApplyFunctionReturnType(TypeSyntax returnTypeSyntax, SemanticModel semantic, ConversionFunction function) {
            if (returnTypeSyntax == null || function == null) {
                return;
            }

            if (returnTypeSyntax is RefTypeSyntax refTypeSyntax) {
                function.ReturnType = VariableUtil.GetVarType(refTypeSyntax.Type, semantic);
                function.ReturnsReference = true;
            } else {
                function.ReturnType = VariableUtil.GetVarType(returnTypeSyntax, semantic);
            }

            if (function.ReturnType != null && function.ReturnType.Type == VariableDataType.Void) {
                function.ReturnType = null;
                function.ReturnsReference = false;
            }
        }

        /// <summary>
        /// Determines whether an explicit interface method should be omitted because the current native interface model cannot represent that contract directly.
        /// </summary>
        /// <param name="method">Method declaration syntax under consideration.</param>
        /// <param name="methodSymbol">Resolved Roslyn method symbol.</param>
        /// <returns><c>true</c> when the method is an explicit interface implementation that should not be emitted; otherwise <c>false</c>.</returns>
        static bool ShouldSkipExplicitInterfaceMethod(MethodDeclarationSyntax method, IMethodSymbol methodSymbol) {
            if (method?.ExplicitInterfaceSpecifier != null &&
                methodSymbol != null &&
                methodSymbol.ExplicitInterfaceImplementations.Any(interfaceMethod =>
                    string.Equals(interfaceMethod.ContainingType?.ToDisplayString(), "System.Collections.IEnumerable", StringComparison.Ordinal) &&
                    string.Equals(interfaceMethod.Name, "GetEnumerator", StringComparison.Ordinal))) {
                return true;
            }

            if (method?.ExplicitInterfaceSpecifier != null &&
                methodSymbol != null &&
                methodSymbol.ExplicitInterfaceImplementations.Any(interfaceMethod =>
                    string.Equals(interfaceMethod.ContainingType?.OriginalDefinition?.ToDisplayString(), "System.Collections.Generic.IEnumerable<T>", StringComparison.Ordinal) &&
                    string.Equals(interfaceMethod.Name, "GetEnumerator", StringComparison.Ordinal))) {
                return true;
            }

            return method?.ExplicitInterfaceSpecifier != null &&
                methodSymbol != null &&
                methodSymbol.ExplicitInterfaceImplementations.Length > 0 &&
                methodSymbol.ExplicitInterfaceImplementations.All(interfaceMethod => interfaceMethod.IsGenericMethod);
        }

        private static string ProcessLiteralExpression(LiteralExpressionSyntax literalExpression, ConversionContext context) {
            string literalValue;

            switch (literalExpression.Kind()) {
                case SyntaxKind.TrueLiteralExpression:
                    literalValue = "true";
                    break;
                case SyntaxKind.FalseLiteralExpression:
                    literalValue = "false";
                    break;
                case SyntaxKind.NumericLiteralExpression:
                    literalValue = literalExpression.Token.ValueText;
                    break;
                case SyntaxKind.StringLiteralExpression:
                    literalValue = $"\"{literalExpression.Token.ValueText}\"";
                    literalValue = Regex.Replace(literalValue, @"(?<!\\)\\(?!\\)", @"\\");
                    literalValue = Regex.Replace(literalValue, @"\r?\n", match => {
                        return match.Value == "\r\n" ? "\\r\\n" : "\\n";
                });
                    break;
                case SyntaxKind.NullLiteralExpression:
                    literalValue = "null";
                    break;
                default:
                    throw new Exception("Unsupported literal type");
            }

            return literalValue;
        }

        private static void ProcessField(SemanticModel semantic, FieldDeclarationSyntax fMember, ConversionContext context) {
            VariableDeclarationSyntax declaration = fMember.Declaration;
            bool isStatic;
            bool isOverride;
            MemberAccessType access;
            MemberDeclarationType type;
            MemberUtil.GetModifiers(fMember.Modifiers, out isStatic, out isOverride, out access, out type);

            VariableType fieldType = VariableUtil.GetVarType(declaration.Type, semantic);
            foreach (VariableDeclaratorSyntax variableDeclarator in declaration.Variables) {
                ConversionVariable variable = context.StartVar();
                variable.Semantic = semantic;
                variable.Name = variableDeclarator.Identifier.ToString();
                variable.VarType = new VariableType(fieldType);

                variable.IsStatic = isStatic;
                variable.IsConst = fMember.Modifiers.Any(modifier => modifier.ValueText == "const");
                variable.AccessType = access;
                variable.IsOverride = isOverride;
                variable.DeclarationType = type;
                if (semantic.GetDeclaredSymbol(variableDeclarator) is IFieldSymbol fieldSymbol &&
                    TryGetExplicitLayoutOffset(fieldSymbol, out int explicitLayoutOffset)) {
                    variable.HasExplicitLayoutOffset = true;
                    variable.ExplicitLayoutOffset = explicitLayoutOffset;
                }

                if (variableDeclarator.Initializer != null) {
                    variable.AssignmentExpression = variableDeclarator.Initializer.Value;
                    if (variableDeclarator.Initializer.Value is LiteralExpressionSyntax literal) {
                        variable.Assignment = ProcessLiteralExpression(literal, context);
                    }
                }

                variable.SetDefaultAssignment();
            }
        }

        private static void ProcessProperty(SemanticModel semantic, PropertyDeclarationSyntax pMember, ConversionContext context) {
            bool isStatic;
            bool isOverride;
            MemberAccessType accessType;
            MemberDeclarationType type;
            MemberUtil.GetModifiers(pMember.Modifiers, out isStatic, out isOverride, out accessType, out type);

            IPropertySymbol propertySymbol = semantic.GetDeclaredSymbol(pMember) as IPropertySymbol;
            if (ShouldSkipExplicitInterfaceProperty(pMember, propertySymbol)) {
                return;
            }

            ConversionVariable variable = context.StartVar();
            variable.Semantic = semantic;
            variable.Name = pMember.Identifier.ToString();
            variable.IsStatic = isStatic;

            variable.AccessType = accessType;
            if (propertySymbol?.ExplicitInterfaceImplementations.Length > 0) {
                variable.AccessType = MemberAccessType.Public;
            }
            variable.VarType = propertySymbol != null
                ? VariableUtil.GetVarType(propertySymbol.Type)
                : VariableUtil.GetVarType(pMember.Type, semantic);
            variable.IsOverride = isOverride;
            variable.IsAbstract = type == MemberDeclarationType.Abstract;
            variable.DeclarationType = type;
            if (propertySymbol != null) {
                if (propertySymbol.RefKind == RefKind.Ref) {
                    variable.ReturnsReference = true;
                } else if (propertySymbol.RefKind == RefKind.RefReadOnly) {
                    variable.ReturnsConstReference = true;
                }
            }
            if (pMember.Initializer != null) {
                variable.AssignmentExpression = pMember.Initializer.Value;
                if (pMember.Initializer.Value is LiteralExpressionSyntax literal) {
                    variable.Assignment = literal.Token.ValueText;
                }
            }

            if (pMember.AccessorList == null) {
                if (pMember.ExpressionBody != null) {
                    variable.IsGet = true;
                    variable.ArrowExpression = pMember.ExpressionBody.Expression;
                }
            } else {
                // loop through each accessor (get/set)
                foreach (var accessor in pMember.AccessorList.Accessors) {
                    // check if this accessor is a set accessor
                    if (accessor.Kind() == SyntaxKind.GetAccessorDeclaration) {
                        variable.IsGet = true;
                        if (accessor.Body != null) {
                            variable.GetBlock = accessor.Body;
                        } else if (accessor.ExpressionBody != null) {
                            variable.ArrowExpression = accessor.ExpressionBody.Expression;
                            PreProcessExpression(semantic, context, accessor.ExpressionBody.Expression);
                        }
                    } else if (accessor.Kind() == SyntaxKind.SetAccessorDeclaration) {
                        variable.IsSet = true;
                        if (accessor.Body != null) {
                            variable.SetBlock = accessor.Body;
                        } else if (accessor.ExpressionBody != null) {
                            variable.SetBlock = SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(accessor.ExpressionBody.Expression));
                            PreProcessExpression(semantic, context, accessor.ExpressionBody.Expression);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Determines whether one explicit interface property should be omitted because the current native interface surface cannot represent it without a signature collision.
        /// </summary>
        /// <param name="property">Property declaration syntax under consideration.</param>
        /// <param name="propertySymbol">Resolved Roslyn property symbol.</param>
        /// <returns><c>true</c> when the property should not be emitted; otherwise <c>false</c>.</returns>
        static bool ShouldSkipExplicitInterfaceProperty(PropertyDeclarationSyntax property, IPropertySymbol propertySymbol) {
            if (property?.ExplicitInterfaceSpecifier == null || propertySymbol == null) {
                return false;
            }

            return propertySymbol.ExplicitInterfaceImplementations.Any(interfaceProperty =>
                string.Equals(interfaceProperty.ContainingType?.ToDisplayString(), "System.Collections.IEnumerator", StringComparison.Ordinal) &&
                string.Equals(interfaceProperty.Name, "Current", StringComparison.Ordinal));
        }

        private static void ProcessEvent(SemanticModel semantic, EventFieldDeclarationSyntax eventDecl, ConversionContext context) {
            // Extract modifiers from the event declaration
            MemberUtil.GetModifiers(eventDecl.Modifiers,
                out bool isStatic,
                out bool isOverride,
                out MemberAccessType accessType,
                out MemberDeclarationType declarationType);

            // Roslyn syntax: Event declarations can have multiple variables
            foreach (var variableDeclarator in eventDecl.Declaration.Variables) {
                ConversionVariable variable = context.StartVar();
                variable.Semantic = semantic;
                variable.Name = variableDeclarator.Identifier.Text; // Correctly access the identifier
                variable.IsStatic = isStatic;
                variable.AccessType = accessType;
                variable.VarType = new VariableType(VariableDataType.Object, "Event");
                if (string.IsNullOrEmpty(variable.Assignment)) {
                    variable.Assignment = "new Event()";
                }
                variable.IsOverride = isOverride;
                variable.DeclarationType = declarationType;
            }
        }

        public static ExpressionResult PreProcessExpression(SemanticModel semantic, ConversionContext context, SyntaxNode exp) {
            if (exp is NamespaceDeclarationSyntax nameSpace) {
                string name = nameSpace.Name.ToString();
                if (context.Program.Rules.IgnoredNamespaces.Any(c => name.Contains(c))) {
                    return new ExpressionResult(false);
                }

                foreach (var m in nameSpace.Members) {
                    PreProcessExpression(semantic, context, m);
                }
            } else if (exp is FileScopedNamespaceDeclarationSyntax fileNameSpace) {
                string name = fileNameSpace.Name.ToString();
                if (context.Program.Rules.IgnoredNamespaces.Any(c => name.Contains(c))) {
                    return new ExpressionResult(false);
                }

                foreach (var m in fileNameSpace.Members) {
                    PreProcessExpression(semantic, context, m);
                }
            } else if (exp is ClassDeclarationSyntax classDecl) {
                if (context.Program.Rules.IgnoredClasses.Any(c => classDecl.Identifier.ToString().Contains(c))) {
                    return new ExpressionResult(false);
                }

                ProcessClassDeclaration(semantic, classDecl, context);
            } else if (exp is StructDeclarationSyntax structDecl) {
                if (context.Program.Rules.IgnoredClasses.Any(c => structDecl.Identifier.ToString().Contains(c))) {
                    return new ExpressionResult(false);
                }

                ProcessStructDeclaration(semantic, structDecl, context);
            } else if (exp is InterfaceDeclarationSyntax ifaceDecl) {
                List<string> outerGenericArgs = CloneCurrentGenericArguments(context);

                // declare class
                var cl = StartOrReuseTypeDeclarationClass(semantic, ifaceDecl, context);
                cl.Name = ifaceDecl.Identifier.ToString();
                cl.DeclarationType = MemberDeclarationType.Interface;
                cl.IsValueType = false;
                cl.Semantic = semantic;
                cl.TypeSymbol = semantic.GetDeclaredSymbol(ifaceDecl) as INamedTypeSymbol;

                ApplyTypeGenericArguments(cl, outerGenericArgs, ifaceDecl.TypeParameterList);

                foreach (MemberDeclarationSyntax memberSyntax in ifaceDecl.Members) {
                    PreProcessExpression(semantic, context, memberSyntax);
                }

                if (ifaceDecl.BaseList != null) {
                    foreach (var baseType in ifaceDecl.BaseList.ChildNodes()) {
                        if (baseType is SimpleBaseTypeSyntax) {
                            SimpleBaseTypeSyntax baseSyntax = (SimpleBaseTypeSyntax)baseType;
                            VariableType type = VariableUtil.GetVarType(baseSyntax.Type!, semantic);
                            if (!cl.Extensions.Contains(type.TypeName)) {
                                cl.Extensions.Add(type.TypeName);
                            }
                        }
                    }
                }

                context.PopClass();
            } else if (exp is EnumDeclarationSyntax) {
                EnumDeclarationSyntax Enum = (EnumDeclarationSyntax)exp;

                var cl = context.StartClass();
                cl.DeclarationType = MemberDeclarationType.Enum;
                cl.IsValueType = true;
                cl.Name = Enum.Identifier.ToString();
                cl.EnumMembers = new List<object>();
                cl.Semantic = semantic;
                cl.TypeSymbol = semantic.GetDeclaredSymbol(Enum) as INamedTypeSymbol;

                if (Enum.BaseList != null) {
                    foreach (var baseType in Enum.BaseList.ChildNodes()) {
                        if (baseType is SimpleBaseTypeSyntax) {
                            SimpleBaseTypeSyntax baseSyntax = (SimpleBaseTypeSyntax)baseType;
                            string type = baseSyntax.Type.ToString();
                        } else {
                            throw new NotImplementedException();
                        }
                    }
                }

                foreach (MemberDeclarationSyntax memberSyntax in Enum.Members) {
                    cl.EnumMembers.Add(memberSyntax.ToString());
                }

                context.PopClass();
            } else if (exp is DelegateDeclarationSyntax del) {
                ProcessDelegateDeclaration(semantic, del, context);
            } else if (exp is MethodDeclarationSyntax method) {
                ProcessMethodDeclaration(semantic, method, context);
            } else if (exp is OperatorDeclarationSyntax operatorDeclaration) {
                ProcessOperatorDeclaration(semantic, operatorDeclaration, context);
            } else if (exp is ConversionOperatorDeclarationSyntax conversionOperatorDeclaration) {
                ProcessConversionOperatorDeclaration(semantic, conversionOperatorDeclaration, context);
            } else if (exp is ConstructorDeclarationSyntax constructor) {
                ProcessConstructorDeclaration(semantic, constructor, context);
            } else if (exp is FieldDeclarationSyntax field) {
                // int x = 0;
                ProcessField(semantic, field, context);
            } else if (exp is PropertyDeclarationSyntax prop) {
                // int x { get; set; }
                ProcessProperty(semantic, prop, context);
            } else if (exp is IndexerDeclarationSyntax indexerDeclaration) {
                ProcessIndexer(semantic, indexerDeclaration, context);
            } else if (exp is EventFieldDeclarationSyntax eventDecl) {
                ProcessEvent(semantic, eventDecl, context);
            } else if (exp is BlockSyntax block) {
                PreProcessBlock(semantic, context, block);
            } else if (exp is LocalDeclarationStatementSyntax local) {
                PreProcessDeclaration(semantic, context, local.Declaration);
            } else if (exp is ExpressionStatementSyntax) {
                ExpressionSyntax expression = ((ExpressionStatementSyntax)exp).Expression;
                PreProcessExpression(semantic, context, expression);
            } else if (exp is AssignmentExpressionSyntax assignment) {
                PreProcessAssignmentExpressionSyntax(semantic, context, assignment);
            } else if (exp is UsingStatementSyntax usingStatement) {
                PreProcessUsingStatement(semantic, context, usingStatement);
            } else if (exp is InvocationExpressionSyntax invocationExpression) {
                PreProcessInvocationExpressionSyntax(semantic, context, invocationExpression);
            } else if (exp is ObjectCreationExpressionSyntax objectCreationExpression) {
                PreProcessObjectCreationExpressionSyntax(semantic, context, objectCreationExpression);
            } else if (exp is IfStatementSyntax ifStatement) {
                PreProcessIfStatement(semantic, context, ifStatement);
            } else if (exp is MemberAccessExpressionSyntax memberAccess) {
                PreProcessMemberAccessExpressionSyntax(semantic, context, memberAccess);
            } else if (exp is IdentifierNameSyntax identifier) {
                PreProcessIdentifierNameSyntax(semantic, context, identifier);
            } else if (exp is ReturnStatementSyntax ret) {
                PreProcessReturnStatementSyntax(semantic, context, ret);
            } else if (exp is ThrowStatementSyntax throwStatement) {
                if (throwStatement.Expression != null) {
                    PreProcessExpression(semantic, context, throwStatement.Expression);
                }
            } else if (exp is ThrowExpressionSyntax throwExpression) {
                PreProcessExpression(semantic, context, throwExpression.Expression);
            } else if (exp is TryStatementSyntax tryStatement) {
                PreProcessTryStatementSyntax(semantic, context, tryStatement);
            } else if (exp is BinaryExpressionSyntax ||
                        exp is SwitchStatementSyntax ||
                        exp is LockStatementSyntax ||
                        exp is PostfixUnaryExpressionSyntax ||
                        exp is PrefixUnaryExpressionSyntax ||
                        exp is ForStatementSyntax ||
                        exp is ConditionalAccessExpressionSyntax ||
                        exp is WhileStatementSyntax ||
                        exp is ForEachStatementSyntax) {
                // ignore
            } else {
                //Debugger.Break();
            }

            return new ExpressionResult(false);
        }

        /// <summary>
        /// Converts one Roslyn parameter list into conversion variables, preserving type metadata, default values, and by-reference modifiers.
        /// </summary>
        /// <param name="semantic">Semantic model that resolves parameter types.</param>
        /// <param name="parameters">Roslyn parameters to convert.</param>
        /// <param name="destination">Destination collection that receives converted parameters.</param>
        static void AddParameterList(SemanticModel semantic, SeparatedSyntaxList<ParameterSyntax> parameters, IList<ConversionVariable> destination) {
            foreach (ParameterSyntax parameter in parameters) {
                ConversionVariable variable = new ConversionVariable();
                variable.Semantic = semantic;
                variable.Name = parameter.Identifier.ToString();
                variable.VarType = VariableUtil.GetVarType(parameter.Type!, semantic);

                if (parameter.Default != null) {
                    variable.DefaultValue = VariableUtil.ProcessAssignment(parameter.Default);
                }

                variable.Modifier = GetParameterModifier(parameter.Modifiers);
                destination.Add(variable);
            }
        }

        /// <summary>
        /// Converts Roslyn parameter modifier tokens into the stable conversion flag set used by downstream emitters.
        /// </summary>
        /// <param name="modifiers">Roslyn parameter modifier tokens.</param>
        /// <returns>The combined conversion modifier flags.</returns>
        static ParameterModifier GetParameterModifier(SyntaxTokenList modifiers) {
            ParameterModifier modifier = ParameterModifier.None;
            foreach (SyntaxToken token in modifiers) {
                switch (token.Kind()) {
                    case SyntaxKind.InKeyword:
                        modifier |= ParameterModifier.In;
                        break;
                    case SyntaxKind.OutKeyword:
                        modifier |= ParameterModifier.Out;
                        break;
                    case SyntaxKind.RefKeyword:
                        modifier |= ParameterModifier.Ref;
                        break;
                    case SyntaxKind.ParamsKeyword:
                        modifier |= ParameterModifier.Params;
                        break;
                    case SyntaxKind.ThisKeyword:
                        modifier |= ParameterModifier.This;
                        break;
                }
            }

            return modifier;
        }

        /// <summary>
        /// Reuses one existing converted type model for partial declarations so later syntax parts append members instead of overwriting earlier emitted files.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve the declared type symbol.</param>
        /// <param name="typeDeclaration">Current type declaration syntax.</param>
        /// <param name="context">Conversion context receiving the active class scope.</param>
        /// <returns>The active converted class model for the declaration.</returns>
        static ConversionClass StartOrReuseTypeDeclarationClass(
            SemanticModel semantic,
            TypeDeclarationSyntax typeDeclaration,
            ConversionContext context) {
            if (semantic == null) {
                throw new ArgumentNullException(nameof(semantic));
            }
            if (typeDeclaration == null) {
                throw new ArgumentNullException(nameof(typeDeclaration));
            }
            if (context == null) {
                throw new ArgumentNullException(nameof(context));
            }

            if (typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)) &&
                semantic.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol declaredTypeSymbol) {
                ConversionClass existingClass = context.Program.Classes.FirstOrDefault(candidate =>
                    !candidate.IsNative &&
                    candidate.TypeSymbol != null &&
                    SymbolEqualityComparer.Default.Equals(candidate.TypeSymbol, declaredTypeSymbol));
                if (existingClass != null) {
                    return context.PushClass(existingClass);
                }
            }

            return context.StartClass();
        }

        /// <summary>
        /// Clones the active outer type generic parameters so nested converted types can capture them as compile-time template symbols.
        /// </summary>
        /// <param name="context">Current conversion context.</param>
        /// <returns>Cloned outer generic parameter names, or an empty list when no outer generic parameters are active.</returns>
        static List<string> CloneCurrentGenericArguments(ConversionContext context) {
            if (context?.CurrentClass?.GenericArgs == null || context.CurrentClass.GenericArgs.Count == 0) {
                return [];
            }

            return new List<string>(context.CurrentClass.GenericArgs);
        }

        /// <summary>
        /// Applies captured outer generic parameters and locally declared type parameters to one converted type declaration.
        /// </summary>
        /// <param name="conversionClass">Converted type that should receive compile-time generic parameter names.</param>
        /// <param name="outerGenericArgs">Captured outer generic parameters from the containing converted type.</param>
        /// <param name="typeParameterList">Locally declared type parameters on the current syntax node.</param>
        static void ApplyTypeGenericArguments(ConversionClass conversionClass, IReadOnlyList<string> outerGenericArgs, TypeParameterListSyntax typeParameterList) {
            if (conversionClass == null) {
                throw new ArgumentNullException(nameof(conversionClass));
            }

            bool hasOuterGenericArgs = outerGenericArgs != null && outerGenericArgs.Count > 0;
            bool hasDeclaredGenericArgs = typeParameterList != null && typeParameterList.Parameters.Count > 0;
            if (!hasOuterGenericArgs && !hasDeclaredGenericArgs) {
                return;
            }

            conversionClass.GenericArgs = new List<string>();
            if (hasOuterGenericArgs) {
                for (int index = 0; index < outerGenericArgs.Count; index++) {
                    conversionClass.GenericArgs.Add(outerGenericArgs[index]);
                }
            }

            if (!hasDeclaredGenericArgs) {
                return;
            }

            foreach (TypeParameterSyntax typeParameter in typeParameterList.Parameters) {
                string typeParameterName = typeParameter.ToString();
                if (!conversionClass.GenericArgs.Contains(typeParameterName)) {
                    conversionClass.GenericArgs.Add(typeParameterName);
                }
            }
        }

        private static void ProcessIndexer(SemanticModel semantic, IndexerDeclarationSyntax indexerDeclaration, ConversionContext context) {
            bool isStatic;
            bool isOverride;
            MemberAccessType accessType;
            MemberDeclarationType declarationType;
            MemberUtil.GetModifiers(indexerDeclaration.Modifiers, out isStatic, out isOverride, out accessType, out declarationType);

            IPropertySymbol propertySymbol = semantic.GetDeclaredSymbol(indexerDeclaration) as IPropertySymbol;
            if (propertySymbol?.ExplicitInterfaceImplementations.Length > 0) {
                accessType = MemberAccessType.Public;
            }
            VariableType indexerType = propertySymbol != null
                ? VariableUtil.GetVarType(propertySymbol.Type)
                : VariableUtil.GetVarType(indexerDeclaration.Type, semantic);

            if (indexerDeclaration.AccessorList == null) {
                if (indexerDeclaration.ExpressionBody != null) {
                    ConversionFunction getter = context.StartFn();
                    getter.Semantic = semantic;
                    getter.IsStatic = isStatic;
                    getter.IsOverride = isOverride;
                    getter.AccessType = accessType;
                    getter.DeclarationType = declarationType;
                    getter.Name = "get_Item";
                    getter.ReturnType = new VariableType(indexerType);

                    if (propertySymbol != null) {
                        if (propertySymbol.RefKind == RefKind.Ref) {
                            getter.ReturnsReference = true;
                        } else if (propertySymbol.RefKind == RefKind.RefReadOnly) {
                            getter.ReturnsConstReference = true;
                        }
                    }

                    AddParameterList(semantic, indexerDeclaration.ParameterList.Parameters, getter.InParameters);
                    getter.ArrowExpression = indexerDeclaration.ExpressionBody;
                    PreProcessExpression(semantic, context, indexerDeclaration.ExpressionBody);
                }

                return;
            }

            foreach (AccessorDeclarationSyntax accessor in indexerDeclaration.AccessorList.Accessors) {
                if (accessor.Kind() == SyntaxKind.GetAccessorDeclaration) {
                    ConversionFunction getter = context.StartFn();
                    getter.Semantic = semantic;
                    getter.IsStatic = isStatic;
                    getter.IsOverride = isOverride;
                    getter.AccessType = accessType;
                    getter.DeclarationType = declarationType;
                    getter.Name = "get_Item";
                    getter.ReturnType = new VariableType(indexerType);

                    if (propertySymbol != null) {
                        if (propertySymbol.RefKind == RefKind.Ref) {
                            getter.ReturnsReference = true;
                        } else if (propertySymbol.RefKind == RefKind.RefReadOnly) {
                            getter.ReturnsConstReference = true;
                        }
                    }

                    AddParameterList(semantic, indexerDeclaration.ParameterList.Parameters, getter.InParameters);

                    if (accessor.Body != null) {
                        getter.RawBlock = accessor.Body;
                        PreProcessExpression(semantic, context, accessor.Body);
                    } else if (accessor.ExpressionBody != null) {
                        getter.ArrowExpression = accessor.ExpressionBody;
                        PreProcessExpression(semantic, context, accessor.ExpressionBody);
                    }
                } else if (accessor.Kind() == SyntaxKind.SetAccessorDeclaration) {
                    ConversionFunction setter = context.StartFn();
                    setter.Semantic = semantic;
                    setter.IsStatic = isStatic;
                    setter.IsOverride = isOverride;
                    setter.AccessType = accessType;
                    setter.DeclarationType = declarationType;
                    setter.Name = "set_Item";
                    setter.ReturnType = new VariableType(VariableDataType.Void, "void");

                    AddParameterList(semantic, indexerDeclaration.ParameterList.Parameters, setter.InParameters);
                    setter.InParameters.Add(new ConversionVariable {
                        Semantic = semantic,
                        Name = "value",
                        VarType = new VariableType(indexerType)
                    });

                    if (accessor.Body != null) {
                        setter.RawBlock = accessor.Body;
                        PreProcessExpression(semantic, context, accessor.Body);
                    } else if (accessor.ExpressionBody != null) {
                        setter.RawBlock = SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(accessor.ExpressionBody.Expression));
                        PreProcessExpression(semantic, context, accessor.ExpressionBody.Expression);
                    }
                }
            }
        }

        protected static void PreProcessTryStatementSyntax(SemanticModel semantic, ConversionContext context, TryStatementSyntax tryStatement) {
            PreProcessExpression(semantic, context, tryStatement.Block);
            //PreProcessExpression(semantic, context, tryStatement.Catches);
            //PreProcessExpression(semantic, context, tryStatement.Finally);
        }

        protected static void PreProcessReturnStatementSyntax(SemanticModel semantic, ConversionContext context, ReturnStatementSyntax ret) {
            if (ret.Expression == null) {
                return;
            }

            var typeInfo = semantic.GetTypeInfo(ret.Expression);
            ITypeSymbol typeSymbol = typeInfo.ConvertedType ?? typeInfo.Type;
            if (typeSymbol == null) {
                return;
            }

            string type = typeSymbol.ToString();
            ConversionClass cl = context.CurrentClass;
            if (!cl.ReferencedClasses.Contains(type)) {
                cl.ReferencedClasses.Add(type);
            }

            ConversionFunction fn = context.CurrentFunction;
            if (fn.AnalyzedReturns == null) {
                fn.AnalyzedReturns = new List<VariableType>();
            }

            fn.AnalyzedReturns.Add(VariableUtil.GetVarType(type));
        }

        protected static void PreProcessIdentifierNameSyntax(SemanticModel semantic, ConversionContext context, IdentifierNameSyntax identifier) {
            string name = identifier.ToString();
            bool isMethod = false;

            ISymbol? nsSymbol = semantic.GetSymbolInfo(identifier).Symbol;
            if (nsSymbol is INamespaceSymbol namespaceSymbol) {
                if (namespaceSymbol.IsNamespace) {
                    return;
                }
            } else if (nsSymbol is IMethodSymbol methodSymbol) {
                isMethod = true;
            }

            if (nsSymbol is ITypeSymbol typeSymbol) {
                int kksk = -1;
            }

            if (nsSymbol is ITypeSymbol namedTypeSymbol) {
                int kksk = -1;
            }

            if (nsSymbol is INamespaceOrTypeSymbol nameSpaceType) {
                bool isType = nameSpaceType.IsType;

                string type = identifier.ToString();
                ConversionClass cl = context.CurrentClass;
                if (!cl.ReferencedClasses.Contains(type)) {
                    cl.ReferencedClasses.Add(type);
                }
            }
        }

        protected static ExpressionResult PreProcessMemberAccessExpressionSyntax(SemanticModel semantic, ConversionContext context, MemberAccessExpressionSyntax memberAccess) {
            PreProcessExpression(semantic, context, memberAccess.Expression);

            ISymbol? symbol = semantic.GetSymbolInfo(memberAccess).Symbol ?? semantic.GetSymbolInfo(memberAccess.Name).Symbol;
            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            if (symbol is INamedTypeSymbol namedTypeSymbol) {
                AddReferencedClass(context.CurrentClass, namedTypeSymbol.ToDisplayString());
            }

            if (symbol is IFieldSymbol fieldSymbol && fieldSymbol.IsStatic && fieldSymbol.ContainingType != null) {
                AddReferencedClass(context.CurrentClass, fieldSymbol.ContainingType.ToDisplayString());
            } else if (symbol is IPropertySymbol propertySymbol && propertySymbol.IsStatic && propertySymbol.ContainingType != null) {
                AddReferencedClass(context.CurrentClass, propertySymbol.ContainingType.ToDisplayString());
            } else if (symbol is IMethodSymbol methodSymbol && methodSymbol.IsStatic && methodSymbol.ContainingType != null) {
                AddReferencedClass(context.CurrentClass, methodSymbol.ContainingType.ToDisplayString());
            } else if (TryResolveReferencedStaticType(semantic, memberAccess.Expression, out string referencedStaticTypeName)) {
                AddReferencedClass(context.CurrentClass, referencedStaticTypeName);
            }

            ISymbol? receiverSymbol = semantic.GetSymbolInfo(memberAccess.Expression).Symbol;
            if (receiverSymbol is IAliasSymbol receiverAliasSymbol) {
                receiverSymbol = receiverAliasSymbol.Target;
            }

            if (receiverSymbol == null && memberAccess.Expression is MemberAccessExpressionSyntax nestedMemberAccess) {
                receiverSymbol = semantic.GetSymbolInfo(nestedMemberAccess.Name).Symbol;
                if (receiverSymbol is IAliasSymbol nestedReceiverAliasSymbol) {
                    receiverSymbol = nestedReceiverAliasSymbol.Target;
                }
            }

            if (receiverSymbol is IFieldSymbol receiverFieldSymbol &&
                receiverFieldSymbol.IsStatic &&
                receiverFieldSymbol.ContainingType != null) {
                AddReferencedClass(context.CurrentClass, receiverFieldSymbol.ContainingType.ToDisplayString());
            } else if (receiverSymbol is IPropertySymbol receiverPropertySymbol &&
                receiverPropertySymbol.IsStatic &&
                receiverPropertySymbol.ContainingType != null) {
                AddReferencedClass(context.CurrentClass, receiverPropertySymbol.ContainingType.ToDisplayString());
            }

            return PreProcessExpression(semantic, context, memberAccess.Name);
        }

        /// <summary>
        /// Registers a referenced class on the current conversion class when the discovered type name is non-empty.
        /// </summary>
        /// <param name="conversionClass">Class that owns the currently preprocessed member body.</param>
        /// <param name="typeName">Referenced type name to register.</param>
        static void AddReferencedClass(ConversionClass conversionClass, string typeName) {
            if (conversionClass == null || string.IsNullOrWhiteSpace(typeName)) {
                return;
            }

            if (!conversionClass.ReferencedClasses.Contains(typeName)) {
                conversionClass.ReferencedClasses.Add(typeName);
            }
        }

        /// <summary>
        /// Resolves a static type reference from a member-access receiver so the emitted class can track body-only type dependencies.
        /// </summary>
        /// <param name="semantic">Semantic model that resolves symbols for the current syntax tree.</param>
        /// <param name="expression">Receiver expression that may name a static type.</param>
        /// <param name="typeName">Resolved static type name when successful.</param>
        /// <returns><c>true</c> when the receiver resolves to a concrete named type; otherwise <c>false</c>.</returns>
        static bool TryResolveReferencedStaticType(SemanticModel semantic, ExpressionSyntax expression, out string typeName) {
            typeName = string.Empty;

            ISymbol? expressionSymbol = semantic.GetSymbolInfo(expression).Symbol;
            if (expressionSymbol is IAliasSymbol aliasSymbol) {
                expressionSymbol = aliasSymbol.Target;
            }

            if (expressionSymbol is INamedTypeSymbol namedTypeSymbol) {
                typeName = namedTypeSymbol.ToDisplayString();
                return !string.IsNullOrWhiteSpace(typeName);
            }

            ITypeSymbol expressionType = semantic.GetTypeInfo(expression).Type;
            if (expressionType is INamedTypeSymbol fallbackNamedTypeSymbol) {
                typeName = fallbackNamedTypeSymbol.ToDisplayString();
                return !string.IsNullOrWhiteSpace(typeName);
            }

            return false;
        }

        protected static void PreProcessObjectCreationExpressionSyntax(SemanticModel semantic, ConversionContext context, ObjectCreationExpressionSyntax objectCreationExpression) {
            string type = objectCreationExpression.Type.ToString();
            ConversionClass cl = context.CurrentClass;
            if (!cl.ReferencedClasses.Contains(type)) {
                cl.ReferencedClasses.Add(type);
            }

            if (objectCreationExpression.ArgumentList != null) {
                foreach (ArgumentSyntax argument in objectCreationExpression.ArgumentList.Arguments) {
                    PreProcessExpression(semantic, context, argument.Expression);
                }
            }

            if (objectCreationExpression.Initializer != null) {
                PreProcessExpression(semantic, context, objectCreationExpression.Initializer);
            }
        }

        protected static void PreProcessDeclaration(SemanticModel semantic, ConversionContext context, VariableDeclarationSyntax declaration) {
            ConversionFunction fn = context.CurrentFunction;

            ConversionClass cl = context.CurrentClass;

            for (int i = 0; i < declaration.Variables.Count; i++) {
                var variable = declaration.Variables[i];

                ConversionFunctionVariableUsage usage = new ConversionFunctionVariableUsage();
                usage.Name = variable.Identifier.ToString();

                string type = declaration.Type.ToString();
                if (type != "var" && !cl.ReferencedClasses.Contains(type)) {
                    cl.ReferencedClasses.Add(type);
                }

                if (type == "var" && variable.Initializer != null) {
                    string inferredType = semantic.GetTypeInfo(variable.Initializer.Value).Type?.ToString();
                    if (!string.IsNullOrWhiteSpace(inferredType) && !cl.ReferencedClasses.Contains(inferredType)) {
                        cl.ReferencedClasses.Add(inferredType);
                    }
                }

                if (variable.Initializer != null) {
                    PreProcessExpression(semantic, context, variable.Initializer.Value);
                }

                fn.BodyVariables.Add(usage);
            }
        }

        protected static void PreProcessAssignmentExpressionSyntax(SemanticModel semantic, ConversionContext context, AssignmentExpressionSyntax assignment) {
            string name;
            if (assignment.Left is IdentifierNameSyntax identifierName) {
                name = identifierName.Identifier.ToString();
            } else {
                return;
                //throw new NotImplementedException();
            }

            ConversionFunction fn = context.CurrentFunction;
            ConversionFunctionVariableUsage usage = fn.BodyVariables.FirstOrDefault(c => c.Name == name);
            if (usage != null) {
                usage.Reassignment = true;
            }
        }

        protected static void PreProcessBlock(SemanticModel semantic, ConversionContext context, BlockSyntax block) {
            foreach (var statement in block.Statements) {
                List<string> newLines = new List<string>();
                PreProcessExpression(semantic, context, statement);
            }
        }

        protected static void PreProcessUsingStatement(SemanticModel semantic, ConversionContext context, UsingStatementSyntax usingStatement) {
            // process the resource declaration (if any)
            if (usingStatement.Declaration != null) {
                PreProcessDeclaration(semantic, context, usingStatement.Declaration);
            } else if (usingStatement.Expression != null) {
                PreProcessExpression(semantic, context, usingStatement.Expression);
            }

            PreProcessExpression(semantic, context, usingStatement.Statement);
        }

        protected static void PreProcessIfStatement(SemanticModel semantic, ConversionContext context, IfStatementSyntax ifStatement) {
            PreProcessExpression(semantic, context, ifStatement.Condition);

            PreProcessExpression(semantic, context, ifStatement.Statement);

            // Process 'else' part if exists
            if (ifStatement.Else != null) {
                if (ifStatement.Else.Statement is IfStatementSyntax elseIfStatement) {
                    PreProcessExpression(semantic, context, elseIfStatement); // Handle else-if cases
                } else {
                    PreProcessExpression(semantic, context, ifStatement.Else.Statement);
                }
            }
        }

        protected static void PreProcessInvocationExpressionSyntax(SemanticModel semantic, ConversionContext context, InvocationExpressionSyntax invocationExpression) {
            PreProcessExpression(semantic, context, invocationExpression.Expression);
        }
    }
}
