using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace cs2.cpp;

/// <summary>
/// Propagates local native ownership through one Roslyn control-flow graph and records deterministic emission actions.
/// </summary>
public sealed class CPPMethodControlFlowOwnershipAnalyzer {
    /// <summary>
    /// Classifies native ownership of source expressions.
    /// </summary>
    readonly CPPOwnershipExpressionClassifier ExpressionClassifier;

    /// <summary>
    /// Creates source-located ownership diagnostics.
    /// </summary>
    readonly CPPOwnershipDiagnosticFactory DiagnosticFactory;

    /// <summary>
    /// Conservatively combines predecessor ownership states.
    /// </summary>
    readonly CPPOwnershipStateMerger StateMerger;

    /// <summary>
    /// Provides reviewed parameter contracts for framework methods without source summaries.
    /// </summary>
    readonly CPPIntrinsicOwnershipCatalog IntrinsicCatalog;

    /// <summary>
    /// Initializes one method analyzer with explicit semantic collaborators.
    /// </summary>
    /// <param name="expressionClassifier">Classifier used for initializer and replacement expressions.</param>
    /// <param name="diagnosticFactory">Factory used for source-located hard errors.</param>
    public CPPMethodControlFlowOwnershipAnalyzer(
        CPPOwnershipExpressionClassifier expressionClassifier,
        CPPOwnershipDiagnosticFactory diagnosticFactory)
        : this(
            expressionClassifier,
            diagnosticFactory,
            new CPPOwnershipStateMerger(),
            new CPPIntrinsicOwnershipCatalog()) {
    }

    /// <summary>
    /// Initializes one method analyzer with an explicit state merger.
    /// </summary>
    /// <param name="expressionClassifier">Classifier used for initializer and replacement expressions.</param>
    /// <param name="diagnosticFactory">Factory used for source-located hard errors.</param>
    /// <param name="stateMerger">Merger used at control-flow joins.</param>
    public CPPMethodControlFlowOwnershipAnalyzer(
        CPPOwnershipExpressionClassifier expressionClassifier,
        CPPOwnershipDiagnosticFactory diagnosticFactory,
        CPPOwnershipStateMerger stateMerger)
        : this(
            expressionClassifier,
            diagnosticFactory,
            stateMerger,
            new CPPIntrinsicOwnershipCatalog()) {
    }

    /// <summary>
    /// Initializes one method analyzer with explicit state and framework ownership collaborators.
    /// </summary>
    /// <param name="expressionClassifier">Classifier used for initializer and replacement expressions.</param>
    /// <param name="diagnosticFactory">Factory used for source-located hard errors.</param>
    /// <param name="stateMerger">Merger used at control-flow joins.</param>
    /// <param name="intrinsicCatalog">Reviewed framework parameter ownership contracts.</param>
    public CPPMethodControlFlowOwnershipAnalyzer(
        CPPOwnershipExpressionClassifier expressionClassifier,
        CPPOwnershipDiagnosticFactory diagnosticFactory,
        CPPOwnershipStateMerger stateMerger,
        CPPIntrinsicOwnershipCatalog intrinsicCatalog) {
        ExpressionClassifier = expressionClassifier ?? throw new ArgumentNullException(nameof(expressionClassifier));
        DiagnosticFactory = diagnosticFactory ?? throw new ArgumentNullException(nameof(diagnosticFactory));
        StateMerger = stateMerger ?? throw new ArgumentNullException(nameof(stateMerger));
        IntrinsicCatalog = intrinsicCatalog ?? throw new ArgumentNullException(nameof(intrinsicCatalog));
    }

    /// <summary>
    /// Analyzes one method graph to a fixed point and appends its plans, transitions, and hard errors.
    /// </summary>
    /// <param name="methodDeclaration">Source declaration of the method.</param>
    /// <param name="semanticModel">Semantic model for the source tree.</param>
    /// <param name="controlFlowGraph">Roslyn control-flow graph for the method.</param>
    /// <param name="method">Resolved method symbol.</param>
    /// <param name="summaries">Resolved method ownership contracts.</param>
    /// <param name="localPlans">Aggregate local emission plans.</param>
    /// <param name="transitions">Aggregate ownership transitions.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    public void Analyze(
        SyntaxNode methodDeclaration,
        SemanticModel semanticModel,
        ControlFlowGraph controlFlowGraph,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IDictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        Dictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations = DiscoverLocalPlans(
            methodDeclaration,
            semanticModel,
            summaries,
            localPlans);
        Dictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal = CreatePlansByLocal(
            declarations,
            localPlans);

        Dictionary<BasicBlock, Dictionary<ILocalSymbol, CPPLocalOwnershipState>> outputStates = ResolveOutputStates(
            controlFlowGraph,
            semanticModel,
            method,
            summaries,
            plansByLocal);
        foreach (BasicBlock block in controlFlowGraph.Blocks) {
            if (!block.IsReachable || !CanResolveInput(block, outputStates)) {
                continue;
            }

            Dictionary<ILocalSymbol, CPPLocalOwnershipState> state = ResolveInputState(
                controlFlowGraph,
                block,
                outputStates,
                plansByLocal,
                semanticModel,
                method,
                summaries,
                declarations,
                true,
                transitions,
                diagnostics);
            ReportAmbiguousJoin(block, methodDeclaration, semanticModel, method, state, diagnostics);
            ProcessBlock(
                block,
                semanticModel,
                method,
                summaries,
                declarations,
                plansByLocal,
                state,
                true,
                transitions,
                diagnostics);
        }

        ValidateCaptures(
            methodDeclaration,
            semanticModel,
            controlFlowGraph,
            method,
            summaries,
            declarations,
            plansByLocal,
            outputStates,
            diagnostics);
        AddScopeCleanupTransitions(controlFlowGraph, declarations, outputStates, transitions);
    }

    /// <summary>
    /// Discovers ownership-relevant source locals and creates stable declaration plans before graph propagation.
    /// </summary>
    /// <param name="methodDeclaration">Method containing the declarations.</param>
    /// <param name="semanticModel">Semantic model for initializer classification.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="localPlans">Aggregate local emission plans.</param>
    /// <returns>Source declarations keyed by local symbol.</returns>
    Dictionary<ILocalSymbol, VariableDeclaratorSyntax> DiscoverLocalPlans(
        SyntaxNode methodDeclaration,
        SemanticModel semanticModel,
        CPPMethodOwnershipSummaryResolution summaries,
        IDictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans) {
        Dictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations = new(SymbolEqualityComparer.Default);
        foreach (VariableDeclaratorSyntax declaration in methodDeclaration.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()) {
            if (IsInsideNestedExecutable(declaration, methodDeclaration)) {
                continue;
            }

            ILocalSymbol local = semanticModel.GetDeclaredSymbol(declaration) as ILocalSymbol;
            if (local == null) {
                continue;
            }
            if (!CPPOwnershipTypeClassifier.RequiresClassification(local.Type)) {
                continue;
            }

            CPPOwnershipKind initialOwnership = declaration.Initializer == null
                ? CPPOwnershipKind.Unknown
                : ExpressionClassifier.Classify(
                    semanticModel.GetOperation(declaration.Initializer.Value),
                    summaries.Summaries);
            bool initiallyOwnsValue = initialOwnership == CPPOwnershipKind.Owned;
            bool gainsOwnedValue = initiallyOwnsValue || HasOwnedReplacement(
                methodDeclaration,
                semanticModel,
                local,
                summaries);
            if (initialOwnership == CPPOwnershipKind.Unknown && !gainsOwnedValue) {
                continue;
            }

            CPPOwnershipKind planOwnership = gainsOwnedValue
                ? CPPOwnershipKind.Owned
                : initialOwnership;
            CPPLocalOwnershipPlan plan = new(
                declaration,
                planOwnership,
                CreateOwnershipFlagName(declaration),
                gainsOwnedValue,
                initiallyOwnsValue);
            declarations[local] = declaration;
            localPlans[declaration] = plan;
        }

        return declarations;
    }

    /// <summary>
    /// Determines whether a local receives an owned replacement after its declaration.
    /// </summary>
    /// <param name="methodDeclaration">Method containing candidate assignments.</param>
    /// <param name="semanticModel">Semantic model for assignment classification.</param>
    /// <param name="local">Local whose replacements should be inspected.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <returns><c>true</c> when at least one assignment produces a fresh owned value.</returns>
    bool HasOwnedReplacement(
        SyntaxNode methodDeclaration,
        SemanticModel semanticModel,
        ILocalSymbol local,
        CPPMethodOwnershipSummaryResolution summaries) {
        foreach (AssignmentExpressionSyntax assignment in methodDeclaration.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()) {
            if (IsInsideNestedExecutable(assignment, methodDeclaration)) {
                continue;
            }

            IAssignmentOperation operation = semanticModel.GetOperation(assignment) as IAssignmentOperation;
            ILocalReferenceOperation targetLocal = operation?.Target as ILocalReferenceOperation;
            if (targetLocal != null &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                ExpressionClassifier.Classify(operation.Value, summaries.Summaries) == CPPOwnershipKind.Owned) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates local-symbol lookup for previously discovered declaration plans.
    /// </summary>
    /// <param name="declarations">Source declarations keyed by local symbol.</param>
    /// <param name="localPlans">Aggregate plans keyed by exact declaration syntax.</param>
    /// <returns>Plans keyed by their Roslyn local symbols.</returns>
    static Dictionary<ILocalSymbol, CPPLocalOwnershipPlan> CreatePlansByLocal(
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IDictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans) {
        Dictionary<ILocalSymbol, CPPLocalOwnershipPlan> result = new(SymbolEqualityComparer.Default);
        foreach (KeyValuePair<ILocalSymbol, VariableDeclaratorSyntax> declaration in declarations) {
            result[declaration.Key] = localPlans[declaration.Value];
        }

        return result;
    }

    /// <summary>
    /// Iterates the graph until every reachable block has a stable outgoing ownership state.
    /// </summary>
    /// <param name="controlFlowGraph">Method control-flow graph.</param>
    /// <param name="semanticModel">Semantic model for graph operations.</param>
    /// <param name="method">Containing method symbol.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="plansByLocal">Ownership plans keyed by local symbol.</param>
    /// <returns>Stable outgoing state for each reachable block.</returns>
    Dictionary<BasicBlock, Dictionary<ILocalSymbol, CPPLocalOwnershipState>> ResolveOutputStates(
        ControlFlowGraph controlFlowGraph,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal) {
        Dictionary<BasicBlock, Dictionary<ILocalSymbol, CPPLocalOwnershipState>> outputStates = [];
        int iterationLimit = Math.Max(8, controlFlowGraph.Blocks.Length * 8);
        for (int iteration = 0; iteration < iterationLimit; iteration++) {
            bool changed = false;
            foreach (BasicBlock block in controlFlowGraph.Blocks) {
                if (!block.IsReachable || !CanResolveInput(block, outputStates)) {
                    continue;
                }

                Dictionary<ILocalSymbol, CPPLocalOwnershipState> state = ResolveInputState(
                    controlFlowGraph,
                    block,
                    outputStates,
                    plansByLocal,
                    semanticModel,
                    method,
                    summaries,
                    null,
                    false,
                    null,
                    null);
                ProcessBlock(
                    block,
                    semanticModel,
                    method,
                    summaries,
                    null,
                    plansByLocal,
                    state,
                    false,
                    null,
                    null);
                if (!outputStates.TryGetValue(block, out Dictionary<ILocalSymbol, CPPLocalOwnershipState> priorState) ||
                    !StatesEqual(priorState, state)) {
                    outputStates[block] = CloneState(state);
                    changed = true;
                }
            }

            if (!changed) {
                return outputStates;
            }
        }

        throw new InvalidOperationException("Native ownership control-flow analysis did not converge.");
    }

    /// <summary>
    /// Determines whether enough predecessor information exists to evaluate one block.
    /// </summary>
    /// <param name="block">Block whose input should be checked.</param>
    /// <param name="outputStates">Known predecessor outputs.</param>
    /// <returns><c>true</c> for entry blocks or when at least one reachable predecessor has an output.</returns>
    static bool CanResolveInput(
        BasicBlock block,
        IReadOnlyDictionary<BasicBlock, Dictionary<ILocalSymbol, CPPLocalOwnershipState>> outputStates) {
        return block.Kind == BasicBlockKind.Entry ||
            block.Predecessors.Any(predecessor => predecessor.Source.IsReachable && outputStates.ContainsKey(predecessor.Source));
    }

    /// <summary>
    /// Merges all currently known reachable predecessor outputs for one block.
    /// </summary>
    /// <param name="controlFlowGraph">Method graph containing finalization-region blocks.</param>
    /// <param name="block">Block receiving predecessor states.</param>
    /// <param name="outputStates">Stable or current predecessor outputs.</param>
    /// <param name="plansByLocal">Ownership plans controlling guarded null joins.</param>
    /// <param name="semanticModel">Semantic model for finalization operations.</param>
    /// <param name="method">Containing method symbol.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations when transitions are enabled.</param>
    /// <param name="emit">Whether finalization processing should emit transitions and diagnostics.</param>
    /// <param name="transitions">Aggregate transitions when emission is enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when emission is enabled.</param>
    /// <returns>A mutable input state for block transfer.</returns>
    Dictionary<ILocalSymbol, CPPLocalOwnershipState> ResolveInputState(
        ControlFlowGraph controlFlowGraph,
        BasicBlock block,
        IReadOnlyDictionary<BasicBlock, Dictionary<ILocalSymbol, CPPLocalOwnershipState>> outputStates,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (block.Kind == BasicBlockKind.Entry) {
            return new Dictionary<ILocalSymbol, CPPLocalOwnershipState>(SymbolEqualityComparer.Default);
        }

        IReadOnlyList<Dictionary<ILocalSymbol, CPPLocalOwnershipState>> predecessorStates = block.Predecessors
            .Where(predecessor => predecessor.Source.IsReachable && outputStates.ContainsKey(predecessor.Source))
            .Select(predecessor => ApplyFinalizationRegions(
                controlFlowGraph,
                predecessor,
                outputStates[predecessor.Source],
                semanticModel,
                method,
                summaries,
                declarations,
                plansByLocal,
                emit,
                transitions,
                diagnostics))
            .ToArray();
        HashSet<ILocalSymbol> locals = new(SymbolEqualityComparer.Default);
        foreach (Dictionary<ILocalSymbol, CPPLocalOwnershipState> predecessorState in predecessorStates) {
            locals.UnionWith(predecessorState.Keys);
        }

        Dictionary<ILocalSymbol, CPPLocalOwnershipState> result = new(SymbolEqualityComparer.Default);
        foreach (ILocalSymbol local in locals) {
            CPPLocalOwnershipState[] incomingStates = predecessorStates
                .Select(state => state.TryGetValue(local, out CPPLocalOwnershipState value)
                    ? value
                    : CPPLocalOwnershipState.CreateUninitialized())
                .ToArray();
            bool permitsUninitializedOwned = plansByLocal.TryGetValue(local, out CPPLocalOwnershipPlan plan) &&
                plan.RequiresScopeGuard;
            result[local] = StateMerger.Merge(incomingStates, permitsUninitializedOwned);
        }

        return result;
    }

    /// <summary>
    /// Applies Roslyn finalization regions carried by one control-flow edge before it reaches its destination.
    /// </summary>
    /// <param name="controlFlowGraph">Method graph containing finalization blocks.</param>
    /// <param name="branch">Predecessor branch whose finally regions execute on the edge.</param>
    /// <param name="sourceState">Ownership state leaving the branch source.</param>
    /// <param name="semanticModel">Semantic model for finalization operations.</param>
    /// <param name="method">Containing method symbol.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations when transitions are enabled.</param>
    /// <param name="plansByLocal">Ownership plans keyed by local symbol.</param>
    /// <param name="emit">Whether finalization processing should emit transitions and diagnostics.</param>
    /// <param name="transitions">Aggregate transitions when emission is enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when emission is enabled.</param>
    /// <returns>The edge state after all required finally regions execute.</returns>
    Dictionary<ILocalSymbol, CPPLocalOwnershipState> ApplyFinalizationRegions(
        ControlFlowGraph controlFlowGraph,
        ControlFlowBranch branch,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipState> sourceState,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        Dictionary<ILocalSymbol, CPPLocalOwnershipState> result = CloneState(sourceState);
        foreach (ControlFlowRegion finallyRegion in branch.FinallyRegions) {
            result = ApplyFinalizationRegion(
                controlFlowGraph,
                finallyRegion,
                result,
                semanticModel,
                method,
                summaries,
                declarations,
                plansByLocal,
                emit,
                transitions,
                diagnostics);
        }

        return result;
    }

    /// <summary>
    /// Propagates ownership through one finally-region CFG and conservatively merges its executable exits.
    /// </summary>
    /// <param name="controlFlowGraph">Method graph containing the finalization region.</param>
    /// <param name="finallyRegion">Finally region to execute.</param>
    /// <param name="sourceState">Ownership state entering the region.</param>
    /// <param name="semanticModel">Semantic model for finalization operations.</param>
    /// <param name="method">Containing method symbol.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations when transitions are enabled.</param>
    /// <param name="plansByLocal">Ownership plans keyed by local symbol.</param>
    /// <param name="emit">Whether to append transitions and diagnostics.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when enabled.</param>
    /// <returns>The conservatively merged ownership state leaving the finally region.</returns>
    Dictionary<ILocalSymbol, CPPLocalOwnershipState> ApplyFinalizationRegion(
        ControlFlowGraph controlFlowGraph,
        ControlFlowRegion finallyRegion,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipState> sourceState,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        Dictionary<BasicBlock, Dictionary<ILocalSymbol, CPPLocalOwnershipState>> regionOutputs = [];
        int regionBlockCount = finallyRegion.LastBlockOrdinal - finallyRegion.FirstBlockOrdinal + 1;
        int iterationLimit = Math.Max(8, regionBlockCount * 8);
        for (int iteration = 0; iteration < iterationLimit; iteration++) {
            bool changed = false;
            for (int blockOrdinal = finallyRegion.FirstBlockOrdinal;
                 blockOrdinal <= finallyRegion.LastBlockOrdinal;
                 blockOrdinal++) {
                BasicBlock block = controlFlowGraph.Blocks[blockOrdinal];
                List<Dictionary<ILocalSymbol, CPPLocalOwnershipState>> incomingStates = [];
                if (blockOrdinal == finallyRegion.FirstBlockOrdinal) {
                    incomingStates.Add(CloneState(sourceState));
                }
                foreach (ControlFlowBranch predecessor in block.Predecessors) {
                    if (predecessor.Source.Ordinal >= finallyRegion.FirstBlockOrdinal &&
                        predecessor.Source.Ordinal <= finallyRegion.LastBlockOrdinal &&
                        regionOutputs.TryGetValue(predecessor.Source, out Dictionary<ILocalSymbol, CPPLocalOwnershipState> predecessorState)) {
                        incomingStates.Add(predecessorState);
                    }
                }
                if (incomingStates.Count == 0) {
                    continue;
                }

                Dictionary<ILocalSymbol, CPPLocalOwnershipState> state = MergeOwnershipStates(
                    incomingStates,
                    plansByLocal);
                ProcessBlock(
                    block,
                    semanticModel,
                    method,
                    summaries,
                    declarations,
                    plansByLocal,
                    state,
                    emit,
                    transitions,
                    diagnostics);
                if (!regionOutputs.TryGetValue(block, out Dictionary<ILocalSymbol, CPPLocalOwnershipState> priorState) ||
                    !StatesEqual(priorState, state)) {
                    regionOutputs[block] = CloneState(state);
                    changed = true;
                }
            }
            if (!changed) {
                break;
            }
            if (iteration == iterationLimit - 1) {
                throw new InvalidOperationException("Native ownership finalization-region analysis did not converge.");
            }
        }

        IReadOnlyList<Dictionary<ILocalSymbol, CPPLocalOwnershipState>> exitStates = regionOutputs
            .Where(output => GetSuccessorBranches(output.Key).Any(successor =>
                successor.Destination == null ||
                successor.Destination.Ordinal < finallyRegion.FirstBlockOrdinal ||
                successor.Destination.Ordinal > finallyRegion.LastBlockOrdinal))
            .Select(output => output.Value)
            .ToArray();
        if (exitStates.Count == 0 &&
            regionOutputs.TryGetValue(controlFlowGraph.Blocks[finallyRegion.LastBlockOrdinal], out Dictionary<ILocalSymbol, CPPLocalOwnershipState> lastState)) {
            return CloneState(lastState);
        }

        return MergeOwnershipStates(exitStates, plansByLocal);
    }

    /// <summary>
    /// Conservatively merges complete method states from multiple executable control-flow paths.
    /// </summary>
    /// <param name="states">Incoming complete local states.</param>
    /// <param name="plansByLocal">Ownership plans controlling guarded null joins.</param>
    /// <returns>A merged mutable state.</returns>
    Dictionary<ILocalSymbol, CPPLocalOwnershipState> MergeOwnershipStates(
        IReadOnlyList<Dictionary<ILocalSymbol, CPPLocalOwnershipState>> states,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal) {
        HashSet<ILocalSymbol> locals = new(SymbolEqualityComparer.Default);
        foreach (Dictionary<ILocalSymbol, CPPLocalOwnershipState> state in states) {
            locals.UnionWith(state.Keys);
        }

        Dictionary<ILocalSymbol, CPPLocalOwnershipState> result = new(SymbolEqualityComparer.Default);
        foreach (ILocalSymbol local in locals) {
            CPPLocalOwnershipState[] localStates = states
                .Select(state => state.TryGetValue(local, out CPPLocalOwnershipState value)
                    ? value
                    : CPPLocalOwnershipState.CreateUninitialized())
                .ToArray();
            bool permitsUninitializedOwned = plansByLocal.TryGetValue(local, out CPPLocalOwnershipPlan plan) &&
                plan.RequiresScopeGuard;
            result[local] = StateMerger.Merge(localStates, permitsUninitializedOwned);
        }

        return result;
    }

    /// <summary>
    /// Enumerates the non-null outgoing branches for one basic block without duplicates.
    /// </summary>
    /// <param name="block">Block whose outgoing branches should be inspected.</param>
    /// <returns>Fall-through and conditional branches.</returns>
    static IReadOnlyList<ControlFlowBranch> GetSuccessorBranches(BasicBlock block) {
        List<ControlFlowBranch> branches = [];
        if (block.FallThroughSuccessor != null) {
            branches.Add(block.FallThroughSuccessor);
        }
        if (block.ConditionalSuccessor != null &&
            !ReferenceEquals(block.ConditionalSuccessor, block.FallThroughSuccessor)) {
            branches.Add(block.ConditionalSuccessor);
        }

        return branches;
    }

    /// <summary>
    /// Applies ownership transfer functions for one basic block.
    /// </summary>
    /// <param name="block">Block whose operations should be applied.</param>
    /// <param name="semanticModel">Semantic model for block syntax.</param>
    /// <param name="method">Containing method when diagnostics are enabled.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations keyed by local when transitions are enabled.</param>
    /// <param name="plansByLocal">Ownership plans keyed by local symbol.</param>
    /// <param name="state">Mutable local state at block entry.</param>
    /// <param name="emit">Whether to append diagnostics and transitions.</param>
    /// <param name="transitions">Aggregate transitions when emission is enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when emission is enabled.</param>
    void ProcessBlock(
        BasicBlock block,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        foreach (SyntaxNode syntax in GetBlockOwnershipSyntax(block)) {
            ProcessOwnershipSyntax(
                syntax,
                semanticModel,
                method,
                summaries,
                declarations,
                plansByLocal,
                state,
                emit,
                transitions,
                diagnostics);
        }
    }

    /// <summary>
    /// Applies one ownership-relevant source operation to the current local state.
    /// </summary>
    /// <param name="syntax">Ownership-relevant source syntax.</param>
    /// <param name="semanticModel">Semantic model for the operation.</param>
    /// <param name="method">Containing method.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="plansByLocal">Ownership plans keyed by local.</param>
    /// <param name="state">Mutable local ownership state.</param>
    /// <param name="emit">Whether to append transitions and diagnostics.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when enabled.</param>
    void ProcessOwnershipSyntax(
        SyntaxNode syntax,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (syntax is VariableDeclaratorSyntax declaration) {
            ProcessDeclaration(declaration, semanticModel, summaries, plansByLocal, state, emit, transitions);
        } else if (syntax is InvocationExpressionSyntax invocation) {
            ProcessInvocation(invocation, semanticModel, method, summaries, declarations, state, emit, transitions, diagnostics);
        } else if (syntax is BaseObjectCreationExpressionSyntax objectCreation) {
            ProcessObjectCreation(objectCreation, semanticModel, method, summaries, declarations, state, emit, transitions, diagnostics);
        } else if (syntax is AssignmentExpressionSyntax assignment) {
            ProcessAssignment(assignment, semanticModel, method, summaries, declarations, plansByLocal, state, emit, transitions, diagnostics);
        } else if (syntax is ReturnStatementSyntax returnStatement) {
            ProcessReturn(returnStatement, semanticModel, method, summaries, declarations, state, emit, transitions, diagnostics);
        }
    }

    /// <summary>
    /// Applies one local declaration's initial ownership state.
    /// </summary>
    /// <param name="declaration">Local declaration syntax.</param>
    /// <param name="semanticModel">Semantic model for the declaration.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="plansByLocal">Ownership plans keyed by local symbol.</param>
    /// <param name="state">Mutable local state.</param>
    /// <param name="emit">Whether to append acquisition transitions.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    void ProcessDeclaration(
        VariableDeclaratorSyntax declaration,
        SemanticModel semanticModel,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions) {
        ILocalSymbol local = semanticModel.GetDeclaredSymbol(declaration) as ILocalSymbol;
        if (local == null || !plansByLocal.TryGetValue(local, out CPPLocalOwnershipPlan plan)) {
            return;
        }

        if (declaration.Initializer == null) {
            state[local] = CPPLocalOwnershipState.CreateUninitialized();
            return;
        }

        CPPOwnershipKind initializerOwnership = ExpressionClassifier.Classify(
            semanticModel.GetOperation(declaration.Initializer.Value),
            summaries.Summaries);
        if (initializerOwnership == CPPOwnershipKind.Unknown) {
            state[local] = CPPLocalOwnershipState.CreateUninitialized();
            return;
        }

        state[local] = new CPPLocalOwnershipState(
            initializerOwnership,
            CPPOwnershipLifecycle.Live,
            true);
        if (emit && initializerOwnership == CPPOwnershipKind.Owned) {
            AddTransition(transitions, new CPPOwnershipTransition(
                declaration,
                plan.Declaration,
                CPPOwnershipTransitionKind.Acquire,
                CPPOwnershipKind.Owned,
                CPPOwnershipLifecycle.Live));
        }
    }

    /// <summary>
    /// Applies release and takes-ownership call semantics to one invocation.
    /// </summary>
    /// <param name="invocationSyntax">Invocation source syntax.</param>
    /// <param name="semanticModel">Semantic model for the invocation.</param>
    /// <param name="method">Containing method when diagnostics are enabled.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="state">Mutable local state.</param>
    /// <param name="emit">Whether to append diagnostics and transitions.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when enabled.</param>
    void ProcessInvocation(
        InvocationExpressionSyntax invocationSyntax,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        IInvocationOperation invocation = semanticModel.GetOperation(invocationSyntax) as IInvocationOperation;
        if (invocation == null) {
            return;
        }

        if (emit) {
            ValidateInvocationUses(invocation, method, state, diagnostics);
        }
        if (IsNativeRelease(invocation.TargetMethod)) {
            ProcessRelease(invocation, method, declarations, state, emit, transitions, diagnostics);
            return;
        }

        if (invocation.TargetMethod.ReducedFrom != null && invocation.Instance != null) {
            ProcessOwnershipArgument(
                invocationSyntax,
                invocation.Instance,
                invocation.TargetMethod.ReducedFrom.Parameters[0],
                invocation.Instance.Syntax,
                method,
                ResolveSummary(invocation.TargetMethod.ReducedFrom, summaries),
                declarations,
                state,
                emit,
                transitions,
                diagnostics);
        }

        ProcessOwnershipArguments(
            invocationSyntax,
            invocation.TargetMethod,
            invocation.Arguments,
            method,
            summaries,
            declarations,
            state,
            emit,
            transitions,
            diagnostics);
    }

    /// <summary>
    /// Applies takes-ownership constructor parameter semantics to one object creation expression.
    /// </summary>
    /// <param name="objectCreationSyntax">Object creation source syntax.</param>
    /// <param name="semanticModel">Semantic model for constructor arguments.</param>
    /// <param name="method">Containing method when diagnostics are enabled.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="state">Mutable local state.</param>
    /// <param name="emit">Whether to append diagnostics and transitions.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when enabled.</param>
    void ProcessObjectCreation(
        BaseObjectCreationExpressionSyntax objectCreationSyntax,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        IObjectCreationOperation objectCreation = semanticModel.GetOperation(objectCreationSyntax) as IObjectCreationOperation;
        if (objectCreation?.Constructor == null) {
            return;
        }

        if (emit) {
            foreach (IArgumentOperation argument in objectCreation.Arguments) {
                ValidateDeadLocalUsesAfterNestedOwnershipOperations(argument.Value, method, state, diagnostics);
            }
        }

        ProcessOwnershipArguments(
            objectCreationSyntax,
            objectCreation.Constructor,
            objectCreation.Arguments,
            method,
            summaries,
            declarations,
            state,
            emit,
            transitions,
            diagnostics);
    }

    /// <summary>
    /// Applies resolved parameter ownership to direct local arguments at one invocation or constructor boundary.
    /// </summary>
    /// <param name="transferSyntax">Exact call-site syntax that receives transfer transitions.</param>
    /// <param name="targetMethod">Invoked method or constructor.</param>
    /// <param name="arguments">Semantically aligned call arguments.</param>
    /// <param name="method">Containing source method.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="state">Mutable local state.</param>
    /// <param name="emit">Whether to append diagnostics and transitions.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when enabled.</param>
    void ProcessOwnershipArguments(
        SyntaxNode transferSyntax,
        IMethodSymbol targetMethod,
        System.Collections.Immutable.ImmutableArray<IArgumentOperation> arguments,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        CPPMethodOwnershipSummary targetSummary = ResolveSummary(targetMethod, summaries);
        foreach (IArgumentOperation argument in arguments) {
            ProcessOwnershipArgument(
                transferSyntax,
                argument.Value,
                argument.Parameter,
                argument.Syntax,
                method,
                targetSummary,
                declarations,
                state,
                emit,
                transitions,
                diagnostics);
        }
    }

    /// <summary>
    /// Applies one parameter ownership contract to a direct local value at a call boundary.
    /// </summary>
    /// <param name="transferSyntax">Exact invocation or construction syntax receiving a transfer transition.</param>
    /// <param name="value">Argument or reduced-extension receiver value.</param>
    /// <param name="parameter">Target parameter receiving the value.</param>
    /// <param name="diagnosticSyntax">Source syntax used for boundary diagnostics.</param>
    /// <param name="method">Containing source method.</param>
    /// <param name="targetSummary">Resolved target ownership summary when available.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="state">Mutable local ownership state.</param>
    /// <param name="emit">Whether to append diagnostics and transitions.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when enabled.</param>
    void ProcessOwnershipArgument(
        SyntaxNode transferSyntax,
        IOperation value,
        IParameterSymbol parameter,
        SyntaxNode diagnosticSyntax,
        IMethodSymbol method,
        CPPMethodOwnershipSummary targetSummary,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        ILocalReferenceOperation localReference = UnwrapLocalReference(value);
        if (localReference == null ||
            !state.TryGetValue(localReference.Local, out CPPLocalOwnershipState localState) ||
            !localState.IsInitialized ||
            localState.Ownership != CPPOwnershipKind.Owned) {
            return;
        }

        CPPParameterOwnershipKind parameterOwnership = ResolveParameterOwnership(parameter, targetSummary);
        bool retainedBorrowConfinedByUsing = parameterOwnership == CPPParameterOwnershipKind.EscapesWithReturn &&
            IsCallResultConfinedByUsing(transferSyntax);
        if (parameterOwnership == CPPParameterOwnershipKind.TakesOwnership ||
            parameterOwnership == CPPParameterOwnershipKind.TakesOwnershipRetained) {
            bool insertsIntoRetainingCollection = IntrinsicCatalog.IsOwnershipTransferringCollectionInsertion(parameter?.ContainingSymbol as IMethodSymbol);
            if (localState.Lifecycle == CPPOwnershipLifecycle.TransferredRetained && insertsIntoRetainingCollection) {
                return;
            }

            if (localState.Lifecycle != CPPOwnershipLifecycle.Live) {
                if (emit) {
                    AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                        "CPPOWN004",
                        diagnosticSyntax,
                        method,
                        $"Local '{localReference.Local.Name}' is transferred more than once by the same call boundary.",
                        "Pass each owned local to at most one takes-ownership parameter or receiver."));
                }
                return;
            }

            bool retainedByCollection = insertsIntoRetainingCollection ||
                parameterOwnership == CPPParameterOwnershipKind.TakesOwnershipRetained;
            state[localReference.Local] = new CPPLocalOwnershipState(
                CPPOwnershipKind.Owned,
                retainedByCollection ? CPPOwnershipLifecycle.TransferredRetained : CPPOwnershipLifecycle.Transferred,
                true);
            if (emit) {
                AddTransition(transitions, new CPPOwnershipTransition(
                    transferSyntax,
                    declarations[localReference.Local],
                    CPPOwnershipTransitionKind.Transfer,
                    CPPOwnershipKind.Owned,
                    CPPOwnershipLifecycle.Transferred));
            }
        } else if (emit &&
            parameterOwnership != CPPParameterOwnershipKind.NoEscape &&
            parameterOwnership != CPPParameterOwnershipKind.RetainsBorrow &&
            !retainedBorrowConfinedByUsing &&
            localState.Lifecycle == CPPOwnershipLifecycle.Live) {
            bool escapes = parameterOwnership == CPPParameterOwnershipKind.Escapes ||
                parameterOwnership == CPPParameterOwnershipKind.EscapesWithReturn;
            string diagnosticCode = escapes
                ? "CPPOWN002"
                : "CPPOWN001";
            string diagnosticMessage = escapes
                ? $"Owned local '{localReference.Local.Name}' escapes through parameter '{parameter?.Name}' without transferring cleanup responsibility."
                : $"Owned local '{localReference.Local.Name}' crosses parameter '{parameter?.Name}' without a native ownership contract.";
            string correction = escapes
                ? "Redesign the callee to take ownership with verified cleanup or keep the argument scoped to the call."
                : "Mark the parameter as no-escape, retains-borrow, or takes-ownership, or pass borrowed storage instead.";
            AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                diagnosticCode,
                diagnosticSyntax,
                method,
                diagnosticMessage,
                correction));
        }
    }

    /// <summary>
    /// Determines whether one call result that retains a borrowed argument is immediately bounded by a using declaration or statement.
    /// </summary>
    /// <param name="callSyntax">Invocation or object-creation syntax producing the retaining disposable.</param>
    /// <returns><c>true</c> when the complete call initializes a local disposed by the containing scope.</returns>
    static bool IsCallResultConfinedByUsing(SyntaxNode callSyntax) {
        VariableDeclaratorSyntax declaration = callSyntax.AncestorsAndSelf()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(candidate => candidate.Initializer?.Value.Span.Contains(callSyntax.Span) == true);
        if (declaration == null) {
            return false;
        }

        LocalDeclarationStatementSyntax localDeclaration = declaration.Ancestors()
            .OfType<LocalDeclarationStatementSyntax>()
            .FirstOrDefault();
        if (localDeclaration != null && localDeclaration.UsingKeyword.RawKind != 0) {
            return true;
        }

        return declaration.Ancestors().OfType<UsingStatementSyntax>().Any();
    }

    /// <summary>
    /// Applies explicit native cleanup to one local argument.
    /// </summary>
    /// <param name="invocation">Native cleanup invocation.</param>
    /// <param name="method">Containing method when diagnostics are enabled.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="state">Mutable local state.</param>
    /// <param name="emit">Whether to append diagnostics and transitions.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when enabled.</param>
    void ProcessRelease(
        IInvocationOperation invocation,
        IMethodSymbol method,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (invocation.Arguments.Length == 0) {
            return;
        }

        ILocalReferenceOperation localReference = UnwrapLocalReference(invocation.Arguments[0].Value);
        if (localReference == null || !state.TryGetValue(localReference.Local, out CPPLocalOwnershipState localState)) {
            return;
        }
        if (localState.Ownership == CPPOwnershipKind.Borrowed) {
            if (emit) {
                AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                    "CPPOWN003",
                    invocation.Syntax,
                    method,
                    $"Borrowed local '{localReference.Local.Name}' cannot be released by this scope.",
                    "Remove the cleanup call or establish an owned value before releasing it."));
            }
            return;
        }
        if (localState.Lifecycle != CPPOwnershipLifecycle.Live) {
            return;
        }

        state[localReference.Local] = new CPPLocalOwnershipState(
            localState.Ownership,
            CPPOwnershipLifecycle.Released,
            localState.IsInitialized);
        if (emit) {
            AddTransition(transitions, new CPPOwnershipTransition(
                invocation.Syntax,
                declarations[localReference.Local],
                CPPOwnershipTransitionKind.Release,
                CPPOwnershipKind.Owned,
                CPPOwnershipLifecycle.Released));
        }
    }

    /// <summary>
    /// Applies local replacement, nulling, out-parameter transfer, container-element transfer, and member-escape semantics to one assignment.
    /// </summary>
    /// <param name="assignmentSyntax">Assignment source syntax.</param>
    /// <param name="semanticModel">Semantic model for the assignment.</param>
    /// <param name="method">Containing method when diagnostics are enabled.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="plansByLocal">Ownership plans keyed by local.</param>
    /// <param name="state">Mutable local state.</param>
    /// <param name="emit">Whether to append diagnostics and transitions.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when enabled.</param>
    void ProcessAssignment(
        AssignmentExpressionSyntax assignmentSyntax,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        IAssignmentOperation assignment = semanticModel.GetOperation(assignmentSyntax) as IAssignmentOperation;
        if (assignment == null) {
            return;
        }

        if (emit) {
            ValidateDeadLocalUsesAfterNestedOwnershipOperations(assignment.Value, method, state, diagnostics);
        }
        if (assignment.Target is ILocalReferenceOperation targetLocal && plansByLocal.ContainsKey(targetLocal.Local)) {
            ProcessLocalAssignment(
                assignment,
                targetLocal.Local,
                method,
                summaries,
                declarations,
                state,
                emit,
                transitions,
                diagnostics);
            return;
        }

        ILocalReferenceOperation valueLocal = UnwrapLocalReference(assignment.Value);
        if (valueLocal == null ||
            !state.TryGetValue(valueLocal.Local, out CPPLocalOwnershipState valueState) ||
            valueState.Ownership != CPPOwnershipKind.Owned ||
            valueState.Lifecycle != CPPOwnershipLifecycle.Live) {
            return;
        }

        bool transfersToOutParameter = assignment.Target is IParameterReferenceOperation targetParameter &&
            targetParameter.Parameter.RefKind == RefKind.Out;
        if (transfersToOutParameter || assignment.Target is IArrayElementReferenceOperation) {
            state[valueLocal.Local] = new CPPLocalOwnershipState(
                CPPOwnershipKind.Owned,
                CPPOwnershipLifecycle.Transferred,
                true);
            if (emit) {
                AddTransition(transitions, new CPPOwnershipTransition(
                    assignmentSyntax,
                    declarations[valueLocal.Local],
                    CPPOwnershipTransitionKind.Transfer,
                    CPPOwnershipKind.Owned,
                    CPPOwnershipLifecycle.Transferred));
            }
            return;
        }

        if (!IsOrdinaryMemberTarget(assignment.Target)) {
            return;
        }

        ISymbol targetMember = ResolveMemberSymbol(assignment.Target);
        if (HasAttribute(targetMember, "NativeOwnedMember")) {
            state[valueLocal.Local] = new CPPLocalOwnershipState(
                CPPOwnershipKind.Owned,
                CPPOwnershipLifecycle.Transferred,
                true);
            if (emit) {
                AddTransition(transitions, new CPPOwnershipTransition(
                    assignmentSyntax,
                    declarations[valueLocal.Local],
                    CPPOwnershipTransitionKind.Transfer,
                    CPPOwnershipKind.Owned,
                    CPPOwnershipLifecycle.Transferred));
            }
            return;
        }

        if (!emit) {
            return;
        }

        AddDiagnostic(diagnostics, DiagnosticFactory.Create(
            "CPPOWN002",
            assignmentSyntax,
            method,
            $"Owned local '{valueLocal.Local.Name}' escapes into a member without an owned-member contract.",
            "Mark the destination member as native-owned or transfer through an explicitly contracted API."));
    }

    /// <summary>
    /// Applies replacement semantics when the assignment target is an ownership-tracked local.
    /// </summary>
    /// <param name="assignment">Semantic assignment operation.</param>
    /// <param name="local">Target local.</param>
    /// <param name="method">Containing method when diagnostics are enabled.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="state">Mutable local state.</param>
    /// <param name="emit">Whether to append diagnostics and transitions.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when enabled.</param>
    void ProcessLocalAssignment(
        IAssignmentOperation assignment,
        ILocalSymbol local,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        state.TryGetValue(local, out CPPLocalOwnershipState currentState);
        currentState ??= CPPLocalOwnershipState.CreateUninitialized();
        if (IsNullOperation(assignment.Value)) {
            if (currentState.IsInitialized &&
                currentState.Ownership == CPPOwnershipKind.Owned &&
                currentState.Lifecycle == CPPOwnershipLifecycle.Live &&
                emit) {
                AddTransition(transitions, new CPPOwnershipTransition(
                    assignment.Syntax,
                    declarations[local],
                    CPPOwnershipTransitionKind.Release,
                    CPPOwnershipKind.Owned,
                    CPPOwnershipLifecycle.Released));
            }
            state[local] = CPPLocalOwnershipState.CreateUninitialized();
            return;
        }

        CPPOwnershipKind replacementOwnership = ExpressionClassifier.Classify(assignment.Value, summaries.Summaries);
        if (replacementOwnership == CPPOwnershipKind.Borrowed &&
            (!currentState.IsInitialized || currentState.Ownership == CPPOwnershipKind.Borrowed)) {
            state[local] = new CPPLocalOwnershipState(
                CPPOwnershipKind.Borrowed,
                CPPOwnershipLifecycle.Live,
                true);
            return;
        }
        if (replacementOwnership != CPPOwnershipKind.Owned) {
            if (emit) {
                AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                    "CPPOWN008",
                    assignment.Syntax,
                    method,
                    $"Owned local '{local.Name}' is replaced by a value whose ownership is {replacementOwnership.ToString().ToLowerInvariant()}.",
                    "Replace the local only with a proven owned value after releasing its previous value."));
            }
            state[local] = new CPPLocalOwnershipState(CPPOwnershipKind.Unknown, CPPOwnershipLifecycle.Live, true);
            return;
        }

        state[local] = new CPPLocalOwnershipState(
            CPPOwnershipKind.Owned,
            CPPOwnershipLifecycle.Live,
            true);
        if (emit) {
            AddTransition(transitions, new CPPOwnershipTransition(
                assignment.Syntax,
                declarations[local],
                CPPOwnershipTransitionKind.Replace,
                CPPOwnershipKind.Owned,
                CPPOwnershipLifecycle.Live));
        }
    }

    /// <summary>
    /// Transfers a returned live owned local when the containing method returns owned storage.
    /// </summary>
    /// <param name="returnSyntax">Return source syntax.</param>
    /// <param name="semanticModel">Semantic model for the return.</param>
    /// <param name="method">Containing method when diagnostics are enabled.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="state">Mutable local state.</param>
    /// <param name="emit">Whether to append diagnostics and transitions.</param>
    /// <param name="transitions">Aggregate transitions when enabled.</param>
    /// <param name="diagnostics">Aggregate diagnostics when enabled.</param>
    void ProcessReturn(
        ReturnStatementSyntax returnSyntax,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        bool emit,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        IReturnOperation returnOperation = semanticModel.GetOperation(returnSyntax) as IReturnOperation;
        if (returnOperation?.ReturnedValue == null) {
            return;
        }
        if (emit) {
            ValidateDeadLocalUsesAfterNestedOwnershipOperations(returnOperation.ReturnedValue, method, state, diagnostics);
        }

        CPPMethodOwnershipSummary methodSummary = ResolveSummary(method, summaries);
        if (methodSummary?.ReturnOwnership != CPPOwnershipKind.Owned) {
            return;
        }

        ILocalReferenceOperation localReference = ResolveReturnedOwnedLocal(
            returnOperation.ReturnedValue,
            method,
            semanticModel,
            state,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
        if (localReference == null) {
            return;
        }

        state[localReference.Local] = new CPPLocalOwnershipState(
            CPPOwnershipKind.Owned,
            CPPOwnershipLifecycle.Transferred,
            true);
        if (emit) {
            AddTransition(transitions, new CPPOwnershipTransition(
                returnSyntax,
                declarations[localReference.Local],
                CPPOwnershipTransitionKind.Transfer,
                CPPOwnershipKind.Owned,
                CPPOwnershipLifecycle.Transferred));
        }
    }

    /// <summary>
    /// Resolves a returned local alias chain to the live owned local responsible for the underlying native allocation.
    /// </summary>
    /// <param name="operation">Returned value operation or one recursively resolved alias source.</param>
    /// <param name="method">Method containing the return.</param>
    /// <param name="semanticModel">Semantic model for local provenance expressions.</param>
    /// <param name="state">Current ownership state keyed by tracked local.</param>
    /// <param name="visitedLocals">Locals already traversed while resolving the alias chain.</param>
    /// <returns>The live owned source local, or null when the returned value does not provably alias one.</returns>
    static ILocalReferenceOperation ResolveReturnedOwnedLocal(
        IOperation operation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        ISet<ILocalSymbol> visitedLocals) {
        ILocalReferenceOperation localReference = UnwrapLocalReference(operation);
        if (localReference == null || !visitedLocals.Add(localReference.Local)) {
            return null;
        }

        if (state.TryGetValue(localReference.Local, out CPPLocalOwnershipState localState) &&
            localState.Ownership == CPPOwnershipKind.Owned &&
            localState.Lifecycle == CPPOwnershipLifecycle.Live) {
            return localReference;
        }

        if (!CPPLocalValueProvenanceResolver.TryResolveStableSourceExpression(
            method,
            localReference.Local,
            semanticModel,
            out ExpressionSyntax sourceExpression)) {
            return null;
        }

        return ResolveReturnedOwnedLocal(
            semanticModel.GetOperation(sourceExpression),
            method,
            semanticModel,
            state,
            visitedLocals);
    }

    /// <summary>
    /// Rejects lambda, delegate, and local-function capture of a live owned local.
    /// </summary>
    /// <param name="methodDeclaration">Method whose nested executables should be checked.</param>
    /// <param name="semanticModel">Semantic model for captured identifiers.</param>
    /// <param name="controlFlowGraph">Containing method graph used to locate each capture site.</param>
    /// <param name="method">Containing method symbol.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="plansByLocal">Ownership plans keyed by local symbol.</param>
    /// <param name="outputStates">Stable block output states used to reconstruct capture-site state.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void ValidateCaptures(
        SyntaxNode methodDeclaration,
        SemanticModel semanticModel,
        ControlFlowGraph controlFlowGraph,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal,
        IReadOnlyDictionary<BasicBlock, Dictionary<ILocalSymbol, CPPLocalOwnershipState>> outputStates,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        IEnumerable<SyntaxNode> nestedExecutables = methodDeclaration.DescendantNodes()
            .Where(node => node is AnonymousFunctionExpressionSyntax || node is LocalFunctionStatementSyntax);
        foreach (SyntaxNode nestedExecutable in nestedExecutables) {
            foreach (IdentifierNameSyntax identifier in nestedExecutable.DescendantNodes().OfType<IdentifierNameSyntax>()) {
                ILocalSymbol local = semanticModel.GetSymbolInfo(identifier).Symbol as ILocalSymbol;
                if (local == null ||
                    !plansByLocal.TryGetValue(local, out CPPLocalOwnershipPlan plan) ||
                    !plan.RequiresScopeGuard ||
                    !IsLiveOwnedAtSyntax(
                        local,
                        plan,
                        nestedExecutable,
                        controlFlowGraph,
                        semanticModel,
                        method,
                        summaries,
                        declarations,
                        plansByLocal,
                        outputStates)) {
                    continue;
                }

                AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                    "CPPOWN002",
                    nestedExecutable,
                    method,
                    $"Owned local '{local.Name}' is captured by an executable object whose lifetime cannot be proven local.",
                    "Avoid capturing the owned local; pass borrowed data to a no-escape call or transfer ownership explicitly."));
                break;
            }
        }
    }

    /// <summary>
    /// Reconstructs whether one local is live and owned immediately before a nested executable is created.
    /// </summary>
    /// <param name="local">Captured local symbol.</param>
    /// <param name="plan">Local ownership plan used only when Roslyn exposes no capture block.</param>
    /// <param name="syntax">Capture-site syntax.</param>
    /// <param name="controlFlowGraph">Containing method graph.</param>
    /// <param name="semanticModel">Semantic model for preceding operations.</param>
    /// <param name="method">Containing method symbol.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="plansByLocal">Ownership plans keyed by local.</param>
    /// <param name="outputStates">Stable block output states.</param>
    /// <returns><c>true</c> when CFG state at the capture site is live and owned.</returns>
    bool IsLiveOwnedAtSyntax(
        ILocalSymbol local,
        CPPLocalOwnershipPlan plan,
        SyntaxNode syntax,
        ControlFlowGraph controlFlowGraph,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipPlan> plansByLocal,
        IReadOnlyDictionary<BasicBlock, Dictionary<ILocalSymbol, CPPLocalOwnershipState>> outputStates) {
        BasicBlock captureBlock = controlFlowGraph.Blocks.FirstOrDefault(block =>
            block.Operations.Any(operation => operation.Syntax.FullSpan.Contains(syntax.FullSpan)) ||
            block.BranchValue?.Syntax.FullSpan.Contains(syntax.FullSpan) == true);
        if (captureBlock == null || !CanResolveInput(captureBlock, outputStates)) {
            return plan.InitiallyOwnsValue && plan.InitialOwnership == CPPOwnershipKind.Owned;
        }

        Dictionary<ILocalSymbol, CPPLocalOwnershipState> state = ResolveInputState(
            controlFlowGraph,
            captureBlock,
            outputStates,
            plansByLocal,
            semanticModel,
            method,
            summaries,
            declarations,
            false,
            null,
            null);
        foreach (SyntaxNode precedingSyntax in GetBlockOwnershipSyntax(captureBlock)) {
            if (precedingSyntax.SpanStart >= syntax.SpanStart) {
                break;
            }
            ProcessOwnershipSyntax(
                precedingSyntax,
                semanticModel,
                method,
                summaries,
                declarations,
                plansByLocal,
                state,
                false,
                null,
                null);
        }

        return state.TryGetValue(local, out CPPLocalOwnershipState localState) &&
            localState.IsInitialized &&
            localState.Ownership == CPPOwnershipKind.Owned &&
            localState.Lifecycle == CPPOwnershipLifecycle.Live;
    }

    /// <summary>
    /// Reports incompatible predecessor ownership at one control-flow join.
    /// </summary>
    /// <param name="block">Joined basic block.</param>
    /// <param name="methodDeclaration">Containing source method.</param>
    /// <param name="semanticModel">Semantic model used to distinguish active locals from values carried out of a completed lexical scope.</param>
    /// <param name="method">Containing method symbol.</param>
    /// <param name="state">Merged input state.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void ReportAmbiguousJoin(
        BasicBlock block,
        SyntaxNode methodDeclaration,
        SemanticModel semanticModel,
        IMethodSymbol method,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (block.Kind == BasicBlockKind.Exit || block.Predecessors.Length < 2) {
            return;
        }

        SyntaxNode joinSyntax = ResolveBlockSyntax(block, methodDeclaration);
        foreach (KeyValuePair<ILocalSymbol, CPPLocalOwnershipState> localState in state) {
            if (!localState.Value.IsAmbiguous) {
                continue;
            }
            if (!IsLocalVisibleAtJoin(localState.Key, joinSyntax, semanticModel)) {
                continue;
            }

            AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                "CPPOWN009",
                joinSyntax,
                method,
                $"Control-flow paths disagree about ownership or lifecycle for local '{localState.Key.Name}'.",
                "Make every incoming path leave the local in the same owned, released, or transferred state."));
        }
    }

    /// <summary>
    /// Determines whether one local is lexically visible at a control-flow join so a prior iteration's completed lifetime cannot contaminate a later declaration.
    /// </summary>
    /// <param name="local">Local whose lexical visibility should be tested.</param>
    /// <param name="joinSyntax">Source syntax representing the joined basic block.</param>
    /// <param name="semanticModel">Semantic model that resolves symbols visible at the join.</param>
    /// <returns><c>true</c> when the local is in scope at the join; otherwise <c>false</c>.</returns>
    static bool IsLocalVisibleAtJoin(
        ILocalSymbol local,
        SyntaxNode joinSyntax,
        SemanticModel semanticModel) {
        return semanticModel.LookupSymbols(joinSyntax.SpanStart, name: local.Name)
            .Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, local));
    }

    /// <summary>
    /// Adds one scope-cleanup transition when the merged normal method exit retains a live owned local.
    /// </summary>
    /// <param name="controlFlowGraph">Method graph containing the unique normal exit block.</param>
    /// <param name="declarations">Source declarations keyed by local.</param>
    /// <param name="outputStates">Stable outgoing states for reachable blocks.</param>
    /// <param name="transitions">Aggregate ownership transitions.</param>
    static void AddScopeCleanupTransitions(
        ControlFlowGraph controlFlowGraph,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> declarations,
        IReadOnlyDictionary<BasicBlock, Dictionary<ILocalSymbol, CPPLocalOwnershipState>> outputStates,
        ICollection<CPPOwnershipTransition> transitions) {
        BasicBlock exitBlock = controlFlowGraph.Blocks.Single(block => block.Kind == BasicBlockKind.Exit);
        outputStates.TryGetValue(exitBlock, out Dictionary<ILocalSymbol, CPPLocalOwnershipState> exitState);
        foreach (KeyValuePair<ILocalSymbol, VariableDeclaratorSyntax> declaration in declarations) {
            CPPLocalOwnershipState resolvedExitLocal = null;
            bool hasResolvedExitState = exitState != null &&
                exitState.TryGetValue(declaration.Key, out resolvedExitLocal) &&
                !resolvedExitLocal.IsAmbiguous;
            bool hasLiveOwnedExit = hasResolvedExitState
                ? resolvedExitLocal.IsInitialized &&
                    resolvedExitLocal.Ownership == CPPOwnershipKind.Owned &&
                    resolvedExitLocal.Lifecycle == CPPOwnershipLifecycle.Live
                : exitBlock.Predecessors.Any(predecessor =>
                    outputStates.TryGetValue(predecessor.Source, out Dictionary<ILocalSymbol, CPPLocalOwnershipState> predecessorState) &&
                    predecessorState.TryGetValue(declaration.Key, out CPPLocalOwnershipState predecessorLocal) &&
                    predecessorLocal.IsInitialized &&
                    predecessorLocal.Ownership == CPPOwnershipKind.Owned &&
                    predecessorLocal.Lifecycle == CPPOwnershipLifecycle.Live);
            if (!hasLiveOwnedExit) {
                continue;
            }

            AddTransition(transitions, new CPPOwnershipTransition(
                declaration.Value,
                declaration.Value,
                CPPOwnershipTransitionKind.ScopeCleanup,
                CPPOwnershipKind.Owned,
                CPPOwnershipLifecycle.ScopeCleanup));
        }
    }

    /// <summary>
    /// Collects ownership-relevant syntax represented by one reachable block in source order.
    /// </summary>
    /// <param name="block">Basic block to inspect.</param>
    /// <returns>Unique declarations, calls, assignments, and returns for the block.</returns>
    static IReadOnlyList<SyntaxNode> GetBlockOwnershipSyntax(BasicBlock block) {
        HashSet<SyntaxNode> seenSyntax = [];
        List<SyntaxNode> syntaxValues = [];
        foreach (IOperation operation in block.Operations) {
            AddOwnershipOperationsInEvaluationOrder(operation, seenSyntax, syntaxValues);
        }
        if (block.BranchValue != null) {
            AddOwnershipOperationsInEvaluationOrder(block.BranchValue, seenSyntax, syntaxValues);
            ReturnStatementSyntax returnStatement = block.BranchValue.Syntax
                .AncestorsAndSelf()
                .OfType<ReturnStatementSyntax>()
                .FirstOrDefault();
            if (returnStatement != null && seenSyntax.Add(returnStatement)) {
                syntaxValues.Add(returnStatement);
            }
        }

        return syntaxValues;
    }

    /// <summary>
    /// Adds ownership-relevant operations in Roslyn child evaluation order before their containing operation.
    /// </summary>
    /// <param name="operation">Semantic operation to traverse.</param>
    /// <param name="seenSyntax">Exact syntax nodes already added.</param>
    /// <param name="syntaxValues">Ordered ownership syntax receiving operations.</param>
    static void AddOwnershipOperationsInEvaluationOrder(
        IOperation operation,
        ISet<SyntaxNode> seenSyntax,
        ICollection<SyntaxNode> syntaxValues) {
        if (operation is IAnonymousFunctionOperation || operation is ILocalFunctionOperation) {
            return;
        } else if (operation is IVariableDeclarationGroupOperation declarationGroup) {
            foreach (IVariableDeclarationOperation declaration in declarationGroup.Declarations) {
                AddOwnershipOperationsInEvaluationOrder(declaration, seenSyntax, syntaxValues);
            }
            return;
        } else if (operation is IVariableDeclarationOperation variableDeclaration) {
            foreach (IVariableDeclaratorOperation declarator in variableDeclaration.Declarators) {
                AddOwnershipOperationsInEvaluationOrder(declarator, seenSyntax, syntaxValues);
            }
            return;
        } else if (operation is IVariableDeclaratorOperation variableDeclarator) {
            if (variableDeclarator.Initializer != null) {
                AddOwnershipOperationsInEvaluationOrder(variableDeclarator.Initializer.Value, seenSyntax, syntaxValues);
            }
            VariableDeclaratorSyntax declarationSyntax = variableDeclarator.Syntax.DescendantNodesAndSelf()
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            if (declarationSyntax != null && seenSyntax.Add(declarationSyntax)) {
                syntaxValues.Add(declarationSyntax);
            }
            return;
        }

        foreach (IOperation childOperation in operation.ChildOperations) {
            AddOwnershipOperationsInEvaluationOrder(childOperation, seenSyntax, syntaxValues);
        }

        SyntaxNode syntax = null;
        if (operation is IInvocationOperation && operation.Syntax is InvocationExpressionSyntax) {
            syntax = operation.Syntax;
        } else if (operation is IObjectCreationOperation && operation.Syntax is BaseObjectCreationExpressionSyntax) {
            syntax = operation.Syntax;
        } else if (operation is ISimpleAssignmentOperation && operation.Syntax is VariableDeclaratorSyntax) {
            syntax = operation.Syntax;
        } else if (operation is ISimpleAssignmentOperation && operation.Syntax is AssignmentExpressionSyntax) {
            syntax = operation.Syntax;
        } else if (operation is IReturnOperation && operation.Syntax is ReturnStatementSyntax) {
            syntax = operation.Syntax;
        }
        if (syntax != null && seenSyntax.Add(syntax)) {
            syntaxValues.Add(syntax);
        }
    }

    /// <summary>
    /// Validates one invocation's receiver and direct arguments after nested argument expressions have executed.
    /// </summary>
    /// <param name="invocation">Invocation whose direct uses should be checked.</param>
    /// <param name="method">Containing method.</param>
    /// <param name="state">Current local ownership state.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void ValidateInvocationUses(
        IInvocationOperation invocation,
        IMethodSymbol method,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (invocation.Instance != null) {
            ValidateDeadLocalUsesAfterNestedOwnershipOperations(invocation.Instance, method, state, diagnostics);
        }
        foreach (IArgumentOperation argument in invocation.Arguments) {
            ValidateDeadLocalUsesAfterNestedOwnershipOperations(argument.Value, method, state, diagnostics);
        }
    }

    /// <summary>
    /// Reports dead local references evaluated by the current expression while excluding nested calls and assignments validated at their own evaluation points.
    /// </summary>
    /// <param name="operation">Current expression operation whose non-boundary descendants should be validated.</param>
    /// <param name="method">Containing method.</param>
    /// <param name="state">Current local ownership state.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void ValidateDeadLocalUsesAfterNestedOwnershipOperations(
        IOperation operation,
        IMethodSymbol method,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (operation is IInvocationOperation ||
            operation is IObjectCreationOperation ||
            operation is IAssignmentOperation) {
            return;
        }
        if (operation is ILocalReferenceOperation localReference) {
            ValidateDeadLocalUse(localReference, method, state, diagnostics);
            return;
        }

        foreach (IOperation childOperation in operation.ChildOperations) {
            ValidateDeadLocalUsesAfterNestedOwnershipOperations(childOperation, method, state, diagnostics);
        }
    }

    /// <summary>
    /// Resolves a representative source node for one control-flow join.
    /// </summary>
    /// <param name="block">Basic block requiring a source location.</param>
    /// <param name="methodDeclaration">Fallback source method.</param>
    /// <returns>The first block operation, branch value, or method declaration.</returns>
    static SyntaxNode ResolveBlockSyntax(BasicBlock block, SyntaxNode methodDeclaration) {
        if (block.Operations.Length > 0) {
            return block.Operations[0].Syntax;
        } else if (block.BranchValue != null) {
            return block.BranchValue.Syntax;
        }

        return methodDeclaration;
    }

    /// <summary>
    /// Reports local references whose release or transfer already ended their lifetime.
    /// </summary>
    /// <param name="operation">Operation whose local references should be validated.</param>
    /// <param name="method">Containing method.</param>
    /// <param name="state">Current local ownership state.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void ValidateDeadLocalUses(
        IOperation operation,
        IMethodSymbol method,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        foreach (ILocalReferenceOperation localReference in operation.DescendantsAndSelf().OfType<ILocalReferenceOperation>()) {
            ValidateDeadLocalUse(localReference, method, state, diagnostics);
        }
    }

    /// <summary>
    /// Reports one local reference when its native lifetime has already ended.
    /// </summary>
    /// <param name="localReference">Local reference evaluated at the current point.</param>
    /// <param name="method">Containing method.</param>
    /// <param name="state">Current local ownership state.</param>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    void ValidateDeadLocalUse(
        ILocalReferenceOperation localReference,
        IMethodSymbol method,
        IDictionary<ILocalSymbol, CPPLocalOwnershipState> state,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (!state.TryGetValue(localReference.Local, out CPPLocalOwnershipState localState) ||
            localState.Lifecycle == CPPOwnershipLifecycle.Live ||
            localState.Lifecycle == CPPOwnershipLifecycle.TransferredRetained) {
            return;
        }

        AddDiagnostic(diagnostics, DiagnosticFactory.Create(
            "CPPOWN004",
            localReference.Syntax,
            method,
            $"Local '{localReference.Local.Name}' is used after its native lifetime became {localState.Lifecycle.ToString().ToLowerInvariant()}.",
            "Move the use before the release or transfer, or assign a new owned value before using the local."));
    }

    /// <summary>
    /// Resolves one target method summary when available.
    /// </summary>
    /// <param name="method">Target method.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <returns>The target summary, or null for an unreviewed boundary.</returns>
    static CPPMethodOwnershipSummary ResolveSummary(
        IMethodSymbol method,
        CPPMethodOwnershipSummaryResolution summaries) {
        summaries.Summaries.TryGetValue(CPPMethodOwnershipKey.Create(method), out CPPMethodOwnershipSummary summary);
        return summary;
    }

    /// <summary>
    /// Resolves the native ownership behavior of one target parameter.
    /// </summary>
    /// <param name="parameter">Target parameter.</param>
    /// <param name="summary">Resolved target summary when available.</param>
    /// <returns>The verified parameter ownership behavior.</returns>
    CPPParameterOwnershipKind ResolveParameterOwnership(
        IParameterSymbol parameter,
        CPPMethodOwnershipSummary summary) {
        if (parameter == null) {
            return CPPParameterOwnershipKind.Unknown;
        }
        if (HasAttribute(parameter, "NativeTakesOwnership")) {
            return CPPParameterOwnershipKind.TakesOwnership;
        }
        if (IntrinsicCatalog.TryGetParameterOwnership(parameter, out CPPParameterOwnershipKind intrinsicOwnership)) {
            return intrinsicOwnership;
        }
        return summary != null
            ? summary.GetParameterOwnership(parameter.Ordinal)
            : CPPParameterOwnershipKind.Unknown;
    }

    /// <summary>
    /// Determines whether one invocation is an explicit native cleanup helper.
    /// </summary>
    /// <param name="method">Invoked method.</param>
    /// <returns><c>true</c> for native delete or release helpers.</returns>
    static bool IsNativeRelease(IMethodSymbol method) {
        return string.Equals(method.ContainingType?.Name, "NativeOwnership", StringComparison.Ordinal) &&
            (string.Equals(method.Name, "Delete", StringComparison.Ordinal) ||
             string.Equals(method.Name, "Release", StringComparison.Ordinal) ||
             string.Equals(method.Name, "DisposeAndDelete", StringComparison.Ordinal) ||
             string.Equals(method.Name, "DisposeAndRelease", StringComparison.Ordinal) ||
             string.Equals(method.Name, "DeleteItemsAndRelease", StringComparison.Ordinal) ||
             string.Equals(method.Name, "DisposeItemsAndRelease", StringComparison.Ordinal));
    }

    /// <summary>
    /// Determines whether one operation is a constant null value.
    /// </summary>
    /// <param name="operation">Operation to inspect.</param>
    /// <returns><c>true</c> when the operation has a constant null value.</returns>
    static bool IsNullOperation(IOperation operation) {
        return operation.ConstantValue.HasValue && operation.ConstantValue.Value == null;
    }

    /// <summary>
    /// Determines whether one assignment target is a field or property.
    /// </summary>
    /// <param name="target">Assignment target operation.</param>
    /// <returns><c>true</c> when the target can retain a value beyond the call.</returns>
    static bool IsOrdinaryMemberTarget(IOperation target) {
        return target is IFieldReferenceOperation || target is IPropertyReferenceOperation;
    }

    /// <summary>
    /// Resolves the field or property symbol represented by an assignment target.
    /// </summary>
    /// <param name="target">Assignment target operation.</param>
    /// <returns>The member symbol, or null for a non-member target.</returns>
    static ISymbol ResolveMemberSymbol(IOperation target) {
        if (target is IFieldReferenceOperation fieldReference) {
            return fieldReference.Field;
        } else if (target is IPropertyReferenceOperation propertyReference) {
            return propertyReference.Property;
        }

        return null;
    }

    /// <summary>
    /// Removes transparent conversions and parentheses to resolve a direct local reference.
    /// </summary>
    /// <param name="operation">Value operation to unwrap.</param>
    /// <returns>The direct local reference, or null when the value is not one local.</returns>
    static ILocalReferenceOperation UnwrapLocalReference(IOperation operation) {
        while (true) {
            if (operation is IConversionOperation conversion) {
                operation = conversion.Operand;
            } else if (operation is IParenthesizedOperation parenthesized) {
                operation = parenthesized.Operand;
            } else {
                break;
            }
        }

        return operation as ILocalReferenceOperation;
    }

    /// <summary>
    /// Determines whether a syntax node belongs to a lambda, delegate, or local function nested in the method.
    /// </summary>
    /// <param name="syntax">Syntax node to inspect.</param>
    /// <param name="methodDeclaration">Containing top-level method.</param>
    /// <returns><c>true</c> when the node executes in a nested executable body.</returns>
    static bool IsInsideNestedExecutable(SyntaxNode syntax, SyntaxNode methodDeclaration) {
        return syntax.Ancestors()
            .TakeWhile(ancestor => !ReferenceEquals(ancestor, methodDeclaration))
            .Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax || ancestor is LocalFunctionStatementSyntax);
    }

    /// <summary>
    /// Creates a deterministic generated C++ ownership flag identifier.
    /// </summary>
    /// <param name="declaration">Source local declaration.</param>
    /// <returns>A stable identifier containing local name and source offset.</returns>
    static string CreateOwnershipFlagName(VariableDeclaratorSyntax declaration) {
        string sanitizedName = new string(declaration.Identifier.Text
            .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
            .ToArray());
        return $"__owns_{sanitizedName}_{declaration.SpanStart:X8}";
    }

    /// <summary>
    /// Clones one mutable method state with Roslyn symbol identity semantics.
    /// </summary>
    /// <param name="state">State to clone.</param>
    /// <returns>An independent dictionary containing the same immutable values.</returns>
    static Dictionary<ILocalSymbol, CPPLocalOwnershipState> CloneState(
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipState> state) {
        Dictionary<ILocalSymbol, CPPLocalOwnershipState> result = new(SymbolEqualityComparer.Default);
        foreach (KeyValuePair<ILocalSymbol, CPPLocalOwnershipState> value in state) {
            result[value.Key] = value.Value;
        }

        return result;
    }

    /// <summary>
    /// Determines whether two method states contain identical local semantic values.
    /// </summary>
    /// <param name="left">First method state.</param>
    /// <param name="right">Second method state.</param>
    /// <returns><c>true</c> when both states contain the same local values.</returns>
    static bool StatesEqual(
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipState> left,
        IReadOnlyDictionary<ILocalSymbol, CPPLocalOwnershipState> right) {
        if (left.Count != right.Count) {
            return false;
        }
        foreach (KeyValuePair<ILocalSymbol, CPPLocalOwnershipState> value in left) {
            if (!right.TryGetValue(value.Key, out CPPLocalOwnershipState other) ||
                !value.Value.SemanticallyEquals(other)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds one transition unless the same kind, local, and exact source site already exists.
    /// </summary>
    /// <param name="transitions">Aggregate ownership transitions.</param>
    /// <param name="transition">Transition candidate.</param>
    static void AddTransition(
        ICollection<CPPOwnershipTransition> transitions,
        CPPOwnershipTransition transition) {
        if (!transitions.Any(existing =>
            existing.Kind == transition.Kind &&
            ReferenceEquals(existing.LocalDeclaration, transition.LocalDeclaration) &&
            ReferenceEquals(existing.Syntax, transition.Syntax))) {
            transitions.Add(transition);
        }
    }

    /// <summary>
    /// Adds one diagnostic unless its code and exact source span were already reported.
    /// </summary>
    /// <param name="diagnostics">Aggregate ownership diagnostics.</param>
    /// <param name="diagnostic">Diagnostic candidate.</param>
    static void AddDiagnostic(
        ICollection<CPPConversionDiagnostic> diagnostics,
        CPPConversionDiagnostic diagnostic) {
        if (!diagnostics.Any(existing =>
            string.Equals(existing.Code, diagnostic.Code, StringComparison.Ordinal) &&
            string.Equals(existing.FilePath, diagnostic.FilePath, StringComparison.Ordinal) &&
            existing.LineNumber == diagnostic.LineNumber &&
            existing.ColumnNumber == diagnostic.ColumnNumber)) {
            diagnostics.Add(diagnostic);
        }
    }

    /// <summary>
    /// Determines whether one symbol carries an ownership contract name with or without the attribute suffix.
    /// </summary>
    /// <param name="symbol">Symbol whose attributes should be inspected.</param>
    /// <param name="contractName">Contract name without the conventional suffix.</param>
    /// <returns><c>true</c> when the contract is present.</returns>
    static bool HasAttribute(ISymbol symbol, string contractName) {
        if (symbol == null) {
            return false;
        }
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
