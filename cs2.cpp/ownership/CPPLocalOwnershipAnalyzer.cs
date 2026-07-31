using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace cs2.cpp;

/// <summary>
/// Analyzes native pointer ownership for source locals and produces an emission plan before C++ lowering.
/// </summary>
public sealed class CPPLocalOwnershipAnalyzer {
    /// <summary>
    /// Classifies initializer and call-result expressions from resolved semantic contracts.
    /// </summary>
    readonly CPPOwnershipExpressionClassifier ExpressionClassifier;

    /// <summary>
    /// Creates source-located hard errors for invalid ownership operations.
    /// </summary>
    readonly CPPOwnershipDiagnosticFactory DiagnosticFactory;

    /// <summary>
    /// Initializes an analyzer with the standard expression classifier and diagnostic factory.
    /// </summary>
    public CPPLocalOwnershipAnalyzer()
        : this(new CPPOwnershipExpressionClassifier(), new CPPOwnershipDiagnosticFactory()) {
    }

    /// <summary>
    /// Initializes an analyzer with explicit semantic collaborators.
    /// </summary>
    /// <param name="expressionClassifier">Classifier used for initializer ownership.</param>
    /// <param name="diagnosticFactory">Factory used for source-located ownership errors.</param>
    public CPPLocalOwnershipAnalyzer(
        CPPOwnershipExpressionClassifier expressionClassifier,
        CPPOwnershipDiagnosticFactory diagnosticFactory) {
        ExpressionClassifier = expressionClassifier ?? throw new ArgumentNullException(nameof(expressionClassifier));
        DiagnosticFactory = diagnosticFactory ?? throw new ArgumentNullException(nameof(diagnosticFactory));
    }

    /// <summary>
    /// Analyzes all source method bodies in the supplied compilations and creates deterministic local lifetime plans.
    /// </summary>
    /// <param name="compilations">Roslyn compilations participating in one generated native program.</param>
    /// <param name="summaries">Previously resolved method return and parameter ownership contracts.</param>
    /// <returns>Local plans, ownership transitions, and any hard semantic errors.</returns>
    public CPPOwnershipAnalysisResult Analyze(
        IReadOnlyList<Compilation> compilations,
        CPPMethodOwnershipSummaryResolution summaries) {
        if (compilations == null) {
            throw new ArgumentNullException(nameof(compilations));
        }
        if (summaries == null) {
            throw new ArgumentNullException(nameof(summaries));
        }

        Dictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans = [];
        List<CPPOwnershipTransition> transitions = [];
        List<CPPConversionDiagnostic> diagnostics = [];
        foreach (Compilation compilation in compilations) {
            AnalyzeCompilation(compilation, summaries, localPlans, transitions, diagnostics);
        }

        return new CPPOwnershipAnalysisResult(
            summaries,
            new CPPOwnershipEmissionPlan(localPlans, transitions),
            diagnostics);
    }

    /// <summary>
    /// Analyzes every executable method declaration in one compilation.
    /// </summary>
    /// <param name="compilation">Compilation containing the source methods.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="localPlans">Mutable aggregate of local emission plans.</param>
    /// <param name="transitions">Mutable aggregate of ownership transitions.</param>
    /// <param name="diagnostics">Mutable aggregate of hard ownership errors.</param>
    void AnalyzeCompilation(
        Compilation compilation,
        CPPMethodOwnershipSummaryResolution summaries,
        IDictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (compilation == null) {
            throw new ArgumentException("Ownership analysis cannot contain a null compilation.", nameof(compilation));
        }

        foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
            SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (MethodDeclarationSyntax methodDeclaration in syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()) {
                AnalyzeMethod(methodDeclaration, semanticModel, summaries, localPlans, transitions, diagnostics);
            }
        }
    }

    /// <summary>
    /// Walks reachable operations for one method and tracks each ownership-relevant local through its lifecycle.
    /// </summary>
    /// <param name="methodDeclaration">Source method to analyze.</param>
    /// <param name="semanticModel">Semantic model for the method source tree.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="localPlans">Mutable aggregate of local emission plans.</param>
    /// <param name="transitions">Mutable aggregate of ownership transitions.</param>
    /// <param name="diagnostics">Mutable aggregate of hard ownership errors.</param>
    void AnalyzeMethod(
        MethodDeclarationSyntax methodDeclaration,
        SemanticModel semanticModel,
        CPPMethodOwnershipSummaryResolution summaries,
        IDictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (methodDeclaration.Body == null) {
            return;
        }

        ControlFlowGraph controlFlowGraph = ControlFlowGraph.Create(methodDeclaration, semanticModel);
        IMethodSymbol method = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
        if (method == null) {
            return;
        }
        Dictionary<ILocalSymbol, CPPOwnershipKind> ownership = new(SymbolEqualityComparer.Default);
        Dictionary<ILocalSymbol, CPPOwnershipLifecycle> lifecycle = new(SymbolEqualityComparer.Default);
        Dictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations = new(SymbolEqualityComparer.Default);
        foreach (SyntaxNode syntax in GetReachableOwnershipSyntax(controlFlowGraph)) {
            if (syntax is VariableDeclaratorSyntax variableDeclaration) {
                AnalyzeDeclaration(variableDeclaration, semanticModel, summaries, ownership, lifecycle, declarations, localPlans, transitions);
            } else if (syntax is InvocationExpressionSyntax invocation) {
                AnalyzeInvocation(invocation, semanticModel, method, summaries, ownership, lifecycle, declarations, transitions, diagnostics);
            } else if (syntax is AssignmentExpressionSyntax assignment) {
                AnalyzeAssignment(assignment, semanticModel, method, ownership, lifecycle, diagnostics);
            } else if (syntax is ReturnStatementSyntax returnStatement) {
                AnalyzeReturn(returnStatement, semanticModel, method, summaries, ownership, lifecycle, declarations, transitions, diagnostics);
            }
        }

        foreach (KeyValuePair<ILocalSymbol, CPPOwnershipKind> localOwnership in ownership) {
            if (localOwnership.Value != CPPOwnershipKind.Owned ||
                lifecycle[localOwnership.Key] != CPPOwnershipLifecycle.Live) {
                continue;
            }

            VariableDeclaratorSyntax declaration = declarations[localOwnership.Key];
            transitions.Add(new CPPOwnershipTransition(
                declaration,
                declaration,
                CPPOwnershipTransitionKind.ScopeCleanup,
                CPPOwnershipKind.Owned,
                CPPOwnershipLifecycle.ScopeCleanup));
        }
    }

    /// <summary>
    /// Collects ownership-relevant syntax represented by reachable control-flow blocks in source order.
    /// </summary>
    /// <param name="controlFlowGraph">Roslyn control-flow graph for one method body.</param>
    /// <returns>Unique reachable declarations, calls, assignments, and returns in source order.</returns>
    static IReadOnlyList<SyntaxNode> GetReachableOwnershipSyntax(ControlFlowGraph controlFlowGraph) {
        HashSet<SyntaxNode> syntaxValues = [];
        foreach (BasicBlock block in controlFlowGraph.Blocks) {
            if (!block.IsReachable) {
                continue;
            }

            foreach (IOperation operation in block.Operations) {
                AddOwnershipSyntax(operation.Syntax, syntaxValues);
            }
            if (block.BranchValue != null) {
                AddOwnershipSyntax(block.BranchValue.Syntax, syntaxValues);
                ReturnStatementSyntax returnStatement = block.BranchValue.Syntax
                    .AncestorsAndSelf()
                    .OfType<ReturnStatementSyntax>()
                    .FirstOrDefault();
                if (returnStatement != null) {
                    syntaxValues.Add(returnStatement);
                }
            }
        }

        return syntaxValues.OrderBy(syntax => syntax.SpanStart)
            .ThenByDescending(syntax => syntax.Span.Length)
            .ToArray();
    }

    /// <summary>
    /// Adds ownership-relevant syntax under one reachable operation root.
    /// </summary>
    /// <param name="root">Reachable syntax root.</param>
    /// <param name="syntaxValues">Set receiving exact syntax nodes.</param>
    static void AddOwnershipSyntax(SyntaxNode root, ISet<SyntaxNode> syntaxValues) {
        foreach (SyntaxNode syntax in root.DescendantNodesAndSelf()) {
            if (syntax is VariableDeclaratorSyntax ||
                syntax is InvocationExpressionSyntax ||
                syntax is AssignmentExpressionSyntax ||
                syntax is ReturnStatementSyntax) {
                syntaxValues.Add(syntax);
            }
        }
    }

    /// <summary>
    /// Classifies one local initializer and creates its cleanup plan when native ownership is known.
    /// </summary>
    /// <param name="declaration">Local declaration to classify.</param>
    /// <param name="semanticModel">Semantic model for the declaration.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="ownership">Current ownership state by local.</param>
    /// <param name="lifecycle">Current lifecycle by local.</param>
    /// <param name="declarations">Source declaration by local.</param>
    /// <param name="localPlans">Aggregate local emission plans.</param>
    /// <param name="transitions">Aggregate ownership transitions.</param>
    void AnalyzeDeclaration(
        VariableDeclaratorSyntax declaration,
        SemanticModel semanticModel,
        CPPMethodOwnershipSummaryResolution summaries,
        IDictionary<ILocalSymbol, CPPOwnershipKind> ownership,
        IDictionary<ILocalSymbol, CPPOwnershipLifecycle> lifecycle,
        IDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IDictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans,
        ICollection<CPPOwnershipTransition> transitions) {
        if (declaration.Initializer == null) {
            return;
        }

        ILocalSymbol local = semanticModel.GetDeclaredSymbol(declaration) as ILocalSymbol;
        IOperation initializer = semanticModel.GetOperation(declaration.Initializer.Value);
        CPPOwnershipKind initialOwnership = ExpressionClassifier.Classify(initializer, summaries.Summaries);
        if (local == null || initialOwnership == CPPOwnershipKind.Unknown) {
            return;
        }

        ownership[local] = initialOwnership;
        lifecycle[local] = CPPOwnershipLifecycle.Live;
        declarations[local] = declaration;
        bool requiresScopeGuard = initialOwnership == CPPOwnershipKind.Owned;
        CPPLocalOwnershipPlan plan = new(
            declaration,
            initialOwnership,
            CreateOwnershipFlagName(declaration),
            requiresScopeGuard);
        localPlans[declaration] = plan;
        if (requiresScopeGuard) {
            transitions.Add(new CPPOwnershipTransition(
                declaration,
                declaration,
                CPPOwnershipTransitionKind.Acquire,
                CPPOwnershipKind.Owned,
                CPPOwnershipLifecycle.Live));
        }
    }

    /// <summary>
    /// Validates invocation uses and applies explicit release or parameter-transfer contracts.
    /// </summary>
    /// <param name="invocationSyntax">Invocation source syntax.</param>
    /// <param name="semanticModel">Semantic model for the invocation.</param>
    /// <param name="containingMethod">Method containing the invocation.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="ownership">Current ownership state by local.</param>
    /// <param name="lifecycle">Current lifecycle by local.</param>
    /// <param name="declarations">Source declaration by local.</param>
    /// <param name="transitions">Aggregate ownership transitions.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void AnalyzeInvocation(
        InvocationExpressionSyntax invocationSyntax,
        SemanticModel semanticModel,
        IMethodSymbol containingMethod,
        CPPMethodOwnershipSummaryResolution summaries,
        IDictionary<ILocalSymbol, CPPOwnershipKind> ownership,
        IDictionary<ILocalSymbol, CPPOwnershipLifecycle> lifecycle,
        IDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        IInvocationOperation invocation = semanticModel.GetOperation(invocationSyntax) as IInvocationOperation;
        if (invocation == null) {
            return;
        }

        ValidateDeadLocalUses(invocation, containingMethod, lifecycle, diagnostics);
        if (IsNativeRelease(invocation.TargetMethod)) {
            AnalyzeRelease(invocation, containingMethod, ownership, lifecycle, declarations, transitions, diagnostics);
            return;
        }

        CPPMethodOwnershipSummary targetSummary = ResolveSummary(invocation.TargetMethod, summaries);
        foreach (IArgumentOperation argument in invocation.Arguments) {
            ILocalReferenceOperation localReference = UnwrapLocalReference(argument.Value);
            if (localReference == null ||
                !ownership.TryGetValue(localReference.Local, out CPPOwnershipKind argumentOwnership) ||
                argumentOwnership != CPPOwnershipKind.Owned ||
                lifecycle[localReference.Local] != CPPOwnershipLifecycle.Live) {
                continue;
            }

            CPPParameterOwnershipKind parameterOwnership = ResolveParameterOwnership(
                invocation.TargetMethod,
                argument.Parameter,
                targetSummary);
            if (parameterOwnership == CPPParameterOwnershipKind.TakesOwnership) {
                lifecycle[localReference.Local] = CPPOwnershipLifecycle.Transferred;
                transitions.Add(new CPPOwnershipTransition(
                    invocationSyntax,
                    declarations[localReference.Local],
                    CPPOwnershipTransitionKind.Transfer,
                    CPPOwnershipKind.Owned,
                    CPPOwnershipLifecycle.Transferred));
            } else if (parameterOwnership == CPPParameterOwnershipKind.Unknown) {
                diagnostics.Add(DiagnosticFactory.Create(
                    "CPPOWN001",
                    argument.Syntax,
                    containingMethod,
                    $"Owned local '{localReference.Local.Name}' crosses parameter '{argument.Parameter?.Name}' without a native ownership contract.",
                    "Mark the parameter as no-escape or takes-ownership, or pass borrowed storage instead."));
            }
        }
    }

    /// <summary>
    /// Applies explicit native release semantics to the local passed to a cleanup helper.
    /// </summary>
    /// <param name="invocation">Native cleanup invocation.</param>
    /// <param name="containingMethod">Method containing the cleanup.</param>
    /// <param name="ownership">Current ownership state by local.</param>
    /// <param name="lifecycle">Current lifecycle by local.</param>
    /// <param name="declarations">Source declaration by local.</param>
    /// <param name="transitions">Aggregate ownership transitions.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void AnalyzeRelease(
        IInvocationOperation invocation,
        IMethodSymbol containingMethod,
        IDictionary<ILocalSymbol, CPPOwnershipKind> ownership,
        IDictionary<ILocalSymbol, CPPOwnershipLifecycle> lifecycle,
        IDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (invocation.Arguments.Length == 0) {
            return;
        }

        ILocalReferenceOperation localReference = UnwrapLocalReference(invocation.Arguments[0].Value);
        if (localReference == null || !ownership.TryGetValue(localReference.Local, out CPPOwnershipKind localOwnership)) {
            return;
        }
        if (localOwnership == CPPOwnershipKind.Borrowed) {
            diagnostics.Add(DiagnosticFactory.Create(
                "CPPOWN003",
                invocation.Syntax,
                containingMethod,
                $"Borrowed local '{localReference.Local.Name}' cannot be released by this scope.",
                "Remove the cleanup call or establish an owned value before releasing it."));
            return;
        }
        if (lifecycle[localReference.Local] != CPPOwnershipLifecycle.Live) {
            return;
        }

        lifecycle[localReference.Local] = CPPOwnershipLifecycle.Released;
        transitions.Add(new CPPOwnershipTransition(
            invocation.Syntax,
            declarations[localReference.Local],
            CPPOwnershipTransitionKind.Release,
            CPPOwnershipKind.Owned,
            CPPOwnershipLifecycle.Released));
    }

    /// <summary>
    /// Rejects assigning a live owned local into an ordinary field or property without an owned-member contract.
    /// </summary>
    /// <param name="assignmentSyntax">Assignment source syntax.</param>
    /// <param name="semanticModel">Semantic model for the assignment.</param>
    /// <param name="containingMethod">Method containing the assignment.</param>
    /// <param name="ownership">Current ownership state by local.</param>
    /// <param name="lifecycle">Current lifecycle by local.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void AnalyzeAssignment(
        AssignmentExpressionSyntax assignmentSyntax,
        SemanticModel semanticModel,
        IMethodSymbol containingMethod,
        IDictionary<ILocalSymbol, CPPOwnershipKind> ownership,
        IDictionary<ILocalSymbol, CPPOwnershipLifecycle> lifecycle,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        IAssignmentOperation assignment = semanticModel.GetOperation(assignmentSyntax) as IAssignmentOperation;
        if (assignment == null) {
            return;
        }

        ValidateDeadLocalUses(assignment.Value, containingMethod, lifecycle, diagnostics);
        ILocalReferenceOperation localReference = UnwrapLocalReference(assignment.Value);
        if (localReference == null ||
            !ownership.TryGetValue(localReference.Local, out CPPOwnershipKind localOwnership) ||
            localOwnership != CPPOwnershipKind.Owned ||
            lifecycle[localReference.Local] != CPPOwnershipLifecycle.Live ||
            !IsOrdinaryMemberTarget(assignment.Target)) {
            return;
        }

        diagnostics.Add(DiagnosticFactory.Create(
            "CPPOWN002",
            assignmentSyntax,
            containingMethod,
            $"Owned local '{localReference.Local.Name}' escapes into a member without an owned-member contract.",
            "Mark the destination member as native-owned or transfer through an explicitly contracted API."));
    }

    /// <summary>
    /// Transfers a live owned local returned from a method whose resolved return contract is owned.
    /// </summary>
    /// <param name="returnSyntax">Return statement source syntax.</param>
    /// <param name="semanticModel">Semantic model for the return.</param>
    /// <param name="containingMethod">Method containing the return.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="ownership">Current ownership state by local.</param>
    /// <param name="lifecycle">Current lifecycle by local.</param>
    /// <param name="declarations">Source declaration by local.</param>
    /// <param name="transitions">Aggregate ownership transitions.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void AnalyzeReturn(
        ReturnStatementSyntax returnSyntax,
        SemanticModel semanticModel,
        IMethodSymbol containingMethod,
        CPPMethodOwnershipSummaryResolution summaries,
        IDictionary<ILocalSymbol, CPPOwnershipKind> ownership,
        IDictionary<ILocalSymbol, CPPOwnershipLifecycle> lifecycle,
        IDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        IReturnOperation returnOperation = semanticModel.GetOperation(returnSyntax) as IReturnOperation;
        if (returnOperation?.ReturnedValue == null) {
            return;
        }

        ValidateDeadLocalUses(returnOperation.ReturnedValue, containingMethod, lifecycle, diagnostics);
        ILocalReferenceOperation localReference = UnwrapLocalReference(returnOperation.ReturnedValue);
        CPPMethodOwnershipSummary methodSummary = ResolveSummary(containingMethod, summaries);
        if (localReference == null ||
            methodSummary?.ReturnOwnership != CPPOwnershipKind.Owned ||
            !ownership.TryGetValue(localReference.Local, out CPPOwnershipKind localOwnership) ||
            localOwnership != CPPOwnershipKind.Owned ||
            lifecycle[localReference.Local] != CPPOwnershipLifecycle.Live) {
            return;
        }

        lifecycle[localReference.Local] = CPPOwnershipLifecycle.Transferred;
        transitions.Add(new CPPOwnershipTransition(
            returnSyntax,
            declarations[localReference.Local],
            CPPOwnershipTransitionKind.Transfer,
            CPPOwnershipKind.Owned,
            CPPOwnershipLifecycle.Transferred));
    }

    /// <summary>
    /// Reports references to locals whose lifetime already ended through release or transfer.
    /// </summary>
    /// <param name="operation">Operation whose local references should be validated.</param>
    /// <param name="containingMethod">Method containing the operation.</param>
    /// <param name="lifecycle">Current lifecycle by local.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void ValidateDeadLocalUses(
        IOperation operation,
        IMethodSymbol containingMethod,
        IDictionary<ILocalSymbol, CPPOwnershipLifecycle> lifecycle,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        foreach (ILocalReferenceOperation localReference in operation.DescendantsAndSelf().OfType<ILocalReferenceOperation>()) {
            if (!lifecycle.TryGetValue(localReference.Local, out CPPOwnershipLifecycle localLifecycle) ||
                localLifecycle == CPPOwnershipLifecycle.Live) {
                continue;
            }

            diagnostics.Add(DiagnosticFactory.Create(
                "CPPOWN004",
                localReference.Syntax,
                containingMethod,
                $"Local '{localReference.Local.Name}' is used after its native lifetime became {localLifecycle.ToString().ToLowerInvariant()}.",
                "Move the use before the release or transfer, or assign a new owned value before using the local."));
        }
    }

    /// <summary>
    /// Resolves one method summary when the target participates in ownership analysis.
    /// </summary>
    /// <param name="method">Target method.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <returns>The target summary, or null for an unreviewed external method.</returns>
    static CPPMethodOwnershipSummary ResolveSummary(
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries) {
        string methodKey = CPPMethodOwnershipKey.Create(method);
        summaries.Summaries.TryGetValue(methodKey, out CPPMethodOwnershipSummary summary);
        return summary;
    }

    /// <summary>
    /// Resolves how one target parameter treats native ownership.
    /// </summary>
    /// <param name="method">Invoked method.</param>
    /// <param name="parameter">Parameter receiving the argument.</param>
    /// <param name="summary">Resolved method summary when available.</param>
    /// <returns>The verified parameter ownership behavior.</returns>
    static CPPParameterOwnershipKind ResolveParameterOwnership(
        IMethodSymbol method,
        IParameterSymbol parameter,
        CPPMethodOwnershipSummary summary) {
        if (parameter == null) {
            return CPPParameterOwnershipKind.Unknown;
        }
        if (HasAttribute(parameter, "NativeTakesOwnership")) {
            return CPPParameterOwnershipKind.TakesOwnership;
        }
        if (summary != null) {
            return summary.GetParameterOwnership(parameter.Ordinal);
        }

        return method.DeclaringSyntaxReferences.Length > 0
            ? CPPParameterOwnershipKind.NoEscape
            : CPPParameterOwnershipKind.Unknown;
    }

    /// <summary>
    /// Determines whether one method is an explicit generated-native cleanup helper.
    /// </summary>
    /// <param name="method">Invoked method to inspect.</param>
    /// <returns><c>true</c> for supported native delete or release helpers.</returns>
    static bool IsNativeRelease(IMethodSymbol method) {
        return string.Equals(method.ContainingType?.Name, "NativeOwnership", StringComparison.Ordinal) &&
            (string.Equals(method.Name, "Delete", StringComparison.Ordinal) ||
             string.Equals(method.Name, "Release", StringComparison.Ordinal));
    }

    /// <summary>
    /// Determines whether an assignment destination is an ordinary field or property.
    /// </summary>
    /// <param name="target">Assignment destination operation.</param>
    /// <returns><c>true</c> when the target can retain a value beyond the method call.</returns>
    static bool IsOrdinaryMemberTarget(IOperation target) {
        return target is IFieldReferenceOperation || target is IPropertyReferenceOperation;
    }

    /// <summary>
    /// Removes transparent conversions and parentheses to resolve a direct local reference.
    /// </summary>
    /// <param name="operation">Value operation to unwrap.</param>
    /// <returns>The direct local reference, or null when the value is not one local.</returns>
    static ILocalReferenceOperation UnwrapLocalReference(IOperation operation) {
        while (operation is IConversionOperation conversion) {
            operation = conversion.Operand;
        }
        while (operation is IParenthesizedOperation parenthesized) {
            operation = parenthesized.Operand;
        }

        return operation as ILocalReferenceOperation;
    }

    /// <summary>
    /// Creates a deterministic generated C++ flag identifier for one source local declaration.
    /// </summary>
    /// <param name="declaration">Source local declaration.</param>
    /// <returns>A stable identifier containing the sanitized local name and source offset.</returns>
    static string CreateOwnershipFlagName(VariableDeclaratorSyntax declaration) {
        string sanitizedName = new string(declaration.Identifier.Text
            .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
            .ToArray());
        return $"__owns_{sanitizedName}_{declaration.SpanStart:X8}";
    }

    /// <summary>
    /// Determines whether one symbol carries an ownership contract name with or without the attribute suffix.
    /// </summary>
    /// <param name="symbol">Symbol whose attributes should be inspected.</param>
    /// <param name="contractName">Contract name without the conventional suffix.</param>
    /// <returns><c>true</c> when the contract is present.</returns>
    static bool HasAttribute(ISymbol symbol, string contractName) {
        foreach (AttributeData attribute in symbol.GetAttributes()) {
            string attributeName = attribute.AttributeClass?.Name ?? string.Empty;
            if (string.Equals(attributeName, contractName, StringComparison.Ordinal) ||
                string.Equals(attributeName, contractName + "Attribute", StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }
}
