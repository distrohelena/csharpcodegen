using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace cs2.core {
    public class ConversionPreProcessor {
        private static void ProcessClassDeclaration(SemanticModel semantic, ClassDeclarationSyntax classDecl, ConversionContext context) {
            // declare class
            var cl = context.StartClass();
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
            cl.Semantic = semantic;
            cl.TypeSymbol = semantic.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;

            if (classDecl.TypeParameterList != null) {
                cl.GenericArgs = new List<string>();
                foreach (var type in classDecl.TypeParameterList.Parameters) {
                    cl.GenericArgs.Add(type.ToString());
                }
            }

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
                        cl.Extensions.Add(type.TypeName);
                    } else {
                        Debugger.Break();
                    }
                }
            }


            context.PopClass();
        }

        private static void ProcessStructDeclaration(SemanticModel semantic, StructDeclarationSyntax structDecl, ConversionContext context) {
            // declare class
            var cl = context.StartClass();
            cl.Name = structDecl.Identifier.ToString();

            bool isStatic;
            bool isOverride;
            MemberAccessType access;
            MemberDeclarationType classType;
            MemberUtil.GetModifiers(structDecl.Modifiers, out isStatic, out isOverride, out access, out classType);

            cl.DeclarationType = classType;
            cl.Semantic = semantic;
            cl.TypeSymbol = semantic.GetDeclaredSymbol(structDecl) as INamedTypeSymbol;

            if (structDecl.TypeParameterList != null) {
                cl.GenericArgs = new List<string>();
                foreach (var type in structDecl.TypeParameterList.Parameters) {
                    cl.GenericArgs.Add(type.ToString());
                }
            }

            foreach (MemberDeclarationSyntax memberSyntax in structDecl.Members) {
                PreProcessExpression(semantic, context, memberSyntax);
            }

            if (structDecl.BaseList != null) {
                foreach (var baseType in structDecl.BaseList.ChildNodes()) {
                    if (baseType is SimpleBaseTypeSyntax) {
                        SimpleBaseTypeSyntax baseSyntax = (SimpleBaseTypeSyntax)baseType;

                        var type = VariableUtil.GetVarType(baseSyntax.Type!, semantic);
                        cl.Extensions.Add(type.TypeName);
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
            func.IsStatic = isStatic;
            func.AccessType = accessType;
            func.IsConstructor = true;
            func.Name = context.CurrentClass.Name;

            List<ConversionVariable> inParams = new List<ConversionVariable>();

            foreach (ParameterSyntax inParam in constructor.ParameterList.ChildNodes()) {
                ConversionVariable v = new ConversionVariable();
                v.Name = inParam.Identifier.ToString();
                v.VarType = VariableUtil.GetVarType(inParam.Type, semantic);

                if (inParam.Default != null) {
                    v.DefaultValue = VariableUtil.ProcessAssignment(inParam.Default);
                }

                inParams.Add(v);
            }
            func.InParameters = inParams;

            var block = constructor.Body;
            if (block != null) {
                func.RawBlock = block;
            }
        }

        private static void ProcessMethodDeclaration(SemanticModel semantic, MethodDeclarationSyntax method, ConversionContext context) {
            string name = method.Identifier.ToString();

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
            func.IsStatic = isStatic;
            func.AccessType = access;
            func.Name = name;
            func.Remap = mappedName;
            func.DeclarationType = type;
            func.IsAsync = MemberUtil.IsAsync(method.Modifiers);

            if (method.ReturnType != null) {
                func.ReturnType = VariableUtil.GetVarType(method.ReturnType, semantic);
                if (func.ReturnType.Type == VariableDataType.Void) {
                    func.ReturnType = null;
                }
            }

            if (context.CurrentClass.Name == "Node" &&
                name == "TryGetEdge") {
                //Debugger.Break();
            }

            foreach (ParameterSyntax inParam in method.ParameterList.ChildNodes()) {
                ConversionVariable v = new ConversionVariable();
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
            func.IsStatic = isStatic;
            func.AccessType = access;
            func.Name = "delegate";
            func.Remap = mappedName;

            func.GenericParameters = delegateDecl.TypeParameterList?
                .Parameters
                .Select(param => param.Identifier.Text)
                .ToList();

            // Parameters
            var parameters = new List<ConversionVariable>();
            foreach (ParameterSyntax parameter in delegateDecl.ParameterList.Parameters) {
                var tsParam = new ConversionVariable {
                    Name = parameter.Identifier.ToString(),
                    VarType = VariableUtil.GetVarType(parameter.Type!, semantic)
                };
                parameters.Add(tsParam);
            }
            func.InParameters = parameters;

            context.PopClass();
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
            VariableDeclaratorSyntax firstVar = declaration.Variables[0];

            bool isStatic;
            bool isOverride;
            MemberAccessType access;
            MemberDeclarationType type;
            MemberUtil.GetModifiers(fMember.Modifiers, out isStatic, out isOverride, out access, out type);

            ConversionVariable variable = context.StartVar();
            variable.Name = firstVar.Identifier.ToString();
            variable.VarType = VariableUtil.GetVarType(declaration.Type, semantic);

            variable.IsStatic = isStatic;
            variable.AccessType = access;
            variable.IsOverride = isOverride;
            variable.DeclarationType = type;

            if (firstVar.Initializer != null) {
                if (firstVar.Initializer.Value is LiteralExpressionSyntax literal) {
                    variable.Assignment = ProcessLiteralExpression(literal, context);
                }
            }

            variable.SetDefaultAssignment();
        }

        private static void ProcessProperty(SemanticModel semantic, PropertyDeclarationSyntax pMember, ConversionContext context) {
            bool isStatic;
            bool isOverride;
            MemberAccessType accessType;
            MemberDeclarationType type;
            MemberUtil.GetModifiers(pMember.Modifiers, out isStatic, out isOverride, out accessType, out type);

            ConversionVariable variable = context.StartVar();
            variable.Name = pMember.Identifier.ToString();
            variable.IsStatic = isStatic;

            variable.AccessType = accessType;
            variable.VarType = VariableUtil.GetVarType(pMember.Type, semantic);
            variable.IsOverride = isOverride;
            variable.DeclarationType = type;

            if (pMember.AccessorList == null) {
                if (pMember.ExpressionBody != null) {
                    variable.ArrowExpression = pMember.ExpressionBody.Expression;
                }
            } else {
                // loop through each accessor (get/set)
                foreach (var accessor in pMember.AccessorList.Accessors) {
                    // check if this accessor is a set accessor
                    if (accessor.Kind() == SyntaxKind.GetAccessorDeclaration) {
                        if (accessor.Body == null) {
                            variable.IsGet = true;
                        } else {
                            variable.GetBlock = accessor.Body;
                        }
                    } else if (accessor.Kind() == SyntaxKind.SetAccessorDeclaration) {
                        if (accessor.Body == null) {
                            variable.IsSet = true;
                        } else {
                            variable.SetBlock = accessor.Body;
                        }
                    }
                }
            }
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
                variable.Name = variableDeclarator.Identifier.Text; // Correctly access the identifier
                variable.IsStatic = isStatic;
                variable.AccessType = accessType;
                variable.VarType = VariableUtil.GetVarType(eventDecl.Declaration.Type, semantic); // Access the type from the declaration
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
                // declare class
                var cl = context.StartClass();
                cl.Name = ifaceDecl.Identifier.ToString();
                cl.DeclarationType = MemberDeclarationType.Interface;
                cl.Semantic = semantic;
                cl.TypeSymbol = semantic.GetDeclaredSymbol(ifaceDecl) as INamedTypeSymbol;

                foreach (MemberDeclarationSyntax memberSyntax in ifaceDecl.Members) {
                    PreProcessExpression(semantic, context, memberSyntax);
                }

                if (ifaceDecl.BaseList != null) {
                    foreach (var baseType in ifaceDecl.BaseList.ChildNodes()) {
                        if (baseType is SimpleBaseTypeSyntax) {
                            SimpleBaseTypeSyntax baseSyntax = (SimpleBaseTypeSyntax)baseType;
                            string type = baseSyntax.Type.ToString();
                            cl.Extensions.Add(type);
                        }
                    }
                }

                context.PopClass();
            } else if (exp is EnumDeclarationSyntax) {
                EnumDeclarationSyntax Enum = (EnumDeclarationSyntax)exp;

                var cl = context.StartClass();
                cl.DeclarationType = MemberDeclarationType.Enum;
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
            } else if (exp is ConstructorDeclarationSyntax constructor) {
                ProcessConstructorDeclaration(semantic, constructor, context);
            } else if (exp is FieldDeclarationSyntax field) {
                // int x = 0;
                ProcessField(semantic, field, context);
            } else if (exp is PropertyDeclarationSyntax prop) {
                // int x { get; set; }
                ProcessProperty(semantic, prop, context);
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
            } else if (exp is IfStatementSyntax ifStatement) {
                PreProcessIfStatement(semantic, context, ifStatement);
            } else if (exp is MemberAccessExpressionSyntax memberAccess) {
                PreProcessMemberAccessExpressionSyntax(semantic, context, memberAccess);
            } else if (exp is IdentifierNameSyntax identifier) {
                PreProcessIdentifierNameSyntax(semantic, context, identifier);
            } else if (exp is ReturnStatementSyntax ret) {
                PreProcessReturnStatementSyntax(semantic, context, ret);
            } else if (exp is TryStatementSyntax tryStatement) {
                PreProcessTryStatementSyntax(semantic, context, tryStatement);
            } else if (exp is BinaryExpressionSyntax ||
                        exp is ThrowStatementSyntax ||
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

            string type = typeInfo.ConvertedType.ToString();
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

            return PreProcessExpression(semantic, context, memberAccess.Name);
        }

        protected static void PreProcessDeclaration(SemanticModel semantic, ConversionContext context, VariableDeclarationSyntax declaration) {
            ConversionFunction fn = context.CurrentFunction;

            ConversionClass cl = context.CurrentClass;

            for (int i = 0; i < declaration.Variables.Count; i++) {
                var variable = declaration.Variables[i];

                ConversionFunctionVariableUsage usage = new ConversionFunctionVariableUsage();
                usage.Name = variable.Identifier.ToString();

                string type = declaration.Type.ToString();
                if (!cl.ReferencedClasses.Contains(type)) {
                    cl.ReferencedClasses.Add(type);
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
                throw new NotImplementedException();
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
