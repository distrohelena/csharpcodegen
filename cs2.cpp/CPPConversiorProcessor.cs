using cs2.core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
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

            if (expression is BinaryExpressionSyntax binaryExpression &&
                binaryExpression.IsKind(SyntaxKind.IsExpression)) {
                return ProcessIsTypeCheckExpression(semantic, context, binaryExpression, lines);
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

            VariableType cppType = ConvertToCPPType(VariableUtil.GetVarType(typeSymbol), out _);
            return new ExpressionResult(true, VariablePath.Unknown, cppType);
        }

        ExpressionResult ProcessIsTypeCheckExpression(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binaryExpression,
            List<string> lines) {
            VariableType targetType;
            if (binaryExpression.Right is TypeSyntax typeSyntax) {
                targetType = VariableUtil.GetVarType(typeSyntax, semantic);
            } else {
                targetType = VariableUtil.GetVarType(binaryExpression.Right.ToString());
            }

            CPPTypeData targetTypeData;
            VariableType cppTargetType = ConvertToCPPType(targetType, out targetTypeData);
            if (!targetTypeData.IsPointer) {
                List<string> valueSourceLines = new List<string>();
                int valueStart = context.DepthClass;
                ExpressionResult valueSourceResult = ProcessExpression(semantic, context, binaryExpression.Left, valueSourceLines);
                context.PopClass(valueStart);
                if (!valueSourceResult.Processed) {
                    return valueSourceResult;
                }

                lines.AddRange(valueSourceLines);
                lines.Add(" != nullptr");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
            }

            List<string> sourceLines = new List<string>();
            int start = context.DepthClass;
            ExpressionResult sourceResult = ProcessExpression(semantic, context, binaryExpression.Left, sourceLines);
            context.PopClass(start);
            if (!sourceResult.Processed) {
                return sourceResult;
            }

            RegisterRuntimeRequirement("NativeCast");

            string targetTypeName = cppTargetType.ToCPPString(context.Program);
            lines.Add($"he_cpp_try_cast<{targetTypeName}>(");
            lines.AddRange(sourceLines);
            lines.Add(") != nullptr");
            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
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

            List<string> receiverLines = new List<string>();
            int startReceiver = context.DepthClass;
            ProcessExpression(semantic, context, conditionalAccess.Expression, receiverLines);
            context.PopClass(startReceiver);

            if (IsEventExpression(semantic, conditionalAccess.Expression)) {
                lines.AddRange(receiverLines);
                lines.Add(".Invoke(");

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

                lines.Add(");\n");
                return true;
            }

            if (!TryGetExpressionTypeSymbol(semantic, conditionalAccess.Expression, out ITypeSymbol receiverTypeSymbol) ||
                !IsActionTypeSymbol(receiverTypeSymbol)) {
                return false;
            }

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
            if (TryProcessNativeListCapacityAssignment(semantic, context, assignment, lines)) {
                return;
            }

            if (TryProcessComputedPropertyAssignment(semantic, context, assignment, lines)) {
                return;
            }

            if (TryProcessValueTypeCompoundAssignment(semantic, context, assignment, lines)) {
                return;
            }

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
            bool isQualifiedMemberName = identifier.Parent is MemberAccessExpressionSyntax qualifiedMemberAccess &&
                ReferenceEquals(qualifiedMemberAccess.Name, identifier);

            ISymbol? nsSymbol = semantic.GetSymbolInfo(identifier).Symbol;
            if (nsSymbol is IAliasSymbol aliasSymbol) {
                nsSymbol = aliasSymbol.Target;
            }

            if (nsSymbol is IMethodSymbol accessorMethodSymbol &&
                accessorMethodSymbol.AssociatedSymbol is IPropertySymbol associatedPropertySymbol) {
                nsSymbol = associatedPropertySymbol;
            }

            if ((nsSymbol == null || nsSymbol is not IPropertySymbol && nsSymbol is not IFieldSymbol && nsSymbol is not IMethodSymbol) &&
                identifier.Parent is MemberAccessExpressionSyntax parentMemberAccess &&
                ReferenceEquals(parentMemberAccess.Name, identifier)) {
                ITypeSymbol? receiverTypeSymbol = semantic.GetTypeInfo(parentMemberAccess.Expression).ConvertedType ?? semantic.GetTypeInfo(parentMemberAccess.Expression).Type;
                if (receiverTypeSymbol != null) {
                    nsSymbol = receiverTypeSymbol.GetMembers(name)
                        .FirstOrDefault(member => member is IPropertySymbol || member is IFieldSymbol || member is IMethodSymbol);
                }
            }

            if (nsSymbol is IMethodSymbol methodSymbol) {
                isMethod = true;
            }

            string propertyGetterCallName = string.Empty;
            if (nsSymbol is IPropertySymbol propertySymbol &&
                TryBuildPropertyGetterCall(identifier, propertySymbol, out string resolvedGetterCallName)) {
                propertyGetterCallName = resolvedGetterCallName;
            }

            VariablePath varPath = VariablePath.Unknown;
            if (nsSymbol is INamespaceSymbol) {
                varPath = VariablePath.Static;
            } else if (nsSymbol is INamespaceOrTypeSymbol nameSpaceType && nameSpaceType.IsType) {
                varPath = VariablePath.Static;
            }

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
                bool isResolvedPropertyAccessor = !string.IsNullOrEmpty(propertyGetterCallName) &&
                    nsSymbol is IPropertySymbol;
                bool isClassVar = ((classVar != null || isResolvedPropertyAccessor) &&
                    functionInVar == null &&
                    matchingVars.Count == 0) ||
                    (classFn != null &&
                    functionInVar == null &&
                    matchingVars.Count == 0);


                if (isClassVar && !isQualifiedMemberName) {
                    bool isStaticClassMember = false;
                    ISymbol identifierSymbol = semantic.GetSymbolInfo(identifier).Symbol;
                    if (identifierSymbol is IAliasSymbol identifierAliasSymbol) {
                        identifierSymbol = identifierAliasSymbol.Target;
                    }

                    if (identifierSymbol is IFieldSymbol fieldSymbol) {
                        isStaticClassMember = fieldSymbol.IsStatic;
                    } else if (identifierSymbol is IPropertySymbol identifierPropertySymbol) {
                        isStaticClassMember = identifierPropertySymbol.IsStatic;
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

                if (classFn == null || string.IsNullOrEmpty(classFn.Remap)) {
                    ConversionVariable? varOnClass = currentClass.Variables.FirstOrDefault(c => c.Name == name);
                    if (emitMethodGroupPointer) {
                        lines.Add($"&{currentClass.GetEmittedTypeName()}::");
                    }

                    if (!string.IsNullOrEmpty(propertyGetterCallName)) {
                        lines.Add(propertyGetterCallName);
                    } else if (varOnClass == null || string.IsNullOrEmpty(varOnClass.Remap)) {
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
            } else if (!string.IsNullOrEmpty(propertyGetterCallName) &&
                nsSymbol is IPropertySymbol resolvedPropertySymbol) {
                VariableType propertyType = VariableUtil.GetVarType(resolvedPropertySymbol.Type);
                ConversionClass cl = context.Program.Classes.Find(c => c.Name == propertyType.GetTypeScriptType(context.Program));
                context.AddClass(cl);
                ExpressionResult res = new ExpressionResult(true, varPath, propertyType);
                res.Class = cl;
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
        /// Determines whether a constructor argument must be stabilized into a temporary to preserve C# left-to-right evaluation.
        /// </summary>
        /// <param name="expression">Argument expression being evaluated for constructor emission.</param>
        /// <returns><c>true</c> when the argument may observe different behavior under native argument reordering; otherwise <c>false</c>.</returns>
        static bool RequiresStableConstructorArgumentEvaluation(ExpressionSyntax expression) {
            if (expression == null) {
                return false;
            }

            foreach (SyntaxNode node in expression.DescendantNodesAndSelf()) {
                if (node is InvocationExpressionSyntax ||
                    node is ObjectCreationExpressionSyntax ||
                    node is ElementAccessExpressionSyntax ||
                    node is AssignmentExpressionSyntax ||
                    node is AwaitExpressionSyntax ||
                    node is ConditionalAccessExpressionSyntax) {
                    return true;
                }

                if (node is PrefixUnaryExpressionSyntax prefixUnary &&
                    (prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) ||
                     prefixUnary.IsKind(SyntaxKind.PreDecrementExpression))) {
                    return true;
                }

                if (node is PostfixUnaryExpressionSyntax postfixUnary &&
                    (postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) ||
                     postfixUnary.IsKind(SyntaxKind.PostDecrementExpression))) {
                    return true;
                }
            }

            return false;
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
            string memberAccessOperator = UsesDirectMemberAccess(result) ? "." : "->";

            foreach (ExpressionSyntax expression in initializer.Expressions) {
                if (expression is not AssignmentExpressionSyntax assignment) {
                    continue;
                }

                if (TryAppendObjectInitializerSetterAssignment(semantic, context, objectCreation, objectName, memberAccessOperator, assignment, lines)) {
                    continue;
                }

                lines.Add(objectName);
                lines.Add(memberAccessOperator);
                AppendObjectInitializerTarget(semantic, context, assignment.Left, lines);

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

        void AppendObjectInitializerTarget(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax expression,
            List<string> lines) {
            string targetText = RenderExpressionText(semantic, context, expression);
            targetText = StripObjectInitializerReceiverPrefix(targetText);
            lines.Add(targetText);
        }

        static string StripObjectInitializerReceiverPrefix(string targetText) {
            if (string.IsNullOrWhiteSpace(targetText)) {
                return targetText;
            }

            string trimmedTarget = targetText;
            while (trimmedTarget.StartsWith("this->", StringComparison.Ordinal)) {
                trimmedTarget = trimmedTarget["this->".Length..];
            }

            while (trimmedTarget.StartsWith("this.", StringComparison.Ordinal)) {
                trimmedTarget = trimmedTarget["this.".Length..];
            }

            while (trimmedTarget.StartsWith("(*this).", StringComparison.Ordinal)) {
                trimmedTarget = trimmedTarget["(*this).".Length..];
            }

            return trimmedTarget;
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

            if (TryProcessStringObjectCreation(semantic, context, objectCreation, lines, out VariableType stringObjectCreationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, stringObjectCreationType);
            }

            int diagnosticCount = GetDiagnosticCount();
            VariableType sourceType = null;
            VariableType cppType = sourceType;
            bool hasConvertedType = false;
            CPPTypeData cppTypeData = default;
            string objectCreationTypeName = objectCreation.Type.ToString();
            ITypeSymbol objectCreationTypeSymbol = ResolveObjectCreationTypeSymbol(semantic, objectCreation);
            int generatedTypeArity = objectCreationTypeSymbol is INamedTypeSymbol namedObjectCreationTypeSymbol
                ? namedObjectCreationTypeSymbol.TypeArguments.Length
                : 0;
            ConversionClass explicitGeneratedClass = context.Program.FindGeneratedClass(objectCreationTypeName, generatedTypeArity);
            bool hasRuntimeObjectTypeMapping = TryMapObjectCreationRuntimeTypeName(
                objectCreationTypeName,
                objectCreationTypeSymbol?.ToDisplayString() ?? string.Empty,
                out string runtimeObjectTypeName,
                out string runtimeRequirementName);

            if (IsNativeExceptionTypeName(objectCreationTypeName)) {
                sourceType = VariableUtil.GetVarType(NormalizeNativeExceptionTypeName(objectCreationTypeName));
            } else if (hasRuntimeObjectTypeMapping) {
                if (!string.IsNullOrWhiteSpace(runtimeRequirementName)) {
                    codeConverter?.RegisterRuntimeRequirement(runtimeRequirementName);
                }

                sourceType = VariableUtil.GetVarType(runtimeObjectTypeName);
            } else if (explicitGeneratedClass != null) {
                sourceType = VariableUtil.GetVarType(objectCreation.Type, semantic);
            } else {
                if (objectCreationTypeSymbol != null) {
                    sourceType = VariableUtil.GetVarType(objectCreationTypeSymbol.ToDisplayString());
                } else if (TryGetExpressionTypeSymbol(semantic, objectCreation, out ITypeSymbol createdTypeSymbol)) {
                    sourceType = VariableUtil.GetVarType(createdTypeSymbol.ToDisplayString());
                }
            }

            if (sourceType != null) {
                cppType = ConvertToCPPType(sourceType, out cppTypeData);
                hasConvertedType = cppType != null;
                RegisterGeneratedTypeReferences(context, sourceType);
            }

            bool emitHeapAllocation = !hasConvertedType
                ? !IsValueRuntimeTypeName(objectCreation.Type.ToString())
                : cppTypeData.IsPointer;
            IMethodSymbol constructorSymbol = ResolveObjectCreationConstructorSymbol(semantic, objectCreation);
            System.Collections.Immutable.ImmutableArray<IParameterSymbol> constructorParameterSymbols = constructorSymbol != null
                ? constructorSymbol.Parameters
                : System.Collections.Immutable.ImmutableArray<IParameterSymbol>.Empty;
            int explicitArgumentCount = objectCreation.ArgumentList?.Arguments.Count ?? 0;
            bool hasOptionalConstructorArguments = constructorParameterSymbols.Length > explicitArgumentCount &&
                constructorParameterSymbols.Skip(explicitArgumentCount).Any(parameter => parameter.HasExplicitDefaultValue);
            bool requiresStableArgumentEvaluation = explicitArgumentCount > 1 &&
                objectCreation.ArgumentList.Arguments.Any(argument => RequiresStableConstructorArgumentEvaluation(argument.Expression));

            if (requiresStableArgumentEvaluation) {
                lines.Add("([&]() {\n");

                List<string> temporaryArgumentNames = new List<string>();
                for (int i = 0; i < explicitArgumentCount; i++) {
                    ArgumentSyntax arg = objectCreation.ArgumentList.Arguments[i];
                    string temporaryName = "__ctor_arg_" + Guid.NewGuid().ToString("N")[..8];
                    List<string> argumentExpressionLines = new List<string>();

                    int startArg = context.DepthClass;
                    ExpressionResult argumentResult = ProcessExpression(semantic, context, arg.Expression, argumentExpressionLines);
                    context.PopClass(startArg);

                    if (argumentResult.BeforeLines != null && argumentResult.BeforeLines.Count > 0) {
                        lines.AddRange(argumentResult.BeforeLines);
                    }

                    IParameterSymbol parameterSymbol = i < constructorParameterSymbols.Length
                        ? constructorParameterSymbols[i]
                        : null;
                    List<string> loweredArgumentLines = new List<string>();
                    AppendInvocationArgument(
                        semantic,
                        context,
                        arg.Expression,
                        argumentExpressionLines,
                        parameterSymbol,
                        loweredArgumentLines);

                    lines.Add("auto ");
                    lines.Add(temporaryName);
                    lines.Add(" = ");
                    lines.AddRange(loweredArgumentLines);
                    lines.Add(";\n");
                    temporaryArgumentNames.Add(temporaryName);
                }

                lines.Add("return ");
                AppendObjectCreationTypePrefix(
                    semantic,
                    context,
                    objectCreation,
                    lines,
                    emitHeapAllocation,
                    hasConvertedType,
                    explicitGeneratedClass,
                    cppType,
                    hasRuntimeObjectTypeMapping,
                    runtimeObjectTypeName);
                lines.Add("(");
                for (int i = 0; i < temporaryArgumentNames.Count; i++) {
                    lines.Add(temporaryArgumentNames[i]);
                    if (i != temporaryArgumentNames.Count - 1 ||
                        (i == temporaryArgumentNames.Count - 1 && hasOptionalConstructorArguments)) {
                        lines.Add(", ");
                    }
                }

                AppendOptionalInvocationArguments(constructorParameterSymbols, explicitArgumentCount, lines);
                lines.Add(");\n");
                lines.Add("})()");
                return new ExpressionResult(diagnosticCount == GetDiagnosticCount(), VariablePath.Unknown, cppType);
            }

            AppendObjectCreationTypePrefix(
                semantic,
                context,
                objectCreation,
                lines,
                emitHeapAllocation,
                hasConvertedType,
                explicitGeneratedClass,
                cppType,
                hasRuntimeObjectTypeMapping,
                runtimeObjectTypeName);
            lines.Add("(");
            List<string> argumentLines = new List<string>();
            if (objectCreation.ArgumentList != null) {
                for (int i = 0; i < objectCreation.ArgumentList.Arguments.Count; i++) {
                    ArgumentSyntax arg = objectCreation.ArgumentList.Arguments[i];
                    List<string> argumentExpressionLines = new List<string>();

                    int startArg = context.DepthClass;
                    ProcessExpression(semantic, context, arg.Expression, argumentExpressionLines);
                    context.PopClass(startArg);

                    IParameterSymbol parameterSymbol = i < constructorParameterSymbols.Length
                        ? constructorParameterSymbols[i]
                        : null;
                    AppendInvocationArgument(
                        semantic,
                        context,
                        arg.Expression,
                        argumentExpressionLines,
                        parameterSymbol,
                        argumentLines);

                    if (i != objectCreation.ArgumentList.Arguments.Count - 1 ||
                        (i == objectCreation.ArgumentList.Arguments.Count - 1 && hasOptionalConstructorArguments)) {
                        argumentLines.Add(", ");
                    }
                }
            }

            AppendOptionalInvocationArguments(constructorParameterSymbols, explicitArgumentCount, argumentLines);
            lines.AddRange(argumentLines);
            lines.Add(")");
            return new ExpressionResult(diagnosticCount == GetDiagnosticCount(), VariablePath.Unknown, cppType);
        }

        void AppendObjectCreationTypePrefix(
            SemanticModel semantic,
            LayerContext context,
            ObjectCreationExpressionSyntax objectCreation,
            List<string> lines,
            bool emitHeapAllocation,
            bool hasConvertedType,
            ConversionClass explicitGeneratedClass,
            VariableType cppType,
            bool hasRuntimeObjectTypeMapping,
            string runtimeObjectTypeName) {
            if (emitHeapAllocation) {
                lines.Add("new ");
            }

            if (explicitGeneratedClass != null && hasConvertedType) {
                lines.Add(QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context));
            } else if (explicitGeneratedClass != null) {
                lines.Add(QualifyRenderedCppTypeName(explicitGeneratedClass.GetEmittedTypeName(), context));
            } else if (hasConvertedType) {
                lines.Add(QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context));
            } else if (hasRuntimeObjectTypeMapping) {
                lines.Add(runtimeObjectTypeName);
            } else {
                int startDepth = context.DepthClass;
                ProcessExpression(semantic, context, objectCreation.Type, lines);
                context.PopClass(startDepth);
            }
        }

        bool TryProcessStringObjectCreation(
            SemanticModel semantic,
            LayerContext context,
            ObjectCreationExpressionSyntax objectCreation,
            List<string> lines,
            out VariableType resultType) {
            resultType = null;

            if (!IsStringRuntimeTypeReference(semantic, objectCreation.Type) ||
                objectCreation.ArgumentList?.Arguments.Count != 1) {
                return false;
            }

            ArgumentSyntax sourceArgument = objectCreation.ArgumentList.Arguments[0];
            if (!TryGetExpressionTypeSymbol(semantic, sourceArgument.Expression, out ITypeSymbol sourceTypeSymbol) ||
                sourceTypeSymbol is not IArrayTypeSymbol sourceArrayTypeSymbol ||
                sourceArrayTypeSymbol.ElementType.SpecialType != SpecialType.System_Char) {
                return false;
            }

            string sourceText = RenderExpressionText(semantic, context, sourceArgument.Expression);
            lines.Add("std::string(");
            lines.Add($"{sourceText}->Data, static_cast<size_t>({sourceText}->Length)");
            lines.Add(")");
            resultType = VariableUtil.GetVarType("string");
            return true;
        }

        static ITypeSymbol ResolveObjectCreationTypeSymbol(SemanticModel semantic, ObjectCreationExpressionSyntax objectCreation) {
            if (semantic?.GetOperation(objectCreation) is IObjectCreationOperation objectCreationOperation) {
                if (!IsWeakRecoveredTypeSymbol(objectCreationOperation.Constructor?.ContainingType)) {
                    return objectCreationOperation.Constructor.ContainingType;
                }

                if (!IsWeakRecoveredTypeSymbol(objectCreationOperation.Type)) {
                    return objectCreationOperation.Type;
                }
            }

            TypeInfo typeInfo = semantic.GetTypeInfo(objectCreation.Type);
            if (!IsWeakRecoveredTypeSymbol(typeInfo.Type)) {
                return typeInfo.Type;
            }

            ISymbol typeSymbol = semantic.GetSymbolInfo(objectCreation.Type).Symbol;
            if (typeSymbol is IAliasSymbol aliasSymbol) {
                typeSymbol = aliasSymbol.Target;
            }

            if (typeSymbol is INamedTypeSymbol namedTypeSymbol &&
                !IsWeakRecoveredTypeSymbol(namedTypeSymbol)) {
                return namedTypeSymbol;
            }

            if (!IsWeakRecoveredTypeSymbol(typeInfo.ConvertedType)) {
                return typeInfo.ConvertedType;
            }

            return TryGetExpressionTypeSymbol(semantic, objectCreation, out ITypeSymbol createdTypeSymbol)
                ? createdTypeSymbol
                : null;
        }

        IMethodSymbol ResolveObjectCreationConstructorSymbol(SemanticModel semantic, ObjectCreationExpressionSyntax objectCreation) {
            int argumentCount = objectCreation.ArgumentList?.Arguments.Count ?? 0;

            if (semantic.GetOperation(objectCreation) is IObjectCreationOperation objectCreationOperation &&
                CanMethodMatchInvocationArguments(objectCreationOperation.Constructor, argumentCount)) {
                return objectCreationOperation.Constructor;
            }

            SymbolInfo objectCreationSymbolInfo = semantic.GetSymbolInfo(objectCreation);
            IMethodSymbol constructorSymbol = ResolveMethodSymbol(objectCreationSymbolInfo);
            if (CanMethodMatchInvocationArguments(constructorSymbol, argumentCount)) {
                return constructorSymbol;
            }

            return ResolveBestInvocationCandidateMethodSymbol(objectCreationSymbolInfo, argumentCount);
        }

        static bool TryMapObjectCreationRuntimeTypeName(
            string shortTypeName,
            string qualifiedTypeName,
            out string runtimeTypeName,
            out string runtimeRequirementName) {
            runtimeTypeName = string.Empty;
            runtimeRequirementName = string.Empty;

            if (string.Equals(shortTypeName, "MemoryStream", StringComparison.Ordinal) ||
                string.Equals(shortTypeName, "System.IO.MemoryStream", StringComparison.Ordinal) ||
                string.Equals(shortTypeName, "global::System.IO.MemoryStream", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.IO.MemoryStream", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.IO.MemoryStream", StringComparison.Ordinal)) {
                runtimeTypeName = "MemoryStream";
                runtimeRequirementName = "MemoryStream";
                return true;
            }

            return false;
        }

        protected override ExpressionResult ProcessMemberAccessExpressionSyntax(SemanticModel semantic, LayerContext context, MemberAccessExpressionSyntax memberAccess, List<string> lines, List<ExpressionResult> refTypes) {
            if (TryProcessNativeStringLengthMemberAccess(semantic, context, memberAccess, lines, out VariableType stringLengthType)) {
                return new ExpressionResult(true, VariablePath.Unknown, stringLengthType);
            }

            if (TryProcessPrimitiveLimitMemberAccess(memberAccess, lines, out VariableType primitiveLimitType)) {
                return new ExpressionResult(true, VariablePath.Static, primitiveLimitType);
            }

            if (TryProcessPrimitiveNumberRuntimeMemberAccess(memberAccess, lines, out VariableType primitiveNumberType)) {
                return new ExpressionResult(true, VariablePath.Static, primitiveNumberType);
            }

            if (TryProcessRuntimeGetTypeNameMemberAccess(semantic, context, memberAccess, lines, out VariableType runtimeTypeNameType)) {
                return new ExpressionResult(true, VariablePath.Unknown, runtimeTypeNameType);
            }

            if (TryProcessNativeDictionaryKeysMemberAccess(semantic, context, memberAccess, lines, out VariableType dictionaryKeysType)) {
                return new ExpressionResult(true, VariablePath.Unknown, dictionaryKeysType);
            }

            if (TryProcessTupleMemberAccess(semantic, context, memberAccess, lines, out VariableType tupleMemberType)) {
                return new ExpressionResult(true, VariablePath.Unknown, tupleMemberType);
            }

            if (memberAccess.Expression is IdentifierNameSyntax encodingIdentifier &&
                string.Equals(encodingIdentifier.Identifier.Text, "Encoding", StringComparison.Ordinal) &&
                memberAccess.Name is IdentifierNameSyntax encodingMemberIdentifier) {
                lines.Add("Encoding");
                lines.Add("::");
                lines.Add(encodingMemberIdentifier.Identifier.Text);
                return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType("object"));
            }

            if (memberAccess.Expression is IdentifierNameSyntax stringSplitOptionsIdentifier &&
                string.Equals(stringSplitOptionsIdentifier.Identifier.Text, "StringSplitOptions", StringComparison.Ordinal) &&
                memberAccess.Name is IdentifierNameSyntax stringSplitOptionsMemberIdentifier) {
                RegisterRuntimeRequirement("NativeString");
                lines.Add("StringSplitOptions");
                lines.Add("::");
                lines.Add(stringSplitOptionsMemberIdentifier.Identifier.Text);
                return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType("object"));
            }

            if (TryProcessStaticFileIoMemberAccess(memberAccess, lines, out VariableType staticFileIoMemberType)) {
                return new ExpressionResult(true, VariablePath.Static, staticFileIoMemberType);
            }

            if (memberAccess.Expression is IdentifierNameSyntax eventArgsIdentifier &&
                string.Equals(eventArgsIdentifier.Identifier.Text, "EventArgs", StringComparison.Ordinal) &&
                memberAccess.Name is IdentifierNameSyntax eventArgsMemberIdentifier &&
                string.Equals(eventArgsMemberIdentifier.Identifier.Text, "Empty", StringComparison.Ordinal)) {
                lines.Add("nullptr");
                return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType("object"));
            }

            if (memberAccess.Expression is IdentifierNameSyntax appContextIdentifier &&
                string.Equals(appContextIdentifier.Identifier.Text, "AppContext", StringComparison.Ordinal) &&
                memberAccess.Name is IdentifierNameSyntax appContextMemberIdentifier &&
                string.Equals(appContextMemberIdentifier.Identifier.Text, "BaseDirectory", StringComparison.Ordinal)) {
                RegisterRuntimeRequirement("AppContext");
                lines.Add("AppContext");
                lines.Add("::");
                lines.Add("BaseDirectory");
                return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType("string"));
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
                    memberSymbol = ResolveMemberAccessSymbol(semantic, memberAccess);

                    if (memberSymbol is INamespaceSymbol || memberSymbol is INamedTypeSymbol) {
                        useStaticAccess = true;
                    } else if (memberSymbol is IFieldSymbol fieldSymbol && fieldSymbol.IsStatic) {
                        useStaticAccess = true;
                    } else if (memberSymbol is IPropertySymbol memberStaticPropertySymbol && memberStaticPropertySymbol.IsStatic) {
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
                    TryResolveTrackedExpressionVariableType(context, memberAccess.Expression, out VariableType trackedReceiverType) &&
                    IsDirectMemberAccessType(trackedReceiverType) ||
                    (memberAccess.Expression is ElementAccessExpressionSyntax elementAccessExpression &&
                     UsesDirectMemberAccess(semantic, elementAccessExpression.Expression)) ||
                    UsesDirectMemberAccess(memberSymbol);

                if (memberSymbol is IPropertySymbol propertySymbol &&
                    TryBuildPropertyGetterCall(memberAccess, propertySymbol, out string propertyGetterCallName)) {
                    AppendMemberAccessSeparator(lines, useStaticAccess, result.VarPath, useDirectMemberAccess);
                    lines.Add(propertyGetterCallName);
                    VariableType propertyType = VariableUtil.GetVarType(propertySymbol.Type);
                    VariablePath propertyPath = ResolveMemberAccessResultPath(useStaticAccess, result.VarPath, memberSymbol);
                    return new ExpressionResult(true, propertyPath, propertyType);
                }

                if (memberAccess.Name is IdentifierNameSyntax memberIdentifier &&
                    TryResolveGeneratedPropertyGetter(context, semantic, memberAccess.Expression, memberIdentifier.Identifier.Text, out VariableType generatedPropertyType)) {
                    AppendMemberAccessSeparator(lines, useStaticAccess, result.VarPath, useDirectMemberAccess);
                    lines.Add($"get_{memberIdentifier.Identifier.Text}()");
                    VariablePath propertyPath = useStaticAccess ? VariablePath.Unknown : result.VarPath;
                    return new ExpressionResult(true, propertyPath, generatedPropertyType);
                }

                AppendMemberAccessSeparator(lines, useStaticAccess, result.VarPath, useDirectMemberAccess);

                if (!useStaticAccess &&
                    ShouldEmitNativeCountCall(semantic, memberAccess, memberSymbol)) {
                    lines.Add("Count()");
                    return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("int"));
                }

                if (!useStaticAccess &&
                    ShouldEmitNativeCapacityCall(semantic, memberAccess, memberSymbol)) {
                    lines.Add("Capacity()");
                    return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("int"));
                }

                ExpressionResult memberNameResult = ProcessExpression(semantic, context, memberAccess.Name, lines, refTypes);
                VariableType resolvedMemberType = ResolveMemberAccessResultType(memberSymbol) ?? memberNameResult.Type;
                VariablePath resolvedMemberPath = ResolveMemberAccessResultPath(useStaticAccess, result.VarPath, memberSymbol);
                return new ExpressionResult(memberNameResult.Processed, resolvedMemberPath, resolvedMemberType, memberNameResult.BeforeLines, memberNameResult.AfterLines);
            }
            return ProcessExpression(semantic, context, memberAccess.Name, lines, refTypes);
        }

        bool TryProcessComputedPropertyAssignment(SemanticModel semantic, LayerContext context, AssignmentExpressionSyntax assignment, List<string> lines) {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) {
                return false;
            }

            IPropertySymbol propertySymbol = null;
            string propertyName = string.Empty;
            bool useSymbolBackedProperty = TryGetAssignedPropertySymbol(semantic, assignment.Left, out propertySymbol) &&
                propertySymbol.SetMethod != null;
            if (useSymbolBackedProperty) {
                propertyName = propertySymbol.Name;
            } else if (!TryResolveGeneratedAssignedPropertyName(context, semantic, assignment.Left, out propertyName)) {
                return false;
            }

            if (assignment.Left is IdentifierNameSyntax) {
                if (useSymbolBackedProperty && propertySymbol.IsStatic) {
                    lines.Add(GetContainingTypeAccessName(context, propertySymbol.ContainingType));
                    lines.Add("::");
                } else {
                    lines.Add("this->");
                }
            } else if (assignment.Left is MemberAccessExpressionSyntax memberAccess) {
                int startDepth = context.Class.Count;
                ExpressionResult receiverResult = ProcessExpression(semantic, context, memberAccess.Expression, lines);
                context.PopClass(startDepth);

                bool useStaticAccess = receiverResult.VarPath == VariablePath.Static || memberAccess.Expression is BaseExpressionSyntax;
                if (!useStaticAccess) {
                    ISymbol? receiverSymbol = semantic.GetSymbolInfo(memberAccess.Expression).Symbol;
                    if (receiverSymbol is IAliasSymbol receiverAliasSymbol) {
                        receiverSymbol = receiverAliasSymbol.Target;
                    }

                    if (receiverSymbol is INamespaceSymbol || receiverSymbol is INamedTypeSymbol) {
                        useStaticAccess = true;
                    }
                }

                bool useDirectMemberAccess = UsesDirectMemberAccess(receiverResult) ||
                    UsesDirectMemberAccess(semantic, memberAccess.Expression) ||
                    TryResolveTrackedExpressionVariableType(context, memberAccess.Expression, out VariableType trackedReceiverType) &&
                    IsDirectMemberAccessType(trackedReceiverType) ||
                    (memberAccess.Expression is ElementAccessExpressionSyntax elementAccessExpression &&
                     UsesDirectMemberAccess(semantic, elementAccessExpression.Expression));
                AppendMemberAccessSeparator(lines, useStaticAccess, receiverResult.VarPath, useDirectMemberAccess);
            } else {
                return false;
            }

            lines.Add($"set_{propertyName}(");
            int rightStartDepth = context.Class.Count;
            ProcessExpression(semantic, context, assignment.Right, lines);
            context.PopClass(rightStartDepth);
            lines.Add(")");
            return true;
        }

        bool TryProcessValueTypeCompoundAssignment(SemanticModel semantic, LayerContext context, AssignmentExpressionSyntax assignment, List<string> lines) {
            if (assignment == null ||
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression)) {
                return false;
            }

            ISymbol assignmentSymbol = semantic.GetSymbolInfo(assignment.Left).Symbol;
            if (assignmentSymbol is IAliasSymbol aliasSymbol) {
                assignmentSymbol = aliasSymbol.Target;
            }

            if (assignmentSymbol is IEventSymbol) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, assignment.Left, out ITypeSymbol leftTypeSymbol) ||
                IsWeakRecoveredTypeSymbol(leftTypeSymbol)) {
                return false;
            }

            VariableType leftVariableType = VariableUtil.GetVarType(leftTypeSymbol);
            VariableType leftCppType = ConvertToCPPType(leftVariableType, out CPPTypeData leftTypeData);
            if (leftCppType == null ||
                leftTypeData.IsPointer ||
                leftTypeData.IsNativeType) {
                return false;
            }

            string operatorToken = assignment.OperatorToken.Text;
            if (operatorToken.Length < 2 ||
                !operatorToken.EndsWith("=", StringComparison.Ordinal)) {
                return false;
            }

            string binaryOperator = operatorToken[..^1];
            string leftText = RenderExpressionText(semantic, context, assignment.Left);
            if (string.IsNullOrWhiteSpace(leftText)) {
                return false;
            }

            lines.Add(leftText);
            lines.Add(" = ");
            lines.Add(leftText);
            lines.Add($" {binaryOperator} ");

            int rightStartDepth = context.Class.Count;
            ProcessExpression(semantic, context, assignment.Right, lines);
            context.PopClass(rightStartDepth);
            return true;
        }

        static bool TryGetAssignedPropertySymbol(SemanticModel semantic, ExpressionSyntax expression, out IPropertySymbol propertySymbol) {
            propertySymbol = null;

            ISymbol? symbol = semantic.GetSymbolInfo(expression).Symbol;
            if (expression is MemberAccessExpressionSyntax memberAccessExpression) {
                symbol = ResolveMemberAccessSymbol(semantic, memberAccessExpression);
            }

            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            if (symbol is not IPropertySymbol resolvedPropertySymbol) {
                return false;
            }

            propertySymbol = resolvedPropertySymbol;
            return true;
        }

        string GetContainingTypeAccessName(LayerContext context, INamedTypeSymbol containingTypeSymbol) {
            if (containingTypeSymbol == null) {
                return string.Empty;
            }

            ConversionClass generatedClass = context.Program.FindGeneratedClass(containingTypeSymbol.Name, containingTypeSymbol.TypeArguments.Length);
            if (generatedClass != null) {
                return generatedClass.GetEmittedTypeName();
            }

            return containingTypeSymbol.Name;
        }

        static void AppendMemberAccessSeparator(List<string> lines, bool useStaticAccess, VariablePath receiverPath, bool useDirectMemberAccess) {
            switch (useStaticAccess ? VariablePath.Static : receiverPath) {
                case VariablePath.Static:
                    lines.Add("::");
                    break;
                default:
                    lines.Add(useDirectMemberAccess ? "." : "->");
                    break;
            }
        }

        static ISymbol ResolveMemberAccessSymbol(SemanticModel semantic, MemberAccessExpressionSyntax memberAccess) {
            ISymbol? primarySymbol = semantic.GetSymbolInfo(memberAccess).Symbol;
            if (primarySymbol is IAliasSymbol primaryAliasSymbol) {
                primarySymbol = primaryAliasSymbol.Target;
            }

            if (primarySymbol is IMethodSymbol primaryAccessorMethodSymbol &&
                primaryAccessorMethodSymbol.AssociatedSymbol is IPropertySymbol primaryAssociatedPropertySymbol) {
                primarySymbol = primaryAssociatedPropertySymbol;
            }

            if (primarySymbol is IFieldSymbol ||
                primarySymbol is IPropertySymbol ||
                primarySymbol is IMethodSymbol ||
                primarySymbol is IEventSymbol) {
                return primarySymbol;
            }

            ISymbol? nameSymbol = semantic.GetSymbolInfo(memberAccess.Name).Symbol;
            if (nameSymbol is IAliasSymbol nameAliasSymbol) {
                nameSymbol = nameAliasSymbol.Target;
            }

            if (nameSymbol is IMethodSymbol nameAccessorMethodSymbol &&
                nameAccessorMethodSymbol.AssociatedSymbol is IPropertySymbol nameAssociatedPropertySymbol) {
                nameSymbol = nameAssociatedPropertySymbol;
            }

            if (nameSymbol != null) {
                return nameSymbol;
            }

            ITypeSymbol? receiverTypeSymbol = semantic.GetTypeInfo(memberAccess.Expression).ConvertedType ?? semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (IsWeakRecoveredTypeSymbol(receiverTypeSymbol) &&
                TryGetExpressionTypeSymbol(semantic, memberAccess.Expression, out ITypeSymbol recoveredReceiverTypeSymbol) &&
                !IsWeakRecoveredTypeSymbol(recoveredReceiverTypeSymbol)) {
                receiverTypeSymbol = recoveredReceiverTypeSymbol;
            }

            if (receiverTypeSymbol != null) {
                ISymbol receiverMemberSymbol = receiverTypeSymbol.GetMembers(memberAccess.Name.Identifier.Text)
                    .FirstOrDefault(member => member is IPropertySymbol || member is IFieldSymbol || member is IMethodSymbol);
                if (receiverMemberSymbol != null) {
                    return receiverMemberSymbol;
                }
            }

            return primarySymbol;
        }

        bool TryProcessRuntimeGetTypeNameMemberAccess(
            SemanticModel semantic,
            LayerContext context,
            MemberAccessExpressionSyntax memberAccess,
            List<string> lines,
            out VariableType runtimeTypeNameType) {
            runtimeTypeNameType = VariableUtil.GetVarType("object");

            if (!string.Equals(memberAccess.Name.Identifier.Text, "Name", StringComparison.Ordinal) ||
                memberAccess.Expression is not InvocationExpressionSyntax invocationExpression ||
                invocationExpression.Expression is not MemberAccessExpressionSyntax invocationMemberAccess ||
                !string.Equals(invocationMemberAccess.Name.Identifier.Text, "GetType", StringComparison.Ordinal) ||
                invocationExpression.ArgumentList.Arguments.Count != 0) {
                return false;
            }

            ITypeSymbol receiverTypeSymbol = semantic.GetTypeInfo(invocationMemberAccess.Expression).ConvertedType ?? semantic.GetTypeInfo(invocationMemberAccess.Expression).Type;
            if (receiverTypeSymbol == null) {
                return false;
            }

            VariableType receiverSourceType = VariableUtil.GetVarType(receiverTypeSymbol);
            CPPTypeData receiverTypeData;
            VariableType receiverCppType = ConvertToCPPType(receiverSourceType, out receiverTypeData);
            string receiverCppTypeName = receiverCppType.ToCPPString(context.Program);
            string sourceTypeName = string.IsNullOrWhiteSpace(receiverSourceType.TypeName)
                ? receiverTypeSymbol.Name
                : receiverSourceType.TypeName;

            RegisterRuntimeRequirement("NativeType");
            lines.Add($"he_cpp_type_of<{receiverCppTypeName}>(\"{sourceTypeName}\")->Name");
            runtimeTypeNameType = VariableUtil.GetVarType("string");
            return true;
        }

        static bool TryProcessPrimitiveLimitMemberAccess(
            MemberAccessExpressionSyntax memberAccess,
            List<string> lines,
            out VariableType resultType) {
            resultType = null;

            if (memberAccess.Expression is not PredefinedTypeSyntax predefinedType ||
                memberAccess.Name is not IdentifierNameSyntax identifierName) {
                return false;
            }

            if (!TryGetPrimitiveLimitLiteral(
                    predefinedType.Keyword.ValueText,
                    identifierName.Identifier.Text,
                    out string limitLiteral)) {
                return false;
            }

            lines.Add(limitLiteral);
            resultType = VariableUtil.GetVarType(predefinedType.Keyword.ValueText);
            return true;
        }

        static bool TryGetPrimitiveLimitLiteral(
            string primitiveTypeName,
            string memberName,
            out string limitLiteral) {
            limitLiteral = string.Empty;

            switch (primitiveTypeName) {
                case "float":
                    switch (memberName) {
                        case "MaxValue":
                            limitLiteral = "3.4028234663852886e38f";
                            return true;
                        case "MinValue":
                            limitLiteral = "-3.4028234663852886e38f";
                            return true;
                    }
                    break;
                case "double":
                    switch (memberName) {
                        case "MaxValue":
                            limitLiteral = "1.7976931348623157e308";
                            return true;
                        case "MinValue":
                            limitLiteral = "-1.7976931348623157e308";
                            return true;
                    }
                    break;
                case "int":
                    switch (memberName) {
                        case "MaxValue":
                            limitLiteral = "2147483647";
                            return true;
                        case "MinValue":
                            limitLiteral = "-2147483648";
                            return true;
                    }
                    break;
                case "uint":
                    switch (memberName) {
                        case "MaxValue":
                            limitLiteral = "4294967295u";
                            return true;
                        case "MinValue":
                            limitLiteral = "0u";
                            return true;
                    }
                    break;
                case "long":
                    switch (memberName) {
                        case "MaxValue":
                            limitLiteral = "9223372036854775807ll";
                            return true;
                        case "MinValue":
                            limitLiteral = "(-9223372036854775807ll - 1ll)";
                            return true;
                    }
                    break;
                case "ulong":
                    switch (memberName) {
                        case "MaxValue":
                            limitLiteral = "18446744073709551615ull";
                            return true;
                        case "MinValue":
                            limitLiteral = "0ull";
                            return true;
                    }
                    break;
                case "short":
                    switch (memberName) {
                        case "MaxValue":
                            limitLiteral = "32767";
                            return true;
                        case "MinValue":
                            limitLiteral = "-32768";
                            return true;
                    }
                    break;
                case "ushort":
                    switch (memberName) {
                        case "MaxValue":
                            limitLiteral = "65535";
                            return true;
                        case "MinValue":
                            limitLiteral = "0";
                            return true;
                    }
                    break;
                case "sbyte":
                    switch (memberName) {
                        case "MaxValue":
                            limitLiteral = "127";
                            return true;
                        case "MinValue":
                            limitLiteral = "-128";
                            return true;
                    }
                    break;
                case "byte":
                    switch (memberName) {
                        case "MaxValue":
                            limitLiteral = "255";
                            return true;
                        case "MinValue":
                            limitLiteral = "0";
                            return true;
                    }
                    break;
            }

            return false;
        }

        bool TryProcessPrimitiveNumberRuntimeMemberAccess(
            MemberAccessExpressionSyntax memberAccess,
            List<string> lines,
            out VariableType resultType) {
            resultType = null;

            if (memberAccess.Expression is not PredefinedTypeSyntax predefinedType ||
                memberAccess.Name is not IdentifierNameSyntax identifierName) {
                return false;
            }

            string predefinedTypeName = predefinedType.Keyword.ValueText;
            string memberName = identifierName.Identifier.Text;
            bool isSupportedNumberMember =
                string.Equals(predefinedTypeName, "int", StringComparison.Ordinal) &&
                string.Equals(memberName, "TryParse", StringComparison.Ordinal) ||
                (string.Equals(predefinedTypeName, "float", StringComparison.Ordinal) ||
                 string.Equals(predefinedTypeName, "double", StringComparison.Ordinal)) &&
                string.Equals(memberName, "IsPositiveInfinity", StringComparison.Ordinal);
            if (!isSupportedNumberMember) {
                return false;
            }

            RegisterRuntimeRequirement("Number");
            lines.Add("Number::");
            lines.Add(memberName);
            resultType = VariableUtil.GetVarType("object");
            return true;
        }

        /// <summary>
        /// Resolves the emitted variable type for a bound member symbol so downstream tuple inference can use the actual member result type instead of the identifier token type.
        /// </summary>
        /// <param name="memberSymbol">Resolved member symbol for the member-access expression.</param>
        /// <returns>The resolved member variable type when available; otherwise <c>null</c>.</returns>
        static VariableType ResolveMemberAccessResultType(ISymbol memberSymbol) {
            if (memberSymbol is IAliasSymbol aliasSymbol) {
                memberSymbol = aliasSymbol.Target;
            }

            if (memberSymbol is IFieldSymbol fieldSymbol) {
                return VariableUtil.GetVarType(fieldSymbol.Type);
            }

            if (memberSymbol is IPropertySymbol propertySymbol) {
                return VariableUtil.GetVarType(propertySymbol.Type);
            }

            if (memberSymbol is IMethodSymbol methodSymbol) {
                return VariableUtil.GetVarType(methodSymbol.ReturnType);
            }

            if (memberSymbol is IEventSymbol eventSymbol) {
                return VariableUtil.GetVarType(eventSymbol.Type);
            }

            return null;
        }

        /// <summary>
        /// Determines the access path that should be carried forward after a member access is emitted.
        /// </summary>
        /// <param name="usedStaticAccess">Whether the current member access emitted the static <c>::</c> operator.</param>
        /// <param name="receiverPath">Access path carried by the receiver expression.</param>
        /// <param name="memberSymbol">Resolved member symbol for the current access.</param>
        /// <returns>The access path that should be associated with the member access result.</returns>
        static VariablePath ResolveMemberAccessResultPath(bool usedStaticAccess, VariablePath receiverPath, ISymbol memberSymbol) {
            if (!usedStaticAccess) {
                return receiverPath;
            }

            if (memberSymbol is IAliasSymbol aliasSymbol) {
                memberSymbol = aliasSymbol.Target;
            }

            return memberSymbol is INamespaceSymbol or INamedTypeSymbol
                ? VariablePath.Static
                : VariablePath.Unknown;
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

            if (TryGetConstantStringValue(semantic, memberAccess.Expression, out string constantStringValue)) {
                lines.Add(constantStringValue.Length.ToString());
                return true;
            }

            if (memberAccess.Expression is LiteralExpressionSyntax literalExpression &&
                literalExpression.IsKind(SyntaxKind.StringLiteralExpression)) {
                lines.Add(literalExpression.Token.ValueText.Length.ToString());
                return true;
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

        bool TryProcessTupleMemberAccess(
            SemanticModel semantic,
            LayerContext context,
            MemberAccessExpressionSyntax memberAccess,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("object");

            if (memberAccess.Name is not IdentifierNameSyntax identifierName) {
                return false;
            }

            int tupleElementIndex = -1;
            ITypeSymbol tupleElementTypeSymbol = null;
            VariableType tupleElementVariableType = null;
            if (!TryResolveTupleMemberAccessSymbol(semantic, memberAccess, out tupleElementIndex, out tupleElementTypeSymbol) &&
                !TryResolveTupleMemberVariableType(context, memberAccess.Expression, identifierName.Identifier.Text, out tupleElementIndex, out tupleElementVariableType) &&
                !TryResolveTupleMemberTypeSymbol(semantic, memberAccess.Expression, identifierName.Identifier.Text, out tupleElementIndex, out tupleElementTypeSymbol)) {
                    return false;
            }

            string receiverText = RenderExpressionText(semantic, context, memberAccess.Expression);
            string memberAccessOperator = UsesDirectMemberAccess(semantic, memberAccess.Expression) ? "." : "->";
            lines.Add($"{receiverText}{memberAccessOperator}Item{tupleElementIndex + 1}");
            resultType = tupleElementTypeSymbol != null
                ? VariableUtil.GetVarType(tupleElementTypeSymbol)
                : tupleElementVariableType;
            return true;
        }

        /// <summary>
        /// Resolves a tuple member access directly from the Roslyn member symbol when tuple element names are preserved there but the receiver type is weakened.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the member access.</param>
        /// <param name="memberAccess">Tuple member access expression to inspect.</param>
        /// <param name="tupleElementIndex">Resolved zero-based tuple element index.</param>
        /// <param name="tupleElementTypeSymbol">Resolved tuple element type symbol.</param>
        /// <returns><c>true</c> when the member symbol maps to a tuple element; otherwise <c>false</c>.</returns>
        static bool TryResolveTupleMemberAccessSymbol(
            SemanticModel semantic,
            MemberAccessExpressionSyntax memberAccess,
            out int tupleElementIndex,
            out ITypeSymbol tupleElementTypeSymbol) {
            tupleElementIndex = -1;
            tupleElementTypeSymbol = null;

            ISymbol tupleMemberSymbol = semantic.GetSymbolInfo(memberAccess.Name).Symbol ?? semantic.GetSymbolInfo(memberAccess).Symbol;
            if (tupleMemberSymbol is IAliasSymbol aliasSymbol) {
                tupleMemberSymbol = aliasSymbol.Target;
            }

            if (tupleMemberSymbol is IFieldSymbol fieldSymbol &&
                fieldSymbol.ContainingType is INamedTypeSymbol fieldContainingType &&
                fieldContainingType.IsTupleType) {
                return TryResolveTupleElement(fieldContainingType, fieldSymbol.Name, out tupleElementIndex, out tupleElementTypeSymbol);
            }

            if (tupleMemberSymbol is IPropertySymbol propertySymbol &&
                propertySymbol.ContainingType is INamedTypeSymbol propertyContainingType &&
                propertyContainingType.IsTupleType) {
                return TryResolveTupleElement(propertyContainingType, propertySymbol.Name, out tupleElementIndex, out tupleElementTypeSymbol);
            }

            return false;
        }

        static bool TryGetConstantStringValue(SemanticModel semantic, ExpressionSyntax expression, out string value) {
            value = null;
            if (semantic == null || expression == null) {
                return false;
            }

            Optional<object> constantValue = semantic.GetConstantValue(expression);
            if (!constantValue.HasValue || constantValue.Value is not string stringValue) {
                return false;
            }

            value = stringValue;
            return true;
        }

        /// <summary>
        /// Resolves a tuple member access from the converter's tracked local-variable metadata when Roslyn no longer reports the tuple receiver strongly.
        /// </summary>
        /// <param name="context">Current lowering context.</param>
        /// <param name="receiverExpression">Tuple receiver expression.</param>
        /// <param name="memberName">Tuple member name requested by the source expression.</param>
        /// <param name="tupleElementIndex">Resolved zero-based tuple element index.</param>
        /// <param name="tupleElementVariableType">Resolved tuple element variable type.</param>
        /// <returns><c>true</c> when the tracked receiver variable exposes tuple element metadata for the requested member; otherwise <c>false</c>.</returns>
        static bool TryResolveTupleMemberVariableType(
            LayerContext context,
            ExpressionSyntax receiverExpression,
            string memberName,
            out int tupleElementIndex,
            out VariableType tupleElementVariableType) {
            tupleElementIndex = -1;
            tupleElementVariableType = null;

            if (receiverExpression is not IdentifierNameSyntax receiverIdentifier) {
                return false;
            }

            FunctionStack currentFunction = context.GetCurrentFunction();
            if (currentFunction == null) {
                return false;
            }

            ConversionVariable trackedVariable = currentFunction.Stack.LastOrDefault(candidate => candidate.Name == receiverIdentifier.Identifier.Text);
            if (trackedVariable == null && currentFunction.Function?.InParameters != null) {
                trackedVariable = currentFunction.Function.InParameters.LastOrDefault(candidate => candidate.Name == receiverIdentifier.Identifier.Text);
            }

            if (trackedVariable?.VarType?.Type != VariableDataType.Tuple) {
                return false;
            }

            for (int index = 0; index < trackedVariable.VarType.GenericArgs.Count; index++) {
                string tupleElementName = trackedVariable.VarType.Args.Count > index
                    ? trackedVariable.VarType.Args[index].TypeName
                    : string.Empty;

                if (string.Equals(tupleElementName, memberName, StringComparison.Ordinal) ||
                    string.Equals($"Item{index + 1}", memberName, StringComparison.Ordinal)) {
                    tupleElementIndex = index;
                    tupleElementVariableType = trackedVariable.VarType.GenericArgs[index];
                    return tupleElementVariableType != null;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves the tuple element index and element type for a tuple member access by inspecting the receiver type directly instead of depending on member-symbol binding.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the member access.</param>
        /// <param name="receiverExpression">Tuple receiver expression.</param>
        /// <param name="memberName">Tuple member name requested by the source expression.</param>
        /// <param name="tupleElementIndex">Resolved zero-based tuple element index.</param>
        /// <param name="tupleElementTypeSymbol">Resolved tuple element type symbol.</param>
        /// <returns><c>true</c> when the receiver resolves to a tuple and the member maps to one of its elements; otherwise <c>false</c>.</returns>
        static bool TryResolveTupleMemberTypeSymbol(
            SemanticModel semantic,
            ExpressionSyntax receiverExpression,
            string memberName,
            out int tupleElementIndex,
            out ITypeSymbol tupleElementTypeSymbol) {
            tupleElementIndex = -1;
            tupleElementTypeSymbol = null;

            if (!TryGetExpressionTypeSymbol(semantic, receiverExpression, out ITypeSymbol receiverTypeSymbol) ||
                receiverTypeSymbol is not INamedTypeSymbol namedTupleType ||
                !namedTupleType.IsTupleType) {
                return false;
            }

            return TryResolveTupleElement(namedTupleType, memberName, out tupleElementIndex, out tupleElementTypeSymbol);
        }

        /// <summary>
        /// Maps a tuple member name to its zero-based tuple element index and type.
        /// </summary>
        /// <param name="namedTupleType">Tuple type that owns the requested member.</param>
        /// <param name="memberName">Tuple member name requested by the source expression.</param>
        /// <param name="tupleElementIndex">Resolved zero-based tuple element index.</param>
        /// <param name="tupleElementTypeSymbol">Resolved tuple element type symbol.</param>
        /// <returns><c>true</c> when the member name matches a tuple element; otherwise <c>false</c>.</returns>
        static bool TryResolveTupleElement(
            INamedTypeSymbol namedTupleType,
            string memberName,
            out int tupleElementIndex,
            out ITypeSymbol tupleElementTypeSymbol) {
            tupleElementIndex = -1;
            tupleElementTypeSymbol = null;

            for (int index = 0; index < namedTupleType.TupleElements.Length; index++) {
                IFieldSymbol tupleElement = namedTupleType.TupleElements[index];
                if (string.Equals(tupleElement.Name, memberName, StringComparison.Ordinal) ||
                    string.Equals($"Item{index + 1}", memberName, StringComparison.Ordinal)) {
                    tupleElementIndex = index;
                    tupleElementTypeSymbol = tupleElement.Type;
                    return tupleElementTypeSymbol != null;
                }
            }

            return false;
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

            if (TryProcessReferenceEqualsInvocation(semantic, context, invocationExpression, lines)) {
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
            }

            if (TryProcessRuntimeGetTypeInvocation(semantic, context, invocationExpression, lines, out VariableType runtimeGetTypeType)) {
                return new ExpressionResult(true, VariablePath.Unknown, runtimeGetTypeType);
            }

            if (TryProcessEncodingInvocation(semantic, context, invocationExpression, lines, out VariableType encodingInvocationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, encodingInvocationType);
            }

            if (TryProcessSha256Invocation(semantic, context, invocationExpression, lines, out VariableType sha256InvocationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, sha256InvocationType);
            }

            if (TryProcessNativeArrayInvocation(semantic, context, invocationExpression, lines, out VariableType nativeArrayType)) {
                return new ExpressionResult(true, VariablePath.Unknown, nativeArrayType);
            }

            if (TryProcessNativeStringInvocation(semantic, context, invocationExpression, lines, out VariableType nativeStringType)) {
                return new ExpressionResult(true, VariablePath.Unknown, nativeStringType);
            }

            if (TryProcessDirectoryInvocation(semantic, context, invocationExpression, lines, out VariableType directoryInvocationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, directoryInvocationType);
            }

            if (TryProcessPrimitiveGetHashCodeInvocation(semantic, context, invocationExpression, lines, out VariableType primitiveGetHashCodeType)) {
                return new ExpressionResult(true, VariablePath.Unknown, primitiveGetHashCodeType);
            }

            if (TryProcessNativeToStringInvocation(semantic, context, invocationExpression, lines, out VariableType nativeToStringType)) {
                return new ExpressionResult(true, VariablePath.Unknown, nativeToStringType);
            }

            if (TryProcessEventInvocation(semantic, context, invocationExpression, lines)) {
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("void"));
            }

            if (TryProcessDelegateInvocation(semantic, context, invocationExpression, lines, out VariableType delegateInvocationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, delegateInvocationType);
            }

            List<string> argLines = ["("];
            int count = 0;
            List<ExpressionResult> types = new List<ExpressionResult>();
            IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameterSymbols = invokedMethodSymbol != null
                ? invokedMethodSymbol.Parameters
                : System.Collections.Immutable.ImmutableArray<IParameterSymbol>.Empty;

            List<string> beforeLines = new List<string>();

            foreach (var arg in invocationExpression.ArgumentList.Arguments) {
                string refKeyword = arg.RefKindKeyword.ToString();
                bool isRef = false;
                if (refKeyword != "") {
                    isRef = true;
                }

                List<string> argumentExpressionLines = new List<string>();
                int startArg = context.DepthClass;
                ExpressionResult argumentResult = ProcessExpression(semantic, context, arg.Expression, argumentExpressionLines);
                context.PopClass(startArg);
                types.Add(argumentResult);

                IParameterSymbol parameterSymbol = count < parameterSymbols.Length ? parameterSymbols[count] : null;
                AppendInvocationArgument(
                    semantic,
                    context,
                    arg.Expression,
                    argumentExpressionLines,
                    parameterSymbol,
                    argLines);

                if (isRef) {
                    AddRefOrOutDeclarationBeforeLines(semantic, context, arg.Expression, parameterSymbol, beforeLines);
                }

                count++;
                if (count != invocationExpression.ArgumentList.Arguments.Count ||
                    (parameterSymbols.Length > invocationExpression.ArgumentList.Arguments.Count &&
                     parameterSymbols.Skip(invocationExpression.ArgumentList.Arguments.Count).Any(parameter => parameter.HasExplicitDefaultValue))) {
                    argLines.Add(", ");
                }
            }

            AppendOptionalInvocationArguments(parameterSymbols, invocationExpression.ArgumentList.Arguments.Count, argLines);
            argLines.Add(")");

            int start = context.DepthClass;
            ExpressionResult result = ProcessExpression(semantic, context, invocationExpression.Expression, lines, types);
            context.PopClass(start);

            if (invokedMethodSymbol != null) {
                AppendResolvedInvocationTypeArgumentsIfNeeded(invocationExpression, invokedMethodSymbol, context, lines);
            }

            lines.AddRange(argLines);

            result.BeforeLines = beforeLines;
            return result;
        }

        bool TryProcessReferenceEqualsInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines) {
            bool isReferenceEquals =
                invocationExpression.Expression is IdentifierNameSyntax identifierName &&
                string.Equals(identifierName.Identifier.Text, "ReferenceEquals", StringComparison.Ordinal);

            if (!isReferenceEquals &&
                invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess) {
                isReferenceEquals = string.Equals(memberAccess.Name.Identifier.Text, "ReferenceEquals", StringComparison.Ordinal);
            }

            if (!isReferenceEquals || invocationExpression.ArgumentList.Arguments.Count != 2) {
                return false;
            }

            lines.Add("(");
            ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0].Expression, lines);
            lines.Add(" == ");
            ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[1].Expression, lines);
            lines.Add(")");
            return true;
        }

        bool TryProcessRuntimeGetTypeInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType runtimeGetTypeType) {
            runtimeGetTypeType = VariableUtil.GetVarType("object");

            if (invocationExpression.ArgumentList.Arguments.Count != 0 ||
                invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                !string.Equals(memberAccess.Name.Identifier.Text, "GetType", StringComparison.Ordinal)) {
                return false;
            }

            TypeInfo invocationTypeInfo = semantic.GetTypeInfo(invocationExpression);
            ITypeSymbol invocationTypeSymbol = invocationTypeInfo.ConvertedType ?? invocationTypeInfo.Type;
            if (invocationTypeSymbol != null &&
                !string.Equals(invocationTypeSymbol.Name, "Type", StringComparison.Ordinal)) {
                return false;
            }

            ITypeSymbol receiverTypeSymbol = semantic.GetTypeInfo(memberAccess.Expression).ConvertedType ?? semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (receiverTypeSymbol == null) {
                return false;
            }

            VariableType receiverSourceType = VariableUtil.GetVarType(receiverTypeSymbol);
            CPPTypeData receiverTypeData;
            VariableType receiverCppType = ConvertToCPPType(receiverSourceType, out receiverTypeData);
            string receiverCppTypeName = receiverCppType.ToCPPString(context.Program);
            string sourceTypeName = string.IsNullOrWhiteSpace(receiverSourceType.TypeName)
                ? receiverTypeSymbol.Name
                : receiverSourceType.TypeName;

            RegisterRuntimeRequirement("NativeType");
            lines.Add($"he_cpp_type_of<{receiverCppTypeName}>(\"{sourceTypeName}\")");
            runtimeGetTypeType = VariableUtil.GetVarType("Type");
            return true;
        }

        bool TryProcessEncodingInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = null;

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Expression is not MemberAccessExpressionSyntax encodingMemberAccess ||
                encodingMemberAccess.Name is not IdentifierNameSyntax encodingInstanceIdentifier ||
                memberAccess.Name is not IdentifierNameSyntax encodingMethodIdentifier ||
                !string.Equals(encodingInstanceIdentifier.Identifier.Text, "UTF8", StringComparison.Ordinal)) {
                return false;
            }

            string encodingReceiverText = encodingMemberAccess.Expression.ToString();
            if (!string.Equals(encodingReceiverText, "Encoding", StringComparison.Ordinal) &&
                !string.Equals(encodingReceiverText, "System.Text.Encoding", StringComparison.Ordinal) &&
                !string.Equals(encodingReceiverText, "global::System.Text.Encoding", StringComparison.Ordinal)) {
                return false;
            }

            string methodName = encodingMethodIdentifier.Identifier.Text;
            if (!string.Equals(methodName, "GetBytes", StringComparison.Ordinal) &&
                !string.Equals(methodName, "GetString", StringComparison.Ordinal)) {
                return false;
            }

            RegisterRuntimeRequirement("Encoding");
            lines.Add("Encoding::");
            lines.Add(methodName);
            lines.Add("(Encoding::UTF8");
            if (invocationExpression.ArgumentList.Arguments.Count > 0) {
                lines.Add(", ");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
            }

            lines.Add(")");
            if (TryGetExpressionTypeSymbol(semantic, invocationExpression, out ITypeSymbol resultTypeSymbol)) {
                resultType = VariableUtil.GetVarType(resultTypeSymbol);
            } else {
                resultType = string.Equals(methodName, "GetBytes", StringComparison.Ordinal)
                    ? VariableUtil.GetVarType("byte[]")
                    : VariableUtil.GetVarType("string");
            }

            return true;
        }

        static bool TryProcessStaticFileIoMemberAccess(
            MemberAccessExpressionSyntax memberAccess,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("object");

            if (memberAccess.Expression is not IdentifierNameSyntax receiverIdentifier ||
                memberAccess.Name is not IdentifierNameSyntax memberIdentifier) {
                return false;
            }

            string receiverName = receiverIdentifier.Identifier.Text;
            if (!string.Equals(receiverName, "FileMode", StringComparison.Ordinal) &&
                !string.Equals(receiverName, "FileAccess", StringComparison.Ordinal) &&
                !string.Equals(receiverName, "FileShare", StringComparison.Ordinal)) {
                return false;
            }

            lines.Add(receiverName);
            lines.Add("::");
            lines.Add(memberIdentifier.Identifier.Text);
            return true;
        }

        void AppendInvocationArgument(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax argumentExpression,
            List<string> argumentExpressionLines,
            IParameterSymbol parameterSymbol,
            List<string> argumentLines) {
            if (parameterSymbol != null &&
                argumentExpression is CollectionExpressionSyntax collectionExpression &&
                TryAppendTargetTypedCollectionExpressionArgument(semantic, context, collectionExpression, parameterSymbol, argumentLines)) {
                return;
            }

            if (parameterSymbol != null &&
                TryAppendTargetTypedArrayInitializerArgument(semantic, context, argumentExpression, parameterSymbol, argumentLines)) {
                return;
            }

            if (parameterSymbol != null &&
                TryAppendArrayAsListInvocationArgument(semantic, context, argumentExpression, parameterSymbol, argumentExpressionLines, argumentLines)) {
                return;
            }

            IMethodSymbol methodGroupSymbol = ResolveMethodSymbol(semantic.GetSymbolInfo(argumentExpression));
            if (parameterSymbol != null &&
                methodGroupSymbol != null &&
                TryGetDelegateWrapperTypeName(parameterSymbol.Type, methodGroupSymbol, context, out string delegateWrapperTypeName)) {
                argumentLines.Add($"new {delegateWrapperTypeName}(");
                argumentLines.Add(string.Concat(argumentExpressionLines));
                argumentLines.Add(")");
                return;
            }

            argumentLines.AddRange(argumentExpressionLines);
        }

        bool TryAppendArrayAsListInvocationArgument(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax argumentExpression,
            IParameterSymbol parameterSymbol,
            List<string> argumentExpressionLines,
            List<string> argumentLines) {
            if (!IsListFamilyTypeSymbol(parameterSymbol.Type) ||
                !TryResolveArrayElementTypeSymbol(semantic, argumentExpression, out ITypeSymbol arrayElementTypeSymbol)) {
                return false;
            }

            RegisterRuntimeRequirement("NativeList");
            VariableType elementType = VariableUtil.GetVarType(arrayElementTypeSymbol);
            string elementTypeName = GetCppTypeToken(elementType, context.Program);
            argumentLines.Add($"new List<{elementTypeName}>(");
            argumentLines.Add(string.Concat(argumentExpressionLines));
            argumentLines.Add(")");
            return true;
        }

        bool TryResolveArrayElementTypeSymbol(
            SemanticModel semantic,
            ExpressionSyntax expression,
            out ITypeSymbol elementTypeSymbol) {
            elementTypeSymbol = null;

            TypeInfo expressionTypeInfo = semantic.GetTypeInfo(expression);
            if (expressionTypeInfo.Type is IArrayTypeSymbol naturalArrayTypeSymbol) {
                elementTypeSymbol = naturalArrayTypeSymbol.ElementType;
                return elementTypeSymbol != null;
            }

            if (TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol expressionTypeSymbol) &&
                expressionTypeSymbol is IArrayTypeSymbol arrayTypeSymbol) {
                elementTypeSymbol = arrayTypeSymbol.ElementType;
                return elementTypeSymbol != null;
            }

            if (expression is InvocationExpressionSyntax invocationExpression) {
                IMethodSymbol methodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
                if (methodSymbol != null &&
                    methodSymbol.IsStatic &&
                    string.Equals(methodSymbol.Name, "Empty", StringComparison.Ordinal) &&
                    methodSymbol.TypeArguments.Length == 1 &&
                    string.Equals(methodSymbol.ContainingType?.Name, "Array", StringComparison.Ordinal)) {
                    elementTypeSymbol = methodSymbol.TypeArguments[0];
                    return elementTypeSymbol != null;
                }

                if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess &&
                    string.Equals(memberAccess.Expression.ToString(), "Array", StringComparison.Ordinal) &&
                    memberAccess.Name is GenericNameSyntax genericName &&
                    string.Equals(genericName.Identifier.Text, "Empty", StringComparison.Ordinal) &&
                    genericName.TypeArgumentList.Arguments.Count == 1) {
                    elementTypeSymbol = semantic.GetTypeInfo(genericName.TypeArgumentList.Arguments[0]).Type;
                    return elementTypeSymbol != null;
                }
            }

            return false;
        }

        void AppendResolvedInvocationTypeArgumentsIfNeeded(
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            LayerContext context,
            List<string> lines) {
            if (!invokedMethodSymbol.IsGenericMethod ||
                invokedMethodSymbol.TypeArguments.Length == 0 ||
                InvocationHasExplicitTypeArguments(invocationExpression) ||
                InvocationUsesUnresolvedMethodTypeParameters(invokedMethodSymbol)) {
                return;
            }

            lines.Add("<");
            for (int index = 0; index < invokedMethodSymbol.TypeArguments.Length; index++) {
                lines.Add(GetCppTypeToken(VariableUtil.GetVarType(invokedMethodSymbol.TypeArguments[index]), context.Program));
                if (index < invokedMethodSymbol.TypeArguments.Length - 1) {
                    lines.Add(", ");
                }
            }

            lines.Add(">");
        }

        static bool InvocationHasExplicitTypeArguments(InvocationExpressionSyntax invocationExpression) {
            return invocationExpression.Expression is GenericNameSyntax ||
                invocationExpression.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax } ||
                invocationExpression.Expression is MemberBindingExpressionSyntax { Name: GenericNameSyntax };
        }

        static bool InvocationUsesUnresolvedMethodTypeParameters(IMethodSymbol invokedMethodSymbol) {
            if (invokedMethodSymbol.TypeArguments.Length != invokedMethodSymbol.TypeParameters.Length) {
                return false;
            }

            for (int index = 0; index < invokedMethodSymbol.TypeArguments.Length; index++) {
                if (invokedMethodSymbol.TypeArguments[index] is not ITypeParameterSymbol typeParameterSymbol ||
                    !SymbolEqualityComparer.Default.Equals(typeParameterSymbol, invokedMethodSymbol.TypeParameters[index])) {
                    return false;
                }
            }

            return true;
        }

        bool TryAppendTargetTypedCollectionExpressionArgument(
            SemanticModel semantic,
            LayerContext context,
            CollectionExpressionSyntax collectionExpression,
            IParameterSymbol parameterSymbol,
            List<string> argumentLines) {
            if (!IsListFamilyTypeSymbol(parameterSymbol.Type)) {
                return false;
            }

            VariableType targetType = VariableUtil.GetVarType(parameterSymbol.Type);
            CPPTypeData targetTypeData;
            VariableType cppTargetType = ConvertToCPPType(targetType, out targetTypeData);

            argumentLines.Add("new ");
            argumentLines.Add(cppTargetType.ToCPPString(context.Program));
            argumentLines.Add("(");
            AppendCollectionExpressionInitializerList(semantic, context, collectionExpression, argumentLines);
            argumentLines.Add(")");
            return true;
        }

        bool TryAppendTargetTypedArrayInitializerArgument(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax argumentExpression,
            IParameterSymbol parameterSymbol,
            List<string> argumentLines) {
            if (!IsListFamilyTypeSymbol(parameterSymbol.Type)) {
                return false;
            }

            SeparatedSyntaxList<ExpressionSyntax> expressions;
            if (argumentExpression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation) {
                expressions = implicitArrayCreation.Initializer.Expressions;
            } else if (argumentExpression is ArrayCreationExpressionSyntax arrayCreationExpression &&
                arrayCreationExpression.Initializer != null) {
                expressions = arrayCreationExpression.Initializer.Expressions;
            } else {
                return false;
            }

            VariableType targetType = VariableUtil.GetVarType(parameterSymbol.Type);
            CPPTypeData targetTypeData;
            VariableType cppTargetType = ConvertToCPPType(targetType, out targetTypeData);

            argumentLines.Add("new ");
            argumentLines.Add(cppTargetType.ToCPPString(context.Program));
            argumentLines.Add("({ ");
            AppendExpressionList(semantic, context, expressions, argumentLines);
            argumentLines.Add(" })");
            return true;
        }

        void AppendOptionalInvocationArguments(
            System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameterSymbols,
            int explicitArgumentCount,
            List<string> argumentLines) {
            for (int index = explicitArgumentCount; index < parameterSymbols.Length; index++) {
                IParameterSymbol parameterSymbol = parameterSymbols[index];
                if (!parameterSymbol.HasExplicitDefaultValue) {
                    continue;
                }

                AppendOptionalArgumentDefaultValue(parameterSymbol, argumentLines);
                if (index < parameterSymbols.Length - 1 &&
                    parameterSymbols.Skip(index + 1).Any(parameter => parameter.HasExplicitDefaultValue)) {
                    argumentLines.Add(", ");
                }
            }
        }

        void AppendOptionalArgumentDefaultValue(IParameterSymbol parameterSymbol, List<string> argumentLines) {
            object explicitDefaultValue = parameterSymbol.ExplicitDefaultValue;
            if (explicitDefaultValue == null) {
                argumentLines.Add("nullptr");
                return;
            }

            if (explicitDefaultValue is bool boolValue) {
                argumentLines.Add(boolValue ? "true" : "false");
                return;
            }

            if (explicitDefaultValue is string stringValue) {
                argumentLines.Add(SymbolDisplay.FormatLiteral(stringValue, true));
                return;
            }

            if (parameterSymbol.Type?.TypeKind == TypeKind.Enum) {
                argumentLines.Add($"{parameterSymbol.Type.Name}::{explicitDefaultValue}");
                return;
            }

            argumentLines.Add(Convert.ToString(explicitDefaultValue, System.Globalization.CultureInfo.InvariantCulture));
        }

        bool TryProcessNativeArrayInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("object");

            IMethodSymbol methodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            VariableType elementType;
            if (methodSymbol != null &&
                methodSymbol.IsStatic &&
                string.Equals(methodSymbol.Name, "Empty", StringComparison.Ordinal) &&
                methodSymbol.TypeArguments.Length == 1 &&
                string.Equals(methodSymbol.ContainingType?.Name, "Array", StringComparison.Ordinal)) {
                elementType = VariableUtil.GetVarType(methodSymbol.TypeArguments[0]);
            } else if (methodSymbol != null &&
                methodSymbol.IsStatic &&
                string.Equals(methodSymbol.Name, "Copy", StringComparison.Ordinal) &&
                string.Equals(methodSymbol.ContainingType?.Name, "Array", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 3 &&
                TryResolveArrayElementTypeSymbol(semantic, invocationExpression.ArgumentList.Arguments[0].Expression, out ITypeSymbol copyElementTypeSymbol)) {
                codeConverter?.RegisterRuntimeRequirement("NativeArray");
                string copyElementTypeName = GetCppTypeToken(VariableUtil.GetVarType(copyElementTypeSymbol), context.Program);
                lines.Add($"Array<{copyElementTypeName}>::Copy(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("void");
                return true;
            } else if (invocationExpression.Expression is MemberAccessExpressionSyntax copyMemberAccess &&
                string.Equals(copyMemberAccess.Expression.ToString(), "Array", StringComparison.Ordinal) &&
                copyMemberAccess.Name is IdentifierNameSyntax copyIdentifierName &&
                string.Equals(copyIdentifierName.Identifier.Text, "Copy", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 3 &&
                TryResolveArrayElementTypeSymbol(semantic, invocationExpression.ArgumentList.Arguments[0].Expression, out ITypeSymbol copySyntaxElementTypeSymbol)) {
                codeConverter?.RegisterRuntimeRequirement("NativeArray");
                string copyElementTypeName = GetCppTypeToken(VariableUtil.GetVarType(copySyntaxElementTypeSymbol), context.Program);
                lines.Add($"Array<{copyElementTypeName}>::Copy(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("void");
                return true;
            } else if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess &&
                string.Equals(memberAccess.Expression.ToString(), "Array", StringComparison.Ordinal) &&
                memberAccess.Name is GenericNameSyntax genericName &&
                string.Equals(genericName.Identifier.Text, "Empty", StringComparison.Ordinal) &&
                genericName.TypeArgumentList.Arguments.Count == 1) {
                elementType = VariableUtil.GetVarType(genericName.TypeArgumentList.Arguments[0], semantic);
            } else {
                return false;
            }

            codeConverter?.RegisterRuntimeRequirement("NativeArray");
            string elementTypeName = GetCppTypeToken(elementType, context.Program);

            lines.Add($"Array<{elementTypeName}>::Empty()");

            VariableType arrayType = new VariableType(VariableDataType.Array, "Array");
            arrayType.GenericArgs.Add(elementType);
            resultType = arrayType;
            return true;
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
            IParameterSymbol parameterSymbol,
            List<string> beforeLines) {
            if (expression is not DeclarationExpressionSyntax declarationExpression ||
                declarationExpression.Designation is not SingleVariableDesignationSyntax singleVariableDesignation) {
                return;
            }

            ITypeSymbol inferredOutTypeSymbol = TryResolveOutArgumentTypeSymbol(semantic, expression, parameterSymbol);

            VariableType variableType = declarationExpression.Type is IdentifierNameSyntax declarationIdentifier &&
                string.Equals(declarationIdentifier.Identifier.Text, "var", StringComparison.Ordinal) &&
                inferredOutTypeSymbol != null
                ? VariableUtil.GetVarType(inferredOutTypeSymbol)
                : ResolveDeclarationExpressionVariableType(semantic, declarationExpression, singleVariableDesignation);
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(variableType, out typeData);
            string pointerSuffix = typeData.IsPointer ? "*" : string.Empty;
            RegisterGeneratedTypeReferences(context, variableType);
            string typeName = QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context);

            beforeLines.Add($"{typeName}{pointerSuffix} {singleVariableDesignation.Identifier.Text};\n");
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
            bool isInstanceStringCall = !isStaticStringCall && IsStringLikeExpression(semantic, memberAccess.Expression);
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

            if (isStaticStringCall &&
                string.Equals(memberName, "Concat", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count > 0) {
                RegisterRuntimeRequirement("NativeString");
                lines.Add("String::Concat(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("string");
                return true;
            }

            if (isStaticStringCall &&
                string.Equals(memberName, "Join", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count >= 2) {
                RegisterRuntimeRequirement("NativeString");
                bool usesCollectionJoinArray = invocationExpression.ArgumentList.Arguments.Count == 2 &&
                    TryGetExpressionTypeSymbol(semantic, invocationExpression.ArgumentList.Arguments[1].Expression, out ITypeSymbol joinValuesTypeSymbol) &&
                    IsListFamilyTypeSymbol(joinValuesTypeSymbol);
                lines.Add(usesCollectionJoinArray ? "String::JoinArray(" : "String::Join(");

                ArgumentSyntax separatorArgument = invocationExpression.ArgumentList.Arguments[0];
                int separatorStart = context.DepthClass;
                ProcessExpression(semantic, context, separatorArgument.Expression, lines);
                context.PopClass(separatorStart);

                lines.Add(", ");
                if (usesCollectionJoinArray) {
                    int collectionStart = context.DepthClass;
                    ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[1].Expression, lines);
                    context.PopClass(collectionStart);
                    lines.Add("->ToArray()");
                } else {
                    AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments.Skip(1), lines);
                }

                lines.Add(")");
                resultType = VariableUtil.GetVarType("string");
                return true;
            }

            if (isStaticStringCall &&
                string.Equals(memberName, "IsDigit", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 1) {
                RegisterRuntimeRequirement("NativeString");
                lines.Add("String::IsDigit(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("bool");
                return true;
            }

            if (isStaticStringCall &&
                (string.Equals(memberName, "ToUpper", StringComparison.Ordinal) ||
                 string.Equals(memberName, "ToUpperInvariant", StringComparison.Ordinal)) &&
                invocationExpression.ArgumentList.Arguments.Count == 1) {
                RegisterRuntimeRequirement("NativeString");
                lines.Add("String::ToUpper(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("char");
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

            if (string.Equals(memberName, "Trim", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 0) {
                lines.Add("String::Trim(");
                lines.Add(receiverText);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("string");
                return true;
            }

            if (string.Equals(memberName, "TrimStart", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 0) {
                lines.Add("String::TrimStart(");
                lines.Add(receiverText);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("string");
                return true;
            }

            if (string.Equals(memberName, "Substring", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count is 1 or 2) {
                lines.Add("String::Substring(");
                lines.Add(receiverText);
                lines.Add(", ");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("string");
                return true;
            }

            if (string.Equals(memberName, "Remove", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count is 1 or 2) {
                lines.Add("String::Remove(");
                lines.Add(receiverText);
                lines.Add(", ");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("string");
                return true;
            }

            if (string.Equals(memberName, "Insert", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 2) {
                lines.Add("String::Insert(");
                lines.Add(receiverText);
                lines.Add(", ");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("string");
                return true;
            }

            if (string.Equals(memberName, "Split", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 3) {
                lines.Add("String::Split(");
                lines.Add(receiverText);
                lines.Add(", ");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("string[]");
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

        bool TryProcessDirectoryInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("object");

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name is not IdentifierNameSyntax identifierName ||
                !string.Equals(memberAccess.Expression.ToString(), "Directory", StringComparison.Ordinal)) {
                return false;
            }

            if (string.Equals(identifierName.Identifier.Text, "Exists", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 1) {
                RegisterRuntimeRequirement("Directory");
                lines.Add("Directory::Exists(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("bool");
                return true;
            }

            if (string.Equals(identifierName.Identifier.Text, "CreateDirectory", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 1) {
                RegisterRuntimeRequirement("Directory");
                lines.Add("Directory::CreateDirectory(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("void");
                return true;
            }

            return false;
        }

        bool TryProcessDelegateInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("void");

            bool isDelegateInvocation = TryResolveDelegateTypeSymbol(semantic, invocationExpression.Expression, out ITypeSymbol delegateTypeSymbol);
            if (!isDelegateInvocation &&
                (!TryResolveTrackedExpressionVariableType(context, invocationExpression.Expression, out VariableType trackedDelegateType) ||
                 !IsDelegateWrapperVariableType(context, trackedDelegateType))) {
                return false;
            }

            IMethodSymbol invokeMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (invokeMethodSymbol?.ReturnType != null &&
                !IsWeakRecoveredTypeSymbol(invokeMethodSymbol.ReturnType)) {
                resultType = VariableUtil.GetVarType(invokeMethodSymbol.ReturnType);
            }

            string delegateText = RenderExpressionText(semantic, context, invocationExpression.Expression);
            lines.Add("(*");
            lines.Add(delegateText);
            lines.Add(")(");
            AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
            lines.Add(")");
            return true;
        }

        static bool TryResolveDelegateTypeSymbol(
            SemanticModel semantic,
            ExpressionSyntax expression,
            out ITypeSymbol delegateTypeSymbol) {
            delegateTypeSymbol = null;

            if (TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol expressionTypeSymbol) &&
                expressionTypeSymbol?.TypeKind == TypeKind.Delegate) {
                delegateTypeSymbol = expressionTypeSymbol;
                return true;
            }

            foreach (ISymbol symbol in EnumerateResolvedSymbols(semantic, expression)) {
                switch (symbol) {
                    case ILocalSymbol localSymbol when localSymbol.Type?.TypeKind == TypeKind.Delegate:
                        delegateTypeSymbol = localSymbol.Type;
                        return true;
                    case IParameterSymbol parameterSymbol when parameterSymbol.Type?.TypeKind == TypeKind.Delegate:
                        delegateTypeSymbol = parameterSymbol.Type;
                        return true;
                    case IFieldSymbol fieldSymbol when fieldSymbol.Type?.TypeKind == TypeKind.Delegate:
                        delegateTypeSymbol = fieldSymbol.Type;
                        return true;
                    case IPropertySymbol propertySymbol when propertySymbol.Type?.TypeKind == TypeKind.Delegate:
                        delegateTypeSymbol = propertySymbol.Type;
                        return true;
                }
            }

            return false;
        }

        bool IsDelegateWrapperVariableType(LayerContext context, VariableType variableType) {
            if (variableType == null) {
                return false;
            }

            string typeName = variableType.TypeName ?? string.Empty;
            if (typeName.Contains("Func", StringComparison.Ordinal) ||
                typeName.Contains("Action", StringComparison.Ordinal)) {
                return true;
            }

            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(variableType, out typeData);
            string cppTypeName = cppType.ToCPPString(context.Program);
            return cppTypeName.StartsWith("Func<", StringComparison.Ordinal) ||
                cppTypeName.StartsWith("Action<", StringComparison.Ordinal) ||
                cppTypeName.Contains("::Func<", StringComparison.Ordinal) ||
                cppTypeName.Contains("::Action<", StringComparison.Ordinal);
        }

        bool TryProcessPrimitiveGetHashCodeInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("int");

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name is not IdentifierNameSyntax identifierName ||
                !string.Equals(identifierName.Identifier.Text, "GetHashCode", StringComparison.Ordinal) ||
                invocationExpression.ArgumentList.Arguments.Count != 0) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, memberAccess.Expression, out ITypeSymbol receiverTypeSymbol) ||
                receiverTypeSymbol == null) {
                IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
                receiverTypeSymbol = invokedMethodSymbol?.ReceiverType;
            }

            if (receiverTypeSymbol == null) {
                return false;
            }

            string receiverText = RenderExpressionText(semantic, context, memberAccess.Expression);
            if (receiverTypeSymbol.TypeKind == TypeKind.Enum) {
                lines.Add($"static_cast<int32_t>({receiverText})");
                return true;
            }

            if (!IsPrimitiveGetHashCodeReceiverType(receiverTypeSymbol)) {
                return false;
            }

            RegisterRuntimeRequirement("Number");
            lines.Add("Number::GetHashCode(");
            lines.Add(receiverText);
            lines.Add(")");
            return true;
        }

        static bool IsPrimitiveGetHashCodeReceiverType(ITypeSymbol receiverTypeSymbol) {
            return receiverTypeSymbol.SpecialType is
                SpecialType.System_Boolean or
                SpecialType.System_Char or
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Single or
                SpecialType.System_Double;
        }

        bool TryProcessSha256Invocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = null;

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name is not IdentifierNameSyntax memberIdentifier ||
                !string.Equals(memberIdentifier.Identifier.Text, "HashData", StringComparison.Ordinal)) {
                return false;
            }

            string receiverText = memberAccess.Expression.ToString();
            if (!string.Equals(receiverText, "System.Security.Cryptography.SHA256", StringComparison.Ordinal) &&
                !string.Equals(receiverText, "SHA256", StringComparison.Ordinal)) {
                return false;
            }

            RegisterRuntimeRequirement("SHA256");
            lines.Add("SHA256::HashData(");
            AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
            lines.Add(")");
            resultType = VariableUtil.GetVarType("byte[]");
            return true;
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

            if (TryResolvePathSeparatorToStringInvocation(memberAccess.Expression, out string pathSeparatorMemberName)) {
                lines.Add($"std::string(1, Path::{pathSeparatorMemberName})");
                return true;
            }

            if (memberAccess.Expression is ObjectCreationExpressionSyntax guidObjectCreation &&
                IsGuidObjectCreation(guidObjectCreation, semantic)) {
                lines.Add("std::string(\"00000000-0000-0000-0000-000000000000\")");
                return true;
            }

            if (TryResolveStaticPathSeparatorMemberName(semantic, memberAccess.Expression, out string staticPathSeparatorMemberName)) {
                lines.Add($"std::string(1, Path::{staticPathSeparatorMemberName})");
                return true;
            }

            IMethodSymbol toStringMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);

            List<string> receiverLines = new List<string>();
            int start = context.DepthClass;
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(start);

            string receiverText = string.Concat(receiverLines);
            if (IsStringLikeExpression(semantic, memberAccess.Expression)) {
                lines.Add(receiverText);
                return true;
            }

            if (receiverText.StartsWith("String::", StringComparison.Ordinal) ||
                receiverText.StartsWith("std::string", StringComparison.Ordinal)) {
                lines.Add(receiverText);
                return true;
            }

            ITypeSymbol receiverTypeSymbol = ResolveNativeToStringReceiverType(semantic, memberAccess.Expression, toStringMethodSymbol);
            if (receiverTypeSymbol?.SpecialType == SpecialType.System_Char) {
                lines.Add($"std::string(1, {receiverText})");
                return true;
            }

            if (receiverTypeSymbol?.TypeKind == TypeKind.Enum) {
                lines.Add($"std::to_string(static_cast<int32_t>({receiverText}))");
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

        static bool TryResolvePathSeparatorToStringInvocation(ExpressionSyntax expression, out string memberName) {
            memberName = string.Empty;

            if (expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Expression is not IdentifierNameSyntax identifierName ||
                memberAccess.Name is not IdentifierNameSyntax memberIdentifier) {
                return false;
            }

            if (!string.Equals(identifierName.Identifier.Text, "Path", StringComparison.Ordinal)) {
                return false;
            }

            if (!string.Equals(memberIdentifier.Identifier.Text, "DirectorySeparatorChar", StringComparison.Ordinal) &&
                !string.Equals(memberIdentifier.Identifier.Text, "AltDirectorySeparatorChar", StringComparison.Ordinal)) {
                return false;
            }

            memberName = memberIdentifier.Identifier.Text;
            return true;
        }

        static bool TryResolveStaticPathSeparatorMemberName(
            SemanticModel semantic,
            ExpressionSyntax expression,
            out string memberName) {
            memberName = string.Empty;

            if (expression is not MemberAccessExpressionSyntax memberAccess) {
                return false;
            }

            if (memberAccess.Expression is IdentifierNameSyntax identifierName &&
                string.Equals(identifierName.Identifier.Text, "Path", StringComparison.Ordinal) &&
                memberAccess.Name is IdentifierNameSyntax memberIdentifier &&
                (string.Equals(memberIdentifier.Identifier.Text, "DirectorySeparatorChar", StringComparison.Ordinal) ||
                 string.Equals(memberIdentifier.Identifier.Text, "AltDirectorySeparatorChar", StringComparison.Ordinal))) {
                memberName = memberIdentifier.Identifier.Text;
                return true;
            }

            ISymbol symbol = semantic.GetSymbolInfo(memberAccess).Symbol ?? semantic.GetSymbolInfo(memberAccess.Name).Symbol;
            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            if (symbol is not IPropertySymbol propertySymbol ||
                propertySymbol.ContainingType?.ToDisplayString() != "System.IO.Path") {
                return false;
            }

            if (!string.Equals(propertySymbol.Name, "DirectorySeparatorChar", StringComparison.Ordinal) &&
                !string.Equals(propertySymbol.Name, "AltDirectorySeparatorChar", StringComparison.Ordinal)) {
                return false;
            }

            memberName = propertySymbol.Name;
            return true;
        }

        static bool IsGuidObjectCreation(ObjectCreationExpressionSyntax objectCreation, SemanticModel semantic) {
            if (objectCreation == null) {
                return false;
            }

            string typeName = objectCreation.Type.ToString();
            if (string.Equals(typeName, "Guid", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Guid", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Guid", StringComparison.Ordinal)) {
                return true;
            }

            ITypeSymbol typeSymbol = semantic.GetTypeInfo(objectCreation.Type).Type ?? semantic.GetTypeInfo(objectCreation.Type).ConvertedType;
            return string.Equals(typeSymbol?.ToDisplayString(), "System.Guid", StringComparison.Ordinal);
        }

        IMethodSymbol ResolveInvokedMethodSymbol(SemanticModel semantic, InvocationExpressionSyntax invocationExpression) {
            int argumentCount = invocationExpression.ArgumentList.Arguments.Count;

            if (semantic.GetOperation(invocationExpression) is IInvocationOperation invocationOperation) {
                if (CanMethodMatchInvocationArguments(invocationOperation.TargetMethod, argumentCount)) {
                    return invocationOperation.TargetMethod;
                }
            }

            SymbolInfo invocationSymbolInfo = semantic.GetSymbolInfo(invocationExpression);
            IMethodSymbol methodSymbol = ResolveMethodSymbol(invocationSymbolInfo);
            if (CanMethodMatchInvocationArguments(methodSymbol, argumentCount)) {
                return methodSymbol;
            }

            IMethodSymbol candidateMethodSymbol = ResolveBestInvocationCandidateMethodSymbol(invocationSymbolInfo, argumentCount);
            if (candidateMethodSymbol != null) {
                return candidateMethodSymbol;
            }

            SymbolInfo expressionSymbolInfo = semantic.GetSymbolInfo(invocationExpression.Expression);
            methodSymbol = ResolveMethodSymbol(expressionSymbolInfo);
            if (CanMethodMatchInvocationArguments(methodSymbol, argumentCount)) {
                return methodSymbol;
            }

            return ResolveBestInvocationCandidateMethodSymbol(expressionSymbolInfo, argumentCount);
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

        static IMethodSymbol ResolveBestInvocationCandidateMethodSymbol(SymbolInfo symbolInfo, int argumentCount) {
            return symbolInfo.CandidateSymbols
                .OfType<IMethodSymbol>()
                .Where(candidateMethodSymbol => CanMethodMatchInvocationArguments(candidateMethodSymbol, argumentCount))
                .OrderByDescending(candidateMethodSymbol => candidateMethodSymbol.Parameters.Length == argumentCount)
                .ThenByDescending(candidateMethodSymbol => candidateMethodSymbol.Parameters.Length)
                .ThenByDescending(candidateMethodSymbol => candidateMethodSymbol.IsGenericMethod)
                .FirstOrDefault();
        }

        static bool CanMethodMatchInvocationArguments(IMethodSymbol methodSymbol, int argumentCount) {
            if (methodSymbol == null) {
                return false;
            }

            int requiredParameterCount = methodSymbol.Parameters.Count(parameterSymbol => !parameterSymbol.IsOptional && !parameterSymbol.IsParams);
            if (argumentCount < requiredParameterCount) {
                return false;
            }

            if (methodSymbol.Parameters.Any(parameterSymbol => parameterSymbol.IsParams)) {
                return argumentCount >= requiredParameterCount;
            }

            return argumentCount <= methodSymbol.Parameters.Length;
        }

        ITypeSymbol ResolveNativeToStringReceiverType(
            SemanticModel semantic,
            ExpressionSyntax receiverExpression,
            IMethodSymbol toStringMethodSymbol) {
            if (TryResolveNativeCollectionPropertyTypeSymbol(semantic, receiverExpression, out ITypeSymbol collectionPropertyTypeSymbol)) {
                return collectionPropertyTypeSymbol;
            }

            if (receiverExpression is MemberAccessExpressionSyntax nullableValueAccess &&
                string.Equals(nullableValueAccess.Name.Identifier.Text, "Value", StringComparison.Ordinal) &&
                TryResolveNullableUnderlyingType(semantic, nullableValueAccess.Expression, out ITypeSymbol nullableUnderlyingType)) {
                return nullableUnderlyingType;
            }

            if (TryGetExpressionTypeSymbol(semantic, receiverExpression, out ITypeSymbol receiverTypeSymbol)) {
                return receiverTypeSymbol;
            }

            if (toStringMethodSymbol?.Name == "ToString" && toStringMethodSymbol.ContainingType != null) {
                return toStringMethodSymbol.ContainingType;
            }

            return null;
        }

        static bool TryResolveNativeCollectionPropertyTypeSymbol(
            SemanticModel semantic,
            ExpressionSyntax expression,
            out ITypeSymbol typeSymbol) {
            typeSymbol = null;

            if (expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name is not IdentifierNameSyntax identifierName) {
                return false;
            }

            bool isCountProperty = string.Equals(identifierName.Identifier.Text, "Count", StringComparison.Ordinal);
            bool isCapacityProperty = string.Equals(identifierName.Identifier.Text, "Capacity", StringComparison.Ordinal);
            bool isLengthProperty = string.Equals(identifierName.Identifier.Text, "Length", StringComparison.Ordinal);
            if (!isCountProperty && !isCapacityProperty && !isLengthProperty) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, memberAccess.Expression, out ITypeSymbol receiverTypeSymbol)) {
                return false;
            }

            if ((isCountProperty && IsCountableCollectionTypeSymbol(receiverTypeSymbol)) ||
                (isCapacityProperty && IsListFamilyTypeSymbol(receiverTypeSymbol)) ||
                (isLengthProperty && (receiverTypeSymbol is IArrayTypeSymbol ||
                                      string.Equals(receiverTypeSymbol.Name, "Array", StringComparison.Ordinal)))) {
                typeSymbol = semantic.Compilation.GetSpecialType(SpecialType.System_Int32);
                return true;
            }

            return false;
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

        bool IsStringLikeExpression(SemanticModel semantic, ExpressionSyntax expression) {
            if (IsStringExpression(semantic, expression)) {
                return true;
            }

            if (expression is not InvocationExpressionSyntax invocationExpression) {
                return false;
            }

            IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (invokedMethodSymbol != null &&
                invokedMethodSymbol.ReturnType?.SpecialType == SpecialType.System_String) {
                return true;
            }

            return invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is IdentifierNameSyntax identifierName &&
                (string.Equals(identifierName.Identifier.Text, "Substring", StringComparison.Ordinal) ||
                 string.Equals(identifierName.Identifier.Text, "Trim", StringComparison.Ordinal) ||
                 string.Equals(identifierName.Identifier.Text, "ToLowerInvariant", StringComparison.Ordinal)) &&
                IsStringLikeExpression(semantic, memberAccess.Expression);
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

        static bool ShouldEmitNativeCapacityCall(
            SemanticModel semantic,
            MemberAccessExpressionSyntax memberAccess,
            ISymbol memberSymbol) {
            if (memberAccess?.Name is not IdentifierNameSyntax identifierName ||
                !string.Equals(identifierName.Identifier.Text, "Capacity", StringComparison.Ordinal)) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, memberAccess.Expression, out ITypeSymbol receiverTypeSymbol)) {
                return false;
            }

            return IsListFamilyTypeSymbol(receiverTypeSymbol);
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

        static bool IsListFamilyTypeSymbol(ITypeSymbol typeSymbol) {
            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return false;
            }

            string name = namedTypeSymbol.Name;
            string displayText = namedTypeSymbol.ToDisplayString();
            return string.Equals(name, "List", StringComparison.Ordinal) ||
                string.Equals(name, "IReadOnlyList", StringComparison.Ordinal) ||
                string.Equals(name, "ICollection", StringComparison.Ordinal) ||
                string.Equals(name, "IReadOnlyCollection", StringComparison.Ordinal) ||
                string.Equals(name, "IEnumerable", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.IReadOnlyList<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.ICollection<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.IReadOnlyCollection<", StringComparison.Ordinal) ||
                displayText.StartsWith("System.Collections.Generic.IEnumerable<", StringComparison.Ordinal);
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
            ConversionClass currentClass = context.GetCurrentClass();
            if (currentClass?.TypeSymbol?.IsValueType == true) {
                lines.Add("(*this)");
            } else {
                lines.Add("this");
            }

            context.AddClass(context.Class[0]);
        }

        protected override ExpressionResult ProcessBinaryExpressionSyntax(SemanticModel semantic, LayerContext context, BinaryExpressionSyntax binary, List<string> lines) {
            if (binary.IsKind(SyntaxKind.CoalesceExpression) && binary.Right is ThrowExpressionSyntax throwExpression) {
                return ProcessCoalesceThrowExpression(semantic, context, binary, throwExpression, lines);
            }

            if (binary.IsKind(SyntaxKind.CoalesceExpression) &&
                TryProcessStringCoalesceExpression(semantic, context, binary, lines, out ExpressionResult stringCoalesceResult)) {
                return stringCoalesceResult;
            }

            if (binary.IsKind(SyntaxKind.AddExpression) &&
                TryProcessStringConcatenationExpression(semantic, context, binary, lines, out ExpressionResult stringConcatResult)) {
                return stringConcatResult;
            }

            if (binary.IsKind(SyntaxKind.CoalesceExpression) &&
                TryProcessPointerCoalesceExpression(semantic, context, binary, lines, out ExpressionResult coalesceResult)) {
                return coalesceResult;
            }

            if (TryProcessEventNullComparison(semantic, context, binary, lines)) {
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
            }

            if (TryProcessStringNullComparison(semantic, context, binary, lines)) {
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
            }

            if (TryProcessEnumArithmeticExpression(semantic, context, binary, lines, out ExpressionResult enumArithmeticResult)) {
                return enumArithmeticResult;
            }

            BinaryOpTypes op = ParseBinaryExpression(semantic, context, binary, out List<string> left, out List<string> right, out ExpressionResult result);

            string rightText = string.Concat(right).Trim();
            ITypeSymbol leftTypeSymbol = semantic.GetTypeInfo(binary.Left).ConvertedType ?? semantic.GetTypeInfo(binary.Left).Type;
            if ((binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)) &&
                string.Equals(rightText, "nullptr", StringComparison.Ordinal) &&
                ((result.Type != null && result.Type.Type == VariableDataType.String) ||
                 leftTypeSymbol?.SpecialType == SpecialType.System_String)) {
                RegisterRuntimeRequirement("NativeString");

                if (binary.IsKind(SyntaxKind.NotEqualsExpression)) {
                    lines.Add("!");
                }

                lines.Add("String::IsNullOrEmpty(");
                lines.AddRange(left);
                lines.Add(")");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
            }

            lines.AddRange(left);

            lines.Add($" {op.ToStringOperator()} ");

            lines.AddRange(right);

            if (binary.IsKind(SyntaxKind.SubtractExpression) && IsDateTimeVariableType(result.Type)) {
                RegisterRuntimeRequirement("NativeDateTime");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("TimeSpan"));
            }

            return result;
        }

        bool TryProcessEnumArithmeticExpression(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binary,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (!binary.IsKind(SyntaxKind.SubtractExpression) &&
                !binary.IsKind(SyntaxKind.AddExpression)) {
                return false;
            }

            ITypeSymbol leftTypeSymbol = semantic.GetTypeInfo(binary.Left).ConvertedType ?? semantic.GetTypeInfo(binary.Left).Type;
            ITypeSymbol rightTypeSymbol = semantic.GetTypeInfo(binary.Right).ConvertedType ?? semantic.GetTypeInfo(binary.Right).Type;
            bool leftIsEnum = leftTypeSymbol?.TypeKind == TypeKind.Enum;
            bool rightIsEnum = rightTypeSymbol?.TypeKind == TypeKind.Enum;
            if (!leftIsEnum && !rightIsEnum) {
                return false;
            }

            lines.Add("static_cast<int32_t>(");
            int leftDepth = context.DepthClass;
            ProcessExpression(semantic, context, binary.Left, lines);
            context.PopClass(leftDepth);
            lines.Add(")");
            lines.Add(binary.IsKind(SyntaxKind.SubtractExpression) ? " - " : " + ");
            lines.Add("static_cast<int32_t>(");
            int rightDepth = context.DepthClass;
            ProcessExpression(semantic, context, binary.Right, lines);
            context.PopClass(rightDepth);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("int"));
            return true;
        }

        bool TryProcessPointerCoalesceExpression(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binary,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (!TryGetExpressionTypeSymbol(semantic, binary, out ITypeSymbol resultTypeSymbol) ||
                IsWeakRecoveredTypeSymbol(resultTypeSymbol)) {
                if (!TryResolvePreferredCoalesceTypeSymbol(semantic, binary, out resultTypeSymbol)) {
                    return false;
                }
            }

            VariableType resultSourceType = VariableUtil.GetVarType(resultTypeSymbol);
            CPPTypeData resultTypeData;
            VariableType cppResultType = ConvertToCPPType(resultSourceType, out resultTypeData);

            List<string> leftLines = new List<string>();
            int startLeft = context.DepthClass;
            ExpressionResult leftResult = ProcessExpression(semantic, context, binary.Left, leftLines);
            context.PopClass(startLeft);
            if (!leftResult.Processed) {
                result = leftResult;
                return true;
            }

            List<string> rightLines = new List<string>();
            int startRight = context.DepthClass;
            ExpressionResult rightResult = ProcessExpression(semantic, context, binary.Right, rightLines);
            context.PopClass(startRight);
            if (!rightResult.Processed) {
                result = rightResult;
                return true;
            }

            if (IsWeakVariableType(resultSourceType) ||
                !resultTypeData.IsPointer) {
                if (TryResolvePreferredCoalesceVariableType(leftResult.Type, rightResult.Type, out VariableType preferredResultType)) {
                    resultSourceType = preferredResultType;
                    cppResultType = ConvertToCPPType(resultSourceType, out resultTypeData);
                }
            }

            if (!resultTypeData.IsPointer) {
                return false;
            }

            string tempName = "__coalesce_" + Guid.NewGuid().ToString("N")[..8];
            string typeName = QualifyRenderedCppTypeName(cppResultType.ToCPPString(context.Program), context);
            string pointerSuffix = resultTypeData.IsPointer ? "*" : string.Empty;

            lines.Add("([&]() {\n");
            if (leftResult.BeforeLines != null && leftResult.BeforeLines.Count > 0) {
                lines.AddRange(leftResult.BeforeLines);
            }

            lines.Add($"{typeName}{pointerSuffix} {tempName} = ");
            lines.AddRange(leftLines);
            lines.Add(";\n");

            if (rightResult.BeforeLines != null && rightResult.BeforeLines.Count > 0) {
                lines.AddRange(rightResult.BeforeLines);
            }

            lines.Add("return ");
            lines.Add(tempName);
            lines.Add(" != nullptr ? ");
            lines.Add(tempName);
            lines.Add(" : ");
            lines.AddRange(rightLines);
            lines.Add(";\n})()");

            result = new ExpressionResult(true, VariablePath.Unknown, cppResultType);
            return true;
        }

        bool TryResolvePreferredCoalesceVariableType(
            VariableType leftType,
            VariableType rightType,
            out VariableType resultType) {
            resultType = null;

            if (TryResolvePointerCompatibleCoalesceType(leftType, out resultType)) {
                return true;
            }

            return TryResolvePointerCompatibleCoalesceType(rightType, out resultType);
        }

        bool TryResolvePointerCompatibleCoalesceType(VariableType candidateType, out VariableType resultType) {
            resultType = null;
            if (IsWeakVariableType(candidateType)) {
                return false;
            }

            VariableType convertedType = ConvertToCPPType(candidateType, out CPPTypeData typeData);
            if (!typeData.IsPointer) {
                return false;
            }

            resultType = candidateType;
            return convertedType != null;
        }

        static bool TryResolvePreferredCoalesceTypeSymbol(
            SemanticModel semantic,
            BinaryExpressionSyntax binary,
            out ITypeSymbol resultTypeSymbol) {
            resultTypeSymbol = null;

            if (TryResolvePreferredCoalesceOperandTypeSymbol(semantic, binary.Left, out resultTypeSymbol)) {
                return true;
            }

            return TryResolvePreferredCoalesceOperandTypeSymbol(semantic, binary.Right, out resultTypeSymbol);
        }

        static bool TryResolvePreferredCoalesceOperandTypeSymbol(
            SemanticModel semantic,
            ExpressionSyntax expression,
            out ITypeSymbol resultTypeSymbol) {
            resultTypeSymbol = null;

            TypeInfo typeInfo = semantic.GetTypeInfo(expression);
            if (!IsWeakRecoveredTypeSymbol(typeInfo.Type)) {
                resultTypeSymbol = typeInfo.Type;
                return true;
            }

            if (TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol expressionTypeSymbol) &&
                !IsWeakRecoveredTypeSymbol(expressionTypeSymbol)) {
                resultTypeSymbol = expressionTypeSymbol;
                return true;
            }

            return false;
        }

        bool TryProcessStringCoalesceExpression(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binary,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (!IsStringCoalesceExpression(semantic, binary)) {
                return false;
            }

            int start = context.DepthClass;
            result = ProcessExpression(semantic, context, binary.Left, lines);
            context.PopClass(start);
            return true;
        }

        bool TryProcessStringConcatenationExpression(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binary,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (!IsStringConcatenationExpression(semantic, binary)) {
                return false;
            }

            RegisterRuntimeRequirement("NativeString");
            List<string> segments = new List<string>();
            CollectStringConcatenationSegments(semantic, context, binary, segments);
            lines.Add("String::Concat(");
            for (int index = 0; index < segments.Count; index++) {
                lines.Add(segments[index]);
                if (index < segments.Count - 1) {
                    lines.Add(", ");
                }
            }
            lines.Add(")");

            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
            return true;
        }

        void CollectStringConcatenationSegments(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax expression,
            List<string> segments) {
            if (expression is BinaryExpressionSyntax nestedBinary &&
                nestedBinary.IsKind(SyntaxKind.AddExpression)) {
                CollectStringConcatenationSegments(semantic, context, nestedBinary.Left, segments);
                CollectStringConcatenationSegments(semantic, context, nestedBinary.Right, segments);
                return;
            }

            segments.Add(BuildStringExpressionSegment(semantic, context, expression));
        }

        bool IsStringConcatenationExpression(SemanticModel semantic, BinaryExpressionSyntax binary) {
            ITypeSymbol resultTypeSymbol = semantic.GetTypeInfo(binary).ConvertedType ?? semantic.GetTypeInfo(binary).Type;
            return IsStringTypeSymbol(resultTypeSymbol) ||
                IsStringLikeExpression(semantic, binary.Left) ||
                IsStringLikeExpression(semantic, binary.Right);
        }

        bool IsStringCoalesceExpression(SemanticModel semantic, BinaryExpressionSyntax binary) {
            ITypeSymbol resultTypeSymbol = semantic.GetTypeInfo(binary).ConvertedType ?? semantic.GetTypeInfo(binary).Type;
            return IsStringTypeSymbol(resultTypeSymbol) ||
                IsStringLikeExpression(semantic, binary.Left) ||
                IsStringLikeExpression(semantic, binary.Right);
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

        bool TryProcessStringNullComparison(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binary,
            List<string> lines) {
            if (!binary.IsKind(SyntaxKind.NotEqualsExpression) &&
                !binary.IsKind(SyntaxKind.EqualsExpression)) {
                return false;
            }

            ExpressionSyntax stringSide = null;
            if (binary.Left is LiteralExpressionSyntax leftLiteral &&
                leftLiteral.IsKind(SyntaxKind.NullLiteralExpression)) {
                stringSide = binary.Right;
            } else if (binary.Right is LiteralExpressionSyntax rightLiteral &&
                rightLiteral.IsKind(SyntaxKind.NullLiteralExpression)) {
                stringSide = binary.Left;
            }

            if (stringSide == null) {
                return false;
            }

            List<string> stringLines = new List<string>();
            int startString = context.DepthClass;
            ExpressionResult stringResult = ProcessExpression(semantic, context, stringSide, stringLines);
            context.PopClass(startString);

            if (!IsManagedStringExpression(semantic, stringSide, stringResult)) {
                return false;
            }

            RegisterRuntimeRequirement("NativeString");

            if (binary.IsKind(SyntaxKind.NotEqualsExpression)) {
                lines.Add("!");
            }

            lines.Add("String::IsNullOrEmpty(");
            lines.AddRange(stringLines);
            lines.Add(")");
            return true;
        }

        /// <summary>
        /// Determines whether a lowered expression should be treated as a managed string for native comparison helpers.
        /// </summary>
        /// <param name="semantic">Semantic model used to inspect the expression.</param>
        /// <param name="expression">Source expression being evaluated.</param>
        /// <param name="expressionResult">Lowered expression metadata produced by the active lowering path.</param>
        /// <returns><c>true</c> when the expression represents a managed string value; otherwise, <c>false</c>.</returns>
        static bool IsManagedStringExpression(
            SemanticModel semantic,
            ExpressionSyntax expression,
            ExpressionResult expressionResult) {
            VariableType expressionType = expressionResult.Type;
            if (expressionType?.Type == VariableDataType.String ||
                string.Equals(expressionType?.TypeName, "std::string", StringComparison.Ordinal) ||
                string.Equals(expressionType?.TypeName, "string", StringComparison.Ordinal) ||
                string.Equals(expressionType?.TypeName, "String", StringComparison.Ordinal)) {
                return true;
            }

            if (TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol stringTypeSymbol) &&
                stringTypeSymbol.SpecialType == SpecialType.System_String) {
                return true;
            }

            if (expression is not ElementAccessExpressionSyntax elementAccess) {
                return false;
            }

            TypeInfo receiverTypeInfo = semantic.GetTypeInfo(elementAccess.Expression);
            ITypeSymbol receiverTypeSymbol = receiverTypeInfo.ConvertedType ?? receiverTypeInfo.Type;
            if (receiverTypeSymbol is IArrayTypeSymbol arrayTypeSymbol) {
                return arrayTypeSymbol.ElementType.SpecialType == SpecialType.System_String;
            }

            if (receiverTypeSymbol is not INamedTypeSymbol namedTypeSymbol ||
                namedTypeSymbol.TypeArguments.Length == 0) {
                return false;
            }

            ITypeSymbol elementTypeSymbol = namedTypeSymbol.TypeArguments[0];
            if (elementTypeSymbol.SpecialType != SpecialType.System_String) {
                return false;
            }

            string receiverName = namedTypeSymbol.Name;
            string receiverDisplayName = namedTypeSymbol.ToDisplayString();
            return string.Equals(receiverName, "List", StringComparison.Ordinal) ||
                string.Equals(receiverName, "IReadOnlyList", StringComparison.Ordinal) ||
                receiverDisplayName.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal) ||
                receiverDisplayName.StartsWith("System.Collections.Generic.IReadOnlyList<", StringComparison.Ordinal);
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

        bool TryProcessNativeListCapacityAssignment(
            SemanticModel semantic,
            LayerContext context,
            AssignmentExpressionSyntax assignment,
            List<string> lines) {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                assignment.Left is not MemberAccessExpressionSyntax memberAccess ||
                !ShouldEmitNativeCapacityCall(semantic, memberAccess, semantic.GetSymbolInfo(memberAccess).Symbol ?? semantic.GetSymbolInfo(memberAccess.Name).Symbol)) {
                return false;
            }

            string receiverText = RenderExpressionText(semantic, context, memberAccess.Expression);
            lines.Add($"{receiverText}->SetCapacity(");
            int start = context.DepthClass;
            ProcessExpression(semantic, context, assignment.Right, lines);
            context.PopClass(start);
            lines.Add(")");
            return true;
        }

        static bool IsStringTypeSymbol(ITypeSymbol typeSymbol) {
            return typeSymbol?.SpecialType == SpecialType.System_String;
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

            ITypeSymbol resultTypeSymbol = semantic.GetTypeInfo(binary).ConvertedType ?? semantic.GetTypeInfo(binary).Type;
            if (IsStringTypeSymbol(resultTypeSymbol) || IsStringCoalesceExpression(semantic, binary)) {
                lines.AddRange(left);
                return leftResult;
            }

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
                lines.Add(RenderConvertedGenericArgumentType(semantic, context, genType));

                if (i < count - 1) {
                    lines.Add(",");
                }

                i++;
            }
            lines.Add(">");
        }

        protected override void ProcessImplicitArrayCreationExpression(SemanticModel semantic, LayerContext context, ImplicitArrayCreationExpressionSyntax implicitArray, List<string> lines) {
            if (TryProcessArrayInitializerTargetType(semantic, context, implicitArray, implicitArray.Initializer.Expressions, lines)) {
                return;
            }

            lines.Add("[");
            AppendExpressionList(semantic, context, implicitArray.Initializer.Expressions, lines);
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
                if (!TryProcessArrayInitializerTargetType(semantic, context, arrayCreation, arrayCreation.Initializer.Expressions, lines)) {
                    lines.Add("{ ");
                    AppendExpressionList(semantic, context, arrayCreation.Initializer.Expressions, lines);
                    lines.Add(" }");
                }
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

        bool TryProcessArrayInitializerTargetType(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax arrayExpression,
            SeparatedSyntaxList<ExpressionSyntax> expressions,
            List<string> lines) {
            TypeInfo targetTypeInfo = semantic.GetTypeInfo(arrayExpression);
            ITypeSymbol? targetTypeSymbol = targetTypeInfo.ConvertedType ?? targetTypeInfo.Type;

            if (targetTypeSymbol is IArrayTypeSymbol arrayTypeSymbol) {
                string arrayElementTypeName = GetCppTypeToken(VariableUtil.GetVarType(arrayTypeSymbol.ElementType), context.Program);
                lines.Add($"new Array<{arrayElementTypeName}>({{ ");
                AppendExpressionList(semantic, context, expressions, lines);
                lines.Add(" })");
                return true;
            }

            VariableType targetVariableType = targetTypeSymbol != null
                ? VariableUtil.GetVarType(targetTypeSymbol)
                : VariableUtil.GetVarType("object");

            if (targetVariableType.Type != VariableDataType.List) {
                return false;
            }

            CPPTypeData targetTypeData;
            VariableType cppTargetType = ConvertToCPPType(targetVariableType, out targetTypeData);
            lines.Add($"new {cppTargetType.ToCPPString(context.Program)}({{ ");
            AppendExpressionList(semantic, context, expressions, lines);
            lines.Add(" })");
            return true;
        }

        void AppendExpressionList(
            SemanticModel semantic,
            LayerContext context,
            SeparatedSyntaxList<ExpressionSyntax> expressions,
            List<string> lines) {
            for (int i = 0; i < expressions.Count; i++) {
                ProcessExpression(semantic, context, expressions[i], lines);

                if (i < expressions.Count - 1) {
                    lines.Add(", ");
                }
            }
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
            TypeInfo targetTypeInfo = semantic.GetTypeInfo(collectionExpression);
            ITypeSymbol targetTypeSymbol = targetTypeInfo.ConvertedType ?? targetTypeInfo.Type;

            if (targetTypeSymbol is IArrayTypeSymbol arrayTypeSymbol) {
                string arrayElementTypeName = GetCppTypeToken(VariableUtil.GetVarType(arrayTypeSymbol.ElementType), context.Program);
                lines.Add($"new Array<{arrayElementTypeName}>(");
                AppendCollectionExpressionInitializerList(semantic, context, collectionExpression, lines);
                lines.Add(")");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(targetTypeSymbol));
            }

            VariableType targetVariableType = targetTypeSymbol != null
                ? VariableUtil.GetVarType(targetTypeSymbol)
                : VariableUtil.GetVarType("object");

            if (targetVariableType.Type == VariableDataType.List) {
                CPPTypeData targetTypeData;
                VariableType cppTargetType = ConvertToCPPType(targetVariableType, out targetTypeData);
                lines.Add($"new {cppTargetType.ToCPPString(context.Program)}(");
                AppendCollectionExpressionInitializerList(semantic, context, collectionExpression, lines);
                lines.Add(")");
                return new ExpressionResult(true, VariablePath.Unknown, cppTargetType);
            }

            AppendCollectionExpressionInitializerList(semantic, context, collectionExpression, lines);
            return new ExpressionResult(true, VariablePath.Unknown, null);
        }

        void AppendCollectionExpressionInitializerList(
            SemanticModel semantic,
            LayerContext context,
            CollectionExpressionSyntax collectionExpression,
            List<string> lines) {
            lines.Add("{ ");

            for (int i = 0; i < collectionExpression.Elements.Count; i++) {
                if (collectionExpression.Elements[i] is not ExpressionElementSyntax expressionElement) {
                    continue;
                }

                int startDepth = context.DepthClass;
                ProcessExpression(semantic, context, expressionElement.Expression, lines);
                context.PopClass(startDepth);
                if (i < collectionExpression.Elements.Count - 1) {
                    lines.Add(", ");
                }
            }

            lines.Add(" }");
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

        /// <summary>
        /// Emits a native ValueTuple allocation and recovers element types from the tuple arguments when Roslyn does not surface a strong tuple symbol.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the tuple expression.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="tupleExpression">Tuple expression being lowered.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        protected override void ProcessTupleExpression(SemanticModel semantic, LayerContext context, TupleExpressionSyntax tupleExpression, List<string> lines) {
            List<List<string>> argumentLines = new List<List<string>>();
            List<ExpressionResult> argumentResults = new List<ExpressionResult>();
            foreach (ArgumentSyntax argument in tupleExpression.Arguments) {
                List<string> currentArgumentLines = new List<string>();
                int startDepth = context.DepthClass;
                ExpressionResult currentArgumentResult = ProcessExpression(semantic, context, argument.Expression, currentArgumentLines);
                context.PopClass(startDepth);
                argumentLines.Add(currentArgumentLines);
                argumentResults.Add(currentArgumentResult);
            }

            VariableType tupleType = ResolveTupleExpressionType(semantic, tupleExpression);
            tupleType = RefineTupleExpressionTypeFromTrackedAddReceiver(context, tupleExpression, tupleType);
            tupleType = RefineTupleExpressionTypeFromArgumentResults(tupleType, argumentResults);
            RegisterGeneratedTypeReferences(context, tupleType);

            lines.Add("new ");
            lines.Add(QualifyRenderedCppTypeName(tupleType.ToCPPString(context.Program), context));
            lines.Add("(");

            for (int i = 0; i < tupleExpression.Arguments.Count; i++) {
                lines.AddRange(argumentLines[i]);

                if (i < tupleExpression.Arguments.Count - 1) {
                    lines.Add(", ");
                }
            }

            lines.Add(")");
        }

        /// <summary>
        /// Resolves the tuple allocation type for a tuple expression, preferring Roslyn's tuple symbol and falling back to the individual argument expression types when necessary.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the tuple expression.</param>
        /// <param name="tupleExpression">Tuple expression whose allocation type must be recovered.</param>
        /// <returns>The recovered tuple variable type used for native ValueTuple emission.</returns>
        VariableType ResolveTupleExpressionType(SemanticModel semantic, TupleExpressionSyntax tupleExpression) {
            TypeInfo tupleTypeInfo = semantic.GetTypeInfo(tupleExpression);
            ITypeSymbol tupleTypeSymbol = tupleTypeInfo.ConvertedType ?? tupleTypeInfo.Type;
            if (HasStrongTupleElementTypes(tupleTypeSymbol, tupleExpression.Arguments.Count)) {
                return VariableUtil.GetVarType(tupleTypeSymbol);
            }

            if (TryResolveListAddTupleElementType(semantic, tupleExpression, out ITypeSymbol listElementTupleTypeSymbol) &&
                HasStrongTupleElementTypes(listElementTupleTypeSymbol, tupleExpression.Arguments.Count)) {
                return VariableUtil.GetVarType(listElementTupleTypeSymbol);
            }

            if (TryResolveTargetTypedTupleExpressionType(semantic, tupleExpression, out ITypeSymbol targetTupleTypeSymbol) &&
                HasStrongTupleElementTypes(targetTupleTypeSymbol, tupleExpression.Arguments.Count)) {
                return VariableUtil.GetVarType(targetTupleTypeSymbol);
            }

            VariableType inferredTupleType = InferTupleExpressionType(semantic, tupleExpression);
            if (inferredTupleType.GenericArgs.Count == tupleExpression.Arguments.Count) {
                return inferredTupleType;
            }

            if (tupleTypeSymbol != null) {
                return VariableUtil.GetVarType(tupleTypeSymbol);
            }

            return new VariableType(VariableDataType.Tuple);
        }

        /// <summary>
        /// Resolves the concrete tuple element type from a strongly typed list receiver when a tuple literal is passed to <c>Add</c>.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the tuple expression.</param>
        /// <param name="tupleExpression">Tuple expression being lowered.</param>
        /// <param name="tupleTypeSymbol">Resolved tuple element type when the receiver list is strongly typed.</param>
        /// <returns><c>true</c> when the tuple expression is the argument to a list-family <c>Add</c> call with a tuple element type; otherwise <c>false</c>.</returns>
        bool TryResolveListAddTupleElementType(
            SemanticModel semantic,
            TupleExpressionSyntax tupleExpression,
            out ITypeSymbol tupleTypeSymbol) {
            tupleTypeSymbol = null;

            if (tupleExpression.Parent is not ArgumentSyntax tupleArgument ||
                tupleArgument.Parent?.Parent is not InvocationExpressionSyntax invocationExpression ||
                invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name is not IdentifierNameSyntax addIdentifier ||
                !string.Equals(addIdentifier.Identifier.Text, "Add", StringComparison.Ordinal)) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, memberAccess.Expression, out ITypeSymbol receiverTypeSymbol) ||
                receiverTypeSymbol is not INamedTypeSymbol namedReceiverType ||
                !IsListFamilyTypeSymbol(namedReceiverType) ||
                namedReceiverType.TypeArguments.Length == 0) {
                return false;
            }

            tupleTypeSymbol = namedReceiverType.TypeArguments[0];
            return tupleTypeSymbol is INamedTypeSymbol namedTupleType && namedTupleType.IsTupleType;
        }

        /// <summary>
        /// Resolves a target-typed tuple literal from its containing invocation argument so lowered tuple construction can reuse the parameter's concrete tuple element types.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the tuple expression.</param>
        /// <param name="tupleExpression">Tuple expression being lowered.</param>
        /// <param name="targetTupleTypeSymbol">Resolved target tuple type symbol when one is available.</param>
        /// <returns><c>true</c> when the tuple expression is target-typed by an invocation parameter with tuple element information; otherwise <c>false</c>.</returns>
        bool TryResolveTargetTypedTupleExpressionType(
            SemanticModel semantic,
            TupleExpressionSyntax tupleExpression,
            out ITypeSymbol targetTupleTypeSymbol) {
            targetTupleTypeSymbol = null;

            if (tupleExpression.Parent is not ArgumentSyntax tupleArgument ||
                tupleArgument.Parent?.Parent is not InvocationExpressionSyntax invocationExpression) {
                return false;
            }

            IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (invokedMethodSymbol == null) {
                return false;
            }

            int argumentIndex = invocationExpression.ArgumentList.Arguments.IndexOf(tupleArgument);
            if (argumentIndex < 0 || invokedMethodSymbol.Parameters.Length == 0) {
                return false;
            }

            int parameterIndex = argumentIndex;
            if (parameterIndex >= invokedMethodSymbol.Parameters.Length) {
                if (!invokedMethodSymbol.Parameters[^1].IsParams) {
                    return false;
                }

                parameterIndex = invokedMethodSymbol.Parameters.Length - 1;
            }

            targetTupleTypeSymbol = invokedMethodSymbol.Parameters[parameterIndex].Type;
            return targetTupleTypeSymbol != null;
        }

        /// <summary>
        /// Determines whether Roslyn supplied a tuple type whose element symbols are concrete enough to drive native ValueTuple emission directly.
        /// </summary>
        /// <param name="tupleTypeSymbol">Type symbol reported for the tuple expression.</param>
        /// <param name="elementCount">Expected number of tuple elements.</param>
        /// <returns><c>true</c> when the tuple element symbols are concrete and match the expression arity; otherwise <c>false</c>.</returns>
        static bool HasStrongTupleElementTypes(ITypeSymbol tupleTypeSymbol, int elementCount) {
            if (tupleTypeSymbol is not INamedTypeSymbol namedTupleType ||
                !namedTupleType.IsTupleType ||
                namedTupleType.TupleElements.Length != elementCount) {
                return false;
            }

            foreach (IFieldSymbol tupleElement in namedTupleType.TupleElements) {
                if (tupleElement.Type == null ||
                    tupleElement.Type.TypeKind == TypeKind.Error ||
                    tupleElement.Type.SpecialType == SpecialType.System_Object) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds a tuple type from the resolved tuple argument expression types so native ValueTuple construction stays strongly typed even when the tuple literal type is weakened upstream.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the tuple expression.</param>
        /// <param name="tupleExpression">Tuple expression whose arguments should be inspected.</param>
        /// <returns>The inferred tuple variable type assembled from the tuple arguments.</returns>
        VariableType InferTupleExpressionType(SemanticModel semantic, TupleExpressionSyntax tupleExpression) {
            VariableType tupleType = new VariableType(VariableDataType.Tuple);

            foreach (ArgumentSyntax argument in tupleExpression.Arguments) {
                if (TryGetExpressionTypeSymbol(semantic, argument.Expression, out ITypeSymbol argumentTypeSymbol) &&
                    argumentTypeSymbol != null &&
                    argumentTypeSymbol.TypeKind != TypeKind.Error) {
                    tupleType.GenericArgs.Add(VariableUtil.GetVarType(argumentTypeSymbol));
                } else {
                    tupleType.GenericArgs.Add(VariableUtil.GetVarType("object"));
                }
            }

            return tupleType;
        }

        /// <summary>
        /// Replaces weak tuple generic arguments with the already-lowered argument result types so native tuple construction stays concrete even when Roslyn weakens the tuple literal.
        /// </summary>
        /// <param name="tupleType">Tuple type recovered from semantic analysis.</param>
        /// <param name="argumentResults">Lowered tuple argument results collected during emission.</param>
        /// <returns>The refined tuple type that should be used for native ValueTuple construction.</returns>
        static VariableType RefineTupleExpressionTypeFromArgumentResults(VariableType tupleType, List<ExpressionResult> argumentResults) {
            if (argumentResults.Count == 0) {
                return tupleType;
            }

            bool needsTupleShape = tupleType == null || tupleType.Type != VariableDataType.Tuple;
            bool needsElementCountRepair = needsTupleShape || tupleType.GenericArgs.Count != argumentResults.Count;
            bool needsWeakElementRepair = !needsElementCountRepair && tupleType.GenericArgs.Any(IsWeakVariableType);
            if (!needsElementCountRepair && !needsWeakElementRepair) {
                return tupleType;
            }

            VariableType refinedTupleType = new VariableType(VariableDataType.Tuple);
            if (tupleType?.Args != null && tupleType.Args.Count > 0) {
                refinedTupleType.Args.AddRange(tupleType.Args.Select(tupleArgument => new VariableType(tupleArgument)));
            }

            for (int index = 0; index < argumentResults.Count; index++) {
                VariableType currentElementType = !needsTupleShape && index < tupleType.GenericArgs.Count
                    ? tupleType.GenericArgs[index]
                    : null;

                if (IsWeakVariableType(currentElementType) &&
                    !IsWeakVariableType(argumentResults[index].Type)) {
                    refinedTupleType.GenericArgs.Add(new VariableType(argumentResults[index].Type));
                    continue;
                }

                if (currentElementType != null) {
                    refinedTupleType.GenericArgs.Add(new VariableType(currentElementType));
                    continue;
                }

                refinedTupleType.GenericArgs.Add(VariableUtil.GetVarType("object"));
            }

            return refinedTupleType;
        }

        /// <summary>
        /// Reuses the converter's tracked local-variable metadata for list <c>Add</c> calls when semantic tuple recovery stays weak.
        /// </summary>
        /// <param name="context">Current lowering context.</param>
        /// <param name="tupleExpression">Tuple expression being lowered.</param>
        /// <param name="tupleType">Current tuple type candidate.</param>
        /// <returns>The receiver element tuple type when it is available from tracked locals; otherwise the original tuple type.</returns>
        static VariableType RefineTupleExpressionTypeFromTrackedAddReceiver(
            LayerContext context,
            TupleExpressionSyntax tupleExpression,
            VariableType tupleType) {
            if (tupleType != null &&
                tupleType.Type == VariableDataType.Tuple &&
                tupleType.GenericArgs.Count == tupleExpression.Arguments.Count &&
                !tupleType.GenericArgs.Any(IsWeakVariableType)) {
                return tupleType;
            }

            if (tupleExpression.Parent is not ArgumentSyntax tupleArgument ||
                tupleArgument.Parent?.Parent is not InvocationExpressionSyntax invocationExpression ||
                invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Expression is not IdentifierNameSyntax receiverIdentifier ||
                memberAccess.Name is not IdentifierNameSyntax addIdentifier ||
                !string.Equals(addIdentifier.Identifier.Text, "Add", StringComparison.Ordinal)) {
                return tupleType;
            }

            FunctionStack currentFunction = context.GetCurrentFunction();
            if (currentFunction == null) {
                return tupleType;
            }

            ConversionVariable trackedVariable = currentFunction.Stack.LastOrDefault(candidate => candidate.Name == receiverIdentifier.Identifier.Text);
            if (trackedVariable == null && currentFunction.Function?.InParameters != null) {
                trackedVariable = currentFunction.Function.InParameters.LastOrDefault(candidate => candidate.Name == receiverIdentifier.Identifier.Text);
            }

            VariableType receiverElementType = trackedVariable?.VarType?.GenericArgs.Count > 0
                ? trackedVariable.VarType.GenericArgs[0]
                : null;
            if (receiverElementType?.Type != VariableDataType.Tuple ||
                receiverElementType.GenericArgs.Count != tupleExpression.Arguments.Count) {
                return tupleType;
            }

            return new VariableType(receiverElementType);
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
            return BuildStringExpressionSegment(semantic, context, interpolation.Expression);
        }

        string BuildStringExpressionSegment(SemanticModel semantic, LayerContext context, ExpressionSyntax expression) {
            if (expression is BinaryExpressionSyntax nestedStringConcatBinary &&
                nestedStringConcatBinary.IsKind(SyntaxKind.AddExpression) &&
                IsStringConcatenationExpression(semantic, nestedStringConcatBinary)) {
                List<string> nestedSegments = new List<string>();
                CollectStringConcatenationSegments(semantic, context, nestedStringConcatBinary, nestedSegments);
                return $"String::Concat({string.Join(", ", nestedSegments)})";
            }

            List<string> interpolationLines = new List<string>();
            ExpressionResult interpolationResult = ProcessExpression(semantic, context, expression, interpolationLines);

            string expressionText = string.Concat(interpolationLines);
            VariableType expressionType = interpolationResult.Type ?? ResolveInterpolationType(semantic, expression);
            if ((expressionType == null || IsWeakVariableType(expressionType)) &&
                TryResolveInferredExpressionVariableType(context, semantic, expression, out VariableType inferredExpressionType)) {
                expressionType = inferredExpressionType;
            }

            if ((expressionType == null || IsWeakVariableType(expressionType)) &&
                TryResolveTrackedExpressionVariableType(context, expression, out VariableType trackedExpressionType)) {
                expressionType = trackedExpressionType;
            }

            if (expressionType.Type == VariableDataType.String ||
                IsStringLikeExpression(semantic, expression) ||
                expressionText.StartsWith("String::", StringComparison.Ordinal) ||
                expressionText.StartsWith("std::string", StringComparison.Ordinal)) {
                return expressionText;
            }

            if (expressionType.Type == VariableDataType.Char) {
                return $"std::string(1, {expressionText})";
            }

            if (expressionType.IsEnum) {
                return $"std::to_string(static_cast<int32_t>({expressionText}))";
            }

            if (IsNativeTypeNamePropertyAccess(semantic, expression)) {
                return expressionText;
            }

            if (IsNativeInterpolationType(expressionType.Type)) {
                return $"std::to_string({expressionText})";
            }

            if (expression is MemberAccessExpressionSyntax nullableValueAccess &&
                string.Equals(nullableValueAccess.Name.Identifier.Text, "Value", StringComparison.Ordinal) &&
                TryResolveNullableUnderlyingType(semantic, nullableValueAccess.Expression, out ITypeSymbol nullableUnderlyingType) &&
                IsNativeInterpolationType(VariableUtil.GetVarType(nullableUnderlyingType).Type)) {
                return $"std::to_string({expressionText})";
            }

            if ((TryResolveNativeCollectionPropertyTypeSymbol(semantic, expression, out ITypeSymbol collectionPropertyTypeSymbol) &&
                 IsNativeToStringTypeSymbol(collectionPropertyTypeSymbol)) ||
                (TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol interpolationTypeSymbol) &&
                 IsNativeToStringTypeSymbol(interpolationTypeSymbol))) {
                return $"std::to_string({expressionText})";
            }

            return $"{expressionText}->ToString()";
        }

        bool IsNativeTypeNamePropertyAccess(SemanticModel semantic, ExpressionSyntax expression) {
            if (expression is not MemberAccessExpressionSyntax memberAccess ||
                !string.Equals(memberAccess.Name.Identifier.Text, "Name", StringComparison.Ordinal)) {
                return false;
            }

            return true;
        }

        VariableType ResolveInterpolationType(SemanticModel semantic, ExpressionSyntax expression) {
            if (TryResolveNativeCollectionPropertyTypeSymbol(semantic, expression, out ITypeSymbol collectionPropertyTypeSymbol)) {
                return VariableUtil.GetVarType(collectionPropertyTypeSymbol);
            }

            if (TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol expressionType) &&
                expressionType != null) {
                return VariableUtil.GetVarType(expressionType);
            }

            return VariableUtil.GetVarType("string");
        }

        string GetCppTypeToken(VariableType sourceType, ConversionProgram program) {
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);
            if (typeData.IsPointer) {
                return $"{cppType.ToCPPString(program)}*";
            }

            return cppType.ToCPPString(program);
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
            bool shouldDereferenceElementAccess = ShouldDereferenceElementAccessExpression(semantic, elementAccess.Expression);
            if (shouldDereferenceElementAccess) {
                lines.Add("(*");
            }

            // Process the expression being accessed (e.g., array or object)
            int startClass = context.DepthClass;
            ProcessExpression(semantic, context, elementAccess.Expression, lines);
            List<ConversionClass> saved = context.SavePopClass(startClass);

            if (shouldDereferenceElementAccess) {
                lines.Add(")");
            }

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
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(varType, out typeData);

            if (!typeData.IsPointer &&
                TryGetExpressionTypeSymbol(semantic, castExpr.Expression, out ITypeSymbol sourceTypeSymbol) &&
                sourceTypeSymbol.SpecialType == SpecialType.System_Object) {
                lines.Add("(*static_cast<");
                lines.Add(cppType.ToCPPString(context.Program));
                lines.Add("*>(");
                ProcessExpression(semantic, context, castExpr.Expression, lines);
                lines.Add("))");
                return new ExpressionResult(true, VariablePath.Unknown, varType);
            }

            lines.Add("static_cast<");
            lines.Add(cppType.ToCPPString(context.Program));
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
            lines.Add("{\n");

            string disposalTarget = string.Empty;
            bool disposalUsesPointerAccess = false;

            if (usingStatement.Declaration != null) {
                ProcessDeclaration(semantic, context, usingStatement.Declaration, lines);
                lines.Add(";\n");

                VariableDeclaratorSyntax declaredVariable = usingStatement.Declaration.Variables.FirstOrDefault();
                if (declaredVariable != null) {
                    disposalTarget = declaredVariable.Identifier.Text;
                }

                VariableType declarationType = VariableUtil.GetVarType(usingStatement.Declaration.Type, semantic);
                CPPTypeData declarationTypeData;
                ConvertToCPPType(declarationType, out declarationTypeData);
                disposalUsesPointerAccess = declarationTypeData.IsPointer;
            } else if (usingStatement.Expression != null) {
                string usingResourceName = "__usingResource_" + Guid.NewGuid().ToString("N")[..8];
                List<string> resourceLines = new List<string>();
                int start = context.DepthClass;
                ExpressionResult resourceResult = ProcessExpression(semantic, context, usingStatement.Expression, resourceLines);
                context.PopClass(start);

                lines.Add("auto ");
                lines.Add(usingResourceName);
                lines.Add(" = ");
                lines.AddRange(resourceLines);
                lines.Add(";\n");

                disposalTarget = usingResourceName;
                disposalUsesPointerAccess = !UsesDirectMemberAccess(resourceResult);
            }

            ProcessStatement(semantic, context, usingStatement.Statement, lines);

            if (!string.IsNullOrWhiteSpace(disposalTarget)) {
                if (disposalUsesPointerAccess) {
                    lines.Add("if (");
                    lines.Add(disposalTarget);
                    lines.Add(" != nullptr) {\n");
                    lines.Add(disposalTarget);
                    lines.Add("->Dispose();\n");
                    lines.Add("}\n");
                } else {
                    lines.Add(disposalTarget);
                    lines.Add(".Dispose();\n");
                }
            }

            lines.Add("}\n");
        }

        protected override void ProcessLockStatement(SemanticModel semantic, LayerContext context, LockStatementSyntax lockStatement, List<string> lines) {
            // You can implement custom locking logic here if needed, otherwise omit the lock
            lines.Add("// Lock omitted in TypeScript\n");

            // Process the body of the lock statement
            ProcessStatement(semantic, context, lockStatement.Statement, lines);
        }


        protected override void ProcessTryStatement(SemanticModel semantic, LayerContext context, TryStatementSyntax tryStatement, List<string> lines) {
            if (tryStatement.Finally != null) {
                RegisterRuntimeRequirement("NativeFinally");

                string guardName = "__finallyGuard_" + Guid.NewGuid().ToString("N")[..8];
                lines.Add("{\n");
                lines.Add($"auto {guardName} = he_cpp_make_scope_exit([&]() {{\n");
                ProcessStatement(semantic, context, tryStatement.Finally.Block, lines);
                lines.Add("});\n");

                if (tryStatement.Catches.Count > 0) {
                    lines.Add("try {\n");
                    ProcessStatement(semantic, context, tryStatement.Block, lines);
                    lines.Add("}\n");

                    foreach (var catchClause in tryStatement.Catches) {
                        lines.Add("catch (...) {\n");
                        ProcessStatement(semantic, context, catchClause.Block, lines);
                        lines.Add("}\n");
                    }
                } else {
                    ProcessStatement(semantic, context, tryStatement.Block, lines);
                }

                lines.Add("}\n");
                return;
            }

            lines.Add("try {\n");
            ProcessStatement(semantic, context, tryStatement.Block, lines);
            lines.Add("}\n");

            foreach (var catchClause in tryStatement.Catches) {
                lines.Add("catch (...) {\n");
                ProcessStatement(semantic, context, catchClause.Block, lines);
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
            bool isPointerPatternType = declaredTypeData.IsPointer ||
                IsReferencePatternType(semantic, declarationPattern.Type, declaredType);
            if (!isPointerPatternType) {
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
            string declaredVariableTypeName = declaredType.IsGenericParameter
                ? declaredTypeName
                : $"{declaredTypeName}*";
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

            lines.Add($"    {declaredVariableTypeName} {variableName} = he_cpp_try_cast<{declaredTypeName}>({sourceExpression});\n");
            lines.Add($"    if ({variableName} != nullptr)\n");
            lines.Add("    {\n");

            ExpressionResult statementResult = ProcessStatement(semantic, context, ifStatement.Statement, lines);
            lines.Add("    }\n");

            if (ifStatement.Else != null) {
                lines.Add("else ");
                if (ifStatement.Else.Statement is IfStatementSyntax elseIfStatement) {
                    lines.Add("{\n");
                    ProcessIfStatement(semantic, context, elseIfStatement, lines);
                    lines.Add("}\n");
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

        bool IsReferencePatternType(
            SemanticModel semantic,
            TypeSyntax typeSyntax,
            VariableType declaredType) {
            ITypeSymbol typeSymbol = semantic.GetTypeInfo(typeSyntax).Type ?? semantic.GetTypeInfo(typeSyntax).ConvertedType;
            if (typeSymbol is ITypeParameterSymbol typeParameterSymbol) {
                if (typeParameterSymbol.HasReferenceTypeConstraint) {
                    return true;
                }

                return typeParameterSymbol.ConstraintTypes.Any(constraintType => !constraintType.IsValueType);
            }

            if (typeSymbol != null) {
                return !typeSymbol.IsValueType;
            }

            return declaredType?.IsGenericParameter == true;
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
            if (IsStringLikeExpression(semantic, switchStatement.Expression)) {
                ProcessStringSwitchStatement(semantic, context, switchStatement, lines);
                return;
            }

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

        void ProcessStringSwitchStatement(
            SemanticModel semantic,
            LayerContext context,
            SwitchStatementSyntax switchStatement,
            List<string> lines) {
            RegisterRuntimeRequirement("NativeString");

            string switchValueName = $"__switchValue{lines.Count}_{context.DepthFunction}";
            lines.Add("{\n");
            lines.Add($"const std::string {switchValueName} = ");
            int depth = context.DepthClass;
            ProcessExpression(semantic, context, switchStatement.Expression, lines);
            context.PopClass(depth);
            lines.Add(";\n");

            SwitchSectionSyntax defaultSection = null;
            bool wroteConditionalSection = false;

            foreach (SwitchSectionSyntax section in switchStatement.Sections) {
                List<CaseSwitchLabelSyntax> caseLabels = section.Labels.OfType<CaseSwitchLabelSyntax>().ToList();
                if (caseLabels.Count == 0) {
                    if (section.Labels.OfType<DefaultSwitchLabelSyntax>().Any()) {
                        defaultSection = section;
                    }

                    continue;
                }

                lines.Add(wroteConditionalSection ? "else if (" : "if (");
                for (int labelIndex = 0; labelIndex < caseLabels.Count; labelIndex++) {
                    if (labelIndex > 0) {
                        lines.Add(" || ");
                    }

                    lines.Add($"String::Equals({switchValueName}, ");
                    depth = context.DepthClass;
                    ProcessExpression(semantic, context, caseLabels[labelIndex].Value, lines);
                    context.PopClass(depth);
                    lines.Add(")");
                }

                lines.Add(") {\n");
                foreach (StatementSyntax statement in section.Statements) {
                    ProcessStatement(semantic, context, statement, lines);
                }

                lines.Add("}\n");
                wroteConditionalSection = true;
            }

            if (defaultSection != null) {
                lines.Add(wroteConditionalSection ? "else {\n" : "{\n");
                foreach (StatementSyntax statement in defaultSection.Statements) {
                    ProcessStatement(semantic, context, statement, lines);
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

            if (parsedType != null && parsedType.IsEnum) {
                typeData.IsArray = false;
                typeData.IsNativeType = false;
                typeData.IsPointer = false;
                return parsedType;
            }

            if (parsedType != null && parsedType.IsGenericParameter) {
                typeData.IsArray = false;
                typeData.IsNativeType = false;
                typeData.IsPointer = false;
                return parsedType;
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
                            typeData.IsPointer = true;
                            return new VariableType(parsedType.Type, "StringBuilder");
                        }

                        if (string.Equals(parsedType.TypeName, "StringReader", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.IO.StringReader", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("StringReader");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = true;
                            return new VariableType(parsedType.Type, "StringReader");
                        }

                        if (string.Equals(parsedType.TypeName, "MemoryStream", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.IO.MemoryStream", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("MemoryStream");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = true;
                            return new VariableType(parsedType.Type, "MemoryStream");
                        }

                        if (string.Equals(parsedType.TypeName, "StreamReader", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.IO.StreamReader", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("StreamReader");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = true;
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

                        if (string.Equals(parsedType.TypeName, "Func", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Func", StringComparison.Ordinal)) {
                            typeData.IsNativeType = false;
                            typeData.IsPointer = true;
                            return CreateConvertedGenericType(parsedType, "Func");
                        }

                        if (string.Equals(parsedType.TypeName, "Action", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Action", StringComparison.Ordinal)) {
                            typeData.IsNativeType = false;
                            typeData.IsPointer = true;
                            return CreateConvertedGenericType(parsedType, "Action");
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

                        ConversionClass generatedClass = ResolveGeneratedClass(parsedType);
                        if (generatedClass?.TypeSymbol?.IsValueType == true) {
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, generatedClass.GetEmittedTypeName(), parsedType.Args.ToList(), parsedType.GenericArgs.ToList());
                        }

                        if (generatedClass != null) {
                            typeData.IsNativeType = false;
                            typeData.IsPointer = true;
                            return CreateConvertedGenericType(parsedType, generatedClass.GetEmittedTypeName());
                        }

                        typeData.IsNativeType = false;
                        return parsedType;
                }
                case VariableDataType.Array: {
                        codeConverter?.RegisterRuntimeRequirement("NativeArray");
                        typeData.IsArray = false;
                        typeData.IsNativeType = false;
                        typeData.IsPointer = true;
                        return CreateConvertedGenericType(parsedType, "Array");
                }
                default:
                    typeData.IsArray = false;
                    typeData.IsNativeType = false;
                    typeData.IsPointer = true;
                    return parsedType;
            }
        }

        ConversionClass ResolveGeneratedClass(VariableType parsedType) {
            if (parsedType == null || codeConverter?.Program == null || string.IsNullOrWhiteSpace(parsedType.TypeName)) {
                return null;
            }

            string typeName = parsedType.TypeName;
            int separatorIndex = typeName.LastIndexOf('.');
            if (separatorIndex >= 0 && separatorIndex < typeName.Length - 1) {
                typeName = typeName[(separatorIndex + 1)..];
            }

            int nestedSeparatorIndex = typeName.LastIndexOf('+');
            if (nestedSeparatorIndex >= 0 && nestedSeparatorIndex < typeName.Length - 1) {
                typeName = typeName[(nestedSeparatorIndex + 1)..];
            }

            return codeConverter.Program.FindGeneratedClass(typeName, parsedType.GenericArgs?.Count ?? 0);
        }

        /// <summary>
        /// Determines whether a lowered expression should use direct value-member access instead of pointer-member access.
        /// </summary>
        /// <param name="result">Lowered receiver expression metadata.</param>
        /// <returns><c>true</c> when the receiver is a lightweight runtime value type; otherwise <c>false</c>.</returns>
        bool UsesDirectMemberAccess(ExpressionResult result) {
            return result.Type != null &&
                IsDirectMemberAccessType(result.Type);
        }

        /// <summary>
        /// Determines whether a lowered expression should use direct value-member access by inspecting Roslyn type information.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve the expression type.</param>
        /// <param name="expression">Expression to inspect.</param>
        /// <returns><c>true</c> when the expression resolves to a lightweight runtime value type; otherwise <c>false</c>.</returns>
        bool UsesDirectMemberAccess(SemanticModel semantic, ExpressionSyntax expression) {
            if (semantic == null || expression == null) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol typeSymbol)) {
                return expression is MemberAccessExpressionSyntax memberAccess &&
                    TryResolveMemberAccessResultTypeSymbol(semantic, memberAccess, out ITypeSymbol memberResultTypeSymbol) &&
                    IsDirectMemberAccessType(VariableUtil.GetVarType(memberResultTypeSymbol));
            }

            if (IsWeakRecoveredTypeSymbol(typeSymbol) &&
                expression is MemberAccessExpressionSyntax weakMemberAccess &&
                TryResolveMemberAccessResultTypeSymbol(semantic, weakMemberAccess, out ITypeSymbol recoveredMemberResultTypeSymbol)) {
                typeSymbol = recoveredMemberResultTypeSymbol;
            }

            return IsDirectMemberAccessType(VariableUtil.GetVarType(typeSymbol));
        }

        static bool TryResolveMemberAccessResultTypeSymbol(
            SemanticModel semantic,
            MemberAccessExpressionSyntax memberAccess,
            out ITypeSymbol typeSymbol) {
            typeSymbol = null;

            ISymbol memberSymbol = ResolveMemberAccessSymbol(semantic, memberAccess);

            switch (memberSymbol) {
                case IFieldSymbol fieldSymbol:
                    typeSymbol = fieldSymbol.Type;
                    return typeSymbol != null;
                case IPropertySymbol propertySymbol:
                    typeSymbol = propertySymbol.Type;
                    return typeSymbol != null;
                case IMethodSymbol methodSymbol:
                    typeSymbol = methodSymbol.ReturnType;
                    return typeSymbol != null;
                case IEventSymbol eventSymbol:
                    typeSymbol = eventSymbol.Type;
                    return typeSymbol != null;
                default:
                    return false;
            }
        }

        bool TryResolveTrackedExpressionVariableType(
            LayerContext context,
            ExpressionSyntax expression,
            out VariableType variableType) {
            variableType = null;

            if (context == null || expression == null) {
                return false;
            }

            if (expression is IdentifierNameSyntax identifierName) {
                return TryResolveTrackedIdentifierVariableType(context, identifierName.Identifier.Text, out variableType);
            }

            if (expression is MemberAccessExpressionSyntax memberAccess) {
                if (!TryResolveTrackedExpressionVariableType(context, memberAccess.Expression, out VariableType receiverType)) {
                    return false;
                }

                return TryResolveTrackedMemberVariableType(receiverType, memberAccess.Name.Identifier.Text, out variableType);
            }

            return false;
        }

        bool TryResolveTrackedIdentifierVariableType(
            LayerContext context,
            string identifier,
            out VariableType variableType) {
            variableType = null;

            FunctionStack currentFunction = context.GetCurrentFunction();
            ConversionVariable trackedVariable = currentFunction?.Stack.LastOrDefault(candidate => candidate.Name == identifier);
            if (trackedVariable == null && currentFunction?.Function?.InParameters != null) {
                trackedVariable = currentFunction.Function.InParameters.LastOrDefault(candidate => candidate.Name == identifier);
            }

            if (trackedVariable != null && trackedVariable.VarType != null) {
                variableType = trackedVariable.VarType;
                return true;
            }

            ConversionClass currentClass = context.GetCurrentClass();
            ConversionVariable classVariable = currentClass?.Variables?.LastOrDefault(candidate => candidate.Name == identifier);
            if (classVariable != null && classVariable.VarType != null) {
                variableType = classVariable.VarType;
                return true;
            }

            return false;
        }

        bool TryResolveTrackedMemberVariableType(
            VariableType receiverType,
            string memberName,
            out VariableType variableType) {
            variableType = null;

            ConversionClass receiverClass = ResolveGeneratedClass(receiverType);
            if (receiverClass?.Variables == null) {
                return false;
            }

            ConversionVariable memberVariable = receiverClass.Variables.LastOrDefault(candidate => candidate.Name == memberName);
            if (memberVariable?.VarType == null) {
                return false;
            }

            variableType = memberVariable.VarType;
            return true;
        }

        /// <summary>
        /// Determines whether a lowered member should use direct value-member access by inspecting its declaring type.
        /// </summary>
        /// <param name="symbol">Resolved member symbol.</param>
        /// <returns><c>true</c> when the member belongs to a lightweight runtime value type; otherwise <c>false</c>.</returns>
        bool UsesDirectMemberAccess(ISymbol symbol) {
            if (symbol == null || symbol.ContainingType == null) {
                return false;
            }

            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            return symbol.ContainingType != null &&
                IsDirectMemberAccessType(VariableUtil.GetVarType(symbol.ContainingType));
        }

        /// <summary>
        /// Determines whether a converted variable type should use direct member access because the emitted C++ type is not pointer-backed.
        /// </summary>
        /// <param name="variableType">Source-side variable type metadata to inspect.</param>
        /// <returns><c>true</c> when the emitted C++ receiver should use <c>.</c>; otherwise <c>false</c>.</returns>
        bool IsDirectMemberAccessType(VariableType variableType) {
            if (variableType == null) {
                return false;
            }

            if (variableType.IsNullable ||
                string.Equals(variableType.TypeName, "Nullable", StringComparison.Ordinal) ||
                IsDirectRuntimeTypeName(variableType.TypeName)) {
                return true;
            }

            ConvertToCPPType(variableType, out CPPTypeData typeData);
            return !typeData.IsPointer;
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

            if (string.Equals(shortTypeName, "BitConverter", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.BitConverter", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.BitConverter", StringComparison.Ordinal)) {
                runtimeTypeName = "BitConverter";
                runtimeRequirementName = "BitConverter";
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

            if (string.Equals(shortTypeName, "RegexOptions", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Text.RegularExpressions.RegexOptions", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Text.RegularExpressions.RegexOptions", StringComparison.Ordinal)) {
                runtimeTypeName = "RegexOptions";
                runtimeRequirementName = "Regex";
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

        static bool IsFuncTypeSymbol(ITypeSymbol? typeSymbol) {
            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return false;
            }

            string displayText = namedTypeSymbol.ToDisplayString();
            return string.Equals(namedTypeSymbol.Name, "Func", StringComparison.Ordinal) &&
                (string.Equals(namedTypeSymbol.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal) ||
                 displayText.StartsWith("System.Func", StringComparison.Ordinal) ||
                 string.Equals(displayText, "Func", StringComparison.Ordinal) ||
                 displayText.StartsWith("Func<", StringComparison.Ordinal));
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

        bool TryGetDelegateWrapperTypeName(
            ITypeSymbol typeSymbol,
            IMethodSymbol methodGroupSymbol,
            LayerContext context,
            out string delegateWrapperTypeName) {
            delegateWrapperTypeName = string.Empty;

            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol || !namedTypeSymbol.IsGenericType) {
                return false;
            }

            if (IsActionTypeSymbol(namedTypeSymbol)) {
                List<string> actionArgumentTypes = new List<string>();
                foreach (IParameterSymbol parameterSymbol in methodGroupSymbol.Parameters) {
                    actionArgumentTypes.Add(GetCppTypeToken(VariableUtil.GetVarType(parameterSymbol.Type), context.Program));
                }

                delegateWrapperTypeName = $"Action<{string.Join(", ", actionArgumentTypes)}>";
                return true;
            }

            if (!IsFuncTypeSymbol(namedTypeSymbol)) {
                return false;
            }

            List<string> funcArgumentTypes = new List<string>();
            foreach (IParameterSymbol parameterSymbol in methodGroupSymbol.Parameters) {
                funcArgumentTypes.Add(GetCppTypeToken(VariableUtil.GetVarType(parameterSymbol.Type), context.Program));
            }

            funcArgumentTypes.Add(GetCppTypeToken(VariableUtil.GetVarType(methodGroupSymbol.ReturnType), context.Program));
            delegateWrapperTypeName = $"Func<{string.Join(", ", funcArgumentTypes)}>";
            return true;
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
                return namedTypeSymbol.SpecialType == SpecialType.System_String ||
                    namedTypeSymbol.SpecialType == SpecialType.System_Char;
            }

            string expressionText = expression.ToString();
            return string.Equals(expressionText, "string", StringComparison.Ordinal) ||
                string.Equals(expressionText, "char", StringComparison.Ordinal) ||
                string.Equals(expressionText, "String", StringComparison.Ordinal) ||
                string.Equals(expressionText, "Char", StringComparison.Ordinal) ||
                string.Equals(expressionText, "System.String", StringComparison.Ordinal) ||
                string.Equals(expressionText, "System.Char", StringComparison.Ordinal);
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

        void AppendInvocationArguments(
            SemanticModel semantic,
            LayerContext context,
            IEnumerable<ArgumentSyntax> arguments,
            List<string> lines) {
            List<ArgumentSyntax> argumentList = arguments.ToList();
            for (int index = 0; index < argumentList.Count; index++) {
                ArgumentSyntax argument = argumentList[index];
                int start = context.DepthClass;
                ProcessExpression(semantic, context, argument.Expression, lines);
                context.PopClass(start);

                if (index < argumentList.Count - 1) {
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
                string.Equals(typeName, "FileNotFoundException", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.IO.FileNotFoundException", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.IO.FileNotFoundException", StringComparison.Ordinal) ||
                string.Equals(typeName, "DirectoryNotFoundException", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.IO.DirectoryNotFoundException", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.IO.DirectoryNotFoundException", StringComparison.Ordinal) ||
                string.Equals(typeName, "NotSupportedException", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.NotSupportedException", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.NotSupportedException", StringComparison.Ordinal);
        }

        static string NormalizeNativeExceptionTypeName(string typeName) {
            int globalSeparatorIndex = typeName.LastIndexOf("::", StringComparison.Ordinal);
            if (globalSeparatorIndex >= 0) {
                return typeName[(globalSeparatorIndex + 2)..];
            }

            int namespaceSeparatorIndex = typeName.LastIndexOf('.');
            if (namespaceSeparatorIndex >= 0) {
                return typeName[(namespaceSeparatorIndex + 1)..];
            }

            return typeName;
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
            return string.Equals(typeName, "Regex", StringComparison.Ordinal) ||
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
            typeSymbol = typeInfo.ConvertedType;
            if (IsWeakRecoveredTypeSymbol(typeSymbol) &&
                !IsWeakRecoveredTypeSymbol(typeInfo.Type)) {
                typeSymbol = typeInfo.Type;
            } else if (typeSymbol == null) {
                typeSymbol = typeInfo.Type;
            }

            if (IsWeakRecoveredTypeSymbol(typeSymbol) &&
                semantic.GetOperation(expression)?.Type is ITypeSymbol operationType &&
                !IsWeakRecoveredTypeSymbol(operationType)) {
                typeSymbol = operationType;
            }

            if (IsWeakRecoveredTypeSymbol(typeSymbol) &&
                TryResolveExpressionTypeFromStructure(semantic, expression, out ITypeSymbol structuredTypeSymbol)) {
                typeSymbol = structuredTypeSymbol;
            }

            if (expression is ElementAccessExpressionSyntax preferredElementAccess &&
                IsWeakRecoveredTypeSymbol(typeSymbol) &&
                TryResolveElementAccessTypeSymbol(semantic, preferredElementAccess, out ITypeSymbol preferredElementTypeSymbol)) {
                typeSymbol = preferredElementTypeSymbol;
                return true;
            }

            if (!IsWeakRecoveredTypeSymbol(typeSymbol)) {
                return true;
            }

            if (expression is ElementAccessExpressionSyntax elementAccess &&
                TryResolveElementAccessTypeSymbol(semantic, elementAccess, out typeSymbol)) {
                return true;
            }

            if (expression is MemberAccessExpressionSyntax memberAccess &&
                TryResolveMemberAccessResultTypeSymbol(semantic, memberAccess, out ITypeSymbol memberTypeSymbol)) {
                typeSymbol = memberTypeSymbol;
                return true;
            }

            if (TryResolveExpressionTypeFromSymbolInfo(semantic, expression, out ITypeSymbol symbolType)) {
                typeSymbol = symbolType;
                return true;
            }

            return typeSymbol != null;
        }

        static bool TryResolveExpressionTypeFromStructure(
            SemanticModel semantic,
            ExpressionSyntax expression,
            out ITypeSymbol typeSymbol) {
            typeSymbol = null;

            if (expression is InvocationExpressionSyntax invocationExpression) {
                int argumentCount = invocationExpression.ArgumentList.Arguments.Count;
                IMethodSymbol invokedMethodSymbol = null;

                if (semantic.GetOperation(invocationExpression) is IInvocationOperation invocationOperation &&
                    CanMethodMatchInvocationArguments(invocationOperation.TargetMethod, argumentCount)) {
                    invokedMethodSymbol = invocationOperation.TargetMethod;
                } else {
                    SymbolInfo invocationSymbolInfo = semantic.GetSymbolInfo(invocationExpression);
                    invokedMethodSymbol = ResolveMethodSymbol(invocationSymbolInfo);
                    if (!CanMethodMatchInvocationArguments(invokedMethodSymbol, argumentCount)) {
                        invokedMethodSymbol = ResolveBestInvocationCandidateMethodSymbol(invocationSymbolInfo, argumentCount);
                    }
                }

                if (!IsWeakRecoveredTypeSymbol(invokedMethodSymbol?.ReturnType)) {
                    typeSymbol = invokedMethodSymbol.ReturnType;
                    return true;
                }
            }

            if (expression is CastExpressionSyntax castExpression) {
                typeSymbol = semantic.GetTypeInfo(castExpression.Type).Type ?? semantic.GetTypeInfo(castExpression.Type).ConvertedType;
                return !IsWeakRecoveredTypeSymbol(typeSymbol);
            }

            if (expression is PrefixUnaryExpressionSyntax prefixExpression &&
                TryGetExpressionTypeSymbol(semantic, prefixExpression.Operand, out ITypeSymbol prefixOperandType) &&
                !IsWeakRecoveredTypeSymbol(prefixOperandType)) {
                typeSymbol = prefixOperandType;
                return true;
            }

            if (expression is ParenthesizedExpressionSyntax parenthesizedExpression) {
                return TryGetExpressionTypeSymbol(semantic, parenthesizedExpression.Expression, out typeSymbol);
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryResolveBestCommonTypeSymbol(semantic, conditionalExpression.WhenTrue, conditionalExpression.WhenFalse, out typeSymbol)) {
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression &&
                TryResolveBestCommonTypeSymbol(semantic, binaryExpression.Left, binaryExpression.Right, out typeSymbol)) {
                return true;
            }

            return false;
        }

        static bool TryResolveBestCommonTypeSymbol(
            SemanticModel semantic,
            ExpressionSyntax leftExpression,
            ExpressionSyntax rightExpression,
            out ITypeSymbol typeSymbol) {
            typeSymbol = null;

            if (!TryGetExpressionTypeSymbol(semantic, leftExpression, out ITypeSymbol leftTypeSymbol) ||
                !TryGetExpressionTypeSymbol(semantic, rightExpression, out ITypeSymbol rightTypeSymbol) ||
                IsWeakRecoveredTypeSymbol(leftTypeSymbol) ||
                IsWeakRecoveredTypeSymbol(rightTypeSymbol)) {
                return false;
            }

            if (SymbolEqualityComparer.Default.Equals(leftTypeSymbol, rightTypeSymbol)) {
                typeSymbol = leftTypeSymbol;
                return true;
            }

            SpecialType commonSpecialType = ChoosePreferredNumericSpecialType(leftTypeSymbol.SpecialType, rightTypeSymbol.SpecialType);
            if (commonSpecialType == SpecialType.None) {
                return false;
            }

            typeSymbol = semantic.Compilation.GetSpecialType(commonSpecialType);
            return !IsWeakRecoveredTypeSymbol(typeSymbol);
        }

        static SpecialType ChoosePreferredNumericSpecialType(SpecialType leftType, SpecialType rightType) {
            if (leftType == rightType) {
                return leftType;
            }

            SpecialType[] precedence = new[] {
                SpecialType.System_Double,
                SpecialType.System_Single,
                SpecialType.System_Int64,
                SpecialType.System_UInt64,
                SpecialType.System_Int32,
                SpecialType.System_UInt32,
                SpecialType.System_Int16,
                SpecialType.System_UInt16,
                SpecialType.System_SByte,
                SpecialType.System_Byte
            };

            foreach (SpecialType specialType in precedence) {
                if (leftType == specialType || rightType == specialType) {
                    return specialType;
                }
            }

            return SpecialType.None;
        }

        /// <summary>
        /// Resolves an expression type from the bound Roslyn symbol when type-info conversion weakens the expression to <c>object</c>.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve the expression.</param>
        /// <param name="expression">Expression whose bound symbol should be inspected.</param>
        /// <param name="typeSymbol">Resolved type symbol when the symbol exposes one.</param>
        /// <returns><c>true</c> when a symbol-backed type was recovered; otherwise <c>false</c>.</returns>
        static bool TryResolveExpressionTypeFromSymbolInfo(SemanticModel semantic, ExpressionSyntax expression, out ITypeSymbol typeSymbol) {
            ISymbol symbol = semantic.GetSymbolInfo(expression).Symbol;
            if (symbol == null && expression is MemberAccessExpressionSyntax memberAccess) {
                symbol = semantic.GetSymbolInfo(memberAccess.Name).Symbol;
            }

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
        /// Determines whether a recovered type symbol is too weak to trust for lowering decisions and should trigger stronger fallback resolution.
        /// </summary>
        /// <param name="typeSymbol">Type symbol to inspect.</param>
        /// <returns><c>true</c> when the symbol is missing, erroneous, or weakened to <c>object</c>; otherwise <c>false</c>.</returns>
        static bool IsWeakRecoveredTypeSymbol(ITypeSymbol typeSymbol) {
            return typeSymbol == null ||
                typeSymbol.TypeKind == TypeKind.Error ||
                typeSymbol.SpecialType == SpecialType.System_Object;
        }

        /// <summary>
        /// Determines whether a converter variable type is too weak to drive precise native tuple construction.
        /// </summary>
        /// <param name="variableType">Variable type metadata to inspect.</param>
        /// <returns><c>true</c> when the type is missing, unknown, or the managed <c>object</c> fallback; otherwise <c>false</c>.</returns>
        static bool IsWeakVariableType(VariableType variableType) {
            return variableType == null ||
                variableType.Type == VariableDataType.Unknown ||
                variableType.Type == VariableDataType.Object && string.Equals(variableType.TypeName, "object", StringComparison.OrdinalIgnoreCase);
        }

        static bool TryResolveElementAccessTypeSymbol(
            SemanticModel semantic,
            ElementAccessExpressionSyntax elementAccess,
            out ITypeSymbol typeSymbol) {
            typeSymbol = null;

            if (!TryGetExpressionTypeSymbol(semantic, elementAccess.Expression, out ITypeSymbol receiverTypeSymbol)) {
                return false;
            }

            if (receiverTypeSymbol.SpecialType == SpecialType.System_String) {
                typeSymbol = semantic.Compilation.GetSpecialType(SpecialType.System_Char);
                return typeSymbol != null && typeSymbol.SpecialType == SpecialType.System_Char;
            }

            if (receiverTypeSymbol is IArrayTypeSymbol arrayTypeSymbol) {
                typeSymbol = arrayTypeSymbol.ElementType;
                return typeSymbol != null;
            }

            if (receiverTypeSymbol is not INamedTypeSymbol namedTypeSymbol ||
                namedTypeSymbol.TypeArguments.Length == 0) {
                return false;
            }

            string receiverName = namedTypeSymbol.Name;
            string receiverDisplayName = namedTypeSymbol.ToDisplayString();
            if (string.Equals(receiverName, "List", StringComparison.Ordinal) ||
                string.Equals(receiverName, "IReadOnlyList", StringComparison.Ordinal) ||
                receiverDisplayName.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal) ||
                receiverDisplayName.StartsWith("System.Collections.Generic.IReadOnlyList<", StringComparison.Ordinal)) {
                typeSymbol = namedTypeSymbol.TypeArguments[0];
                return typeSymbol != null;
            }

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
                    convertedGenericArgument = new VariableType(
                        VariableDataType.Unknown,
                        $"{convertedGenericArgument.ToCPPString(null)}*");
                }

                convertedGenericArguments.Add(convertedGenericArgument);
            }

            return new VariableType(parsedType.Type, cppTypeName, parsedType.Args.ToList(), convertedGenericArguments);
        }

        string RenderConvertedGenericArgumentType(SemanticModel semantic, LayerContext context, TypeSyntax typeSyntax) {
            VariableType sourceType = VariableUtil.GetVarType(typeSyntax, semantic);
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);

            if (typeData.IsPointer) {
                return $"{cppType.ToCPPString(context.Program)}*";
            }

            return cppType.ToCPPString(context.Program);
        }

        static bool ShouldDereferenceElementAccessExpression(SemanticModel semantic, ExpressionSyntax expression) {
            if (!TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol typeSymbol)) {
                return false;
            }

            if (typeSymbol.SpecialType == SpecialType.System_String) {
                return false;
            }

            return typeSymbol is IArrayTypeSymbol || IsCountableCollectionTypeSymbol(typeSymbol);
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
            VariableType varType = ResolveDeclarationType(semantic, declaration);

            if (TryProcessSplitMultiVariableDeclaration(semantic, context, declaration, varType, lines)) {
                return;
            }

            if (TryProcessStackAllocDeclaration(semantic, context, declaration, varType, lines)) {
                return;
            }

            if (TryProcessCollectionExpressionArrayDeclaration(semantic, context, declaration, varType, lines)) {
                return;
            }

            if (TryProcessStringReaderLineDeclaration(semantic, context, declaration, varType, lines)) {
                return;
            }

            if (TryProcessVarElementAccessDeclaration(semantic, context, declaration, lines)) {
                return;
            }

            if (TryProcessVarMemberAccessDeclaration(semantic, context, declaration, lines)) {
                return;
            }

            if (TryProcessInferredVarDeclaration(semantic, context, declaration, lines)) {
                return;
            }

            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(varType, out typeData);
            RegisterGeneratedTypeReferences(context, varType);

            FunctionStack fnStack = context.GetCurrentFunction();

            string pointer = typeData.IsPointer ? " *" : " ";
            string declarationTypeName = QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context);

            List<string> newLines = [$"{declarationTypeName}{pointer}"];
            List<string> beforeDeclarationLines = new List<string>();

            FunctionStack? fn = context.GetCurrentFunction();
            bool isConstant = true;

            int start = context.DepthClass;

            for (int i = 0; i < declaration.Variables.Count; i++) {
                var variable = declaration.Variables[i];
                string name = variable.Identifier.ToString();
                newLines.Add(name);

                ConversionFunctionVariableUsage usage = fnStack.Function.BodyVariables.FirstOrDefault(c => c.Name == name);
                if ((usage != null && usage.Reassignment) ||
                    IsDeclaredLocalMutated(semantic, variable)) {
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
                    if (result.BeforeLines != null && result.BeforeLines.Count > 0) {
                        beforeDeclarationLines.AddRange(result.BeforeLines);
                    }
                }
            }

            context.PopClass(start);

            if (beforeDeclarationLines.Count > 0) {
                lines.AddRange(beforeDeclarationLines);
            }

            if (isConstant && typeData.IsNativeType && declaration.Parent is not ForStatementSyntax) {
                lines.Add("const ");
            }
            lines.AddRange(newLines);
        }

        static bool IsDeclaredLocalMutated(SemanticModel semantic, VariableDeclaratorSyntax variable) {
            if (semantic.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol) {
                return false;
            }

            SyntaxNode mutationScope = variable.FirstAncestorOrSelf<BlockSyntax>() ?? variable.SyntaxTree.GetRoot();
            foreach (AssignmentExpressionSyntax assignmentExpression in mutationScope.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
                if (SymbolsMatch(semantic.GetSymbolInfo(assignmentExpression.Left).Symbol, localSymbol)) {
                    return true;
                }
            }

            foreach (PrefixUnaryExpressionSyntax prefixExpression in mutationScope.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>()) {
                if ((prefixExpression.IsKind(SyntaxKind.PreIncrementExpression) ||
                     prefixExpression.IsKind(SyntaxKind.PreDecrementExpression)) &&
                    SymbolsMatch(semantic.GetSymbolInfo(prefixExpression.Operand).Symbol, localSymbol)) {
                    return true;
                }
            }

            foreach (PostfixUnaryExpressionSyntax postfixExpression in mutationScope.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>()) {
                if ((postfixExpression.IsKind(SyntaxKind.PostIncrementExpression) ||
                     postfixExpression.IsKind(SyntaxKind.PostDecrementExpression)) &&
                    SymbolsMatch(semantic.GetSymbolInfo(postfixExpression.Operand).Symbol, localSymbol)) {
                    return true;
                }
            }

            foreach (ArgumentSyntax argumentSyntax in mutationScope.DescendantNodes().OfType<ArgumentSyntax>()) {
                bool isRefOrOutArgument = argumentSyntax.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword) ||
                    argumentSyntax.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword);
                if (!isRefOrOutArgument &&
                    semantic.GetOperation(argumentSyntax) is IArgumentOperation argumentOperation &&
                    argumentOperation.Parameter != null &&
                    (argumentOperation.Parameter.RefKind == RefKind.Out ||
                     argumentOperation.Parameter.RefKind == RefKind.Ref)) {
                    isRefOrOutArgument = true;
                }

                if (!isRefOrOutArgument) {
                    continue;
                }

                if (SymbolsMatch(semantic.GetSymbolInfo(argumentSyntax.Expression).Symbol, localSymbol)) {
                    return true;
                }
            }

            return false;
        }

        static bool SymbolsMatch(ISymbol symbol, ILocalSymbol localSymbol) {
            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            return SymbolEqualityComparer.Default.Equals(symbol, localSymbol);
        }

        VariableType ResolveDeclarationType(SemanticModel semantic, VariableDeclarationSyntax declaration) {
            if (declaration.Type is IdentifierNameSyntax identifierName &&
                string.Equals(identifierName.Identifier.Text, "var", StringComparison.Ordinal) &&
                declaration.Variables.Count == 1) {
                VariableDeclaratorSyntax variable = declaration.Variables[0];
                if (semantic.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol) {
                    VariableType localType = VariableUtil.GetVarType(localSymbol.Type);
                    if (!IsWeakObjectVariableType(localType)) {
                        return localType;
                    }
                }

                if (variable.Initializer?.Value is ElementAccessExpressionSyntax elementAccessExpression &&
                    TryResolveElementAccessTypeSymbol(semantic, elementAccessExpression, out ITypeSymbol elementTypeSymbol)) {
                    return VariableUtil.GetVarType(elementTypeSymbol);
                }

                if (variable.Initializer?.Value is MemberAccessExpressionSyntax memberAccessExpression &&
                    TryResolveMemberAccessResultTypeSymbol(semantic, memberAccessExpression, out ITypeSymbol memberTypeSymbol)) {
                    return VariableUtil.GetVarType(memberTypeSymbol);
                }

                if (variable.Initializer?.Value != null &&
                    TryResolveInferredExpressionVariableType(null, semantic, variable.Initializer.Value, out VariableType inferredInitializerType) &&
                    !IsWeakObjectVariableType(inferredInitializerType)) {
                    return inferredInitializerType;
                }

                if (variable.Initializer?.Value != null &&
                    TryGetExpressionTypeSymbol(semantic, variable.Initializer.Value, out ITypeSymbol initializerTypeSymbol)) {
                    return VariableUtil.GetVarType(initializerTypeSymbol);
                }

                if (variable.Initializer?.Value is ConditionalExpressionSyntax conditionalInitializer) {
                    if (TryResolveNumericLiteralVariableType(conditionalInitializer.WhenTrue, out VariableType whenTrueLiteralType)) {
                        return whenTrueLiteralType;
                    }

                    if (TryResolveNumericLiteralVariableType(conditionalInitializer.WhenFalse, out VariableType whenFalseLiteralType)) {
                        return whenFalseLiteralType;
                    }

                    if (TryGetExpressionTypeSymbol(semantic, conditionalInitializer.WhenTrue, out ITypeSymbol whenTrueTypeSymbol) &&
                        !IsWeakRecoveredTypeSymbol(whenTrueTypeSymbol)) {
                        return VariableUtil.GetVarType(whenTrueTypeSymbol);
                    }

                    if (TryGetExpressionTypeSymbol(semantic, conditionalInitializer.WhenFalse, out ITypeSymbol whenFalseTypeSymbol) &&
                        !IsWeakRecoveredTypeSymbol(whenFalseTypeSymbol)) {
                        return VariableUtil.GetVarType(whenFalseTypeSymbol);
                    }
                }
            }

            return VariableUtil.GetVarType(declaration.Type, semantic);
        }

        bool TryProcessVarElementAccessDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            List<string> lines) {
            if (declaration.Type is not IdentifierNameSyntax identifierName ||
                !string.Equals(identifierName.Identifier.Text, "var", StringComparison.Ordinal) ||
                declaration.Variables.Count != 1) {
                return false;
            }

            VariableDeclaratorSyntax variable = declaration.Variables[0];
            if (variable.Initializer?.Value is not ElementAccessExpressionSyntax initializerExpression) {
                return false;
            }

            VariableType sourceType = null;
            if (semantic.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol) {
                sourceType = VariableUtil.GetVarType(localSymbol.Type);
            }

            if (sourceType == null ||
                IsWeakObjectVariableType(sourceType)) {
                if (TryResolveElementAccessTypeSymbol(semantic, initializerExpression, out ITypeSymbol elementTypeSymbol)) {
                    sourceType = VariableUtil.GetVarType(elementTypeSymbol);
                } else {
                    if (!TryGetExpressionTypeSymbol(semantic, initializerExpression, out ITypeSymbol initializerTypeSymbol)) {
                        return false;
                    }

                    sourceType = VariableUtil.GetVarType(initializerTypeSymbol);
                }
            }

            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);
            RegisterGeneratedTypeReferences(context, sourceType);

            FunctionStack fn = context.GetCurrentFunction();
            if (fn != null) {
                ConversionVariable conversionVariable = new ConversionVariable();
                conversionVariable.Name = variable.Identifier.ToString();
                conversionVariable.VarType = sourceType;
                fn.Stack.Add(conversionVariable);
            }

            string pointer = typeData.IsPointer ? " *" : " ";
            string declarationTypeName = QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context);
            lines.Add($"{declarationTypeName}{pointer}{variable.Identifier} = ");

            int start = context.DepthClass;
            ProcessExpression(semantic, context, initializerExpression, lines);
            context.PopClass(start);
            return true;
        }

        bool TryProcessVarMemberAccessDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            List<string> lines) {
            if (declaration.Type is not IdentifierNameSyntax identifierName ||
                !string.Equals(identifierName.Identifier.Text, "var", StringComparison.Ordinal) ||
                declaration.Variables.Count != 1) {
                return false;
            }

            VariableDeclaratorSyntax variable = declaration.Variables[0];
            if (variable.Initializer?.Value is not MemberAccessExpressionSyntax initializerExpression) {
                return false;
            }

            VariableType sourceType = null;
            if (semantic.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol) {
                sourceType = VariableUtil.GetVarType(localSymbol.Type);
            }

            if (sourceType == null || IsWeakObjectVariableType(sourceType)) {
                if (TryResolveMemberAccessResultTypeSymbol(semantic, initializerExpression, out ITypeSymbol memberTypeSymbol)) {
                    sourceType = VariableUtil.GetVarType(memberTypeSymbol);
                } else if (TryResolveTrackedExpressionVariableType(context, initializerExpression.Expression, out VariableType receiverType) &&
                    TryResolveTrackedMemberVariableType(receiverType, initializerExpression.Name.Identifier.Text, out VariableType trackedMemberType)) {
                    sourceType = trackedMemberType;
                } else if (TryGetExpressionTypeSymbol(semantic, initializerExpression, out ITypeSymbol initializerTypeSymbol)) {
                    sourceType = VariableUtil.GetVarType(initializerTypeSymbol);
                }
            }

            if (sourceType == null || IsWeakObjectVariableType(sourceType)) {
                return false;
            }

            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);
            RegisterGeneratedTypeReferences(context, sourceType);

            FunctionStack fn = context.GetCurrentFunction();
            if (fn != null) {
                ConversionVariable conversionVariable = new ConversionVariable();
                conversionVariable.Name = variable.Identifier.ToString();
                conversionVariable.VarType = sourceType;
                fn.Stack.Add(conversionVariable);
            }

            string pointer = typeData.IsPointer ? " *" : " ";
            string declarationTypeName = QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context);
            lines.Add($"{declarationTypeName}{pointer}{variable.Identifier} = ");

            int start = context.DepthClass;
            ProcessExpression(semantic, context, initializerExpression, lines);
            context.PopClass(start);
            return true;
        }

        bool TryProcessInferredVarDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            List<string> lines) {
            if (declaration.Type is not IdentifierNameSyntax identifierName ||
                !string.Equals(identifierName.Identifier.Text, "var", StringComparison.Ordinal) ||
                declaration.Variables.Count != 1) {
                return false;
            }

            VariableDeclaratorSyntax variable = declaration.Variables[0];
            if (variable.Initializer?.Value == null) {
                return false;
            }

            VariableType sourceType = null;
            if (semantic.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol) {
                sourceType = VariableUtil.GetVarType(localSymbol.Type);
            }

            if (sourceType == null || IsWeakObjectVariableType(sourceType)) {
                TryResolveInferredExpressionVariableType(context, semantic, variable.Initializer.Value, out sourceType);
            }

            if ((sourceType == null || IsWeakObjectVariableType(sourceType)) &&
                variable.Initializer.Value is ConditionalExpressionSyntax conditionalInitializer &&
                TryResolveInferredExpressionVariableType(context, semantic, conditionalInitializer.WhenTrue, out VariableType whenTrueInitializerType) &&
                !IsWeakObjectVariableType(whenTrueInitializerType)) {
                sourceType = whenTrueInitializerType;
            }

            if ((sourceType == null || IsWeakObjectVariableType(sourceType)) &&
                variable.Initializer.Value is ConditionalExpressionSyntax conditionalLiteralInitializer &&
                TryResolveNumericLiteralVariableType(conditionalLiteralInitializer.WhenTrue, out VariableType whenTrueLiteralType)) {
                sourceType = whenTrueLiteralType;
            }

            if (sourceType == null || IsWeakObjectVariableType(sourceType)) {
                List<string> probeLines = new List<string>();
                int probeStart = context.DepthClass;
                ExpressionResult probeResult = ProcessExpression(semantic, context, variable.Initializer.Value, probeLines);
                context.PopClass(probeStart);
                if (!IsWeakVariableType(probeResult.Type)) {
                    sourceType = probeResult.Type;
                }
            }

            if (sourceType == null || IsWeakObjectVariableType(sourceType)) {
                return false;
            }

            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);
            RegisterGeneratedTypeReferences(context, sourceType);

            FunctionStack fn = context.GetCurrentFunction();
            if (fn != null) {
                ConversionVariable conversionVariable = new ConversionVariable();
                conversionVariable.Name = variable.Identifier.ToString();
                conversionVariable.VarType = sourceType;
                fn.Stack.Add(conversionVariable);
            }

            string pointer = typeData.IsPointer ? " *" : " ";
            string declarationTypeName = QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context);
            lines.Add($"{declarationTypeName}{pointer}{variable.Identifier} = ");

            int start = context.DepthClass;
            ProcessExpression(semantic, context, variable.Initializer.Value, lines);
            context.PopClass(start);
            return true;
        }

        bool TryResolveInferredExpressionVariableType(
            LayerContext context,
            SemanticModel semantic,
            ExpressionSyntax expression,
            out VariableType variableType) {
            variableType = null;

            if (expression == null) {
                return false;
            }

            if (expression is ParenthesizedExpressionSyntax parenthesizedExpression) {
                return TryResolveInferredExpressionVariableType(context, semantic, parenthesizedExpression.Expression, out variableType);
            }

            if (TryResolveNumericLiteralVariableType(expression, out variableType)) {
                return true;
            }

            if (TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol expressionTypeSymbol) &&
                !IsWeakRecoveredTypeSymbol(expressionTypeSymbol)) {
                variableType = VariableUtil.GetVarType(expressionTypeSymbol);
                return !IsWeakObjectVariableType(variableType);
            }

            if (expression is MemberAccessExpressionSyntax memberAccessExpression &&
                TryResolveTrackedExpressionVariableType(context, memberAccessExpression.Expression, out VariableType receiverType) &&
                TryResolveTrackedMemberVariableType(receiverType, memberAccessExpression.Name.Identifier.Text, out VariableType trackedMemberType)) {
                variableType = trackedMemberType;
                return !IsWeakObjectVariableType(variableType);
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnaryExpression) {
                return TryResolveInferredExpressionVariableType(context, semantic, prefixUnaryExpression.Operand, out variableType);
            }

            if (expression is CastExpressionSyntax castExpression) {
                variableType = VariableUtil.GetVarType(castExpression.Type, semantic);
                return !IsWeakObjectVariableType(variableType);
            }

            if (expression is InvocationExpressionSyntax invocationExpression) {
                if (invocationExpression.Expression is MemberAccessExpressionSyntax getHashCodeMemberAccess &&
                    getHashCodeMemberAccess.Name is IdentifierNameSyntax getHashCodeIdentifier &&
                    string.Equals(getHashCodeIdentifier.Identifier.Text, "GetHashCode", StringComparison.Ordinal) &&
                    invocationExpression.ArgumentList.Arguments.Count == 0) {
                    variableType = VariableUtil.GetVarType("int");
                    return true;
                }

                IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
                if (invokedMethodSymbol?.ReturnType != null &&
                    !IsWeakRecoveredTypeSymbol(invokedMethodSymbol.ReturnType)) {
                    variableType = VariableUtil.GetVarType(invokedMethodSymbol.ReturnType);
                    return !IsWeakObjectVariableType(variableType);
                }
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryResolveBestCommonTypeSymbol(semantic, conditionalExpression.WhenTrue, conditionalExpression.WhenFalse, out ITypeSymbol conditionalTypeSymbol) &&
                !IsWeakRecoveredTypeSymbol(conditionalTypeSymbol)) {
                variableType = VariableUtil.GetVarType(conditionalTypeSymbol);
                return !IsWeakObjectVariableType(variableType);
            }

            if (expression is ConditionalExpressionSyntax singleStrongConditionalExpression) {
                bool hasWhenTrueType = TryResolveInferredExpressionVariableType(context, semantic, singleStrongConditionalExpression.WhenTrue, out VariableType whenTrueOnlyType);
                bool hasWhenFalseType = TryResolveInferredExpressionVariableType(context, semantic, singleStrongConditionalExpression.WhenFalse, out VariableType whenFalseOnlyType);
                if (hasWhenTrueType && !hasWhenFalseType) {
                    variableType = whenTrueOnlyType;
                    return !IsWeakObjectVariableType(variableType);
                }

                if (!hasWhenTrueType && hasWhenFalseType) {
                    variableType = whenFalseOnlyType;
                    return !IsWeakObjectVariableType(variableType);
                }
            }

            if (expression is ConditionalExpressionSyntax conditionalExpressionSyntax &&
                TryResolveInferredExpressionVariableType(context, semantic, conditionalExpressionSyntax.WhenTrue, out VariableType whenTrueType) &&
                TryResolveInferredExpressionVariableType(context, semantic, conditionalExpressionSyntax.WhenFalse, out VariableType whenFalseType)) {
                variableType = ChoosePreferredVariableType(whenTrueType, whenFalseType);
                return !IsWeakObjectVariableType(variableType);
            }

            if (expression is BinaryExpressionSyntax binaryExpression &&
                TryResolveBestCommonTypeSymbol(semantic, binaryExpression.Left, binaryExpression.Right, out ITypeSymbol binaryTypeSymbol) &&
                !IsWeakRecoveredTypeSymbol(binaryTypeSymbol)) {
                variableType = VariableUtil.GetVarType(binaryTypeSymbol);
                return !IsWeakObjectVariableType(variableType);
            }

            if (expression is BinaryExpressionSyntax binaryExpressionSyntax &&
                TryResolveInferredExpressionVariableType(context, semantic, binaryExpressionSyntax.Left, out VariableType leftType) &&
                TryResolveInferredExpressionVariableType(context, semantic, binaryExpressionSyntax.Right, out VariableType rightType)) {
                variableType = ChoosePreferredVariableType(leftType, rightType);
                return !IsWeakObjectVariableType(variableType);
            }

            return false;
        }

        VariableType ChoosePreferredVariableType(VariableType leftType, VariableType rightType) {
            if (leftType == null) {
                return rightType;
            }

            if (rightType == null) {
                return leftType;
            }

            if (leftType.Type == rightType.Type &&
                string.Equals(leftType.TypeName, rightType.TypeName, StringComparison.Ordinal)) {
                return leftType;
            }

            VariableDataType[] precedence = new[] {
                VariableDataType.Double,
                VariableDataType.Single,
                VariableDataType.Int64,
                VariableDataType.UInt64,
                VariableDataType.Int32,
                VariableDataType.UInt32,
                VariableDataType.Int16,
                VariableDataType.UInt16,
                VariableDataType.Int8,
                VariableDataType.UInt8
            };

            foreach (VariableDataType preferredType in precedence) {
                if (leftType.Type == preferredType || rightType.Type == preferredType) {
                    return leftType.Type == preferredType ? leftType : rightType;
                }
            }

            return leftType;
        }

        static bool TryResolveNumericLiteralVariableType(ExpressionSyntax expression, out VariableType variableType) {
            variableType = null;

            if (expression is PrefixUnaryExpressionSyntax prefixUnaryExpression &&
                (prefixUnaryExpression.IsKind(SyntaxKind.UnaryMinusExpression) ||
                 prefixUnaryExpression.IsKind(SyntaxKind.UnaryPlusExpression))) {
                return TryResolveNumericLiteralVariableType(prefixUnaryExpression.Operand, out variableType);
            }

            if (expression is not LiteralExpressionSyntax literalExpression) {
                return false;
            }

            string literalText = literalExpression.Token.Text;
            if (literalText.EndsWith("f", StringComparison.OrdinalIgnoreCase)) {
                variableType = VariableUtil.GetVarType("float");
                return true;
            }

            if (literalText.Contains('.', StringComparison.Ordinal) ||
                literalText.EndsWith("d", StringComparison.OrdinalIgnoreCase)) {
                variableType = VariableUtil.GetVarType("double");
                return true;
            }

            if (literalExpression.IsKind(SyntaxKind.NumericLiteralExpression)) {
                variableType = VariableUtil.GetVarType("int");
                return true;
            }

            return false;
        }

        static bool IsWeakObjectVariableType(VariableType variableType) {
            return variableType != null &&
                variableType.Type == VariableDataType.Object &&
                (string.IsNullOrWhiteSpace(variableType.TypeName) ||
                 string.Equals(variableType.TypeName, "object", StringComparison.Ordinal)) &&
                (variableType.GenericArgs == null || variableType.GenericArgs.Count == 0);
        }

        void RegisterGeneratedTypeReferences(LayerContext context, VariableType variableType) {
            if (context == null || variableType == null) {
                return;
            }

            RegisterGeneratedTypeReference(context, variableType.TypeName, variableType.GenericArgs.Count);

            if (variableType.GenericArgs == null) {
                return;
            }

            foreach (VariableType genericArgument in variableType.GenericArgs) {
                RegisterGeneratedTypeReferences(context, genericArgument);
            }
        }

        void RegisterGeneratedTypeReference(LayerContext context, string typeName, int genericArgCount) {
            if (string.IsNullOrWhiteSpace(typeName)) {
                return;
            }

            string normalizedTypeName = typeName;
            int lastDotIndex = normalizedTypeName.LastIndexOf('.');
            if (lastDotIndex >= 0 && lastDotIndex < normalizedTypeName.Length - 1) {
                normalizedTypeName = normalizedTypeName[(lastDotIndex + 1)..];
            }

            int nestedSeparatorIndex = normalizedTypeName.LastIndexOf('+');
            if (nestedSeparatorIndex >= 0 && nestedSeparatorIndex < normalizedTypeName.Length - 1) {
                normalizedTypeName = normalizedTypeName[(nestedSeparatorIndex + 1)..];
            }

            ConversionClass generatedClass = context.Program.FindGeneratedClass(normalizedTypeName, genericArgCount);
            if (generatedClass == null) {
                return;
            }

            ConversionClass currentClass = context.GetCurrentClass();
            if (currentClass == null || currentClass.ReferencedClasses.Contains(generatedClass.Name)) {
                return;
            }

            currentClass.ReferencedClasses.Add(generatedClass.Name);
        }

        string QualifyRenderedCppTypeName(string renderedTypeName, LayerContext context) {
            if (string.IsNullOrWhiteSpace(renderedTypeName) || context?.Program?.Classes == null) {
                return renderedTypeName;
            }

            string qualifiedTypeName = renderedTypeName;
            foreach (ConversionClass generatedClass in context.Program.Classes) {
                string generatedTypeName = generatedClass.GetEmittedTypeName();
                qualifiedTypeName = Regex.Replace(
                    qualifiedTypeName,
                    $@"(?<!:)\b{Regex.Escape(generatedTypeName)}\b",
                    $"::{generatedTypeName}");
            }

            return qualifiedTypeName;
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

            FunctionStack currentFunction = context.GetCurrentFunction();
            if (currentFunction != null) {
                ConversionVariable stackVariable = new ConversionVariable();
                stackVariable.Name = variable.Identifier.ToString();
                stackVariable.VarType = declarationType;
                currentFunction.Stack.Add(stackVariable);
            }

            CPPTypeData declarationTypeData;
            VariableType cppDeclarationType = ConvertToCPPType(declarationType, out declarationTypeData);
            RegisterGeneratedTypeReferences(context, declarationType);

            string pointer = declarationTypeData.IsPointer ? " *" : " ";
            string declarationTypeName = QualifyRenderedCppTypeName(cppDeclarationType.ToCPPString(context.Program), context);
            lines.Add($"{declarationTypeName}{pointer}{variable.Identifier.Text} = ");
            ProcessCollectionExpression(semantic, context, collectionExpression, lines);
            return true;
        }

        bool TryProcessSplitMultiVariableDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            VariableType varType,
            List<string> lines) {
            if (declaration.Variables.Count <= 1 ||
                declaration.Parent is not LocalDeclarationStatementSyntax) {
                return false;
            }

            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(varType, out typeData);
            RegisterGeneratedTypeReferences(context, varType);

            string pointer = typeData.IsPointer ? " *" : " ";
            string declarationTypeName = QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context);
            FunctionStack fnStack = context.GetCurrentFunction();
            FunctionStack fn = context.GetCurrentFunction();

            for (int index = 0; index < declaration.Variables.Count; index++) {
                VariableDeclaratorSyntax variable = declaration.Variables[index];
                bool isConstant = true;
                List<string> declarationLines = [$"{declarationTypeName}{pointer}", variable.Identifier.Text];
                List<string> beforeDeclarationLines = new List<string>();

                ConversionFunctionVariableUsage usage = fnStack?.Function?.BodyVariables.FirstOrDefault(candidate => candidate.Name == variable.Identifier.Text);
                if ((usage != null && usage.Reassignment) ||
                    IsDeclaredLocalMutated(semantic, variable)) {
                    isConstant = false;
                }

                if (typeData.IsArray &&
                    cppType.Type != VariableDataType.String &&
                    !string.Equals(cppType.TypeName, "std::string", StringComparison.Ordinal)) {
                    declarationLines.Add("[]");
                }

                if (fn != null) {
                    ConversionVariable stackVariable = new ConversionVariable();
                    stackVariable.Name = variable.Identifier.Text;
                    stackVariable.VarType = varType;
                    fn.Stack.Add(stackVariable);
                }

                if (variable.Initializer != null) {
                    declarationLines.Add(" = ");
                    ExpressionResult result = ProcessExpression(semantic, context, variable.Initializer.Value, declarationLines);
                    if (result.BeforeLines != null && result.BeforeLines.Count > 0) {
                        beforeDeclarationLines.AddRange(result.BeforeLines);
                    }
                }

                if (beforeDeclarationLines.Count > 0) {
                    lines.AddRange(beforeDeclarationLines);
                }

                if (isConstant && typeData.IsNativeType) {
                    lines.Add("const ");
                }

                lines.AddRange(declarationLines);
                if (index < declaration.Variables.Count - 1) {
                    lines.Add(";\n");
                }
            }

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
            if (arrayCreation.Type.RankSpecifiers.Count == 1) {
                string elementTypeName = GetCppTypeToken(elementType, context.Program);
                lines.Add($"new Array<{elementTypeName}>(");
                ProcessExpression(semantic, context, outerRankSpecifier.Sizes[0], lines);
                lines.Add(")");
                return true;
            }

            VariableType nestedElementType = elementType;
            for (int i = 1; i < arrayCreation.Type.RankSpecifiers.Count; i++) {
                VariableType arrayWrapper = new VariableType(VariableDataType.Array, "Array");
                arrayWrapper.GenericArgs.Add(nestedElementType);
                nestedElementType = arrayWrapper;
            }

            string nestedElementTypeName = GetCppTypeToken(nestedElementType, context.Program);
            lines.Add($"new Array<{nestedElementTypeName}>(");
            ProcessExpression(semantic, context, outerRankSpecifier.Sizes[0], lines);
            lines.Add(")");
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

            if (literalText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                literalText.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) {
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

            if (TryAppendArrayAsListReturnValue(semantic, context, ret, returnLines, lines)) {
                context.PopClass(start);
                return;
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

        bool TryAppendArrayAsListReturnValue(
            SemanticModel semantic,
            LayerContext context,
            ReturnStatementSyntax ret,
            List<string> returnLines,
            List<string> lines) {
            ITypeSymbol returnTypeSymbol = ResolveEnclosingReturnTypeSymbol(semantic, ret);
            if (!IsListFamilyTypeSymbol(returnTypeSymbol) ||
                !TryResolveArrayElementTypeSymbol(semantic, ret.Expression, out ITypeSymbol arrayElementTypeSymbol)) {
                return false;
            }

            RegisterRuntimeRequirement("NativeList");
            string elementTypeName = GetCppTypeToken(VariableUtil.GetVarType(arrayElementTypeSymbol), context.Program);
            lines.Add($"return new List<{elementTypeName}>(");
            lines.AddRange(returnLines);
            lines.Add(");");
            return true;
        }

        static ITypeSymbol ResolveEnclosingReturnTypeSymbol(SemanticModel semantic, SyntaxNode node) {
            ISymbol enclosingSymbol = semantic?.GetEnclosingSymbol(node?.SpanStart ?? 0);
            if (enclosingSymbol is IMethodSymbol methodSymbol) {
                if (methodSymbol.AssociatedSymbol is IPropertySymbol propertySymbol) {
                    return propertySymbol.Type;
                }

                return methodSymbol.ReturnType;
            }

            if (enclosingSymbol is IPropertySymbol directPropertySymbol) {
                return directPropertySymbol.Type;
            }

            for (SyntaxNode currentNode = node?.Parent; currentNode != null; currentNode = currentNode.Parent) {
                if (currentNode is PropertyDeclarationSyntax propertyDeclaration) {
                    TypeInfo propertyTypeInfo = semantic.GetTypeInfo(propertyDeclaration.Type);
                    return propertyTypeInfo.Type ?? propertyTypeInfo.ConvertedType;
                }

                if (currentNode is MethodDeclarationSyntax methodDeclaration) {
                    TypeInfo methodTypeInfo = semantic.GetTypeInfo(methodDeclaration.ReturnType);
                    return methodTypeInfo.Type ?? methodTypeInfo.ConvertedType;
                }
            }

            return null;
        }

        protected override ExpressionResult ProcessDeclarationExpressionSyntax(SemanticModel semantic, LayerContext context, DeclarationExpressionSyntax declaration, List<string> lines) {
            if (declaration.Designation is SingleVariableDesignationSyntax single) {
                string identifier = single.Identifier.Text;
                lines.Add(identifier);

                ITypeSymbol inferredOutTypeSymbol = TryResolveOutArgumentTypeSymbol(semantic, declaration, null);
                VariableType variableType = inferredOutTypeSymbol != null
                    ? VariableUtil.GetVarType(inferredOutTypeSymbol)
                    : ResolveDeclarationExpressionVariableType(semantic, declaration, single);
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

        static VariableType ResolveDeclarationExpressionVariableType(
            SemanticModel semantic,
            DeclarationExpressionSyntax declarationExpression,
            SingleVariableDesignationSyntax designation) {
            ITypeSymbol declarationTypeSymbol = semantic.GetTypeInfo(declarationExpression).ConvertedType ?? semantic.GetTypeInfo(declarationExpression).Type;
            if (declarationTypeSymbol != null &&
                declarationTypeSymbol.TypeKind != TypeKind.Error) {
                return VariableUtil.GetVarType(declarationTypeSymbol);
            }

            if (semantic.GetDeclaredSymbol(designation) is ILocalSymbol localSymbol) {
                return VariableUtil.GetVarType(localSymbol.Type);
            }

            return VariableUtil.GetVarType(declarationExpression.Type, semantic);
        }

        ITypeSymbol TryResolveOutArgumentTypeSymbol(
            SemanticModel semantic,
            ExpressionSyntax expression,
            IParameterSymbol parameterSymbol) {
            ITypeSymbol inferredOutTypeSymbol = parameterSymbol?.Type;
            if (inferredOutTypeSymbol == null &&
                expression.Parent is ArgumentSyntax argumentSyntax &&
                semantic.GetOperation(argumentSyntax) is IArgumentOperation argumentOperation) {
                inferredOutTypeSymbol = argumentOperation.Parameter?.Type;
            }

            if (inferredOutTypeSymbol == null &&
                expression.Parent is ArgumentSyntax containingArgument &&
                containingArgument.Parent is BaseArgumentListSyntax containingArgumentList &&
                containingArgumentList.Parent is InvocationExpressionSyntax containingInvocation) {
                int argumentIndex = containingArgumentList.Arguments.IndexOf(containingArgument);
                if (semantic.GetOperation(containingInvocation) is IInvocationOperation containingInvocationOperation &&
                    containingInvocationOperation.TargetMethod != null &&
                    argumentIndex >= 0 &&
                    argumentIndex < containingInvocationOperation.TargetMethod.Parameters.Length) {
                    inferredOutTypeSymbol = containingInvocationOperation.TargetMethod.Parameters[argumentIndex].Type;
                }

                IMethodSymbol containingMethodSymbol = inferredOutTypeSymbol == null
                    ? ResolveInvokedMethodSymbol(semantic, containingInvocation)
                    : null;
                if (inferredOutTypeSymbol == null &&
                    containingMethodSymbol != null &&
                    argumentIndex >= 0 &&
                    argumentIndex < containingMethodSymbol.Parameters.Length) {
                    inferredOutTypeSymbol = containingMethodSymbol.Parameters[argumentIndex].Type;
                }

                if (inferredOutTypeSymbol == null &&
                    argumentIndex == 1 &&
                    containingInvocation.Expression is MemberAccessExpressionSyntax dictionaryMemberAccess &&
                    string.Equals(dictionaryMemberAccess.Name.Identifier.Text, "TryGetValue", StringComparison.Ordinal) &&
                    TryGetExpressionTypeSymbol(semantic, dictionaryMemberAccess.Expression, out ITypeSymbol receiverTypeSymbol) &&
                    receiverTypeSymbol is INamedTypeSymbol receiverNamedTypeSymbol &&
                    IsDictionaryTypeSymbol(receiverNamedTypeSymbol) &&
                    receiverNamedTypeSymbol.TypeArguments.Length >= 2) {
                    inferredOutTypeSymbol = receiverNamedTypeSymbol.TypeArguments[1];
                }
            }

            return inferredOutTypeSymbol;
        }

        static bool TryBuildPropertyGetterCall(
            SyntaxNode accessNode,
            IPropertySymbol propertySymbol,
            out string getterCallName) {
            getterCallName = string.Empty;

            if (accessNode == null ||
                propertySymbol == null ||
                propertySymbol.IsIndexer ||
                propertySymbol.GetMethod == null ||
                !RequiresPropertyAccessorLowering(propertySymbol) ||
                IsPropertyWriteContext(accessNode)) {
                return false;
            }

            foreach (SyntaxReference syntaxReference in propertySymbol.DeclaringSyntaxReferences) {
                if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax propertyDeclaration) {
                    continue;
                }

                if (propertyDeclaration.ExpressionBody != null) {
                    getterCallName = $"get_{propertySymbol.Name}()";
                    return true;
                }

                if (propertyDeclaration.AccessorList == null) {
                    getterCallName = $"get_{propertySymbol.Name}()";
                    return true;
                }

                foreach (AccessorDeclarationSyntax accessorDeclaration in propertyDeclaration.AccessorList.Accessors) {
                    if (accessorDeclaration.Kind() == SyntaxKind.GetAccessorDeclaration &&
                        (accessorDeclaration.Body != null || accessorDeclaration.ExpressionBody != null)) {
                        getterCallName = $"get_{propertySymbol.Name}()";
                        return true;
                    }
                }
            }

            if (propertySymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                propertySymbol.IsAbstract) {
                getterCallName = $"get_{propertySymbol.Name}()";
                return true;
            }

            return false;
        }

        bool TryResolveGeneratedPropertyGetter(
            LayerContext context,
            SemanticModel semantic,
            ExpressionSyntax receiverExpression,
            string propertyName,
            out VariableType propertyType) {
            propertyType = null;

            if (!TryResolveGeneratedReceiverClass(context, semantic, receiverExpression, out ConversionClass receiverClass)) {
                return false;
            }

            ConversionVariable propertyVariable = receiverClass.Variables.LastOrDefault(candidate => candidate.Name == propertyName && candidate.IsGet);
            if (propertyVariable?.VarType != null) {
                propertyType = new VariableType(propertyVariable.VarType);
                return true;
            }

            if (!TryResolveGeneratedPropertyAccessor(receiverClass, propertyName, "get_", out ConversionFunction accessorFunction) ||
                accessorFunction.ReturnType == null) {
                return false;
            }

            propertyType = new VariableType(accessorFunction.ReturnType);
            return true;
        }

        bool TryResolveGeneratedAssignedPropertyName(
            LayerContext context,
            SemanticModel semantic,
            ExpressionSyntax assignmentLeft,
            out string propertyName) {
            propertyName = string.Empty;

            if (assignmentLeft is IdentifierNameSyntax identifierName &&
                TryResolveGeneratedPropertySetter(context, semantic, assignmentLeft, identifierName.Identifier.Text)) {
                propertyName = identifierName.Identifier.Text;
                return true;
            }

            if (assignmentLeft is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is IdentifierNameSyntax memberIdentifier &&
                TryResolveGeneratedPropertySetter(context, semantic, memberAccess.Expression, memberIdentifier.Identifier.Text)) {
                propertyName = memberIdentifier.Identifier.Text;
                return true;
            }

            return false;
        }

        bool TryAppendObjectInitializerSetterAssignment(
            SemanticModel semantic,
            LayerContext context,
            ObjectCreationExpressionSyntax objectCreation,
            string objectName,
            string memberAccessOperator,
            AssignmentExpressionSyntax assignment,
            List<string> lines) {
            string propertyName = string.Empty;

            if (TryGetAssignedPropertySymbol(semantic, assignment.Left, out IPropertySymbol propertySymbol) &&
                propertySymbol.SetMethod != null) {
                propertyName = propertySymbol.Name;
            } else if (assignment.Left is IdentifierNameSyntax identifierName &&
                TryResolveGeneratedPropertySetter(semantic, objectCreation.Type, identifierName.Identifier.Text)) {
                propertyName = identifierName.Identifier.Text;
            }

            if (string.IsNullOrWhiteSpace(propertyName)) {
                return false;
            }

            lines.Add(objectName);
            lines.Add(memberAccessOperator);
            lines.Add($"set_{propertyName}(");

            int startRight = context.DepthClass;
            ProcessExpression(semantic, context, assignment.Right, lines);
            context.PopClass(startRight);

            lines.Add(");\n");
            return true;
        }

        bool TryResolveGeneratedPropertyAccessor(
            LayerContext context,
            SemanticModel semantic,
            ExpressionSyntax receiverExpression,
            string propertyName,
            string accessorPrefix,
            out ConversionFunction accessorFunction) {
            accessorFunction = null;

            if (!TryResolveGeneratedReceiverClass(context, semantic, receiverExpression, out ConversionClass receiverClass)) {
                return false;
            }

            return TryResolveGeneratedPropertyAccessor(receiverClass, propertyName, accessorPrefix, out accessorFunction);
        }

        bool TryResolveGeneratedPropertyAccessor(
            SemanticModel semantic,
            TypeSyntax receiverTypeSyntax,
            string propertyName,
            string accessorPrefix,
            out ConversionFunction accessorFunction) {
            accessorFunction = null;

            ITypeSymbol receiverTypeSymbol = semantic.GetTypeInfo(receiverTypeSyntax).Type ?? semantic.GetTypeInfo(receiverTypeSyntax).ConvertedType;
            if (receiverTypeSymbol == null) {
                return false;
            }

            ConversionClass receiverClass = ResolveGeneratedClass(VariableUtil.GetVarType(receiverTypeSymbol));
            if (receiverClass == null) {
                return false;
            }

            return TryResolveGeneratedPropertyAccessor(receiverClass, propertyName, accessorPrefix, out accessorFunction);
        }

        static bool TryResolveGeneratedPropertyAccessor(
            ConversionClass receiverClass,
            string propertyName,
            string accessorPrefix,
            out ConversionFunction accessorFunction) {
            accessorFunction = receiverClass?.Functions?.LastOrDefault(candidate => candidate.Name == $"{accessorPrefix}{propertyName}");
            return accessorFunction != null;
        }

        bool TryResolveGeneratedPropertySetter(
            LayerContext context,
            SemanticModel semantic,
            ExpressionSyntax receiverExpression,
            string propertyName) {
            if (!TryResolveGeneratedReceiverClass(context, semantic, receiverExpression, out ConversionClass receiverClass)) {
                return false;
            }

            if (receiverClass.Variables.Any(candidate => candidate.Name == propertyName && candidate.IsSet)) {
                return true;
            }

            return TryResolveGeneratedPropertyAccessor(receiverClass, propertyName, "set_", out _);
        }

        bool TryResolveGeneratedPropertySetter(
            SemanticModel semantic,
            TypeSyntax receiverTypeSyntax,
            string propertyName) {
            ITypeSymbol receiverTypeSymbol = semantic.GetTypeInfo(receiverTypeSyntax).Type ?? semantic.GetTypeInfo(receiverTypeSyntax).ConvertedType;
            if (receiverTypeSymbol == null) {
                return false;
            }

            ConversionClass receiverClass = ResolveGeneratedClass(VariableUtil.GetVarType(receiverTypeSymbol));
            if (receiverClass == null) {
                return false;
            }

            if (receiverClass.Variables.Any(candidate => candidate.Name == propertyName && candidate.IsSet)) {
                return true;
            }

            return TryResolveGeneratedPropertyAccessor(receiverClass, propertyName, "set_", out _);
        }

        bool TryResolveGeneratedReceiverClass(
            LayerContext context,
            SemanticModel semantic,
            ExpressionSyntax receiverExpression,
            out ConversionClass receiverClass) {
            receiverClass = null;

            VariableType receiverType = null;
            if (!TryResolveTrackedExpressionVariableType(context, receiverExpression, out receiverType)) {
                if (!TryGetExpressionTypeSymbol(semantic, receiverExpression, out ITypeSymbol receiverTypeSymbol)) {
                    return false;
                }

                receiverType = VariableUtil.GetVarType(receiverTypeSymbol);
            }

            receiverClass = ResolveGeneratedClass(receiverType);
            return receiverClass != null;
        }

        static bool RequiresPropertyAccessorLowering(IPropertySymbol propertySymbol) {
            if (propertySymbol == null ||
                propertySymbol.IsIndexer) {
                return false;
            }

            if (propertySymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                propertySymbol.IsAbstract) {
                return true;
            }

            foreach (SyntaxReference syntaxReference in propertySymbol.DeclaringSyntaxReferences) {
                if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax propertyDeclaration) {
                    continue;
                }

                if (propertyDeclaration.ExpressionBody != null) {
                    return true;
                }

                if (propertyDeclaration.AccessorList == null) {
                    continue;
                }

                foreach (AccessorDeclarationSyntax accessorDeclaration in propertyDeclaration.AccessorList.Accessors) {
                    if (accessorDeclaration.Body != null ||
                        accessorDeclaration.ExpressionBody != null) {
                        return true;
                    }
                }
            }

            return false;
        }

        static bool IsPropertyWriteContext(SyntaxNode accessNode) {
            if (accessNode?.Parent is AssignmentExpressionSyntax directAssignmentExpression &&
                ReferenceEquals(directAssignmentExpression.Left, accessNode)) {
                return true;
            }

            if (accessNode?.Parent is MemberAccessExpressionSyntax memberAccess) {
                if (memberAccess.Parent is AssignmentExpressionSyntax memberAssignmentExpression &&
                    ReferenceEquals(memberAssignmentExpression.Left, memberAccess)) {
                        return true;
                }

                if (memberAccess.Parent is PrefixUnaryExpressionSyntax memberPrefixUnaryExpression &&
                    (memberPrefixUnaryExpression.IsKind(SyntaxKind.PreIncrementExpression) ||
                     memberPrefixUnaryExpression.IsKind(SyntaxKind.PreDecrementExpression))) {
                    return true;
                }

                if (memberAccess.Parent is PostfixUnaryExpressionSyntax memberPostfixUnaryExpression &&
                    (memberPostfixUnaryExpression.IsKind(SyntaxKind.PostIncrementExpression) ||
                     memberPostfixUnaryExpression.IsKind(SyntaxKind.PostDecrementExpression))) {
                    return true;
                }
            }

            if (accessNode?.Parent is PrefixUnaryExpressionSyntax directPrefixUnaryExpression &&
                (directPrefixUnaryExpression.IsKind(SyntaxKind.PreIncrementExpression) ||
                 directPrefixUnaryExpression.IsKind(SyntaxKind.PreDecrementExpression))) {
                return true;
            }

            if (accessNode?.Parent is PostfixUnaryExpressionSyntax directPostfixUnaryExpression &&
                (directPostfixUnaryExpression.IsKind(SyntaxKind.PostIncrementExpression) ||
                 directPostfixUnaryExpression.IsKind(SyntaxKind.PostDecrementExpression))) {
                return true;
            }

            return false;
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
