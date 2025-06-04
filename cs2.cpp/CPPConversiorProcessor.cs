﻿using cs2.core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nucleus;
using System.Text.RegularExpressions;

namespace cs2.cpp {
    public class CPPConversiorProcessor : ConversionProcessor {
        private CPPCodeConverter codeConverter;

        public CPPConversiorProcessor(CPPCodeConverter converter) {
            codeConverter = converter;
        }

        protected override void ProcessAssignmentExpressionSyntax(SemanticModel semantic, LayerContext context, AssignmentExpressionSyntax assignment, List<string> lines) {
            int startDepth = context.Class.Count;
            ExpressionResult assignResult = ProcessExpression(semantic, context, assignment.Left, lines);
            context.PopClass(startDepth);

            string operatorVal = assignment.OperatorToken.ToString();
            if (assignResult.Type?.Type == VariableDataType.Callback && (operatorVal == "+=" || operatorVal == "-=")) {
                lines.Add($" = ");
            } else {
                lines.Add($" {operatorVal} ");
            }

            startDepth = context.Class.Count;
            ProcessExpression(semantic, context, assignment.Right, lines);
            context.PopClass(startDepth);
        }

        protected override ExpressionResult ProcessIdentifierNameSyntax(SemanticModel semantic, LayerContext context, IdentifierNameSyntax identifier, List<string> lines, List<ExpressionResult> refTypes) {
            string name = identifier.ToString();
            bool isMethod = false;

            ISymbol? nsSymbol = semantic.GetSymbolInfo(identifier).Symbol;
            if (nsSymbol is INamespaceSymbol namespaceSymbol) {
                if (namespaceSymbol.IsNamespace) {
                    return new ExpressionResult(false);
                }
            } else if (nsSymbol is IMethodSymbol methodSymbol) {
                isMethod = true;
            }

            VariablePath varPath = VariablePath.Unknown;
            if (nsSymbol is INamespaceOrTypeSymbol nameSpaceType) {
                bool isType = nameSpaceType.IsType;
                if (isType) {
                    varPath = VariablePath.Static;
                }
            }

            int layer = context.GetClassLayer();

            // abstract class
            ConversionClass? staticClass = context.Program.Classes.Find(c => c.Name == name);

            ConversionClass? currentClass = context.GetCurrentClass();

            var classVars = new List<ConversionVariable>();
            context.Class.ForEach(fn => {
                var result = fn?.Variables.Where(var => var.Name == name);
                if (result != null) {
                    classVars.AddRange(result);
                }
            });

            // variable from the current class
            ConversionVariable? classVar = currentClass?.Variables.Find(c => c.Name == name);

            if (classVar == null && staticClass == null) {
                string camelCame = StringUtil.ToCamelCase(name);
                classVar = currentClass?.Variables.Find(c => c.Name == camelCame);
                if (classVar != null) {
                    name = camelCame;
                }
            }

            // function from the current class
            ConversionFunction? classFn = currentClass?.Functions.Find(c => c.Name == name);

            bool paramsMatch = classFn?.InParameters?.Count == refTypes?.Count;

            // here: dynamic system for typed functions. Like BinaryWriter writeByte, writeInt
            if (currentClass != null && classFn == null && isMethod) {
                // search for closest version
                string lowercase = name.ToLowerInvariant();
                string searchName = lowercase;
                for (int i = 0; i < refTypes?.Count; i++) {
                    ExpressionResult result = refTypes[i];
                    if (result.Type == null) {
                        continue;
                    }

                    string typeName = result.Type.TypeName;
                    searchName += StringUtil.CapitalizerFirstLetter(typeName);
                }

                ConversionFunction similarFn = currentClass.Functions.Find(c => c.Name == searchName);
                if (similarFn == null) {
                    // search for lower first letter
                    lowercase = name[0].ToString().ToLowerInvariant() + name.Remove(0, 1);

                    similarFn = currentClass.Functions.Find(c => c.Name == lowercase);
                    if (similarFn != null) {
                        classFn = similarFn;
                        name = similarFn.Name;
                    }
                } else {
                    classFn = similarFn;
                    name = similarFn.Name;
                }
            } else if (!paramsMatch && classFn != null && classFn.InParameters != null && refTypes != null) {
                ConversionFunction overload = currentClass.Functions.Find(c => {
                    if (c.Name.StartsWith(name)) {
                        // check in parameters
                        if (c.InParameters == null ||
                        c.InParameters.Count != refTypes.Count) {
                            return false;
                        }

                        for (int i = 0; i < c.InParameters.Count; i++) {
                            ConversionVariable inParam = c.InParameters[i];
                            ExpressionResult result = refTypes[i];
                            if (result.Type == null) {
                                continue;
                            }


                            if (inParam.VarType.ToString() != result.Type.ToString()) {
                                return false;
                            }
                        }

                        return true;

                    }
                    return false;
                });

                if (overload != null) {
                    classFn = overload;
                }
            }

            // current function
            FunctionStack? currentFn = context.GetCurrentFunction();
            // in-parameter for the current function
            ConversionVariable? functionInVar = currentFn?.Function.InParameters?.Find(c => c.Name == name);

            // current stack
            ConversionVariable? stackVar = currentFn?.Stack.Find(c => c.Name == name);

            var matchingVars = new List<ConversionVariable>();
            context.Function.ForEach(fn => {
                var result = fn?.Stack.Where(var => var.Name == name);
                if (result != null) {
                    matchingVars.AddRange(result);
                }
            });

            if (currentClass == null) {
                lines.Add(name);
            } else {
                if (layer == 1) {
                    bool isClassVar = (classVar != null &&
                        functionInVar == null &&
                        matchingVars.Count == 0) ||
                        (classFn != null &&
                        functionInVar == null &&
                        matchingVars.Count == 0);


                    if (isClassVar) {
                        if (lines.Count > 1) {
                            string b2 = lines[lines.Count - 2];
                            string b1 = lines[lines.Count - 1];

                            if (b2 == "this" && b1.IndexOf(";") == -1) {
                            } else {
                                lines.Add("this.");
                            }
                        } else {
                            // semantic
                            ISymbol? symbol = semantic.GetSymbolInfo(identifier).Symbol;
                            if (symbol is INamedTypeSymbol namedTypeSymbol) {
                                if (!namedTypeSymbol.IsStatic &&
                                    !namedTypeSymbol.IsType) {
                                    lines.Add("this.");
                                }
                            } else {
                                lines.Add("this.");
                            }
                        }
                    }
                }

                if (classFn == null || string.IsNullOrEmpty(classFn.Remap)) {
                    ConversionVariable? varOnClass = currentClass.Variables.FirstOrDefault(c => c.Name == name);
                    if (varOnClass == null || string.IsNullOrEmpty(varOnClass.Remap)) {
                        lines.Add(name);
                    } else {
                        lines.Add(varOnClass.Remap);
                    }
                } else {
                    lines.Add(classFn.Remap);
                }
            }

            if (stackVar != null) {
                varPath = VariablePath.FunctionStack;
                ConversionClass cl = context.Program.Classes.Find(c => c.Name == stackVar.VarType.GetTypeScriptType(context.Program));
                context.AddClass(cl);
                ExpressionResult res = new ExpressionResult(true, varPath, stackVar.VarType);
                res.Class = cl;
                res.Variable = stackVar;
                return res;
            } else if (functionInVar != null) {
                ConversionClass cl = context.Program.Classes.Find(c => c.Name == functionInVar.VarType.GetTypeScriptType(context.Program));
                context.AddClass(cl);
                ExpressionResult res = new ExpressionResult(true, varPath, functionInVar.VarType);
                res.Class = cl;
                res.Variable = functionInVar;
                return res;
            } else if (classVar != null) {
                ConversionClass cl = context.Program.Classes.Find(c => c.Name == classVar.VarType.GetTypeScriptType(context.Program));
                context.AddClass(cl);
                ExpressionResult res = new ExpressionResult(true, varPath, classVar.VarType);
                res.Class = cl;
                res.Variable = classVar;
                return res;
            } else if (staticClass != null) {
                context.AddClass(staticClass);
                ExpressionResult res = new ExpressionResult(true, varPath, null);
                res.Class = staticClass;
                return res;
            } else if (classFn != null) {
                if (classFn.ReturnType != null) {
                    // invoked function
                    if (classFn.ReturnType.Type != VariableDataType.Void) {
                        context.AddClass(context.Program.Classes.Find(c => c.Name == classFn.ReturnType.GetTypeScriptType(context.Program)));
                        return new ExpressionResult(true, varPath, classFn.ReturnType);
                    }
                }
            } else {
                //Debugger.Break();
            }

            return new ExpressionResult(true);
        }

        protected override ExpressionResult ProcessObjectCreationExpressionSyntax(SemanticModel semantic, LayerContext context, ObjectCreationExpressionSyntax objectCreation, List<string> lines) {
            lines.Add("new ");

            int startDepth = context.DepthClass;
            ExpressionResult typeResult = ProcessExpression(semantic, context, objectCreation.Type, lines);
            context.PopClass(startDepth);

            if (objectCreation.ArgumentList == null) {
                ProcessExpression(semantic, context, objectCreation.Initializer, lines);
            } else {
                lines.Add("(");
                for (int i = 0; i < objectCreation.ArgumentList.Arguments.Count; i++) {
                    var arg = objectCreation.ArgumentList.Arguments[i];

                    int startArg = context.DepthClass;
                    ExpressionResult expResult = ProcessExpression(semantic, context, arg.Expression, lines);
                    context.PopClass(startArg);

                    if (i != objectCreation.ArgumentList.Arguments.Count - 1) {
                        lines.Add(", ");
                    }
                }
                lines.Add(")");
            }

            return new ExpressionResult(false);
        }

        protected override ExpressionResult ProcessMemberAccessExpressionSyntax(SemanticModel semantic, LayerContext context, MemberAccessExpressionSyntax memberAccess, List<string> lines, List<ExpressionResult> refTypes) {
            ExpressionResult result = ProcessExpression(semantic, context, memberAccess.Expression, lines);
            if (result.Processed) {
                // shit is this static access, pointer access or direct access
                switch (result.VarPath) {
                    case VariablePath.Static:
                        lines.Add("::");
                        break;
                    default: {
                            lines.Add("->");

                            if (result.Variable != null) {
                                int xx = -1;
                            }
                        }
                        break;
                }
            }
            return ProcessExpression(semantic, context, memberAccess.Name, lines, refTypes);
        }

        protected override ExpressionResult ProcessInvocationExpressionSyntax(SemanticModel semantic, LayerContext context, InvocationExpressionSyntax invocationExpression, List<string> lines) {
            List<string> argLines = ["("];
            int count = 0;
            List<ExpressionResult> types = new List<ExpressionResult>();

            Dictionary<string, string> outs = new Dictionary<string, string>();

            List<string> beforeLines = new List<string>();
            List<string> addLines = new List<string>();

            foreach (var arg in invocationExpression.ArgumentList.Arguments) {
                string refKeyword = arg.RefKindKeyword.ToString();
                string strName = string.Empty;
                bool isRef = false;
                if (refKeyword != "") {
                    isRef = true;
                    beforeLines.Add("let ");
                    strName = "out_" + Guid.NewGuid().ToString().ToLower().Remove(16);
                    strName = StringUtil.Replace(strName, "-", "");
                    beforeLines.Add(strName);
                    beforeLines.Add(" = { value: null };\n");
                }

                int startArg = context.DepthClass;
                int argLinesIndex = argLines.Count;
                types.Add(ProcessExpression(semantic, context, arg.Expression, argLines));
                context.PopClass(startArg);

                if (isRef) {
                    string outName = argLines[argLinesIndex];
                    outs.Add(outName, strName);
                    argLines.RemoveAt(argLinesIndex);
                    argLines.Add(strName);
                }

                count++;
                if (count != invocationExpression.ArgumentList.Arguments.Count) {
                    argLines.Add(", ");
                }
            }
            argLines.Add(")");

            int start = context.DepthClass;
            ExpressionResult result = ProcessExpression(semantic, context, invocationExpression.Expression, lines, types);
            context.PopClass(start);

            lines.AddRange(argLines);

            foreach (var pair in outs) {
                addLines.Add($"{pair.Key} = {pair.Value}.value;\n");
            }

            result.BeforeLines = beforeLines;
            result.AfterLines = addLines;
            return result;
        }

        protected override void ProcessThisExpressionSyntax(SemanticModel semantic, LayerContext context, ThisExpressionSyntax thisExpression, List<string> lines) {
            lines.Add("this");
            context.AddClass(context.Class[0]);
        }

        protected override ExpressionResult ProcessBinaryExpressionSyntax(SemanticModel semantic, LayerContext context, BinaryExpressionSyntax binary, List<string> lines) {
            BinaryOpTypes op = ParseBinaryExpression(semantic, context, binary, out List<string> left, out List<string> right, out ExpressionResult result);
            lines.AddRange(left);

            lines.Add($" {op.ToStringOperator()} ");

            lines.AddRange(right);
            return result;
        }

        protected override void ProcessGenericNameSyntax(SemanticModel semantic, LayerContext context, GenericNameSyntax generic, List<string> lines) {
            lines.Add(generic.Identifier.ToString());

            lines.Add("<");

            int count = generic.TypeArgumentList.Arguments.Count;
            int i = 0;
            foreach (var genType in generic.TypeArgumentList.Arguments) {
                VariableType type = VariableUtil.GetVarType(genType, semantic);
                lines.Add(type.ToCPPString(context.Program));

                if (i < count - 1) {
                    lines.Add(",");
                }

                i++;
            }
            lines.Add(">");
        }

        protected override void ProcessImplicitArrayCreationExpression(SemanticModel semantic, LayerContext context, ImplicitArrayCreationExpressionSyntax implicitArray, List<string> lines) {
            // Start the array literal
            lines.Add("[");

            // Process each expression in the initializer
            for (int i = 0; i < implicitArray.Initializer.Expressions.Count; i++) {
                ProcessExpression(semantic, context, implicitArray.Initializer.Expressions[i], lines);

                // Add a comma separator if it's not the last element
                if (i < implicitArray.Initializer.Expressions.Count - 1) {
                    lines.Add(", ");
                }
            }

            // Close the array literal
            lines.Add("]");
        }

        protected override void ProcessAwait(SemanticModel semantic, LayerContext context, AwaitExpressionSyntax awaitExpression, List<string> lines) {
            lines.Add("await ");

            ProcessExpression(semantic, context, awaitExpression.Expression, lines);
        }

        protected override ExpressionResult ProcessQualifiedName(SemanticModel semantic, LayerContext context, QualifiedNameSyntax qualifiedName, List<string> lines) {
            // Process the left part of the qualified name (e.g., "System" in "System.Console")
            if (ProcessExpression(semantic, context, qualifiedName.Left, lines).Processed) {
                // Add the dot separator
                lines.Add(".");
            }

            // Process the right part of the qualified name (e.g., "Console" in "System.Console")
            return ProcessExpression(semantic, context, qualifiedName.Right, lines);
        }

        protected override void ProcessTypeOfExpression(SemanticModel semantic, LayerContext context, TypeOfExpressionSyntax typeOfExpression, List<string> lines) {
            lines.Add("typeof ");
            ProcessExpression(semantic, context, typeOfExpression.Type, lines);
        }

        protected override void ProcessSimpleLambdaExpression(SemanticModel semantic, LayerContext context, SimpleLambdaExpressionSyntax simpleLambda, List<string> lines) {
            // Add the parameter of the lambda
            lines.Add(simpleLambda.Parameter.Identifier.Text);

            // Add the arrow (=>) for TypeScript arrow function
            lines.Add(" => ");

            // Process the body of the lambda, which could be an expression or a block
            if (simpleLambda.Body is ExpressionSyntax) {
                ProcessExpression(semantic, context, (ExpressionSyntax)simpleLambda.Body, lines);
            } else if (simpleLambda.Body is BlockSyntax) {
                lines.Add("{\n");
                ProcessStatement(semantic, context, (BlockSyntax)simpleLambda.Body, lines);
                lines.Add("}\n");
            }
        }

        protected override void ProcessArrayCreationExpression(SemanticModel semantic, LayerContext context, ArrayCreationExpressionSyntax arrayCreation, List<string> lines) {
            // Check if there's an initializer (e.g., new int[] { 1, 2, 3 })
            if (arrayCreation.Initializer != null) {
                lines.Add("[");
                for (int i = 0; i < arrayCreation.Initializer.Expressions.Count; i++) {
                    ProcessExpression(semantic, context, arrayCreation.Initializer.Expressions[i], lines);

                    if (i < arrayCreation.Initializer.Expressions.Count - 1) {
                        lines.Add(", ");
                    }
                }
                lines.Add("]");
            }
            // If it's an array with specified size (e.g., new int[5])
            else if (arrayCreation.Type.RankSpecifiers.Any()) {
                lines.Add("new Array(");
                foreach (var rankSpecifier in arrayCreation.Type.RankSpecifiers) {
                    foreach (var size in rankSpecifier.Sizes) {
                        ProcessExpression(semantic, context, size, lines);
                    }
                }
                lines.Add(")");
            }
        }

        protected override void ProcessParenthesizedExpression(SemanticModel semantic, LayerContext context, ParenthesizedExpressionSyntax parenthesizedExpression, List<string> lines) {
            lines.Add("(");
            ProcessExpression(semantic, context, parenthesizedExpression.Expression, lines);
            lines.Add(")");
        }

        protected override void ProcessBaseExpression(SemanticModel semantic, LayerContext context, BaseExpressionSyntax baseExpression, List<string> lines) {
            lines.Add("super");

            context.AddClass(context.GetCurrentClass());

        }

        protected override void ProcessInitializerExpression(SemanticModel semantic, LayerContext context, InitializerExpressionSyntax initializerExpression, List<string> lines) {
            bool isArray = false;
            if (initializerExpression.Kind().ToString() == "ArrayInitializerExpression") {
                isArray = true;
                lines.Add("[ ");
            } else {
                lines.Add("{ ");
            }

            for (int i = 0; i < initializerExpression.Expressions.Count; i++) {
                ProcessExpression(semantic, context, initializerExpression.Expressions[i], lines);

                if (i < initializerExpression.Expressions.Count - 1) {
                    lines.Add(", ");
                }
            }

            if (isArray) {
                lines.Add(" ]");
            } else {
                lines.Add(" }");
            }
        }

        protected override void ProcessTupleExpression(SemanticModel semantic, LayerContext context, TupleExpressionSyntax tupleExpression, List<string> lines) {
            lines.Add("[");

            for (int i = 0; i < tupleExpression.Arguments.Count; i++) {
                ProcessExpression(semantic, context, tupleExpression.Arguments[i].Expression, lines);

                if (i < tupleExpression.Arguments.Count - 1) {
                    lines.Add(", ");
                }
            }

            lines.Add("]");
        }


        protected override void ProcessPredefinedType(SemanticModel semantic, LayerContext context, PredefinedTypeSyntax predefinedType, List<string> lines) {
            var type = predefinedType.Keyword.ValueText;

            string name = type;
            switch (type) {
                case "int":
                case "long":
                case "float":
                case "double":
                case "decimal":
                case "short":
                case "byte":
                    name = "Number";
                    break;
                case "bool":
                    name = "Boolean";
                    break;
                case "string":
                    name = "Boolean";
                    break;
                case "char":
                    name = "String";
                    break;
                case "object":
                    name = "any";
                    break;
                case "void":
                    name = "void";
                    break;
            }

            lines.Add(name);
            context.AddClass(context.Program.Classes.Find(c => c.Name == name));
        }


        protected override void ProcessDefaultExpression(SemanticModel semantic, LayerContext context, DefaultExpressionSyntax defaultExpression, List<string> lines) {
            var type = defaultExpression.Type.ToString();

            // Add the default value based on the type
            if (type == "int" || type == "float" || type == "double" || type == "decimal" || type == "long" || type == "short" || type == "byte") {
                lines.Add("0");
            } else if (type == "bool") {
                lines.Add("false");
            } else if (type == "char") {
                lines.Add("'\\0'");
            } else {
                lines.Add("null"); // Default to null for reference types or unknown types
            }
        }

        protected override ExpressionResult ProcessInterpolatedStringExpression(SemanticModel semantic, LayerContext context, InterpolatedStringExpressionSyntax interpolatedString, List<string> lines) {
            // Add the backtick to start the template literal
            lines.Add("`");

            // Process each content part inside the interpolated string
            foreach (var content in interpolatedString.Contents) {
                if (content is InterpolationSyntax interpolation) {
                    // For interpolated expressions, wrap them in ${}
                    lines.Add("${");
                    ProcessExpression(semantic, context, interpolation.Expression, lines);
                    lines.Add("}");
                } else if (content is InterpolatedStringTextSyntax text) {
                    // Regular string content
                    lines.Add(text.TextToken.Text);
                }
            }

            // Add the backtick to close the template literal
            lines.Add("`");

            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
        }

        protected override void ProcessElementAccessExpression(SemanticModel semantic, LayerContext context, ElementAccessExpressionSyntax elementAccess, List<string> lines) {
            // Process the expression being accessed (e.g., array or object)
            int startClass = context.DepthClass;
            ProcessExpression(semantic, context, elementAccess.Expression, lines);
            List<ConversionClass> saved = context.SavePopClass(startClass);

            // Add the opening bracket
            lines.Add("[");

            // Process the index argument
            foreach (var argument in elementAccess.ArgumentList.Arguments) {
                startClass = context.DepthClass;
                ProcessExpression(semantic, context, argument.Expression, lines);
                context.PopClass(startClass);

            }

            // Add the closing bracket
            lines.Add("]");

            context.LoadClass(saved);
        }

        protected override void ProcessPostfixUnaryExpression(SemanticModel semantic, LayerContext context, PostfixUnaryExpressionSyntax postfixUnary, List<string> lines) {
            // Process the operand first
            int start = context.DepthClass;
            ProcessExpression(semantic, context, postfixUnary.Operand, lines);
            context.PopClass(start);

            // Add the postfix operator (e.g., ++ or --)
            lines.Add(postfixUnary.OperatorToken.ToString());
        }

        protected override ExpressionResult ProcessPrefixUnaryExpression(SemanticModel semantic, LayerContext context, PrefixUnaryExpressionSyntax prefixUnary, List<string> lines) {
            // Map the operator to the corresponding TypeScript operator
            string operatorSymbol = prefixUnary.OperatorToken.ToString();
            lines.Add(operatorSymbol);

            // Process the operand
            int start = context.DepthClass;
            ExpressionResult result = ProcessExpression(semantic, context, prefixUnary.Operand, lines);
            context.PopClass(start);

            return result;
        }

        protected override void ProcessMemberBindingExpression(SemanticModel semantic, LayerContext context, MemberBindingExpressionSyntax memberBinding, List<string> lines) {
            // Access the member (property, method, etc.)
            lines.Add(memberBinding.Name.ToString());
        }

        protected override void ProcessConditionalAccessExpression(SemanticModel semantic, LayerContext context, ConditionalAccessExpressionSyntax conditionalAccess, List<string> lines) {
            // Process the expression being accessed conditionally
            ProcessExpression(semantic, context, conditionalAccess.Expression, lines);
            lines.Add("?.");

            // Process the member or invocation being accessed conditionally
            ProcessExpression(semantic, context, (ExpressionSyntax)conditionalAccess.WhenNotNull, lines);
        }

        protected override ExpressionResult ProcessCastExpression(SemanticModel semantic, LayerContext context, CastExpressionSyntax castExpr, List<string> lines) {
            VariableType varType = VariableUtil.GetVarType(castExpr.Type, semantic);

            lines.Add("<");
            lines.Add(varType.ToCPPString(context.Program)); // Type of the cast
            lines.Add(">");
            lines.Add("<unknown>");

            ProcessExpression(semantic, context, castExpr.Expression, lines); // Expression being cast

            return new ExpressionResult(true, VariablePath.Unknown, varType);
        }

        protected override void ProcessConditionalExpression(SemanticModel semantic, LayerContext context, ConditionalExpressionSyntax conditional, List<string> lines) {
            // Process the condition (before the ?)
            ProcessExpression(semantic, context, conditional.Condition, lines);
            lines.Add(" ? ");

            // Process the true branch (after the ? and before the :)
            ProcessExpression(semantic, context, conditional.WhenTrue, lines);
            lines.Add(" : ");

            // Process the false branch (after the :)
            ProcessExpression(semantic, context, conditional.WhenFalse, lines);
        }

        protected override void ProcessLambdaExpression(SemanticModel semantic, LayerContext context, ParenthesizedLambdaExpressionSyntax lambda, List<string> lines) {
            lines.Add("(");
            for (int i = 0; i < lambda.ParameterList.Parameters.Count; i++) {
                var parameter = lambda.ParameterList.Parameters[i];
                lines.Add(parameter.Identifier.ToString());

                if (i < lambda.ParameterList.Parameters.Count - 1) {
                    lines.Add(", ");
                }
            }
            lines.Add(") => ");

            if (lambda.Body is BlockSyntax block) {
                lines.Add("{\n");
                ProcessBlock(semantic, context, block, lines);
                lines.Add("}\n");
            } else {
                ProcessExpression(semantic, context, (ExpressionSyntax)lambda.Body, lines);
                lines.Add(";\n");
            }
        }

        protected override void ProcessEmptyStatement(SemanticModel semantic, LayerContext context, EmptyStatementSyntax emptyStatement, List<string> lines) {
            lines.Add(";\n");
        }

        protected override void ProcessDoStatement(SemanticModel semantic, LayerContext context, DoStatementSyntax doStatement, List<string> lines) {
            // Start the `do` block
            lines.Add("do {\n");

            // Process the body of the `do` statement
            int start = context.DepthClass;
            ProcessStatement(semantic, context, doStatement.Statement, lines);
            context.PopClass(start);

            // Close the `do` block and start the `while` condition
            lines.Add("} while (");

            // Process the condition expression
            int start2 = context.DepthClass;
            ProcessExpression(semantic, context, doStatement.Condition, lines);
            context.PopClass(start2);

            // Close the `while` statement
            lines.Add(");\n");
        }


        protected override void ProcessUsingStatement(SemanticModel semantic, LayerContext context, UsingStatementSyntax usingStatement, List<string> lines) {
            lines.Add("try {\n");

            // process the resource declaration (if any)
            if (usingStatement.Declaration != null) {
                ProcessDeclaration(semantic, context, usingStatement.Declaration, lines);
                lines.Add(";\n");
            } else if (usingStatement.Expression != null) {
                ProcessExpression(semantic, context, usingStatement.Expression, lines);
                lines.Add(";\n");
            }

            // process the body of the using statement
            ProcessStatement(semantic, context, usingStatement.Statement, lines);

            lines.Add("} catch () {\n");
            lines.Add("}\n");

            // optionally, add resource disposal logic in the finally block
            if (usingStatement.Declaration != null) {
                foreach (var variable in usingStatement.Declaration.Variables) {
                    lines.Add($"{variable.Identifier.Text}.dispose();\n");
                }
            } else if (usingStatement.Expression != null) {
                lines.Add(usingStatement.Expression.ToString() + ".dispose();\n");
            }

        }

        protected override void ProcessLockStatement(SemanticModel semantic, LayerContext context, LockStatementSyntax lockStatement, List<string> lines) {
            // You can implement custom locking logic here if needed, otherwise omit the lock
            lines.Add("// Lock omitted in TypeScript\n");

            // Process the body of the lock statement
            ProcessStatement(semantic, context, lockStatement.Statement, lines);
        }


        protected override void ProcessTryStatement(SemanticModel semantic, LayerContext context, TryStatementSyntax tryStatement, List<string> lines) {
            // Process the 'try' block
            lines.Add("try {\n");
            ProcessStatement(semantic, context, tryStatement.Block, lines);
            lines.Add("}\n");

            // Process the 'catch' block(s)
            foreach (var catchClause in tryStatement.Catches) {
                lines.Add("catch (");
                if (catchClause.Declaration != null) {
                    lines.Add(catchClause.Declaration.Identifier.Text);
                } else {
                    lines.Add("err"); // Default error variable if none provided
                }
                lines.Add(") {\n");
                ProcessStatement(semantic, context, catchClause.Block, lines);
                lines.Add("}\n");
            }

            // Process the 'finally' block, if it exists
            if (tryStatement.Finally != null) {
                lines.Add("finally {\n");
                ProcessStatement(semantic, context, tryStatement.Finally.Block, lines);
                lines.Add("}\n");
            }
        }

        protected override void ProcessForEachStatement(SemanticModel semantic, LayerContext context, ForEachStatementSyntax forEachStatement, List<string> lines) {
            lines.Add("for (let ");
            lines.Add(forEachStatement.Identifier.Text);
            lines.Add(" of ");
            ProcessExpression(semantic, context, forEachStatement.Expression, lines);
            lines.Add(") {\n");

            // Process the body of the forEach loop
            ProcessStatement(semantic, context, forEachStatement.Statement, lines);

            lines.Add("}\n");
        }

        protected override void ProcessContinueStatement(SemanticModel semantic, LayerContext context, ContinueStatementSyntax continueStatement, List<string> lines) {
            lines.Add("continue;\n");
        }

        protected override void ProcessWhileStatement(SemanticModel semantic, LayerContext context, WhileStatementSyntax whileStatement, List<string> lines) {
            lines.Add("while (");
            ProcessExpression(semantic, context, whileStatement.Condition, lines);
            lines.Add(") {\n");

            // Process the body of the while loop
            ProcessStatement(semantic, context, whileStatement.Statement, lines);

            lines.Add("}\n");
        }

        protected override void ProcessForStatement(SemanticModel semantic, LayerContext context, ForStatementSyntax forStatement, List<string> lines) {
            lines.Add("for (");

            // Process initialization (if it exists)
            if (forStatement.Declaration != null) {
                ProcessDeclaration(semantic, context, forStatement.Declaration, lines);
            } else if (forStatement.Initializers.Any()) {
                foreach (var initializer in forStatement.Initializers) {
                    ProcessExpression(semantic, context, initializer, lines);
                }
            }
            lines.Add("; ");

            // Process condition (if it exists)
            if (forStatement.Condition != null) {
                int startClass = context.DepthClass;
                ProcessExpression(semantic, context, forStatement.Condition, lines);
                context.PopClass(startClass);
            }

            lines.Add("; ");

            // Process incrementors
            foreach (var incrementor in forStatement.Incrementors) {
                int startClass = context.DepthClass;
                ProcessExpression(semantic, context, incrementor, lines);
                context.PopClass(startClass);
            }
            lines.Add(") {\n");

            // Process the body of the for loop
            ProcessStatement(semantic, context, forStatement.Statement, lines);

            lines.Add("}\n");
        }

        protected override ExpressionResult ProcessIfStatement(SemanticModel semantic, LayerContext context, IfStatementSyntax ifStatement, List<string> lines) {
            lines.Add("    if (");

            int start = context.DepthClass;
            ExpressionResult condResult = ProcessExpression(semantic, context, ifStatement.Condition, lines);
            context.PopClass(start);

            lines.Add(")\n");
            lines.Add("    {\n");

            // Process the 'then' statements
            ExpressionResult result = ProcessStatement(semantic, context, ifStatement.Statement, lines);
            lines.Add("    }\n");

            // Process 'else' part if exists
            if (ifStatement.Else != null) {
                lines.Add("else ");
                if (ifStatement.Else.Statement is IfStatementSyntax elseIfStatement) {
                    ProcessIfStatement(semantic, context, elseIfStatement, lines); // Handle else-if cases
                } else {
                    lines.Add("{\n");
                    ProcessStatement(semantic, context, ifStatement.Else.Statement, lines);
                    lines.Add("}\n");
                }
            }

            return condResult;
        }

        protected override void ProcessThrowStatement(SemanticModel semantic, LayerContext context, ThrowStatementSyntax throwStatement, List<string> lines) {
            if (throwStatement.Expression == null) {
                lines.Add("throw new Error('Throw empty. TODO: Throw exception');\n");
            } else {
                lines.Add("throw ");
                ProcessExpression(semantic, context, throwStatement.Expression, lines);
                lines.Add(";\n");
            }
        }

        protected override void ProcessSwitchStatement(SemanticModel semantic, LayerContext context, SwitchStatementSyntax switchStatement, List<string> lines) {
            lines.Add("switch (");
            int depth = context.DepthClass;
            ProcessExpression(semantic, context, switchStatement.Expression, lines);
            context.PopClass(depth);
            lines.Add(") {\n");

            foreach (var section in switchStatement.Sections) {
                foreach (var label in section.Labels) {
                    if (label is CaseSwitchLabelSyntax caseLabel) {
                        lines.Add("case ");

                        depth = context.DepthClass;
                        ProcessExpression(semantic, context, caseLabel.Value, lines);
                        context.PopClass(depth);

                        lines.Add(":");

                    } else if (label is DefaultSwitchLabelSyntax) {
                        lines.Add("default: ");
                    }

                }
                lines.Add(" {\n");

                foreach (var stmt in section.Statements) {
                    ProcessStatement(semantic, context, stmt, lines);
                }
                lines.Add("}\n");
            }

            lines.Add("}\n\n");
        }

        public VariableType ConvertToCPPType(VariableType parsedType, out CPPTypeData typeData) {
            typeData = new CPPTypeData();

            switch (parsedType.Type) {
                case VariableDataType.Single: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "float");
                    }
                case VariableDataType.Double: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "double");
                    }
                case VariableDataType.UInt32: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "uint32_t");
                    }
                case VariableDataType.Int32: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "int32_t");
                    }
                case VariableDataType.UInt64: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "uint64_t");
                    }
                case VariableDataType.Int64: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "int64_t");
                    }

                case VariableDataType.String:

                    if (codeConverter.CPPRules.UseStdString) {
                        throw new NotImplementedException();
                    } else {
                        typeData.IsArray = true;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "char");
                    }
                default:
                    typeData.IsArray = false;
                    typeData.IsNativeType = false;
                    typeData.IsPointer = true;
                    return parsedType;
            }
        }

        protected override void ProcessDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            List<string> lines
            ) {
            VariableType varType = VariableUtil.GetVarType(declaration.Type, semantic);

            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(varType, out typeData);

            FunctionStack fnStack = context.GetCurrentFunction();

            string pointer = typeData.IsPointer ? " *" : " ";
            List<string> newLines = [$"{cppType.TypeName}{pointer}"];

            FunctionStack? fn = context.GetCurrentFunction();
            bool isConstant = true;

            int start = context.DepthClass;

            for (int i = 0; i < declaration.Variables.Count; i++) {
                var variable = declaration.Variables[i];
                string name = variable.Identifier.ToString();
                newLines.Add(name);

                ConversionFunctionVariableUsage usage = fnStack.Function.BodyVariables.FirstOrDefault(c => c.Name == name);
                if (usage != null && usage.Reassignment) {
                    isConstant = false;
                }

                if (typeData.IsArray) {
                    newLines.Add("[]");
                }

                if (i < declaration.Variables.Count - 1) {
                    newLines.Add(",");
                }

                ConversionVariable var = null;
                if (fn != null) {
                    var = new ConversionVariable();
                    var.Name = variable.Identifier.ToString();
                    var.VarType = varType;
                    fn.Stack.Add(var);
                }

                if (variable.Initializer != null) {
                    newLines.Add($" = ");
                    ExpressionResult result = ProcessExpression(semantic, context, variable.Initializer.Value, newLines);
                    int xxx = -1;

                    if (var != null) {

                    }
                }
            }

            context.PopClass(start);

            if (isConstant && typeData.IsNativeType) {
                lines.Add("const ");
            }
            lines.AddRange(newLines);
        }

        protected override ExpressionResult ProcessLiteralExpression(LayerContext context, LiteralExpressionSyntax literalExpression, List<string> lines) {
            string literalValue;
            string type;

            switch (literalExpression.Kind()) {
                case SyntaxKind.TrueLiteralExpression:
                    literalValue = "true";
                    type = "bool";
                    break;
                case SyntaxKind.FalseLiteralExpression:
                    literalValue = "false";
                    type = "bool";
                    break;
                case SyntaxKind.NumericLiteralExpression:
                    literalValue = literalExpression.Token.ValueText;
                    type = "int";
                    break;
                case SyntaxKind.CharacterLiteralExpression: {
                        type = "char";
                        string value = literalExpression.Token.ValueText;
                        literalValue = Regex.Replace(value, @"(?<!\\)\\(?!\\)", @"\\");
                        literalValue = Regex.Replace(literalValue, @"\r?\n", match => {
                            return match.Value == "\r\n" ? "\\r\\n" : "\\n";
                        });
                        literalValue = $"\"{literalValue}\"";
                        break;
                    }
                case SyntaxKind.StringLiteralExpression: {
                        type = "string";
                        string value = literalExpression.Token.ValueText;
                        literalValue = Regex.Replace(value, @"(?<!\\)\\(?!\\)", @"\\");
                        literalValue = Regex.Replace(literalValue, @"\r?\n", match => {
                            return match.Value == "\r\n" ? "\\r\\n" : "\\n";
                        });
                        literalValue = $"\"{literalValue}\"";
                        break;
                    }
                case SyntaxKind.NullLiteralExpression:
                    type = "null";
                    literalValue = "null";
                    break;
                case SyntaxKind.DefaultLiteralExpression:
                    type = "null";
                    literalValue = "null";
                    break;
                default:
                    throw new Exception("Unsupported literal type");
            }

            lines.Add(literalValue);

            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(type));
        }

        public override void ProcessArrowExpressionClause(SemanticModel semantic, LayerContext context, ArrowExpressionClauseSyntax arrowExpression, List<string> lines) {
            lines.Add(" = ");
            ProcessExpression(semantic, context, arrowExpression.Expression, lines);
        }
    }
}
