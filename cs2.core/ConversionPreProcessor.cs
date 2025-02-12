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

            bool isStatic;
            bool isOverride;
            MemberAccessType access;
            MemberDeclarationType classType;
            MemberUtil.GetModifiers(classDecl.Modifiers, out isStatic, out isOverride, out access, out classType);

            cl.DeclarationType = classType;
            cl.Semantic = semantic;

            if (classDecl.TypeParameterList != null) {
                cl.GenericArgs = new List<string>();
                foreach (var type in classDecl.TypeParameterList.Parameters) {
                    cl.GenericArgs.Add(type.ToString());
                }
            }

            foreach (MemberDeclarationSyntax memberSyntax in classDecl.Members) {
                PreProcessExpression(semantic, memberSyntax, context);
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

        private static void ProcessConstructorDeclaration(SemanticModel semantic, ConstructorDeclarationSyntax constructor, ConversionContext context) {
            bool isStatic;
            bool isOverride;
            MemberAccessType accessType;
            MemberDeclarationType type;
            MemberUtil.GetModifiers(constructor.Modifiers, out isStatic, out isOverride, out accessType, out type);

            ConvertedFunction func = context.StartFn();
            func.IsStatic = isStatic;
            func.AccessType = accessType;
            func.IsConstructor = true;
            func.Name = context.CurrentClass.Name;

            List<ConvertedVariable> inParams = new List<ConvertedVariable>();
            foreach (ParameterSyntax inParam in constructor.ParameterList.ChildNodes()) {
                ConvertedVariable v = new ConvertedVariable();
                v.Name = inParam.Identifier.ToString();
                v.VarType = VariableUtil.GetVarType(inParam.Type, semantic);
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

            ConvertedFunction func = context.StartFn();
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

            foreach (ParameterSyntax inParam in method.ParameterList.ChildNodes()) {
                ConvertedVariable v = new ConvertedVariable();
                v.Name = inParam.Identifier.ToString();
                v.VarType = VariableUtil.GetVarType(inParam.Type!, semantic);

                if (inParam.Default != null) {
                    v.DefaultValue = VariableUtil.ProcessAssignment(inParam.Default);
                }

                func.InParameters.Add(v);
            }

            func.GenericParameters = method.TypeParameterList?
                .Parameters
                .Select(param => param.Identifier.Text)
                .ToList();

            if (method.Body != null) {
                func.RawBlock = method.Body;
            } else if (method.ExpressionBody != null) {
                if (context.CurrentClass.DeclarationType == MemberDeclarationType.Class) {
                    func.ArrowExpression = method.ExpressionBody;
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

            ConvertedFunction func = context.StartFn();
            func.IsStatic = isStatic;
            func.AccessType = access;
            func.Name = "delegate";
            func.Remap = mappedName;

            func.GenericParameters = delegateDecl.TypeParameterList?
                .Parameters
                .Select(param => param.Identifier.Text)
                .ToList();

            // Parameters
            var parameters = new List<ConvertedVariable>();
            foreach (ParameterSyntax parameter in delegateDecl.ParameterList.Parameters) {
                var tsParam = new ConvertedVariable {
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

            ConvertedVariable variable = context.StartVar();
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

            ConvertedVariable variable = context.StartVar();
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
                ConvertedVariable variable = context.StartVar();
                variable.Name = variableDeclarator.Identifier.Text; // Correctly access the identifier
                variable.IsStatic = isStatic;
                variable.AccessType = accessType;
                variable.VarType = VariableUtil.GetVarType(eventDecl.Declaration.Type, semantic); // Access the type from the declaration
                variable.IsOverride = isOverride;
                variable.DeclarationType = declarationType;
            }
        }

        public static void PreProcessExpression(SemanticModel semantic, SyntaxNode exp, ConversionContext context) {
            if (exp is NamespaceDeclarationSyntax nameSpace) {
                string name = nameSpace.Name.ToString();
                if (context.Program.Rules.IgnoredNamespaces.Any(c => name.Contains(c))) {
                    return;
                }

                foreach (var m in nameSpace.Members) {
                    PreProcessExpression(semantic, m, context);
                }
            } else if (exp is ClassDeclarationSyntax classDecl) {
                if (context.Program.Rules.IgnoredClasses.Any(c => classDecl.Identifier.ToString().Contains(c))) {
                    return;
                }

                ProcessClassDeclaration(semantic, classDecl, context);
            } else if (exp is StructDeclarationSyntax structDecl) {
                // declare class
                var cl = context.StartClass();
                cl.Name = structDecl.Identifier.ToString();

                foreach (MemberDeclarationSyntax memberSyntax in structDecl.Members) {
                    PreProcessExpression(semantic, memberSyntax, context);
                }

                context.PopClass();
            } else if (exp is InterfaceDeclarationSyntax ifaceDecl) {
                // declare class
                var cl = context.StartClass();
                cl.Name = ifaceDecl.Identifier.ToString();
                cl.DeclarationType = MemberDeclarationType.Interface;

                foreach (MemberDeclarationSyntax memberSyntax in ifaceDecl.Members) {
                    PreProcessExpression(semantic, memberSyntax, context);
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
                ProcessDelegateDeclaration(semantic,del, context);
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
            } else {
                //Debugger.Break();
            }
        }
    }
}
