using cs2.core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace cs2.cpp {
    public class CPPConversiorProcessor : ConversionProcessor {
        private CPPCodeConverter codeConverter;
        private int temporaryNameCounter;

        public CPPConversiorProcessor(CPPCodeConverter converter) {
            codeConverter = converter;
        }

        /// <summary>
        /// Gets the runtime requirement registrar for the active conversion run.
        /// </summary>
        public CPPRuntimeRequirementRegistrar RuntimeRequirementRegistrar => codeConverter?.RuntimeRequirementRegistrar;

        /// <summary>
        /// Gets the active conversion options for the current backend run.
        /// </summary>
        public CPPConversionOptions Options => codeConverter?.Options;

        /// <summary>
        /// Begins runtime-helper tracking for the currently emitted type when a converter-backed registrar is available.
        /// </summary>
        /// <returns>The active type scope, or an empty scope when no converter-backed registrar is available.</returns>
        public CPPTypeRuntimeRequirementScope BeginTypeRuntimeRequirementScope() {
            if (codeConverter?.RuntimeRequirementRegistrar == null) {
                return new CPPTypeRuntimeRequirementScope();
            }

            return codeConverter.RuntimeRequirementRegistrar.BeginTypeScope();
        }

        /// <summary>
        /// Ends runtime-helper tracking for the currently emitted type when a converter-backed registrar is available.
        /// </summary>
        /// <param name="typeScope">Type scope to stop tracking.</param>
        public void EndTypeRuntimeRequirementScope(CPPTypeRuntimeRequirementScope typeScope) {
            if (codeConverter?.RuntimeRequirementRegistrar == null) {
                return;
            }

            codeConverter.RuntimeRequirementRegistrar.EndTypeScope(typeScope);
        }

        /// <summary>
        /// Generates a stable, compiler-safe temporary name for lowered expressions.
        /// </summary>
        /// <param name="prefix">Prefix used to describe the temporary.</param>
        /// <returns>A unique identifier that stays within valid C++ identifier syntax.</returns>
        string CreateTemporaryName(string prefix) {
            int uniqueIndex = temporaryNameCounter++;
            return $"{prefix}_{uniqueIndex:X8}";
        }

        /// <summary>
        /// Tries to resolve a runtime requirement definition by name when a converter-backed registrar is available.
        /// </summary>
        /// <param name="name">Stable runtime requirement name.</param>
        /// <param name="definition">Resolved runtime requirement definition when found.</param>
        /// <returns>True when the requirement was resolved.</returns>
        public bool TryGetRuntimeRequirementDefinition(string name, out CPPRuntimeRequirementDefinition definition) {
            definition = null;
            if (codeConverter?.RuntimeRequirementRegistrar == null) {
                return false;
            }

            return codeConverter.RuntimeRequirementRegistrar.TryGet(name, out definition);
        }

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

            if (expression is IsPatternExpressionSyntax isPatternExpression &&
                TryProcessDeclarationPatternExpression(semantic, context, isPatternExpression, lines, out ExpressionResult declarationPatternResult)) {
                return declarationPatternResult;
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

        ExpressionResult ProcessAsTypeExpression(
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
            lines.Add(")");
            return new ExpressionResult(true, VariablePath.Unknown, targetType);
        }

        bool TryProcessDeclarationPatternExpression(
            SemanticModel semantic,
            LayerContext context,
            IsPatternExpressionSyntax isPatternExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (isPatternExpression.Pattern is not DeclarationPatternSyntax declarationPattern ||
                declarationPattern.Designation is not SingleVariableDesignationSyntax designation) {
                return false;
            }

            VariableType declaredType = VariableUtil.GetVarType(declarationPattern.Type, semantic);
            CPPTypeData declaredTypeData;
            VariableType cppDeclaredType = ConvertToCPPType(declaredType, out declaredTypeData);

            List<string> sourceLines = new List<string>();
            int sourceStart = context.DepthClass;
            ExpressionResult sourceResult = ProcessExpression(semantic, context, isPatternExpression.Expression, sourceLines);
            context.PopClass(sourceStart);
            if (!sourceResult.Processed) {
                result = sourceResult;
                return true;
            }

            string declaredTypeName = cppDeclaredType.ToCPPString(context.Program);
            string variableName = designation.Identifier.Text;
            string sourceExpression = string.Concat(sourceLines);
            List<string> beforeLines = sourceResult.BeforeLines != null
                ? new List<string>(sourceResult.BeforeLines)
                : new List<string>();

            FunctionStack currentFunction = context.GetCurrentFunction();
            ConversionVariable conversionVariable = null;
            if (currentFunction != null) {
                conversionVariable = new ConversionVariable();
                conversionVariable.Name = variableName;
                conversionVariable.VarType = declaredType;
            }

            if (declaredTypeData.IsPointer || IsReferencePatternType(semantic, declarationPattern.Type, declaredType)) {
                RegisterRuntimeRequirement("NativeCast");

                beforeLines.Add($"{declaredTypeName}* {variableName} = he_cpp_try_cast<{declaredTypeName}>({sourceExpression});\n");
                lines.Add($"{variableName} != nullptr");
            } else {
                string pointerName = $"__pattern_{variableName}";

                beforeLines.Add($"{declaredTypeName}* {pointerName} = static_cast<{declaredTypeName}*>({sourceExpression});\n");
                if (conversionVariable != null) {
                    conversionVariable.Remap = $"(*{pointerName})";
                }

                lines.Add($"{pointerName} != nullptr");
            }

            if (conversionVariable != null) {
                currentFunction.Stack.Add(conversionVariable);
            }

            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"), beforeLines);
            if (conversionVariable != null) {
                result.Variable = conversionVariable;
            }

            return true;
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
                if (statement is BlockSyntax nestedBlock) {
                    lines.Add("{\n");
                    ProcessBlock(semantic, context, nestedBlock, lines, depth);
                    lines.Add("}\n");
                    continue;
                }

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

            string disposalTarget = localDeclaration.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? string.Empty;
            bool disposalUsesPointerAccess = IsPointerDeclaration(semantic, localDeclaration.Declaration);
            AppendDisposalGuard(lines, disposalTarget, disposalUsesPointerAccess, "__usingDisposeGuard");

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

            if (statement is ExpressionStatementSyntax conditionalInvocationExpressionStatement &&
                TryProcessConditionalMethodInvocationStatement(semantic, context, conditionalInvocationExpressionStatement.Expression, lines)) {
                return new ExpressionResult(true);
            }

            if (statement is ExpressionStatementSyntax nativeOwnershipExpressionStatement &&
                TryProcessNativeOwnershipInvocationStatement(semantic, context, nativeOwnershipExpressionStatement.Expression, lines)) {
                return new ExpressionResult(true);
            }

            if (statement is LocalDeclarationStatementSyntax localDeclarationStatement &&
                TryProcessNonEscapingManagedLocalDeclarationStatement(semantic, context, localDeclarationStatement, lines)) {
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

        bool TryProcessNativeOwnershipInvocationStatement(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax expression,
            List<string> lines) {
            if (expression is not InvocationExpressionSyntax invocationExpression) {
                return false;
            }

            IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (invokedMethodSymbol == null ||
                !string.Equals(invokedMethodSymbol.ContainingType?.Name, "NativeOwnership", StringComparison.Ordinal)) {
                return false;
            }

            if (invocationExpression.ArgumentList.Arguments.Count != 1) {
                throw new InvalidOperationException($"Native ownership helper '{invokedMethodSymbol.Name}' requires exactly one argument.");
            }

            ArgumentSyntax argument = invocationExpression.ArgumentList.Arguments[0];
            if (string.Equals(invokedMethodSymbol.Name, "Delete", StringComparison.Ordinal)) {
                NativeOwnershipTarget nativeOwnershipTarget = ResolveNativeOwnershipTarget(semantic, context, argument.Expression, false);
                lines.AddRange(nativeOwnershipTarget.BeforeLines);
                lines.Add("delete ");
                lines.Add(nativeOwnershipTarget.ReadExpression);
                lines.Add(";\n");
                return true;
            }

            if (string.Equals(invokedMethodSymbol.Name, "DisposeAndDelete", StringComparison.Ordinal)) {
                NativeOwnershipTarget nativeOwnershipTarget = ResolveNativeOwnershipTarget(semantic, context, argument.Expression, false);
                lines.AddRange(nativeOwnershipTarget.BeforeLines);
                lines.Add("if (");
                lines.Add(nativeOwnershipTarget.ReadExpression);
                lines.Add(" != nullptr)\n{\n");
                lines.Add(nativeOwnershipTarget.ReadExpression);
                lines.Add("->Dispose();\n");
                lines.Add("delete ");
                lines.Add(nativeOwnershipTarget.ReadExpression);
                lines.Add(";\n");
                lines.Add("}\n");
                return true;
            }

            if (string.Equals(invokedMethodSymbol.Name, "Release", StringComparison.Ordinal)) {
                NativeOwnershipTarget nativeOwnershipTarget = ResolveNativeOwnershipTarget(semantic, context, argument.Expression, true);
                lines.AddRange(nativeOwnershipTarget.BeforeLines);
                lines.Add("delete ");
                lines.Add(nativeOwnershipTarget.ReadExpression);
                lines.Add(";\n");
                lines.Add(nativeOwnershipTarget.ClearExpression);
                lines.Add(";\n");
                return true;
            }

            if (string.Equals(invokedMethodSymbol.Name, "DisposeAndRelease", StringComparison.Ordinal)) {
                NativeOwnershipTarget nativeOwnershipTarget = ResolveNativeOwnershipTarget(semantic, context, argument.Expression, true);
                lines.AddRange(nativeOwnershipTarget.BeforeLines);
                lines.Add("if (");
                lines.Add(nativeOwnershipTarget.ReadExpression);
                lines.Add(" != nullptr)\n{\n");
                lines.Add(nativeOwnershipTarget.ReadExpression);
                lines.Add("->Dispose();\n");
                lines.Add("delete ");
                lines.Add(nativeOwnershipTarget.ReadExpression);
                lines.Add(";\n");
                lines.Add("}\n");
                lines.Add(nativeOwnershipTarget.ClearExpression);
                lines.Add(";\n");
                return true;
            }

            return false;
        }

        NativeOwnershipTarget ResolveNativeOwnershipTarget(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax targetExpression,
            bool requireWritableClearTarget) {
            List<string> beforeLines = new List<string>();
            List<string> readExpressionLines = new List<string>();
            int start = context.DepthClass;
            ExpressionResult readExpressionResult = ProcessExpression(semantic, context, targetExpression, readExpressionLines);
            context.PopClass(start);
            if (readExpressionResult.BeforeLines != null && readExpressionResult.BeforeLines.Count > 0) {
                beforeLines.AddRange(readExpressionResult.BeforeLines);
            }

            string readExpression = string.Concat(readExpressionLines);
            string clearExpression = requireWritableClearTarget
                ? ResolveNativeOwnershipClearExpression(semantic, context, targetExpression)
                : string.Empty;
            return new NativeOwnershipTarget(readExpression, clearExpression, beforeLines);
        }

        string ResolveNativeOwnershipClearExpression(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax targetExpression) {
            ISymbol targetSymbol = ResolveNativeOwnershipTargetSymbol(semantic, targetExpression);
            if (targetSymbol is ILocalSymbol || targetSymbol is IParameterSymbol || targetSymbol is IFieldSymbol) {
                List<string> clearLines = new List<string>();
                int start = context.DepthClass;
                ProcessExpression(semantic, context, targetExpression, clearLines);
                context.PopClass(start);
                return string.Concat(clearLines) + " = nullptr";
            }

            if (targetSymbol is IPropertySymbol propertySymbol) {
                if (propertySymbol.SetMethod == null) {
                    throw new InvalidOperationException($"Native ownership release requires a writable property target for '{propertySymbol.Name}'.");
                }

                if (propertySymbol.IsStatic) {
                    VariableType propertySourceType = VariableUtil.GetVarType(propertySymbol.ContainingType);
                    string propertyContainingTypeName = GetCppTypeToken(propertySourceType, context.Program);
                    return $"{propertyContainingTypeName}::set_{propertySymbol.Name}(nullptr)";
                }

                if (targetExpression is MemberAccessExpressionSyntax memberAccessExpression) {
                    List<string> receiverLines = new List<string>();
                    int receiverStart = context.DepthClass;
                    ExpressionResult receiverResult = ProcessExpression(semantic, context, memberAccessExpression.Expression, receiverLines);
                    context.PopClass(receiverStart);
                    string receiverText = string.Concat(receiverLines);
                    string memberAccessToken = UsesDirectMemberAccess(receiverResult) ? "." : "->";
                    return $"{receiverText}{memberAccessToken}set_{propertySymbol.Name}(nullptr)";
                }

                return $"this->set_{propertySymbol.Name}(nullptr)";
            }

            throw new InvalidOperationException($"Native ownership helper target '{targetExpression}' must resolve to a field, local, parameter, or writable property.");
        }

        static ISymbol ResolveNativeOwnershipTargetSymbol(
            SemanticModel semantic,
            ExpressionSyntax targetExpression) {
            SymbolInfo symbolInfo = semantic.GetSymbolInfo(targetExpression);
            ISymbol targetSymbol = symbolInfo.Symbol;
            if (targetSymbol is IAliasSymbol aliasSymbol) {
                targetSymbol = aliasSymbol.Target;
            }

            if (targetSymbol != null) {
                return targetSymbol;
            }

            if (semantic.GetOperation(targetExpression) is ILocalReferenceOperation localReferenceOperation) {
                return localReferenceOperation.Local;
            }

            if (semantic.GetOperation(targetExpression) is IParameterReferenceOperation parameterReferenceOperation) {
                return parameterReferenceOperation.Parameter;
            }

            if (semantic.GetOperation(targetExpression) is IFieldReferenceOperation fieldReferenceOperation) {
                return fieldReferenceOperation.Field;
            }

            if (semantic.GetOperation(targetExpression) is IPropertyReferenceOperation propertyReferenceOperation) {
                return propertyReferenceOperation.Property;
            }

            if (symbolInfo.CandidateSymbols.Length > 0) {
                return symbolInfo.CandidateSymbols[0];
            }

            return null;
        }

        sealed class NativeOwnershipTarget {
            public NativeOwnershipTarget(string readExpression, string clearExpression, List<string> beforeLines) {
                ReadExpression = readExpression;
                ClearExpression = clearExpression;
                BeforeLines = beforeLines;
            }

            public string ReadExpression { get; }

            public string ClearExpression { get; }

            public List<string> BeforeLines { get; }
        }

        bool TryProcessNonEscapingManagedLocalDeclarationStatement(
            SemanticModel semantic,
            LayerContext context,
            LocalDeclarationStatementSyntax localDeclarationStatement,
            List<string> lines) {
            if (localDeclarationStatement == null || localDeclarationStatement.Declaration == null) {
                return false;
            }

            if (localDeclarationStatement.Declaration.Variables.Count != 1) {
                return false;
            }

            VariableDeclaratorSyntax variable = localDeclarationStatement.Declaration.Variables[0];
            if (!ShouldDeleteManagedLocalAtScopeExit(semantic, context, variable, localDeclarationStatement.Declaration)) {
                return false;
            }

            ProcessDeclaration(semantic, context, localDeclarationStatement.Declaration, lines);
            lines.Add(";\n");

            string guardName = CreateTemporaryName("__localDeleteGuard");
            RegisterRuntimeRequirement("NativeFinally");
            lines.Add($"auto {guardName} = he_cpp_make_scope_exit([&]() {{\n");
            lines.Add("delete ");
            lines.Add(variable.Identifier.Text);
            lines.Add(";\n");
            lines.Add("});\n");
            return true;
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

        bool TryProcessConditionalMethodInvocationStatement(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax expression,
            List<string> lines) {
            if (expression is not ConditionalAccessExpressionSyntax conditionalAccess ||
                conditionalAccess.WhenNotNull is not InvocationExpressionSyntax invocation) {
                return false;
            }

            if (invocation.Expression is MemberBindingExpressionSyntax memberBinding &&
                memberBinding.Name is IdentifierNameSyntax memberBindingIdentifier &&
                string.Equals(memberBindingIdentifier.Identifier.Text, "Invoke", StringComparison.Ordinal)) {
                return false;
            }

            if (invocation.Expression is not MemberBindingExpressionSyntax &&
                invocation.Expression is not MemberAccessExpressionSyntax) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, conditionalAccess.Expression, out ITypeSymbol receiverTypeSymbol) ||
                receiverTypeSymbol == null ||
                receiverTypeSymbol.IsValueType ||
                receiverTypeSymbol.SpecialType == SpecialType.System_String) {
                return false;
            }

            List<string> receiverLines = new List<string>();
            int startReceiver = context.DepthClass;
            ExpressionResult receiverResult = ProcessExpression(semantic, context, conditionalAccess.Expression, receiverLines);
            context.PopClass(startReceiver);
            if (!receiverResult.Processed) {
                return false;
            }

            if (receiverResult.BeforeLines != null &&
                receiverResult.BeforeLines.Count > 0) {
                lines.AddRange(receiverResult.BeforeLines);
            }

            lines.Add("if (");
            lines.AddRange(receiverLines);
            lines.Add(" != nullptr)\n{\n");
            IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocation);
            if (!TryProcessConditionalMultiImplementationGenericInvocation(
                    semantic,
                    context,
                    invocation,
                    invokedMethodSymbol,
                    receiverTypeSymbol,
                    receiverLines,
                    lines)) {
                lines.AddRange(receiverLines);
                lines.Add("->");
                AppendConditionalAccessInvocationTarget(semantic, context, conditionalAccess.Expression, invocation, lines);
                lines.Add("(");
                AppendInvocationArguments(semantic, context, invocation.ArgumentList.Arguments, lines);
                lines.Add(");\n");
            }

            lines.Add("}\n");

            if (receiverResult.AfterLines != null &&
                receiverResult.AfterLines.Count > 0) {
                lines.AddRange(receiverResult.AfterLines);
            }

            return true;
        }

        void AppendConditionalAccessInvocationTarget(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax receiverExpression,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines) {
            IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (invokedMethodSymbol != null &&
                TryResolveGeneratedContainingClass(invokedMethodSymbol, out _)) {
                if (ShouldEmitDependentTemplateQualifier(invokedMethodSymbol, ReceiverRequiresDependentTemplateQualifier(semantic, receiverExpression))) {
                    lines.Add("template ");
                }

                lines.Add(GetEmittedFunctionName(invokedMethodSymbol));
                AppendInvocationGenericArgumentsFromSyntax(semantic, context, invocationExpression.Expression, lines);
                return;
            }

            if (invocationExpression.Expression is MemberBindingExpressionSyntax memberBindingExpression) {
                ProcessExpression(semantic, context, memberBindingExpression.Name, lines);
                return;
            }

            if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccessExpression) {
                ProcessExpression(semantic, context, memberAccessExpression.Name, lines);
            }
        }

        bool TryProcessConditionalMultiImplementationGenericInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            ITypeSymbol receiverTypeSymbol,
            List<string> receiverLines,
            List<string> lines) {
            if (invokedMethodSymbol == null ||
                !invokedMethodSymbol.IsGenericMethod) {
                return false;
            }

            List<ConversionClass> matchingDefinitions = ResolveGeneratedGenericInvocationDefinitions(receiverTypeSymbol, invokedMethodSymbol.OriginalDefinition);
            if (matchingDefinitions.Count <= 1) {
                return false;
            }

            List<INamedTypeSymbol> implementationTypeSymbols = ResolveGeneratedGenericInvocationConcreteImplementations(semantic.Compilation, receiverTypeSymbol, invokedMethodSymbol.OriginalDefinition);
            if (implementationTypeSymbols.Count <= 1) {
                return false;
            }

            List<string> beforeLines = new List<string>();
            List<string> invocationLines = new List<string>();
            if (!TryAppendMultiImplementationGenericInvocationExpression(
                    semantic,
                    context,
                    invocationExpression,
                    invokedMethodSymbol,
                    string.Concat(receiverLines),
                    implementationTypeSymbols,
                    beforeLines,
                    invocationLines,
                    out _)) {
                return false;
            }

            lines.AddRange(beforeLines);
            lines.AddRange(invocationLines);
            lines.Add(";\n");
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

            if (TryProcessTupleAssignmentExpression(semantic, context, assignment, lines)) {
                return;
            }

            if (TryProcessRebindableRefLocalAssignment(semantic, context, assignment, lines)) {
                return;
            }

            if (TryProcessRefReturnPropertyAssignment(semantic, context, assignment, lines)) {
                return;
            }

            if (TryProcessComputedPropertyAssignment(semantic, context, assignment, lines)) {
                return;
            }

            if (TryProcessValueTypeCompoundAssignment(semantic, context, assignment, lines)) {
                return;
            }

            if (TryProcessScopeDeletedManagedLocalReassignment(semantic, context, assignment, lines)) {
                return;
            }

            int startDepth = context.Class.Count;
            List<string> leftLines = new List<string>();
            ExpressionResult assignResult = ProcessExpression(semantic, context, assignment.Left, leftLines);
            context.PopClass(startDepth);

            startDepth = context.Class.Count;
            List<string> rightLines = new List<string>();
            ExpressionResult rightResult = ProcessExpression(semantic, context, assignment.Right, rightLines);
            context.PopClass(startDepth);
            if (assignResult.BeforeLines != null && assignResult.BeforeLines.Count > 0) {
                lines.AddRange(assignResult.BeforeLines);
            }
            if (rightResult.BeforeLines != null && rightResult.BeforeLines.Count > 0) {
                lines.AddRange(rightResult.BeforeLines);
            }
            lines.AddRange(leftLines);

            string operatorVal = assignment.OperatorToken.ToString();
            if (assignResult.Type?.Type == VariableDataType.Callback && (operatorVal == "+=" || operatorVal == "-=")) {
                lines.Add(" = ");
            } else {
                lines.Add($" {operatorVal} ");
            }
            if (ShouldEmitEmptyStringForTargetedNullAssignment(semantic, assignment.Left, assignment.Right)) {
                lines.Add("std::string()");
            } else if (TryAppendEventMethodGroupAssignmentValue(semantic, context, assignment, lines)) {
            } else if (TryAppendDelegateLambdaAssignmentValue(semantic, context, assignment, rightLines, lines)) {
            } else if (TryAppendDelegateMethodGroupAssignmentValue(semantic, context, assignment, lines)) {
            } else if (TryAppendArrayAsListAssignmentValue(semantic, context, assignment, rightLines, lines)) {
            } else {
                lines.AddRange(rightLines);
            }
            if (rightResult.AfterLines != null && rightResult.AfterLines.Count > 0) {
                lines.Add(";\n");
                lines.AddRange(rightResult.AfterLines);
            }
        }

        bool TryProcessTupleAssignmentExpression(
            SemanticModel semantic,
            LayerContext context,
            AssignmentExpressionSyntax assignment,
            List<string> lines) {
            if (semantic == null ||
                context == null ||
                assignment == null ||
                lines == null ||
                assignment.Kind() != SyntaxKind.SimpleAssignmentExpression ||
                assignment.Left is not TupleExpressionSyntax &&
                assignment.Left is not DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax }) {
                return false;
            }

            ParenthesizedVariableDesignationSyntax tupleDesignation = null;
            TupleExpressionSyntax tupleAssignmentTarget = assignment.Left as TupleExpressionSyntax;
            if (tupleAssignmentTarget == null &&
                assignment.Left is DeclarationExpressionSyntax declarationExpression &&
                declarationExpression.Designation is ParenthesizedVariableDesignationSyntax declarationDesignation) {
                tupleDesignation = declarationDesignation;
            }

            int targetElementCount = tupleAssignmentTarget?.Arguments.Count ?? tupleDesignation?.Variables.Count ?? 0;
            if (targetElementCount == 0) {
                return false;
            }

            List<string> rightLines = new List<string>();
            int startDepth = context.DepthClass;
            ExpressionResult rightResult = ProcessExpression(semantic, context, assignment.Right, rightLines);
            context.PopClass(startDepth);
            if (!rightResult.Processed) {
                return false;
            }

            VariableType tupleType = ResolveTupleDeconstructionType(semantic, context, assignment.Right, rightResult);
            if (tupleType?.Type != VariableDataType.Tuple ||
                tupleType.GenericArgs.Count < targetElementCount) {
                return false;
            }

            if (rightResult.BeforeLines != null && rightResult.BeforeLines.Count > 0) {
                lines.AddRange(rightResult.BeforeLines);
            }

            string temporaryName = CreateTemporaryName("__deconstruct");
            lines.Add($"const auto {temporaryName} = ");
            lines.AddRange(rightLines);
            lines.Add(";\n");

            string tupleMemberAccessOperator = ".";
            FunctionStack functionStack = context.GetCurrentFunction();
            for (int index = 0; index < targetElementCount; index++) {
                string targetName = null;
                VariableType declaredTargetType = null;
                bool discardTarget = false;
                if (tupleAssignmentTarget != null) {
                    if (!TryResolveTupleAssignmentTarget(
                        semantic,
                        tupleAssignmentTarget.Arguments[index].Expression,
                        out targetName,
                        out declaredTargetType,
                        out discardTarget)) {
                        return false;
                    }

                    if (discardTarget) {
                        continue;
                    }
                } else if (tupleDesignation?.Variables[index] is SingleVariableDesignationSyntax singleVariableDesignation) {
                    targetName = singleVariableDesignation.Identifier.Text;
                }

                if (string.IsNullOrWhiteSpace(targetName)) {
                    return false;
                }

                VariableType elementType = tupleType.GenericArgs[index];
                VariableType targetType = declaredTargetType ?? elementType;
                RegisterGeneratedTypeReferences(context, targetType);

                ConversionVariable trackedVariable = functionStack?.Stack?.FindLast(candidate => candidate.Name == targetName);
                if (trackedVariable == null) {
                    CPPTypeData elementTypeData;
                    VariableType cppElementType = ConvertToCPPType(targetType, out elementTypeData);
                    string pointerSuffix = elementTypeData.IsPointer ? "*" : string.Empty;
                    string declarationTypeName = QualifyRenderedCppTypeName(cppElementType.ToCPPString(context.Program), context);
                    lines.Add($"{declarationTypeName}{pointerSuffix} {targetName} = {temporaryName}{tupleMemberAccessOperator}Item{index + 1};\n");

                    functionStack?.Stack?.Add(new ConversionVariable {
                        Name = targetName,
                        VarType = targetType
                    });
                } else {
                    lines.Add($"{targetName} = {temporaryName}{tupleMemberAccessOperator}Item{index + 1};\n");
                }
            }

            if (rightResult.AfterLines != null && rightResult.AfterLines.Count > 0) {
                lines.AddRange(rightResult.AfterLines);
            }

            return true;
        }

        static bool TryResolveTupleAssignmentTarget(
            SemanticModel semantic,
            ExpressionSyntax targetExpression,
            out string targetName,
            out VariableType declaredTargetType,
            out bool discardTarget) {
            targetName = null;
            declaredTargetType = null;
            discardTarget = false;

            if (targetExpression is IdentifierNameSyntax targetIdentifier) {
                targetName = targetIdentifier.Identifier.Text;
                return true;
            }

            if (targetExpression is not DeclarationExpressionSyntax declarationExpression) {
                return false;
            }

            if (declarationExpression.Designation is DiscardDesignationSyntax) {
                discardTarget = true;
                return true;
            }

            if (declarationExpression.Designation is not SingleVariableDesignationSyntax singleVariableDesignation) {
                return false;
            }

            targetName = singleVariableDesignation.Identifier.Text;
            declaredTargetType = ResolveDeclarationExpressionVariableType(semantic, declarationExpression, singleVariableDesignation);
            return true;
        }

        bool TryProcessRebindableRefLocalAssignment(
            SemanticModel semantic,
            LayerContext context,
            AssignmentExpressionSyntax assignment,
            List<string> lines) {
            if (semantic == null ||
                context == null ||
                assignment == null ||
                lines == null ||
                assignment.Kind() != SyntaxKind.SimpleAssignmentExpression ||
                assignment.Right is not RefExpressionSyntax refExpression ||
                assignment.Left is not IdentifierNameSyntax leftIdentifier ||
                semantic.GetSymbolInfo(leftIdentifier).Symbol is not ILocalSymbol) {
                return false;
            }

            ConversionVariable stackVariable = context.GetCurrentFunction()?.Stack?.FindLast(candidate => candidate.Name == leftIdentifier.Identifier.Text);
            if (stackVariable == null ||
                !stackVariable.IsRebindableReferenceLocal) {
                return false;
            }

            List<string> rightLines = new List<string>();
            ExpressionResult rightResult = ProcessExpression(semantic, context, refExpression.Expression, rightLines);
            if (rightResult.BeforeLines != null && rightResult.BeforeLines.Count > 0) {
                lines.AddRange(rightResult.BeforeLines);
            }

            lines.Add(leftIdentifier.Identifier.Text);
            lines.Add(" = &");
            lines.AddRange(rightLines);
            if (rightResult.AfterLines != null && rightResult.AfterLines.Count > 0) {
                lines.Add(";\n");
                lines.AddRange(rightResult.AfterLines);
            }

            return true;
        }

        /// <summary>
        /// Determines whether one simple assignment writes a null-like literal into a string target and therefore must lower to an empty native string instead of a pointer literal.
        /// </summary>
        /// <param name="semantic">Semantic model for the active document.</param>
        /// <param name="leftExpression">Assignment target being written.</param>
        /// <param name="rightExpression">Assignment value being lowered.</param>
        /// <returns><c>true</c> when the assignment should emit <c>std::string()</c>; otherwise, <c>false</c>.</returns>
        static bool ShouldEmitEmptyStringForTargetedNullAssignment(
            SemanticModel semantic,
            ExpressionSyntax leftExpression,
            ExpressionSyntax rightExpression) {
            if (semantic == null || leftExpression == null || rightExpression == null) {
                return false;
            }

            if (!IsNullLikeExpression(rightExpression)) {
                return false;
            }

            return TryGetAssignmentTargetTypeSymbol(semantic, leftExpression, out ITypeSymbol leftTypeSymbol)
                && IsStringTypeSymbol(leftTypeSymbol);
        }

        /// <summary>
        /// Wraps array-backed right-hand assignment values as native lists when the assignment target expects one IReadOnlyList-like contract.
        /// </summary>
        /// <param name="semantic">Semantic model for the active document.</param>
        /// <param name="context">Conversion context used to resolve runtime type tokens.</param>
        /// <param name="assignment">Assignment being lowered.</param>
        /// <param name="rightLines">Already-lowered right-hand expression tokens.</param>
        /// <param name="lines">Output buffer receiving the converted assignment value.</param>
        /// <returns><c>true</c> when one native list wrapper was emitted; otherwise <c>false</c>.</returns>
        bool TryAppendArrayAsListAssignmentValue(
            SemanticModel semantic,
            LayerContext context,
            AssignmentExpressionSyntax assignment,
            List<string> rightLines,
            List<string> lines) {
            if (semantic == null ||
                context == null ||
                assignment == null ||
                rightLines == null ||
                lines == null ||
                assignment.Kind() != SyntaxKind.SimpleAssignmentExpression ||
                !TryGetAssignmentTargetTypeSymbol(semantic, assignment.Left, out ITypeSymbol leftTypeSymbol) ||
                !IsListFamilyTypeSymbol(leftTypeSymbol) ||
                !TryResolveArrayElementTypeSymbol(semantic, assignment.Right, out ITypeSymbol arrayElementTypeSymbol)) {
                return false;
            }

            RegisterRuntimeRequirement("NativeList");
            VariableType elementType = VariableUtil.GetVarType(arrayElementTypeSymbol);
            string elementTypeName = GetCppTypeToken(elementType, context.Program);
            lines.Add($"new List<{elementTypeName}>(");
            lines.AddRange(rightLines);
            lines.Add(")");
            return true;
        }

        bool TryAppendDelegateMethodGroupAssignmentValue(
            SemanticModel semantic,
            LayerContext context,
            AssignmentExpressionSyntax assignment,
            List<string> lines) {
            if (semantic == null ||
                context == null ||
                assignment == null ||
                lines == null ||
                assignment.Kind() != SyntaxKind.SimpleAssignmentExpression ||
                !TryGetAssignmentTargetTypeSymbol(semantic, assignment.Left, out ITypeSymbol leftTypeSymbol) ||
                leftTypeSymbol.TypeKind != TypeKind.Delegate) {
                return false;
            }

            IMethodSymbol methodGroupSymbol = ResolveMethodSymbol(semantic.GetSymbolInfo(assignment.Right));
            if (methodGroupSymbol == null) {
                return false;
            }

            return TryAppendDelegateWrapperConstruction(
                semantic,
                context,
                leftTypeSymbol,
                methodGroupSymbol,
                assignment.Right,
                lines);
        }

        bool TryAppendEventMethodGroupAssignmentValue(
            SemanticModel semantic,
            LayerContext context,
            AssignmentExpressionSyntax assignment,
            List<string> lines) {
            if (semantic == null ||
                context == null ||
                assignment == null ||
                lines == null ||
                !assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
                !assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) ||
                !IsEventExpression(semantic, assignment.Left)) {
                return false;
            }

            IMethodSymbol methodGroupSymbol = ResolveMethodSymbol(semantic.GetSymbolInfo(assignment.Right));
            if (methodGroupSymbol == null) {
                return false;
            }

            RegisterRuntimeRequirement("NativeEvent");
            if (methodGroupSymbol.IsStatic) {
                lines.Add(RenderQualifiedMethodPointerTarget(methodGroupSymbol, context));
                return true;
            }

            if (!TryResolveBoundDelegateReceiverText(semantic, context, assignment.Right, methodGroupSymbol, out string receiverText)) {
                return false;
            }

            lines.Add("Event::Bind(");
            lines.Add(receiverText);
            lines.Add(", ");
            lines.Add(RenderQualifiedMethodPointerTarget(methodGroupSymbol, context));
            lines.Add(")");
            return true;
        }

        bool TryGetDelegateLambdaWrapperTypeName(
            INamedTypeSymbol delegateTypeSymbol,
            LayerContext context,
            out string delegateWrapperTypeName) {
            delegateWrapperTypeName = string.Empty;
            if (delegateTypeSymbol == null || context == null) {
                return false;
            }

            if (TryGetFrameworkThreadDelegateWrapperTypeName(delegateTypeSymbol, context, out delegateWrapperTypeName)) {
                return true;
            }

            if (delegateTypeSymbol.TypeKind == TypeKind.Delegate &&
                !string.Equals(delegateTypeSymbol.Name, "Action", StringComparison.Ordinal) &&
                !string.Equals(delegateTypeSymbol.Name, "Func", StringComparison.Ordinal)) {
                delegateWrapperTypeName = GetCustomDelegateWrapperTypeName(delegateTypeSymbol, context);
                return !string.IsNullOrWhiteSpace(delegateWrapperTypeName);
            }

            IMethodSymbol invokeMethod = delegateTypeSymbol.DelegateInvokeMethod;
            if (invokeMethod == null) {
                return false;
            }

            if (IsActionTypeSymbol(delegateTypeSymbol) && invokeMethod.ReturnsVoid) {
                List<string> actionArgumentTypes = new List<string>();
                foreach (IParameterSymbol parameterSymbol in invokeMethod.Parameters) {
                    actionArgumentTypes.Add(GetCppTypeToken(VariableUtil.GetVarType(parameterSymbol.Type), context.Program));
                }

                delegateWrapperTypeName = $"Action<{string.Join(", ", actionArgumentTypes)}>";
                return true;
            }

            if (!IsFuncTypeSymbol(delegateTypeSymbol) || invokeMethod.ReturnsVoid) {
                return false;
            }

            List<string> funcArgumentTypes = new List<string>();
            foreach (IParameterSymbol parameterSymbol in invokeMethod.Parameters) {
                funcArgumentTypes.Add(GetCppTypeToken(VariableUtil.GetVarType(parameterSymbol.Type), context.Program));
            }

            funcArgumentTypes.Add(GetCppTypeToken(VariableUtil.GetVarType(invokeMethod.ReturnType), context.Program));
            delegateWrapperTypeName = $"Func<{string.Join(", ", funcArgumentTypes)}>";
            return true;
        }

        bool TryAppendDelegateLambdaAssignmentValue(
            SemanticModel semantic,
            LayerContext context,
            AssignmentExpressionSyntax assignment,
            List<string> rightLines,
            List<string> lines) {
            if (semantic == null ||
                context == null ||
                assignment == null ||
                rightLines == null ||
                lines == null ||
                assignment.Kind() != SyntaxKind.SimpleAssignmentExpression ||
                assignment.Right is not AnonymousFunctionExpressionSyntax ||
                !TryGetAssignmentTargetTypeSymbol(semantic, assignment.Left, out ITypeSymbol leftTypeSymbol) ||
                leftTypeSymbol is not INamedTypeSymbol namedDelegateTypeSymbol ||
                namedDelegateTypeSymbol.TypeKind != TypeKind.Delegate ||
                !TryGetDelegateLambdaWrapperTypeName(namedDelegateTypeSymbol, context, out string delegateWrapperTypeName)) {
                return false;
            }

            lines.Add("new ");
            lines.Add(QualifyRenderedCppTypeName(delegateWrapperTypeName, context));
            lines.Add("(");
            lines.AddRange(rightLines);
            lines.Add(")");
            return true;
        }

        /// <summary>
        /// Resolves the static type of one assignment target using type information first and symbol information as a fallback for properties and fields.
        /// </summary>
        /// <param name="semantic">Semantic model for the active document.</param>
        /// <param name="leftExpression">Assignment target whose type should be resolved.</param>
        /// <param name="typeSymbol">Resolved target type when available.</param>
        /// <returns><c>true</c> when a target type was resolved; otherwise, <c>false</c>.</returns>
        static bool TryGetAssignmentTargetTypeSymbol(
            SemanticModel semantic,
            ExpressionSyntax leftExpression,
            out ITypeSymbol typeSymbol) {
            typeSymbol = null;
            if (semantic == null || leftExpression == null || semantic.SyntaxTree != leftExpression.SyntaxTree) {
                return false;
            }

            try {
                TypeInfo typeInfo = semantic.GetTypeInfo(leftExpression);
                typeSymbol = typeInfo.ConvertedType ?? typeInfo.Type;
            } catch (ArgumentException) {
                typeSymbol = null;
            }

            if (typeSymbol != null) {
                return true;
            }

            ISymbol targetSymbol;
            try {
                targetSymbol = semantic.GetSymbolInfo(leftExpression).Symbol;
            } catch (ArgumentException) {
                return false;
            }

            if (targetSymbol is IAliasSymbol aliasSymbol) {
                targetSymbol = aliasSymbol.Target;
            }

            if (targetSymbol is IPropertySymbol propertySymbol) {
                typeSymbol = propertySymbol.Type;
            } else if (targetSymbol is IFieldSymbol fieldSymbol) {
                typeSymbol = fieldSymbol.Type;
            } else if (targetSymbol is ILocalSymbol localSymbol) {
                typeSymbol = localSymbol.Type;
            } else if (targetSymbol is IParameterSymbol parameterSymbol) {
                typeSymbol = parameterSymbol.Type;
            }

            return typeSymbol != null;
        }

        /// <summary>
        /// Determines whether one expression represents a null-like literal in source.
        /// </summary>
        /// <param name="expression">Expression to inspect.</param>
        /// <returns><c>true</c> when the expression is <c>null</c> or <c>default</c>; otherwise, <c>false</c>.</returns>
        static bool IsNullLikeExpression(ExpressionSyntax expression) {
            if (expression == null) {
                return false;
            }

            return expression.IsKind(SyntaxKind.NullLiteralExpression)
                || expression.IsKind(SyntaxKind.DefaultLiteralExpression)
                || expression.IsKind(SyntaxKind.DefaultExpression);
        }

        bool TryProcessScopeDeletedManagedLocalReassignment(
            SemanticModel semantic,
            LayerContext context,
            AssignmentExpressionSyntax assignment,
            List<string> lines) {
            if (semantic == null || context == null || assignment == null || lines == null) {
                return false;
            }

            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                assignment.Left is not IdentifierNameSyntax identifierName ||
                !ShouldDeleteManagedLocalAtScopeExit(semantic, context, identifierName) ||
                !IsManagedHeapAllocationExpression(assignment.Right) ||
                DoesExpressionReferenceLocal(semantic, assignment.Right, identifierName)) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, assignment.Right, out ITypeSymbol rightTypeSymbol)) {
                return false;
            }

            VariableType rightType = VariableUtil.GetVarType(rightTypeSymbol);
            VariableType cppRightType = ConvertToCPPType(rightType, out CPPTypeData rightTypeData);
            if (cppRightType == null || !rightTypeData.IsPointer) {
                return false;
            }

            string valueName = CreateTemporaryName("__reassignValue");
            lines.Add("auto ");
            lines.Add(valueName);
            lines.Add(" = ");
            int rightStartDepth = context.Class.Count;
            ProcessExpression(semantic, context, assignment.Right, lines);
            context.PopClass(rightStartDepth);
            lines.Add(";\n");
            lines.Add("delete ");
            lines.Add(identifierName.Identifier.Text);
            lines.Add(";\n");
            lines.Add(identifierName.Identifier.Text);
            lines.Add(" = ");
            lines.Add(valueName);
            return true;
        }

        protected override ExpressionResult ProcessIdentifierNameSyntax(SemanticModel semantic, LayerContext context, IdentifierNameSyntax identifier, List<string> lines, List<ExpressionResult> refTypes) {
            string name = identifier.ToString();
            if (string.Equals(name, "_", StringComparison.Ordinal) &&
                identifier.Parent is ArgumentSyntax discardArgument &&
                !string.IsNullOrEmpty(discardArgument.RefKindKeyword.ToString())) {
                lines.Add(GetDiscardTemporaryName(identifier));
                return new ExpressionResult(true);
            }

            bool isMethod = false;
            bool emitMethodGroupPointer = false;
            MemberAccessExpressionSyntax qualifiedMemberAccess = identifier.Parent as MemberAccessExpressionSyntax;
            bool isQualifiedMemberName = qualifiedMemberAccess != null &&
                ReferenceEquals(qualifiedMemberAccess.Name, identifier);

            ISymbol nsSymbol;
            try {
                nsSymbol = semantic.GetSymbolInfo(identifier).Symbol;
            } catch (ArgumentException) {
                nsSymbol = null;
            }

            if (nsSymbol == null &&
                isQualifiedMemberName) {
                try {
                    nsSymbol = semantic.GetSymbolInfo(qualifiedMemberAccess).Symbol;
                } catch (ArgumentException) {
                    nsSymbol = null;
                }
            }

            if (TryResolveInvocationTargetMethodSymbol(semantic, identifier, out IMethodSymbol invocationTargetMethodSymbol)) {
                nsSymbol = invocationTargetMethodSymbol;
            }

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

            if ((nsSymbol == null || nsSymbol is INamedTypeSymbol || nsSymbol is INamespaceSymbol) &&
                !isQualifiedMemberName) {
                INamedTypeSymbol enclosingTypeSymbol = semantic.GetEnclosingSymbol(identifier.SpanStart)?.ContainingType;
                if (enclosingTypeSymbol != null) {
                    ISymbol enclosingMemberSymbol = enclosingTypeSymbol.GetMembers(name)
                        .FirstOrDefault(member => member is IPropertySymbol || member is IFieldSymbol || member is IMethodSymbol);
                    if (enclosingMemberSymbol != null) {
                        nsSymbol = enclosingMemberSymbol;
                    }
                }
            }

            if (nsSymbol is IMethodSymbol methodSymbol) {
                isMethod = true;
            }

            ConversionFunction resolvedGeneratedMethod = null;
            if (nsSymbol is IMethodSymbol resolvedMethodSymbol &&
                TryResolveGeneratedFunction(resolvedMethodSymbol, out ConversionFunction generatedMethod)) {
                resolvedGeneratedMethod = generatedMethod;
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
            ConversionFunction? classFn = resolvedGeneratedMethod ?? currentClass?.Functions.Find(c => c.Name == name);

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
            ConversionVariable? stackVar = currentFn?.Stack.FindLast(c => c.Name == name);

            var matchingVars = new List<ConversionVariable>();
            context.Function.ForEach(fn => {
                var result = fn?.Stack.Where(var => var.Name == name);
                if (result != null) {
                    matchingVars.AddRange(result);
                }
            });

            string emittedIdentifierName = stackVar?.Remap ?? functionInVar?.Remap ?? CPPIdentifierSanitizer.SanitizeIdentifier(name);
            if (stackVar?.IsRebindableReferenceLocal == true) {
                emittedIdentifierName = $"(*{emittedIdentifierName})";
            }
            bool emitTemplateDisambiguator = ShouldEmitTemplateDisambiguatorForIdentifier(semantic, identifier, nsSymbol);

            if (currentClass == null) {
                lines.Add(emittedIdentifierName);
            } else {
                string staticContainingTypeAccessName = TryResolveUnqualifiedStaticContainingTypeAccessName(
                    context,
                    currentClass,
                    nsSymbol,
                    classVar,
                    functionInVar,
                    matchingVars.Count,
                    isQualifiedMemberName);
                if (!string.IsNullOrWhiteSpace(staticContainingTypeAccessName)) {
                    lines.Add(staticContainingTypeAccessName);
                    lines.Add("::");
                }

                bool isResolvedPropertyAccessor = !string.IsNullOrEmpty(propertyGetterCallName) &&
                    nsSymbol is IPropertySymbol;
                bool isResolvedInstanceMemberSymbol = nsSymbol is IFieldSymbol ||
                    nsSymbol is IPropertySymbol ||
                    nsSymbol is IMethodSymbol;
                bool isClassVar = ((classVar != null || isResolvedPropertyAccessor) &&
                    functionInVar == null &&
                    matchingVars.Count == 0) ||
                    (classFn != null &&
                    functionInVar == null &&
                    matchingVars.Count == 0) ||
                    (isResolvedInstanceMemberSymbol &&
                    functionInVar == null &&
                    matchingVars.Count == 0);


                if (isClassVar && !isQualifiedMemberName) {
                    bool isStaticClassMember = false;
                    ISymbol identifierSymbol;
                    try {
                        identifierSymbol = semantic.GetSymbolInfo(identifier).Symbol;
                    } catch (ArgumentException) {
                        identifierSymbol = null;
                    }

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
                            ISymbol symbol;
                            try {
                                symbol = semantic.GetSymbolInfo(identifier).Symbol;
                            } catch (ArgumentException) {
                                symbol = null;
                            }

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

                ConversionFunction emittedFunction = resolvedGeneratedMethod ?? classFn;
                if (classFn == null || string.IsNullOrEmpty(classFn.Remap)) {
                    ConversionVariable? varOnClass = currentClass.Variables.FirstOrDefault(c => c.Name == name);
                    if (emitMethodGroupPointer) {
                        lines.Add($"&{currentClass.GetEmittedTypeName()}::");
                    }

                    if (!string.IsNullOrEmpty(propertyGetterCallName)) {
                        lines.Add(propertyGetterCallName);
                    } else if (emittedFunction != null) {
                        if (emitTemplateDisambiguator) {
                            lines.Add("template ");
                        }

                        lines.Add(GetEmittedFunctionName(emittedFunction));
                    } else if (stackVar != null || functionInVar != null) {
                        lines.Add(emittedIdentifierName);
                    } else if (varOnClass == null || string.IsNullOrEmpty(varOnClass.Remap)) {
                        lines.Add(name);
                    } else {
                        lines.Add(varOnClass.Remap);
                    }
                } else {
                    if (emitMethodGroupPointer) {
                        lines.Add($"&{currentClass.GetEmittedTypeName()}::");
                    }

                    if (emitTemplateDisambiguator) {
                        lines.Add("template ");
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

        bool TryResolveInvocationTargetMethodSymbol(SemanticModel semantic, IdentifierNameSyntax identifier, out IMethodSymbol methodSymbol) {
            methodSymbol = null;
            if (identifier == null) {
                return false;
            }

            if (identifier.Parent is InvocationExpressionSyntax directInvocation &&
                ReferenceEquals(directInvocation.Expression, identifier)) {
                methodSymbol = ResolveInvokedMethodSymbol(semantic, directInvocation);
                return methodSymbol != null;
            }

            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
                ReferenceEquals(memberAccess.Name, identifier) &&
                memberAccess.Parent is InvocationExpressionSyntax memberInvocation &&
                ReferenceEquals(memberInvocation.Expression, memberAccess)) {
                methodSymbol = ResolveInvokedMethodSymbol(semantic, memberInvocation);
                return methodSymbol != null;
            }

            return false;
        }

        static string GetEmittedFunctionName(ConversionFunction function) {
            if (function == null) {
                return string.Empty;
            }

            return function.Name + GetRefModifierSuffix(function);
        }

        static string GetEmittedFunctionName(IMethodSymbol methodSymbol) {
            if (methodSymbol == null) {
                return string.Empty;
            }

            return methodSymbol.Name + GetRefModifierSuffix(methodSymbol);
        }

        string ResolveConvertedFunctionName(IMethodSymbol methodSymbol, ConversionFunction generatedFunction = null) {
            if (generatedFunction != null) {
                return GetEmittedFunctionName(generatedFunction);
            }

            if (TryResolveGeneratedFunction(methodSymbol, out ConversionFunction resolvedGeneratedFunction)) {
                return GetEmittedFunctionName(resolvedGeneratedFunction);
            }

            return GetEmittedFunctionName(methodSymbol);
        }

        static string GetConversionOperatorFunctionName(IMethodSymbol methodSymbol) {
            if (methodSymbol == null) {
                return string.Empty;
            }

            string operatorPrefix = methodSymbol.Name switch {
                "op_Implicit" => "op_Implicit_to_",
                "op_Explicit" => "op_Explicit_to_",
                _ => methodSymbol.Name + "_to_"
            };

            string targetName = methodSymbol.ReturnType?.OriginalDefinition?.MetadataName
                ?? methodSymbol.ReturnType?.MetadataName
                ?? methodSymbol.ReturnType?.Name
                ?? "Unknown";
            return operatorPrefix + targetName.Replace('`', '_');
        }

        static string BuildSourceMethodKey(IMethodSymbol methodSymbol) {
            if (methodSymbol == null) {
                return string.Empty;
            }

            SymbolDisplayFormat displayFormat = new SymbolDisplayFormat(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.None,
                memberOptions: SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters,
                parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
            return methodSymbol.OriginalDefinition.ToDisplayString(displayFormat);
        }

        static string GetRefModifierSuffix(ConversionFunction function) {
            if (function?.InParameters == null || function.InParameters.Count == 0) {
                return string.Empty;
            }

            List<string> suffixParts = new List<string>();
            for (int index = 0; index < function.InParameters.Count; index++) {
                ConversionVariable parameter = function.InParameters[index];
                if ((parameter.Modifier & ParameterModifier.Ref) != 0) {
                    suffixParts.Add($"ref{index}");
                } else if ((parameter.Modifier & ParameterModifier.Out) != 0) {
                    suffixParts.Add($"out{index}");
                }
            }

            return suffixParts.Count == 0
                ? string.Empty
                : "__" + string.Join("_", suffixParts);
        }

        static string GetRefModifierSuffix(IMethodSymbol methodSymbol) {
            if (methodSymbol == null || methodSymbol.Parameters.Length == 0) {
                return string.Empty;
            }

            List<string> suffixParts = new List<string>();
            for (int index = 0; index < methodSymbol.Parameters.Length; index++) {
                IParameterSymbol parameter = methodSymbol.Parameters[index];
                if (parameter.RefKind == RefKind.Ref) {
                    suffixParts.Add($"ref{index}");
                } else if (parameter.RefKind == RefKind.Out) {
                    suffixParts.Add($"out{index}");
                }
            }

            return suffixParts.Count == 0
                ? string.Empty
                : "__" + string.Join("_", suffixParts);
        }

        protected override ExpressionResult ProcessObjectCreationExpressionSyntax(SemanticModel semantic, LayerContext context, ObjectCreationExpressionSyntax objectCreation, List<string> lines) {
            if (objectCreation.Initializer is InitializerExpressionSyntax initializer && IsObjectInitializer(initializer)) {
                return ProcessObjectInitializerCreation(semantic, context, objectCreation, initializer, lines);
            }

            if (IsSystemObjectType(semantic, objectCreation.Type)) {
                lines.Add("new char[1]");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("object"));
            }

            if (TryProcessDelegateObjectCreationExpression(semantic, context, objectCreation, lines, out VariableType delegateObjectCreationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, delegateObjectCreationType);
            }

            int diagnosticCount = GetDiagnosticCount();
            BuildObjectCreationExpression(semantic, context, objectCreation, lines);

            return new ExpressionResult(diagnosticCount == GetDiagnosticCount());
        }

        protected override ExpressionResult ProcessImplicitObjectCreationExpressionSyntax(
            SemanticModel semantic,
            LayerContext context,
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation,
            List<string> lines) {
            ITypeSymbol createdTypeSymbol = semantic.GetTypeInfo(implicitObjectCreation).Type ?? semantic.GetTypeInfo(implicitObjectCreation).ConvertedType;
            if (createdTypeSymbol == null) {
                return new ExpressionResult(false);
            }

            TypeSyntax explicitTypeSyntax = SyntaxFactory.ParseTypeName(createdTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            if (TryProcessDelegateObjectCreationExpression(
                semantic,
                context,
                explicitTypeSyntax,
                createdTypeSymbol,
                implicitObjectCreation.ArgumentList,
                lines,
                out VariableType delegateObjectCreationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, delegateObjectCreationType);
            }

            if (implicitObjectCreation.Initializer is InitializerExpressionSyntax initializer &&
                IsObjectInitializer(initializer)) {
                return ProcessObjectInitializerCreation(
                    semantic,
                    context,
                    explicitTypeSyntax,
                    createdTypeSymbol,
                    implicitObjectCreation.ArgumentList,
                    initializer,
                    lines);
            }

            int diagnosticCount = GetDiagnosticCount();
            BuildObjectCreationExpression(
                semantic,
                context,
                explicitTypeSyntax,
                createdTypeSymbol,
                implicitObjectCreation.ArgumentList,
                null,
                lines);

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
            return ProcessObjectInitializerCreation(
                semantic,
                context,
                objectCreation.Type,
                ResolveObjectCreationTypeSymbol(semantic, objectCreation),
                objectCreation.ArgumentList,
                initializer,
                lines);
        }

        ExpressionResult ProcessObjectInitializerCreation(
            SemanticModel semantic,
            LayerContext context,
            TypeSyntax objectCreationTypeSyntax,
            ITypeSymbol objectCreationTypeSymbol,
            ArgumentListSyntax argumentList,
            InitializerExpressionSyntax initializer,
            List<string> lines) {
            string objectName = CreateTemporaryName("__object");

            lines.Add($"({GetObjectConstructionLambdaCaptureList(context)}() {{\n");
            lines.Add("auto ");
            lines.Add(objectName);
            lines.Add(" = ");

            ExpressionResult result = BuildObjectCreationExpression(
                semantic,
                context,
                objectCreationTypeSyntax,
                objectCreationTypeSymbol,
                argumentList,
                null,
                lines);
            lines.Add(";\n");
            string memberAccessOperator = UsesDirectMemberAccess(result) ? "." : "->";

            foreach (ExpressionSyntax expression in initializer.Expressions) {
                if (expression is not AssignmentExpressionSyntax assignment) {
                    continue;
                }

                if (TryAppendObjectInitializerSetterAssignment(semantic, context, objectCreationTypeSyntax, objectName, memberAccessOperator, assignment, lines)) {
                    continue;
                }

                int startRight = context.DepthClass;
                List<string> rightLines = new List<string>();
                ExpressionResult rightResult = ProcessExpression(semantic, context, assignment.Right, rightLines);
                context.PopClass(startRight);
                if (rightResult.BeforeLines != null && rightResult.BeforeLines.Count > 0) {
                    lines.AddRange(rightResult.BeforeLines);
                }

                lines.Add(objectName);
                lines.Add(memberAccessOperator);
                AppendObjectInitializerTarget(semantic, context, assignment.Left, lines);
                lines.Add(" = ");
                lines.AddRange(rightLines);

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
            if (expression is IdentifierNameSyntax identifierName) {
                lines.Add(identifierName.Identifier.Text);
                return;
            }

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
            return BuildObjectCreationExpression(
                semantic,
                context,
                objectCreation.Type,
                ResolveObjectCreationTypeSymbol(semantic, objectCreation),
                objectCreation.ArgumentList,
                objectCreation,
                lines);
        }

        ExpressionResult BuildObjectCreationExpression(
            SemanticModel semantic,
            LayerContext context,
            TypeSyntax objectCreationTypeSyntax,
            ITypeSymbol objectCreationTypeSymbol,
            ArgumentListSyntax argumentList,
            ObjectCreationExpressionSyntax semanticObjectCreation,
            List<string> lines) {
            if (IsSystemObjectType(semantic, objectCreationTypeSyntax)) {
                lines.Add("new char[1]");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("object"));
            }

            if (TryProcessStringObjectCreation(semantic, context, objectCreationTypeSyntax, argumentList, lines, out VariableType stringObjectCreationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, stringObjectCreationType);
            }

            int diagnosticCount = GetDiagnosticCount();
            VariableType sourceType = null;
            VariableType cppType = sourceType;
            bool hasConvertedType = false;
            CPPTypeData cppTypeData = default;
            string objectCreationTypeName = objectCreationTypeSyntax.ToString();
            int generatedTypeArity = objectCreationTypeSymbol is INamedTypeSymbol namedObjectCreationTypeSymbol
                ? namedObjectCreationTypeSymbol.TypeArguments.Length
                : 0;
            ConversionClass explicitGeneratedClass = objectCreationTypeSymbol is INamedTypeSymbol namedGeneratedObjectCreationTypeSymbol
                ? context.Program.FindGeneratedClass(namedGeneratedObjectCreationTypeSymbol)
                : null;
            explicitGeneratedClass ??= context.Program.FindGeneratedClass(objectCreationTypeName, generatedTypeArity);
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
                sourceType = objectCreationTypeSymbol != null
                    ? VariableUtil.GetVarType(objectCreationTypeSymbol)
                    : VariableUtil.GetVarType(objectCreationTypeSyntax, semantic);
            } else {
                if (objectCreationTypeSymbol != null) {
                    sourceType = VariableUtil.GetVarType(objectCreationTypeSymbol);
                } else if (semanticObjectCreation != null &&
                    TryGetExpressionTypeSymbol(semantic, semanticObjectCreation, out ITypeSymbol createdTypeSymbol)) {
                    sourceType = VariableUtil.GetVarType(createdTypeSymbol);
                }
            }

            if (sourceType != null) {
                cppType = ConvertToCPPType(sourceType, out cppTypeData);
                hasConvertedType = cppType != null;
                RegisterGeneratedTypeReferences(context, sourceType);
            }

            bool emitHeapAllocation = !hasConvertedType
                ? !(IsValueRuntimeTypeName(objectCreationTypeSyntax.ToString()) || objectCreationTypeSymbol?.IsValueType == true)
                : cppTypeData.IsPointer;
            IMethodSymbol constructorSymbol = semanticObjectCreation != null
                ? ResolveObjectCreationConstructorSymbol(semantic, semanticObjectCreation)
                : ResolveObjectCreationConstructorSymbol(objectCreationTypeSymbol, argumentList);
            System.Collections.Immutable.ImmutableArray<IParameterSymbol> constructorParameterSymbols = constructorSymbol != null
                ? constructorSymbol.Parameters
                : System.Collections.Immutable.ImmutableArray<IParameterSymbol>.Empty;
            int explicitArgumentCount = argumentList?.Arguments.Count ?? 0;
            bool hasOptionalConstructorArguments = constructorParameterSymbols.Length > explicitArgumentCount &&
                constructorParameterSymbols.Skip(explicitArgumentCount).Any(parameter => parameter.HasExplicitDefaultValue);
            bool requiresStableArgumentEvaluation = explicitArgumentCount > 1 &&
                argumentList.Arguments.Any(argument => RequiresStableConstructorArgumentEvaluation(argument.Expression));
            if (requiresStableArgumentEvaluation) {
                lines.Add($"({GetObjectConstructionLambdaCaptureList(context)}() {{\n");

                List<string> temporaryArgumentNames = new List<string>();
                List<string> constructorBeforeLines = new List<string>();
                for (int i = 0; i < explicitArgumentCount; i++) {
                    ArgumentSyntax arg = argumentList.Arguments[i];
                    string temporaryName = CreateTemporaryName("__ctor_arg");
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
                        constructorSymbol,
                        constructorBeforeLines,
                        loweredArgumentLines);

                    if (constructorBeforeLines.Count > 0) {
                        lines.AddRange(constructorBeforeLines);
                        constructorBeforeLines.Clear();
                    }

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
                    objectCreationTypeSyntax,
                    lines,
                    emitHeapAllocation,
                    hasConvertedType,
                    explicitGeneratedClass,
                    cppType,
                    hasRuntimeObjectTypeMapping,
                    runtimeObjectTypeName);
                lines.Add("(");
                for (int i = 0; i < temporaryArgumentNames.Count; i++) {
                    IParameterSymbol parameterSymbol = i < constructorParameterSymbols.Length
                        ? constructorParameterSymbols[i]
                        : null;
                    if (ShouldCastConstructorArgumentForReferenceOverload(constructorSymbol, i, parameterSymbol)) {
                        AppendConstructorReferenceDisambiguation(
                            context,
                            parameterSymbol,
                            new List<string> { temporaryArgumentNames[i] },
                            lines);
                    } else if (ShouldWrapConstructorArgumentForByValueOverload(constructorSymbol, i, parameterSymbol)) {
                        AppendConstructorByValueDisambiguation(
                            context,
                            parameterSymbol,
                            new List<string> { temporaryArgumentNames[i] },
                            lines);
                    } else {
                        lines.Add(temporaryArgumentNames[i]);
                    }

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
                objectCreationTypeSyntax,
                lines,
                emitHeapAllocation,
                hasConvertedType,
                explicitGeneratedClass,
                cppType,
                hasRuntimeObjectTypeMapping,
                runtimeObjectTypeName);
            lines.Add("(");
            List<string> argumentLines = new List<string>();
            if (argumentList != null) {
                for (int i = 0; i < argumentList.Arguments.Count; i++) {
                    ArgumentSyntax arg = argumentList.Arguments[i];
                    List<string> argumentExpressionLines = new List<string>();

                    int startArg = context.DepthClass;
                    ExpressionResult argumentResult = ProcessExpression(semantic, context, arg.Expression, argumentExpressionLines);
                    context.PopClass(startArg);

                    IParameterSymbol parameterSymbol = i < constructorParameterSymbols.Length
                        ? constructorParameterSymbols[i]
                        : null;
                    List<string> beforeLines = argumentResult.BeforeLines != null
                        ? new List<string>(argumentResult.BeforeLines)
                        : new List<string>();
                    int argumentStartIndex = argumentLines.Count;
                    AppendInvocationArgument(
                        semantic,
                        context,
                        arg.Expression,
                        argumentExpressionLines,
                        parameterSymbol,
                        constructorSymbol,
                        beforeLines,
                        argumentLines);

                    if (ShouldCastConstructorArgumentForReferenceOverload(constructorSymbol, i, parameterSymbol)) {
                        CastConstructorArgumentRangeToReference(context, parameterSymbol, argumentLines, argumentStartIndex);
                    } else if (ShouldWrapConstructorArgumentForByValueOverload(constructorSymbol, i, parameterSymbol)) {
                        WrapConstructorArgumentRange(context, parameterSymbol, argumentLines, argumentStartIndex);
                    }

                    if (beforeLines.Count > 0) {
                        lines.AddRange(beforeLines);
                    }

                    if (i != argumentList.Arguments.Count - 1 ||
                        (i == argumentList.Arguments.Count - 1 && hasOptionalConstructorArguments)) {
                        argumentLines.Add(", ");
                    }
                }
            }

            AppendOptionalInvocationArguments(constructorParameterSymbols, explicitArgumentCount, argumentLines);
            lines.AddRange(argumentLines);
            lines.Add(")");
            return new ExpressionResult(diagnosticCount == GetDiagnosticCount(), VariablePath.Unknown, cppType);
        }

        static bool ShouldWrapConstructorArgumentForByValueOverload(
            IMethodSymbol constructorSymbol,
            int argumentIndex,
            IParameterSymbol parameterSymbol) {
            if (constructorSymbol == null ||
                constructorSymbol.MethodKind != MethodKind.Constructor ||
                parameterSymbol == null ||
                parameterSymbol.RefKind != RefKind.None ||
                argumentIndex < 0 ||
                argumentIndex >= constructorSymbol.Parameters.Length ||
                constructorSymbol.ContainingType == null) {
                return false;
            }

            foreach (IMethodSymbol candidate in constructorSymbol.ContainingType.InstanceConstructors) {
                if (candidate == null ||
                    SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, constructorSymbol.OriginalDefinition) ||
                    candidate.Parameters.Length != constructorSymbol.Parameters.Length ||
                    candidate.Parameters[argumentIndex].RefKind != RefKind.Ref) {
                    continue;
                }

                bool parametersMatch = true;
                for (int parameterIndex = 0; parameterIndex < constructorSymbol.Parameters.Length; parameterIndex++) {
                    IParameterSymbol selectedParameter = constructorSymbol.Parameters[parameterIndex];
                    IParameterSymbol candidateParameter = candidate.Parameters[parameterIndex];
                    if (!SymbolEqualityComparer.Default.Equals(selectedParameter.Type, candidateParameter.Type)) {
                        parametersMatch = false;
                        break;
                    }

                    if (parameterIndex != argumentIndex &&
                        selectedParameter.RefKind != candidateParameter.RefKind) {
                        parametersMatch = false;
                        break;
                    }
                }

                if (parametersMatch) {
                    return true;
                }
            }

            return false;
        }

        static bool ShouldCastConstructorArgumentForReferenceOverload(
            IMethodSymbol constructorSymbol,
            int argumentIndex,
            IParameterSymbol parameterSymbol) {
            if (constructorSymbol == null ||
                constructorSymbol.MethodKind != MethodKind.Constructor ||
                parameterSymbol == null ||
                parameterSymbol.RefKind != RefKind.Ref ||
                argumentIndex < 0 ||
                argumentIndex >= constructorSymbol.Parameters.Length ||
                constructorSymbol.ContainingType == null) {
                return false;
            }

            foreach (IMethodSymbol candidate in constructorSymbol.ContainingType.InstanceConstructors) {
                if (candidate == null ||
                    SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, constructorSymbol.OriginalDefinition) ||
                    candidate.Parameters.Length != constructorSymbol.Parameters.Length ||
                    candidate.Parameters[argumentIndex].RefKind != RefKind.None) {
                    continue;
                }

                bool parametersMatch = true;
                for (int parameterIndex = 0; parameterIndex < constructorSymbol.Parameters.Length; parameterIndex++) {
                    IParameterSymbol selectedParameter = constructorSymbol.Parameters[parameterIndex];
                    IParameterSymbol candidateParameter = candidate.Parameters[parameterIndex];
                    if (!SymbolEqualityComparer.Default.Equals(selectedParameter.Type, candidateParameter.Type)) {
                        parametersMatch = false;
                        break;
                    }

                    if (parameterIndex != argumentIndex &&
                        selectedParameter.RefKind != candidateParameter.RefKind) {
                        parametersMatch = false;
                        break;
                    }
                }

                if (parametersMatch) {
                    return true;
                }
            }

            return false;
        }

        void WrapConstructorArgumentRange(
            LayerContext context,
            IParameterSymbol parameterSymbol,
            List<string> argumentLines,
            int startIndex) {
            if (parameterSymbol == null ||
                argumentLines == null ||
                startIndex < 0 ||
                startIndex >= argumentLines.Count) {
                return;
            }

            List<string> existingArgumentLines = argumentLines.GetRange(startIndex, argumentLines.Count - startIndex);
            argumentLines.RemoveRange(startIndex, argumentLines.Count - startIndex);
            AppendConstructorByValueDisambiguation(context, parameterSymbol, existingArgumentLines, argumentLines);
        }

        void CastConstructorArgumentRangeToReference(
            LayerContext context,
            IParameterSymbol parameterSymbol,
            List<string> argumentLines,
            int startIndex) {
            if (parameterSymbol == null ||
                argumentLines == null ||
                startIndex < 0 ||
                startIndex >= argumentLines.Count) {
                return;
            }

            List<string> existingArgumentLines = argumentLines.GetRange(startIndex, argumentLines.Count - startIndex);
            argumentLines.RemoveRange(startIndex, argumentLines.Count - startIndex);
            AppendConstructorReferenceDisambiguation(context, parameterSymbol, existingArgumentLines, argumentLines);
        }

        void AppendConstructorByValueDisambiguation(
            LayerContext context,
            IParameterSymbol parameterSymbol,
            List<string> argumentExpressionLines,
            List<string> lines) {
            VariableType parameterType = VariableUtil.GetVarType(parameterSymbol.Type);
            VariableType cppParameterType = ConvertToCPPType(parameterType, out CPPTypeData cppTypeData);
            string parameterTypeName = QualifyRenderedCppTypeName(cppParameterType.ToCPPString(context.Program), context);
            lines.Add(parameterTypeName);
            lines.Add("(");
            lines.AddRange(argumentExpressionLines);
            lines.Add(")");
        }

        void AppendConstructorReferenceDisambiguation(
            LayerContext context,
            IParameterSymbol parameterSymbol,
            List<string> argumentExpressionLines,
            List<string> lines) {
            VariableType parameterType = VariableUtil.GetVarType(parameterSymbol.Type);
            VariableType cppParameterType = ConvertToCPPType(parameterType, out CPPTypeData cppTypeData);
            string parameterTypeName = QualifyRenderedCppTypeName(cppParameterType.ToCPPString(context.Program), context);
            lines.Add("static_cast<");
            lines.Add(parameterTypeName);
            lines.Add("&>(");
            lines.AddRange(argumentExpressionLines);
            lines.Add(")");
        }

        bool TryProcessDelegateObjectCreationExpression(
            SemanticModel semantic,
            LayerContext context,
            ObjectCreationExpressionSyntax objectCreation,
            List<string> lines,
            out VariableType delegateObjectCreationType) {
            return TryProcessDelegateObjectCreationExpression(
                semantic,
                context,
                objectCreation?.Type,
                ResolveObjectCreationTypeSymbol(semantic, objectCreation),
                objectCreation?.ArgumentList,
                lines,
                out delegateObjectCreationType);
        }

        bool TryProcessDelegateObjectCreationExpression(
            SemanticModel semantic,
            LayerContext context,
            TypeSyntax objectCreationTypeSyntax,
            ITypeSymbol objectCreationTypeSymbol,
            ArgumentListSyntax argumentList,
            List<string> lines,
            out VariableType delegateObjectCreationType) {
            delegateObjectCreationType = null;
            if (semantic == null ||
                context == null ||
                objectCreationTypeSyntax == null ||
                objectCreationTypeSymbol is not INamedTypeSymbol namedDelegateTypeSymbol ||
                namedDelegateTypeSymbol.TypeKind != TypeKind.Delegate ||
                lines == null) {
                return false;
            }

            delegateObjectCreationType = VariableUtil.GetVarType(namedDelegateTypeSymbol);
            RegisterGeneratedTypeReferences(context, delegateObjectCreationType);
            VariableType cppType = ConvertToCPPType(delegateObjectCreationType, out CPPTypeData typeData);
            string emittedDelegateTypeName = cppType != null
                ? QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context)
                : QualifyRenderedCppTypeName(GetCustomDelegateWrapperTypeName(namedDelegateTypeSymbol, context), context);

            if (typeData.IsPointer) {
                lines.Add("new ");
            }

            lines.Add(emittedDelegateTypeName);
            lines.Add("(");

            if (argumentList != null) {
                for (int argumentIndex = 0; argumentIndex < argumentList.Arguments.Count; argumentIndex++) {
                    ArgumentSyntax argument = argumentList.Arguments[argumentIndex];
                    if (!TryAppendDelegateObjectCreationArgument(semantic, context, argument.Expression, lines)) {
                        int argumentStart = context.DepthClass;
                        ProcessExpression(semantic, context, argument.Expression, lines);
                        context.PopClass(argumentStart);
                    }

                    if (argumentIndex < argumentList.Arguments.Count - 1) {
                        lines.Add(", ");
                    }
                }
            }

            lines.Add(")");
            return true;
        }

        bool TryAppendDelegateObjectCreationArgument(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax argumentExpression,
            List<string> lines) {
            if (semantic == null ||
                context == null ||
                argumentExpression == null ||
                lines == null ||
                argumentExpression is AnonymousFunctionExpressionSyntax ||
                !ReferenceEquals(argumentExpression.SyntaxTree, semantic.SyntaxTree)) {
                return false;
            }

            IMethodSymbol methodGroupSymbol = ResolveMethodSymbol(semantic.GetSymbolInfo(argumentExpression));
            if (methodGroupSymbol == null) {
                return false;
            }

            if (methodGroupSymbol.IsStatic) {
                lines.Add(RenderQualifiedMethodPointerTarget(methodGroupSymbol, context));
                return true;
            }

            if (!TryResolveBoundDelegateReceiverText(semantic, context, argumentExpression, methodGroupSymbol, out string receiverText)) {
                return false;
            }

            lines.Add("std::bind_front(");
            lines.Add(RenderQualifiedMethodPointerTarget(methodGroupSymbol, context));
            lines.Add(", ");
            lines.Add(receiverText);
            lines.Add(")");
            return true;
        }

        void AppendObjectCreationTypePrefix(
            SemanticModel semantic,
            LayerContext context,
            TypeSyntax objectCreationTypeSyntax,
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
                lines.Add(QualifyObjectCreationTypeName(cppType.ToCPPString(context.Program), context));
            } else if (hasRuntimeObjectTypeMapping) {
                lines.Add(runtimeObjectTypeName);
            } else {
                int startDepth = context.DepthClass;
                ProcessExpression(semantic, context, objectCreationTypeSyntax, lines);
                context.PopClass(startDepth);
            }
        }

        string QualifyObjectCreationTypeName(string renderedTypeName, LayerContext context) {
            string qualifiedTypeName = QualifyRenderedCppTypeName(renderedTypeName, context);
            if (string.IsNullOrWhiteSpace(qualifiedTypeName) ||
                qualifiedTypeName.StartsWith("::", StringComparison.Ordinal)) {
                return qualifiedTypeName;
            }

            if (qualifiedTypeName.StartsWith("Span<", StringComparison.Ordinal) ||
                qualifiedTypeName.StartsWith("ReadOnlySpan<", StringComparison.Ordinal) ||
                qualifiedTypeName.StartsWith("Array<", StringComparison.Ordinal)) {
                return $"::{qualifiedTypeName}";
            }

            return qualifiedTypeName;
        }

        bool TryProcessStringObjectCreation(
            SemanticModel semantic,
            LayerContext context,
            TypeSyntax objectCreationTypeSyntax,
            ArgumentListSyntax argumentList,
            List<string> lines,
            out VariableType resultType) {
            resultType = null;

            if (!IsStringRuntimeTypeReference(semantic, objectCreationTypeSyntax) ||
                argumentList?.Arguments.Count != 1) {
                return false;
            }

            ArgumentSyntax sourceArgument = argumentList.Arguments[0];
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
            if (semantic == null || objectCreation == null) {
                return null;
            }

            if (!ReferenceEquals(objectCreation.SyntaxTree, semantic.SyntaxTree)) {
                return ResolveObjectCreationTypeSymbolFromText(semantic, objectCreation.Type?.ToString());
            }

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

            if (semantic == null || objectCreation == null) {
                return null;
            }

            if (!ReferenceEquals(objectCreation.SyntaxTree, semantic.SyntaxTree)) {
                if (ResolveObjectCreationTypeSymbol(semantic, objectCreation) is INamedTypeSymbol fallbackNamedTypeSymbol) {
                    return fallbackNamedTypeSymbol.InstanceConstructors
                        .FirstOrDefault(constructorSymbol => CanMethodMatchInvocationArguments(constructorSymbol, argumentCount));
                }

                return null;
            }

            SymbolInfo objectCreationSymbolInfo = semantic.GetSymbolInfo(objectCreation);
            IMethodSymbol candidateConstructorSymbol = ResolveBestObjectCreationCandidateMethodSymbol(semantic, objectCreation, objectCreationSymbolInfo, argumentCount);
            if (candidateConstructorSymbol != null) {
                return candidateConstructorSymbol;
            }

            if (semantic.GetOperation(objectCreation) is IObjectCreationOperation objectCreationOperation &&
                CanMethodMatchInvocationArguments(objectCreationOperation.Constructor, argumentCount)) {
                return objectCreationOperation.Constructor;
            }

            IMethodSymbol constructorSymbol = ResolveMethodSymbol(objectCreationSymbolInfo);
            if (CanMethodMatchInvocationArguments(constructorSymbol, argumentCount)) {
                return constructorSymbol;
            }

            return ResolveBestObjectCreationCandidateMethodSymbol(semantic, objectCreation, objectCreationSymbolInfo, argumentCount) ??
                ResolveBestInvocationCandidateMethodSymbol(objectCreationSymbolInfo, argumentCount);
        }

        static IMethodSymbol ResolveObjectCreationConstructorSymbol(ITypeSymbol objectCreationTypeSymbol, ArgumentListSyntax argumentList) {
            if (objectCreationTypeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return null;
            }

            int argumentCount = argumentList?.Arguments.Count ?? 0;
            return namedTypeSymbol.InstanceConstructors
                .FirstOrDefault(constructorSymbol => CanMethodMatchInvocationArguments(constructorSymbol, argumentCount));
        }

        static ITypeSymbol ResolveObjectCreationTypeSymbolFromText(SemanticModel semantic, string typeName) {
            if (semantic?.Compilation == null || string.IsNullOrWhiteSpace(typeName)) {
                return null;
            }

            string normalizedTypeName = typeName.Replace("global::", string.Empty, StringComparison.Ordinal);
            ITypeSymbol resolvedTypeSymbol = semantic.Compilation.GetTypeByMetadataName(normalizedTypeName);
            if (resolvedTypeSymbol != null) {
                return resolvedTypeSymbol;
            }

            string simpleTypeName = normalizedTypeName.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(simpleTypeName)) {
                return null;
            }

            foreach (INamedTypeSymbol candidateTypeSymbol in semantic.Compilation.GetSymbolsWithName(simpleTypeName).OfType<INamedTypeSymbol>()) {
                if (string.Equals(candidateTypeSymbol.ToDisplayString(), normalizedTypeName, StringComparison.Ordinal) ||
                    string.Equals(candidateTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), $"global::{normalizedTypeName}", StringComparison.Ordinal)) {
                    return candidateTypeSymbol;
                }
            }

            return null;
        }

        static IMethodSymbol ResolveBestObjectCreationCandidateMethodSymbol(
            SemanticModel semantic,
            ObjectCreationExpressionSyntax objectCreation,
            SymbolInfo symbolInfo,
            int argumentCount) {
            return symbolInfo.CandidateSymbols
                .OfType<IMethodSymbol>()
                .Select(candidateMethodSymbol => new {
                    Method = candidateMethodSymbol,
                    Score = ScoreObjectCreationCandidateMethod(semantic, objectCreation, candidateMethodSymbol, argumentCount)
                })
                .Where(candidate => candidate.Score >= 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Method.Parameters.Length == argumentCount)
                .ThenByDescending(candidate => candidate.Method.Parameters.Length)
                .Select(candidate => candidate.Method)
                .FirstOrDefault();
        }

        static int ScoreObjectCreationCandidateMethod(
            SemanticModel semantic,
            ObjectCreationExpressionSyntax objectCreation,
            IMethodSymbol methodSymbol,
            int argumentCount) {
            if (!CanMethodMatchInvocationArguments(methodSymbol, argumentCount)) {
                return -1;
            }

            int score = methodSymbol.Parameters.Length == argumentCount ? 1000 : 0;
            SeparatedSyntaxList<ArgumentSyntax> arguments = objectCreation.ArgumentList?.Arguments ?? default;
            for (int argumentIndex = 0; argumentIndex < arguments.Count; argumentIndex++) {
                IParameterSymbol parameterSymbol = ResolveInvocationParameter(methodSymbol, argumentIndex);
                if (parameterSymbol == null) {
                    return -1;
                }

                if (!MatchesArgumentModifier(arguments[argumentIndex], parameterSymbol)) {
                    return -1;
                }

                ITypeSymbol argumentTypeSymbol = semantic.GetTypeInfo(arguments[argumentIndex].Expression).ConvertedType ??
                    semantic.GetTypeInfo(arguments[argumentIndex].Expression).Type;
                if (argumentTypeSymbol == null) {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(argumentTypeSymbol, parameterSymbol.Type)) {
                    score += 100;
                    continue;
                }

                Conversion conversion = semantic.ClassifyConversion(arguments[argumentIndex].Expression, parameterSymbol.Type);
                if (!conversion.Exists) {
                    return -1;
                }

                if (conversion.IsIdentity) {
                    score += 90;
                } else if (conversion.IsImplicit) {
                    score += 60;
                } else if (conversion.IsReference) {
                    score += 40;
                } else if (conversion.IsBoxing || conversion.IsUnboxing) {
                    score += 20;
                } else {
                    score += 1;
                }
            }

            return score;
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

            if (string.Equals(shortTypeName, "Guid", StringComparison.Ordinal) ||
                string.Equals(shortTypeName, "System.Guid", StringComparison.Ordinal) ||
                string.Equals(shortTypeName, "global::System.Guid", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Guid", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Guid", StringComparison.Ordinal)) {
                runtimeTypeName = "Guid";
                runtimeRequirementName = "Guid";
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

            if (TryProcessPrimitiveNumberRuntimeMemberAccess(semantic, memberAccess, lines, out VariableType primitiveNumberType)) {
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

            ISymbol staticReceiverSymbol = semantic.GetSymbolInfo(memberAccess.Expression).Symbol;
            if (staticReceiverSymbol is IAliasSymbol staticReceiverAliasSymbol) {
                staticReceiverSymbol = staticReceiverAliasSymbol.Target;
            }

            ExpressionResult result;
            if (staticReceiverSymbol is INamespaceSymbol) {
                lines.Add(memberAccess.Expression.ToString());
                result = new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType("object"));
            } else if (staticReceiverSymbol is INamedTypeSymbol staticReceiverTypeSymbol) {
                lines.Add(GetContainingTypeAccessName(context, staticReceiverTypeSymbol));
                result = new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType(staticReceiverTypeSymbol));
            } else {
                List<string> receiverLines = new List<string>();
                result = ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
                if (result.BeforeLines != null && result.BeforeLines.Count > 0) {
                    lines.AddRange(result.BeforeLines);
                }

                lines.AddRange(receiverLines);
            }

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
                if (IsPointerBackedExpressionType(context, semantic, memberAccess.Expression, result.Type)) {
                    useDirectMemberAccess = false;
                }

                IPropertySymbol resolvedPropertySymbol = memberSymbol as IPropertySymbol;
                if (resolvedPropertySymbol == null &&
                    memberAccess.Name is IdentifierNameSyntax propertyIdentifier &&
                    TryResolveReceiverPropertySymbol(semantic, memberAccess.Expression, propertyIdentifier.Identifier.Text, out IPropertySymbol fallbackPropertySymbol)) {
                    resolvedPropertySymbol = fallbackPropertySymbol;
                    memberSymbol = fallbackPropertySymbol;
                }

                if (resolvedPropertySymbol != null &&
                    TryBuildPropertyGetterCall(memberAccess, resolvedPropertySymbol, out string propertyGetterCallName)) {
                    AppendMemberAccessSeparator(lines, useStaticAccess, result.VarPath, useDirectMemberAccess);
                    lines.Add(propertyGetterCallName);
                    VariableType propertyType = VariableUtil.GetVarType(resolvedPropertySymbol.Type);
                    VariablePath propertyPath = ResolveMemberAccessResultPath(useStaticAccess, result.VarPath, memberSymbol);
                    return new ExpressionResult(true, propertyPath, propertyType);
                }

                if (memberSymbol == null &&
                    memberAccess.Name is IdentifierNameSyntax memberIdentifier &&
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

                ConversionClass staticReceiverGeneratedClass = useStaticAccess
                    ? TryResolveGeneratedReceiverClass(context, result.Type)
                    : null;

                if (memberAccess.Parent is InvocationExpressionSyntax containingInvocation &&
                    ReferenceEquals(containingInvocation.Expression, memberAccess)) {
                    IMethodSymbol invokedMemberMethodSymbol = ResolveInvokedMethodSymbol(semantic, containingInvocation);
                    if (invokedMemberMethodSymbol != null &&
                        (TryResolveGeneratedContainingClass(invokedMemberMethodSymbol, out _) ||
                         staticReceiverGeneratedClass != null)) {
                        if (ShouldEmitDependentTemplateQualifier(invokedMemberMethodSymbol, ReceiverRequiresDependentTemplateQualifier(semantic, memberAccess.Expression))) {
                            lines.Add("template ");
                        }

                        lines.Add(ResolveConvertedFunctionName(invokedMemberMethodSymbol));
                        AppendInvocationGenericArgumentsFromSyntax(semantic, context, containingInvocation.Expression, lines);
                        VariableType resolvedInvocationType = ResolveMemberAccessResultType(invokedMemberMethodSymbol) ?? VariableUtil.GetVarType(invokedMemberMethodSymbol.ReturnType);
                        VariablePath resolvedInvocationPath = ResolveMemberAccessResultPath(useStaticAccess, result.VarPath, invokedMemberMethodSymbol);
                        return new ExpressionResult(true, resolvedInvocationPath, resolvedInvocationType);
                    }
                }

                if (useStaticAccess &&
                    result.Type?.IsEnum == true &&
                    memberAccess.Name is IdentifierNameSyntax enumMemberIdentifier) {
                    lines.Add(enumMemberIdentifier.Identifier.Text);
                    return new ExpressionResult(true, VariablePath.Static, result.Type);
                }

                ConversionClass receiverGeneratedClass = !useStaticAccess
                    ? TryResolveGeneratedReceiverClass(context, result.Type)
                    : staticReceiverGeneratedClass;
                ExpressionResult memberNameResult = ProcessMemberNameWithReceiverContext(
                    semantic,
                    context,
                    memberAccess.Name,
                    lines,
                    refTypes,
                    receiverGeneratedClass);
                VariableType resolvedMemberType = ResolveMemberAccessResultType(memberSymbol) ?? memberNameResult.Type;
                VariablePath resolvedMemberPath = ResolveMemberAccessResultPath(useStaticAccess, result.VarPath, memberSymbol);
                return new ExpressionResult(memberNameResult.Processed, resolvedMemberPath, resolvedMemberType, memberNameResult.BeforeLines, memberNameResult.AfterLines);
            }
            return ProcessExpression(semantic, context, memberAccess.Name, lines, refTypes);
        }

        ExpressionResult ProcessMemberNameWithReceiverContext(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax memberNameExpression,
            List<string> lines,
            List<ExpressionResult> refTypes,
            ConversionClass receiverGeneratedClass) {
            int startDepth = context.DepthClass;
            if (receiverGeneratedClass != null) {
                context.AddClass(receiverGeneratedClass);
            }

            ExpressionResult memberNameResult = ProcessExpression(semantic, context, memberNameExpression, lines, refTypes);
            context.PopClass(startDepth);
            return memberNameResult;
        }

        ConversionClass TryResolveGeneratedReceiverClass(LayerContext context, VariableType receiverType) {
            if (context?.Program == null || receiverType == null) {
                return null;
            }

            ConversionClass generatedClass = context.Program.FindGeneratedClass(receiverType);
            if (generatedClass != null) {
                return generatedClass;
            }

            VariableType cppReceiverType = ConvertToCPPType(receiverType, out _);
            if (cppReceiverType == null) {
                return null;
            }

            return context.Program.FindGeneratedClass(cppReceiverType);
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
                if (IsPointerBackedExpressionType(context, semantic, memberAccess.Expression, receiverResult.Type)) {
                    useDirectMemberAccess = false;
                }
                AppendMemberAccessSeparator(lines, useStaticAccess, receiverResult.VarPath, useDirectMemberAccess);
            } else {
                return false;
            }

            lines.Add($"set_{propertyName}(");
            if (ShouldEmitEmptyStringForTargetedNullAssignment(semantic, assignment.Left, assignment.Right)) {
                lines.Add("std::string()");
            } else if (TryAppendDelegateMethodGroupAssignmentValue(semantic, context, assignment, lines)) {
            } else {
                int rightStartDepth = context.Class.Count;
                List<string> rightLines = new List<string>();
                ProcessExpression(semantic, context, assignment.Right, rightLines);
                context.PopClass(rightStartDepth);
                if (!TryAppendDelegateLambdaAssignmentValue(semantic, context, assignment, rightLines, lines)) {
                    lines.AddRange(rightLines);
                }
            }
            lines.Add(")");
            return true;
        }

        bool TryProcessRefReturnPropertyAssignment(SemanticModel semantic, LayerContext context, AssignmentExpressionSyntax assignment, List<string> lines) {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                !TryGetAssignedPropertySymbol(semantic, assignment.Left, out IPropertySymbol propertySymbol) ||
                propertySymbol.SetMethod != null ||
                (!propertySymbol.ReturnsByRef && !propertySymbol.ReturnsByRefReadonly)) {
                return false;
            }

            string propertyName = propertySymbol.Name;
            if (assignment.Left is IdentifierNameSyntax) {
                if (propertySymbol.IsStatic) {
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
                if (IsPointerBackedExpressionType(context, semantic, memberAccess.Expression, receiverResult.Type)) {
                    useDirectMemberAccess = false;
                }

                AppendMemberAccessSeparator(lines, useStaticAccess, receiverResult.VarPath, useDirectMemberAccess);
            } else {
                return false;
            }

            lines.Add($"get_{propertyName}()");
            lines.Add(" = ");
            if (ShouldEmitEmptyStringForTargetedNullAssignment(semantic, assignment.Left, assignment.Right)) {
                lines.Add("std::string()");
            } else if (TryAppendDelegateMethodGroupAssignmentValue(semantic, context, assignment, lines)) {
            } else {
                int rightStartDepth = context.Class.Count;
                List<string> rightLines = new List<string>();
                ProcessExpression(semantic, context, assignment.Right, rightLines);
                context.PopClass(rightStartDepth);
                if (!TryAppendDelegateLambdaAssignmentValue(semantic, context, assignment, rightLines, lines)) {
                    lines.AddRange(rightLines);
                }
            }

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
            if (semantic == null || expression == null) {
                return false;
            }

            if (semantic.SyntaxTree != expression.SyntaxTree) {
                return false;
            }

            ISymbol symbol;
            try {
                symbol = semantic.GetSymbolInfo(expression).Symbol;
            } catch (ArgumentException) {
                return false;
            }

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

            VariableType sourceType = VariableUtil.GetVarType(containingTypeSymbol);
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);
            string accessName = cppType.ToCPPString(context.Program);

            if (string.IsNullOrWhiteSpace(accessName)) {
                return containingTypeSymbol.Name;
            }

            return TrimStaticTypeAccessName(accessName);
        }

        /// <summary>
        /// Normalizes a converted static type token so it can safely precede a C++ scope-resolution operator.
        /// </summary>
        /// <param name="accessName">Converted type token.</param>
        /// <returns>Type token without leading global qualifiers or pointer suffixes.</returns>
        static string TrimStaticTypeAccessName(string accessName) {
            if (string.IsNullOrWhiteSpace(accessName)) {
                return string.Empty;
            }

            string normalizedAccessName = accessName.Trim();
            while (normalizedAccessName.StartsWith("::", StringComparison.Ordinal)) {
                normalizedAccessName = normalizedAccessName[2..];
            }

            while (normalizedAccessName.EndsWith("*", StringComparison.Ordinal)) {
                normalizedAccessName = normalizedAccessName[..^1].TrimEnd();
            }

            return normalizedAccessName;
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

        bool TryProcessPrimitiveLimitMemberAccess(
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

            if (limitLiteral.StartsWith("Number::", StringComparison.Ordinal)) {
                RegisterRuntimeRequirement("Number");
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
                        case "PositiveInfinity":
                            limitLiteral = "Number::PositiveInfinity<float>()";
                            return true;
                        case "NegativeInfinity":
                            limitLiteral = "Number::NegativeInfinity<float>()";
                            return true;
                        case "NaN":
                            limitLiteral = "Number::NaN<float>()";
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
                        case "PositiveInfinity":
                            limitLiteral = "Number::PositiveInfinity<double>()";
                            return true;
                        case "NegativeInfinity":
                            limitLiteral = "Number::NegativeInfinity<double>()";
                            return true;
                        case "NaN":
                            limitLiteral = "Number::NaN<double>()";
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
            SemanticModel semantic,
            MemberAccessExpressionSyntax memberAccess,
            List<string> lines,
            out VariableType resultType) {
            resultType = null;

            if (semantic == null) {
                throw new ArgumentNullException(nameof(semantic));
            }

            if (memberAccess.Name is not IdentifierNameSyntax identifierName) {
                return false;
            }

            string memberName = identifierName.Identifier.Text;
            SpecialType receiverSpecialType = ResolvePrimitiveStaticReceiverSpecialType(semantic, memberAccess.Expression);
            bool isSupportedNumberMember =
                receiverSpecialType == SpecialType.System_Int32 &&
                string.Equals(memberName, "TryParse", StringComparison.Ordinal) ||
                (receiverSpecialType == SpecialType.System_Single ||
                 receiverSpecialType == SpecialType.System_Double) &&
                (string.Equals(memberName, "IsPositiveInfinity", StringComparison.Ordinal) ||
                 string.Equals(memberName, "IsNaN", StringComparison.Ordinal) ||
                 string.Equals(memberName, "IsInfinity", StringComparison.Ordinal));
            if (isSupportedNumberMember) {
                RegisterRuntimeRequirement("Number");
                lines.Add("Number::");
                lines.Add(memberName);
                resultType = VariableUtil.GetVarType("object");
                return true;
            }

            if (!TryMapPrimitiveMathRuntimeMember(receiverSpecialType, memberName)) {
                return false;
            }

            RegisterRuntimeRequirement("Math");
            if (receiverSpecialType == SpecialType.System_Single) {
                lines.Add("MathF::");
            } else {
                lines.Add("Math::");
            }
            lines.Add(memberName);
            resultType = receiverSpecialType switch {
                SpecialType.System_Int32 => VariableUtil.GetVarType("int"),
                SpecialType.System_Single => VariableUtil.GetVarType("float"),
                SpecialType.System_Double => VariableUtil.GetVarType("double"),
                _ => VariableUtil.GetVarType("object")
            };
            return true;
        }

        static bool TryMapPrimitiveMathRuntimeMember(SpecialType receiverSpecialType, string memberName) {
            if (receiverSpecialType != SpecialType.System_Int32 &&
                receiverSpecialType != SpecialType.System_Single &&
                receiverSpecialType != SpecialType.System_Double) {
                return false;
            }

            return memberName switch {
                "Abs" => true,
                "Acos" => true,
                "Atan2" => true,
                "Ceiling" => true,
                "Clamp" => true,
                "Floor" => true,
                "IsFinite" => true,
                "Log2" => true,
                "Max" => true,
                "Min" => true,
                "MinMagnitude" => true,
                "Round" => true,
                "Sin" => true,
                "Cos" => true,
                "Sqrt" => true,
                "Tan" => true,
                _ => false
            };
        }

        /// <summary>
        /// Resolves the special primitive type represented by one static member receiver so primitive helper calls can be lowered consistently regardless of whether Roslyn surfaced the receiver as a keyword or CLR type symbol.
        /// </summary>
        /// <param name="semantic">Semantic model that owns the member-access expression.</param>
        /// <param name="receiverExpression">Static member receiver expression to inspect.</param>
        /// <returns>The resolved primitive special type when the receiver is a supported primitive static type; otherwise <see cref="SpecialType.None"/>.</returns>
        static SpecialType ResolvePrimitiveStaticReceiverSpecialType(
            SemanticModel semantic,
            ExpressionSyntax receiverExpression) {
            if (receiverExpression is PredefinedTypeSyntax predefinedType) {
                return predefinedType.Keyword.ValueText switch {
                    "int" => SpecialType.System_Int32,
                    "float" => SpecialType.System_Single,
                    "double" => SpecialType.System_Double,
                    _ => SpecialType.None
                };
            }

            ITypeSymbol receiverTypeSymbol = semantic.GetTypeInfo(receiverExpression).Type;
            if (receiverTypeSymbol != null && receiverTypeSymbol.SpecialType != SpecialType.None) {
                return receiverTypeSymbol.SpecialType;
            }

            ISymbol receiverSymbol = semantic.GetSymbolInfo(receiverExpression).Symbol;
            if (receiverSymbol is IAliasSymbol aliasSymbol) {
                receiverSymbol = aliasSymbol.Target;
            }

            if (receiverSymbol is INamedTypeSymbol namedTypeSymbol) {
                return namedTypeSymbol.SpecialType;
            }

            return SpecialType.None;
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
            bool resolvedFromTrackedTupleVariable = false;
            if (!TryResolveTupleMemberAccessSymbol(semantic, memberAccess, out tupleElementIndex, out tupleElementTypeSymbol) &&
                !(resolvedFromTrackedTupleVariable = TryResolveTupleMemberVariableType(context, memberAccess.Expression, identifierName.Identifier.Text, out tupleElementIndex, out tupleElementVariableType)) &&
                !TryResolveTupleMemberTypeSymbol(semantic, memberAccess.Expression, identifierName.Identifier.Text, out tupleElementIndex, out tupleElementTypeSymbol)) {
                    return false;
            }

            string receiverText = RenderExpressionText(semantic, context, memberAccess.Expression);
            bool receiverIsRefLocal = IsRefLocalExpression(semantic, memberAccess.Expression);
            bool trackedReceiverUsesDirectAccess = TryResolveTrackedExpressionVariableType(context, memberAccess.Expression, out VariableType trackedReceiverType) &&
                IsDirectMemberAccessType(trackedReceiverType);
            string memberAccessOperator = receiverIsRefLocal ||
                resolvedFromTrackedTupleVariable ||
                trackedReceiverUsesDirectAccess ||
                UsesDirectMemberAccess(semantic, memberAccess.Expression)
                ? "."
                : "->";
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
        /// Determines whether one expression resolves to a Roslyn ref local so tuple members can preserve direct value-slot access.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the expression.</param>
        /// <param name="expression">Expression to inspect.</param>
        /// <returns><c>true</c> when the expression resolves to a ref local; otherwise <c>false</c>.</returns>
        static bool IsRefLocalExpression(SemanticModel semantic, ExpressionSyntax expression) {
            if (semantic == null || expression == null) {
                return false;
            }

            if (expression is ParenthesizedExpressionSyntax parenthesizedExpression) {
                return IsRefLocalExpression(semantic, parenthesizedExpression.Expression);
            }

            if (expression is CastExpressionSyntax castExpression) {
                return IsRefLocalExpression(semantic, castExpression.Expression);
            }

            if (expression is not IdentifierNameSyntax identifierName) {
                return false;
            }

            return semantic.GetSymbolInfo(identifierName).Symbol is ILocalSymbol localSymbol &&
                localSymbol.RefKind != RefKind.None;
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

            if (TryResolveStaticRuntimeType(semantic, receiverExpression, out _, out _)) {
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
            } else if (receiverSymbol is INamedTypeSymbol receiverTypeSymbol) {
                referencedTypeName = receiverTypeSymbol.Name;
            } else if (receiverResult.Class != null) {
                referencedTypeName = receiverResult.Class.Name;
            }

            if (!string.IsNullOrWhiteSpace(referencedTypeName) &&
                !currentClass.ReferencedClasses.Contains(referencedTypeName)) {
                currentClass.ReferencedClasses.Add(referencedTypeName);
            }
        }

        void RegisterGeneratedTypeDependency(LayerContext context, string referencedTypeName) {
            ConversionClass currentClass = context?.GetCurrentClass();
            if (currentClass == null || string.IsNullOrWhiteSpace(referencedTypeName)) {
                return;
            }

            if (!currentClass.ReferencedClasses.Contains(referencedTypeName)) {
                currentClass.ReferencedClasses.Add(referencedTypeName);
            }
        }

        void AppendInvocationGenericArgumentsFromSyntax(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax invocationTargetExpression,
            List<string> lines) {
            GenericNameSyntax genericName = invocationTargetExpression switch {
                GenericNameSyntax targetGenericName => targetGenericName,
                MemberAccessExpressionSyntax { Name: GenericNameSyntax targetGenericName } => targetGenericName,
                MemberBindingExpressionSyntax { Name: GenericNameSyntax targetGenericName } => targetGenericName,
                _ => null
            };
            if (genericName == null) {
                return;
            }

            lines.Add("<");
            for (int index = 0; index < genericName.TypeArgumentList.Arguments.Count; index++) {
                if (index > 0) {
                    lines.Add(",");
                }

                TypeSyntax genericArgumentSyntax = genericName.TypeArgumentList.Arguments[index];
                VariableType genericArgumentType = VariableUtil.GetVarType(genericArgumentSyntax, semantic);
                RegisterGeneratedTypeReferences(context, genericArgumentType);
                ITypeSymbol genericArgumentTypeSymbol = semantic.GetTypeInfo(genericArgumentSyntax).Type;
                if (genericArgumentTypeSymbol is INamedTypeSymbol namedGenericArgumentTypeSymbol) {
                    ConversionClass symbolResolvedGeneratedClass = context.Program.FindGeneratedClass(namedGenericArgumentTypeSymbol);
                    if (symbolResolvedGeneratedClass != null) {
                        RegisterGeneratedTypeDependency(context, symbolResolvedGeneratedClass.GetEmittedTypeName());
                    }
                }
                string renderedGenericArgumentType = RenderConvertedGenericArgumentType(semantic, context, genericArgumentSyntax);
                ConversionClass renderedGeneratedClass = context.Program.FindGeneratedClass(renderedGenericArgumentType, genericArgumentType.GenericArgs.Count);
                if (renderedGeneratedClass != null) {
                    RegisterGeneratedTypeDependency(context, renderedGeneratedClass.GetEmittedTypeName());
                }
                lines.Add(renderedGenericArgumentType);
            }
            lines.Add(">");
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

            if (TryProcessBinaryPrimitivesInvocation(semantic, context, invocationExpression, lines, out VariableType binaryPrimitivesInvocationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, binaryPrimitivesInvocationType);
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

            if (TryProcessUnsafeInvocation(semantic, context, invocationExpression, lines, out VariableType unsafeInvocationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, unsafeInvocationType);
            }

            if (TryProcessNativeFreeFunctionInvocation(semantic, context, invocationExpression, lines, out VariableType nativeFreeFunctionType)) {
                return new ExpressionResult(true, VariablePath.Unknown, nativeFreeFunctionType);
            }

            if (TryProcessSystemArrayInvocation(semantic, context, invocationExpression, lines, out VariableType systemArrayInvocationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, systemArrayInvocationType);
            }

            if (TryProcessDirectoryInvocation(semantic, context, invocationExpression, lines, out VariableType directoryInvocationType)) {
                return new ExpressionResult(true, VariablePath.Unknown, directoryInvocationType);
            }

            if (TryProcessPrimitiveGetHashCodeInvocation(semantic, context, invocationExpression, lines, out VariableType primitiveGetHashCodeType)) {
                return new ExpressionResult(true, VariablePath.Unknown, primitiveGetHashCodeType);
            }

            if (TryProcessPrimitiveCompareToInvocation(semantic, context, invocationExpression, lines, out VariableType primitiveCompareToType)) {
                return new ExpressionResult(true, VariablePath.Unknown, primitiveCompareToType);
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

            IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (TryProcessReducedExtensionInvocation(semantic, context, invocationExpression, invokedMethodSymbol, lines, out ExpressionResult reducedExtensionResult)) {
                return reducedExtensionResult;
            }

            if (TryProcessMultiImplementationGenericInvocation(semantic, context, invocationExpression, invokedMethodSymbol, lines, out VariableType multiImplementationInvocationType, out List<string> multiImplementationBeforeLines)) {
                return new ExpressionResult(true, VariablePath.Unknown, multiImplementationInvocationType, multiImplementationBeforeLines);
            }

            List<string> argLines = ["("];
            int count = 0;
            List<ExpressionResult> types = new List<ExpressionResult>();

            System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameterSymbols = invokedMethodSymbol != null
                ? invokedMethodSymbol.Parameters
                : System.Collections.Immutable.ImmutableArray<IParameterSymbol>.Empty;

            List<string> beforeLines = new List<string>();

            if (parameterSymbols.Length > 0 &&
                invocationExpression.ArgumentList.Arguments.Any(argument => argument.NameColon != null)) {
                ArgumentSyntax[] alignedArguments = AlignInvocationArguments(invocationExpression.ArgumentList.Arguments, parameterSymbols);
                bool wroteAnyArgument = false;
                for (int parameterIndex = 0; parameterIndex < parameterSymbols.Length; ++parameterIndex) {
                    IParameterSymbol parameterSymbol = parameterSymbols[parameterIndex];
                    ArgumentSyntax argument = alignedArguments[parameterIndex];
                    if (argument == null) {
                        if (!parameterSymbol.HasExplicitDefaultValue) {
                            continue;
                        }

                        if (wroteAnyArgument) {
                            argLines.Add(", ");
                        }

                        AppendOptionalArgumentDefaultValue(parameterSymbol, argLines);
                        wroteAnyArgument = true;
                        continue;
                    }

                    List<string> argumentExpressionLines = new List<string>();
                    int startArg = context.DepthClass;
                    ExpressionResult argumentResult = ProcessExpression(semantic, context, argument.Expression, argumentExpressionLines);
                    context.PopClass(startArg);
                    types.Add(argumentResult);
                    if (argumentResult.BeforeLines != null && argumentResult.BeforeLines.Count > 0) {
                        beforeLines.AddRange(argumentResult.BeforeLines);
                    }

                    if (wroteAnyArgument) {
                        argLines.Add(", ");
                    }

                    AppendInvocationArgument(
                        semantic,
                        context,
                        argument.Expression,
                        argumentExpressionLines,
                        parameterSymbol,
                        invokedMethodSymbol,
                        beforeLines,
                        argLines);

                    if (!string.IsNullOrEmpty(argument.RefKindKeyword.ToString())) {
                        AddRefOrOutDeclarationBeforeLines(semantic, context, argument.Expression, parameterSymbol, beforeLines);
                    }

                    wroteAnyArgument = true;
                }
            } else {
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
                    if (argumentResult.BeforeLines != null && argumentResult.BeforeLines.Count > 0) {
                        beforeLines.AddRange(argumentResult.BeforeLines);
                    }

                    IParameterSymbol parameterSymbol = count < parameterSymbols.Length ? parameterSymbols[count] : null;
                    AppendInvocationArgument(
                        semantic,
                        context,
                        arg.Expression,
                        argumentExpressionLines,
                        parameterSymbol,
                        invokedMethodSymbol,
                        beforeLines,
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
            }
            argLines.Add(")");

            List<string> invocationTargetLines = new List<string>();
            int start = context.DepthClass;
            ExpressionResult result;
            if (TryProcessGeneratedStaticImportedInvocationTarget(semantic, context, invocationExpression, invokedMethodSymbol, invocationTargetLines, out ExpressionResult generatedInvocationTargetResult)) {
                result = generatedInvocationTargetResult;
            } else {
                result = ProcessExpression(semantic, context, invocationExpression.Expression, invocationTargetLines, types);
                context.PopClass(start);
            }
            if (result.BeforeLines != null && result.BeforeLines.Count > 0) {
                beforeLines.AddRange(result.BeforeLines);
            }

            if (invokedMethodSymbol != null) {
                TryRewriteGenericInterfaceInvocationTarget(semantic, context, invocationExpression, invokedMethodSymbol, invocationTargetLines, beforeLines);
            }

            if (invokedMethodSymbol != null) {
                AppendResolvedInvocationTypeArgumentsIfNeeded(invocationExpression, invokedMethodSymbol, context, invocationTargetLines);
            }

            invocationTargetLines.AddRange(argLines);

            if (beforeLines.Count > 0) {
                result.BeforeLines = beforeLines;
            } else {
                result.BeforeLines = null;
            }

            lines.AddRange(invocationTargetLines);
            return result;
        }

        bool TryProcessSystemArrayInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType invocationType) {
            invocationType = null;

            IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (invokedMethodSymbol?.ContainingType == null ||
                !string.Equals(invokedMethodSymbol.ContainingType.ToDisplayString(), "System.Array", StringComparison.Ordinal) ||
                !string.Equals(invokedMethodSymbol.Name, "Clear", StringComparison.Ordinal) ||
                invocationExpression.ArgumentList.Arguments.Count != 3 ||
                !TryResolveArrayElementTypeSymbol(semantic, invocationExpression.ArgumentList.Arguments[0].Expression, out ITypeSymbol elementTypeSymbol)) {
                return false;
            }

            RegisterRuntimeRequirement("NativeArray");
            VariableType elementType = VariableUtil.GetVarType(elementTypeSymbol);
            RegisterGeneratedTypeReferences(context, elementType);
            string elementTypeName = GetCppTypeToken(elementType, context.Program);

            List<string> beforeLines = new List<string>();
            List<string> invocationLines = new List<string> {
                $"Array<{elementTypeName}>::Clear("
            };

            for (int argumentIndex = 0; argumentIndex < invocationExpression.ArgumentList.Arguments.Count; argumentIndex++) {
                ArgumentSyntax argument = invocationExpression.ArgumentList.Arguments[argumentIndex];
                List<string> argumentExpressionLines = new List<string>();
                int argumentStart = context.DepthClass;
                ExpressionResult argumentResult = ProcessExpression(semantic, context, argument.Expression, argumentExpressionLines);
                context.PopClass(argumentStart);
                if (argumentResult.BeforeLines != null && argumentResult.BeforeLines.Count > 0) {
                    beforeLines.AddRange(argumentResult.BeforeLines);
                }

                IParameterSymbol parameterSymbol = argumentIndex < invokedMethodSymbol.Parameters.Length
                    ? invokedMethodSymbol.Parameters[argumentIndex]
                    : null;
                AppendInvocationArgument(
                    semantic,
                    context,
                    argument.Expression,
                    argumentExpressionLines,
                    parameterSymbol,
                    invokedMethodSymbol,
                    beforeLines,
                    invocationLines);

                if (argumentIndex != invocationExpression.ArgumentList.Arguments.Count - 1) {
                    invocationLines.Add(", ");
                }
            }

            invocationLines.Add(")");
            if (beforeLines.Count > 0) {
                lines.AddRange(beforeLines);
            }

            lines.AddRange(invocationLines);
            invocationType = VariableUtil.GetVarType("void");
            return true;
        }

        bool TryProcessReducedExtensionInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess) {
                return false;
            }

            IMethodSymbol extensionMethodSymbol = invokedMethodSymbol?.ReducedFrom;
            if (extensionMethodSymbol == null &&
                invokedMethodSymbol?.IsExtensionMethod == true) {
                ISymbol receiverSymbol = semantic.GetSymbolInfo(memberAccess.Expression).Symbol;
                if (receiverSymbol is IAliasSymbol receiverAliasSymbol) {
                    receiverSymbol = receiverAliasSymbol.Target;
                }

                if (receiverSymbol is not INamespaceSymbol &&
                    receiverSymbol is not INamedTypeSymbol) {
                    extensionMethodSymbol = invokedMethodSymbol;
                }
            }

            if (extensionMethodSymbol == null) {
                return false;
            }

            List<string> beforeLines = new List<string>();
            List<string> invocationLines = new List<string>();
            List<string> receiverLines = new List<string>();
            RegisterGeneratedTypeReferences(context, VariableUtil.GetVarType(extensionMethodSymbol.ContainingType));

            int receiverStart = context.DepthClass;
            ExpressionResult receiverResult = ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(receiverStart);
            if (receiverResult.BeforeLines != null && receiverResult.BeforeLines.Count > 0) {
                beforeLines.AddRange(receiverResult.BeforeLines);
            }

            invocationLines.Add(GetContainingTypeAccessName(context, extensionMethodSymbol.ContainingType));
            invocationLines.Add("::");
            invocationLines.Add(ResolveConvertedFunctionName(extensionMethodSymbol));
            AppendResolvedInvocationTypeArgumentsIfNeeded(invocationExpression, invokedMethodSymbol, context, invocationLines);
            invocationLines.Add("(");

            System.Collections.Immutable.ImmutableArray<IParameterSymbol> extensionParameters = extensionMethodSymbol.Parameters;
            IParameterSymbol receiverParameterSymbol = extensionParameters.Length > 0 ? extensionParameters[0] : null;
            AppendInvocationArgument(
                semantic,
                context,
                memberAccess.Expression,
                receiverLines,
                receiverParameterSymbol,
                extensionMethodSymbol,
                beforeLines,
                invocationLines);

            if (invocationExpression.ArgumentList.Arguments.Count > 0) {
                invocationLines.Add(", ");
            }

            for (int argumentIndex = 0; argumentIndex < invocationExpression.ArgumentList.Arguments.Count; argumentIndex++) {
                ArgumentSyntax argument = invocationExpression.ArgumentList.Arguments[argumentIndex];
                List<string> argumentExpressionLines = new List<string>();
                int argumentStart = context.DepthClass;
                ExpressionResult argumentResult = ProcessExpression(semantic, context, argument.Expression, argumentExpressionLines);
                context.PopClass(argumentStart);
                if (argumentResult.BeforeLines != null && argumentResult.BeforeLines.Count > 0) {
                    beforeLines.AddRange(argumentResult.BeforeLines);
                }

                IParameterSymbol parameterSymbol = argumentIndex + 1 < extensionParameters.Length
                    ? extensionParameters[argumentIndex + 1]
                    : null;
                AppendInvocationArgument(
                    semantic,
                    context,
                    argument.Expression,
                    argumentExpressionLines,
                    parameterSymbol,
                    extensionMethodSymbol,
                    beforeLines,
                    invocationLines);

                if (!string.IsNullOrEmpty(argument.RefKindKeyword.ToString())) {
                    AddRefOrOutDeclarationBeforeLines(semantic, context, argument.Expression, parameterSymbol, beforeLines);
                }

                if (argumentIndex != invocationExpression.ArgumentList.Arguments.Count - 1) {
                    invocationLines.Add(", ");
                }
            }

            invocationLines.Add(")");
            if (beforeLines.Count > 0) {
                lines.AddRange(beforeLines);
            }

            lines.AddRange(invocationLines);
            result = new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType(invokedMethodSymbol.ReturnType));
            return true;
        }

        bool TryProcessGeneratedStaticImportedInvocationTarget(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invokedMethodSymbol == null ||
                !invokedMethodSymbol.IsStatic ||
                !TryResolveGeneratedContainingClass(invokedMethodSymbol, out ConversionClass generatedClass)) {
                return false;
            }

            if (invocationExpression.Expression is not IdentifierNameSyntax &&
                invocationExpression.Expression is not GenericNameSyntax) {
                return false;
            }

            RegisterGeneratedTypeDependency(context, generatedClass.Name);
            lines.Add(GetContainingTypeAccessName(context, invokedMethodSymbol.ContainingType));
            lines.Add("::");
            if (RequiresDependentTemplateQualifier(invokedMethodSymbol)) {
                lines.Add("template ");
            }

            lines.Add(ResolveConvertedFunctionName(invokedMethodSymbol));
            AppendInvocationGenericArgumentsFromSyntax(semantic, context, invocationExpression.Expression, lines);

            VariableType invocationType = VariableUtil.GetVarType(invokedMethodSymbol.ReturnType);
            result = new ExpressionResult(true, VariablePath.Static, invocationType);
            return true;
        }

        /// <summary>
        /// Lowers System.Runtime.CompilerServices.Unsafe intrinsics to explicit native helper calls so generated output does not depend on unresolved managed helper classes.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve the invoked method symbol.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="invocationExpression">Invocation being lowered.</param>
        /// <param name="lines">Output line buffer that receives the lowered expression.</param>
        /// <param name="resultType">Resolved expression result type.</param>
        /// <returns><c>true</c> when the invocation was handled as an Unsafe intrinsic; otherwise <c>false</c>.</returns>
        bool TryProcessUnsafeInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("void");
            IMethodSymbol methodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (!IsUnsafeIntrinsicMethod(methodSymbol)) {
                return false;
            }

            EnsureUnsafeShimInclude(context);

            if (string.Equals(methodSymbol.Name, "SizeOf", StringComparison.Ordinal) &&
                methodSymbol.TypeArguments.Length == 1 &&
                invocationExpression.ArgumentList.Arguments.Count == 0) {
                VariableType targetType = VariableUtil.GetVarType(methodSymbol.TypeArguments[0]);
                RegisterGeneratedTypeReferences(context, targetType);
                lines.Add("he_cpp_unsafe_size_of<");
                lines.Add(RenderUnsafeHelperTypeName(context, targetType));
                lines.Add(">()");
                resultType = VariableUtil.GetVarType("int");
                return true;
            }

            if (string.Equals(methodSymbol.Name, "SkipInit", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 1) {
                ArgumentSyntax argument = invocationExpression.ArgumentList.Arguments[0];
                if (argument.Expression is DeclarationExpressionSyntax declarationExpression &&
                    declarationExpression.Designation is SingleVariableDesignationSyntax) {
                    AddRefOrOutDeclarationBeforeLines(semantic, context, argument.Expression, null, lines);
                }

                lines.Add("(void)0");
                resultType = VariableUtil.GetVarType("void");
                return true;
            }

            if (string.Equals(methodSymbol.Name, "As", StringComparison.Ordinal) &&
                methodSymbol.TypeArguments.Length == 2 &&
                invocationExpression.ArgumentList.Arguments.Count == 1) {
                VariableType targetType = VariableUtil.GetVarType(methodSymbol.TypeArguments[1]);
                RegisterGeneratedTypeReferences(context, targetType);
                lines.Add("he_cpp_unsafe_as<");
                lines.Add(RenderUnsafeHelperTypeName(context, targetType));
                lines.Add(">(");
                lines.Add(RenderUnsafeSourceExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0]));
                lines.Add(")");
                resultType = targetType;
                return true;
            }

            if (string.Equals(methodSymbol.Name, "AsRef", StringComparison.Ordinal) &&
                methodSymbol.TypeArguments.Length == 1 &&
                invocationExpression.ArgumentList.Arguments.Count == 1) {
                VariableType targetType = VariableUtil.GetVarType(methodSymbol.TypeArguments[0]);
                RegisterGeneratedTypeReferences(context, targetType);
                lines.Add("he_cpp_unsafe_as_ref<");
                lines.Add(RenderUnsafeHelperTypeName(context, targetType));
                lines.Add(">(");
                lines.Add(RenderUnsafeSourceExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0]));
                lines.Add(")");
                resultType = targetType;
                return true;
            }

            if (string.Equals(methodSymbol.Name, "AsPointer", StringComparison.Ordinal) &&
                methodSymbol.TypeArguments.Length == 1 &&
                invocationExpression.ArgumentList.Arguments.Count == 1) {
                VariableType targetType = VariableUtil.GetVarType(methodSymbol.TypeArguments[0]);
                RegisterGeneratedTypeReferences(context, targetType);
                lines.Add("he_cpp_unsafe_as_pointer<");
                lines.Add(RenderUnsafeHelperTypeName(context, targetType));
                lines.Add(">(");
                lines.Add(RenderUnsafeSourceExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0]));
                lines.Add(")");
                resultType = VariableUtil.GetVarType("object");
                return true;
            }

            if (string.Equals(methodSymbol.Name, "Add", StringComparison.Ordinal) &&
                methodSymbol.TypeArguments.Length == 1 &&
                invocationExpression.ArgumentList.Arguments.Count == 2) {
                VariableType targetType = VariableUtil.GetVarType(methodSymbol.TypeArguments[0]);
                RegisterGeneratedTypeReferences(context, targetType);
                lines.Add("he_cpp_unsafe_add<");
                lines.Add(RenderUnsafeHelperTypeName(context, targetType));
                lines.Add(">(");
                lines.Add(RenderUnsafeSourceExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0]));
                lines.Add(", ");
                lines.Add(RenderExpressionText(semantic, context, invocationExpression.ArgumentList.Arguments[1].Expression));
                lines.Add(")");
                resultType = targetType;
                return true;
            }

            if (string.Equals(methodSymbol.Name, "InitBlockUnaligned", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 3) {
                lines.Add("he_cpp_unsafe_init_block_unaligned(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("void");
                return true;
            }

            if (string.Equals(methodSymbol.Name, "CopyBlockUnaligned", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 3) {
                lines.Add("he_cpp_unsafe_copy_block_unaligned(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("void");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether one invoked method belongs to System.Runtime.CompilerServices.Unsafe.
        /// </summary>
        /// <param name="methodSymbol">Resolved method symbol to inspect.</param>
        /// <returns><c>true</c> when the method belongs to the Unsafe intrinsic helper; otherwise <c>false</c>.</returns>
        static bool IsUnsafeIntrinsicMethod(IMethodSymbol methodSymbol) {
            if (methodSymbol?.ContainingType == null) {
                return false;
            }

            string containingTypeName = methodSymbol.ContainingType.ToDisplayString();
            return string.Equals(containingTypeName, "System.Runtime.CompilerServices.Unsafe", StringComparison.Ordinal) ||
                string.Equals(containingTypeName, "global::System.Runtime.CompilerServices.Unsafe", StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures the current generated type includes the Unsafe shim so native helper declarations are available wherever Unsafe intrinsics were lowered.
        /// </summary>
        /// <param name="context">Current lowering context.</param>
        void EnsureUnsafeShimInclude(LayerContext context) {
            ConversionClass currentClass = context.GetCurrentClass();
            if (currentClass == null || currentClass.SourceIncludes.Contains("runtime/native_unsafe.hpp", StringComparer.Ordinal)) {
                return;
            }

            currentClass.SourceIncludes.Add("runtime/native_unsafe.hpp");
        }

        /// <summary>
        /// Renders one C++ helper type name for a lowered Unsafe intrinsic generic argument.
        /// </summary>
        /// <param name="context">Current lowering context.</param>
        /// <param name="variableType">Converted variable type to render.</param>
        /// <returns>Qualified C++ type token suitable for helper template arguments.</returns>
        string RenderUnsafeHelperTypeName(LayerContext context, VariableType variableType) {
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(variableType, out typeData);
            return QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context);
        }

        /// <summary>
        /// Renders one Unsafe intrinsic source expression, adding an address-of operator when the source argument is not already pointer-shaped in native output.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the invocation argument.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="argument">Unsafe intrinsic argument that supplies the source storage.</param>
        /// <returns>Rendered source expression that native helper templates can reinterpret safely.</returns>
        string RenderUnsafeSourceExpression(
            SemanticModel semantic,
            LayerContext context,
            ArgumentSyntax argument) {
            string expressionText = RenderExpressionText(semantic, context, argument.Expression);
            if (string.IsNullOrWhiteSpace(expressionText)) {
                return expressionText;
            }

            if (argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) ||
                argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword) ||
                argument.RefOrOutKeyword.IsKind(SyntaxKind.InKeyword)) {
                return $"&({expressionText})";
            }

            if (TryGetExpressionTypeSymbol(semantic, argument.Expression, out ITypeSymbol argumentTypeSymbol)) {
                VariableType argumentType = VariableUtil.GetVarType(argumentTypeSymbol);
                ConvertToCPPType(argumentType, out CPPTypeData argumentTypeData);
                if (argumentTypeData.IsPointer) {
                    return expressionText;
                }
            }

            return $"&({expressionText})";
        }

        /// <summary>
        /// Rewrites generic interface or abstract-base method invocations to the sole generated concrete implementation when C++ cannot represent the source-side virtual generic contract directly.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve receiver metadata.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="invocationExpression">Invocation being lowered.</param>
        /// <param name="invokedMethodSymbol">Resolved source method symbol.</param>
        /// <param name="invocationTargetLines">Target token buffer to rewrite in place.</param>
        /// <param name="beforeLines">Before-lines buffer that receives any receiver temporaries produced by the rewrite.</param>
        /// <returns><c>true</c> when the invocation target was rewritten; otherwise <c>false</c>.</returns>
        bool TryProcessMultiImplementationGenericInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            List<string> lines,
            out VariableType invocationType,
            out List<string> expressionBeforeLines) {
            invocationType = null;
            expressionBeforeLines = null;
            if (invokedMethodSymbol == null ||
                !invokedMethodSymbol.IsGenericMethod ||
                !TryResolveGeneratedGenericInvocationImplementations(semantic, context, invocationExpression, invokedMethodSymbol, out string receiverText, out List<INamedTypeSymbol> implementationTypeSymbols, out List<string> beforeLines)) {
                return false;
            }

            if (!TryAppendMultiImplementationGenericInvocationExpression(
                    semantic,
                    context,
                    invocationExpression,
                    invokedMethodSymbol,
                    receiverText,
                    implementationTypeSymbols,
                    beforeLines,
                    lines,
                    out invocationType)) {
                return false;
            }

            expressionBeforeLines = beforeLines.Count > 0 ? beforeLines : null;
            return true;
        }

        bool TryAppendMultiImplementationGenericInvocationExpression(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            string receiverText,
            List<INamedTypeSymbol> implementationTypeSymbols,
            List<string> beforeLines,
            List<string> lines,
            out VariableType invocationType) {
            invocationType = null;
            if (invokedMethodSymbol == null ||
                string.IsNullOrWhiteSpace(receiverText) ||
                implementationTypeSymbols == null ||
                implementationTypeSymbols.Count <= 1) {
                return false;
            }

            List<string> argLines = BuildInvocationArgumentList(semantic, context, invocationExpression, invokedMethodSymbol, beforeLines);
            ConversionClass currentClass = GetOwningEmissionClass(context);
            foreach (INamedTypeSymbol implementationTypeSymbol in implementationTypeSymbols) {
                ConversionClass implementationClass = context.Program.FindGeneratedClass(implementationTypeSymbol);
                if (currentClass != null && implementationClass != null) {
                    string emittedTypeName = implementationClass.GetEmittedTypeName();
                    if (!currentClass.ReferencedClasses.Contains(emittedTypeName, StringComparer.Ordinal)) {
                        currentClass.ReferencedClasses.Add(emittedTypeName);
                    }

                    string includePath = implementationClass.GetEmittedFileStem(context.Program) + ".hpp";
                    if (!currentClass.SourceIncludes.Contains(includePath, StringComparer.Ordinal)) {
                        currentClass.SourceIncludes.Add(includePath);
                    }
                }

                RegisterGeneratedTypeReferences(context, VariableUtil.GetVarType(implementationTypeSymbol));
                RegisterGeneratedDispatchTypeArgumentDependencies(context, currentClass, implementationTypeSymbol);
            }

            if (currentClass != null &&
                !currentClass.SourceIncludes.Contains("runtime/native_exceptions.hpp", StringComparer.Ordinal)) {
                currentClass.SourceIncludes.Add("runtime/native_exceptions.hpp");
            }

            string emittedFunctionName = ResolveConvertedFunctionName(invokedMethodSymbol);
            if (invokedMethodSymbol.ReturnType != null && invokedMethodSymbol.ReturnType.SpecialType != SpecialType.System_Void) {
                invocationType = VariableUtil.GetVarType(invokedMethodSymbol.ReturnType);
                VariableType convertedInvocationType = ConvertToCPPType(invocationType, out CPPTypeData invocationTypeData);
                string returnTypeName = QualifyRenderedCppTypeName(convertedInvocationType.ToCPPString(context.Program), context);
                if (invocationTypeData.IsPointer) {
                    returnTypeName += "*";
                }
                lines.Add("([&]() -> ");
                lines.Add(returnTypeName);
                lines.Add(" {\n");
            } else {
                invocationType = VariableUtil.GetVarType("void");
                lines.Add("([&]() {\n");
            }

            foreach (INamedTypeSymbol implementationTypeSymbol in implementationTypeSymbols) {
                string implementationTypeName = QualifyRenderedCppTypeName(RenderQualifiedContainingTypeName(implementationTypeSymbol, context), context);
                lines.Add("if (auto heCppDispatchImpl = dynamic_cast<");
                lines.Add(implementationTypeName);
                lines.Add("*>(");
                lines.Add(receiverText);
                lines.Add(")) {\n");
                if (invokedMethodSymbol.ReturnType != null && invokedMethodSymbol.ReturnType.SpecialType != SpecialType.System_Void) {
                    lines.Add("return ");
                }

                lines.Add("heCppDispatchImpl->");
                lines.Add(emittedFunctionName);
                AppendConcreteDispatchInvocationTypeArgumentsIfNeeded(invocationExpression, invokedMethodSymbol, context, lines);
                lines.AddRange(argLines);
                lines.Add(";\n");
                if (invokedMethodSymbol.ReturnType == null || invokedMethodSymbol.ReturnType.SpecialType == SpecialType.System_Void) {
                    lines.Add("return;\n");
                }

                lines.Add("}\n");
            }

            lines.Add("throw new NotSupportedException(\"No generated implementation matched generic dispatch receiver.\");\n");
            lines.Add("})()");
            return true;
        }

        void RegisterGeneratedDispatchTypeArgumentDependencies(
            LayerContext context,
            ConversionClass currentClass,
            INamedTypeSymbol implementationTypeSymbol) {
            if (context?.Program == null || currentClass == null || implementationTypeSymbol == null) {
                return;
            }

            foreach (ITypeSymbol typeArgumentSymbol in implementationTypeSymbol.TypeArguments) {
                if (typeArgumentSymbol is not INamedTypeSymbol namedTypeArgumentSymbol) {
                    continue;
                }

                ConversionClass generatedTypeArgumentClass = context.Program.FindGeneratedClass(namedTypeArgumentSymbol);
                if (generatedTypeArgumentClass != null) {
                    string emittedTypeName = generatedTypeArgumentClass.GetEmittedTypeName();
                    if (!currentClass.ReferencedClasses.Contains(emittedTypeName, StringComparer.Ordinal)) {
                        currentClass.ReferencedClasses.Add(emittedTypeName);
                    }

                    string includePath = generatedTypeArgumentClass.GetEmittedFileStem(context.Program) + ".hpp";
                    if (!currentClass.SourceIncludes.Contains(includePath, StringComparer.Ordinal)) {
                        currentClass.SourceIncludes.Add(includePath);
                    }
                }

                RegisterGeneratedDispatchTypeArgumentDependencies(context, currentClass, namedTypeArgumentSymbol);
            }
        }

        /// <summary>
        /// Resolves the owning emitted class for dependency registration even when temporary receiver/base scopes have been pushed onto the context stack.
        /// </summary>
        /// <param name="context">Current lowering context.</param>
        /// <returns>The outermost emitted class when available; otherwise the current class.</returns>
        static ConversionClass GetOwningEmissionClass(LayerContext context) {
            if (context == null) {
                return null;
            }

            foreach (ConversionClass conversionClass in context.Class) {
                if (conversionClass != null) {
                    return conversionClass;
                }
            }

            return context.GetCurrentClass();
        }

        /// <summary>
        /// Builds one emitted invocation argument list while preserving ref/out setup lines that must execute before the call.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the invocation.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="invocationExpression">Invocation whose arguments should be rendered.</param>
        /// <param name="invokedMethodSymbol">Resolved target method symbol.</param>
        /// <param name="beforeLines">Before-lines buffer that receives any argument setup temporaries.</param>
        /// <returns>Rendered argument list including parentheses.</returns>
        List<string> BuildInvocationArgumentList(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            List<string> beforeLines) {
            List<string> argLines = ["("];
            System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameterSymbols = invokedMethodSymbol != null
                ? invokedMethodSymbol.Parameters
                : System.Collections.Immutable.ImmutableArray<IParameterSymbol>.Empty;

            if (parameterSymbols.Length > 0 &&
                invocationExpression.ArgumentList.Arguments.Any(argument => argument.NameColon != null)) {
                ArgumentSyntax[] alignedArguments = AlignInvocationArguments(invocationExpression.ArgumentList.Arguments, parameterSymbols);
                bool wroteAnyArgument = false;
                for (int parameterIndex = 0; parameterIndex < parameterSymbols.Length; ++parameterIndex) {
                    IParameterSymbol parameterSymbol = parameterSymbols[parameterIndex];
                    ArgumentSyntax argument = alignedArguments[parameterIndex];
                    if (argument == null) {
                        if (!parameterSymbol.HasExplicitDefaultValue) {
                            continue;
                        }

                        if (wroteAnyArgument) {
                            argLines.Add(", ");
                        }

                        AppendOptionalArgumentDefaultValue(parameterSymbol, argLines);
                        wroteAnyArgument = true;
                        continue;
                    }

                    if (wroteAnyArgument) {
                        argLines.Add(", ");
                    }

                    List<string> argumentExpressionLines = new List<string>();
                    AppendInvocationArgument(
                        semantic,
                        context,
                        argument.Expression,
                        argumentExpressionLines,
                        parameterSymbol,
                        invokedMethodSymbol,
                        beforeLines,
                        argLines);

                    if (argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) || argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)) {
                        AddRefOrOutDeclarationBeforeLines(semantic, context, argument.Expression, parameterSymbol, beforeLines);
                    }

                    wroteAnyArgument = true;
                }

                if (wroteAnyArgument &&
                    parameterSymbols.Skip(invocationExpression.ArgumentList.Arguments.Count).Any(parameter => parameter.HasExplicitDefaultValue)) {
                    argLines.Add(", ");
                }

                AppendOptionalInvocationArguments(parameterSymbols, invocationExpression.ArgumentList.Arguments.Count, argLines);
            } else {
                int count = 0;
                foreach (ArgumentSyntax arg in invocationExpression.ArgumentList.Arguments) {
                    bool isRef = arg.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) || arg.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword);
                    List<string> argumentExpressionLines = new List<string>();
                    ExpressionResult argumentResult = ProcessExpression(semantic, context, arg.Expression, argumentExpressionLines);
                    if (argumentResult.BeforeLines != null && argumentResult.BeforeLines.Count > 0) {
                        beforeLines.AddRange(argumentResult.BeforeLines);
                    }

                    IParameterSymbol parameterSymbol = count < parameterSymbols.Length ? parameterSymbols[count] : null;
                    AppendInvocationArgument(
                        semantic,
                        context,
                        arg.Expression,
                        argumentExpressionLines,
                        parameterSymbol,
                        invokedMethodSymbol,
                        beforeLines,
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
            }

            argLines.Add(")");
            return argLines;
        }

        /// <summary>
        /// Resolves one generic invocation that requires runtime dispatch across multiple generated implementations and captures the evaluated receiver text.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve receiver metadata.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="invocationExpression">Invocation being lowered.</param>
        /// <param name="invokedMethodSymbol">Resolved source method symbol.</param>
        /// <param name="receiverText">Receives the receiver expression text to dispatch against.</param>
        /// <param name="implementationTypeSymbols">Receives the concrete implementation types that satisfy the method contract.</param>
        /// <param name="beforeLines">Receives any receiver-evaluation setup lines.</param>
        /// <returns><c>true</c> when runtime dispatch should be emitted; otherwise <c>false</c>.</returns>
        bool TryResolveGeneratedGenericInvocationImplementations(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            out string receiverText,
            out List<INamedTypeSymbol> implementationTypeSymbols,
            out List<string> beforeLines) {
            receiverText = string.Empty;
            implementationTypeSymbols = null;
            beforeLines = new List<string>();

            if (!TryResolveGenericInvocationReceiver(semantic, context, invocationExpression, invokedMethodSymbol, out ExpressionSyntax receiverExpression, out ITypeSymbol receiverTypeSymbol, out string implicitReceiverText)) {
                return false;
            }

            List<ConversionClass> matchingDefinitions = ResolveGeneratedGenericInvocationDefinitions(receiverTypeSymbol, invokedMethodSymbol.OriginalDefinition);
            if (matchingDefinitions.Count <= 1) {
                return false;
            }

            implementationTypeSymbols = ResolveGeneratedGenericInvocationConcreteImplementations(semantic.Compilation, receiverTypeSymbol, invokedMethodSymbol.OriginalDefinition);
            if (implementationTypeSymbols.Count <= 1) {
                return false;
            }

            if (receiverExpression == null) {
                receiverText = implicitReceiverText;
                return true;
            }

            List<string> receiverLines = new List<string>();
            int receiverStartDepth = context.DepthClass;
            ExpressionResult receiverResult = ProcessExpression(semantic, context, receiverExpression, receiverLines);
            context.PopClass(receiverStartDepth);
            if (receiverResult.BeforeLines != null && receiverResult.BeforeLines.Count > 0) {
                beforeLines.AddRange(receiverResult.BeforeLines);
            }

            string renderedReceiverText = string.Concat(receiverLines);
            if (IsSimpleRuntimeDispatchReceiver(receiverExpression)) {
                receiverText = renderedReceiverText;
                return true;
            }

            string receiverTemporaryName = CreateTemporaryName("heCppDispatchReceiver");
            beforeLines.Add($"auto {receiverTemporaryName} = {renderedReceiverText};\n");
            receiverText = receiverTemporaryName;
            return true;
        }

        /// <summary>
        /// Determines whether one receiver expression can be reused directly inside multiple dispatch branches without introducing repeated side effects.
        /// </summary>
        /// <param name="receiverExpression">Receiver syntax to inspect.</param>
        /// <returns><c>true</c> when the receiver can be reused directly; otherwise <c>false</c>.</returns>
        static bool IsSimpleRuntimeDispatchReceiver(ExpressionSyntax receiverExpression) {
            return receiverExpression is ThisExpressionSyntax ||
                receiverExpression is IdentifierNameSyntax ||
                receiverExpression is MemberAccessExpressionSyntax;
        }

        bool TryRewriteGenericInterfaceInvocationTarget(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            List<string> invocationTargetLines,
            List<string> beforeLines) {
            if (!invokedMethodSymbol.IsGenericMethod ||
                invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                !TryResolveGeneratedGenericInvocationImplementation(semantic, memberAccess.Expression, invokedMethodSymbol, out ConversionClass implementationClass)) {
                return false;
            }

            List<string> rewrittenReceiverLines = new List<string>();
            ExpressionResult receiverResult = ProcessExpression(semantic, context, memberAccess.Expression, rewrittenReceiverLines);
            if (receiverResult.BeforeLines != null && receiverResult.BeforeLines.Count > 0) {
                beforeLines.AddRange(receiverResult.BeforeLines);
            }

            ConversionClass currentClass = GetOwningEmissionClass(context);
            if (currentClass != null && implementationClass != null) {
                string emittedTypeName = implementationClass.GetEmittedTypeName();
                if (!currentClass.ReferencedClasses.Contains(emittedTypeName, StringComparer.Ordinal)) {
                    currentClass.ReferencedClasses.Add(emittedTypeName);
                }

                string includePath = implementationClass.GetEmittedFileStem(context.Program) + ".hpp";
                if (!currentClass.SourceIncludes.Contains(includePath, StringComparer.Ordinal)) {
                    currentClass.SourceIncludes.Add(includePath);
                }
            }

            RegisterGeneratedTypeReferences(context, VariableUtil.GetVarType(implementationClass.TypeSymbol));

            invocationTargetLines.Clear();
            invocationTargetLines.Add("static_cast<");
            invocationTargetLines.Add(implementationClass.GetEmittedTypeName());
            invocationTargetLines.Add("*>(");
            invocationTargetLines.AddRange(rewrittenReceiverLines);
            invocationTargetLines.Add(")");
            invocationTargetLines.Add("->");
            invocationTargetLines.Add(GetEmittedFunctionName(invokedMethodSymbol));
            return true;
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

        /// <summary>
        /// Lowers BinaryPrimitives invocations onto the native runtime helper surface and rewrites managed byte-array buffers to raw pointer access.
        /// </summary>
        /// <param name="semantic">Semantic model for the invocation.</param>
        /// <param name="context">Current conversion context.</param>
        /// <param name="invocationExpression">Invocation being lowered.</param>
        /// <param name="lines">Destination token buffer.</param>
        /// <param name="resultType">Receives the inferred result type when the invocation is handled.</param>
        /// <returns><c>true</c> when the invocation targets BinaryPrimitives; otherwise <c>false</c>.</returns>
        bool TryProcessBinaryPrimitivesInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("object");

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name is not IdentifierNameSyntax memberIdentifier ||
                !TryResolveStaticRuntimeType(semantic, memberAccess.Expression, out string runtimeTypeName, out string runtimeRequirementName) ||
                !string.Equals(runtimeTypeName, "BinaryPrimitives", StringComparison.Ordinal)) {
                return false;
            }

            RegisterRuntimeRequirement(runtimeRequirementName);
            lines.Add("BinaryPrimitives::");
            lines.Add(memberIdentifier.Identifier.Text);
            lines.Add("(");
            AppendBinaryPrimitivesInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
            lines.Add(")");

            if (TryGetExpressionTypeSymbol(semantic, invocationExpression, out ITypeSymbol resultTypeSymbol)) {
                resultType = VariableUtil.GetVarType(resultTypeSymbol);
            } else {
                IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
                if (invokedMethodSymbol?.ReturnType != null) {
                    resultType = VariableUtil.GetVarType(invokedMethodSymbol.ReturnType);
                }
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
            IMethodSymbol invokedMethodSymbol,
            List<string> beforeLines,
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
                TryAppendArrayAsListInvocationArgument(
                    semantic,
                    context,
                    argumentExpression,
                    parameterSymbol,
                    invokedMethodSymbol,
                    beforeLines,
                    argumentExpressionLines,
                    argumentLines)) {
                return;
            }

            if (parameterSymbol != null &&
                TryAppendUserDefinedConversionInvocationArgument(
                    semantic,
                    context,
                    argumentExpression,
                    argumentExpressionLines,
                    parameterSymbol,
                    argumentLines)) {
                return;
            }

            if (parameterSymbol != null &&
                TryAppendTargetTypedNumericLiteralInvocationArgument(argumentExpression, parameterSymbol, argumentLines)) {
                return;
            }

            if (TryAppendTargetTypedIntegralArgumentCast(
                semantic,
                context,
                argumentExpression,
                argumentExpressionLines,
                parameterSymbol?.RefKind == RefKind.None ? parameterSymbol?.Type : null,
                argumentLines)) {
                return;
            }

            if (TryAppendScopedTemporaryInvocationArgument(
                semantic,
                context,
                argumentExpression,
                argumentExpressionLines,
                parameterSymbol,
                invokedMethodSymbol,
                beforeLines,
                argumentLines)) {
                return;
            }

            IMethodSymbol methodGroupSymbol = null;
            if (semantic != null &&
                argumentExpression != null &&
                ReferenceEquals(argumentExpression.SyntaxTree, semantic.SyntaxTree) &&
                argumentExpression is not AnonymousFunctionExpressionSyntax) {
                methodGroupSymbol = ResolveMethodSymbol(semantic.GetSymbolInfo(argumentExpression));
            }

            if (parameterSymbol != null &&
                methodGroupSymbol != null &&
                TryGetDelegateWrapperTypeName(parameterSymbol.Type, methodGroupSymbol, context, out string delegateWrapperTypeName)) {
                List<string> delegateConstructionLines = new List<string>();
                if (!TryAppendDelegateWrapperConstruction(
                    semantic,
                    context,
                    parameterSymbol.Type,
                    methodGroupSymbol,
                    argumentExpression,
                    delegateConstructionLines)) {
                    delegateConstructionLines.Add($"new {delegateWrapperTypeName}(");
                    delegateConstructionLines.Add(string.Concat(argumentExpressionLines));
                    delegateConstructionLines.Add(")");
                }

                if (ShouldScopeDeleteDelegateInvocationArgument(invokedMethodSymbol, parameterSymbol)) {
                    string temporaryName = CreateTemporaryName("__delegateArg");
                    beforeLines.Add($"auto {temporaryName} = ");
                    beforeLines.Add(string.Concat(delegateConstructionLines));
                    beforeLines.Add(";\n");
                    AppendDeleteGuard(beforeLines, temporaryName, "__delegateArgDeleteGuard");
                    argumentLines.Add(temporaryName);
                    return;
                }

                argumentLines.AddRange(delegateConstructionLines);
                return;
            }

            if (TryAppendThisInvocationArgument(context, argumentExpression, parameterSymbol, argumentLines)) {
                return;
            }

            argumentLines.AddRange(argumentExpressionLines);
        }

        /// <summary>
        /// Preserves value-type `this` arguments for ref-like calls even when temporary receiver dispatch rewrites have changed the active class scope.
        /// </summary>
        /// <param name="context">Current lowering context.</param>
        /// <param name="argumentExpression">Invocation argument expression being rendered.</param>
        /// <param name="parameterSymbol">Resolved target parameter metadata.</param>
        /// <param name="argumentLines">Destination token buffer for the rendered argument.</param>
        /// <returns><c>true</c> when the argument was handled directly; otherwise <c>false</c>.</returns>
        bool TryAppendThisInvocationArgument(
            LayerContext context,
            ExpressionSyntax argumentExpression,
            IParameterSymbol parameterSymbol,
            List<string> argumentLines) {
            if (context == null ||
                argumentExpression is not ThisExpressionSyntax ||
                parameterSymbol == null ||
                parameterSymbol.RefKind == RefKind.None) {
                return false;
            }

            ConversionClass owningClass = GetOwningEmissionClass(context);
            if (owningClass?.TypeSymbol?.IsValueType == true) {
                argumentLines.Add("(*this)");
            } else {
                argumentLines.Add("this");
            }

            return true;
        }

        bool TryAppendTargetTypedNumericLiteralInvocationArgument(
            ExpressionSyntax argumentExpression,
            IParameterSymbol parameterSymbol,
            List<string> argumentLines) {
            if (argumentExpression == null || parameterSymbol?.Type == null || argumentLines == null) {
                return false;
            }

            if (IsIntegralLikeTypeSymbol(parameterSymbol.Type)) {
                return false;
            }

            if (!TryRewriteTargetTypedNumericLiteral(argumentExpression, parameterSymbol.Type, out string rewrittenLiteral)) {
                return false;
            }

            argumentLines.Add(rewrittenLiteral);
            return true;
        }

        bool TryAppendTargetTypedIntegralArgumentCast(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax argumentExpression,
            List<string> argumentExpressionLines,
            ITypeSymbol targetTypeSymbol,
            List<string> argumentLines) {
            if (semantic == null ||
                context == null ||
                argumentExpression == null ||
                argumentExpressionLines == null ||
                targetTypeSymbol == null ||
                argumentLines == null ||
                !IsIntegralLikeTypeSymbol(targetTypeSymbol)) {
                return false;
            }

            if (!argumentExpression.IsKind(SyntaxKind.NumericLiteralExpression)) {
                if (!TryGetExpressionTypeSymbol(semantic, argumentExpression, out ITypeSymbol sourceTypeSymbol) ||
                    !IsIntegralLikeTypeSymbol(sourceTypeSymbol)) {
                    return false;
                }

                Conversion conversion = semantic.ClassifyConversion(argumentExpression, targetTypeSymbol);
                if (!conversion.Exists || !conversion.IsImplicit) {
                    return false;
                }
            }

            argumentLines.Add("static_cast<");
            argumentLines.Add(GetCppTypeToken(VariableUtil.GetVarType(targetTypeSymbol), context.Program));
            argumentLines.Add(">(");
            argumentLines.AddRange(argumentExpressionLines);
            argumentLines.Add(")");
            return true;
        }

        static bool IsIntegralLikeTypeSymbol(ITypeSymbol typeSymbol) {
            if (typeSymbol == null) {
                return false;
            }

            if (typeSymbol.TypeKind == TypeKind.Enum) {
                return true;
            }

            return typeSymbol.SpecialType switch {
                SpecialType.System_Byte => true,
                SpecialType.System_SByte => true,
                SpecialType.System_Int16 => true,
                SpecialType.System_UInt16 => true,
                SpecialType.System_Int32 => true,
                SpecialType.System_UInt32 => true,
                SpecialType.System_Int64 => true,
                SpecialType.System_UInt64 => true,
                SpecialType.System_Char => true,
                _ => false
            };
        }

        bool TryAppendUserDefinedConversionInvocationArgument(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax argumentExpression,
            List<string> argumentExpressionLines,
            IParameterSymbol parameterSymbol,
            List<string> argumentLines) {
            if (semantic == null ||
                context == null ||
                argumentExpression == null ||
                argumentExpressionLines == null ||
                parameterSymbol?.Type == null ||
                argumentLines == null ||
                !ReferenceEquals(argumentExpression.SyntaxTree, semantic.SyntaxTree)) {
                return false;
            }

            Conversion conversion = semantic.ClassifyConversion(argumentExpression, parameterSymbol.Type);
            if (!conversion.IsUserDefined ||
                conversion.MethodSymbol is not IMethodSymbol conversionMethodSymbol ||
                conversionMethodSymbol.MethodKind != MethodKind.Conversion ||
                !TryResolveGeneratedContainingClass(conversionMethodSymbol, out ConversionClass generatedClass)) {
                return false;
            }

            RegisterGeneratedTypeDependency(context, generatedClass.Name);
            argumentLines.Add(GetContainingTypeAccessName(context, conversionMethodSymbol.ContainingType));
            argumentLines.Add("::");
            argumentLines.Add(GetConversionOperatorFunctionName(conversionMethodSymbol));
            argumentLines.Add("(");
            argumentLines.AddRange(argumentExpressionLines);
            argumentLines.Add(")");
            return true;
        }

        bool TryAppendScopedTemporaryInvocationArgument(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax argumentExpression,
            List<string> argumentExpressionLines,
            IParameterSymbol parameterSymbol,
            IMethodSymbol invokedMethodSymbol,
            List<string> beforeLines,
            List<string> argumentLines) {
            if (!ShouldScopeDeleteManagedInvocationArgument(semantic, argumentExpression, parameterSymbol, invokedMethodSymbol)) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, argumentExpression, out ITypeSymbol expressionTypeSymbol)) {
                return false;
            }

            VariableType expressionType = VariableUtil.GetVarType(expressionTypeSymbol);
            VariableType cppExpressionType = ConvertToCPPType(expressionType, out CPPTypeData typeData);
            if (cppExpressionType == null || !typeData.IsPointer) {
                return false;
            }

            string temporaryName = CreateTemporaryName("__scopedArg");
            beforeLines.Add("auto ");
            beforeLines.Add(temporaryName);
            beforeLines.Add(" = ");
            beforeLines.AddRange(argumentExpressionLines);
            beforeLines.Add(";\n");
            AppendDeleteGuard(beforeLines, temporaryName, "__scopedArgDeleteGuard");
            argumentLines.Add(temporaryName);
            return true;
        }

        static bool ShouldScopeDeleteDelegateInvocationArgument(
            IMethodSymbol invokedMethodSymbol,
            IParameterSymbol parameterSymbol) {
            if (invokedMethodSymbol == null || parameterSymbol?.Type == null || parameterSymbol.Type.TypeKind != TypeKind.Delegate) {
                return false;
            }

            string containingTypeName = invokedMethodSymbol.ContainingType?.Name;
            if (string.Equals(invokedMethodSymbol.Name, "ReadArray", StringComparison.Ordinal) &&
                string.Equals(containingTypeName, "EngineBinaryReader", StringComparison.Ordinal)) {
                return true;
            }

            if (string.Equals(invokedMethodSymbol.Name, "WriteArray", StringComparison.Ordinal) &&
                string.Equals(containingTypeName, "EngineBinaryWriter", StringComparison.Ordinal)) {
                return true;
            }

            return false;
        }

        static bool ShouldScopeDeleteManagedInvocationArgument(
            SemanticModel semantic,
            ExpressionSyntax argumentExpression,
            IParameterSymbol parameterSymbol,
            IMethodSymbol invokedMethodSymbol) {
            if (semantic == null || argumentExpression == null || parameterSymbol == null || invokedMethodSymbol == null) {
                return false;
            }

            if (!ShouldScopeDeleteManagedInvocationTarget(parameterSymbol, invokedMethodSymbol)) {
                return false;
            }

            if (!IsScopedTemporaryArgumentExpression(argumentExpression)) {
                return false;
            }

            return ShouldScopeDeleteManagedInvocationArgument(
                semantic,
                argumentExpression,
                invokedMethodSymbol.ContainingType?.Name ?? string.Empty,
                invokedMethodSymbol.Name,
                parameterSymbol.Name);
        }

        static bool ShouldScopeDeleteManagedInvocationArgument(
            SemanticModel semantic,
            ExpressionSyntax argumentExpression,
            string containingTypeName,
            string methodName,
            string parameterName) {
            if (semantic == null || argumentExpression == null) {
                return false;
            }

            if (!IsScopedTemporaryArgumentExpression(argumentExpression)) {
                return false;
            }

            return ShouldScopeDeleteManagedInvocationTarget(containingTypeName, methodName, parameterName);
        }

        static bool IsScopedTemporaryArgumentExpression(ExpressionSyntax argumentExpression) {
            ExpressionSyntax expression = argumentExpression;
            while (expression is ParenthesizedExpressionSyntax parenthesizedExpression) {
                expression = parenthesizedExpression.Expression;
            }

            return expression is ObjectCreationExpressionSyntax ||
                expression is ImplicitObjectCreationExpressionSyntax ||
                expression is ArrayCreationExpressionSyntax ||
                expression is ImplicitArrayCreationExpressionSyntax ||
                expression is CollectionExpressionSyntax;
        }

        static bool ShouldScopeDeleteManagedInvocationTarget(
            IParameterSymbol parameterSymbol,
            IMethodSymbol invokedMethodSymbol) {
            if (parameterSymbol == null || invokedMethodSymbol == null) {
                return false;
            }

            return ShouldScopeDeleteManagedInvocationTarget(
                invokedMethodSymbol.ContainingType?.Name ?? string.Empty,
                invokedMethodSymbol.Name,
                parameterSymbol.Name);
        }

        static bool ShouldScopeDeleteManagedInvocationTarget(
            string containingTypeName,
            string methodName,
            string parameterName) {
            if (string.Equals(containingTypeName, "HlslShaderBindingParser", StringComparison.Ordinal)) {
                if (string.Equals(methodName, "ParseBindings", StringComparison.Ordinal) &&
                    string.Equals(parameterName, "defines", StringComparison.Ordinal)) {
                    return true;
                }

                if (string.Equals(methodName, "ComputeConstantBufferSize", StringComparison.Ordinal) &&
                    string.Equals(parameterName, "members", StringComparison.Ordinal)) {
                    return true;
                }
            }

            if (string.Equals(containingTypeName, "String", StringComparison.Ordinal) &&
                string.Equals(methodName, "Split", StringComparison.Ordinal) &&
                string.Equals(parameterName, "separators", StringComparison.Ordinal)) {
                return true;
            }

            return false;
        }

        void AppendDeleteGuard(
            List<string> lines,
            string variableName,
            string guardPrefix) {
            string guardName = CreateTemporaryName(guardPrefix);
            RegisterRuntimeRequirement("NativeFinally");
            lines.Add($"auto {guardName} = he_cpp_make_scope_exit([&]() {{\n");
            lines.Add($"delete {variableName};\n");
            lines.Add("});\n");
        }

        bool TryAppendArrayAsListInvocationArgument(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax argumentExpression,
            IParameterSymbol parameterSymbol,
            IMethodSymbol invokedMethodSymbol,
            List<string> beforeLines,
            List<string> argumentExpressionLines,
            List<string> argumentLines) {
            if (!IsListFamilyTypeSymbol(parameterSymbol.Type) ||
                !TryResolveArrayElementTypeSymbol(semantic, argumentExpression, out ITypeSymbol arrayElementTypeSymbol)) {
                return false;
            }

            RegisterRuntimeRequirement("NativeList");
            VariableType elementType = VariableUtil.GetVarType(arrayElementTypeSymbol);
            string elementTypeName = GetCppTypeToken(elementType, context.Program);
            if (ShouldScopeDeleteManagedInvocationTarget(parameterSymbol, invokedMethodSymbol)) {
                string temporaryName = CreateTemporaryName("__scopedArg");
                beforeLines.Add($"auto {temporaryName} = new List<{elementTypeName}>(");
                beforeLines.Add(string.Concat(argumentExpressionLines));
                beforeLines.Add(");\n");
                AppendDeleteGuard(beforeLines, temporaryName, "__scopedArgDeleteGuard");
                argumentLines.Add(temporaryName);
                return true;
            }

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

        void AppendConcreteDispatchInvocationTypeArgumentsIfNeeded(
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            LayerContext context,
            List<string> lines) {
            if (!invokedMethodSymbol.IsGenericMethod ||
                invokedMethodSymbol.TypeArguments.Length == 0 ||
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
                if (parameterSymbol.Type != null &&
                    parameterSymbol.Type.IsValueType &&
                    parameterSymbol.Type.TypeKind != TypeKind.Pointer &&
                    parameterSymbol.Type.TypeKind != TypeKind.FunctionPointer) {
                    argumentLines.Add(GetCppTypeToken(VariableUtil.GetVarType(parameterSymbol.Type), codeConverter.Program));
                    argumentLines.Add("()");
                    return;
                }

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

        static ArgumentSyntax[] AlignInvocationArguments(
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameterSymbols) {
            ArgumentSyntax[] alignedArguments = new ArgumentSyntax[parameterSymbols.Length];
            int nextPositionalParameterIndex = 0;

            foreach (ArgumentSyntax argument in arguments) {
                if (argument.NameColon != null) {
                    string parameterName = argument.NameColon.Name.Identifier.Text;
                    for (int parameterIndex = 0; parameterIndex < parameterSymbols.Length; ++parameterIndex) {
                        if (!string.Equals(parameterSymbols[parameterIndex].Name, parameterName, StringComparison.Ordinal)) {
                            continue;
                        }

                        alignedArguments[parameterIndex] = argument;
                        break;
                    }

                    continue;
                }

                while (nextPositionalParameterIndex < alignedArguments.Length &&
                       alignedArguments[nextPositionalParameterIndex] != null) {
                    ++nextPositionalParameterIndex;
                }

                if (nextPositionalParameterIndex >= alignedArguments.Length) {
                    break;
                }

                alignedArguments[nextPositionalParameterIndex] = argument;
                ++nextPositionalParameterIndex;
            }

            return alignedArguments;
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
                (invocationExpression.ArgumentList.Arguments.Count == 3 || invocationExpression.ArgumentList.Arguments.Count == 5) &&
                TryResolveArrayElementTypeSymbol(semantic, invocationExpression.ArgumentList.Arguments[0].Expression, out ITypeSymbol copyElementTypeSymbol)) {
                codeConverter?.RegisterRuntimeRequirement("NativeArray");
                string copyElementTypeName = GetCppTypeToken(VariableUtil.GetVarType(copyElementTypeSymbol), context.Program);
                lines.Add($"Array<{copyElementTypeName}>::Copy(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("void");
                return true;
            } else if (methodSymbol != null &&
                methodSymbol.IsStatic &&
                string.Equals(methodSymbol.Name, "Resize", StringComparison.Ordinal) &&
                string.Equals(methodSymbol.ContainingType?.Name, "Array", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 2 &&
                TryResolveArrayElementTypeSymbol(semantic, invocationExpression.ArgumentList.Arguments[0].Expression, out ITypeSymbol resizeElementTypeSymbol)) {
                codeConverter?.RegisterRuntimeRequirement("NativeArray");
                string resizeElementTypeName = GetCppTypeToken(VariableUtil.GetVarType(resizeElementTypeSymbol), context.Program);
                lines.Add($"Array<{resizeElementTypeName}>::Resize(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("void");
                return true;
            } else if (invocationExpression.Expression is MemberAccessExpressionSyntax copyMemberAccess &&
                string.Equals(copyMemberAccess.Expression.ToString(), "Array", StringComparison.Ordinal) &&
                copyMemberAccess.Name is IdentifierNameSyntax copyIdentifierName &&
                string.Equals(copyIdentifierName.Identifier.Text, "Copy", StringComparison.Ordinal) &&
                (invocationExpression.ArgumentList.Arguments.Count == 3 || invocationExpression.ArgumentList.Arguments.Count == 5) &&
                TryResolveArrayElementTypeSymbol(semantic, invocationExpression.ArgumentList.Arguments[0].Expression, out ITypeSymbol copySyntaxElementTypeSymbol)) {
                codeConverter?.RegisterRuntimeRequirement("NativeArray");
                string copyElementTypeName = GetCppTypeToken(VariableUtil.GetVarType(copySyntaxElementTypeSymbol), context.Program);
                lines.Add($"Array<{copyElementTypeName}>::Copy(");
                AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                lines.Add(")");
                resultType = VariableUtil.GetVarType("void");
                return true;
            } else if (invocationExpression.Expression is MemberAccessExpressionSyntax resizeMemberAccess &&
                string.Equals(resizeMemberAccess.Expression.ToString(), "Array", StringComparison.Ordinal) &&
                resizeMemberAccess.Name is IdentifierNameSyntax resizeIdentifierName &&
                string.Equals(resizeIdentifierName.Identifier.Text, "Resize", StringComparison.Ordinal) &&
                invocationExpression.ArgumentList.Arguments.Count == 2 &&
                TryResolveArrayElementTypeSymbol(semantic, invocationExpression.ArgumentList.Arguments[0].Expression, out ITypeSymbol resizeSyntaxElementTypeSymbol)) {
                codeConverter?.RegisterRuntimeRequirement("NativeArray");
                string resizeElementTypeName = GetCppTypeToken(VariableUtil.GetVarType(resizeSyntaxElementTypeSymbol), context.Program);
                lines.Add($"Array<{resizeElementTypeName}>::Resize(");
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
            if (expression is IdentifierNameSyntax discardIdentifier &&
                string.Equals(discardIdentifier.Identifier.Text, "_", StringComparison.Ordinal)) {
                ITypeSymbol discardIdentifierTypeSymbol = TryResolveOutArgumentTypeSymbol(semantic, expression, parameterSymbol);
                VariableType discardIdentifierVariableType = VariableUtil.GetVarType(discardIdentifierTypeSymbol ?? semantic.GetTypeInfo(expression).Type);
                CPPTypeData discardIdentifierTypeData;
                VariableType discardIdentifierCppType = ConvertToCPPType(discardIdentifierVariableType, out discardIdentifierTypeData);
                string discardIdentifierPointerSuffix = discardIdentifierTypeData.IsPointer ? "*" : string.Empty;
                RegisterGeneratedTypeReferences(context, discardIdentifierVariableType);
                string discardIdentifierTypeName = QualifyRenderedCppTypeName(discardIdentifierCppType.ToCPPString(context.Program), context);
                beforeLines.Add($"{discardIdentifierTypeName}{discardIdentifierPointerSuffix} {GetDiscardTemporaryName(discardIdentifier)};\n");
                return;
            }

            if (expression is not DeclarationExpressionSyntax declarationExpression) {
                return;
            }

            ITypeSymbol inferredOutTypeSymbol = TryResolveOutArgumentTypeSymbol(semantic, expression, parameterSymbol);
            if (declarationExpression.Designation is DiscardDesignationSyntax) {
                VariableType discardVariableType = declarationExpression.Type is IdentifierNameSyntax discardDeclarationIdentifier &&
                    string.Equals(discardDeclarationIdentifier.Identifier.Text, "var", StringComparison.Ordinal) &&
                    inferredOutTypeSymbol != null
                    ? VariableUtil.GetVarType(inferredOutTypeSymbol)
                    : VariableUtil.GetVarType(inferredOutTypeSymbol ?? semantic.GetTypeInfo(declarationExpression).Type);
                CPPTypeData discardTypeData;
                VariableType discardCppType = ConvertToCPPType(discardVariableType, out discardTypeData);
                string discardPointerSuffix = discardTypeData.IsPointer ? "*" : string.Empty;
                RegisterGeneratedTypeReferences(context, discardVariableType);
                string discardTypeName = QualifyRenderedCppTypeName(discardCppType.ToCPPString(context.Program), context);
                beforeLines.Add($"{discardTypeName}{discardPointerSuffix} {GetDiscardTemporaryName(declarationExpression)};\n");
                return;
            }

            if (declarationExpression.Designation is not SingleVariableDesignationSyntax singleVariableDesignation) {
                return;
            }

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
            string emittedIdentifier = ResolveTrackedIdentifierEmissionName(context, singleVariableDesignation.Identifier.Text);
            beforeLines.Add($"{typeName}{pointerSuffix} {emittedIdentifier};\n");
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
                ArgumentSyntax separatorArgument = invocationExpression.ArgumentList.Arguments[0];
                List<string> separatorLines = new List<string>();
                int separatorStart = context.DepthClass;
                ProcessExpression(semantic, context, separatorArgument.Expression, separatorLines);
                context.PopClass(separatorStart);

                if (ShouldScopeDeleteManagedInvocationArgument(
                    semantic,
                    separatorArgument.Expression,
                    "String",
                    "Split",
                    "separators")) {
                    string temporaryName = CreateTemporaryName("__scopedArg");
                    lines.Add("([&]() {\n");
                    lines.Add("auto ");
                    lines.Add(temporaryName);
                    lines.Add(" = ");
                    lines.AddRange(separatorLines);
                    lines.Add(";\n");
                    AppendDeleteGuard(lines, temporaryName, "__scopedArgDeleteGuard");
                    lines.Add("return String::Split(");
                    lines.Add(receiverText);
                    lines.Add(", ");
                    lines.Add(temporaryName);
                    lines.Add(", ");
                    AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments.Skip(1), lines);
                    lines.Add(");\n");
                    lines.Add("})()");
                } else {
                    lines.Add("String::Split(");
                    lines.Add(receiverText);
                    lines.Add(", ");
                    AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
                    lines.Add(")");
                }

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

        bool TryProcessNativeFreeFunctionInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = null;
            IMethodSymbol methodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (!TryResolveNativeFreeFunctionMetadata(methodSymbol, out string functionName, out string includePath)) {
                return false;
            }

            ConversionClass currentClass = context.GetCurrentClass();
            if (currentClass != null && !string.IsNullOrWhiteSpace(includePath)) {
                currentClass.SourceIncludes.Add(includePath);
            }

            lines.Add(functionName);
            lines.Add("(");
            AppendInvocationArguments(semantic, context, invocationExpression.ArgumentList.Arguments, lines);
            lines.Add(")");

            if (methodSymbol?.ReturnType != null && methodSymbol.ReturnType.SpecialType != SpecialType.System_Void) {
                resultType = VariableUtil.GetVarType(methodSymbol.ReturnType);
            }

            return true;
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

            List<string> beforeLines = new List<string>();
            string delegateText = RenderExpressionText(semantic, context, invocationExpression.Expression);
            System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameterSymbols = invokeMethodSymbol != null
                ? invokeMethodSymbol.Parameters
                : System.Collections.Immutable.ImmutableArray<IParameterSymbol>.Empty;
            List<string> argumentLines = new List<string>();
            for (int argumentIndex = 0; argumentIndex < invocationExpression.ArgumentList.Arguments.Count; ++argumentIndex) {
                ArgumentSyntax argument = invocationExpression.ArgumentList.Arguments[argumentIndex];
                List<string> argumentExpressionLines = new List<string>();
                int argumentStart = context.DepthClass;
                ExpressionResult argumentResult = ProcessExpression(semantic, context, argument.Expression, argumentExpressionLines);
                context.PopClass(argumentStart);
                if (argumentResult.BeforeLines != null && argumentResult.BeforeLines.Count > 0) {
                    beforeLines.AddRange(argumentResult.BeforeLines);
                }

                IParameterSymbol parameterSymbol = argumentIndex < parameterSymbols.Length ? parameterSymbols[argumentIndex] : null;
                AppendInvocationArgument(
                    semantic,
                    context,
                    argument.Expression,
                    argumentExpressionLines,
                    parameterSymbol,
                    invokeMethodSymbol,
                    beforeLines,
                    argumentLines);

                if (!string.IsNullOrEmpty(argument.RefKindKeyword.ToString())) {
                    AddRefOrOutDeclarationBeforeLines(semantic, context, argument.Expression, parameterSymbol, beforeLines);
                }

                if (argumentIndex != invocationExpression.ArgumentList.Arguments.Count - 1) {
                    argumentLines.Add(", ");
                }
            }

            if (beforeLines.Count > 0) {
                lines.AddRange(beforeLines);
            }

            lines.Add("(*");
            lines.Add(delegateText);
            lines.Add(")(");
            lines.AddRange(argumentLines);
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
            if (string.Equals(typeName, "FunctionPointer", StringComparison.Ordinal)) {
                return false;
            }

            if (typeName.Contains("Func", StringComparison.Ordinal) ||
                typeName.Contains("Action", StringComparison.Ordinal)) {
                return true;
            }

            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(variableType, out typeData);
            string cppTypeName = cppType.ToCPPString(context.Program);
            if (string.Equals(cppTypeName, "FunctionPointer", StringComparison.Ordinal) ||
                cppTypeName.StartsWith("FunctionPointer<", StringComparison.Ordinal) ||
                cppTypeName.Contains("::FunctionPointer<", StringComparison.Ordinal)) {
                return false;
            }

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
            if (receiverTypeSymbol is ITypeParameterSymbol) {
                RegisterRuntimeRequirement("NativeHashCode");
                lines.Add("he_cpp_get_hash_code(");
                lines.Add(receiverText);
                lines.Add(")");
                return true;
            }

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
                SpecialType.System_IntPtr or
                SpecialType.System_UIntPtr or
                SpecialType.System_Single or
                SpecialType.System_Double;
        }

        bool TryProcessPrimitiveCompareToInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out VariableType resultType) {
            resultType = VariableUtil.GetVarType("int");

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name is not IdentifierNameSyntax identifierName ||
                !string.Equals(identifierName.Identifier.Text, "CompareTo", StringComparison.Ordinal) ||
                invocationExpression.ArgumentList.Arguments.Count != 1) {
                return false;
            }

            if (!TryGetExpressionTypeSymbol(semantic, memberAccess.Expression, out ITypeSymbol receiverTypeSymbol) ||
                receiverTypeSymbol == null) {
                IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
                receiverTypeSymbol = invokedMethodSymbol?.ReceiverType;
            }

            if (receiverTypeSymbol == null || !IsPrimitiveGetHashCodeReceiverType(receiverTypeSymbol)) {
                return false;
            }

            string receiverText = RenderExpressionText(semantic, context, memberAccess.Expression);
            string argumentText = RenderExpressionText(semantic, context, invocationExpression.ArgumentList.Arguments[0].Expression);
            lines.Add($"(({receiverText}) < ({argumentText}) ? -1 : (({receiverText}) > ({argumentText}) ? 1 : 0))");
            return true;
        }

        static bool RequiresDependentTemplateQualifier(IMethodSymbol methodSymbol) {
            return methodSymbol?.IsGenericMethod == true &&
                ContainsTypeParameter(methodSymbol.ContainingType);
        }

        /// <summary>
        /// Determines whether one invocation target needs the C++ dependent-template disambiguator.
        /// </summary>
        /// <param name="methodSymbol">Resolved method being invoked.</param>
        /// <param name="receiverIsDependent">Whether the invocation receiver is dependent on template parameters.</param>
        /// <returns><c>true</c> when the invocation names a dependent member template; otherwise <c>false</c>.</returns>
        static bool ShouldEmitDependentTemplateQualifier(IMethodSymbol methodSymbol, bool receiverIsDependent) {
            return methodSymbol?.IsGenericMethod == true &&
                (receiverIsDependent || ContainsTypeParameter(methodSymbol.ContainingType));
        }

        static bool ReceiverRequiresDependentTemplateQualifier(SemanticModel semantic, ExpressionSyntax receiverExpression) {
            if (semantic == null ||
                receiverExpression == null ||
                !ReferenceEquals(receiverExpression.SyntaxTree, semantic.SyntaxTree)) {
                return false;
            }

            if (ExpressionContainsDependentTemplateReceiver(semantic, receiverExpression)) {
                return true;
            }

            ITypeSymbol receiverTypeSymbol = semantic.GetTypeInfo(receiverExpression).ConvertedType ?? semantic.GetTypeInfo(receiverExpression).Type;
            return ContainsTypeParameter(receiverTypeSymbol);
        }

        static bool ExpressionContainsDependentTemplateReceiver(SemanticModel semantic, ExpressionSyntax expression) {
            if (semantic == null ||
                expression == null ||
                !ReferenceEquals(expression.SyntaxTree, semantic.SyntaxTree)) {
                return false;
            }

            ITypeSymbol expressionTypeSymbol = semantic.GetTypeInfo(expression).ConvertedType ?? semantic.GetTypeInfo(expression).Type;
            if (ContainsTypeParameter(expressionTypeSymbol)) {
                return true;
            }

            if (expression is MemberAccessExpressionSyntax memberAccessExpression) {
                return ExpressionContainsDependentTemplateReceiver(semantic, memberAccessExpression.Expression);
            }

            if (expression is ElementAccessExpressionSyntax elementAccessExpression) {
                return ExpressionContainsDependentTemplateReceiver(semantic, elementAccessExpression.Expression);
            }

            if (expression is ConditionalAccessExpressionSyntax conditionalAccessExpression) {
                return ExpressionContainsDependentTemplateReceiver(semantic, conditionalAccessExpression.Expression);
            }

            if (expression is ParenthesizedExpressionSyntax parenthesizedExpressionSyntax) {
                return ExpressionContainsDependentTemplateReceiver(semantic, parenthesizedExpressionSyntax.Expression);
            }

            if (expression is CastExpressionSyntax castExpressionSyntax) {
                return ExpressionContainsDependentTemplateReceiver(semantic, castExpressionSyntax.Expression);
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnaryExpressionSyntax) {
                return ExpressionContainsDependentTemplateReceiver(semantic, prefixUnaryExpressionSyntax.Operand);
            }

            if (expression is PostfixUnaryExpressionSyntax postfixUnaryExpressionSyntax) {
                return ExpressionContainsDependentTemplateReceiver(semantic, postfixUnaryExpressionSyntax.Operand);
            }

            return false;
        }

        static bool ContainsTypeParameter(ITypeSymbol typeSymbol) {
            if (typeSymbol == null) {
                return false;
            }

            if (typeSymbol is ITypeParameterSymbol) {
                return true;
            }

            if (typeSymbol is IArrayTypeSymbol arrayTypeSymbol) {
                return ContainsTypeParameter(arrayTypeSymbol.ElementType);
            }

            if (typeSymbol is IPointerTypeSymbol pointerTypeSymbol) {
                return ContainsTypeParameter(pointerTypeSymbol.PointedAtType);
            }

            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return false;
            }

            foreach (ITypeSymbol typeArgument in namedTypeSymbol.TypeArguments) {
                if (ContainsTypeParameter(typeArgument)) {
                    return true;
                }
            }

            if (namedTypeSymbol.ContainingType != null &&
                ContainsTypeParameter(namedTypeSymbol.ContainingType)) {
                return true;
            }

            return false;
        }

        bool ShouldEmitTemplateDisambiguatorForIdentifier(
            SemanticModel semantic,
            IdentifierNameSyntax identifier,
            ISymbol symbol) {
            if (semantic == null ||
                identifier?.Parent is not MemberAccessExpressionSyntax memberAccess ||
                !ReferenceEquals(memberAccess.Name, identifier) ||
                memberAccess.Parent is not InvocationExpressionSyntax invocationExpression ||
                !ReferenceEquals(invocationExpression.Expression, memberAccess)) {
                return false;
            }

            IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (invokedMethodSymbol == null || !invokedMethodSymbol.IsGenericMethod) {
                return false;
            }

            ITypeSymbol receiverTypeSymbol = semantic.GetTypeInfo(memberAccess.Expression).ConvertedType ?? semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (receiverTypeSymbol != null && ContainsTypeParameter(receiverTypeSymbol)) {
                return true;
            }

            return ContainsTypeParameter(invokedMethodSymbol.ContainingType);
        }

        bool ShouldEmitTemplateDisambiguatorForGenericName(
            SemanticModel semantic,
            GenericNameSyntax genericName) {
            if (semantic == null ||
                genericName?.Parent is not MemberAccessExpressionSyntax memberAccess ||
                !ReferenceEquals(memberAccess.Name, genericName) ||
                memberAccess.Parent is not InvocationExpressionSyntax invocationExpression ||
                !ReferenceEquals(invocationExpression.Expression, memberAccess)) {
                return false;
            }

            IMethodSymbol invokedMethodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
            if (invokedMethodSymbol == null || !invokedMethodSymbol.IsGenericMethod) {
                return false;
            }

            ITypeSymbol receiverTypeSymbol = semantic.GetTypeInfo(memberAccess.Expression).ConvertedType ?? semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (receiverTypeSymbol != null && ContainsTypeParameter(receiverTypeSymbol)) {
                return true;
            }

            return ContainsTypeParameter(invokedMethodSymbol.ContainingType);
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

            if (ShouldEmitRuntimeObjectToString(receiverTypeSymbol, toStringMethodSymbol)) {
                RegisterRuntimeRequirement("NativeString");
                lines.Add($"String::ToJoinString({receiverText})");
                return true;
            }

            if (!IsNativeToStringTypeSymbol(receiverTypeSymbol)) {
                return false;
            }

            lines.Add($"std::to_string({receiverText})");
            return true;
        }

        static bool ShouldEmitRuntimeObjectToString(ITypeSymbol receiverTypeSymbol, IMethodSymbol toStringMethodSymbol) {
            if (receiverTypeSymbol == null ||
                receiverTypeSymbol.IsValueType ||
                receiverTypeSymbol.SpecialType == SpecialType.System_String ||
                receiverTypeSymbol.SpecialType == SpecialType.System_Char ||
                receiverTypeSymbol.TypeKind == TypeKind.Enum) {
                return false;
            }

            if (toStringMethodSymbol == null) {
                return receiverTypeSymbol.IsReferenceType;
            }

            if (toStringMethodSymbol.ContainingType?.SpecialType == SpecialType.System_Object) {
                return true;
            }

            return !toStringMethodSymbol.IsOverride &&
                receiverTypeSymbol.IsReferenceType;
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
            IMethodSymbol candidateMethodSymbol = ResolveBestInvocationCandidateMethodSymbol(semantic, invocationExpression, invocationSymbolInfo, argumentCount);
            if (candidateMethodSymbol != null) {
                return candidateMethodSymbol;
            }

            IMethodSymbol methodSymbol = ResolveMethodSymbol(invocationSymbolInfo);
            if (CanMethodMatchInvocationArguments(methodSymbol, argumentCount)) {
                return methodSymbol;
            }

            SymbolInfo expressionSymbolInfo = semantic.GetSymbolInfo(invocationExpression.Expression);
            candidateMethodSymbol = ResolveBestInvocationCandidateMethodSymbol(semantic, invocationExpression, expressionSymbolInfo, argumentCount);
            if (candidateMethodSymbol != null) {
                return candidateMethodSymbol;
            }

            methodSymbol = ResolveMethodSymbol(expressionSymbolInfo);
            if (CanMethodMatchInvocationArguments(methodSymbol, argumentCount)) {
                return methodSymbol;
            }

            return null;
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

        static IMethodSymbol ResolveBestInvocationCandidateMethodSymbol(
            SemanticModel semantic,
            InvocationExpressionSyntax invocationExpression,
            SymbolInfo symbolInfo,
            int argumentCount) {
            return symbolInfo.CandidateSymbols
                .OfType<IMethodSymbol>()
                .Select(candidateMethodSymbol => new {
                    Method = candidateMethodSymbol,
                    Score = ScoreInvocationCandidateMethod(semantic, invocationExpression, candidateMethodSymbol, argumentCount)
                })
                .Where(candidate => candidate.Score >= 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Method.Parameters.Length == argumentCount)
                .ThenByDescending(candidate => candidate.Method.Parameters.Length)
                .ThenBy(candidate => candidate.Method.IsGenericMethod)
                .Select(candidate => candidate.Method)
                .FirstOrDefault();
        }

        static IMethodSymbol ResolveBestInvocationCandidateMethodSymbol(SymbolInfo symbolInfo, int argumentCount) {
            return symbolInfo.CandidateSymbols
                .OfType<IMethodSymbol>()
                .Where(candidateMethodSymbol => CanMethodMatchInvocationArguments(candidateMethodSymbol, argumentCount))
                .OrderByDescending(candidateMethodSymbol => candidateMethodSymbol.Parameters.Length == argumentCount)
                .ThenByDescending(candidateMethodSymbol => candidateMethodSymbol.Parameters.Length)
                .ThenBy(candidateMethodSymbol => candidateMethodSymbol.IsGenericMethod)
                .FirstOrDefault();
        }

        static int ScoreInvocationCandidateMethod(
            SemanticModel semantic,
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol methodSymbol,
            int argumentCount) {
            if (!CanMethodMatchInvocationArguments(methodSymbol, argumentCount)) {
                return -1;
            }

            int score = methodSymbol.Parameters.Length == argumentCount ? 1000 : 0;
            SeparatedSyntaxList<ArgumentSyntax> arguments = invocationExpression.ArgumentList.Arguments;
            for (int argumentIndex = 0; argumentIndex < arguments.Count; argumentIndex++) {
                IParameterSymbol parameterSymbol = ResolveInvocationParameter(methodSymbol, argumentIndex);
                if (parameterSymbol == null) {
                    return -1;
                }

                if (!MatchesArgumentModifier(arguments[argumentIndex], parameterSymbol)) {
                    return -1;
                }

                ITypeSymbol argumentTypeSymbol = semantic.GetTypeInfo(arguments[argumentIndex].Expression).ConvertedType ??
                    semantic.GetTypeInfo(arguments[argumentIndex].Expression).Type;
                if (argumentTypeSymbol == null) {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(argumentTypeSymbol, parameterSymbol.Type)) {
                    score += 100;
                    continue;
                }

                Conversion conversion = semantic.ClassifyConversion(arguments[argumentIndex].Expression, parameterSymbol.Type);
                if (!conversion.Exists) {
                    return -1;
                }

                if (conversion.IsIdentity) {
                    score += 90;
                } else if (conversion.IsImplicit) {
                    score += 60;
                } else if (conversion.IsReference) {
                    score += 40;
                } else if (conversion.IsBoxing || conversion.IsUnboxing) {
                    score += 20;
                } else {
                    score += 1;
                }
            }

            return score;
        }

        static IParameterSymbol ResolveInvocationParameter(IMethodSymbol methodSymbol, int argumentIndex) {
            if (methodSymbol == null || methodSymbol.Parameters.Length == 0) {
                return null;
            }

            if (methodSymbol.Parameters.Last().IsParams &&
                argumentIndex >= methodSymbol.Parameters.Length - 1) {
                return methodSymbol.Parameters.Last();
            }

            return argumentIndex < methodSymbol.Parameters.Length
                ? methodSymbol.Parameters[argumentIndex]
                : null;
        }

        static bool MatchesArgumentModifier(ArgumentSyntax argumentSyntax, IParameterSymbol parameterSymbol) {
            RefKind argumentRefKind = argumentSyntax?.RefKindKeyword.Kind() switch {
                SyntaxKind.RefKeyword => RefKind.Ref,
                SyntaxKind.OutKeyword => RefKind.Out,
                SyntaxKind.InKeyword => RefKind.In,
                _ => RefKind.None
            };

            return parameterSymbol.RefKind switch {
                RefKind.Ref => argumentRefKind == RefKind.Ref,
                RefKind.Out => argumentRefKind == RefKind.Out,
                RefKind.In => argumentRefKind == RefKind.In || argumentRefKind == RefKind.None,
                _ => argumentRefKind == RefKind.None
            };
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

            if (binary.IsKind(SyntaxKind.AsExpression)) {
                return ProcessAsTypeExpression(semantic, context, binary, lines);
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

            if (TryProcessDependentNullComparison(semantic, context, binary, lines)) {
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
            }

            if (TryProcessEnumArithmeticExpression(semantic, context, binary, lines, out ExpressionResult enumArithmeticResult)) {
                return enumArithmeticResult;
            }

            if (TryProcessFloatingPointModuloExpression(semantic, context, binary, lines, out ExpressionResult floatingPointModuloResult)) {
                return floatingPointModuloResult;
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

        bool TryProcessFloatingPointModuloExpression(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binary,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (!binary.IsKind(SyntaxKind.ModuloExpression)) {
                return false;
            }

            ITypeSymbol leftTypeSymbol = semantic.GetTypeInfo(binary.Left).ConvertedType ?? semantic.GetTypeInfo(binary.Left).Type;
            ITypeSymbol rightTypeSymbol = semantic.GetTypeInfo(binary.Right).ConvertedType ?? semantic.GetTypeInfo(binary.Right).Type;
            if (!IsFloatingPointTypeSymbol(leftTypeSymbol) && !IsFloatingPointTypeSymbol(rightTypeSymbol)) {
                return false;
            }

            RegisterRuntimeRequirement("Math");

            List<string> left = new List<string>();
            int startLeft = context.DepthClass;
            ExpressionResult leftResult = ProcessExpression(semantic, context, binary.Left, left);
            context.PopClass(startLeft);

            List<string> right = new List<string>();
            int startRight = context.DepthClass;
            ExpressionResult rightResult = ProcessExpression(semantic, context, binary.Right, right);
            context.PopClass(startRight);

            lines.Add("std::fmod(");
            lines.AddRange(left);
            lines.Add(", ");
            lines.AddRange(right);
            lines.Add(")");

            ITypeSymbol resultTypeSymbol = semantic.GetTypeInfo(binary).ConvertedType ?? semantic.GetTypeInfo(binary).Type;
            VariableType resultType = resultTypeSymbol != null ? VariableUtil.GetVarType(resultTypeSymbol) : VariableUtil.GetVarType("double");
            result = new ExpressionResult(leftResult.Processed && rightResult.Processed, VariablePath.Unknown, resultType);
            return true;
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

            string tempName = CreateTemporaryName("__coalesce");
            string typeName = QualifyRenderedCppTypeName(cppResultType.ToCPPString(context.Program), context);
            string pointerSuffix = resultTypeData.IsPointer ? "*" : string.Empty;

            lines.Add($"({GetObjectConstructionLambdaCaptureList(context)}() {{\n");
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

        /// <summary>
        /// Returns the lambda capture list used by generated immediate-invocation wrappers.
        /// Non-local lambdas cannot use capture-default syntax, so static/namespace-scope
        /// initializers must emit an empty capture list.
        /// </summary>
        string GetObjectConstructionLambdaCaptureList(LayerContext context) {
            return context?.GetCurrentFunction() != null ? "[&]" : "[]";
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

        bool TryProcessDependentNullComparison(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binary,
            List<string> lines) {
            if (!binary.IsKind(SyntaxKind.NotEqualsExpression) &&
                !binary.IsKind(SyntaxKind.EqualsExpression)) {
                return false;
            }

            ExpressionSyntax valueSide = null;
            if (binary.Left is LiteralExpressionSyntax leftLiteral &&
                leftLiteral.IsKind(SyntaxKind.NullLiteralExpression)) {
                valueSide = binary.Right;
            } else if (binary.Right is LiteralExpressionSyntax rightLiteral &&
                rightLiteral.IsKind(SyntaxKind.NullLiteralExpression)) {
                valueSide = binary.Left;
            }

            if (valueSide == null ||
                !TryGetExpressionTypeSymbol(semantic, valueSide, out ITypeSymbol valueTypeSymbol) ||
                !ShouldUsePortableNullComparisonHelper(valueTypeSymbol)) {
                return false;
            }

            List<string> valueLines = new List<string>();
            int startDepth = context.DepthClass;
            ProcessExpression(semantic, context, valueSide, valueLines);
            context.PopClass(startDepth);

            RegisterRuntimeRequirement("NativeNullComparison");
            if (binary.IsKind(SyntaxKind.NotEqualsExpression)) {
                lines.Add("!");
            }

            lines.Add("he_cpp_is_null(");
            lines.AddRange(valueLines);
            lines.Add(")");
            return true;
        }

        static bool ShouldUsePortableNullComparisonHelper(ITypeSymbol valueTypeSymbol) {
            if (valueTypeSymbol is not ITypeParameterSymbol typeParameterSymbol) {
                return false;
            }

            return !typeParameterSymbol.HasReferenceTypeConstraint;
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
            bool qualifyWithThis = TryResolveGenericInvocationMethodSymbol(semantic, generic, out IMethodSymbol methodSymbol) &&
                ShouldQualifyGenericInvocationWithThis(context, methodSymbol, generic);
            if (qualifyWithThis) {
                lines.Add("this->");
            }

            if (qualifyWithThis || ShouldEmitTemplateDisambiguatorForGenericName(semantic, generic)) {
                lines.Add("template ");
            }

            if (TryResolveGenericInvocationFunctionByArguments(semantic, context, generic, out ConversionFunction resolvedGeneratedFunction)) {
                lines.Add(GetEmittedFunctionName(resolvedGeneratedFunction));
            } else if (methodSymbol != null) {
                lines.Add(ResolveConvertedFunctionName(methodSymbol));
            } else {
                lines.Add(generic.Identifier.ToString());
            }

            lines.Add("<");

            int count = generic.TypeArgumentList.Arguments.Count;
            int i = 0;
            foreach (var genType in generic.TypeArgumentList.Arguments) {
                RegisterGeneratedTypeReferences(context, VariableUtil.GetVarType(genType, semantic));
                lines.Add(RenderConvertedGenericArgumentType(semantic, context, genType));

                if (i < count - 1) {
                    lines.Add(",");
                }

                i++;
            }
            lines.Add(">");
        }

        static bool ShouldQualifyGenericInvocationWithThis(LayerContext context, IMethodSymbol methodSymbol, GenericNameSyntax genericName) {
            ConversionClass currentClass = context?.GetCurrentClass();
            if (currentClass == null ||
                context.GetCurrentFunction()?.Function?.IsStatic == true ||
                methodSymbol == null ||
                methodSymbol.IsStatic ||
                genericName?.Parent is not InvocationExpressionSyntax) {
                return false;
            }

            return currentClass.GenericArgs?.Count > 0;
        }

        bool TryResolveGenericInvocationMethodSymbol(SemanticModel semantic, GenericNameSyntax genericName, out IMethodSymbol methodSymbol) {
            methodSymbol = null;
            if (genericName == null) {
                return false;
            }

            InvocationExpressionSyntax invocationExpression = genericName.Parent switch {
                InvocationExpressionSyntax genericDirectInvocation when ReferenceEquals(genericDirectInvocation.Expression, genericName) => genericDirectInvocation,
                MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax genericMemberInvocation } genericMemberAccess when ReferenceEquals(genericMemberAccess.Name, genericName) && ReferenceEquals(genericMemberInvocation.Expression, genericMemberAccess) => genericMemberInvocation,
                MemberBindingExpressionSyntax { Parent: ConditionalAccessExpressionSyntax genericConditionalAccess } genericMemberBinding when ReferenceEquals(genericMemberBinding.Name, genericName) && genericConditionalAccess.Parent is InvocationExpressionSyntax genericConditionalInvocation && ReferenceEquals(genericConditionalInvocation.Expression, genericConditionalAccess) => genericConditionalInvocation,
                _ => null
            };

            if (invocationExpression != null) {
                SymbolInfo genericNameSymbolInfo = semantic.GetSymbolInfo(genericName);
                methodSymbol = ResolveBestInvocationCandidateMethodSymbol(semantic, invocationExpression, genericNameSymbolInfo, invocationExpression.ArgumentList.Arguments.Count);
                if (methodSymbol != null) {
                    return true;
                }

                methodSymbol = ResolveMethodSymbol(genericNameSymbolInfo);
                if (CanMethodMatchInvocationArguments(methodSymbol, invocationExpression.ArgumentList.Arguments.Count)) {
                    return true;
                }
            }

            if (genericName.Parent is InvocationExpressionSyntax directInvocation &&
                ReferenceEquals(directInvocation.Expression, genericName)) {
                methodSymbol = ResolveInvokedMethodSymbol(semantic, directInvocation);
                return methodSymbol != null;
            }

            if (genericName.Parent is MemberAccessExpressionSyntax memberAccess &&
                ReferenceEquals(memberAccess.Name, genericName) &&
                memberAccess.Parent is InvocationExpressionSyntax memberInvocation &&
                ReferenceEquals(memberInvocation.Expression, memberAccess)) {
                methodSymbol = ResolveInvokedMethodSymbol(semantic, memberInvocation);
                return methodSymbol != null;
            }

            if (genericName.Parent is MemberBindingExpressionSyntax memberBinding &&
                ReferenceEquals(memberBinding.Name, genericName) &&
                memberBinding.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.Parent is InvocationExpressionSyntax conditionalInvocation &&
                ReferenceEquals(conditionalInvocation.Expression, conditionalAccess)) {
                methodSymbol = ResolveInvokedMethodSymbol(semantic, conditionalInvocation);
                return methodSymbol != null;
            }

            return false;
        }

        bool TryResolveGenericInvocationFunctionByArguments(
            SemanticModel semantic,
            LayerContext context,
            GenericNameSyntax genericName,
            out ConversionFunction function) {
            function = null;
            if (context?.GetCurrentClass() == null ||
                genericName == null) {
                return false;
            }

            InvocationExpressionSyntax invocationExpression = genericName.Parent switch {
                InvocationExpressionSyntax directInvocation when ReferenceEquals(directInvocation.Expression, genericName) => directInvocation,
                MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax memberInvocation } memberAccess when ReferenceEquals(memberAccess.Name, genericName) && ReferenceEquals(memberInvocation.Expression, memberAccess) => memberInvocation,
                _ => null
            };
            if (invocationExpression == null) {
                return false;
            }

            ConversionClass currentClass = context.GetCurrentClass();
            List<VariableType> argumentTypes = invocationExpression.ArgumentList.Arguments
                .Select(argument => ResolveInvocationArgumentType(semantic, argument.Expression))
                .ToList();

            function = currentClass.Functions.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, genericName.Identifier.Text, StringComparison.Ordinal) &&
                candidate.InParameters?.Count == invocationExpression.ArgumentList.Arguments.Count &&
                (candidate.GenericParameters?.Count ?? 0) == genericName.TypeArgumentList.Arguments.Count &&
                MatchesGenericInvocationArguments(currentClass, candidate, invocationExpression.ArgumentList.Arguments, argumentTypes));
            return function != null;
        }

        static VariableType ResolveInvocationArgumentType(SemanticModel semantic, ExpressionSyntax expression) {
            ITypeSymbol argumentTypeSymbol = semantic.GetTypeInfo(expression).ConvertedType ??
                semantic.GetTypeInfo(expression).Type;
            return argumentTypeSymbol != null
                ? VariableUtil.GetVarType(argumentTypeSymbol)
                : new VariableType();
        }

        static bool MatchesGenericInvocationArguments(
            ConversionClass currentClass,
            ConversionFunction function,
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            List<VariableType> argumentTypes) {
            for (int index = 0; index < arguments.Count; index++) {
                ConversionVariable parameter = function.InParameters[index];
                if (!MatchesArgumentSyntaxModifier(parameter, arguments[index])) {
                    return false;
                }

                if (IsOpenGenericParameter(currentClass, function, parameter.VarType)) {
                    continue;
                }

                VariableType argumentType = argumentTypes[index];
                if (!MatchesParameterType(parameter.VarType, argumentType)) {
                    return false;
                }
            }

            return true;
        }

        static bool MatchesArgumentSyntaxModifier(ConversionVariable parameter, ArgumentSyntax argumentSyntax) {
            bool isRef = (parameter.Modifier & ParameterModifier.Ref) != 0;
            bool isOut = (parameter.Modifier & ParameterModifier.Out) != 0;
            bool isIn = (parameter.Modifier & ParameterModifier.In) != 0;

            return argumentSyntax?.RefKindKeyword.Kind() switch {
                SyntaxKind.RefKeyword => isRef && !isOut,
                SyntaxKind.OutKeyword => isOut,
                SyntaxKind.InKeyword => isIn || (!isRef && !isOut),
                _ => !isRef && !isOut
            };
        }

        static bool IsOpenGenericParameter(ConversionClass currentClass, ConversionFunction function, VariableType parameterType) {
            if (parameterType == null || string.IsNullOrWhiteSpace(parameterType.TypeName)) {
                return false;
            }

            if (function?.GenericParameters?.Contains(parameterType.TypeName) == true) {
                return true;
            }

            return currentClass?.GenericArgs?.Contains(parameterType.TypeName) == true;
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
            RegisterGeneratedTypeReferences(context, sourceType);
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
            AppendNativeLambda(
                semantic,
                context,
                simpleLambda,
                new[] { simpleLambda.Parameter },
                simpleLambda.Body,
                lines);
        }

        protected override ExpressionResult ProcessSwitchExpressionSyntax(
            SemanticModel semantic,
            LayerContext context,
            SwitchExpressionSyntax switchExpression,
            List<string> lines) {
            if (semantic == null || context == null || switchExpression == null || lines == null || switchExpression.Arms.Count == 0) {
                return new ExpressionResult(false);
            }

            List<string> governingExpressionLines = new List<string>();
            int governingStart = context.DepthClass;
            ExpressionResult governingResult = ProcessExpression(semantic, context, switchExpression.GoverningExpression, governingExpressionLines);
            context.PopClass(governingStart);
            if (!governingResult.Processed) {
                return new ExpressionResult(false);
            }

            List<string> beforeLines = governingResult.BeforeLines != null
                ? new List<string>(governingResult.BeforeLines)
                : new List<string>();
            VariableType resultType = null;

            lines.Add("(");
            for (int armIndex = 0; armIndex < switchExpression.Arms.Count; armIndex++) {
                SwitchExpressionArmSyntax arm = switchExpression.Arms[armIndex];
                bool isDiscardArm = arm.Pattern is DiscardPatternSyntax;
                if (!isDiscardArm) {
                    if (!TryAppendSwitchArmCondition(semantic, context, switchExpression, arm, governingExpressionLines, lines)) {
                        return new ExpressionResult(false);
                    }

                    lines.Add(" ? ");
                }

                List<string> armValueLines = new List<string>();
                int armStart = context.DepthClass;
                ExpressionResult armResult = ProcessExpression(semantic, context, arm.Expression, armValueLines);
                context.PopClass(armStart);
                if (!armResult.Processed) {
                    return new ExpressionResult(false);
                }

                if (armResult.BeforeLines != null && armResult.BeforeLines.Count > 0) {
                    beforeLines.AddRange(armResult.BeforeLines);
                }

                resultType ??= armResult.Type;
                lines.AddRange(armValueLines);

                if (!isDiscardArm) {
                    lines.Add(" : ");
                }
            }

            lines.Add(")");
            return beforeLines.Count > 0
                ? new ExpressionResult(true, VariablePath.Unknown, resultType, beforeLines)
                : new ExpressionResult(true, VariablePath.Unknown, resultType);
        }

        bool TryAppendSwitchArmCondition(
            SemanticModel semantic,
            LayerContext context,
            SwitchExpressionSyntax switchExpression,
            SwitchExpressionArmSyntax arm,
            List<string> governingExpressionLines,
            List<string> lines) {
            if (arm.Pattern is ConstantPatternSyntax constantPattern) {
                lines.Add("(");
                lines.AddRange(governingExpressionLines);
                lines.Add(" == ");
                int constantStart = context.DepthClass;
                ExpressionResult constantResult = ProcessExpression(semantic, context, constantPattern.Expression, lines);
                context.PopClass(constantStart);
                lines.Add(")");
                return constantResult.Processed;
            }

            if (arm.Pattern is DeclarationPatternSyntax declarationPattern &&
                declarationPattern.Designation is DiscardDesignationSyntax &&
                declarationPattern.Type is not null) {
                List<string> typeCheckLines = new List<string>();
                int typeCheckStart = context.DepthClass;
                ExpressionResult typeCheckResult = ProcessExpression(semantic, context, declarationPattern.Type, typeCheckLines);
                context.PopClass(typeCheckStart);
                if (!typeCheckResult.Processed) {
                    return false;
                }

                lines.Add("dynamic_cast<");
                lines.AddRange(typeCheckLines);
                lines.Add("*>(");
                lines.AddRange(governingExpressionLines);
                lines.Add(") != nullptr");
                return true;
            }

            return false;
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
            INamedTypeSymbol baseTypeSymbol = semantic?.GetTypeInfo(baseExpression).Type as INamedTypeSymbol;
            string baseClassName = baseTypeSymbol != null
                ? GetContainingTypeAccessName(context, baseTypeSymbol)
                : currentClass?.Extensions?.FirstOrDefault() ?? currentClass?.Name ?? "base";
            lines.Add(baseClassName);

            ConversionClass? baseClass = baseTypeSymbol != null
                ? context.Program.FindGeneratedClass(baseTypeSymbol)
                : context.Program.Classes.FirstOrDefault(c => c.Name == baseClassName);
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

            bool emitByValue = true;
            if (!emitByValue) {
                lines.Add("new ");
            }
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
        /// Determines whether one tuple expression should emit a direct native value construction instead of a heap allocation.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the tuple expression.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="tupleExpression">Tuple expression being lowered.</param>
        /// <returns><c>true</c> when the tuple literal targets a direct native value slot; otherwise <c>false</c>.</returns>
        bool ShouldEmitTupleExpressionByValue(
            SemanticModel semantic,
            LayerContext context,
            TupleExpressionSyntax tupleExpression) {
            if (tupleExpression?.Parent is not AssignmentExpressionSyntax assignmentExpression ||
                assignmentExpression.Right != tupleExpression) {
                return false;
            }

            return IsDirectTupleAssignmentTarget(semantic, context, assignmentExpression.Left);
        }

        /// <summary>
        /// Determines whether one assignment target writes into direct native storage suitable for by-value tuple construction.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the assignment target.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="targetExpression">Assignment target receiving the tuple literal.</param>
        /// <returns><c>true</c> when the target is backed by direct native value storage; otherwise <c>false</c>.</returns>
        bool IsDirectTupleAssignmentTarget(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax targetExpression) {
            if (targetExpression == null) {
                return false;
            }

            if (targetExpression is ElementAccessExpressionSyntax elementAccessExpression) {
                return IsDirectTupleAssignmentReceiver(semantic, context, elementAccessExpression.Expression);
            }

            if (targetExpression is MemberAccessExpressionSyntax memberAccessExpression) {
                return IsDirectTupleAssignmentReceiver(semantic, context, memberAccessExpression.Expression);
            }

            if (targetExpression is IdentifierNameSyntax identifierName) {
                return IsRefLocalExpression(semantic, identifierName);
            }

            return false;
        }

        /// <summary>
        /// Determines whether one receiver expression uses direct native member access so tuple writes land in value storage instead of pointer-backed objects.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the receiver expression.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="receiverExpression">Receiver expression to inspect.</param>
        /// <returns><c>true</c> when the receiver resolves to direct native storage; otherwise <c>false</c>.</returns>
        bool IsDirectTupleAssignmentReceiver(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax receiverExpression) {
            return receiverExpression != null &&
                ((TryResolveTrackedExpressionVariableType(context, receiverExpression, out VariableType trackedReceiverType) &&
                  IsDirectMemberAccessType(trackedReceiverType)) ||
                 UsesDirectMemberAccess(semantic, receiverExpression));
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
            VariableType sourceType = VariableUtil.GetVarType(defaultExpression.Type, semantic);
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);

            if (typeData.IsPointer) {
                lines.Add("nullptr");
                return;
            }

            lines.Add($"{QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context)}()");
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
            int interpolationDepth = context.DepthClass;
            ExpressionResult interpolationResult = ProcessExpression(semantic, context, expression, interpolationLines);
            context.PopClass(interpolationDepth);

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

            if (expression is IdentifierNameSyntax identifierName &&
                context?.GetCurrentClass() != null) {
                ISymbol identifierSymbol = semantic.GetSymbolInfo(identifierName).Symbol;
                if (identifierSymbol is IAliasSymbol identifierAliasSymbol) {
                    identifierSymbol = identifierAliasSymbol.Target;
                }

                if ((identifierSymbol == null || identifierSymbol is INamedTypeSymbol || identifierSymbol is INamespaceSymbol) &&
                    TryResolveGeneratedPropertyAccessor(context.GetCurrentClass(), identifierName.Identifier.Text, "get_", out ConversionFunction accessorFunction)) {
                    expressionText = $"this->{accessorFunction.Name}()";
                    if (accessorFunction.ReturnType != null) {
                        expressionType = accessorFunction.ReturnType;
                    }
                }
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

            if (TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol toStringTypeSymbol) &&
                toStringTypeSymbol.IsValueType) {
                return $"{expressionText}.ToString()";
            }

            RegisterRuntimeRequirement("NativeString");
            return $"String::ToJoinString({expressionText})";
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
            if (sourceType != null &&
                (sourceType.Type == VariableDataType.Tuple ||
                 string.Equals(sourceType.TypeName, "ValueTuple", StringComparison.Ordinal))) {
                return cppType.ToCPPString(program);
            }

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

        /// <summary>
        /// Returns whether the supplied Roslyn type symbol represents one floating-point primitive.
        /// </summary>
        /// <param name="typeSymbol">Type symbol to inspect.</param>
        /// <returns>True when the symbol is <see cref="float"/> or <see cref="double"/>.</returns>
        static bool IsFloatingPointTypeSymbol(ITypeSymbol typeSymbol) {
            if (typeSymbol == null) {
                return false;
            }

            return typeSymbol.SpecialType switch {
                SpecialType.System_Single => true,
                SpecialType.System_Double => true,
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
            if (TryProcessIndexerElementAccessExpression(semantic, context, elementAccess, lines)) {
                return;
            }

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
                if (TryAppendIndexFromEndElementAccessArgument(
                    semantic,
                    context,
                    elementAccess.Expression,
                    shouldDereferenceElementAccess,
                    false,
                    argument.Expression,
                    lines)) {
                    continue;
                }

                startClass = context.DepthClass;
                ProcessExpression(semantic, context, argument.Expression, lines);
                context.PopClass(startClass);

            }

            // Add the closing bracket
            lines.Add("]");

            context.LoadClass(saved);
        }

        /// <summary>
        /// Lowers user-defined indexer access to generated get_Item calls so converted types do not need ad hoc native operator[] support.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the expression.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="elementAccess">Indexer access being lowered.</param>
        /// <param name="lines">Output token buffer that receives emitted C++.</param>
        /// <returns><c>true</c> when the element access resolved to an indexer property and was lowered directly; otherwise, <c>false</c>.</returns>
        bool TryProcessIndexerElementAccessExpression(
            SemanticModel semantic,
            LayerContext context,
            ElementAccessExpressionSyntax elementAccess,
            List<string> lines) {
            if (semantic == null || elementAccess == null) {
                return false;
            }

            ISymbol symbol = semantic.GetSymbolInfo(elementAccess).Symbol;
            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            if (symbol is not IPropertySymbol propertySymbol || !propertySymbol.IsIndexer) {
                return false;
            }

            if (TryGetExpressionTypeSymbol(semantic, elementAccess.Expression, out ITypeSymbol receiverTypeSymbol) &&
                receiverTypeSymbol?.SpecialType == SpecialType.System_String) {
                return false;
            }

            bool shouldDereferenceElementAccess = ShouldDereferenceElementAccessExpression(semantic, elementAccess.Expression);
            if (shouldDereferenceElementAccess) {
                lines.Add("(*");
            }

            int startClass = context.DepthClass;
            ProcessExpression(semantic, context, elementAccess.Expression, lines);
            List<ConversionClass> saved = context.SavePopClass(startClass);

            if (shouldDereferenceElementAccess) {
                lines.Add(")");
            }

            bool useDirectMemberAccess = shouldDereferenceElementAccess ||
                !IsPointerBackedExpressionType(context, semantic, elementAccess.Expression) &&
                (UsesDirectMemberAccess(semantic, elementAccess.Expression) ||
                 TryResolveTrackedExpressionVariableType(context, elementAccess.Expression, out VariableType trackedReceiverType) &&
                 IsDirectMemberAccessType(trackedReceiverType));
            lines.Add(useDirectMemberAccess ? ".get_Item(" : "->get_Item(");
            bool firstArgument = true;
            for (int argumentIndex = 0; argumentIndex < elementAccess.ArgumentList.Arguments.Count; argumentIndex++) {
                ArgumentSyntax argument = elementAccess.ArgumentList.Arguments[argumentIndex];
                if (!firstArgument) {
                    lines.Add(", ");
                }

                if (TryAppendIndexFromEndElementAccessArgument(
                    semantic,
                    context,
                    elementAccess.Expression,
                    shouldDereferenceElementAccess,
                    useDirectMemberAccess,
                    argument.Expression,
                    lines)) {
                    firstArgument = false;
                    continue;
                }

                List<string> argumentExpressionLines = new List<string>();
                startClass = context.DepthClass;
                ProcessExpression(semantic, context, argument.Expression, argumentExpressionLines);
                context.PopClass(startClass);

                IParameterSymbol parameterSymbol = argumentIndex < propertySymbol.Parameters.Length
                    ? propertySymbol.Parameters[argumentIndex]
                    : null;
                if (TryAppendTargetTypedIntegralArgumentCast(
                    semantic,
                    context,
                    argument.Expression,
                    argumentExpressionLines,
                    parameterSymbol?.Type,
                    lines)) {
                    firstArgument = false;
                    continue;
                }

                lines.AddRange(argumentExpressionLines);
                firstArgument = false;
            }

            lines.Add(")");
            context.LoadClass(saved);
            return true;
        }

        bool TryAppendIndexFromEndElementAccessArgument(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax receiverExpression,
            bool shouldDereferenceElementAccess,
            bool useDirectMemberAccess,
            ExpressionSyntax argumentExpression,
            List<string> lines) {
            if (semantic == null ||
                context == null ||
                receiverExpression == null ||
                argumentExpression is not PrefixUnaryExpressionSyntax indexFromEndExpression ||
                !indexFromEndExpression.IsKind(SyntaxKind.IndexExpression)) {
                return false;
            }

            string receiverText = RenderExpressionText(semantic, context, receiverExpression);
            if (string.IsNullOrWhiteSpace(receiverText)) {
                return false;
            }

            string memberAccessOperator = shouldDereferenceElementAccess
                ? "->"
                : useDirectMemberAccess
                    ? "."
                    : "->";
            lines.Add(receiverText);
            lines.Add(memberAccessOperator);
            lines.Add("get_Length() - ");

            int startClass = context.DepthClass;
            ProcessExpression(semantic, context, indexFromEndExpression.Operand, lines);
            context.PopClass(startClass);
            return true;
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
            if (prefixUnary.IsKind(SyntaxKind.AddressOfExpression) &&
                TryProcessFunctionPointerAddressOfExpression(semantic, context, prefixUnary, lines, out ExpressionResult addressOfResult)) {
                return addressOfResult;
            }

            // Map the operator to the corresponding TypeScript operator
            string operatorSymbol = prefixUnary.OperatorToken.ToString();
            lines.Add(operatorSymbol);

            // Process the operand
            int start = context.DepthClass;
            ExpressionResult result = ProcessExpression(semantic, context, prefixUnary.Operand, lines);
            context.PopClass(start);

            if (prefixUnary.IsKind(SyntaxKind.PointerIndirectionExpression)) {
                VariableType dereferencedType = ResolvePointerIndirectionExpressionType(semantic, prefixUnary);
                if (dereferencedType != null) {
                    result.Type = dereferencedType;
                }
            }

            return result;
        }

        static VariableType ResolvePointerIndirectionExpressionType(
            SemanticModel semantic,
            PrefixUnaryExpressionSyntax prefixUnary) {
            if (semantic == null || prefixUnary == null) {
                return null;
            }

            if (semantic.GetTypeInfo(prefixUnary).ConvertedType is ITypeSymbol convertedTypeSymbol &&
                !IsWeakRecoveredTypeSymbol(convertedTypeSymbol)) {
                return VariableUtil.GetVarType(convertedTypeSymbol);
            }

            if (semantic.GetTypeInfo(prefixUnary).Type is ITypeSymbol typeSymbol &&
                !IsWeakRecoveredTypeSymbol(typeSymbol)) {
                return VariableUtil.GetVarType(typeSymbol);
            }

            if (TryGetExpressionTypeSymbol(semantic, prefixUnary.Operand, out ITypeSymbol operandTypeSymbol) &&
                operandTypeSymbol is IPointerTypeSymbol pointerTypeSymbol) {
                return VariableUtil.GetVarType(pointerTypeSymbol.PointedAtType);
            }

            return null;
        }

        /// <summary>
        /// Lowers address-of method expressions used for unmanaged function pointers into one qualified C++ function address.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the expression.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="prefixUnary">Address-of expression being lowered.</param>
        /// <param name="lines">Output token buffer that receives emitted C++ tokens.</param>
        /// <param name="result">Receives the resolved expression result when lowering succeeds.</param>
        /// <returns><c>true</c> when the address-of expression targets a method symbol and was lowered directly; otherwise <c>false</c>.</returns>
        bool TryProcessFunctionPointerAddressOfExpression(
            SemanticModel semantic,
            LayerContext context,
            PrefixUnaryExpressionSyntax prefixUnary,
            List<string> lines,
            out ExpressionResult result) {
            result = default;

            if (semantic == null ||
                prefixUnary == null ||
                !ReferenceEquals(prefixUnary.SyntaxTree, semantic.SyntaxTree) ||
                !ReferenceEquals(prefixUnary.Operand.SyntaxTree, semantic.SyntaxTree)) {
                return false;
            }

            ISymbol symbol = semantic.GetSymbolInfo(prefixUnary.Operand).Symbol;
            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            if (symbol is not IMethodSymbol methodSymbol) {
                return false;
            }

            ITypeSymbol resultTypeSymbol = semantic.GetTypeInfo(prefixUnary).ConvertedType ?? semantic.GetTypeInfo(prefixUnary).Type;
            VariableType resultType = VariableUtil.GetVarType(resultTypeSymbol);
            lines.Add(RenderQualifiedMethodPointerTarget(methodSymbol, context));
            result = new ExpressionResult(true, VariablePath.Unknown, resultType);
            return true;
        }

        /// <summary>
        /// Renders one fully qualified C++ method pointer target, including emitted generic type arguments for the containing type when needed.
        /// </summary>
        /// <param name="methodSymbol">Method whose address is being taken.</param>
        /// <param name="context">Current lowering context.</param>
        /// <returns>Qualified method pointer target text without the leading ampersand.</returns>
        string RenderQualifiedMethodPointerTarget(IMethodSymbol methodSymbol, LayerContext context) {
            if (methodSymbol?.ContainingType == null) {
                return (methodSymbol?.Name ?? string.Empty) + GetMethodPointerRefModifierSuffix(methodSymbol);
            }

            string containingTypeName = RenderQualifiedContainingTypeName(methodSymbol.ContainingType, context);
            string returnTypeName = GetCppTypeToken(VariableUtil.GetVarType(methodSymbol.ReturnType), context.Program);
            List<string> parameterTypeNames = new List<string>();
            foreach (IParameterSymbol parameterSymbol in methodSymbol.Parameters) {
                string parameterTypeName = GetCppTypeToken(VariableUtil.GetVarType(parameterSymbol.Type), context.Program);
                if (parameterSymbol.RefKind == RefKind.Ref || parameterSymbol.RefKind == RefKind.Out) {
                    parameterTypeName += "&";
                }

                parameterTypeNames.Add(parameterTypeName);
            }

            string emittedTargetName = $"{containingTypeName}::{ResolveConvertedFunctionName(methodSymbol)}{RenderMethodPointerGenericArguments(methodSymbol, context)}";
            string functionPointerSignature = methodSymbol.IsStatic
                ? $"{returnTypeName} (*)({string.Join(", ", parameterTypeNames)})"
                : $"{returnTypeName} ({containingTypeName}::*)({string.Join(", ", parameterTypeNames)})";
            return $"static_cast<{functionPointerSignature}>(&{emittedTargetName})";
        }

        /// <summary>
        /// Renders closed generic method arguments for one method pointer target so native overload resolution binds to the intended template instantiation.
        /// </summary>
        /// <param name="methodSymbol">Method whose generic arguments should be rendered.</param>
        /// <param name="context">Current lowering context.</param>
        /// <returns>Closed generic argument list text, or an empty string when the method is nongeneric.</returns>
        string RenderMethodPointerGenericArguments(IMethodSymbol methodSymbol, LayerContext context) {
            if (methodSymbol == null) {
                return string.Empty;
            }

            List<string> renderedTypeArguments = new List<string>();
            if (methodSymbol.TypeArguments.Length > 0) {
                foreach (ITypeSymbol typeArgument in methodSymbol.TypeArguments) {
                    renderedTypeArguments.Add(RenderMethodPointerTypeArgument(typeArgument, context));
                }
            } else if (methodSymbol.TypeParameters.Length > 0) {
                foreach (ITypeParameterSymbol typeParameter in methodSymbol.TypeParameters) {
                    renderedTypeArguments.Add(typeParameter.Name);
                }
            }

            return renderedTypeArguments.Count == 0
                ? string.Empty
                : $"<{string.Join(", ", renderedTypeArguments)}>";
        }

        static string GetMethodPointerRefModifierSuffix(IMethodSymbol methodSymbol) {
            if (methodSymbol?.Parameters == null || methodSymbol.Parameters.Length == 0) {
                return string.Empty;
            }

            List<string> suffixParts = new List<string>();
            for (int index = 0; index < methodSymbol.Parameters.Length; ++index) {
                IParameterSymbol parameter = methodSymbol.Parameters[index];
                if (parameter.RefKind == RefKind.Ref) {
                    suffixParts.Add($"ref{index}");
                    continue;
                }

                if (parameter.RefKind == RefKind.Out) {
                    suffixParts.Add($"out{index}");
                }
            }

            return suffixParts.Count == 0
                ? string.Empty
                : "__" + string.Join("_", suffixParts);
        }

        /// <summary>
        /// Renders the emitted containing type name for one method pointer target, preserving generic type arguments for templated containers.
        /// </summary>
        /// <param name="containingTypeSymbol">Containing type whose emitted name is needed.</param>
        /// <param name="context">Current lowering context.</param>
        /// <returns>Containing type text suitable for a qualified method pointer target.</returns>
        string RenderQualifiedContainingTypeName(INamedTypeSymbol containingTypeSymbol, LayerContext context) {
            ConversionClass generatedClass = context.Program.FindGeneratedClass(containingTypeSymbol);
            string emittedTypeName = generatedClass?.GetEmittedTypeName() ?? containingTypeSymbol.Name;

            List<string> renderedTypeArguments = new List<string>();
            if (containingTypeSymbol.TypeArguments.Length > 0) {
                foreach (ITypeSymbol typeArgument in containingTypeSymbol.TypeArguments) {
                    renderedTypeArguments.Add(RenderMethodPointerTypeArgument(typeArgument, context));
                }
            } else if (containingTypeSymbol.TypeParameters.Length > 0) {
                foreach (ITypeParameterSymbol typeParameter in containingTypeSymbol.TypeParameters) {
                    renderedTypeArguments.Add(typeParameter.Name);
                }
            }

            if (renderedTypeArguments.Count == 0) {
                return emittedTypeName;
            }

            return $"{emittedTypeName}<{string.Join(", ", renderedTypeArguments)}>";
        }

        /// <summary>
        /// Renders one containing-type generic argument for a qualified method pointer target using the same C++ type lowering rules as ordinary signatures.
        /// </summary>
        /// <param name="typeArgument">Generic argument type to render.</param>
        /// <param name="context">Current lowering context.</param>
        /// <returns>Rendered C++ type argument text.</returns>
        string RenderMethodPointerTypeArgument(ITypeSymbol typeArgument, LayerContext context) {
            VariableType sourceType = VariableUtil.GetVarType(typeArgument);
            CPPTypeData typeData;
            VariableType cppType = ConvertToCPPType(sourceType, out typeData);
            string renderedType = cppType.ToCPPString(context.Program);

            if (typeData.IsPointer) {
                return $"{renderedType}*";
            }

            return renderedType;
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
            string castTargetTypeName = cppType.ToCPPString(context.Program) + (typeData.IsPointer ? "*" : string.Empty);
            ITypeSymbol sourceExpressionTypeSymbol = semantic.GetTypeInfo(castExpr.Expression).Type ?? semantic.GetTypeInfo(castExpr.Expression).ConvertedType;
            bool sourceExpressionTypeKnown = sourceExpressionTypeSymbol != null;
            VariableType sourceType = sourceExpressionTypeKnown
                ? VariableUtil.GetVarType(sourceExpressionTypeSymbol)
                : new VariableType(VariableDataType.Object, "object");
            CPPTypeData sourceTypeData;
            VariableType cppSourceType = ConvertToCPPType(sourceType, out sourceTypeData);
            bool sourceIsPointerLike = sourceTypeData.IsPointer;
            bool targetIsPointerLike = typeData.IsPointer;
            bool sourceIsNativePointerInteger = string.Equals(cppSourceType.TypeName, "intptr_t", StringComparison.Ordinal) ||
                string.Equals(cppSourceType.TypeName, "uintptr_t", StringComparison.Ordinal);
            bool targetIsNativePointerInteger = string.Equals(cppType.TypeName, "intptr_t", StringComparison.Ordinal) ||
                string.Equals(cppType.TypeName, "uintptr_t", StringComparison.Ordinal);

            if (!typeData.IsPointer &&
                TryGetExpressionTypeSymbol(semantic, castExpr.Expression, out ITypeSymbol sourceTypeSymbol) &&
                sourceTypeSymbol.SpecialType == SpecialType.System_Object) {
                lines.Add("(*static_cast<");
                lines.Add(castTargetTypeName);
                lines.Add("*>(");
                ProcessExpression(semantic, context, castExpr.Expression, lines);
                lines.Add("))");
                return new ExpressionResult(true, VariablePath.Unknown, varType);
            }

            if (sourceExpressionTypeKnown &&
                sourceIsPointerLike &&
                !targetIsPointerLike &&
                !targetIsNativePointerInteger &&
                IsIntegerLikeCppTypeName(cppType.TypeName)) {
                lines.Add("static_cast<");
                lines.Add(castTargetTypeName);
                lines.Add(">(reinterpret_cast<");
                lines.Add(IsUnsignedIntegerLikeCppTypeName(cppType.TypeName) ? "uintptr_t" : "intptr_t");
                lines.Add(">(");
                ProcessExpression(semantic, context, castExpr.Expression, lines);
                lines.Add("))");
                return new ExpressionResult(true, VariablePath.Unknown, varType);
            }

            bool requiresReinterpretCast = sourceExpressionTypeKnown &&
                ((targetIsPointerLike && (sourceIsNativePointerInteger || IsIntegerLikeCppTypeName(cppSourceType.TypeName))) ||
                (targetIsNativePointerInteger && sourceIsPointerLike) ||
                (targetIsPointerLike && sourceIsPointerLike));
            lines.Add(requiresReinterpretCast ? "reinterpret_cast<" : "static_cast<");
            lines.Add(castTargetTypeName);
            lines.Add(">(");
            ProcessExpression(semantic, context, castExpr.Expression, lines);
            lines.Add(")");

            return new ExpressionResult(true, VariablePath.Unknown, varType);
        }

        static bool IsIntegerLikeCppTypeName(string cppTypeName) {
            return string.Equals(cppTypeName, "int8_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "uint8_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "int16_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "uint16_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "int32_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "uint32_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "int64_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "uint64_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "intptr_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "uintptr_t", StringComparison.Ordinal);
        }

        static bool IsUnsignedIntegerLikeCppTypeName(string cppTypeName) {
            return string.Equals(cppTypeName, "uint8_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "uint16_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "uint32_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "uint64_t", StringComparison.Ordinal) ||
                string.Equals(cppTypeName, "uintptr_t", StringComparison.Ordinal);
        }

        protected override void ProcessConditionalExpression(SemanticModel semantic, LayerContext context, ConditionalExpressionSyntax conditional, List<string> lines) {
            if (TryProcessNullableConditionalExpression(semantic, context, conditional, lines)) {
                return;
            }

            // Process the condition (before the ?)
            int conditionStart = context.DepthClass;
            ProcessExpression(semantic, context, conditional.Condition, lines);
            context.PopClass(conditionStart);
            lines.Add(" ? ");

            // Process the true branch (after the ? and before the :)
            int whenTrueStart = context.DepthClass;
            ProcessExpression(semantic, context, UnwrapRefExpression(conditional.WhenTrue), lines);
            context.PopClass(whenTrueStart);
            lines.Add(" : ");

            // Process the false branch (after the :)
            int whenFalseStart = context.DepthClass;
            ProcessExpression(semantic, context, UnwrapRefExpression(conditional.WhenFalse), lines);
            context.PopClass(whenFalseStart);
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

            int conditionStart = context.DepthClass;
            ProcessExpression(semantic, context, conditional.Condition, lines);
            context.PopClass(conditionStart);
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
            int branchStart = context.DepthClass;
            ProcessExpression(semantic, context, UnwrapRefExpression(branchExpression), lines);
            context.PopClass(branchStart);
            lines.Add(")");
        }

        static ExpressionSyntax UnwrapRefExpression(ExpressionSyntax expression) {
            return expression is RefExpressionSyntax refExpression
                ? refExpression.Expression
                : expression;
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
            AppendNativeLambda(
                semantic,
                context,
                lambda,
                lambda.ParameterList.Parameters,
                lambda.Body,
                lines);
        }

        void AppendNativeLambda(
            SemanticModel semantic,
            LayerContext context,
            LambdaExpressionSyntax lambdaExpression,
            IReadOnlyList<ParameterSyntax> parameters,
            CSharpSyntaxNode lambdaBody,
            List<string> lines) {
            lines.Add("[&](");
            AppendLambdaParameters(semantic, context, parameters, lines);
            lines.Add(")");
            AppendNativeLambdaBody(semantic, context, lambdaExpression, lambdaBody, lines);
        }

        void AppendLambdaParameters(
            SemanticModel semantic,
            LayerContext context,
            IReadOnlyList<ParameterSyntax> parameters,
            List<string> lines) {
            for (int index = 0; index < parameters.Count; index++) {
                ParameterSyntax parameter = parameters[index];
                if (TryGetLambdaParameterTypeName(semantic, context, parameter, out string parameterTypeName)) {
                    lines.Add(parameterTypeName);
                    lines.Add(" ");
                }

                lines.Add(parameter.Identifier.Text);
                if (index < parameters.Count - 1) {
                    lines.Add(", ");
                }
            }
        }

        bool TryGetLambdaParameterTypeName(
            SemanticModel semantic,
            LayerContext context,
            ParameterSyntax parameter,
            out string parameterTypeName) {
            parameterTypeName = string.Empty;
            if (parameter == null) {
                return false;
            }

            IParameterSymbol parameterSymbol = semantic?.GetDeclaredSymbol(parameter) as IParameterSymbol;
            ITypeSymbol parameterTypeSymbol = parameterSymbol?.Type;
            if (parameterTypeSymbol == null && parameter.Type != null) {
                parameterTypeSymbol = semantic?.GetTypeInfo(parameter.Type).Type ?? semantic?.GetTypeInfo(parameter.Type).ConvertedType;
            }

            if (parameterTypeSymbol == null) {
                return false;
            }

            VariableType parameterType = VariableUtil.GetVarType(parameterTypeSymbol);
            RegisterGeneratedTypeReferences(context, parameterType);
            parameterTypeName = GetCppTypeToken(parameterType, context.Program);
            if (parameterSymbol?.RefKind == RefKind.Ref || parameterSymbol?.RefKind == RefKind.Out) {
                parameterTypeName += "&";
            }

            return true;
        }

        void AppendNativeLambdaBody(
            SemanticModel semantic,
            LayerContext context,
            LambdaExpressionSyntax lambdaExpression,
            CSharpSyntaxNode lambdaBody,
            List<string> lines) {
            lines.Add(" {\n");
            if (lambdaBody is BlockSyntax block) {
                ProcessBlock(semantic, context, block, lines);
                lines.Add("}");
                return;
            }

            if (lambdaBody is ExpressionSyntax expressionSyntax) {
                bool returnsVoid = DoesLambdaReturnVoid(semantic, lambdaExpression);
                if (!returnsVoid) {
                    lines.Add("return ");
                }

                int expressionStart = context.DepthClass;
                ProcessExpression(semantic, context, expressionSyntax, lines);
                context.PopClass(expressionStart);
                lines.Add(";\n");
            }

            lines.Add("}");
        }

        static bool DoesLambdaReturnVoid(SemanticModel semantic, LambdaExpressionSyntax lambdaExpression) {
            if (semantic == null || lambdaExpression == null) {
                return false;
            }

            ITypeSymbol lambdaTypeSymbol = semantic.GetTypeInfo(lambdaExpression).ConvertedType ?? semantic.GetTypeInfo(lambdaExpression).Type;
            if (lambdaTypeSymbol is not INamedTypeSymbol delegateTypeSymbol) {
                return false;
            }

            IMethodSymbol invokeMethod = delegateTypeSymbol.DelegateInvokeMethod;
            return invokeMethod?.ReturnsVoid == true;
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
                string usingResourceName = CreateTemporaryName("__usingResource");
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

            AppendDisposalGuard(lines, disposalTarget, disposalUsesPointerAccess, "__usingDisposeGuard");
            ProcessStatement(semantic, context, usingStatement.Statement, lines);
            lines.Add("}\n");
        }

        void AppendDisposalGuard(
            List<string> lines,
            string disposalTarget,
            bool disposalUsesPointerAccess,
            string guardPrefix) {
            if (string.IsNullOrWhiteSpace(disposalTarget)) {
                return;
            }

            string guardName = CreateTemporaryName(guardPrefix);
            RegisterRuntimeRequirement("NativeFinally");
            lines.Add($"auto {guardName} = he_cpp_make_scope_exit([&]() {{\n");
            AppendDisposalCleanupBody(lines, disposalTarget, disposalUsesPointerAccess);
            lines.Add("});\n");
        }

        void AppendDisposalCleanupBody(
            List<string> lines,
            string disposalTarget,
            bool disposalUsesPointerAccess) {
            if (disposalUsesPointerAccess) {
                lines.Add($"if ({disposalTarget} != nullptr) {{\n");
                lines.Add($"{disposalTarget}->Dispose();\n");
                lines.Add($"delete {disposalTarget};\n");
                lines.Add("}\n");
                return;
            }

            lines.Add($"{disposalTarget}.Dispose();\n");
        }

        bool IsPointerDeclaration(
            SemanticModel semantic,
            VariableDeclarationSyntax declaration) {
            VariableType declarationType = ResolveDeclarationType(semantic, declaration);
            if (declarationType == null) {
                return false;
            }

            VariableType cppType = ConvertToCPPType(declarationType, out CPPTypeData typeData);
            return cppType != null && typeData.IsPointer;
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

                string guardName = CreateTemporaryName("__finallyGuard");
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
            List<string> conditionLines = new List<string>();
            int start = context.DepthClass;
            ExpressionResult conditionResult = ProcessExpression(semantic, context, whileStatement.Condition, conditionLines);
            context.PopClass(start);

            if (conditionResult.BeforeLines != null && conditionResult.BeforeLines.Count > 0) {
                lines.AddRange(conditionResult.BeforeLines);
            }

            lines.Add("while (");
            lines.AddRange(conditionLines);
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
                for (int initializerIndex = 0; initializerIndex < forStatement.Initializers.Count; initializerIndex++) {
                    ExpressionSyntax initializer = forStatement.Initializers[initializerIndex];
                    ProcessExpression(semantic, context, initializer, lines);
                    if (initializerIndex < forStatement.Initializers.Count - 1) {
                        lines.Add(", ");
                    }
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
            for (int incrementorIndex = 0; incrementorIndex < forStatement.Incrementors.Count; incrementorIndex++) {
                ExpressionSyntax incrementor = forStatement.Incrementors[incrementorIndex];
                int startClass = context.DepthClass;
                ProcessExpression(semantic, context, incrementor, lines);
                context.PopClass(startClass);
                if (incrementorIndex < forStatement.Incrementors.Count - 1) {
                    lines.Add(", ");
                }
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
                if (ifStatement.Else.Statement is IfStatementSyntax elseIfStatement) {
                    lines.Add("else {\n");
                    ProcessIfStatement(semantic, context, elseIfStatement, lines);
                    lines.Add("}\n");
                } else {
                    lines.Add("else ");
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
                lines.Add("throw;\n");
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

            if (parsedType != null && parsedType.IsPointer) {
                VariableType pointedSourceType = new VariableType(parsedType);
                pointedSourceType.IsPointer = false;

                CPPTypeData pointedTypeData;
                VariableType pointedCppType = ConvertToCPPType(pointedSourceType, out pointedTypeData);
                if (pointedTypeData.IsPointer) {
                    pointedCppType = new VariableType(
                        VariableDataType.Unknown,
                        $"{pointedCppType.ToCPPString(null)}*");
                }

                typeData.IsArray = false;
                typeData.IsNativeType = pointedTypeData.IsNativeType;
                typeData.IsPointer = true;
                return pointedCppType;
            }

            if (parsedType != null && parsedType.IsGenericParameter) {
                typeData.IsArray = false;
                typeData.IsNativeType = false;
                typeData.IsPointer = false;
                return parsedType;
            }

            if (parsedType != null &&
                (parsedType.Type == VariableDataType.Tuple ||
                 string.Equals(parsedType.TypeName, "ValueTuple", StringComparison.Ordinal))) {
                codeConverter?.RegisterRuntimeRequirement("NativeTuple");
                typeData.IsArray = false;
                typeData.IsNativeType = false;
                typeData.IsPointer = false;
                return CreateConvertedGenericType(parsedType, "ValueTuple");
            }

            if (TryResolveConfiguredTypeRemap(parsedType, out VariableType remappedType)) {
                return ConvertToCPPType(remappedType, out typeData);
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
                case VariableDataType.Void: {
                        typeData.IsArray = false;
                        typeData.IsNativeType = true;
                        typeData.IsPointer = false;
                        return new VariableType(parsedType.Type, "void");
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
                case VariableDataType.Callback: {
                        if (string.Equals(parsedType.TypeName, "FunctionPointer", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeFunctionPointer");
                            typeData.IsArray = false;
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return CreateConvertedGenericType(parsedType, "FunctionPointer");
                        }

                        typeData.IsArray = false;
                        typeData.IsNativeType = false;
                        typeData.IsPointer = true;
                        return parsedType;
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

                        if (string.Equals(parsedType.TypeName, "SpinWait", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Threading.SpinWait", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("SpinWait");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "SpinWait");
                        }

                        if (string.Equals(parsedType.TypeName, "SpinLock", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Threading.SpinLock", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("SpinLock");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "SpinLock");
                        }

                        if (string.Equals(parsedType.TypeName, "NotImplementedException", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.NotImplementedException", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NotImplementedException");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = true;
                            return new VariableType(parsedType.Type, "NotImplementedException");
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

                        if (string.Equals(parsedType.TypeName, "BitOperations", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Numerics.BitOperations", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("BitOperations");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "BitOperations");
                        }

                        if (string.Equals(parsedType.TypeName, "MathF", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.MathF", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("Math");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "MathF");
                        }

                        if (string.Equals(parsedType.TypeName, "Span", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Span", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeSpan");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return CreateConvertedGenericType(parsedType, "Span");
                        }

                        if (string.Equals(parsedType.TypeName, "ReadOnlySpan", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.ReadOnlySpan", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeSpan");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return CreateConvertedGenericType(parsedType, "ReadOnlySpan");
                        }

                        if (string.Equals(parsedType.TypeName, "KeyValuePair", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Collections.Generic.KeyValuePair", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeKeyValuePair");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return CreateConvertedGenericType(parsedType, "KeyValuePair");
                        }

                        if (string.Equals(parsedType.TypeName, "Vector", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Numerics.Vector", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeVector");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;

                            if (parsedType.GenericArgs.Count > 0) {
                                return CreateConvertedGenericType(parsedType, "Vector_1");
                            }

                            return new VariableType(parsedType.Type, "Vector");
                        }

                        if (string.Equals(parsedType.TypeName, "Vector128", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Runtime.Intrinsics.Vector128", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeVector128");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;

                            if (parsedType.GenericArgs.Count > 0) {
                                return CreateConvertedGenericType(parsedType, "Vector128_1");
                            }

                            return new VariableType(parsedType.Type, "Vector128");
                        }

                        if (string.Equals(parsedType.TypeName, "Vector256", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Runtime.Intrinsics.Vector256", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeVector256");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;

                            if (parsedType.GenericArgs.Count > 0) {
                                return CreateConvertedGenericType(parsedType, "Vector256");
                            }

                            return new VariableType(parsedType.Type, "Vector256Runtime");
                        }

                        if (string.Equals(parsedType.TypeName, "Vector512", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Runtime.Intrinsics.Vector512", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeVector512");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;

                            if (parsedType.GenericArgs.Count > 0) {
                                return CreateConvertedGenericType(parsedType, "Vector512");
                            }

                            return new VariableType(parsedType.Type, "Vector512Runtime");
                        }

                        if (string.Equals(parsedType.TypeName, "Sse", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Runtime.Intrinsics.X86.Sse", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("Sse");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "Sse");
                        }

                        if (string.Equals(parsedType.TypeName, "Avx", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Runtime.Intrinsics.X86.Avx", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("Avx");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "Avx");
                        }

                        if (string.Equals(parsedType.TypeName, "Avx2", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Runtime.Intrinsics.X86.Avx2", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("Avx2");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "Avx2");
                        }

                        if (string.Equals(parsedType.TypeName, "Sse41", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.Runtime.Intrinsics.X86.Sse41", StringComparison.Ordinal)) {
                            codeConverter?.RegisterRuntimeRequirement("Sse41");
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "Sse41");
                        }

                        if (string.Equals(parsedType.TypeName, "nint", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "IntPtr", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "System.IntPtr", StringComparison.Ordinal)) {
                            typeData.IsNativeType = true;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType.Type, "intptr_t");
                        }

                        if (string.Equals(parsedType.TypeName, "nuint", StringComparison.Ordinal) ||
                            string.Equals(parsedType.TypeName, "UIntPtr", StringComparison.Ordinal) ||
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

                        if (parsedType.IsEnum || parsedType.IsValueType) {
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            return new VariableType(parsedType);
                        }

                        if (IsNativeExceptionTypeName(parsedType.TypeName)) {
                            codeConverter?.RegisterRuntimeRequirement("NativeExceptions");
                        }

                        ConversionClass generatedClass = ResolveGeneratedClass(parsedType);
                        if (generatedClass?.TypeSymbol?.IsValueType == true) {
                            typeData.IsNativeType = false;
                            typeData.IsPointer = false;
                            VariableType generatedValueType = new VariableType(parsedType);
                            generatedValueType.TypeName = generatedClass.GetEmittedTypeName();
                            return generatedValueType;
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

        /// <summary>
        /// Resolves one caller-configured source type remap before the backend applies its built-in native lowering rules.
        /// </summary>
        /// <param name="parsedType">Source type being lowered.</param>
        /// <param name="remappedType">Resolved remapped target type when present.</param>
        /// <returns><c>true</c> when a configured remap was found; otherwise <c>false</c>.</returns>
        bool TryResolveConfiguredTypeRemap(VariableType parsedType, out VariableType remappedType) {
            remappedType = null;
            if (parsedType == null || codeConverter?.Program?.TypeMap == null || codeConverter.Program.TypeMap.Count == 0) {
                return false;
            }

            string remappedTypeName = string.Empty;
            if (!string.IsNullOrWhiteSpace(parsedType.QualifiedTypeName) &&
                codeConverter.Program.TypeMap.TryGetValue(parsedType.QualifiedTypeName, out remappedTypeName)) {
                if (string.Equals(remappedTypeName, parsedType.QualifiedTypeName, StringComparison.Ordinal)) {
                    return false;
                }

                remappedType = VariableUtil.GetVarType(remappedTypeName);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(parsedType.TypeName) &&
                codeConverter.Program.TypeMap.TryGetValue(parsedType.TypeName, out remappedTypeName)) {
                if (string.Equals(remappedTypeName, parsedType.TypeName, StringComparison.Ordinal)) {
                    return false;
                }

                remappedType = VariableUtil.GetVarType(remappedTypeName);
                return true;
            }

            return false;
        }

        bool TryResolveGeneratedFunction(IMethodSymbol methodSymbol, out ConversionFunction function) {
            function = null;
            if (methodSymbol == null) {
                return false;
            }

            methodSymbol = methodSymbol.OriginalDefinition ?? methodSymbol;

            if (!TryResolveGeneratedContainingClass(methodSymbol, out ConversionClass generatedClass)) {
                return false;
            }

            if (generatedClass?.Functions == null) {
                return false;
            }

            string sourceMethodKey = BuildSourceMethodKey(methodSymbol);
            if (!string.IsNullOrWhiteSpace(sourceMethodKey)) {
                function = generatedClass.Functions.FirstOrDefault(candidate =>
                    string.Equals(candidate.SourceMethodKey, sourceMethodKey, StringComparison.Ordinal));
                if (function != null) {
                    return true;
                }
            }

            function = generatedClass.Functions.FirstOrDefault(candidate => MatchesMethodSymbol(candidate, methodSymbol));
            return function != null;
        }

        bool TryResolveGeneratedContainingClass(IMethodSymbol methodSymbol, out ConversionClass generatedClass) {
            generatedClass = null;
            if (methodSymbol?.ContainingType == null || codeConverter?.Program == null) {
                return false;
            }

            generatedClass = codeConverter.Program.FindGeneratedClass(methodSymbol.ContainingType);
            if (generatedClass != null) {
                return true;
            }

            VariableType containingType = VariableUtil.GetVarType(methodSymbol.ContainingType);
            generatedClass = ResolveGeneratedClass(containingType);
            return generatedClass != null;
        }

        static bool MatchesMethodSymbol(ConversionFunction function, IMethodSymbol methodSymbol) {
            if (function == null || methodSymbol == null) {
                return false;
            }

            if (!string.Equals(function.Name, methodSymbol.Name, StringComparison.Ordinal)) {
                return false;
            }

            int functionGenericParameterCount = function.GenericParameters?.Count ?? 0;
            if (functionGenericParameterCount != methodSymbol.TypeParameters.Length) {
                return false;
            }

            int functionParameterCount = function.InParameters?.Count ?? 0;
            if (functionParameterCount != methodSymbol.Parameters.Length) {
                return false;
            }

            for (int index = 0; index < functionParameterCount; index++) {
                ConversionVariable parameter = function.InParameters[index];
                IParameterSymbol parameterSymbol = methodSymbol.Parameters[index];
                if (!MatchesParameterModifier(parameter, parameterSymbol)) {
                    return false;
                }

                if (!MatchesParameterType(parameter.VarType, VariableUtil.GetVarType(parameterSymbol.Type))) {
                    return false;
                }
            }

            return true;
        }

        static bool MatchesParameterModifier(ConversionVariable parameter, IParameterSymbol parameterSymbol) {
            bool isRef = (parameter.Modifier & ParameterModifier.Ref) != 0;
            bool isOut = (parameter.Modifier & ParameterModifier.Out) != 0;
            return parameterSymbol.RefKind switch {
                RefKind.Ref => isRef && !isOut,
                RefKind.Out => isOut,
                _ => !isRef && !isOut
            };
        }

        static bool MatchesParameterType(VariableType leftType, VariableType rightType) {
            if (leftType == null || rightType == null) {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(leftType.QualifiedTypeName) &&
                !string.IsNullOrWhiteSpace(rightType.QualifiedTypeName)) {
                return string.Equals(leftType.QualifiedTypeName, rightType.QualifiedTypeName, StringComparison.Ordinal);
            }

            return string.Equals(leftType.ToString(), rightType.ToString(), StringComparison.Ordinal);
        }

        ConversionClass ResolveGeneratedClass(VariableType parsedType) {
            if (parsedType == null || codeConverter?.Program == null || string.IsNullOrWhiteSpace(parsedType.TypeName)) {
                return null;
            }

            ConversionClass qualifiedMatch = codeConverter.Program.FindGeneratedClass(parsedType);
            if (qualifiedMatch != null) {
                return qualifiedMatch;
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

            if (expression is ParenthesizedExpressionSyntax parenthesizedExpression) {
                return UsesDirectMemberAccess(semantic, parenthesizedExpression.Expression);
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

        bool IsPointerBackedExpressionType(
            LayerContext context,
            SemanticModel semantic,
            ExpressionSyntax expression,
            VariableType explicitType = null) {
            VariableType receiverType = explicitType;
            if (receiverType == null &&
                context != null &&
                TryResolveTrackedExpressionVariableType(context, expression, out VariableType trackedReceiverType)) {
                receiverType = trackedReceiverType;
            }

            if (receiverType == null &&
                TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol receiverTypeSymbol)) {
                receiverType = VariableUtil.GetVarType(receiverTypeSymbol);
            }

            if (receiverType == null) {
                return false;
            }

            ConvertToCPPType(receiverType, out CPPTypeData typeData);
            return typeData.IsPointer;
        }

        string TryResolveUnqualifiedStaticContainingTypeAccessName(
            LayerContext context,
            ConversionClass currentClass,
            ISymbol symbol,
            ConversionVariable classVar,
            ConversionVariable functionInVar,
            int matchingVariableCount,
            bool isQualifiedMemberName) {
            if (context == null ||
                currentClass == null ||
                symbol == null ||
                classVar != null ||
                functionInVar != null ||
                matchingVariableCount != 0 ||
                isQualifiedMemberName) {
                return string.Empty;
            }

            if (symbol is IAliasSymbol aliasSymbol) {
                symbol = aliasSymbol.Target;
            }

            INamedTypeSymbol containingTypeSymbol = null;
            bool isStaticMember = false;
            if (symbol is IFieldSymbol fieldSymbol) {
                containingTypeSymbol = fieldSymbol.ContainingType;
                isStaticMember = fieldSymbol.IsStatic;
            } else if (symbol is IPropertySymbol propertySymbol) {
                containingTypeSymbol = propertySymbol.ContainingType;
                isStaticMember = propertySymbol.IsStatic;
            } else if (symbol is IMethodSymbol methodSymbol) {
                containingTypeSymbol = methodSymbol.ContainingType;
                isStaticMember = methodSymbol.IsStatic;
            }

            if (!isStaticMember ||
                containingTypeSymbol == null ||
                string.Equals(currentClass.Name, containingTypeSymbol.Name, StringComparison.Ordinal)) {
                return string.Empty;
            }

            return GetContainingTypeAccessName(context, containingTypeSymbol);
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
                namedTypeSymbol.IsGenericType) {
                return false;
            }

            if (symbol is INamedTypeSymbol mappedTypeSymbol &&
                TryMapStaticRuntimeTypeName(mappedTypeSymbol.Name, mappedTypeSymbol.ToDisplayString(), out runtimeTypeName, out runtimeRequirementName)) {
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

            if (string.Equals(shortTypeName, "Guid", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Guid", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Guid", StringComparison.Ordinal)) {
                runtimeTypeName = "Guid";
                runtimeRequirementName = "Guid";
                return true;
            }

            if (string.Equals(shortTypeName, "MathF", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.MathF", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.MathF", StringComparison.Ordinal)) {
                runtimeTypeName = "MathF";
                runtimeRequirementName = "Math";
                return true;
            }

            if (string.Equals(shortTypeName, "NativeMemory", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Runtime.InteropServices.NativeMemory", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Runtime.InteropServices.NativeMemory", StringComparison.Ordinal)) {
                runtimeTypeName = "NativeMemory";
                runtimeRequirementName = "NativeMemory";
                return true;
            }

            if (string.Equals(shortTypeName, "MemoryMarshal", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Runtime.InteropServices.MemoryMarshal", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Runtime.InteropServices.MemoryMarshal", StringComparison.Ordinal)) {
                runtimeTypeName = "MemoryMarshal";
                runtimeRequirementName = "MemoryMarshal";
                return true;
            }

            if (string.Equals(shortTypeName, "Vector512", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Runtime.Intrinsics.Vector512", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Runtime.Intrinsics.Vector512", StringComparison.Ordinal)) {
                runtimeTypeName = "Vector512Runtime";
                runtimeRequirementName = "NativeVector512";
                return true;
            }

            if (string.Equals(shortTypeName, "Debug", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Diagnostics.Debug", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Diagnostics.Debug", StringComparison.Ordinal)) {
                runtimeTypeName = "System::Diagnostics::Debug";
                runtimeRequirementName = "Debug";
                return true;
            }

            if (string.Equals(shortTypeName, "Stopwatch", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Diagnostics.Stopwatch", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Diagnostics.Stopwatch", StringComparison.Ordinal)) {
                runtimeTypeName = "System::Diagnostics::Stopwatch";
                runtimeRequirementName = "Stopwatch";
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

            if (string.Equals(shortTypeName, "Interlocked", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Threading.Interlocked", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Threading.Interlocked", StringComparison.Ordinal)) {
                runtimeTypeName = "Interlocked";
                runtimeRequirementName = "Interlocked";
                return true;
            }

            if (string.Equals(shortTypeName, "Volatile", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Threading.Volatile", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Threading.Volatile", StringComparison.Ordinal)) {
                runtimeTypeName = "Volatile";
                runtimeRequirementName = "Volatile";
                return true;
            }

            if (string.Equals(qualifiedTypeName, "System.Buffer", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Buffer", StringComparison.Ordinal)) {
                runtimeTypeName = "Buffer";
                runtimeRequirementName = "Buffer";
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

            if (string.Equals(shortTypeName, "BitOperations", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.Numerics.BitOperations", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "global::System.Numerics.BitOperations", StringComparison.Ordinal)) {
                runtimeTypeName = "BitOperations";
                runtimeRequirementName = "BitOperations";
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

            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return false;
            }

            if (TryGetFrameworkThreadDelegateWrapperTypeName(namedTypeSymbol, context, out delegateWrapperTypeName)) {
                return true;
            }

            if (namedTypeSymbol.TypeKind == TypeKind.Delegate &&
                !IsActionTypeSymbol(namedTypeSymbol) &&
                !IsFuncTypeSymbol(namedTypeSymbol)) {
                delegateWrapperTypeName = GetCustomDelegateWrapperTypeName(namedTypeSymbol, context);
                return !string.IsNullOrWhiteSpace(delegateWrapperTypeName);
            }

            if (!namedTypeSymbol.IsGenericType) {
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

        static string GetCustomDelegateWrapperTypeName(INamedTypeSymbol delegateTypeSymbol, LayerContext context) {
            if (delegateTypeSymbol == null || context?.Program == null) {
                return string.Empty;
            }

            foreach (ConversionClass generatedClass in context.Program.Classes) {
                if (generatedClass?.TypeSymbol != null &&
                    SymbolEqualityComparer.Default.Equals(generatedClass.TypeSymbol, delegateTypeSymbol)) {
                    return generatedClass.GetEmittedTypeName();
                }
            }

            return delegateTypeSymbol.Name;
        }

        bool TryGetFrameworkThreadDelegateWrapperTypeName(
            INamedTypeSymbol delegateTypeSymbol,
            LayerContext context,
            out string delegateWrapperTypeName) {
            delegateWrapperTypeName = string.Empty;
            string displayName = delegateTypeSymbol.ToDisplayString();
            bool isThreadStart =
                string.Equals(displayName, "System.Threading.ThreadStart", StringComparison.Ordinal) ||
                string.Equals(displayName, "ThreadStart", StringComparison.Ordinal);
            bool isParameterizedThreadStart =
                string.Equals(displayName, "System.Threading.ParameterizedThreadStart", StringComparison.Ordinal) ||
                string.Equals(displayName, "ParameterizedThreadStart", StringComparison.Ordinal);
            if (!isThreadStart && !isParameterizedThreadStart) {
                return false;
            }

            IMethodSymbol invokeMethod = delegateTypeSymbol.DelegateInvokeMethod;
            if (invokeMethod == null || !invokeMethod.ReturnsVoid) {
                return false;
            }

            if (invokeMethod.Parameters.Length == 0) {
                delegateWrapperTypeName = "Action<>";
                return true;
            }

            List<string> argumentTypes = new List<string>();
            foreach (IParameterSymbol parameterSymbol in invokeMethod.Parameters) {
                argumentTypes.Add(GetCppTypeToken(VariableUtil.GetVarType(parameterSymbol.Type), context.Program));
            }

            delegateWrapperTypeName = $"Action<{string.Join(", ", argumentTypes)}>";
            return true;
        }

        bool TryAppendDelegateWrapperConstruction(
            SemanticModel semantic,
            LayerContext context,
            ITypeSymbol delegateTypeSymbol,
            IMethodSymbol methodGroupSymbol,
            ExpressionSyntax methodGroupExpression,
            List<string> lines) {
            if (!TryGetDelegateWrapperTypeName(delegateTypeSymbol, methodGroupSymbol, context, out string delegateWrapperTypeName)) {
                return false;
            }

            List<string> delegateConstructionLines = new List<string> {
                $"new {delegateWrapperTypeName}("
            };
            if (methodGroupSymbol.IsStatic) {
                delegateConstructionLines.Add(RenderQualifiedMethodPointerTarget(methodGroupSymbol, context));
            } else if (TryResolveBoundDelegateReceiverText(semantic, context, methodGroupExpression, methodGroupSymbol, out string receiverText)) {
                delegateConstructionLines.Add("std::bind_front(");
                delegateConstructionLines.Add(RenderQualifiedMethodPointerTarget(methodGroupSymbol, context));
                delegateConstructionLines.Add(", ");
                delegateConstructionLines.Add(receiverText);
                delegateConstructionLines.Add(")");
            } else {
                return false;
            }

            delegateConstructionLines.Add(")");
            lines.AddRange(delegateConstructionLines);
            return true;
        }

        bool TryResolveBoundDelegateReceiverText(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax methodGroupExpression,
            IMethodSymbol methodGroupSymbol,
            out string receiverText) {
            receiverText = string.Empty;
            if (semantic == null ||
                context == null ||
                methodGroupExpression == null ||
                methodGroupSymbol == null ||
                methodGroupSymbol.IsStatic) {
                return false;
            }

            if (methodGroupExpression is IdentifierNameSyntax) {
                receiverText = "this";
                return true;
            }

            if (methodGroupExpression is not MemberAccessExpressionSyntax memberAccess) {
                return false;
            }

            receiverText = RenderExpressionText(semantic, context, memberAccess.Expression);
            return !string.IsNullOrWhiteSpace(receiverText);
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

            if (semantic == null || !ReferenceEquals(expression.SyntaxTree, semantic.SyntaxTree)) {
                string fallbackExpressionText = expression.ToString();
                return string.Equals(fallbackExpressionText, "string", StringComparison.Ordinal) ||
                    string.Equals(fallbackExpressionText, "char", StringComparison.Ordinal) ||
                    string.Equals(fallbackExpressionText, "String", StringComparison.Ordinal) ||
                    string.Equals(fallbackExpressionText, "Char", StringComparison.Ordinal) ||
                    string.Equals(fallbackExpressionText, "System.String", StringComparison.Ordinal) ||
                    string.Equals(fallbackExpressionText, "System.Char", StringComparison.Ordinal) ||
                    string.Equals(fallbackExpressionText, "global::System.String", StringComparison.Ordinal) ||
                    string.Equals(fallbackExpressionText, "global::System.Char", StringComparison.Ordinal);
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

        /// <summary>
        /// Emits BinaryPrimitives arguments while rewriting managed byte-array buffers to the raw native pointer expected by the runtime helper surface.
        /// </summary>
        /// <param name="semantic">Semantic model for the invocation.</param>
        /// <param name="context">Current conversion context.</param>
        /// <param name="arguments">Invocation arguments to emit.</param>
        /// <param name="lines">Destination token buffer.</param>
        void AppendBinaryPrimitivesInvocationArguments(
            SemanticModel semantic,
            LayerContext context,
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            List<string> lines) {
            for (int index = 0; index < arguments.Count; index++) {
                ArgumentSyntax argument = arguments[index];
                if (!TryAppendBinaryPrimitivesBufferArgument(semantic, context, argument.Expression, lines)) {
                    int start = context.DepthClass;
                    ProcessExpression(semantic, context, argument.Expression, lines);
                    context.PopClass(start);
                }

                if (index < arguments.Count - 1) {
                    lines.Add(", ");
                }
            }
        }

        /// <summary>
        /// Emits the raw backing buffer for BinaryPrimitives when the managed argument is a byte array.
        /// </summary>
        /// <param name="semantic">Semantic model for the argument expression.</param>
        /// <param name="context">Current conversion context.</param>
        /// <param name="expression">Argument expression to emit.</param>
        /// <param name="lines">Destination token buffer.</param>
        /// <returns><c>true</c> when the expression was emitted as a native byte pointer; otherwise <c>false</c>.</returns>
        bool TryAppendBinaryPrimitivesBufferArgument(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax expression,
            List<string> lines) {
            if (!TryGetExpressionTypeSymbol(semantic, expression, out ITypeSymbol expressionTypeSymbol) ||
                (!IsByteArrayTypeSymbol(expressionTypeSymbol) && !IsByteSpanTypeSymbol(expressionTypeSymbol))) {
                return false;
            }

            string bufferText = RenderExpressionText(semantic, context, expression);
            if (IsByteArrayTypeSymbol(expressionTypeSymbol)) {
                lines.Add($"{bufferText}->data()");
                return true;
            }

            lines.Add($"{bufferText}.Data");
            return true;
        }

        static bool IsByteSpanTypeSymbol(ITypeSymbol typeSymbol) {
            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol ||
                namedTypeSymbol.TypeArguments.Length != 1) {
                return false;
            }

            string qualifiedTypeName = namedTypeSymbol.ToDisplayString();
            if (!string.Equals(namedTypeSymbol.Name, "Span", StringComparison.Ordinal) &&
                !string.Equals(namedTypeSymbol.Name, "ReadOnlySpan", StringComparison.Ordinal) &&
                !string.Equals(qualifiedTypeName, "System.Span<byte>", StringComparison.Ordinal) &&
                !string.Equals(qualifiedTypeName, "System.ReadOnlySpan<byte>", StringComparison.Ordinal)) {
                return false;
            }

            return namedTypeSymbol.TypeArguments[0].SpecialType == SpecialType.System_Byte;
        }

        /// <summary>
        /// Resolves the sole generated concrete implementation that can satisfy a generic interface or abstract-base invocation.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve receiver metadata.</param>
        /// <param name="receiverExpression">Receiver expression being invoked.</param>
        /// <param name="invokedMethodSymbol">Resolved source method symbol.</param>
        /// <param name="implementationClass">Receives the unique generated implementation when found.</param>
        /// <returns><c>true</c> when exactly one concrete generated implementation is available; otherwise <c>false</c>.</returns>
        bool TryResolveGeneratedGenericInvocationImplementation(
            SemanticModel semantic,
            ExpressionSyntax receiverExpression,
            IMethodSymbol invokedMethodSymbol,
            out ConversionClass implementationClass) {
            implementationClass = null;

            if (!TryResolveGenericInvocationReceiverType(semantic, receiverExpression, out ITypeSymbol receiverTypeSymbol)) {
                return false;
            }

            List<ConversionClass> matchingImplementations = ResolveGeneratedGenericInvocationDefinitions(receiverTypeSymbol, invokedMethodSymbol.OriginalDefinition);
            if (matchingImplementations.Count != 1) {
                return false;
            }

            implementationClass = matchingImplementations[0];
            if (implementationClass.GenericArgs != null && implementationClass.GenericArgs.Count > 0) {
                implementationClass = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Resolves the receiver information for one generic invocation that may require runtime dispatch.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve receiver metadata.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="invocationExpression">Invocation being lowered.</param>
        /// <param name="invokedMethodSymbol">Resolved source method symbol.</param>
        /// <param name="receiverExpression">Receives the explicit receiver expression when one exists.</param>
        /// <param name="receiverTypeSymbol">Receives the resolved receiver type symbol.</param>
        /// <param name="implicitReceiverText">Receives the implicit receiver access text for unqualified instance calls.</param>
        /// <returns><c>true</c> when a dispatch-capable receiver was resolved; otherwise <c>false</c>.</returns>
        bool TryResolveGenericInvocationReceiver(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol invokedMethodSymbol,
            out ExpressionSyntax receiverExpression,
            out ITypeSymbol receiverTypeSymbol,
            out string implicitReceiverText) {
            receiverExpression = null;
            receiverTypeSymbol = null;
            implicitReceiverText = string.Empty;

            if (invokedMethodSymbol == null || invokedMethodSymbol.IsStatic) {
                return false;
            }

            if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess) {
                if (memberAccess.Expression is BaseExpressionSyntax) {
                    return false;
                }

                receiverExpression = memberAccess.Expression;
                return TryResolveGenericInvocationReceiverType(semantic, receiverExpression, out receiverTypeSymbol);
            }

            if (invocationExpression.Expression is not IdentifierNameSyntax &&
                invocationExpression.Expression is not GenericNameSyntax) {
                return false;
            }

            ConversionClass currentClass = context.GetCurrentClass();
            if (currentClass?.TypeSymbol == null || currentClass.TypeSymbol.TypeKind != TypeKind.Class || !currentClass.TypeSymbol.IsAbstract) {
                return false;
            }

            receiverTypeSymbol = currentClass.TypeSymbol;
            implicitReceiverText = "this";
            return true;
        }

        /// <summary>
        /// Resolves the effective receiver type for one generic invocation receiver expression.
        /// </summary>
        /// <param name="semantic">Semantic model used to resolve receiver metadata.</param>
        /// <param name="receiverExpression">Receiver expression to inspect.</param>
        /// <param name="receiverTypeSymbol">Receives the effective receiver type symbol.</param>
        /// <returns><c>true</c> when the receiver type is an interface or abstract class; otherwise <c>false</c>.</returns>
        bool TryResolveGenericInvocationReceiverType(
            SemanticModel semantic,
            ExpressionSyntax receiverExpression,
            out ITypeSymbol receiverTypeSymbol) {
            receiverTypeSymbol = semantic.GetTypeInfo(receiverExpression).ConvertedType ?? semantic.GetTypeInfo(receiverExpression).Type;
            if (IsWeakRecoveredTypeSymbol(receiverTypeSymbol) &&
                TryGetExpressionTypeSymbol(semantic, receiverExpression, out ITypeSymbol recoveredReceiverTypeSymbol) &&
                !IsWeakRecoveredTypeSymbol(recoveredReceiverTypeSymbol)) {
                receiverTypeSymbol = recoveredReceiverTypeSymbol;
            }

            if (receiverTypeSymbol == null) {
                return false;
            }

            bool isInterfaceReceiver = receiverTypeSymbol.TypeKind == TypeKind.Interface;
            bool isAbstractClassReceiver = receiverTypeSymbol.TypeKind == TypeKind.Class &&
                receiverTypeSymbol is INamedTypeSymbol namedReceiverTypeSymbol &&
                namedReceiverTypeSymbol.IsAbstract;
            return isInterfaceReceiver || isAbstractClassReceiver;
        }

        /// <summary>
        /// Resolves the generated class definitions that satisfy one abstract or interface generic method contract.
        /// </summary>
        /// <param name="receiverTypeSymbol">Receiver contract to satisfy.</param>
        /// <param name="targetMethodSymbol">Generic method contract being dispatched.</param>
        /// <returns>Matching generated class definitions.</returns>
        List<ConversionClass> ResolveGeneratedGenericInvocationDefinitions(
            ITypeSymbol receiverTypeSymbol,
            IMethodSymbol targetMethodSymbol) {
            if (receiverTypeSymbol == null || targetMethodSymbol == null) {
                return new List<ConversionClass>();
            }

            return codeConverter.Program.Classes
                .Where(candidate => candidate.TypeSymbol != null &&
                    !candidate.IsNative &&
                    candidate.DeclarationType != MemberDeclarationType.Interface &&
                    candidate.TypeSymbol.TypeKind == TypeKind.Class &&
                    !candidate.TypeSymbol.IsAbstract &&
                    ImplementsGenericInvocationTarget(candidate.TypeSymbol, receiverTypeSymbol, targetMethodSymbol))
                .ToList();
        }

        /// <summary>
        /// Resolves the concrete instantiated generated types that satisfy one abstract or interface generic method contract.
        /// </summary>
        /// <param name="compilation">Compilation whose object creation sites define the reachable concrete runtime types.</param>
        /// <param name="receiverTypeSymbol">Receiver contract to satisfy.</param>
        /// <param name="targetMethodSymbol">Generic method contract being dispatched.</param>
        /// <returns>Distinct concrete implementation types that can satisfy the dispatch.</returns>
        List<INamedTypeSymbol> ResolveGeneratedGenericInvocationConcreteImplementations(
            Compilation compilation,
            ITypeSymbol receiverTypeSymbol,
            IMethodSymbol targetMethodSymbol) {
            if (compilation == null || receiverTypeSymbol == null || targetMethodSymbol == null) {
                return new List<INamedTypeSymbol>();
            }

            List<INamedTypeSymbol> implementations = new List<INamedTypeSymbol>();
            HashSet<string> seenTypeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (INamedTypeSymbol candidateTypeSymbol in codeConverter.GetInstantiatedGeneratedTypes(compilation)) {
                if (!ImplementsGenericInvocationTarget(candidateTypeSymbol, receiverTypeSymbol, targetMethodSymbol)) {
                    continue;
                }

                string qualifiedTypeName = candidateTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!seenTypeNames.Add(qualifiedTypeName)) {
                    continue;
                }

                implementations.Add(candidateTypeSymbol);
            }

            return implementations;
        }

        /// <summary>
        /// Determines whether one generated concrete type provides the supplied generic method contract for a receiver interface or abstract base.
        /// </summary>
        /// <param name="implementationTypeSymbol">Concrete implementation candidate.</param>
        /// <param name="receiverTypeSymbol">Receiver interface or abstract-base type.</param>
        /// <param name="targetMethodSymbol">Generic source method contract to satisfy.</param>
        /// <returns><c>true</c> when the implementation provides the method contract; otherwise <c>false</c>.</returns>
        static bool ImplementsGenericInvocationTarget(
            INamedTypeSymbol implementationTypeSymbol,
            ITypeSymbol receiverTypeSymbol,
            IMethodSymbol targetMethodSymbol) {
            if (implementationTypeSymbol == null || receiverTypeSymbol == null || targetMethodSymbol == null) {
                return false;
            }

            if (receiverTypeSymbol.TypeKind == TypeKind.Interface) {
                if (!implementationTypeSymbol.AllInterfaces.Any(interfaceSymbol =>
                    SymbolEqualityComparer.Default.Equals(interfaceSymbol.OriginalDefinition, receiverTypeSymbol.OriginalDefinition))) {
                    return false;
                }

                return implementationTypeSymbol.FindImplementationForInterfaceMember(targetMethodSymbol) is IMethodSymbol;
            }

            for (INamedTypeSymbol baseTypeSymbol = implementationTypeSymbol; baseTypeSymbol != null; baseTypeSymbol = baseTypeSymbol.BaseType) {
                if (!SymbolEqualityComparer.Default.Equals(baseTypeSymbol.OriginalDefinition, receiverTypeSymbol.OriginalDefinition)) {
                    continue;
                }

                return ImplementsAbstractGenericInvocationTarget(implementationTypeSymbol, targetMethodSymbol);
            }

            return false;
        }

        /// <summary>
        /// Determines whether one concrete class satisfies an abstract generic method contract through any declaration in its inheritance chain, including intermediate generic base classes.
        /// </summary>
        /// <param name="implementationTypeSymbol">Concrete implementation candidate.</param>
        /// <param name="targetMethodSymbol">Abstract generic method contract being checked.</param>
        /// <returns><c>true</c> when the inheritance chain provides the method contract; otherwise <c>false</c>.</returns>
        static bool ImplementsAbstractGenericInvocationTarget(
            INamedTypeSymbol implementationTypeSymbol,
            IMethodSymbol targetMethodSymbol) {
            IMethodSymbol targetOriginalDefinition = targetMethodSymbol.OriginalDefinition;
            for (INamedTypeSymbol currentTypeSymbol = implementationTypeSymbol; currentTypeSymbol != null; currentTypeSymbol = currentTypeSymbol.BaseType) {
                foreach (IMethodSymbol candidateMethodSymbol in currentTypeSymbol.GetMembers(targetMethodSymbol.Name).OfType<IMethodSymbol>()) {
                    if (!candidateMethodSymbol.IsGenericMethod ||
                        candidateMethodSymbol.TypeParameters.Length != targetMethodSymbol.TypeParameters.Length ||
                        candidateMethodSymbol.Parameters.Length != targetMethodSymbol.Parameters.Length) {
                        continue;
                    }

                    if (SymbolEqualityComparer.Default.Equals(candidateMethodSymbol.OriginalDefinition, targetOriginalDefinition)) {
                        return true;
                    }

                    for (IMethodSymbol overriddenMethodSymbol = candidateMethodSymbol.OverriddenMethod; overriddenMethodSymbol != null; overriddenMethodSymbol = overriddenMethodSymbol.OverriddenMethod) {
                        if (SymbolEqualityComparer.Default.Equals(overriddenMethodSymbol.OriginalDefinition, targetOriginalDefinition)) {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves the emitted member name token for a member-access invocation target.
        /// </summary>
        /// <param name="nameSyntax">Member-access name syntax to inspect.</param>
        /// <returns>The emitted member name token.</returns>
        static string GetInvokedMemberName(SimpleNameSyntax nameSyntax) {
            if (nameSyntax is GenericNameSyntax genericName) {
                return genericName.Identifier.Text;
            }

            return nameSyntax.Identifier.Text;
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

        /// <summary>
        /// Determines whether a Roslyn type symbol represents a managed byte array.
        /// </summary>
        /// <param name="typeSymbol">Type symbol to inspect.</param>
        /// <returns><c>true</c> when the symbol is a byte array; otherwise <c>false</c>.</returns>
        static bool IsByteArrayTypeSymbol(ITypeSymbol typeSymbol) {
            return typeSymbol is IArrayTypeSymbol arrayTypeSymbol &&
                arrayTypeSymbol.ElementType.SpecialType == SpecialType.System_Byte;
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
                string.Equals(typeName, "Guid", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Text.RegularExpressions.Regex", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Guid", StringComparison.Ordinal) ||
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
                string.Equals(typeName, "Guid", StringComparison.Ordinal) ||
                string.Equals(typeName, "System.Guid", StringComparison.Ordinal) ||
                string.Equals(typeName, "global::System.Guid", StringComparison.Ordinal) ||
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
            typeSymbol = null;
            if (semantic == null || expression == null) {
                return false;
            }

            if (!ReferenceEquals(expression.SyntaxTree, semantic.SyntaxTree)) {
                if (expression is ObjectCreationExpressionSyntax objectCreationExpression) {
                    typeSymbol = ResolveObjectCreationTypeSymbolFromText(semantic, objectCreationExpression.Type?.ToString());
                }

                return typeSymbol != null;
            }

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
                    invokedMethodSymbol = ResolveBestInvocationCandidateMethodSymbol(semantic, invocationExpression, invocationSymbolInfo, argumentCount);
                    if (invokedMethodSymbol == null) {
                        invokedMethodSymbol = ResolveMethodSymbol(invocationSymbolInfo);
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
                prefixExpression.IsKind(SyntaxKind.PointerIndirectionExpression) &&
                TryGetExpressionTypeSymbol(semantic, prefixExpression.Operand, out ITypeSymbol pointerOperandType) &&
                pointerOperandType is IPointerTypeSymbol pointerTypeSymbol &&
                !IsWeakRecoveredTypeSymbol(pointerTypeSymbol.PointedAtType)) {
                typeSymbol = pointerTypeSymbol.PointedAtType;
                return true;
            }

            if (expression is PrefixUnaryExpressionSyntax otherPrefixExpression &&
                TryGetExpressionTypeSymbol(semantic, otherPrefixExpression.Operand, out ITypeSymbol prefixOperandType) &&
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

            if (semantic == null || typeSyntax == null) {
                return false;
            }

            if (!ReferenceEquals(typeSyntax.SyntaxTree, semantic.SyntaxTree)) {
                string fallbackTypeName = typeSyntax.ToString();
                return string.Equals(fallbackTypeName, "object", StringComparison.Ordinal) ||
                    string.Equals(fallbackTypeName, "System.Object", StringComparison.Ordinal) ||
                    string.Equals(fallbackTypeName, "global::System.Object", StringComparison.Ordinal);
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

            if (TryProcessRefLocalDeclaration(semantic, context, declaration, varType, lines)) {
                return;
            }

            if (TryProcessTupleDeconstructionDeclaration(semantic, context, declaration, lines)) {
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
                    if (ShouldEmitEmptyStringForStringDeclaration(varType, variable.Initializer.Value)) {
                        newLines.Add("std::string()");
                    } else {
                        ExpressionResult result = ProcessExpression(semantic, context, variable.Initializer.Value, newLines);
                        if (result.BeforeLines != null && result.BeforeLines.Count > 0) {
                            beforeDeclarationLines.AddRange(result.BeforeLines);
                        }
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

        /// <summary>
        /// Lowers C# ref locals to native C++ reference bindings so ref-return initializers preserve aliasing semantics without inventing placeholder object pointers.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the declaration.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="declaration">Declaration being evaluated.</param>
        /// <param name="declarationType">Resolved declaration type for tracked-local metadata.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <returns><c>true</c> when the declaration was lowered as a ref local; otherwise <c>false</c>.</returns>
        bool TryProcessRefLocalDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            VariableType declarationType,
            List<string> lines) {
            if (declaration == null ||
                declaration.Type is not RefTypeSyntax ||
                declaration.Variables.Count != 1) {
                return false;
            }

            VariableDeclaratorSyntax variable = declaration.Variables[0];
            if (variable.Initializer?.Value == null) {
                return false;
            }

            ExpressionSyntax initializerExpression = variable.Initializer.Value is RefExpressionSyntax refExpression
                ? refExpression.Expression
                : variable.Initializer.Value;
            List<string> initializerLines = new List<string>();
            int start = context.DepthClass;
            ExpressionResult initializerResult = ProcessExpression(semantic, context, initializerExpression, initializerLines);
            context.PopClass(start);
            if (!initializerResult.Processed) {
                return false;
            }

            if (initializerResult.BeforeLines != null && initializerResult.BeforeLines.Count > 0) {
                lines.AddRange(initializerResult.BeforeLines);
            }

            FunctionStack? functionStack = context.GetCurrentFunction();
            bool isRebindableReferenceLocal = IsDeclaredRefLocalReassigned(semantic, variable);
            if (functionStack != null) {
                functionStack.Stack.Add(new ConversionVariable {
                    Name = variable.Identifier.ToString(),
                    VarType = declarationType,
                    IsRebindableReferenceLocal = isRebindableReferenceLocal
                });
            }

            if (isRebindableReferenceLocal) {
                lines.Add("auto* ");
            } else {
                CPPTypeData declarationTypeData;
                VariableType cppDeclarationType = ConvertToCPPType(declarationType, out declarationTypeData);
                RegisterGeneratedTypeReferences(context, declarationType);
                string declarationTypeName = QualifyRenderedCppTypeName(cppDeclarationType.ToCPPString(context.Program), context);
                string pointerSuffix = declarationTypeData.IsPointer ? "*" : string.Empty;
                lines.Add($"{declarationTypeName}{pointerSuffix}& ");
            }

            lines.Add(variable.Identifier.ToString());
            lines.Add(isRebindableReferenceLocal ? " = &" : " = ");
            lines.AddRange(initializerLines);
            return true;
        }

        /// <summary>
        /// Lowers tuple deconstruction declarations into a temporary tuple value followed by one local assignment per element.
        /// </summary>
        /// <param name="semantic">Semantic model associated with the declaration.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="declaration">Declaration being evaluated.</param>
        /// <param name="lines">Output line buffer that receives emitted C++ tokens.</param>
        /// <returns><c>true</c> when the declaration was lowered as a tuple deconstruction; otherwise <c>false</c>.</returns>
        bool TryProcessTupleDeconstructionDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            List<string> lines) {
            if (declaration == null ||
                declaration.Variables.Count != 1) {
                return false;
            }

            ParenthesizedVariableDesignationSyntax tupleDesignation = declaration
                .DescendantNodes()
                .OfType<ParenthesizedVariableDesignationSyntax>()
                .FirstOrDefault();
            if (tupleDesignation == null ||
                declaration.Variables[0].Initializer?.Value == null) {
                return false;
            }

            ExpressionSyntax initializerExpression = declaration.Variables[0].Initializer.Value;
            List<string> initializerLines = new List<string>();
            int start = context.DepthClass;
            ExpressionResult initializerResult = ProcessExpression(semantic, context, initializerExpression, initializerLines);
            context.PopClass(start);
            if (!initializerResult.Processed) {
                return false;
            }

            VariableType tupleType = ResolveTupleDeconstructionType(semantic, context, initializerExpression, initializerResult);
            if (tupleType?.Type != VariableDataType.Tuple ||
                tupleType.GenericArgs.Count < tupleDesignation.Variables.Count) {
                return false;
            }

            if (initializerResult.BeforeLines != null && initializerResult.BeforeLines.Count > 0) {
                lines.AddRange(initializerResult.BeforeLines);
            }

            string temporaryName = CreateTemporaryName("__deconstruct");
            lines.Add($"const auto {temporaryName} = ");
            lines.AddRange(initializerLines);
            lines.Add(";\n");

            string tupleMemberAccessOperator = ".";
            FunctionStack functionStack = context.GetCurrentFunction();
            for (int index = 0; index < tupleDesignation.Variables.Count; index++) {
                if (tupleDesignation.Variables[index] is not SingleVariableDesignationSyntax singleVariableDesignation) {
                    continue;
                }

                VariableType elementType = tupleType.GenericArgs[index];
                CPPTypeData elementTypeData;
                VariableType cppElementType = ConvertToCPPType(elementType, out elementTypeData);
                RegisterGeneratedTypeReferences(context, elementType);
                string pointerSuffix = elementTypeData.IsPointer ? "*" : string.Empty;
                string declarationTypeName = QualifyRenderedCppTypeName(cppElementType.ToCPPString(context.Program), context);
                lines.Add($"{declarationTypeName}{pointerSuffix} {singleVariableDesignation.Identifier.Text} = {temporaryName}{tupleMemberAccessOperator}Item{index + 1};\n");

                if (functionStack != null) {
                    functionStack.Stack.Add(new ConversionVariable {
                        Name = singleVariableDesignation.Identifier.Text,
                        VarType = elementType
                    });
                }
            }

            return true;
        }

        VariableType ResolveTupleDeconstructionType(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax initializerExpression,
            ExpressionResult initializerResult) {
            if (TryGetExpressionTypeSymbol(semantic, initializerExpression, out ITypeSymbol initializerTypeSymbol)) {
                VariableType symbolType = VariableUtil.GetVarType(initializerTypeSymbol);
                if (symbolType.Type == VariableDataType.Tuple) {
                    return symbolType;
                }
            }

            if (initializerResult.Type != null &&
                initializerResult.Type.Type == VariableDataType.Tuple) {
                return initializerResult.Type;
            }

            if (TryResolveInferredExpressionVariableType(context, semantic, initializerExpression, out VariableType inferredType) &&
                inferredType.Type == VariableDataType.Tuple) {
                return inferredType;
            }

            return null;
        }

        static bool IsDeclaredRefLocalReassigned(SemanticModel semantic, VariableDeclaratorSyntax variable) {
            if (semantic.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol) {
                return false;
            }

            SyntaxNode mutationScope = variable.FirstAncestorOrSelf<BlockSyntax>() ?? variable.SyntaxTree.GetRoot();
            foreach (AssignmentExpressionSyntax assignmentExpression in mutationScope.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
                if (assignmentExpression.Kind() == SyntaxKind.SimpleAssignmentExpression &&
                    assignmentExpression.Right is RefExpressionSyntax &&
                    SymbolsMatch(semantic.GetSymbolInfo(assignmentExpression.Left).Symbol, localSymbol)) {
                    return true;
                }
            }

            return false;
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

        bool ShouldDeleteManagedLocalAtScopeExit(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclaratorSyntax variable,
            VariableDeclarationSyntax declaration) {
            if (semantic == null || context == null || variable == null || declaration == null) {
                return false;
            }

            if (declaration.Parent is not LocalDeclarationStatementSyntax) {
                return false;
            }

            if (!IsOwnedManagedLocalInitializerExpression(semantic, variable.Initializer?.Value)) {
                return false;
            }

            VariableType declarationType = ResolveDeclarationType(semantic, declaration);
            if (declarationType == null) {
                return false;
            }

            VariableType cppType = ConvertToCPPType(declarationType, out CPPTypeData typeData);
            if (cppType == null || !typeData.IsPointer) {
                return false;
            }

            if (HasExplicitNativeOwnershipRelease(semantic, variable)) {
                return false;
            }

            return !DoesLocalEscapeScope(semantic, variable);
        }

        bool ShouldDeleteManagedLocalAtScopeExit(
            SemanticModel semantic,
            LayerContext context,
            IdentifierNameSyntax identifierName) {
            if (semantic == null || context == null || identifierName == null) {
                return false;
            }

            if (semantic.SyntaxTree != identifierName.SyntaxTree) {
                return false;
            }

            ILocalSymbol localSymbol;
            try {
                localSymbol = semantic.GetSymbolInfo(identifierName).Symbol as ILocalSymbol;
            } catch (ArgumentException) {
                return false;
            }

            if (localSymbol == null ||
                localSymbol.DeclaringSyntaxReferences.Length == 0) {
                return false;
            }

            SyntaxNode declarationNode = localSymbol.DeclaringSyntaxReferences[0].GetSyntax();
            if (declarationNode is not VariableDeclaratorSyntax variableDeclarator ||
                variableDeclarator.Parent is not VariableDeclarationSyntax declaration) {
                return false;
            }

            return ShouldDeleteManagedLocalAtScopeExit(semantic, context, variableDeclarator, declaration);
        }

        static bool IsManagedHeapAllocationExpression(ExpressionSyntax expression) {
            if (expression == null) {
                return false;
            }

            while (expression is ParenthesizedExpressionSyntax parenthesizedExpression) {
                expression = parenthesizedExpression.Expression;
            }

            return expression is ObjectCreationExpressionSyntax ||
                expression is ImplicitObjectCreationExpressionSyntax ||
                expression is ArrayCreationExpressionSyntax ||
                expression is ImplicitArrayCreationExpressionSyntax ||
                expression is CollectionExpressionSyntax;
        }

        bool IsOwnedManagedLocalInitializerExpression(SemanticModel semantic, ExpressionSyntax expression) {
            if (expression == null) {
                return false;
            }

            while (expression is ParenthesizedExpressionSyntax parenthesizedExpression) {
                expression = parenthesizedExpression.Expression;
            }

            if (expression is CastExpressionSyntax castExpression) {
                expression = castExpression.Expression;
            }

            if (IsManagedHeapAllocationExpression(expression)) {
                return true;
            }

            ISymbol symbol = semantic.GetSymbolInfo(expression).Symbol;
            if (symbol is IPropertySymbol propertySymbol) {
                return DoesMemberReturnArrayAsOwnedNativeList(semantic, propertySymbol);
            }

            if (expression is InvocationExpressionSyntax invocationExpression) {
                IMethodSymbol methodSymbol = ResolveInvokedMethodSymbol(semantic, invocationExpression);
                return DoesMemberReturnArrayAsOwnedNativeList(semantic, methodSymbol);
            }

            return false;
        }

        bool DoesMemberReturnArrayAsOwnedNativeList(SemanticModel semantic, ISymbol symbol) {
            if (semantic == null || symbol == null) {
                return false;
            }

            ITypeSymbol returnTypeSymbol = ResolveMemberReturnTypeSymbol(symbol);
            if (!IsListFamilyTypeSymbol(returnTypeSymbol)) {
                return false;
            }

            bool foundReturnExpression = false;
            foreach (SyntaxReference syntaxReference in symbol.DeclaringSyntaxReferences) {
                SyntaxNode declaration = syntaxReference.GetSyntax();
                if (!semantic.Compilation.ContainsSyntaxTree(declaration.SyntaxTree)) {
                    return false;
                }

                SemanticModel declarationSemantic;
                try {
                    declarationSemantic = semantic.Compilation.GetSemanticModel(declaration.SyntaxTree);
                } catch (ArgumentException) {
                    return false;
                }

                foreach (ExpressionSyntax returnExpression in EnumerateMemberReturnExpressions(declaration)) {
                    foundReturnExpression = true;
                    if (!TryResolveArrayElementTypeSymbol(declarationSemantic, returnExpression, out _)) {
                        return false;
                    }
                }
            }

            return foundReturnExpression;
        }

        static ITypeSymbol ResolveMemberReturnTypeSymbol(ISymbol symbol) {
            if (symbol is IPropertySymbol propertySymbol) {
                return propertySymbol.Type;
            }

            if (symbol is IMethodSymbol methodSymbol) {
                return methodSymbol.ReturnType;
            }

            return null;
        }

        static IEnumerable<ExpressionSyntax> EnumerateMemberReturnExpressions(SyntaxNode declaration) {
            if (declaration is PropertyDeclarationSyntax propertyDeclaration) {
                if (propertyDeclaration.ExpressionBody?.Expression != null) {
                    yield return propertyDeclaration.ExpressionBody.Expression;
                }

                foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList?.Accessors ?? default) {
                    if (!accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) {
                        continue;
                    }

                    if (accessor.ExpressionBody?.Expression != null) {
                        yield return accessor.ExpressionBody.Expression;
                    }

                    foreach (ReturnStatementSyntax returnStatement in accessor.DescendantNodes().OfType<ReturnStatementSyntax>()) {
                        if (returnStatement.Expression != null) {
                            yield return returnStatement.Expression;
                        }
                    }
                }
            }

            if (declaration is MethodDeclarationSyntax methodDeclaration) {
                if (methodDeclaration.ExpressionBody?.Expression != null) {
                    yield return methodDeclaration.ExpressionBody.Expression;
                }

                foreach (ReturnStatementSyntax returnStatement in methodDeclaration.DescendantNodes().OfType<ReturnStatementSyntax>()) {
                    if (returnStatement.Expression != null) {
                        yield return returnStatement.Expression;
                    }
                }
            }
        }

        static bool DoesExpressionReferenceLocal(
            SemanticModel semantic,
            ExpressionSyntax expression,
            IdentifierNameSyntax identifierName) {
            if (semantic == null || expression == null || identifierName == null) {
                return false;
            }

            if (semantic.SyntaxTree != expression.SyntaxTree ||
                semantic.SyntaxTree != identifierName.SyntaxTree) {
                return false;
            }

            ILocalSymbol localSymbol;
            try {
                localSymbol = semantic.GetSymbolInfo(identifierName).Symbol as ILocalSymbol;
            } catch (ArgumentException) {
                return false;
            }

            if (localSymbol == null) {
                return false;
            }

            foreach (IdentifierNameSyntax candidate in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()) {
                if (SymbolsMatch(semantic.GetSymbolInfo(candidate).Symbol, localSymbol)) {
                    return true;
                }
            }

            return false;
        }

        static bool DoesLocalEscapeScope(SemanticModel semantic, VariableDeclaratorSyntax variable) {
            if (semantic.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol) {
                return false;
            }

            SyntaxNode scope = variable.FirstAncestorOrSelf<BlockSyntax>() ?? variable.SyntaxTree.GetRoot();
            foreach (IdentifierNameSyntax identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>()) {
                if (!SymbolsMatch(semantic.GetSymbolInfo(identifier).Symbol, localSymbol)) {
                    continue;
                }

                if (identifier.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().Any() ||
                    identifier.Ancestors().OfType<LocalFunctionStatementSyntax>().Any()) {
                    return true;
                }

                if (identifier.Ancestors().OfType<ReturnStatementSyntax>().Any() &&
                    identifier.FirstAncestorOrSelf<ReturnStatementSyntax>()?.Expression is ExpressionSyntax returnExpression &&
                    IsDirectLocalValueExpression(semantic, returnExpression, localSymbol)) {
                    return true;
                }

                if (identifier.Parent is EqualsValueClauseSyntax equalsValueClause &&
                    IsDirectLocalValueExpression(semantic, equalsValueClause.Value, localSymbol) &&
                    equalsValueClause.Parent is VariableDeclaratorSyntax targetVariable &&
                    !SymbolEqualityComparer.Default.Equals(semantic.GetDeclaredSymbol(targetVariable), localSymbol)) {
                    return true;
                }

                if (identifier.Parent is AssignmentExpressionSyntax assignmentExpression &&
                    IsDirectLocalValueExpression(semantic, assignmentExpression.Right, localSymbol) &&
                    !SymbolsMatch(semantic.GetSymbolInfo(assignmentExpression.Left).Symbol, localSymbol)) {
                    return true;
                }

                if (identifier.Parent is ArgumentSyntax argumentSyntax) {
                    bool isRefOrOutArgument = argumentSyntax.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword) ||
                        argumentSyntax.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword);
                    if (isRefOrOutArgument) {
                        return true;
                    }

                    if (argumentSyntax.Parent?.Parent is InvocationExpressionSyntax invocationExpression) {
                        if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess &&
                            memberAccess.Expression == identifier) {
                            continue;
                        }

                        if (IsDirectLocalValueExpression(semantic, argumentSyntax.Expression, localSymbol) &&
                            !IsNativeNoEscapeArgument(semantic, invocationExpression, argumentSyntax)) {
                            return true;
                        }
                    }

                    if (argumentSyntax.Parent?.Parent is BaseObjectCreationExpressionSyntax objectCreationExpression) {
                        if (IsDirectLocalValueExpression(semantic, argumentSyntax.Expression, localSymbol)) {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        static bool HasExplicitNativeOwnershipRelease(SemanticModel semantic, VariableDeclaratorSyntax variable) {
            if (semantic.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol) {
                return false;
            }

            SyntaxNode scope = variable.FirstAncestorOrSelf<BlockSyntax>() ?? variable.SyntaxTree.GetRoot();
            foreach (InvocationExpressionSyntax invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) {
                    continue;
                }

                string methodName = memberAccess.Name.Identifier.Text;
                if (!string.Equals(methodName, "Delete", StringComparison.Ordinal) &&
                    !string.Equals(methodName, "Release", StringComparison.Ordinal) &&
                    !string.Equals(methodName, "DisposeAndDelete", StringComparison.Ordinal) &&
                    !string.Equals(methodName, "DisposeAndRelease", StringComparison.Ordinal)) {
                    continue;
                }

                ISymbol invokedMethodSymbol = semantic.GetSymbolInfo(memberAccess).Symbol;
                if (!string.Equals(invokedMethodSymbol?.ContainingType?.Name, "NativeOwnership", StringComparison.Ordinal)) {
                    continue;
                }

                foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments) {
                    if (IsDirectLocalValueExpression(semantic, argument.Expression, localSymbol)) {
                        return true;
                    }
                }
            }

            return false;
        }

        static bool IsNativeNoEscapeArgument(
            SemanticModel semantic,
            InvocationExpressionSyntax invocationExpression,
            ArgumentSyntax argumentSyntax) {
            if (semantic == null || invocationExpression == null || argumentSyntax == null) {
                return false;
            }

            IMethodSymbol methodSymbol = semantic.GetSymbolInfo(invocationExpression).Symbol as IMethodSymbol;
            if (methodSymbol == null) {
                return false;
            }

            int argumentIndex = invocationExpression.ArgumentList.Arguments.IndexOf(argumentSyntax);
            if (argumentIndex < 0 || argumentIndex >= methodSymbol.Parameters.Length) {
                return false;
            }

            IParameterSymbol parameterSymbol = methodSymbol.Parameters[argumentIndex];
            foreach (AttributeData attributeData in parameterSymbol.GetAttributes()) {
                string attributeName = attributeData.AttributeClass?.Name;
                if (string.Equals(attributeName, "NativeNoEscapeAttribute", StringComparison.Ordinal) ||
                    string.Equals(attributeName, "NativeNoEscape", StringComparison.Ordinal)) {
                    return true;
                }
            }

            return false;
        }

        static bool IsDirectLocalValueExpression(
            SemanticModel semantic,
            ExpressionSyntax expression,
            ILocalSymbol localSymbol) {
            if (expression == null) {
                return false;
            }

            if (expression is ParenthesizedExpressionSyntax parenthesizedExpression) {
                return IsDirectLocalValueExpression(semantic, parenthesizedExpression.Expression, localSymbol);
            }

            if (expression is CastExpressionSyntax castExpression) {
                return IsDirectLocalValueExpression(semantic, castExpression.Expression, localSymbol);
            }

            if (expression is IdentifierNameSyntax identifierName) {
                return SymbolsMatch(semantic.GetSymbolInfo(identifierName).Symbol, localSymbol);
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
            if (declaration.Type is RefTypeSyntax refTypeSyntax) {
                if (refTypeSyntax.Type is IdentifierNameSyntax refIdentifierName &&
                    string.Equals(refIdentifierName.Identifier.Text, "var", StringComparison.Ordinal) &&
                    declaration.Variables.Count == 1) {
                    VariableDeclaratorSyntax refVariable = declaration.Variables[0];
                    if (semantic.GetDeclaredSymbol(refVariable) is ILocalSymbol refLocalSymbol) {
                        VariableType localType = VariableUtil.GetVarType(refLocalSymbol.Type);
                        if (!IsWeakObjectVariableType(localType)) {
                            return localType;
                        }
                    }

                    if (refVariable.Initializer?.Value is RefExpressionSyntax refExpression) {
                        ExpressionSyntax sourceExpression = refExpression.Expression;
                        if (sourceExpression is ElementAccessExpressionSyntax refElementAccess &&
                            TryResolveElementAccessTypeSymbol(semantic, refElementAccess, out ITypeSymbol refElementTypeSymbol)) {
                            return VariableUtil.GetVarType(refElementTypeSymbol);
                        }

                        if (sourceExpression is MemberAccessExpressionSyntax refMemberAccess &&
                            TryResolveMemberAccessResultTypeSymbol(semantic, refMemberAccess, out ITypeSymbol refMemberTypeSymbol)) {
                            return VariableUtil.GetVarType(refMemberTypeSymbol);
                        }

                        if (TryGetExpressionTypeSymbol(semantic, sourceExpression, out ITypeSymbol refSourceTypeSymbol)) {
                            return VariableUtil.GetVarType(refSourceTypeSymbol);
                        }
                    }
                }

                return VariableUtil.GetVarType(refTypeSyntax.Type, semantic);
            }

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
                conversionVariable.Remap = CPPIdentifierSanitizer.SanitizeIdentifier(conversionVariable.Name);
                if (string.Equals(conversionVariable.Remap, conversionVariable.Name, StringComparison.Ordinal)) {
                    conversionVariable.Remap = null;
                }
                conversionVariable.VarType = sourceType;
                fn.Stack.Add(conversionVariable);
            }

            string pointer = typeData.IsPointer ? " *" : " ";
            string declarationTypeName = QualifyRenderedCppTypeName(cppType.ToCPPString(context.Program), context);
            List<string> beforeDeclarationLines = new List<string>();
            string emittedVariableName = CPPIdentifierSanitizer.SanitizeIdentifier(variable.Identifier.ToString());
            lines.Add($"{declarationTypeName}{pointer}{emittedVariableName} = ");

            int start = context.DepthClass;
            ExpressionResult initializerResult = ProcessExpression(semantic, context, variable.Initializer.Value, lines);
            context.PopClass(start);
            if (initializerResult.BeforeLines != null && initializerResult.BeforeLines.Count > 0) {
                beforeDeclarationLines.AddRange(initializerResult.BeforeLines);
            }
            if (beforeDeclarationLines.Count > 0) {
                lines.InsertRange(0, beforeDeclarationLines);
            }
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

        static bool TryRewriteTargetTypedNumericLiteral(
            ExpressionSyntax expression,
            ITypeSymbol targetTypeSymbol,
            out string rewrittenLiteral) {
            rewrittenLiteral = string.Empty;
            if (expression == null || targetTypeSymbol == null) {
                return false;
            }

            ExpressionSyntax unwrappedExpression = expression;
            while (unwrappedExpression is ParenthesizedExpressionSyntax parenthesizedExpression) {
                unwrappedExpression = parenthesizedExpression.Expression;
            }

            string signPrefix = string.Empty;
            if (unwrappedExpression is PrefixUnaryExpressionSyntax prefixUnaryExpression &&
                (prefixUnaryExpression.IsKind(SyntaxKind.UnaryMinusExpression) ||
                 prefixUnaryExpression.IsKind(SyntaxKind.UnaryPlusExpression))) {
                signPrefix = prefixUnaryExpression.OperatorToken.Text;
                unwrappedExpression = prefixUnaryExpression.Operand;
            }

            if (unwrappedExpression is not LiteralExpressionSyntax literalExpression ||
                !literalExpression.IsKind(SyntaxKind.NumericLiteralExpression)) {
                return false;
            }

            string normalizedLiteral = NormalizeNumericLiteral(literalExpression.Token.Text);
            switch (targetTypeSymbol.SpecialType) {
                case SpecialType.System_Single:
                    rewrittenLiteral = signPrefix + NormalizeFloatingPointLiteral(normalizedLiteral, appendFloatSuffix: true);
                    return true;
                case SpecialType.System_Double:
                    rewrittenLiteral = signPrefix + NormalizeFloatingPointLiteral(normalizedLiteral, appendFloatSuffix: false);
                    return true;
                default:
                    return false;
            }
        }

        static string NormalizeFloatingPointLiteral(string normalizedLiteral, bool appendFloatSuffix) {
            if (string.IsNullOrWhiteSpace(normalizedLiteral)) {
                return normalizedLiteral;
            }

            string literal = normalizedLiteral;
            if (literal.EndsWith("f", StringComparison.OrdinalIgnoreCase) ||
                literal.EndsWith("d", StringComparison.OrdinalIgnoreCase)) {
                literal = literal[..^1];
            }

            if (!literal.Contains('.', StringComparison.Ordinal) &&
                !literal.Contains('e', StringComparison.OrdinalIgnoreCase)) {
                literal += ".0";
            }

            if (appendFloatSuffix) {
                literal += "f";
            }

            return literal;
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

            ConversionClass generatedClass = context.Program.FindGeneratedClass(variableType);
            if (generatedClass != null) {
                RegisterGeneratedTypeDependency(context, generatedClass.GetEmittedTypeName());
            } else {
            RegisterGeneratedTypeReference(context, variableType.TypeName, variableType.GenericArgs.Count);
            }

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
            string emittedTypeName = generatedClass.GetEmittedTypeName();
            if (currentClass == null || currentClass.ReferencedClasses.Contains(emittedTypeName)) {
                return;
            }

            currentClass.ReferencedClasses.Add(emittedTypeName);
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

            bool isSpanDeclaration = declarationType.TypeName == "Span" && declarationType.GenericArgs.Count == 1;
            bool isPointerDeclaration = declaration.Type is PointerTypeSyntax || declarationType.IsPointer;
            if (!isSpanDeclaration && !isPointerDeclaration) {
                return false;
            }

            VariableDeclaratorSyntax variable = declaration.Variables[0];
            if (variable.Initializer?.Value is not StackAllocArrayCreationExpressionSyntax stackAllocExpression) {
                return false;
            }

            if (stackAllocExpression.Type is not ArrayTypeSyntax stackAllocType) {
                return false;
            }

            if (stackAllocType.RankSpecifiers.Count != 1) {
                return false;
            }

            VariableType elementType = isSpanDeclaration
                ? declarationType.GenericArgs[0]
                : VariableUtil.GetVarType(stackAllocType.ElementType, semantic);
            CPPTypeData elementTypeData;
            VariableType cppElementType = ConvertToCPPType(elementType, out elementTypeData);
            List<string> sizeLines = new List<string>();
            InitializerExpressionSyntax initializer = stackAllocExpression.Initializer;
            bool hasInitializer = initializer != null && initializer.Expressions.Count > 0;
            bool useFixedArrayBuffer = false;
            if (hasInitializer) {
                sizeLines.Add(initializer.Expressions.Count.ToString(CultureInfo.InvariantCulture));
                useFixedArrayBuffer = true;
            } else {
                if (stackAllocType.RankSpecifiers[0].Sizes.Count != 1) {
                    return false;
                }

                ExpressionResult sizeResult = ProcessExpression(semantic, context, stackAllocType.RankSpecifiers[0].Sizes[0], sizeLines);
                if (!sizeResult.Processed) {
                    return false;
                }

                useFixedArrayBuffer = semantic?.GetConstantValue(stackAllocType.RankSpecifiers[0].Sizes[0]).HasValue == true;
            }

            FunctionStack currentFunction = context.GetCurrentFunction();
            if (currentFunction != null) {
                ConversionVariable stackVariable = new ConversionVariable();
                stackVariable.Name = variable.Identifier.ToString();
                stackVariable.Remap = CPPIdentifierSanitizer.SanitizeIdentifier(stackVariable.Name);
                if (string.Equals(stackVariable.Remap, stackVariable.Name, StringComparison.Ordinal)) {
                    stackVariable.Remap = null;
                }
                stackVariable.VarType = declarationType;
                currentFunction.Stack.Add(stackVariable);
            }

            string elementTypeName = cppElementType.ToCPPString(context.Program);
            if (isPointerDeclaration) {
                string bufferName = $"{variable.Identifier.Text}_stackalloc";
                if (useFixedArrayBuffer) {
                    lines.Add($"{elementTypeName} {bufferName}[");
                    lines.AddRange(sizeLines);
                    lines.Add("];\n");
                    AppendStackAllocInitializerAssignments(semantic, context, initializer, bufferName, lines);
                    lines.Add($"{elementTypeName} *{variable.Identifier.Text} = {bufferName}");
                    return true;
                }

                RegisterRuntimeRequirement("NativeArray");
                lines.Add($"Array<{elementTypeName}> {bufferName}(");
                lines.AddRange(sizeLines);
                lines.Add(");\n");
                lines.Add($"{elementTypeName} *{variable.Identifier.Text} = {bufferName}.Data");
                return true;
            }

            string spanBufferName = $"{variable.Identifier.Text}_stackalloc";
            if (useFixedArrayBuffer) {
                lines.Add($"{elementTypeName} {spanBufferName}[");
                lines.AddRange(sizeLines);
                lines.Add("];\n");
                AppendStackAllocInitializerAssignments(semantic, context, initializer, spanBufferName, lines);
                lines.Add($"Span<{elementTypeName}> {variable.Identifier}({spanBufferName}, ");
                lines.AddRange(sizeLines);
                lines.Add(")");
                return true;
            }

            RegisterRuntimeRequirement("NativeArray");
            lines.Add($"Array<{elementTypeName}> {spanBufferName}(");
            lines.AddRange(sizeLines);
            lines.Add(");\n");
            lines.Add($"Span<{elementTypeName}> {variable.Identifier}({spanBufferName}.Data, ");
            lines.AddRange(sizeLines);
            lines.Add(")");
            return true;
        }

        void AppendStackAllocInitializerAssignments(
            SemanticModel semantic,
            LayerContext context,
            InitializerExpressionSyntax initializer,
            string bufferName,
            List<string> lines) {
            if (initializer == null || lines == null) {
                return;
            }

            for (int initializerIndex = 0; initializerIndex < initializer.Expressions.Count; initializerIndex++) {
                ExpressionSyntax initializerExpression = initializer.Expressions[initializerIndex];
                List<string> valueLines = new List<string>();
                int valueStart = context.DepthClass;
                ProcessExpression(semantic, context, initializerExpression, valueLines);
                context.PopClass(valueStart);
                lines.Add($"{bufferName}[{initializerIndex}] = ");
                lines.AddRange(valueLines);
                lines.Add(";\n");
            }
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
                stackVariable.Remap = CPPIdentifierSanitizer.SanitizeIdentifier(stackVariable.Name);
                if (string.Equals(stackVariable.Remap, stackVariable.Name, StringComparison.Ordinal)) {
                    stackVariable.Remap = null;
                }
                stackVariable.VarType = declarationType;
                currentFunction.Stack.Add(stackVariable);
            }

            CPPTypeData declarationTypeData;
            VariableType cppDeclarationType = ConvertToCPPType(declarationType, out declarationTypeData);
            RegisterGeneratedTypeReferences(context, declarationType);

            string pointer = declarationTypeData.IsPointer ? " *" : " ";
            string declarationTypeName = QualifyRenderedCppTypeName(cppDeclarationType.ToCPPString(context.Program), context);
            string emittedVariableName = CPPIdentifierSanitizer.SanitizeIdentifier(variable.Identifier.Text);
            lines.Add($"{declarationTypeName}{pointer}{emittedVariableName} = ");
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
                string emittedVariableName = CPPIdentifierSanitizer.SanitizeIdentifier(variable.Identifier.Text);
                List<string> declarationLines = [$"{declarationTypeName}{pointer}", emittedVariableName];
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
                    stackVariable.Remap = emittedVariableName;
                    if (string.Equals(stackVariable.Remap, stackVariable.Name, StringComparison.Ordinal)) {
                        stackVariable.Remap = null;
                    }
                    stackVariable.VarType = varType;
                    fn.Stack.Add(stackVariable);
                }

                if (variable.Initializer != null) {
                    declarationLines.Add(" = ");
                    if (ShouldEmitEmptyStringForStringDeclaration(varType, variable.Initializer.Value)) {
                        declarationLines.Add("std::string()");
                    } else {
                        ExpressionResult result = ProcessExpression(semantic, context, variable.Initializer.Value, declarationLines);
                        if (result.BeforeLines != null && result.BeforeLines.Count > 0) {
                            beforeDeclarationLines.AddRange(result.BeforeLines);
                        }
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
        /// Determines whether one local declaration must lower a null-like initializer to an empty native string.
        /// </summary>
        /// <param name="declarationType">Resolved declared type for the local variable.</param>
        /// <param name="initializerValue">Initializer expression applied to the declaration.</param>
        /// <returns><c>true</c> when the initializer should emit <c>std::string()</c>; otherwise, <c>false</c>.</returns>
        static bool ShouldEmitEmptyStringForStringDeclaration(VariableType declarationType, ExpressionSyntax initializerValue) {
            if (declarationType == null || initializerValue == null) {
                return false;
            }

            if (declarationType.Type != VariableDataType.String &&
                !string.Equals(declarationType.TypeName, "string", StringComparison.Ordinal) &&
                !string.Equals(declarationType.TypeName, "std::string", StringComparison.Ordinal)) {
                return false;
            }

            return IsNullLikeExpression(initializerValue);
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
                case SyntaxKind.DefaultLiteralExpression: {
                        SemanticModel semantic = context?.GetCurrentFunction()?.Function?.Semantic ?? context?.GetCurrentClass()?.Semantic;
                        ITypeSymbol sourceTypeSymbol = semantic?.GetTypeInfo(literalExpression).Type ?? semantic?.GetTypeInfo(literalExpression).ConvertedType;
                        if (sourceTypeSymbol != null) {
                            VariableType sourceType = VariableUtil.GetVarType(sourceTypeSymbol);
                            CPPTypeData defaultTypeData;
                            VariableType cppDefaultType = ConvertToCPPType(sourceType, out defaultTypeData);
                            type = sourceType.TypeName ?? "object";
                            literalValue = defaultTypeData.IsPointer
                                ? "nullptr"
                                : $"{QualifyRenderedCppTypeName(cppDefaultType.ToCPPString(context.Program), context)}()";
                            break;
                        }

                        type = "null";
                        literalValue = "nullptr";
                        break;
                }
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

            string normalizedLiteral = literalText.Replace("_", string.Empty, StringComparison.Ordinal);
            if (normalizedLiteral.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                normalizedLiteral.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) {
                return normalizedLiteral;
            }

            if (normalizedLiteral.EndsWith("d", StringComparison.Ordinal) || normalizedLiteral.EndsWith("D", StringComparison.Ordinal)) {
                string withoutSuffix = normalizedLiteral[..^1];
                if (!withoutSuffix.Contains('.', StringComparison.Ordinal) &&
                    !withoutSuffix.Contains('e', StringComparison.OrdinalIgnoreCase)) {
                    return withoutSuffix + ".0";
                }

                return withoutSuffix;
            }

            if ((normalizedLiteral.EndsWith("f", StringComparison.Ordinal) || normalizedLiteral.EndsWith("F", StringComparison.Ordinal)) &&
                !normalizedLiteral.Contains('.', StringComparison.Ordinal) &&
                !normalizedLiteral.Contains('e', StringComparison.OrdinalIgnoreCase)) {
                return normalizedLiteral[..^1] + ".0f";
            }

            return normalizedLiteral;
        }

        protected override void ProcessReturnStatement(SemanticModel semantic, LayerContext context, ReturnStatementSyntax ret, List<string> lines) {
            if (ret.Expression == null) {
                lines.Add("return;");
                return;
            }

            int start = context.Class.Count;
            List<string> returnLines = new List<string>();
            ExpressionSyntax returnExpression = ret.Expression is RefExpressionSyntax refExpression
                ? refExpression.Expression
                : ret.Expression;
            ExpressionResult result = ProcessExpression(semantic, context, returnExpression, returnLines);

            if (result.BeforeLines != null) {
                lines.AddRange(result.BeforeLines);
            }

            if (ShouldEmitEmptyStringForTargetedNullReturn(semantic, ret)) {
                lines.Add("return String::Empty;");
                context.PopClass(start);
                return;
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

        /// <summary>
        /// Returns whether one return statement should lower an explicit null literal to the managed empty-string singleton.
        /// </summary>
        /// <param name="semantic">Semantic model for the active document.</param>
        /// <param name="ret">Return statement being lowered.</param>
        /// <returns><c>true</c> when the return target is one string and the expression is one null literal; otherwise <c>false</c>.</returns>
        static bool ShouldEmitEmptyStringForTargetedNullReturn(SemanticModel semantic, ReturnStatementSyntax ret) {
            if (semantic == null ||
                ret?.Expression == null ||
                ret.Expression.Kind() != SyntaxKind.NullLiteralExpression) {
                return false;
            }

            ITypeSymbol returnTypeSymbol = ResolveEnclosingReturnTypeSymbol(semantic, ret);
            return IsStringTypeSymbol(returnTypeSymbol);
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

                ITypeSymbol inferredOutTypeSymbol = TryResolveOutArgumentTypeSymbol(semantic, declaration, null);
                VariableType variableType = inferredOutTypeSymbol != null
                    ? VariableUtil.GetVarType(inferredOutTypeSymbol)
                    : ResolveDeclarationExpressionVariableType(semantic, declaration, single);
                ConversionVariable conversionVariable = null;
                FunctionStack fn = context.GetCurrentFunction();
                if (fn != null) {
                    bool hasExistingTrackedVariable = fn.Stack.Any(candidate => candidate.Name == identifier);
                    conversionVariable = new ConversionVariable {
                        Name = identifier,
                        VarType = variableType,
                        Modifier = ParameterModifier.Out
                    };
                    if (hasExistingTrackedVariable) {
                        conversionVariable.Remap = GetScopedDeclarationExpressionIdentifier(identifier, declaration);
                    }

                    fn.Stack.Add(conversionVariable);
                }

                lines.Add(conversionVariable?.Remap ?? identifier);

                ExpressionResult result = new ExpressionResult(true, conversionVariable != null ? VariablePath.FunctionStack : VariablePath.Unknown, variableType);
                if (conversionVariable != null) {
                    result.Variable = conversionVariable;
                }

                return result;
            }

            if (declaration.Designation is DiscardDesignationSyntax) {
                lines.Add(GetDiscardTemporaryName(declaration));
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

        static string GetDiscardTemporaryName(SyntaxNode discardSyntax) {
            return $"__discard_{discardSyntax.SpanStart:X8}";
        }

        static string GetScopedDeclarationExpressionIdentifier(string identifier, SyntaxNode declarationSyntax) {
            return $"{identifier}_{declarationSyntax.SpanStart:X8}";
        }

        static string ResolveTrackedIdentifierEmissionName(LayerContext context, string identifier) {
            FunctionStack currentFunction = context?.GetCurrentFunction();
            ConversionVariable trackedVariable = currentFunction?.Stack.LastOrDefault(candidate => candidate.Name == identifier);
            return trackedVariable?.Remap ?? identifier;
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

        bool TryBuildPropertyGetterCall(
            SyntaxNode accessNode,
            IPropertySymbol propertySymbol,
            out string getterCallName) {
            getterCallName = string.Empty;

            if (accessNode == null ||
                propertySymbol == null ||
                propertySymbol.IsIndexer ||
                propertySymbol.GetMethod == null ||
                !ShouldLowerPropertyGetter(propertySymbol) ||
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

            if (!IsGeneratedContainingType(propertySymbol.ContainingType)) {
                getterCallName = $"get_{propertySymbol.Name}()";
                return true;
            }

            if (propertySymbol.DeclaringSyntaxReferences.Length == 0) {
                getterCallName = $"get_{propertySymbol.Name}()";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether one property read must lower through the generated getter instead of direct member access.
        /// </summary>
        /// <param name="propertySymbol">Property symbol being lowered.</param>
        /// <returns><c>true</c> when native output must call the getter; otherwise <c>false</c>.</returns>
        bool ShouldLowerPropertyGetter(IPropertySymbol propertySymbol) {
            if (propertySymbol == null ||
                propertySymbol.IsIndexer ||
                propertySymbol.GetMethod == null) {
                return false;
            }

            if (propertySymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                propertySymbol.IsAbstract) {
                return true;
            }

            if (!IsGeneratedContainingType(propertySymbol.ContainingType)) {
                return true;
            }

            return RequiresPropertyAccessorLowering(propertySymbol);
        }

        /// <summary>
        /// Determines whether one Roslyn containing type is emitted as part of the current conversion output.
        /// </summary>
        /// <param name="containingTypeSymbol">Containing type symbol to inspect.</param>
        /// <returns><c>true</c> when the type is generated in the current output; otherwise <c>false</c>.</returns>
        bool IsGeneratedContainingType(INamedTypeSymbol containingTypeSymbol) {
            if (containingTypeSymbol == null ||
                codeConverter?.Program == null) {
                return false;
            }

            return codeConverter.Program.FindGeneratedClass(containingTypeSymbol.Name, containingTypeSymbol.TypeArguments.Length) != null;
        }

        /// <summary>
        /// Attempts to resolve a property symbol directly from the receiver type when semantic binding returns a weaker symbol shape.
        /// </summary>
        /// <param name="semantic">Semantic model used to inspect the receiver expression.</param>
        /// <param name="receiverExpression">Receiver expression that owns the member access.</param>
        /// <param name="propertyName">Property name to resolve on the receiver type.</param>
        /// <param name="propertySymbol">Resolved property symbol when one is found.</param>
        /// <returns><c>true</c> when the receiver type exposes the requested property; otherwise <c>false</c>.</returns>
        static bool TryResolveReceiverPropertySymbol(
            SemanticModel semantic,
            ExpressionSyntax receiverExpression,
            string propertyName,
            out IPropertySymbol propertySymbol) {
            propertySymbol = null;

            if (semantic == null ||
                receiverExpression == null ||
                string.IsNullOrWhiteSpace(propertyName)) {
                return false;
            }

            ITypeSymbol receiverTypeSymbol = semantic.GetTypeInfo(receiverExpression).ConvertedType ?? semantic.GetTypeInfo(receiverExpression).Type;
            if (receiverTypeSymbol == null ||
                IsWeakRecoveredTypeSymbol(receiverTypeSymbol) &&
                !TryGetExpressionTypeSymbol(semantic, receiverExpression, out receiverTypeSymbol)) {
                return false;
            }

            propertySymbol = receiverTypeSymbol.GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();
            return propertySymbol != null;
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

            if (assignmentLeft is IdentifierNameSyntax assignmentIdentifier &&
                TryResolveDirectFieldSymbol(semantic, assignmentIdentifier)) {
                return false;
            }

            if (assignmentLeft is MemberAccessExpressionSyntax assignmentMemberAccess &&
                TryResolveDirectFieldSymbol(semantic, assignmentMemberAccess)) {
                return false;
            }

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

        static bool TryResolveDirectFieldSymbol(SemanticModel semantic, IdentifierNameSyntax identifierName) {
            if (semantic == null ||
                identifierName == null ||
                !ReferenceEquals(identifierName.SyntaxTree, semantic.SyntaxTree)) {
                return false;
            }

            try {
                return semantic.GetSymbolInfo(identifierName).Symbol is IFieldSymbol;
            } catch (ArgumentException) {
                return false;
            }
        }

        static bool TryResolveDirectFieldSymbol(SemanticModel semantic, MemberAccessExpressionSyntax memberAccess) {
            if (semantic == null ||
                memberAccess == null ||
                !ReferenceEquals(memberAccess.SyntaxTree, semantic.SyntaxTree)) {
                return false;
            }

            try {
                return ResolveMemberAccessSymbol(semantic, memberAccess) is IFieldSymbol;
            } catch (ArgumentException) {
                return false;
            }
        }

        bool TryAppendObjectInitializerSetterAssignment(
            SemanticModel semantic,
            LayerContext context,
            TypeSyntax objectCreationTypeSyntax,
            string objectName,
            string memberAccessOperator,
            AssignmentExpressionSyntax assignment,
            List<string> lines) {
            string propertyName = string.Empty;

            if (TryGetAssignedPropertySymbol(semantic, assignment.Left, out IPropertySymbol propertySymbol) &&
                propertySymbol.SetMethod != null) {
                propertyName = propertySymbol.Name;
            } else if (assignment.Left is IdentifierNameSyntax identifierName &&
                TryResolveGeneratedPropertySetter(semantic, objectCreationTypeSyntax, identifierName.Identifier.Text)) {
                propertyName = identifierName.Identifier.Text;
            }

            if (string.IsNullOrWhiteSpace(propertyName)) {
                return false;
            }

            int startRight = context.DepthClass;
            List<string> rightLines = new List<string>();
            ExpressionResult rightResult = ProcessExpression(semantic, context, assignment.Right, rightLines);
            context.PopClass(startRight);
            if (rightResult.BeforeLines != null && rightResult.BeforeLines.Count > 0) {
                lines.AddRange(rightResult.BeforeLines);
            }

            lines.Add(objectName);
            lines.Add(memberAccessOperator);
            lines.Add($"set_{propertyName}(");
            lines.AddRange(rightLines);

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
            if (semantic == null || receiverTypeSyntax == null) {
                return false;
            }

            ITypeSymbol receiverTypeSymbol;
            if (!ReferenceEquals(receiverTypeSyntax.SyntaxTree, semantic.SyntaxTree)) {
                receiverTypeSymbol = ResolveObjectCreationTypeSymbolFromText(semantic, receiverTypeSyntax.ToString());
            } else {
                receiverTypeSymbol = semantic.GetTypeInfo(receiverTypeSyntax).Type ?? semantic.GetTypeInfo(receiverTypeSyntax).ConvertedType;
            }

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
