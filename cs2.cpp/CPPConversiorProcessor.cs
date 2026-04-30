using cs2.core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace cs2.cpp {
    public class CPPConversiorProcessor : ConversionProcessor {
        private CPPCodeConverter codeConverter;

        public CPPConversiorProcessor(CPPCodeConverter converter) {
            codeConverter = converter;
        }

        /// <summary>
        /// Gets the runtime requirement registrar for the active conversion run.
        /// </summary>
        public CPPRuntimeRequirementRegistrar RuntimeRequirementRegistrar => codeConverter.RuntimeRequirementRegistrar;

        /// <summary>
        /// Registers a named runtime requirement for the active conversion run.
        /// </summary>
        /// <param name="name">Stable runtime requirement name to record.</param>
        public void RegisterRuntimeRequirement(string name) {
            codeConverter?.RegisterRuntimeRequirement(name);
        }

        /// <summary>
        /// Processes an expression and records a structured diagnostic when no C++ lowering path exists.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the expression.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="expression">Expression to lower.</param>
        /// <param name="lines">Output line buffer that receives lowered tokens.</param>
        /// <param name="refTypes">Known argument or receiver types for overload resolution.</param>
        /// <returns>The result of expression lowering.</returns>
        public override ExpressionResult ProcessExpression(SemanticModel semantic, LayerContext context, ExpressionSyntax expression, List<string> lines, List<ExpressionResult> refTypes = null) {
            if (expression is SizeOfExpressionSyntax sizeOfExpression) {
                return ProcessSizeOfExpression(semantic, context, sizeOfExpression, lines);
            }

            if (expression is CollectionExpressionSyntax collectionExpression) {
                return ProcessCollectionExpression(semantic, context, collectionExpression, lines);
            }

            if (expression is ElementAccessExpressionSyntax elementAccessExpression) {
                return ProcessElementAccessExpressionResult(semantic, context, elementAccessExpression, lines);
            }

            int diagnosticCount = GetDiagnosticCount();
            ExpressionResult result = base.ProcessExpression(semantic, context, expression, lines, refTypes);

            if (!result.Processed && diagnosticCount == GetDiagnosticCount()) {
                ReportUnsupportedNode(context, expression, $"The C++ backend does not yet support expression syntax '{expression.Kind()}'.");
            }

            return result;
        }

        /// <summary>
        /// Processes an element-access expression and preserves the resolved result type for downstream member-access lowering.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the expression.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="elementAccess">Element-access expression to lower.</param>
        /// <param name="lines">Output line buffer that receives lowered tokens.</param>
        /// <returns>The lowered expression result including the resolved element type when available.</returns>
        ExpressionResult ProcessElementAccessExpressionResult(SemanticModel semantic, LayerContext context, ElementAccessExpressionSyntax elementAccess, List<string> lines) {
            int start = context.DepthClass;
            ProcessElementAccessExpression(semantic, context, elementAccess, lines);
            context.PopClass(start);

            if (!TryGetExpressionTypeSymbol(semantic, elementAccess, out ITypeSymbol typeSymbol)) {
                return new ExpressionResult(true, VariablePath.Unknown, null);
            }

            VariableType cppType = ConvertToCPPType(VariableUtil.GetVarType(typeSymbol.ToDisplayString()), out _);
            return new ExpressionResult(true, VariablePath.Unknown, cppType);
        }

        /// <summary>
        /// Processes a block without re-appending expression prelude and epilogue lines that are already handled at the statement level.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the block.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="block">Block to lower.</param>
        /// <param name="lines">Output line buffer that receives lowered tokens.</param>
        /// <param name="depth">Current indentation depth used by the lowerer.</param>
        /// <returns>The result produced by the last lowered statement.</returns>
        public override ExpressionResult ProcessBlock(SemanticModel semantic, LayerContext context, BlockSyntax block, List<string> lines, int depth = 1) {
            int diagnosticCount = GetDiagnosticCount();
            ProcessStatementsInScope(semantic, context, block.Statements, 0, lines, depth);

            return new ExpressionResult(diagnosticCount == GetDiagnosticCount());
        }

        /// <summary>
        /// Processes a contiguous statement range and rewrites C# using declarations into nested C++ scopes that preserve end-of-scope destruction.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the statements.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="statements">Statements in the active lexical scope.</param>
        /// <param name="startIndex">Index of the next statement to lower.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <param name="depth">Current indentation depth used by the lowerer.</param>
        void ProcessStatementsInScope(
            SemanticModel semantic,
            LayerContext context,
            SyntaxList<StatementSyntax> statements,
            int startIndex,
            List<string> lines,
            int depth) {
            for (int index = startIndex; index < statements.Count; index++) {
                if (TryProcessUsingDeclarationScope(semantic, context, statements, index, lines, depth)) {
                    return;
                }

                StatementSyntax statement = statements[index];
                int start = context.DepthClass;
                ProcessStatement(semantic, context, statement, lines, depth);
                context.PopClass(start);
            }
        }

        /// <summary>
        /// Rewrites a C# using declaration into a nested lexical block so direct-value runtime types can rely on deterministic destruction on scope exit.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the declaration.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="statements">Statements in the active lexical scope.</param>
        /// <param name="statementIndex">Index of the candidate using declaration.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <param name="depth">Current indentation depth used by the lowerer.</param>
        /// <returns><c>true</c> when the statement was rewritten as a scoped using declaration; otherwise, <c>false</c>.</returns>
        bool TryProcessUsingDeclarationScope(
            SemanticModel semantic,
            LayerContext context,
            SyntaxList<StatementSyntax> statements,
            int statementIndex,
            List<string> lines,
            int depth) {
            if (statements[statementIndex] is not LocalDeclarationStatementSyntax localDeclaration ||
                !localDeclaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) {
                return false;
            }

            lines.Add("{\n");

            int start = context.DepthClass;
            ProcessDeclaration(semantic, context, localDeclaration.Declaration, lines);
            context.PopClass(start);
            lines.Add(";\n");

            ProcessStatementsInScope(semantic, context, statements, statementIndex + 1, lines, depth);

            lines.Add("}\n");
            return true;
        }

        /// <summary>
        /// Processes a statement and records a structured diagnostic when no C++ lowering path exists.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the statement.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="statement">Statement to lower.</param>
        /// <param name="lines">Output line buffer that receives lowered tokens.</param>
        /// <param name="depth">Current indentation depth used by the lowerer.</param>
        /// <returns>The result of statement lowering.</returns>
        protected override ExpressionResult ProcessStatement(SemanticModel semantic, LayerContext context, StatementSyntax statement, List<string> lines, int depth = 1) {
            if (statement is ExpressionStatementSyntax expressionStatement &&
                TryProcessConditionalDelegateInvocationStatement(semantic, context, expressionStatement.Expression, lines)) {
                return new ExpressionResult(true);
            }

            if (statement is LocalFunctionStatementSyntax localFunctionStatement) {
                return ProcessLocalFunctionStatement(semantic, context, localFunctionStatement, lines, depth);
            }

        if (statement is CheckedStatementSyntax uncheckedStatement &&
            uncheckedStatement.Kind() == SyntaxKind.UncheckedStatement) {
            return ProcessUncheckedStatement(semantic, context, uncheckedStatement, lines, depth);
        }

            int diagnosticCount = GetDiagnosticCount();
            ExpressionResult result = base.ProcessStatement(semantic, context, statement, lines, depth);

            if (!result.Processed && diagnosticCount == GetDiagnosticCount()) {
                if (IsHandledStatement(statement)) {
                    return new ExpressionResult(true);
                }

                ReportUnsupportedNode(context, statement, $"The C++ backend does not yet support statement syntax '{statement.Kind()}'.");
            }

            return result;
        }

        bool TryProcessConditionalDelegateInvocationStatement(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax expression,
            List<string> lines) {
            if (expression is not ConditionalAccessExpressionSyntax conditionalAccess ||
                conditionalAccess.WhenNotNull is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberBindingExpressionSyntax memberBinding ||
                !string.Equals(memberBinding.Name.Identifier.Text, "Invoke", StringComparison.Ordinal)) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, conditionalAccess.Expression, out ITypeSymbol receiverTypeSymbol) ||
                !IsActionTypeSymbol(receiverTypeSymbol)) {
                return false;
            }

            List<string> receiverLines = new List<string>();
            int startReceiver = context.DepthClass;
            ProcessExpression(semantic, context, conditionalAccess.Expression, receiverLines);
            context.PopClass(startReceiver);

            lines.Add("if (");
            lines.AddRange(receiverLines);
            lines.Add(" != nullptr)\n{\n");
            lines.Add("(*");
            lines.AddRange(receiverLines);
            lines.Add(")(");

            if (invocation.ArgumentList != null) {
                for (int index = 0; index < invocation.ArgumentList.Arguments.Count; index++) {
                    ArgumentSyntax argument = invocation.ArgumentList.Arguments[index];
                    int startArgument = context.DepthClass;
                    ProcessExpression(semantic, context, argument.Expression, lines);
                    context.PopClass(startArgument);

                    if (index < invocation.ArgumentList.Arguments.Count - 1) {
                        lines.Add(", ");
                    }
                }
            }

            lines.Add(");\n}\n");
            return true;
        }

        /// <summary>
        /// Lowers a C# unchecked statement by preserving its lexical scope and emitting only the enclosed block statements.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the unchecked statement.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="uncheckedStatement">Unchecked statement to lower.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <param name="depth">Current indentation depth used by the lowerer.</param>
        /// <returns>The result of lowering the enclosed block.</returns>
    ExpressionResult ProcessUncheckedStatement(
        SemanticModel semantic,
        LayerContext context,
        CheckedStatementSyntax uncheckedStatement,
        List<string> lines,
        int depth) {
            lines.Add("{\n");
            ExpressionResult result = ProcessBlock(semantic, context, uncheckedStatement.Block, lines, depth);
            lines.Add("}\n");
            return result;
        }

        /// <summary>
        /// Lowers a C# local function into a capture-by-reference C++ lambda declared inside the active function body.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the local function.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="localFunctionStatement">Local function statement to lower.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <param name="depth">Current indentation depth used by the lowerer.</param>
        /// <returns>The lowering result for the emitted local lambda declaration.</returns>
        ExpressionResult ProcessLocalFunctionStatement(
            SemanticModel semantic,
            LayerContext context,
            LocalFunctionStatementSyntax localFunctionStatement,
            List<string> lines,
            int depth) {
            FunctionStack outerFunction = context.GetCurrentFunction();
            if (outerFunction != null) {
                ConversionVariable localFunctionVariable = new ConversionVariable();
                localFunctionVariable.Name = localFunctionStatement.Identifier.Text;
                localFunctionVariable.VarType = new VariableType(VariableDataType.Callback, "callback");
                outerFunction.Stack.Add(localFunctionVariable);
            }

            VariableType returnType = localFunctionStatement.ReturnType == null
                ? new VariableType(VariableDataType.Void, "void")
                : VariableUtil.GetVarType(localFunctionStatement.ReturnType, semantic);

            ConversionFunction localFunction = new ConversionFunction();
            localFunction.Name = localFunctionStatement.Identifier.Text;
            localFunction.ReturnType = returnType;
            localFunction.InParameters = CreateLocalFunctionParameters(semantic, localFunctionStatement);

            lines.Add("auto ");
            lines.Add(localFunctionStatement.Identifier.Text);
            lines.Add(" = [&](");
            WriteLocalFunctionParameters(localFunction.InParameters, context, lines);
            lines.Add(") -> ");
            lines.Add(GetCppTypeSignature(returnType, context));

            int functionDepth = context.DepthFunction;
            context.AddFunction(new FunctionStack(localFunction));

            if (localFunctionStatement.Body != null) {
                lines.Add(" {\n");
                ProcessBlock(semantic, context, localFunctionStatement.Body, lines, depth);
                lines.Add("};\n");
                context.PopFunction(functionDepth);
                return new ExpressionResult(true, VariablePath.FunctionStack, returnType);
            }

            if (localFunctionStatement.ExpressionBody != null) {
                lines.Add(" { ");
                if (returnType.Type != VariableDataType.Void) {
                    lines.Add("return ");
                }

                int start = context.DepthClass;
                ExpressionResult expressionResult = ProcessExpression(semantic, context, localFunctionStatement.ExpressionBody.Expression, lines);
                context.PopClass(start);

                lines.Add("; };\n");
                context.PopFunction(functionDepth);
                return new ExpressionResult(expressionResult.Processed, VariablePath.FunctionStack, returnType);
            }

            lines.Add(" {};\n");
            context.PopFunction(functionDepth);
            return new ExpressionResult(true, VariablePath.FunctionStack, returnType);
        }

        /// <summary>
        /// Creates the conversion parameter model used while lowering the body of a local function lambda.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the local function.</param>
        /// <param name="localFunctionStatement">Local function statement whose parameters will be converted.</param>
        /// <returns>The converted parameter list for the synthesized function scope.</returns>
        List<ConversionVariable> CreateLocalFunctionParameters(
            SemanticModel semantic,
            LocalFunctionStatementSyntax localFunctionStatement) {
            List<ConversionVariable> parameters = new List<ConversionVariable>();

            foreach (ParameterSyntax parameter in localFunctionStatement.ParameterList.Parameters) {
                ConversionVariable conversionVariable = new ConversionVariable();
                conversionVariable.Name = parameter.Identifier.Text;
                conversionVariable.VarType = VariableUtil.GetVarType(parameter.Type, semantic);
                parameters.Add(conversionVariable);
            }

            return parameters;
        }

        /// <summary>
        /// Emits the C++ parameter list for a lowered local lambda declaration.
        /// </summary>
        /// <param name="parameters">Converted local-function parameters to emit.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        void WriteLocalFunctionParameters(
            List<ConversionVariable> parameters,
            LayerContext context,
            List<string> lines) {
            for (int i = 0; i < parameters.Count; i++) {
                ConversionVariable parameter = parameters[i];
                lines.Add(GetCppTypeSignature(parameter.VarType, context));
                lines.Add(" ");
                lines.Add(parameter.Name);

                if (i < parameters.Count - 1) {
                    lines.Add(", ");
                }
            }
        }

        /// <summary>
        /// Resolves the emitted C++ type token for a source variable type, including pointer suffixes for reference types.
        /// </summary>
        /// <param name="sourceType">Source variable type to convert.</param>
        /// <param name="context">Current lowering context.</param>
        /// <returns>The emitted C++ type token.</returns>
        string GetCppTypeSignature(VariableType sourceType, LayerContext context) {
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);
            string cppTypeName = cppType.ToCPPString(context.Program);

            string sourceTypeName = sourceType?.TypeName ?? string.Empty;
            string normalizedSourceTypeName = sourceTypeName;
            int separatorIndex = normalizedSourceTypeName.LastIndexOf('.');
            if (separatorIndex >= 0 && separatorIndex < normalizedSourceTypeName.Length - 1) {
                normalizedSourceTypeName = normalizedSourceTypeName[(separatorIndex + 1)..];
            }

            ConversionClass generatedClass = context.Program.FindGeneratedClass(sourceTypeName, sourceType?.GenericArgs?.Count ?? 0) ??
                context.Program.FindGeneratedClass(normalizedSourceTypeName, sourceType?.GenericArgs?.Count ?? 0);
            if (generatedClass != null && generatedClass.DeclarationType == MemberDeclarationType.Enum) {
                return cppTypeName;
            }

            if (typeData.IsPointer) {
                return cppTypeName + "*";
            }

            return cppTypeName;
        }

        /// <summary>
        /// Determines whether the current backend has a lowering path for the supplied statement syntax.
        /// </summary>
        /// <param name="statement">Statement syntax being evaluated after the shared processor dispatch runs.</param>
        /// <returns><c>true</c> when the statement kind is intentionally lowered by this backend; otherwise, <c>false</c>.</returns>
        static bool IsHandledStatement(StatementSyntax statement) {
            return statement is ReturnStatementSyntax
                || statement is LocalDeclarationStatementSyntax
                || statement is SwitchStatementSyntax
                || statement is BlockSyntax
                || statement is BreakStatementSyntax
                || statement is ThrowStatementSyntax
                || statement is ForStatementSyntax
                || statement is WhileStatementSyntax
                || statement is ContinueStatementSyntax
                || statement is ForEachStatementSyntax
                || statement is TryStatementSyntax
                || statement is LockStatementSyntax
                || statement is UsingStatementSyntax
                || statement is DoStatementSyntax
                || statement is EmptyStatementSyntax;
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
            bool emitMethodGroupPointer = false;

            ISymbol? nsSymbol = semantic.GetSymbolInfo(identifier).Symbol;
            if (nsSymbol is IAliasSymbol aliasSymbol) {
                nsSymbol = aliasSymbol.Target;
            }

            if (nsSymbol is IMethodSymbol methodSymbol) {
                isMethod = true;
            }

            VariablePath varPath = VariablePath.Unknown;
            if (nsSymbol is INamespaceSymbol) {
                varPath = VariablePath.Static;
            } else if (nsSymbol is INamespaceOrTypeSymbol nameSpaceType && nameSpaceType.IsType) {
                varPath = VariablePath.Static;
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

            if (currentClass != null &&
                classFn != null &&
                refTypes == null &&
                identifier.Parent is not InvocationExpressionSyntax &&
                identifier.Parent is not MemberAccessExpressionSyntax) {
                emitMethodGroupPointer = true;
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
                        bool isStaticClassMember = false;
                        ISymbol identifierSymbol = semantic.GetSymbolInfo(identifier).Symbol;
                        if (identifierSymbol is IAliasSymbol identifierAliasSymbol) {
                            identifierSymbol = identifierAliasSymbol.Target;
                        }

                        if (identifierSymbol is IFieldSymbol fieldSymbol) {
                            isStaticClassMember = fieldSymbol.IsStatic;
                        } else if (identifierSymbol is IPropertySymbol propertySymbol) {
                            isStaticClassMember = propertySymbol.IsStatic;
                        }

                        if (!isStaticClassMember &&
                            !emitMethodGroupPointer &&
                            currentFn?.Function?.IsStatic != true) {
                            if (lines.Count > 1) {
                                string b2 = lines[lines.Count - 2];
                                string b1 = lines[lines.Count - 1];

                                if (b2 == "this" && b1.IndexOf(";") == -1) {
                                } else {
                                    lines.Add("this->");
                                }
                            } else {
                                // semantic
                                ISymbol? symbol = semantic.GetSymbolInfo(identifier).Symbol;
                                if (symbol is INamedTypeSymbol namedTypeSymbol) {
                                    if (!namedTypeSymbol.IsStatic &&
                                        !namedTypeSymbol.IsType) {
                                        lines.Add("this->");
                                    }
                                } else {
                                    lines.Add("this->");
                                }
                            }
                        }
                    }
                }

                if (classFn == null || string.IsNullOrEmpty(classFn.Remap)) {
                    ConversionVariable? varOnClass = currentClass.Variables.FirstOrDefault(c => c.Name == name);
                    if (emitMethodGroupPointer) {
                        lines.Add($"&{currentClass.GetEmittedTypeName()}::");
                    }

                    if (varOnClass == null || string.IsNullOrEmpty(varOnClass.Remap)) {
                        lines.Add(name);
                    } else {
                        lines.Add(varOnClass.Remap);
                    }
                } else {
                    if (emitMethodGroupPointer) {
                        lines.Add($"&{currentClass.GetEmittedTypeName()}::");
                    }

                    lines.Add(GetEmittedFunctionName(classFn));
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

            return new ExpressionResult(true, varPath, null);
        }

        static string GetEmittedFunctionName(ConversionFunction function) {
            if (function == null) {
                return string.Empty;
            }

            return function.Name;
        }

        protected override ExpressionResult ProcessObjectCreationExpressionSyntax(SemanticModel semantic, LayerContext context, ObjectCreationExpressionSyntax objectCreation, List<string> lines) {
            if (objectCreation.Initializer is InitializerExpressionSyntax initializer && IsObjectInitializer(initializer)) {
                return ProcessObjectInitializerCreation(semantic, context, objectCreation, initializer, lines);
            }

            if (IsSystemObjectType(semantic, objectCreation.Type)) {
                lines.Add("new char[1]");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("object"));
            }

            int diagnosticCount = GetDiagnosticCount();
            BuildObjectCreationExpression(semantic, context, objectCreation, lines);

            return new ExpressionResult(diagnosticCount == GetDiagnosticCount());
        }

        /// <summary>
        /// Determines whether an initializer expression represents a C# object initializer with member assignments.
        /// </summary>
        /// <param name="initializer">Initializer syntax attached to the object creation expression.</param>
        /// <returns><c>true</c> when the initializer contains assignment entries; otherwise <c>false</c>.</returns>
        static bool IsObjectInitializer(InitializerExpressionSyntax initializer) {
            if (initializer == null) {
                return false;
            }

            return initializer.Expressions.Any(expression => expression is AssignmentExpressionSyntax);
        }

        /// <summary>
        /// Lowers a C# object initializer into a temporary object construction expression followed by member assignments.
        /// </summary>
        /// <param name="semantic">Semantic model for the current document.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="objectCreation">Object creation expression being lowered.</param>
        /// <param name="initializer">Initializer that contains member assignments.</param>
        /// <param name="lines">Output token buffer that receives the lowered expression.</param>
        /// <returns>The result produced by the underlying object construction expression.</returns>
        ExpressionResult ProcessObjectInitializerCreation(
            SemanticModel semantic,
            LayerContext context,
            ObjectCreationExpressionSyntax objectCreation,
            InitializerExpressionSyntax initializer,
            List<string> lines) {
            string objectName = "__object_" + Guid.NewGuid().ToString("N")[..8];

            lines.Add("([&]() {\n");
            lines.Add("auto ");
            lines.Add(objectName);
            lines.Add(" = ");

            ExpressionResult result = BuildObjectCreationExpression(semantic, context, objectCreation, lines);
            lines.Add(";\n");

            foreach (ExpressionSyntax expression in initializer.Expressions) {
                if (expression is not AssignmentExpressionSyntax assignment) {
                    continue;
                }

                lines.Add(objectName);
                lines.Add("->");

                int startLeft = context.DepthClass;
                ProcessExpression(semantic, context, assignment.Left, lines);
                context.PopClass(startLeft);

                lines.Add(" = ");

                int startRight = context.DepthClass;
                ProcessExpression(semantic, context, assignment.Right, lines);
                context.PopClass(startRight);

                lines.Add(";\n");
            }

            lines.Add("return ");
            lines.Add(objectName);
            lines.Add(";\n");
            lines.Add("})()");
            return result;
        }

        /// <summary>
        /// Emits the core C++ object construction expression without applying C# object-initializer assignments.
        /// </summary>
        /// <param name="semantic">Semantic model for the current document.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="objectCreation">Object creation expression being lowered.</param>
        /// <param name="lines">Output token buffer that receives the construction expression.</param>
        /// <returns>The expression result describing the constructed object.</returns>
        ExpressionResult BuildObjectCreationExpression(
            SemanticModel semantic,
            LayerContext context,
            ObjectCreationExpressionSyntax objectCreation,
            List<string> lines) {
            if (IsSystemObjectType(semantic, objectCreation.Type)) {
                lines.Add("new char[1]");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("object"));
            }

            int diagnosticCount = GetDiagnosticCount();
            if (!IsValueRuntimeTypeName(objectCreation.Type.ToString())) {
                lines.Add("new ");
            }

            int startDepth = context.DepthClass;
            ExpressionResult typeResult = ProcessExpression(semantic, context, objectCreation.Type, lines);
            context.PopClass(startDepth);

            lines.Add("(");
            if (objectCreation.ArgumentList != null) {
                for (int i = 0; i < objectCreation.ArgumentList.Arguments.Count; i++) {
                    ArgumentSyntax arg = objectCreation.ArgumentList.Arguments[i];

                    int startArg = context.DepthClass;
                    ProcessExpression(semantic, context, arg.Expression, lines);
                    context.PopClass(startArg);

                    if (i != objectCreation.ArgumentList.Arguments.Count - 1) {
                        lines.Add(", ");
                    }
                }
            }

            lines.Add(")");
            return new ExpressionResult(diagnosticCount == GetDiagnosticCount(), VariablePath.Unknown, typeResult.Type);
        }

        protected override ExpressionResult ProcessMemberAccessExpressionSyntax(SemanticModel semantic, LayerContext context, MemberAccessExpressionSyntax memberAccess, List<string> lines, List<ExpressionResult> refTypes) {
            if (TryProcessNativeStringLengthMemberAccess(semantic, context, memberAccess, lines, out VariableType stringLengthType)) {
                return new ExpressionResult(true, VariablePath.Unknown, stringLengthType);
            }

            if (TryProcessNativeDictionaryKeysMemberAccess(semantic, context, memberAccess, lines, out VariableType dictionaryKeysType)) {
                return new ExpressionResult(true, VariablePath.Unknown, dictionaryKeysType);
            }

            if (memberAccess.Expression is IdentifierNameSyntax encodingIdentifier &&
                string.Equals(encodingIdentifier.Identifier.Text, "Encoding", StringComparison.Ordinal) &&
                memberAccess.Name is IdentifierNameSyntax encodingMemberIdentifier) {
                lines.Add("Encoding");
                lines.Add("::");
                lines.Add(encodingMemberIdentifier.Identifier.Text);
                return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType("object"));
            }

            ISymbol staticMemberSymbol = semantic.GetSymbolInfo(memberAccess).Symbol ?? semantic.GetSymbolInfo(memberAccess.Name).Symbol;
            if (staticMemberSymbol is IAliasSymbol staticMemberAliasSymbol) {
                staticMemberSymbol = staticMemberAliasSymbol.Target;
            }

            if (staticMemberSymbol is IFieldSymbol staticFieldSymbol &&
                staticFieldSymbol.IsStatic &&
                staticFieldSymbol.ContainingType != null) {
                if (string.Equals(staticFieldSymbol.ContainingType.Name, "Encoding", StringComparison.Ordinal)) {
                    lines.Add("Encoding");
                    lines.Add("::");
                    lines.Add(staticFieldSymbol.Name);
                    return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType(staticFieldSymbol.Type));
                }
            }

            if (staticMemberSymbol is IPropertySymbol staticPropertySymbol &&
                staticPropertySymbol.IsStatic &&
                staticPropertySymbol.ContainingType != null) {
                if (string.Equals(staticPropertySymbol.ContainingType.Name, "Encoding", StringComparison.Ordinal)) {
                    lines.Add("Encoding");
                    lines.Add("::");
                    lines.Add(staticPropertySymbol.Name);
                    return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType(staticPropertySymbol.Type));
                }
            }

            if (TryResolveStaticRuntimeType(semantic, memberAccess.Expression, out string runtimeTypeName, out string runtimeRequirementName)) {
                RegisterRuntimeRequirement(runtimeRequirementName);
                lines.Add(runtimeTypeName);
                lines.Add("::");
                return ProcessExpression(semantic, context, memberAccess.Name, lines, refTypes);
            }

            ExpressionResult result = ProcessExpression(semantic, context, memberAccess.Expression, lines);
            if (result.Processed) {
                bool useStaticAccess = result.VarPath == VariablePath.Static || memberAccess.Expression is BaseExpressionSyntax;
                ISymbol memberSymbol = null;
                if (!useStaticAccess) {
                    ISymbol? receiverSymbol = semantic.GetSymbolInfo(memberAccess.Expression).Symbol;
                    if (receiverSymbol is IAliasSymbol receiverAliasSymbol) {
                        receiverSymbol = receiverAliasSymbol.Target;
                    }

                    if (receiverSymbol is INamespaceSymbol || receiverSymbol is INamedTypeSymbol) {
                        useStaticAccess = true;
                    }
                }

                if (useStaticAccess && context.GetCurrentClass() != null) {
                    RegisterStaticAccessDependency(semantic, context, memberAccess.Expression, result);
                }

                if (!useStaticAccess) {
                    memberSymbol = semantic.GetSymbolInfo(memberAccess).Symbol;
                    if (memberSymbol == null) {
                        memberSymbol = semantic.GetSymbolInfo(memberAccess.Name).Symbol;
                    }

                    if (memberSymbol is IAliasSymbol aliasSymbol) {
                        memberSymbol = aliasSymbol.Target;
                    }

                    if (memberSymbol is INamespaceSymbol || memberSymbol is INamedTypeSymbol) {
                        useStaticAccess = true;
                    } else if (memberSymbol is IFieldSymbol fieldSymbol && fieldSymbol.IsStatic) {
                        useStaticAccess = true;
                    } else if (memberSymbol is IPropertySymbol propertySymbol && propertySymbol.IsStatic) {
                        useStaticAccess = true;
                    } else if (memberSymbol is IMethodSymbol methodSymbol && methodSymbol.IsStatic) {
                        useStaticAccess = true;
                    }
                }

                if (memberAccess.Name is IdentifierNameSyntax staticIdentifier &&
                    IsDateTimeTypeReference(semantic, memberAccess.Expression) &&
                    (string.Equals(staticIdentifier.Identifier.Text, "Now", StringComparison.Ordinal) ||
                     string.Equals(staticIdentifier.Identifier.Text, "UtcNow", StringComparison.Ordinal))) {
                    RegisterRuntimeRequirement("NativeDateTime");
                    lines.Add("::");
                    lines.Add(staticIdentifier.Identifier.Text);
                    lines.Add("()");
                    return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType("DateTime"));
                }

                bool useDirectMemberAccess = UsesDirectMemberAccess(result) ||
                    UsesDirectMemberAccess(semantic, memberAccess.Expression) ||
                    (memberAccess.Expression is ElementAccessExpressionSyntax elementAccessExpression &&
                     UsesDirectMemberAccess(semantic, elementAccessExpression.Expression)) ||
                    UsesDirectMemberAccess(memberSymbol);

                // shit is this static access, pointer access or direct access
                switch (useStaticAccess ? VariablePath.Static : result.VarPath) {
                    case VariablePath.Static:
                        lines.Add("::");
                        break;
                    default: {
                            lines.Add(useDirectMemberAccess ? "." : "->");

                            if (result.Variable != null) {
                                int xx = -1;
                        }
                    }
                        break;
                }

                if (!useStaticAccess &&
                    ShouldEmitNativeCountCall(semantic, memberAccess, memberSymbol)) {
                    lines.Add("Count()");
                    return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("int"));
                }
            }
            return ProcessExpression(semantic, context, memberAccess.Name, lines, refTypes);
        }

        bool TryProcessNativeStringLengthMemberAccess(
            SemanticModel semantic,
            LayerContext context,
            MemberAccessExpressionSyntax memberAccess,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("int");
            if (memberAccess.Name is not IdentifierNameSyntax identifierName ||
                !string.Equals(identifierName.Identifier.Text, "Length", StringComparison.Ordinal) ||
                !IsStringExpression(semantic, memberAccess.Expression)) {
                return false;
            }

            string receiverText = RenderExpressionText(semantic, context, memberAccess.Expression);
            lines.Add($"static_cast<int32_t>({receiverText}.size())");
            return true;
        }

        bool TryProcessNativeDictionaryKeysMemberAccess(
            SemanticModel semantic,
            LayerContext context,
            MemberAccessExpressionSyntax memberAccess,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("object");
            if (memberAccess.Name is not IdentifierNameSyntax identifierName ||
                !string.Equals(identifierName.Identifier.Text, "Keys", StringComparison.Ordinal) ||
                !TryGetExpressionTypeSymbol(semantic, memberAccess.Expression, out ITypeSymbol receiverTypeSymbol) ||
                !IsDictionaryTypeSymbol(receiverTypeSymbol)) {
                return false;
            }

            string receiverText = RenderExpressionText(semantic, context, memberAccess.Expression);
            lines.Add($"{receiverText}->Keys()");
            return true;
        }

        /// <summary>
        /// Registers the owning generated type for a static access chain so body-only references pull the correct source include.
        /// </summary>
        /// <param name="semantic">Semantic model for the current syntax tree.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="receiverExpression">Expression that owns the emitted static member access.</param>
        /// <param name="receiverResult">Lowered receiver expression result.</param>
        void RegisterStaticAccessDependency(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax receiverExpression,
            ExpressionResult receiverResult) {
            if (context.Class.Count == 0 || context.Class[0] == null) {
                return;
            }

            ConversionClass currentClass = context.Class[0];

            string referencedTypeName = string.Empty;
            ISymbol receiverSymbol = semantic.GetSymbolInfo(receiverExpression).Symbol;
            if (receiverSymbol is IAliasSymbol receiverAliasSymbol) {
                receiverSymbol = receiverAliasSymbol.Target;
            }

            if (receiverSymbol is IFieldSymbol receiverFieldSymbol &&
                receiverFieldSymbol.IsStatic &&
                receiverFieldSymbol.ContainingType != null) {
                referencedTypeName = receiverFieldSymbol.ContainingType.Name;
            } else if (receiverSymbol is IPropertySymbol receiverPropertySymbol &&
                receiverPropertySymbol.IsStatic &&
                receiverPropertySymbol.ContainingType != null) {
                referencedTypeName = receiverPropertySymbol.ContainingType.Name;
            } else if (receiverSymbol is IMethodSymbol receiverMethodSymbol &&
                receiverMethodSymbol.IsStatic &&
                receiverMethodSymbol.ContainingType != null) {
                referencedTypeName = receiverMethodSymbol.ContainingType.Name;
            } else if (receiverResult.Class != null) {
                referencedTypeName = receiverResult.Class.Name;
            }

            if (!string.IsNullOrWhiteSpace(referencedTypeName) &&
                !currentClass.ReferencedClasses.Contains(referencedTypeName)) {
                currentClass.ReferencedClasses.Add(referencedTypeName);
            }
        }

        protected override ExpressionResult ProcessInvocationExpressionSyntax(SemanticModel semantic, LayerContext context, InvocationExpressionSyntax invocationExpression, List<string> lines) {
            if (TryProcessNameOfInvocation(invocationExpression, lines)) {
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
            }

            if (TryProcessNativeStringInvocation(semantic, context, invocationExpression, lines, out VariableType nativeStringType)) {
                return new ExpressionResult(true, VariablePath.Unknown, nativeStringType);
            }

            if (TryProcessNativeToStringInvocation(semantic, context, invocationExpression, lines, out VariableType nativeToStringType)) {
                return new ExpressionResult(true, VariablePath.Unknown, nativeToStringType);
            }

            if (TryProcessEventInvocation(semantic, context, invocationExpression, lines)) {
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("void"));
            }

            List<string> argLines = ["("];
            int count = 0;
            List<ExpressionResult> types = new List<ExpressionResult>();

            List<string> beforeLines = new List<string>();

            foreach (var arg in invocationExpression.ArgumentList.Arguments) {
                string refKeyword = arg.RefKindKeyword.ToString();
                bool isRef = false;
                if (refKeyword != "") {
                    isRef = true;
                }

                int startArg = context.DepthClass;
                types.Add(ProcessExpression(semantic, context, arg.Expression, argLines));
                context.PopClass(startArg);

                if (isRef) {
                    AddRefOrOutDeclarationBeforeLines(semantic, context, arg.Expression, beforeLines);
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

            result.BeforeLines = beforeLines;
            return result;
        }

        bool TryProcessEventInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines) {
            if (!IsEventExpression(semantic, invocationExpression.Expression)) {
                return false;
            }

            int start = context.DepthClass;
            ProcessExpression(semantic, context, invocationExpression.Expression, lines);
            context.PopClass(start);

            lines.Add(".Invoke(");

            for (int index = 0; index < invocationExpression.ArgumentList.Arguments.Count; index++) {
                ArgumentSyntax argument = invocationExpression.ArgumentList.Arguments[index];
                int startArgument = context.DepthClass;
                ProcessExpression(semantic, context, argument.Expression, lines);
                context.PopClass(startArgument);

                if (index < invocationExpression.ArgumentList.Arguments.Count - 1) {
                    lines.Add(", ");
                }
            }

            lines.Add(")");
            return true;
        }

        void AddRefOrOutDeclarationBeforeLines(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax expression,
            List<string> beforeLines) {
            if (expression is not DeclarationExpressionSyntax declarationExpression ||
                declarationExpression.Designation is not SingleVariableDesignationSyntax singleVariableDesignation) {
                return;
            }

            VariableType variableType = VariableUtil.GetVarType(declarationExpression.Type, semantic);
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(variableType, out typeData);
            string pointerSuffix = typeData.IsPointer ? "*" : string.Empty;
            beforeLines.Add($"{cppType.ToCPPString(context.Program)}{pointerSuffix} {singleVariableDesignation.Identifier.Text};\n");
        }

        bool TryProcessNativeStringInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("string");
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name is not IdentifierNameSyntax memberIdentifier) {
                return false;
            }

            string memberName = memberIdentifier.Identifier.Text;
            bool isStaticStringCall = IsStringRuntimeTypeReference(semantic, memberAccess.Expression);
            bool isInstanceStringCall = !isStaticStringCall && IsStringExpression(semantic, memberAccess.Expression);
            if (!isStaticStringCall && !isInstanceStringCall) {
                return false;
            }

            if (isStaticStringCall &&
                string.Equals(memberName, "Equals", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count >= 2) {
                RegisterRuntimeRequirement("NativeString");
                lines.Add("String::Equals(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("bool");
                return true;
            }

            if (!isInstanceStringCall) {
                return false;
            }

            RegisterRuntimeRequirement("NativeString");
            string receiverText = RenderExpressionText(semantic, context, memberAccess.Expression);

            if (string.Equals(memberName, "StartsWith", StringComparison.Ordinal) ||
                string.Equals(memberName, "EndsWith", StringComparison.Ordinal)) {
                lines.Add($"String::{memberName}(");
                lines.Add(receiverText);
                if (invocationExpression.ArgumentList.Arguments.Count > 0) {
                    lines.Add(", ");
                    AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                }

                lines.Add(")");
                resultType = VariableUtil.GetVarType("bool");
                return true;
            }

            if (string.Equals(memberName, "ToLowerInvariant", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 0) {
                lines.Add("String::ToLowerInvariant(");
                lines.Add(receiverText);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("string");
                return true;
            }

            if (string.Equals(memberName, "Equals", StringComparison.Ordinal)) {
                lines.Add("String::Equals(");
                lines.Add(receiverText);
                if (invocationExpression.ArgumentList.Arguments.Count > 0) {
                    lines.Add(", ");
                    AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                }

                lines.Add(")");
                resultType = VariableUtil.GetVarType("bool");
                return true;
            }

            return false;
        }

        bool TryProcessNativeToStringInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("string");

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                !string.Equals(memberAccess.Name.Identifier.Text, "ToString", StringComparison.Ordinal) ||
                invocationExpression.ArgumentList.Arguments.Count != 0) {
                return false;
            }

            IMethodSymbol toStringMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);

            List<string> receiverLines = new List<string>();
            int start = context.DepthClass;
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(start);

            string receiverText = string.Concat(receiverLines);
            ITypeSymbol receiverTypeSymbol = ResolveNativeToStringReceiverType(semantic, memberAccess.Expression, toStringMethodSymbol);
            if (receiverTypeSymbol?.SpecialType == SpecialType.System_Char) {
                lines.Add($"std::string(1, {receiverText})");
                return true;
            }

            bool unresolvedReceiverType = receiverTypeSymbol == null ||
                receiverTypeSymbol.TypeKind == TypeKind.Error ||
                string.Equals(receiverTypeSymbol.ToDisplayString(), "?", StringComparison.Ordinal);

            if (unresolvedReceiverType &&
                toStringMethodSymbol == null &&
                memberAccess.Expression is MemberAccessExpressionSyntax unresolvedNullableValueAccess &&
                string.Equals(unresolvedNullableValueAccess.Name.Identifier.Text, "Value", StringComparison.Ordinal)) {
                lines.Add($"std::to_string({receiverText})");
                return true;
            }

            if (!IsNativeToStringTypeSymbol(receiverTypeSymbol)) {
                return false;
            }

            lines.Add($"std::to_string({receiverText})");
            return true;
        }

        IMethodSymbol ResolveInvokedMethodSymbol(SemanticModel semantic, InvocationExpressionSyntax invocationExpression) {
            IMethodSymbol methodSymbol = ResolveMethodSymbol(semantic.GetSymbolInfo(invocationExpression.Expression));
            if (methodSymbol != null) {
                return methodSymbol;
            }

            return ResolveMethodSymbol(semantic.GetSymbolInfo(invocationExpression));
        }

        static IMethodSymbol ResolveMethodSymbol(SymbolInfo symbolInfo) {
            ISymbol symbol = symbolInfo.Symbol;

            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            if (symbol is IMethodSymbol methodSymbol) {
                return methodSymbol;
            }

            foreach (ISymbol candidateSymbol in symbolInfo.CandidateSymbols) {
                if (candidateSymbol is IMethodSymbol candidateMethodSymbol) {
                    return candidateMethodSymbol;
                }
            }

            return null;
        }

        ITypeSymbol ResolveNativeToStringReceiverType(
            SemanticModel semantic,
            ExpressionSyntax receiverExpression,
            IMethodSymbol toStringMethodSymbol) {
            if (toStringMethodSymbol?.Name == "ToString" && toStringMethodSymbol.ContainingType != null) {
                return toStringMethodSymbol.ContainingType;
            }

            if (TryGetExpressionTypeSymbol(semantic, receiverExpression, out ITypeSymbol receiverTypeSymbol)) {
                return receiverTypeSymbol;
            }

            if (receiverExpression is MemberAccessExpressionSyntax nullableValueAccess &&
                string.Equals(nullableValueAccess.Name.Identifier.Text, "Value", StringComparison.Ordinal) &&
                TryResolveNullableUnderlyingType(semantic, nullableValueAccess.Expression, out ITypeSymbol nullableUnderlyingType)) {
                return nullableUnderlyingType;
            }

            return null;
        }

        bool TryResolveNullableUnderlyingType(
            SemanticModel semantic,
            ExpressionSyntax expression,
            out ITypeSymbol underlyingTypeSymbol) {
            underlyingTypeSymbol = null;

            if (TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol expressionTypeSymbol) &&
                TryUnwrapNullableTypeSymbol(expressionTypeSymbol, out underlyingTypeSymbol)) {
                return true;
            }

            foreach (ISymbol expressionSymbol in EnumerateResolvedSymbols(semantic, expression)) {
                switch (expressionSymbol) {
                    case IFieldSymbol fieldSymbol:
                        if (TryUnwrapNullableTypeSymbol(fieldSymbol.Type, out underlyingTypeSymbol)) {
                            return true;
                        }

                        break;
                    case IPropertySymbol propertySymbol:
                        if (TryUnwrapNullableTypeSymbol(propertySymbol.Type, out underlyingTypeSymbol)) {
                            return true;
                        }

                        break;
                    case ILocalSymbol localSymbol:
                        if (TryUnwrapNullableTypeSymbol(localSymbol.Type, out underlyingTypeSymbol)) {
                            return true;
                        }

                        break;
                    case IParameterSymbol parameterSymbol:
                        if (TryUnwrapNullableTypeSymbol(parameterSymbol.Type, out underlyingTypeSymbol)) {
                            return true;
                        }

                        break;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether a Count property access should lower to the native collection Count() helper.
        /// </summary>
        /// <param name="semantic">Semantic model used to inspect the member access.</param>
        /// <param name="memberAccess">Member access currently being lowered.</param>
        /// <param name="memberSymbol">Resolved member symbol for the access.</param>
        /// <returns><c>true</c> when the access targets a managed collection Count property; otherwise <c>false</c>.</returns>
        static bool ShouldEmitNativeCountCall(
            SemanticModel semantic,
            MemberAccessExpressionSyntax memberAccess,
            ISymbol memberSymbol) {
            if (memberAccess?.Name is not IdentifierNameSyntax identifierName ||
                !string.Equals(identifierName.Identifier.Text, "Count", StringComparison.Ordinal)) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, memberAccess.Expression, out ITypeSymbol receiverTypeSymbol)) {
                return false;
            }

            return IsCountableCollectionTypeSymbol(receiverTypeSymbol);
        }

        /// <summary>
        /// Determines whether a Roslyn type symbol represents a managed collection shape that lowers through the native Count() helper.
        /// </summary>
        /// <param name="typeSymbol">Type symbol to inspect.</param>
        /// <returns><c>true</c> when the type is a list or dictionary family collection; otherwise <c>false</c>.</returns>
        static bool IsCountableCollectionTypeSymbol(ITypeSymbol typeSymbol) {
            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return false;
            }

            string name = namedTypeSymbol.Name;
            string displayText = namedTypeSymbol.ToDisplayString();
            return string.Equals(name, "List", StringComparison.Ordinal) ||
                string.Equals(name, "Dictionary", StringComparison.Ordinal) ||
                string.Equals(name, "IReadOnlyList", StringComparison.Ordinal) ||
                string.Equals(name, "ICollection", StringComparison.Ordinal) ||
                string.Equals(name, "IReadOnlyCollection", StringComparison.Ordinal) ||
                string.Equals(name, "IDictionary", StringComparison.Ordinal) ||
                string.Equals(name, "IReadOnlyDictionary", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.Dictionary<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.IReadOnlyList<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.ICollection<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.IReadOnlyCollection<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.IDictionary<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.IReadOnlyDictionary<", StringComparison.Ordinal);
        }

        static bool TryUnwrapNullableTypeSymbol(ITypeSymbol typeSymbol, out ITypeSymbol underlyingTypeSymbol) {
            underlyingTypeSymbol = null;

            if (typeSymbol == null || !IsNullableTypeSymbol(typeSymbol)) {
                return false;
            }

            if (typeSymbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.TypeArguments.Length == 1) {
                underlyingTypeSymbol = namedTypeSymbol.TypeArguments[0];
                return true;
            }

            return false;
        }

        static IEnumerable<ISymbol> EnumerateResolvedSymbols(SemanticModel semantic, ExpressionSyntax expression) {
            foreach (ISymbol symbol in EnumerateResolvedSymbols(semantic.GetSymbolInfo(expression))) {
                yield return symbol;
            }

            if (expression is MemberAccessExpressionSyntax memberAccess) {
                foreach (ISymbol symbol in EnumerateResolvedSymbols(semantic.GetSymbolInfo(memberAccess.Name))) {
                    yield return symbol;
                }
            } else if (expression is IdentifierNameSyntax identifierName) {
                foreach (ISymbol symbol in EnumerateResolvedSymbols(semantic.GetSymbolInfo(identifierName))) {
                    yield return symbol;
                }
            }
        }

        static IEnumerable<ISymbol> EnumerateResolvedSymbols(SymbolInfo symbolInfo) {
            if (symbolInfo.Symbol != null) {
                yield return UnwrapAliasSymbol(symbolInfo.Symbol);
            }

            foreach (ISymbol candidateSymbol in symbolInfo.CandidateSymbols) {
                yield return UnwrapAliasSymbol(candidateSymbol);
            }
        }

        static ISymbol UnwrapAliasSymbol(ISymbol symbol) {
            if (symbol is IAliasSymbol aliasSymbol) {
                return aliasSymbol.Target;
            }

            return symbol;
        }

        protected override void ProcessThisExpressionSyntax(SemanticModel semantic, LayerContext context, ThisExpressionSyntax thisExpression, List<string> lines) {
            lines.Add("this");
            context.AddClass(context.Class[0]);
        }

        protected override ExpressionResult ProcessBinaryExpressionSyntax(SemanticModel semantic, LayerContext context, BinaryExpressionSyntax binary, List<string> lines) {
            if (binary.IsKind(SyntaxKind.CoalesceExpression) && binary.Right is ThrowExpressionSyntax throwExpression) {
                return ProcessCoalesceThrowExpression(semantic, context, binary, throwExpression, lines);
            }

            if (TryProcessEventNullComparison(semantic, context, binary, lines)) {
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
            }

            BinaryOpTypes op = ParseBinaryExpression(semantic, context, binary, out List<string> left, out List<string> right, out ExpressionResult result);
            lines.AddRange(left);

            lines.Add($" {op.ToStringOperator()} ");

            lines.AddRange(right);

            if (binary.IsKind(SyntaxKind.SubtractExpression) && IsDateTimeVariableType(result.Type)) {
                RegisterRuntimeRequirement("NativeDateTime");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("TimeSpan"));
            }

            return result;
        }

        bool TryProcessEventNullComparison(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binary,
            List<string> lines) {
            if (!binary.IsKind(SyntaxKind.NotEqualsExpression) &&
                !binary.IsKind(SyntaxKind.EqualsExpression)) {
                return false;
            }

            if (binary.Right is not LiteralExpressionSyntax rightLiteral ||
                !rightLiteral.IsKind(SyntaxKind.NullLiteralExpression)) {
                return false;
            }

            if (!IsEventExpression(semantic, binary.Left)) {
                return false;
            }

            lines.Add(binary.IsKind(SyntaxKind.NotEqualsExpression) ? "true" : "false");
            return true;
        }

        static bool IsEventExpression(SemanticModel semantic, ExpressionSyntax expression) {
            if (TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol expressionTypeSymbol) &&
                IsEventTypeSymbol(expressionTypeSymbol)) {
                return true;
            }

            ISymbol symbol = semantic.GetSymbolInfo(expression).Symbol;
            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            return symbol is IEventSymbol;
        }

        /// <summary>
        /// Lowers a null-coalescing throw guard into a C++ conditional expression.
        /// </summary>
        /// <param name="semantic">Semantic model for the active document.</param>
        /// <param name="context">Conversion context for the current member.</param>
        /// <param name="binary">The original coalesce expression.</param>
        /// <param name="throwExpression">Throw branch used when the left operand is null.</param>
        /// <param name="lines">Output token buffer that receives the lowered expression.</param>
        /// <returns>The expression result for the left operand.</returns>
        ExpressionResult ProcessCoalesceThrowExpression(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binary,
            ThrowExpressionSyntax throwExpression,
            List<string> lines) {
            List<string> left = new List<string>();
            int startLeft = context.DepthClass;
            ExpressionResult leftResult = ProcessExpression(semantic, context, binary.Left, left);
            context.PopClass(startLeft);

            List<string> thrown = new List<string>();
            int startThrow = context.DepthClass;
            ProcessExpression(semantic, context, throwExpression.Expression, thrown);
            context.PopClass(startThrow);

            lines.Add("(");
            lines.AddRange(left);
            lines.Add(" != nullptr ? ");
            lines.AddRange(left);
            lines.Add(" : throw ");
            lines.AddRange(thrown);
            lines.Add(")");
            return leftResult;
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
                // Add the C++ scope-resolution separator.
                lines.Add("::");
            }

            // Process the right part of the qualified name (e.g., "Console" in "System.Console")
            return ProcessExpression(semantic, context, qualifiedName.Right, lines);
        }

        protected override void ProcessTypeOfExpression(SemanticModel semantic, LayerContext context, TypeOfExpressionSyntax typeOfExpression, List<string> lines) {
            VariableType sourceType = VariableUtil.GetVarType(typeOfExpression.Type, semantic);
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);
            string cppTypeName = cppType.ToCPPString(context.Program);
            string sourceTypeName = string.IsNullOrWhiteSpace(sourceType.TypeName)
                ? typeOfExpression.Type.ToString()
                : sourceType.TypeName;

            RegisterRuntimeRequirement("NativeType");
            lines.Add($"he_cpp_type_of<{cppTypeName}>(\"{sourceTypeName}\")");
        }

        bool TryProcessNameOfInvocation(InvocationExpressionSyntax invocationExpression, List<string> lines) {
            if (invocationExpression.Expression is not IdentifierNameSyntax identifierName ||
                !string.Equals(identifierName.Identifier.Text, "nameof", StringComparison.Ordinal) ||
                invocationExpression.ArgumentList.Arguments.Count != 1) {
                return false;
            }

            string memberName = ResolveNameOfExpressionText(invocationExpression.ArgumentList.Arguments[0].Expression);
            lines.Add($"\"{EscapeCppStringLiteral(memberName)}\"");
            return true;
        }

        static string ResolveNameOfExpressionText(ExpressionSyntax expression) {
            return expression switch {
                IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
                GenericNameSyntax genericName => genericName.Identifier.Text,
                QualifiedNameSyntax qualifiedName => ResolveNameOfExpressionText(qualifiedName.Right),
                MemberAccessExpressionSyntax memberAccess => ResolveNameOfExpressionText(memberAccess.Name),
                _ => expression.ToString()
            };
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

        protected override ExpressionResult ProcessArrayCreationExpression(SemanticModel semantic, LayerContext context, ArrayCreationExpressionSyntax arrayCreation, List<string> lines) {
            if (arrayCreation.Initializer != null) {
                lines.Add("{ ");
                for (int i = 0; i < arrayCreation.Initializer.Expressions.Count; i++) {
                    ProcessExpression(semantic, context, arrayCreation.Initializer.Expressions[i], lines);

                    if (i < arrayCreation.Initializer.Expressions.Count - 1) {
                        lines.Add(", ");
                    }
                }
                lines.Add(" }");
            } else if (TryProcessDynamicArrayCreation(semantic, context, arrayCreation, lines)) {
            } else if (arrayCreation.Type.RankSpecifiers.Any()) {
                lines.Add("new Array(");
                foreach (ArrayRankSpecifierSyntax rankSpecifier in arrayCreation.Type.RankSpecifiers) {
                    foreach (ExpressionSyntax size in rankSpecifier.Sizes.OfType<ExpressionSyntax>()) {
                        ProcessExpression(semantic, context, size, lines);
                    }
                }
                lines.Add(")");
            }

            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(arrayCreation.Type, semantic));
        }

        /// <summary>
        /// Emits a C++ brace-initializer for collection expressions.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the collection expression.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="collectionExpression">Collection expression to lower.</param>
        /// <param name="lines">Output line buffer that receives lowered tokens.</param>
        /// <returns>A processed result representing the lowered collection expression.</returns>
        ExpressionResult ProcessCollectionExpression(
            SemanticModel semantic,
            LayerContext context,
            CollectionExpressionSyntax collectionExpression,
            List<string> lines) {
            lines.Add("{ ");

            for (int i = 0; i < collectionExpression.Elements.Count; i++) {
                if (collectionExpression.Elements[i] is not ExpressionElementSyntax expressionElement) {
                    return new ExpressionResult(false);
                }

                ProcessExpression(semantic, context, expressionElement.Expression, lines);
                if (i < collectionExpression.Elements.Count - 1) {
                    lines.Add(", ");
                }
            }

            lines.Add(" }");
            return new ExpressionResult(true, VariablePath.Unknown, null);
        }

        protected override ExpressionResult ProcessParenthesizedExpression(SemanticModel semantic, LayerContext context, ParenthesizedExpressionSyntax parenthesizedExpression, List<string> lines) {
            lines.Add("(");
            ExpressionResult result = ProcessExpression(semantic, context, parenthesizedExpression.Expression, lines);
            lines.Add(")");
            return result;
        }

        protected override void ProcessBaseExpression(SemanticModel semantic, LayerContext context, BaseExpressionSyntax baseExpression, List<string> lines) {
            ConversionClass? currentClass = context.GetCurrentClass();
            string baseClassName = currentClass?.Extensions?.FirstOrDefault() ?? currentClass?.Name ?? "base";
            lines.Add(baseClassName);

            ConversionClass? baseClass = context.Program.Classes.FirstOrDefault(c => c.Name == baseClassName);
            context.AddClass(baseClass ?? currentClass);
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
                    name = "String";
                    break;
                case "char":
                    name = "String";
                    break;
                case "object":
                    name = "void";
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
            bool emittedAnySegment = false;

            foreach (var content in interpolatedString.Contents) {
                if (content is InterpolatedStringTextSyntax text) {
                    string textValue = text.TextToken.ValueText;
                    if (textValue.Length == 0) {
                        continue;
                    }

                    AppendInterpolatedStringSegment(lines, $"std::string(\"{EscapeCppStringLiteral(textValue)}\")", ref emittedAnySegment);
                    continue;
                }

                if (content is InterpolationSyntax interpolation) {
                    AppendInterpolatedStringSegment(lines, BuildInterpolatedExpressionSegment(semantic, context, interpolation), ref emittedAnySegment);
                }
            }

            if (!emittedAnySegment) {
                lines.Add("std::string()");
            }

            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
        }

        void AppendInterpolatedStringSegment(List<string> lines, string segment, ref bool emittedAnySegment) {
            if (emittedAnySegment) {
                lines.Add(" + ");
            }

            lines.Add(segment);
            emittedAnySegment = true;
        }

        string BuildInterpolatedExpressionSegment(SemanticModel semantic, LayerContext context, InterpolationSyntax interpolation) {
            List<string> interpolationLines = new List<string>();
            ProcessExpression(semantic, context, interpolation.Expression, interpolationLines);

            string expressionText = string.Concat(interpolationLines);
            VariableType expressionType = ResolveInterpolationType(semantic, interpolation.Expression);

            if (expressionType.Type == VariableDataType.String) {
                return expressionText;
            }

            if (expressionType.Type == VariableDataType.Char) {
                return $"std::string(1, {expressionText})";
            }

            if (IsNativeInterpolationType(expressionType.Type)) {
                return $"std::to_string({expressionText})";
            }

            return $"{expressionText}->ToString()";
        }

        VariableType ResolveInterpolationType(SemanticModel semantic, ExpressionSyntax expression) {
            TypeInfo typeInfo = semantic.GetTypeInfo(expression);
            ITypeSymbol expressionType = typeInfo.ConvertedType ?? typeInfo.Type;

            if (expressionType != null) {
                return VariableUtil.GetVarType(expressionType);
            }

            return VariableUtil.GetVarType("string");
        }

        bool IsNativeInterpolationType(VariableDataType dataType) {
            switch (dataType) {
                case VariableDataType.Single:
                case VariableDataType.Double:
                case VariableDataType.UInt32:
                case VariableDataType.Int32:
                case VariableDataType.UInt64:
                case VariableDataType.Int64:
                case VariableDataType.Int8:
                case VariableDataType.UInt8:
                case VariableDataType.Int16:
                case VariableDataType.UInt16:
                case VariableDataType.Boolean:
                    return true;
                default:
                    return false;
            }
        }

        static bool IsNativeToStringTypeSymbol(ITypeSymbol typeSymbol) {
            if (typeSymbol == null) {
                return false;
            }

            return typeSymbol.SpecialType switch {
                SpecialType.System_Single => true,
                SpecialType.System_Double => true,
                SpecialType.System_UInt32 => true,
                SpecialType.System_Int32 => true,
                SpecialType.System_UInt64 => true,
                SpecialType.System_Int64 => true,
                SpecialType.System_SByte => true,
                SpecialType.System_Byte => true,
                SpecialType.System_Int16 => true,
                SpecialType.System_UInt16 => true,
                SpecialType.System_Boolean => true,
                _ => false
            };
        }

        string EscapeCppStringLiteral(string value) {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
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

            lines.Add("static_cast<");
            lines.Add(varType.ToCPPString(context.Program));
            lines.Add(">(");
            ProcessExpression(semantic, context, castExpr.Expression, lines);
            lines.Add(")");

            return new ExpressionResult(true, VariablePath.Unknown, varType);
        }

        protected override void ProcessConditionalExpression(SemanticModel semantic, LayerContext context, ConditionalExpressionSyntax conditional, List<string> lines) {
            if (TryProcessNullableConditionalExpression(semantic, context, conditional, lines)) {
                return;
            }

            // Process the condition (before the ?)
            ProcessExpression(semantic, context, conditional.Condition, lines);
            lines.Add(" ? ");

            // Process the true branch (after the ? and before the :)
            ProcessExpression(semantic, context, conditional.WhenTrue, lines);
            lines.Add(" : ");

            // Process the false branch (after the :)
            ProcessExpression(semantic, context, conditional.WhenFalse, lines);
        }

        bool TryProcessNullableConditionalExpression(
            SemanticModel semantic,
            LayerContext context,
            ConditionalExpressionSyntax conditional,
            List<string> lines) {
            ITypeSymbol? convertedTypeSymbol = ResolveNullableConditionalTargetType(semantic, context, conditional);
            VariableType convertedSourceType;
            if (IsNullableTypeSymbol(convertedTypeSymbol)) {
                convertedSourceType = VariableUtil.GetVarType(convertedTypeSymbol);
            } else {
                FunctionStack? currentFunction = context.GetCurrentFunction();
                if (conditional.Parent is not ReturnStatementSyntax ||
                    currentFunction?.Function?.ReturnType?.IsNullable != true) {
                    return false;
                }

                convertedSourceType = currentFunction.Function.ReturnType;
            }

            CPPTypeData convertedTypeData;
            VariableType convertedCppType = ConvertToCPPType(convertedSourceType, out convertedTypeData);
            string nullableTypeName = convertedCppType.ToCPPString(context.Program);

            ProcessExpression(semantic, context, conditional.Condition, lines);
            lines.Add(" ? ");
            AppendNullableConditionalBranch(semantic, context, conditional.WhenTrue, nullableTypeName, lines);
            lines.Add(" : ");
            AppendNullableConditionalBranch(semantic, context, conditional.WhenFalse, nullableTypeName, lines);
            return true;
        }

        void AppendNullableConditionalBranch(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax branchExpression,
            string nullableTypeName,
            List<string> lines) {
            if (branchExpression.IsKind(SyntaxKind.NullLiteralExpression) ||
                branchExpression.IsKind(SyntaxKind.DefaultLiteralExpression)) {
                lines.Add($"{nullableTypeName}(nullptr)");
                return;
            }

            lines.Add($"{nullableTypeName}(");
            ProcessExpression(semantic, context, branchExpression, lines);
            lines.Add(")");
        }

        ITypeSymbol? ResolveNullableConditionalTargetType(
            SemanticModel semantic,
            LayerContext context,
            ConditionalExpressionSyntax conditional) {
            TypeInfo conditionalTypeInfo = semantic.GetTypeInfo(conditional);
            if (IsNullableTypeSymbol(conditionalTypeInfo.ConvertedType)) {
                return conditionalTypeInfo.ConvertedType;
            }

            if (IsNullableTypeSymbol(conditionalTypeInfo.Type)) {
                return conditionalTypeInfo.Type;
            }

            if (conditional.Parent is AssignmentExpressionSyntax assignmentExpression) {
                TypeInfo assignmentTypeInfo = semantic.GetTypeInfo(assignmentExpression.Left);
                ITypeSymbol? assignmentType = assignmentTypeInfo.ConvertedType ?? assignmentTypeInfo.Type;
                if (IsNullableTypeSymbol(assignmentType)) {
                    return assignmentType;
                }

                ISymbol? assignmentSymbol = semantic.GetSymbolInfo(assignmentExpression.Left).Symbol;
                if (assignmentSymbol is IPropertySymbol propertySymbol && IsNullableTypeSymbol(propertySymbol.Type)) {
                    return propertySymbol.Type;
                }

                if (assignmentSymbol is IFieldSymbol fieldSymbol && IsNullableTypeSymbol(fieldSymbol.Type)) {
                    return fieldSymbol.Type;
                }
            }

            if (conditional.Parent is EqualsValueClauseSyntax equalsValueClause &&
                equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator &&
                variableDeclarator.Parent is VariableDeclarationSyntax variableDeclaration) {
                TypeInfo declarationTypeInfo = semantic.GetTypeInfo(variableDeclaration.Type);
                ITypeSymbol? declarationType = declarationTypeInfo.ConvertedType ?? declarationTypeInfo.Type;
                if (IsNullableTypeSymbol(declarationType)) {
                    return declarationType;
                }
            }
            return null;
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
                VariableType declarationType = VariableUtil.GetVarType(usingStatement.Declaration.Type, semantic);
                CPPTypeData declarationTypeData;
                ConvertToCPPType(declarationType, out declarationTypeData);
                string memberAccessOperator = declarationTypeData.IsPointer ? "->" : ".";

                foreach (var variable in usingStatement.Declaration.Variables) {
                    lines.Add($"{variable.Identifier.Text}{memberAccessOperator}Dispose();\n");
                }
            } else if (usingStatement.Expression != null) {
                lines.Add(usingStatement.Expression.ToString() + ".Dispose();\n");
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
            lines.Add("for (const auto& ");
            lines.Add(forEachStatement.Identifier.Text);
            lines.Add(" : ");
            if (ShouldDereferenceForEachExpression(semantic, forEachStatement.Expression)) {
                lines.Add("*");
            }

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
            if (TryProcessDeclarationPatternIfStatement(semantic, context, ifStatement, lines, out ExpressionResult patternResult)) {
                return patternResult;
            }

            List<string> conditionLines = new List<string>();
            int start = context.DepthClass;
            ExpressionResult condResult = ProcessExpression(semantic, context, ifStatement.Condition, conditionLines);
            context.PopClass(start);

            if (condResult.BeforeLines != null && condResult.BeforeLines.Count > 0) {
                lines.AddRange(condResult.BeforeLines);
            }

            lines.Add("    if (");
            lines.AddRange(conditionLines);
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

        /// <summary>
        /// Attempts to lower a declaration-pattern guard of the form <c>if (value is TargetType target)</c>.
        /// </summary>
        /// <param name="semantic">Semantic model that resolves the source type information.</param>
        /// <param name="context">Current lowering context for the active class and function.</param>
        /// <param name="ifStatement">If statement being lowered.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <param name="result">Lowering result for the processed guard expression.</param>
        /// <returns><c>true</c> when the declaration-pattern guard was lowered by this specialized path; otherwise, <c>false</c>.</returns>
        bool TryProcessDeclarationPatternIfStatement(
            SemanticModel semantic,
            LayerContext context,
            IfStatementSyntax ifStatement,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (ifStatement.Condition is not IsPatternExpressionSyntax isPatternExpression) {
                return false;
            }

            if (isPatternExpression.Pattern is not DeclarationPatternSyntax declarationPattern) {
                return false;
            }

            if (declarationPattern.Designation is not SingleVariableDesignationSyntax designation) {
                return false;
            }

            VariableType declaredType = VariableUtil.GetVarType(declarationPattern.Type, semantic);
            CPPTypeData declaredTypeData;
            VariableType cppDeclaredType = ConvertToCPPType(declaredType, out declaredTypeData);
            if (!declaredTypeData.IsPointer) {
                return false;
            }

            List<string> conditionExpressionLines = new List<string>();
            int start = context.DepthClass;
            ExpressionResult conditionExpression = ProcessExpression(semantic, context, isPatternExpression.Expression, conditionExpressionLines);
            context.PopClass(start);
            if (!conditionExpression.Processed) {
                result = conditionExpression;
                return true;
            }

            if (conditionExpression.BeforeLines != null && conditionExpression.BeforeLines.Count > 0) {
                lines.AddRange(conditionExpression.BeforeLines);
            }

            string declaredTypeName = cppDeclaredType.ToCPPString(context.Program);
            string variableName = designation.Identifier.Text;
            string sourceExpression = string.Join(string.Empty, conditionExpressionLines);

            codeConverter?.RegisterRuntimeRequirement("NativeCast");

            FunctionStack currentFunction = context.GetCurrentFunction();
            ConversionVariable conversionVariable = null;
            if (currentFunction != null) {
                conversionVariable = new ConversionVariable();
                conversionVariable.Name = variableName;
                conversionVariable.VarType = declaredType;
                currentFunction.Stack.Add(conversionVariable);
            }

            lines.Add($"    {declaredTypeName}* {variableName} = he_cpp_try_cast<{declaredTypeName}>({sourceExpression});\n");
            lines.Add($"    if ({variableName} != nullptr)\n");
            lines.Add("    {\n");

            ExpressionResult statementResult = ProcessStatement(semantic, context, ifStatement.Statement, lines);
            lines.Add("    }\n");

            if (ifStatement.Else != null) {
                lines.Add("else ");
                if (ifStatement.Else.Statement is IfStatementSyntax elseIfStatement) {
                    ProcessIfStatement(semantic, context, elseIfStatement, lines);
                } else {
                    lines.Add("{\n");
                    ProcessStatement(semantic, context, ifStatement.Else.Statement, lines);
                    lines.Add("}\n");
                }
            }

            result = new ExpressionResult(
                statementResult.Processed,
                conversionVariable != null ? VariablePath.FunctionStack : VariablePath.Unknown,
                declaredType);

            if (conversionVariable != null) {
                result.Variable = conversionVariable;
            }

            return true;
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

            if (parsedType != null && parsedType.IsNullable) {
                codeConverter?.RegisterRuntimeRequirement("NativeNullable");

                VariableType nullableBaseSourceType = new VariableType(parsedType);
                nullableBaseSourceType.IsNullable = false;

                CPPTypeData nullableBaseTypeData;
                VariableType nullableBaseType = ConvertToCPPType(nullableBaseSourceType, out nullableBaseTypeData);

                if (nullableBaseTypeData.IsPointer) {
                    nullableBaseType = new VariableType(
                        VariableDataType.Unknown,
                        $"{nullableBaseType.ToCPPString(null)}*");
                }

                typeData.IsArray = false;
                typeData.IsNativeType = false;
                typeData.IsPointer = false;

                return new VariableType(VariableDataType.Unknown, "Nullable", genericArgs: [nullableBaseType]);
            }

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
                case VariableDataType.Int8: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "int8_t");
                }
                case VariableDataType.UInt8: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "uint8_t");
                }
                case VariableDataType.Int16: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "int16_t");
                }
                case VariableDataType.UInt16: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "uint16_t");
                }
                case VariableDataType.Boolean: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "bool");
                }
                case VariableDataType.Char: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "char");
                }

                case VariableDataType.String:

                    if (codeConverter.CPPRules.UseStdString) {
                        codeConverter?.RegisterRuntimeRequirement("NativeString");
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "std::string");
                } else {
                        typeData.IsArray = true;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "char");
                }
                case VariableDataType.List: {
                        codeConverter?.RegisterRuntimeRequirement("NativeList");
                        typeData.IsArray = false;
                        typeData.IsNativeType = false;
                        typeData.IsPointer = true;
                        return CreateConvertedGenericType(parsedType, "List");
                }
                case VariableDataType.Dictionary: {
                        codeConverter?.RegisterRuntimeRequirement("NativeDictionary");
                        typeData.IsArray = false;
                        typeData.IsNativeType = false;
                        typeData.IsPointer = true;
                        return CreateConvertedGenericType(parsedType, "Dictionary");
                }
                case VariableDataType.Object: {
                        typeData.IsArray = false;
                        typeData.IsPointer = true;
                        if (string.Equals(parsedType.TypeName, "object", StringComparison.OrdinalIgnoreCase)) {
                            typeData.IsNativeType = true;
                            return new VariableType(parsedType.Type, "void");
                        }

                        if (string.Equals(parsedType.TypeName, "DateTime", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.DateTime", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeDateTime");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "DateTime");
                        }

                        if (string.Equals(parsedType.TypeName, "TimeSpan", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.TimeSpan", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeDateTime");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "TimeSpan");
                        }

                        if (string.Equals(parsedType.TypeName, "StringBuilder", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Text.StringBuilder", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("StringBuilder");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "StringBuilder");
                        }

                        if (string.Equals(parsedType.TypeName, "StringReader", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.IO.StringReader", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("StringReader");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "StringReader");
                        }

                        if (string.Equals(parsedType.TypeName, "StreamReader", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.IO.StreamReader", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("StreamReader");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "StreamReader");
                        }

                        if (string.Equals(parsedType.TypeName, "Event", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeEvent");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "Event");
                        }

                        if (IsRegexRuntimeTypeName(parsedType.TypeName)) {
                            codeConverter?.RegisterRuntimeRequirement("Regex");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, NormalizeRegexRuntimeTypeName(parsedType.TypeName));
                        }

                        if (string.Equals(parsedType.TypeName, "Stack", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Collections.Generic.Stack", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeStack");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = true;
                            return CreateConvertedGenericType(parsedType, "Stack");
                        }

                        if (string.Equals(parsedType.TypeName, "Type", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Type", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeType");
                            typeData.IsNativeType = false;
                            return new VariableType(parsedType.Type, "Type");
                        }

                        if (string.Equals(parsedType.TypeName, "BinaryPrimitives", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Buffers.Binary.BinaryPrimitives", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("BinaryPrimitives");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "BinaryPrimitives");
                        }

                        if (string.Equals(parsedType.TypeName, "MathF", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.MathF", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("Math");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "MathF");
                        }

                        if (string.Equals(parsedType.TypeName, "Span", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Span", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "ReadOnlySpan", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.ReadOnlySpan", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeSpan");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return CreateConvertedGenericType(parsedType, "Span");
                        }

                        if (string.Equals(parsedType.TypeName, "IntPtr", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.IntPtr", StringComparison.Ordinal)) {
                            typeData.IsNativeType = true;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "intptr_t");
                        }

                        if (string.Equals(parsedType.TypeName, "UIntPtr", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.UIntPtr", StringComparison.Ordinal)) {
                            typeData.IsNativeType = true;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "uintptr_t");
                        }

                        if (string.Equals(parsedType.TypeName, "IDisposable", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.IDisposable", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeDisposable");
                        }

                        if (string.Equals(parsedType.TypeName, "IEquatable", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.IEquatable", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeEquatable");
                        }

                        if (IsNativeExceptionTypeName(parsedType.TypeName)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeExceptions");
                        }

                        typeData.IsNativeType = false;
                        return parsedType;
                }
                case VariableDataType.Array: {
                        codeConverter?.RegisterRuntimeRequirement("NativeArray");
                        typeData.IsArray = false;
                        typeData.IsNativeType = false;
                        typeData.IsPointer = true;
                        return parsedType;
                }
                default:
                    typeData.IsArray = false;
                    typeData.IsNativeType = false;
                    typeData.IsPointer = true;
                    return parsedType;
            }
        }

        /// <summary>
        /// Determines whether a lowered expression should use direct value-member access instead of pointer-member access.
        /// </summary>
        /// <param name="result">Lowered receiver expression metadata.</param>
        /// <returns><c>true</c> when the receiver is a lightweight runtime value type; otherwise <c>false</c>.</returns>
        static bool UsesDirectMemberAccess(ExpressionResult result) {
            return result.Type != null &&
                (result.Type.IsNullable ||
                 string.Equals(result.Type.TypeName, "Nullable", StringComparison.Ordinal) ||
                 IsDirectRuntimeTypeName(result.Type.TypeName));
        }

        /// <summary>
        /// Determines whether a lowered expression should use direct value-member access by inspecting Roslyn type information.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve the expression type.</param>
        /// <param name="expression">Expression to inspect.</param>
        /// <returns><c>true</c> when the expression resolves to a lightweight runtime value type; otherwise <c>false</c>.</returns>
        static bool UsesDirectMemberAccess(SemanticModel semantic, ExpressionSyntax expression) {
            if (semantic == null || expression == null) {
                return false;
            }

            return TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol typeSymbol) &&
                (IsNullableTypeSymbol(typeSymbol) ||
                 IsDirectRuntimeTypeName(typeSymbol.Name) ||
                 IsDirectRuntimeTypeName(typeSymbol.ToDisplayString()));
        }

        /// <summary>
        /// Determines whether a lowered member should use direct value-member access by inspecting its declaring type.
        /// </summary>
        /// <param name="symbol">Resolved member symbol.</param>
        /// <returns><c>true</c> when the member belongs to a lightweight runtime value type; otherwise <c>false</c>.</returns>
        static bool UsesDirectMemberAccess(ISymbol symbol) {
            if (symbol == null || symbol.ContainingType == null) {
                return false;
            }

            return IsNullableTypeSymbol(symbol.ContainingType) ||
                IsDirectRuntimeTypeName(symbol.ContainingType.Name) ||
                IsDirectRuntimeTypeName(symbol.ContainingType.ToDisplayString());
        }

        /// <summary>
        /// Determines whether an expression syntax refers to the managed DateTime type.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve the expression symbol.</param>
        /// <param name="expression">Expression to inspect.</param>
        /// <returns><c>true</c> when the expression resolves to DateTime; otherwise <c>false</c>.</returns>
        static bool IsDateTimeTypeReference(SemanticModel semantic, ExpressionSyntax expression) {
            if (expression == null) {
                return false;
            }

            ISymbol symbol = semantic.GetSymbolInfo(expression).Symbol;
            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            if (symbol is INamedTypeSymbol namedTypeSymbol) {
                return string.Equals(namedTypeSymbol.Name, "DateTime", StringComparison.Ordinal) ||
                    string.Equals(namedTypeSymbol.ToDisplayString(), "System.DateTime", StringComparison.Ordinal);
            }

            return string.Equals(expression.ToString(), "DateTime", StringComparison.Ordinal) ||
                string.Equals(expression.ToString(), "System.DateTime", StringComparison.Ordinal);
        }

        /// <summary>
        /// Attempts to resolve a static managed runtime receiver such as System.IO.Path so member access can emit C++ static scope syntax.
        /// </summary>
        /// <param name="semantic">Semantic model used to inspect the receiver expression.</param>
        /// <param name="expression">Receiver expression from the member access.</param>
        /// <param name="runtimeTypeName">Receives the emitted C++ runtime type name when a match is found.</param>
        /// <param name="runtimeRequirementName">Receives the runtime requirement that must be registered for the emitted type.</param>
        /// <returns><c>true</c> when the receiver resolves to a known static runtime type; otherwise <c>false</c>.</returns>
        static bool TryResolveStaticRuntimeType(
            SemanticModel semantic,
            ExpressionSyntax expression,
            out string runtimeTypeName,
            out string runtimeRequirementName) {
            runtimeTypeName = string.Empty;
            runtimeRequirementName = string.Empty;

            if (expression == null) {
                return false;
            }

            ISymbol? symbol = semantic.GetSymbolInfo(expression).Symbol;
            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            if (symbol is INamedTypeSymbol namedTypeSymbol &&
                TryMapStaticRuntimeTypeName(namedTypeSymbol.Name, namedTypeSymbol.ToDisplayString(), out runtimeTypeName, out runtimeRequirementName)) {
                return true;
            }

            string expressionText = expression.ToString();
            return TryMapStaticRuntimeTypeName(expressionText, expressionText, out runtimeTypeName, out runtimeRequirementName);
        }

        /// <summary>
        /// Maps managed static runtime type references to their emitted C++ runtime types and registration keys.
        /// </summary>
        /// <param name="shortTypeName">Short Roslyn type name when available.</param>
        /// <param name="qualifiedTypeName">Qualified Roslyn type name when available.</param>
        /// <param name="runtimeTypeName">Receives the emitted C++ runtime type name when a match is found.</param>
        /// <param name="runtimeRequirementName">Receives the runtime requirement key when a match is found.</param>
        /// <returns><c>true</c> when the supplied type maps to a known emitted runtime type; otherwise <c>false</c>.</returns>
        static bool TryMapStaticRuntimeTypeName(
            string shortTypeName,
            string qualifiedTypeName,
            out string runtimeTypeName,
            out string runtimeRequirementName) {
            runtimeTypeName = string.Empty;
            runtimeRequirementName = string.Empty;

            if (string.Equals(shortTypeName, "Path", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.IO.Path", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.IO.Path", StringComparison.Ordinal)) {
                runtimeTypeName = "Path";
                runtimeRequirementName = "Path";
                return true;
            }

            if (string.Equals(shortTypeName, "File", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.IO.File", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.IO.File", StringComparison.Ordinal)) {
                runtimeTypeName = "File";
                runtimeRequirementName = "File";
                return true;
            }

            if (string.Equals(shortTypeName, "Math", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Math", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Math", StringComparison.Ordinal)) {
                runtimeTypeName = "Math";
                runtimeRequirementName = "Math";
                return true;
            }

            if (string.Equals(shortTypeName, "MathF", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.MathF", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.MathF", StringComparison.Ordinal)) {
                runtimeTypeName = "MathF";
                runtimeRequirementName = "Math";
                return true;
            }

            if (string.Equals(shortTypeName, "Debug", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Diagnostics.Debug", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Diagnostics.Debug", StringComparison.Ordinal)) {
                runtimeTypeName = "System::Diagnostics::Debug";
                runtimeRequirementName = "Debug";
                return true;
            }

            if (string.Equals(shortTypeName, "string", StringComparison.Ordinal) ||
                string.Equals(shortTypeName, "String", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.String", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.String", StringComparison.Ordinal)) {
                runtimeTypeName = "String";
                runtimeRequirementName = "NativeString";
                return true;
            }

            if (string.Equals(shortTypeName, "StringComparer", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.StringComparer", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.StringComparer", StringComparison.Ordinal)) {
                runtimeTypeName = "StringComparer";
                runtimeRequirementName = "StringComparer";
                return true;
            }

            if (string.Equals(shortTypeName, "StringComparison", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.StringComparison", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.StringComparison", StringComparison.Ordinal)) {
                runtimeTypeName = "StringComparison";
                runtimeRequirementName = "NativeString";
                return true;
            }

            if (string.Equals(shortTypeName, "Encoding", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Text.Encoding", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Text.Encoding", StringComparison.Ordinal)) {
                runtimeTypeName = "Encoding";
                runtimeRequirementName = "Encoding";
                return true;
            }

            if (string.Equals(shortTypeName, "BinaryPrimitives", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Buffers.Binary.BinaryPrimitives", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Buffers.Binary.BinaryPrimitives", StringComparison.Ordinal)) {
                runtimeTypeName = "BinaryPrimitives";
                runtimeRequirementName = "BinaryPrimitives";
                return true;
            }

            if (string.Equals(shortTypeName, "MidpointRounding", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.MidpointRounding", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.MidpointRounding", StringComparison.Ordinal)) {
                runtimeTypeName = "MidpointRounding";
                runtimeRequirementName = "Math";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether a variable type represents the managed DateTime runtime value.
        /// </summary>
        /// <param name="variableType">Variable type metadata to inspect.</param>
        /// <returns><c>true</c> when the variable represents DateTime; otherwise <c>false</c>.</returns>
        static bool IsDateTimeVariableType(VariableType variableType) {
            return variableType != null &&
                (string.Equals(variableType.TypeName, "DateTime", StringComparison.Ordinal) ||
                 string.Equals(variableType.TypeName, "System.DateTime", StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a variable type represents the managed TimeSpan runtime value.
        /// </summary>
        /// <param name="variableType">Variable type metadata to inspect.</param>
        /// <returns><c>true</c> when the variable represents TimeSpan; otherwise <c>false</c>.</returns>
        static bool IsTimeSpanVariableType(VariableType variableType) {
            return variableType != null &&
                (string.Equals(variableType.TypeName, "TimeSpan", StringComparison.Ordinal) ||
                 string.Equals(variableType.TypeName, "System.TimeSpan", StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a Roslyn type symbol represents a managed Nullable{T} value.
        /// </summary>
        /// <param name="typeSymbol">Type symbol to inspect.</param>
        /// <returns><c>true</c> when the symbol represents Nullable{T}; otherwise, <c>false</c>.</returns>
        static bool IsNullableTypeSymbol(ITypeSymbol? typeSymbol) {
            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return false;
            }

            string displayText = namedTypeSymbol.ToDisplayString();
            return string.Equals(namedTypeSymbol.Name, "Nullable", StringComparison.Ordinal) &&
                (string.Equals(namedTypeSymbol.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal) ||
                 displayText.StartsWith("System.Nullable", StringComparison.Ordinal) ||
                 displayText.StartsWith("Nullable<", StringComparison.Ordinal));
        }

        static bool IsActionTypeSymbol(ITypeSymbol? typeSymbol) {
            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return false;
            }

            string displayText = namedTypeSymbol.ToDisplayString();
            return string.Equals(namedTypeSymbol.Name, "Action", StringComparison.Ordinal) &&
                (string.Equals(namedTypeSymbol.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal) ||
                 displayText.StartsWith("System.Action", StringComparison.Ordinal) ||
                 string.Equals(displayText, "Action", StringComparison.Ordinal) ||
                 displayText.StartsWith("Action<", StringComparison.Ordinal));
        }

        static bool IsEventTypeSymbol(ITypeSymbol? typeSymbol) {
            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return false;
            }

            string displayText = namedTypeSymbol.ToDisplayString();
            return string.Equals(namedTypeSymbol.Name, "Event", StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(namedTypeSymbol.ContainingNamespace?.ToDisplayString()) ||
                 string.Equals(displayText, "Event", StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a type-name string refers to the managed StringBuilder runtime value.
        /// </summary>
        /// <param name="typeName">Source type name to inspect.</param>
        /// <returns><c>true</c> when the source type refers to StringBuilder; otherwise <c>false</c>.</returns>
        static bool IsStringBuilderTypeName(string typeName) {
            return string.Equals(typeName, "StringBuilder", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Text.StringBuilder", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Text.StringBuilder", StringComparison.Ordinal);
        }

        static bool IsStringExpression(SemanticModel semantic, ExpressionSyntax expression) {
            return TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol typeSymbol) &&
                typeSymbol.SpecialType == SpecialType.System_String;
        }

        static bool IsStringRuntimeTypeReference(SemanticModel semantic, ExpressionSyntax expression) {
            if (expression == null) {
                return false;
            }

            ISymbol symbol = semantic.GetSymbolInfo(expression).Symbol;
            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            if (symbol is INamedTypeSymbol namedTypeSymbol) {
                return namedTypeSymbol.SpecialType == SpecialType.System_String;
            }

            string expressionText = expression.ToString();
            return string.Equals(expressionText, "string", StringComparison.Ordinal) ||
                string.Equals(expressionText, "String", StringComparison.Ordinal) ||
                string.Equals(expressionText, "System.String", StringComparison.Ordinal);
        }

        static bool IsDictionaryTypeSymbol(ITypeSymbol typeSymbol) {
            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return false;
            }

            string displayText = namedTypeSymbol.ToDisplayString();
            return string.Equals(namedTypeSymbol.Name, "Dictionary", StringComparison.Ordinal) ||
                string.Equals(namedTypeSymbol.Name, "IDictionary", StringComparison.Ordinal) ||
                string.Equals(namedTypeSymbol.Name, "IReadOnlyDictionary", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.Dictionary<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.IDictionary<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.IReadOnlyDictionary<", StringComparison.Ordinal);
        }

        bool ShouldDereferenceForEachExpression(SemanticModel semantic, ExpressionSyntax expression) {
            if (expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is IdentifierNameSyntax identifierName &&
                (string.Equals(identifierName.Identifier.Text, "Keys", StringComparison.Ordinal) ||
                 string.Equals(identifierName.Identifier.Text, "Values", StringComparison.Ordinal))) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol typeSymbol)) {
                return false;
            }

            if (typeSymbol is IArrayTypeSymbol) {
                return true;
            }

            return IsCountableCollectionTypeSymbol(typeSymbol);
        }

        void AppendInvocationArguments(
            SemanticModel semantic,
            LayerContext context,
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            List<string> lines) {
            for (int index = 0; index < arguments.Count; index++) {
                ArgumentSyntax argument = arguments[index];
                int start = context.DepthClass;
                ProcessExpression(semantic, context, argument.Expression, lines);
                context.PopClass(start);

                if (index < arguments.Count - 1) {
                    lines.Add(", ");
                }
            }
        }

        string RenderExpressionText(SemanticModel semantic, LayerContext context, ExpressionSyntax expression) {
            List<string> expressionLines = new List<string>();
            int start = context.DepthClass;
            ProcessExpression(semantic, context, expression, expressionLines);
            context.PopClass(start);
            return string.Concat(expressionLines);
        }

        static bool IsNativeExceptionTypeName(string typeName) {
            return string.Equals(typeName, "Exception", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Exception", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Exception", StringComparison.Ordinal) ||
                string.Equals(typeName, "ArgumentException", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.ArgumentException", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.ArgumentException", StringComparison.Ordinal) ||
                string.Equals(typeName, "ArgumentNullException", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.ArgumentNullException", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.ArgumentNullException", StringComparison.Ordinal) ||
                string.Equals(typeName, "ArgumentOutOfRangeException", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.ArgumentOutOfRangeException", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.ArgumentOutOfRangeException", StringComparison.Ordinal) ||
                string.Equals(typeName, "InvalidOperationException", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.InvalidOperationException", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.InvalidOperationException", StringComparison.Ordinal) ||
                string.Equals(typeName, "EndOfStreamException", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.IO.EndOfStreamException", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.IO.EndOfStreamException", StringComparison.Ordinal) ||
                string.Equals(typeName, "NotSupportedException", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.NotSupportedException", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.NotSupportedException", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a type-name string refers to the managed StringReader runtime value.
        /// </summary>
        /// <param name="typeName">Source type name to inspect.</param>
        /// <returns><c>true</c> when the source type refers to StringReader; otherwise <c>false</c>.</returns>
        static bool IsStringReaderTypeName(string typeName) {
            return string.Equals(typeName, "StringReader", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.IO.StringReader", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.IO.StringReader", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a type-name string refers to the managed StreamReader runtime value.
        /// </summary>
        /// <param name="typeName">Source type name to inspect.</param>
        /// <returns><c>true</c> when the source type refers to StreamReader; otherwise <c>false</c>.</returns>
        static bool IsStreamReaderTypeName(string typeName) {
            return string.Equals(typeName, "StreamReader", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.IO.StreamReader", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.IO.StreamReader", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a type-name string refers to the lightweight regex runtime surface used by transpiled managed parsers.
        /// </summary>
        /// <param name="typeName">Source type name to inspect.</param>
        /// <returns><c>true</c> when the source type refers to the regex runtime surface; otherwise, <c>false</c>.</returns>
        static bool IsRegexRuntimeTypeName(string typeName) {
            return string.Equals(typeName, "Regex", StringComparison.Ordinal) ||
                string.Equals(typeName, "Match", StringComparison.Ordinal) ||
                string.Equals(typeName, "MatchCollection", StringComparison.Ordinal) ||
                string.Equals(typeName, "Group", StringComparison.Ordinal) ||
                string.Equals(typeName, "GroupCollection", StringComparison.Ordinal) ||
                string.Equals(typeName, "RegexOptions", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Text.RegularExpressions.Regex", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Text.RegularExpressions.Match", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Text.RegularExpressions.MatchCollection", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Text.RegularExpressions.Group", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Text.RegularExpressions.GroupCollection", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Text.RegularExpressions.RegexOptions", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Text.RegularExpressions.Regex", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Text.RegularExpressions.Match", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Text.RegularExpressions.MatchCollection", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Text.RegularExpressions.Group", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Text.RegularExpressions.GroupCollection", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Text.RegularExpressions.RegexOptions", StringComparison.Ordinal);
        }

        /// <summary>
        /// Normalizes a source regex runtime type name to its emitted C++ runtime type name.
        /// </summary>
        /// <param name="typeName">Source type name to normalize.</param>
        /// <returns>The emitted C++ runtime type name.</returns>
        static string NormalizeRegexRuntimeTypeName(string typeName) {
            return typeName switch {
                "System.Text.RegularExpressions.Regex" => "Regex",
                "global::System.Text.RegularExpressions.Regex" => "Regex",
                "System.Text.RegularExpressions.Match" => "Match",
                "global::System.Text.RegularExpressions.Match" => "Match",
                "System.Text.RegularExpressions.MatchCollection" => "MatchCollection",
                "global::System.Text.RegularExpressions.MatchCollection" => "MatchCollection",
                "System.Text.RegularExpressions.Group" => "Group",
                "global::System.Text.RegularExpressions.Group" => "Group",
                "System.Text.RegularExpressions.GroupCollection" => "GroupCollection",
                "global::System.Text.RegularExpressions.GroupCollection" => "GroupCollection",
                "System.Text.RegularExpressions.RegexOptions" => "RegexOptions",
                "global::System.Text.RegularExpressions.RegexOptions" => "RegexOptions",
                _ => typeName
            };
        }

        /// <summary>
        /// Determines whether a source type name represents a lightweight runtime value type that should be constructed without heap allocation.
        /// </summary>
        /// <param name="typeName">Source type name to inspect.</param>
        /// <returns><c>true</c> when the type should be emitted as a direct runtime value; otherwise <c>false</c>.</returns>
        static bool IsValueRuntimeTypeName(string typeName) {
            return IsStringBuilderTypeName(typeName) ||
                IsStringReaderTypeName(typeName) ||
                IsStreamReaderTypeName(typeName) ||
                string.Equals(typeName, "Regex", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Text.RegularExpressions.Regex", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Text.RegularExpressions.Regex", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a variable type represents the managed StringBuilder runtime value.
        /// </summary>
        /// <param name="variableType">Variable type metadata to inspect.</param>
        /// <returns><c>true</c> when the variable represents StringBuilder; otherwise <c>false</c>.</returns>
        static bool IsStringBuilderVariableType(VariableType variableType) {
            return variableType != null &&
                IsStringBuilderTypeName(variableType.TypeName);
        }

        /// <summary>
        /// Determines whether a variable type represents the managed StringReader runtime value.
        /// </summary>
        /// <param name="variableType">Variable type metadata to inspect.</param>
        /// <returns><c>true</c> when the variable represents StringReader; otherwise <c>false</c>.</returns>
        static bool IsStringReaderVariableType(VariableType variableType) {
            return variableType != null &&
                IsStringReaderTypeName(variableType.TypeName);
        }

        /// <summary>
        /// Determines whether a variable type represents the managed StreamReader runtime value.
        /// </summary>
        /// <param name="variableType">Variable type metadata to inspect.</param>
        /// <returns><c>true</c> when the variable represents StreamReader; otherwise <c>false</c>.</returns>
        static bool IsStreamReaderVariableType(VariableType variableType) {
            return variableType != null &&
                IsStreamReaderTypeName(variableType.TypeName);
        }

        /// <summary>
        /// Determines whether a variable type represents the lightweight regex runtime surface.
        /// </summary>
        /// <param name="variableType">Variable type metadata to inspect.</param>
        /// <returns><c>true</c> when the variable represents a regex runtime value; otherwise, <c>false</c>.</returns>
        static bool IsRegexRuntimeVariableType(VariableType variableType) {
            return variableType != null &&
                IsRegexRuntimeTypeName(variableType.TypeName);
        }

        /// <summary>
        /// Determines whether a type-name string represents a lightweight runtime value that should use direct member access.
        /// </summary>
        /// <param name="typeName">Type name to inspect.</param>
        /// <returns><c>true</c> when the type should use direct member access; otherwise <c>false</c>.</returns>
        static bool IsDirectRuntimeTypeName(string typeName) {
            return string.Equals(typeName, "DateTime", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.DateTime", StringComparison.Ordinal) ||
                string.Equals(typeName, "TimeSpan", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.TimeSpan", StringComparison.Ordinal) ||
                IsStringBuilderTypeName(typeName) ||
                IsStringReaderTypeName(typeName) ||
                IsStreamReaderTypeName(typeName) ||
                IsRegexRuntimeTypeName(typeName);
        }

        /// <summary>
        /// Attempts to recover the resolved type symbol for an expression from Roslyn semantic information.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve the expression.</param>
        /// <param name="expression">Expression to inspect.</param>
        /// <param name="typeSymbol">Resolved type symbol when available.</param>
        /// <returns><c>true</c> when a concrete type symbol was recovered; otherwise <c>false</c>.</returns>
        static bool TryGetExpressionTypeSymbol(SemanticModel semantic, ExpressionSyntax expression, out ITypeSymbol typeSymbol) {
            TypeInfo typeInfo = semantic.GetTypeInfo(expression);
            typeSymbol = typeInfo.ConvertedType ?? typeInfo.Type;
            if (typeSymbol != null) {
                return true;
            }

            ISymbol symbol = semantic.GetSymbolInfo(expression).Symbol;
            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            if (symbol is ILocalSymbol localSymbol) {
                typeSymbol = localSymbol.Type;
                return typeSymbol != null;
            }

            if (symbol is IParameterSymbol parameterSymbol) {
                typeSymbol = parameterSymbol.Type;
                return typeSymbol != null;
            }

            if (symbol is IFieldSymbol fieldSymbol) {
                typeSymbol = fieldSymbol.Type;
                return typeSymbol != null;
            }

            if (symbol is IPropertySymbol propertySymbol) {
                typeSymbol = propertySymbol.Type;
                return typeSymbol != null;
            }

            if (symbol is IEventSymbol eventSymbol) {
                typeSymbol = eventSymbol.Type;
                return typeSymbol != null;
            }

            if (symbol is IMethodSymbol methodSymbol) {
                typeSymbol = methodSymbol.ReturnType;
                return typeSymbol != null;
            }

            if (symbol is INamedTypeSymbol namedTypeSymbol) {
                typeSymbol = namedTypeSymbol;
                return true;
            }

            typeSymbol = null;
            return false;
        }

        /// <summary>
        /// Creates a converted generic type shell whose generic arguments have already been normalized for C++ emission.
        /// </summary>
        /// <param name="parsedType">The source generic type.</param>
        /// <param name="cppTypeName">The emitted C++ generic type name.</param>
        /// <returns>A generic variable type that carries converted C++ generic arguments.</returns>
        VariableType CreateConvertedGenericType(VariableType parsedType, string cppTypeName) {
            List<VariableType> convertedGenericArguments = new List<VariableType>();

            foreach (VariableType genericArgument in parsedType.GenericArgs) {
                CPPTypeData genericTypeData;
                VariableType convertedGenericArgument = ConvertToCPPType(genericArgument, out genericTypeData);

                if (genericTypeData.IsPointer) {
                    if (convertedGenericArgument.Type == VariableDataType.Array) {
                        convertedGenericArgument = new VariableType(
                            convertedGenericArgument.Type,
                            convertedGenericArgument.TypeName,
                            convertedGenericArgument.Args.ToList(),
                            convertedGenericArgument.GenericArgs.ToList());
                    } else {
                        convertedGenericArgument = new VariableType(
                            VariableDataType.Unknown,
                            $"{convertedGenericArgument.ToCPPString(null)}*");
                    }
                }

                convertedGenericArguments.Add(convertedGenericArgument);
            }

            return new VariableType(parsedType.Type, cppTypeName, parsedType.Args.ToList(), convertedGenericArguments);
        }

        bool IsSystemObjectType(SemanticModel semantic, TypeSyntax typeSyntax) {
            if (typeSyntax is PredefinedTypeSyntax predefinedTypeSyntax &&
                predefinedTypeSyntax.Keyword.ValueText == "object") {
                return true;
            }

            TypeInfo typeInfo = semantic.GetTypeInfo(typeSyntax);
            ITypeSymbol typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
            return typeSymbol?.SpecialType == SpecialType.System_Object;
        }

        protected override void ProcessDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            List<string> lines
            ) {
            VariableType varType = VariableUtil.GetVarType(declaration.Type, semantic);

            if (TryProcessStackAllocDeclaration(semantic, context, declaration, varType, lines)) {
                return;
            }

            if (TryProcessCollectionExpressionArrayDeclaration(semantic, context, declaration, varType, lines)) {
                return;
            }

            if (TryProcessStringReaderLineDeclaration(semantic, context, declaration, varType, lines)) {
                return;
            }

            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(varType, out typeData);

            FunctionStack fnStack = context.GetCurrentFunction();

            string pointer = typeData.IsPointer ? " *" : " ";
            List<string> newLines = [$"{cppType.ToCPPString(context.Program)}{pointer}"];

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

                if (typeData.IsArray &&
                    cppType.Type != VariableDataType.String &&
                    !string.Equals(cppType.TypeName, "std::string", StringComparison.Ordinal)) {
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

        /// <summary>
        /// Lowers StringReader.ReadLine declarations to a lightweight nullable line wrapper that preserves null end-of-stream semantics.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the declaration.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="declaration">Declaration being evaluated.</param>
        /// <param name="declarationType">Resolved abstract type for the declaration.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <returns><c>true</c> when the declaration was handled by this specialized lowering path; otherwise, <c>false</c>.</returns>
        bool TryProcessStringReaderLineDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            VariableType declarationType,
            List<string> lines) {
            if (declaration == null || declarationType == null) {
                return false;
            }

            if (declaration.Variables.Count != 1 || declarationType.Type != VariableDataType.String) {
                return false;
            }

            VariableDeclaratorSyntax variable = declaration.Variables[0];
            if (variable.Initializer?.Value is not InvocationExpressionSyntax invocationExpression) {
                return false;
            }

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                !string.Equals(memberAccess.Name.Identifier.Text, "ReadLine", StringComparison.Ordinal)) {
                return false;
            }

            ITypeSymbol receiverTypeSymbol = semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (receiverTypeSymbol == null ||
                !string.Equals(receiverTypeSymbol.Name, "StringReader", StringComparison.Ordinal)) {
                return false;
            }

            RegisterRuntimeRequirement("StringReader");

            FunctionStack fn = context.GetCurrentFunction();
            if (fn != null) {
                ConversionVariable conversionVariable = new ConversionVariable();
                conversionVariable.Name = variable.Identifier.ToString();
                conversionVariable.VarType = declarationType;
                fn.Stack.Add(conversionVariable);
            }

            lines.Add("StringReaderLine ");
            lines.Add(variable.Identifier.ToString());
            lines.Add(" = ");

            int start = context.DepthClass;
            ProcessExpression(semantic, context, variable.Initializer.Value, lines);
            context.PopClass(start);
            return true;
        }

        /// <summary>
        /// Lowers local span declarations backed by stackalloc into fixed-size native C++ buffers.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the declaration.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="declaration">Declaration being evaluated.</param>
        /// <param name="declarationType">Resolved abstract type for the declaration.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <returns><c>true</c> when the declaration was handled by this specialized lowering path; otherwise, <c>false</c>.</returns>
        bool TryProcessStackAllocDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            VariableType declarationType,
            List<string> lines) {
            if (declaration.Variables.Count != 1) {
                return false;
            }

            if (declarationType.TypeName != "Span" || declarationType.GenericArgs.Count != 1) {
                return false;
            }

            VariableDeclaratorSyntax variable = declaration.Variables[0];
            if (variable.Initializer?.Value is not StackAllocArrayCreationExpressionSyntax stackAllocExpression) {
                return false;
            }

            if (stackAllocExpression.Type is not ArrayTypeSyntax stackAllocType) {
                return false;
            }

            if (stackAllocType.RankSpecifiers.Count != 1 || stackAllocType.RankSpecifiers[0].Sizes.Count != 1) {
                return false;
            }

            VariableType elementType = VariableUtil.GetVarType(stackAllocType.ElementType, semantic);
            CPPTypeData elementTypeData;
            VariableType cppElementType = ConvertToCPPType(elementType, out elementTypeData);
            List<string> sizeLines = new List<string>();
            ExpressionResult sizeResult = ProcessExpression(semantic, context, stackAllocType.RankSpecifiers[0].Sizes[0], sizeLines);
            if (!sizeResult.Processed) {
                return false;
            }

            FunctionStack currentFunction = context.GetCurrentFunction();
            if (currentFunction != null) {
                ConversionVariable stackVariable = new ConversionVariable();
                stackVariable.Name = variable.Identifier.ToString();
                stackVariable.VarType = declarationType;
                currentFunction.Stack.Add(stackVariable);
            }

            lines.Add(cppElementType.ToCPPString(context.Program));
            lines.Add(" ");
            lines.Add(variable.Identifier.ToString());
            lines.Add("[");
            lines.AddRange(sizeLines);
            lines.Add("]");
            return true;
        }

        /// <summary>
        /// Lowers local array declarations initialized from C# collection expressions into C++ fixed-size arrays.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the declaration.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="declaration">Declaration being evaluated.</param>
        /// <param name="declarationType">Resolved abstract type for the declaration.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <returns><c>true</c> when the declaration was handled by this specialized lowering path; otherwise, <c>false</c>.</returns>
        bool TryProcessCollectionExpressionArrayDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            VariableType declarationType,
            List<string> lines) {
            if (declaration.Variables.Count != 1) {
                return false;
            }

            if (declaration.Type is not ArrayTypeSyntax arrayType || arrayType.RankSpecifiers.Count != 1) {
                return false;
            }

            VariableDeclaratorSyntax variable = declaration.Variables[0];
            if (variable.Initializer?.Value is not CollectionExpressionSyntax collectionExpression) {
                return false;
            }

            VariableType elementType = VariableUtil.GetVarType(arrayType.ElementType, semantic);
            CPPTypeData elementTypeData;
            VariableType cppElementType = ConvertToCPPType(elementType, out elementTypeData);

            FunctionStack currentFunction = context.GetCurrentFunction();
            if (currentFunction != null) {
                ConversionVariable stackVariable = new ConversionVariable();
                stackVariable.Name = variable.Identifier.ToString();
                stackVariable.VarType = declarationType;
                currentFunction.Stack.Add(stackVariable);
            }

            lines.Add(cppElementType.ToCPPString(context.Program));
            lines.Add(" ");
            lines.Add(variable.Identifier.ToString());
            lines.Add("[] = ");
            ProcessCollectionExpression(semantic, context, collectionExpression, lines);
            return true;
        }

        /// <summary>
        /// Emits native C++ dynamic-array allocation for single-dimension and jagged-array creation expressions.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the array creation.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="arrayCreation">Array creation syntax being lowered.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <returns><c>true</c> when the array creation was lowered as a native allocation; otherwise, <c>false</c>.</returns>
        bool TryProcessDynamicArrayCreation(
            SemanticModel semantic,
            LayerContext context,
            ArrayCreationExpressionSyntax arrayCreation,
            List<string> lines) {
            if (arrayCreation.Type.RankSpecifiers.Count == 0) {
                return false;
            }

            ArrayRankSpecifierSyntax outerRankSpecifier = arrayCreation.Type.RankSpecifiers[0];
            if (outerRankSpecifier.Sizes.Count != 1 || outerRankSpecifier.Sizes[0] is OmittedArraySizeExpressionSyntax) {
                return false;
            }

            for (int i = 1; i < arrayCreation.Type.RankSpecifiers.Count; i++) {
                ArrayRankSpecifierSyntax rankSpecifier = arrayCreation.Type.RankSpecifiers[i];
                if (rankSpecifier.Sizes.Count != 1 || rankSpecifier.Sizes[0] is not OmittedArraySizeExpressionSyntax) {
                    return false;
                }
            }

            VariableType elementType = VariableUtil.GetVarType(arrayCreation.Type.ElementType, semantic);
            CPPTypeData elementTypeData;
            VariableType cppElementType = ConvertToCPPType(elementType, out elementTypeData);

            lines.Add("new ");
            lines.Add(cppElementType.ToCPPString(context.Program));
            for (int i = 1; i < arrayCreation.Type.RankSpecifiers.Count; i++) {
                lines.Add("*");
            }
            lines.Add("[");
            ProcessExpression(semantic, context, outerRankSpecifier.Sizes[0], lines);
            lines.Add("]");
            return true;
        }

        /// <summary>
        /// Emits a native C++ <c>sizeof(...)</c> expression for stack buffer sizes and similar constant-length operations.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the expression.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="sizeOfExpression">Roslyn sizeof expression syntax.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <returns>A processed result for the emitted size expression.</returns>
        ExpressionResult ProcessSizeOfExpression(
            SemanticModel semantic,
            LayerContext context,
            SizeOfExpressionSyntax sizeOfExpression,
            List<string> lines) {
            VariableType sourceType = VariableUtil.GetVarType(sizeOfExpression.Type, semantic);
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);

            lines.Add("sizeof(");
            lines.Add(cppType.ToCPPString(context.Program));
            lines.Add(")");
            return new ExpressionResult(true, VariablePath.Unknown, new VariableType(VariableDataType.UInt64, "size_t"));
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
                    literalValue = NormalizeNumericLiteral(literalExpression.Token.Text);
                    type = "int";
                    break;
                case SyntaxKind.CharacterLiteralExpression: {
                        type = "char";
                        literalValue = literalExpression.Token.Text;
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
                    literalValue = "nullptr";
                    break;
                case SyntaxKind.DefaultLiteralExpression:
                    type = "null";
                    literalValue = "nullptr";
                    break;
                default:
                    throw new Exception("Unsupported literal type");
            }

            lines.Add(literalValue);

            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(type));
        }

        static string NormalizeNumericLiteral(string literalText) {
            if (string.IsNullOrWhiteSpace(literalText)) {
                return literalText;
            }

            if (literalText.EndsWith("d", StringComparison.Ordinal) || literalText.EndsWith("D", StringComparison.Ordinal)) {
                string withoutSuffix = literalText[..^1];
                if (!withoutSuffix.Contains('.', StringComparison.Ordinal) &&
                    !withoutSuffix.Contains('e', StringComparison.OrdinalIgnoreCase)) {
                    return withoutSuffix + ".0";
                }

                return withoutSuffix;
            }

            if ((literalText.EndsWith("f", StringComparison.Ordinal) || literalText.EndsWith("F", StringComparison.Ordinal)) &&
                !literalText.Contains('.', StringComparison.Ordinal) &&
                !literalText.Contains('e', StringComparison.OrdinalIgnoreCase)) {
                return literalText[..^1] + ".0f";
            }

            return literalText;
        }

        protected override void ProcessReturnStatement(SemanticModel semantic, LayerContext context, ReturnStatementSyntax ret, List<string> lines) {
            if (ret.Expression == null) {
                lines.Add("return;");
                return;
            }

            int start = context.Class.Count;
            List<string> returnLines = new List<string>();
            ExpressionResult result = ProcessExpression(semantic, context, ret.Expression, returnLines);

            if (result.BeforeLines != null) {
                lines.AddRange(result.BeforeLines);
            }

            if (result.AfterLines == null || result.AfterLines.Count == 0) {
                lines.Add("return ");
                lines.AddRange(returnLines);
                lines.Add(";");
            } else {
                lines.Add("auto ___result = ");
                lines.AddRange(returnLines);
                lines.Add(";\n");
                lines.AddRange(result.AfterLines);
                lines.Add("return ___result;");
            }

            context.PopClass(start);
        }

        protected override ExpressionResult ProcessDeclarationExpressionSyntax(SemanticModel semantic, LayerContext context, DeclarationExpressionSyntax declaration, List<string> lines) {
            if (declaration.Designation is SingleVariableDesignationSyntax single) {
                string identifier = single.Identifier.Text;
                lines.Add(identifier);

                VariableType variableType = VariableUtil.GetVarType(declaration.Type, semantic);
                ConversionVariable conversionVariable = null;
                FunctionStack fn = context.GetCurrentFunction();
                if (fn != null) {
                    conversionVariable = new ConversionVariable {
                        Name = identifier,
                        VarType = variableType,
                        Modifier = ParameterModifier.Out
                    };
                    fn.Stack.Add(conversionVariable);
                }

                ExpressionResult result = new ExpressionResult(true, conversionVariable != null ? VariablePath.FunctionStack : VariablePath.Unknown, variableType);
                if (conversionVariable != null) {
                    result.Variable = conversionVariable;
                }

                return result;
            }

            if (declaration.Designation is DiscardDesignationSyntax) {
                lines.Add("_");
                return new ExpressionResult(true);
            }

            lines.Add(declaration.Designation.ToString());
            return new ExpressionResult(true);
        }

        public override void ProcessArrowExpressionClause(SemanticModel semantic, LayerContext context, ArrowExpressionClauseSyntax arrowExpression, List<string> lines) {
            lines.Add(" = ");
            ProcessExpression(semantic, context, arrowExpression.Expression, lines);
        }

        /// <summary>
        /// Gets the current number of recorded conversion diagnostics.
        /// </summary>
        /// <returns>The number of diagnostics attached to the active converter report.</returns>
        int GetDiagnosticCount() {
            if (codeConverter == null) {
                return 0;
            }

            return codeConverter.Report.Diagnostics.Count;
        }

        /// <summary>
        /// Records a generic unsupported-construct diagnostic for a Roslyn syntax node.
        /// </summary>
        /// <param name="context">Current lowering context that identifies the active type and member.</param>
        /// <param name="node">Unsupported syntax node.</param>
        /// <param name="message">Human-readable explanation of the unsupported construct.</param>
        void ReportUnsupportedNode(LayerContext context, SyntaxNode node, string message) {
            if (codeConverter == null || node == null) {
                return;
            }

            ConversionClass currentClass = context?.GetCurrentClass();
            FunctionStack currentFunction = context?.GetCurrentFunction();
            string sourceTypeName = currentClass?.Name ?? string.Empty;
            string sourceMemberName = currentFunction?.Function?.Name ?? string.Empty;
            string filePath = node.SyntaxTree?.FilePath ?? string.Empty;
            string recommendation = "Add a lowering rule for this syntax or move the behavior behind a native runtime adapter.";

            codeConverter.ReportUnsupportedConstruct(
                sourceTypeName,
                sourceMemberName,
                node.Kind().ToString(),
                message,
                recommendation,
                filePath);
        }
    }
}
