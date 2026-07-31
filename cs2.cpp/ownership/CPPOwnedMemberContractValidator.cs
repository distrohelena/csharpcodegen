using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace cs2.cpp;

/// <summary>
/// Verifies that each native-owned member is assigned owned values, cleaned before replacement, and released by disposal.
/// </summary>
public sealed class CPPOwnedMemberContractValidator {
    /// <summary>
    /// Classifies values assigned into owned members.
    /// </summary>
    readonly CPPOwnershipExpressionClassifier ExpressionClassifier;

    /// <summary>
    /// Resolves explicit parameter contracts that can establish assignment provenance without circular inference.
    /// </summary>
    readonly CPPIntrinsicOwnershipCatalog IntrinsicCatalog;

    /// <summary>
    /// Creates source-located member-contract diagnostics.
    /// </summary>
    readonly CPPOwnershipDiagnosticFactory DiagnosticFactory;

    /// <summary>
    /// Initializes a validator with explicit semantic collaborators.
    /// </summary>
    /// <param name="expressionClassifier">Classifier used for assigned values.</param>
    /// <param name="intrinsicCatalog">Catalog used to resolve explicit takes-ownership parameter boundaries.</param>
    /// <param name="diagnosticFactory">Factory used for source-located hard errors.</param>
    public CPPOwnedMemberContractValidator(
        CPPOwnershipExpressionClassifier expressionClassifier,
        CPPIntrinsicOwnershipCatalog intrinsicCatalog,
        CPPOwnershipDiagnosticFactory diagnosticFactory) {
        ExpressionClassifier = expressionClassifier ?? throw new ArgumentNullException(nameof(expressionClassifier));
        IntrinsicCatalog = intrinsicCatalog ?? throw new ArgumentNullException(nameof(intrinsicCatalog));
        DiagnosticFactory = diagnosticFactory ?? throw new ArgumentNullException(nameof(diagnosticFactory));
    }

    /// <summary>
    /// Validates every source member carrying the native-owned contract.
    /// </summary>
    /// <param name="compilations">Compilations participating in the generated native program.</param>
    /// <param name="analysis">Completed local ownership analysis used to classify local assignment sources.</param>
    /// <returns>Hard errors for incomplete or contradictory owned-member lifecycles.</returns>
    public IReadOnlyList<CPPConversionDiagnostic> Validate(
        IReadOnlyList<Compilation> compilations,
        CPPOwnershipAnalysisResult analysis) {
        if (compilations == null) {
            throw new ArgumentNullException(nameof(compilations));
        }
        if (analysis == null) {
            throw new ArgumentNullException(nameof(analysis));
        }

        List<CPPConversionDiagnostic> diagnostics = [];
        foreach (Compilation compilation in compilations) {
            ValidateCompilation(compilation, analysis, diagnostics);
        }
        ValidateAnnotatedAssignmentsAcrossCompilations(compilations, analysis, diagnostics);

        return diagnostics;
    }

    /// <summary>
    /// Validates annotated member writes in every participating compilation, including metadata references to another project.
    /// </summary>
    /// <param name="compilations">All compilations participating in the generated native program.</param>
    /// <param name="analysis">Local ownership transitions used for exact local-to-member transfers.</param>
    /// <param name="diagnostics">Aggregate member diagnostics.</param>
    void ValidateAnnotatedAssignmentsAcrossCompilations(
        IReadOnlyList<Compilation> compilations,
        CPPOwnershipAnalysisResult analysis,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        foreach (Compilation compilation in compilations) {
            foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
                SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
                foreach (AssignmentExpressionSyntax assignmentSyntax in syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()) {
                    IAssignmentOperation assignment = semanticModel.GetOperation(assignmentSyntax) as IAssignmentOperation;
                    ISymbol targetMember = ResolveMemberSymbol(assignment?.Target);
                    if (!HasAttribute(targetMember, "NativeOwnedMember")) {
                        continue;
                    }

                    IMethodSymbol method = semanticModel.GetEnclosingSymbol(assignmentSyntax.SpanStart) as IMethodSymbol;
                    if (method == null) {
                        continue;
                    }

                    ValidateAssignedOwnership(
                        assignmentSyntax,
                        semanticModel,
                        method,
                        analysis,
                        diagnostics);
                    if (!SymbolEqualityComparer.Default.Equals(targetMember.ContainingAssembly, compilation.Assembly)) {
                        SyntaxNode executableDeclaration = assignmentSyntax.Ancestors()
                            .FirstOrDefault(ancestor => ancestor is BaseMethodDeclarationSyntax || ancestor is AccessorDeclarationSyntax);
                        if (executableDeclaration != null) {
                            ValidateMethod(
                                targetMember,
                                executableDeclaration,
                                semanticModel,
                                method,
                                false,
                                true,
                                analysis,
                                diagnostics);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Locates and validates native-owned fields and properties in one compilation.
    /// </summary>
    /// <param name="compilation">Compilation whose source members should be validated.</param>
    /// <param name="analysis">Local ownership plans used for assignment classification.</param>
    /// <param name="diagnostics">Aggregate member diagnostics.</param>
    void ValidateCompilation(
        Compilation compilation,
        CPPOwnershipAnalysisResult analysis,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
            SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (FieldDeclarationSyntax fieldDeclaration in syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<FieldDeclarationSyntax>()) {
                foreach (VariableDeclaratorSyntax variable in fieldDeclaration.Declaration.Variables) {
                    IFieldSymbol field = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                    if (HasAttribute(field, "NativeOwnedMember")) {
                        ValidateMember(field, variable, compilation, analysis, diagnostics);
                    }
                }
            }
            foreach (PropertyDeclarationSyntax propertyDeclaration in syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()) {
                IPropertySymbol property = semanticModel.GetDeclaredSymbol(propertyDeclaration) as IPropertySymbol;
                if (HasAttribute(property, "NativeOwnedMember")) {
                    ValidateMember(property, propertyDeclaration, compilation, analysis, diagnostics);
                }
            }
        }
    }

    /// <summary>
    /// Validates assignments and disposal paths for one native-owned member symbol.
    /// </summary>
    /// <param name="member">Owned field or property symbol.</param>
    /// <param name="declarationSyntax">Source declaration used when disposal is absent.</param>
    /// <param name="compilation">Compilation containing every potential assignment site.</param>
    /// <param name="analysis">Local ownership plans used for assigned local values.</param>
    /// <param name="diagnostics">Aggregate member diagnostics.</param>
    void ValidateMember(
        ISymbol member,
        SyntaxNode declarationSyntax,
        Compilation compilation,
        CPPOwnershipAnalysisResult analysis,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        bool memberStartsInitialized = ValidateMemberInitializer(
            member,
            declarationSyntax,
            compilation.GetSemanticModel(declarationSyntax.SyntaxTree),
            analysis,
            diagnostics);
        MethodDeclarationSyntax disposeDeclaration = null;
        foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
            SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (BaseMethodDeclarationSyntax methodDeclaration in syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<BaseMethodDeclarationSyntax>()) {
                if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null) {
                    continue;
                }

                IMethodSymbol method = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
                if (method == null || !MethodReferencesMember(methodDeclaration, semanticModel, member, method)) {
                    continue;
                }
                bool isDispose = methodDeclaration is MethodDeclarationSyntax &&
                    SymbolEqualityComparer.Default.Equals(method.ContainingType, member.ContainingType) &&
                    string.Equals(method.Name, "Dispose", StringComparison.Ordinal) &&
                    method.Parameters.Length == 0;
                if (isDispose) {
                    disposeDeclaration = (MethodDeclarationSyntax)methodDeclaration;
                }

                ValidateMethod(
                    member,
                    methodDeclaration,
                    semanticModel,
                    method,
                    isDispose,
                    memberStartsInitialized,
                    analysis,
                    diagnostics);
            }
            foreach (AccessorDeclarationSyntax accessorDeclaration in syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<AccessorDeclarationSyntax>()) {
                if (accessorDeclaration.Body == null && accessorDeclaration.ExpressionBody == null) {
                    continue;
                }

                IMethodSymbol accessor = semanticModel.GetDeclaredSymbol(accessorDeclaration) as IMethodSymbol;
                if (accessor == null || !MethodReferencesMember(accessorDeclaration, semanticModel, member, accessor)) {
                    continue;
                }

                ValidateMethod(
                    member,
                    accessorDeclaration,
                    semanticModel,
                    accessor,
                    false,
                    memberStartsInitialized,
                    analysis,
                    diagnostics);
            }
        }

        if (disposeDeclaration == null) {
            AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                "CPPOWN007",
                declarationSyntax,
                member,
                $"Native-owned member '{member.Name}' has no parameterless Dispose method proving final cleanup.",
                "Add Dispose() and release the member on every normal exit."));
        }
    }

    /// <summary>
    /// Validates a field or property initializer and reports whether constructors begin with an existing value.
    /// </summary>
    /// <param name="member">Owned field or property symbol.</param>
    /// <param name="declarationSyntax">Source declaration containing the initializer.</param>
    /// <param name="semanticModel">Semantic model for initializer ownership.</param>
    /// <param name="analysis">Resolved method ownership contracts.</param>
    /// <param name="diagnostics">Aggregate member diagnostics.</param>
    /// <returns><c>true</c> when the member initializer establishes a non-null value before constructors run.</returns>
    bool ValidateMemberInitializer(
        ISymbol member,
        SyntaxNode declarationSyntax,
        SemanticModel semanticModel,
        CPPOwnershipAnalysisResult analysis,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        EqualsValueClauseSyntax initializer = null;
        if (declarationSyntax is VariableDeclaratorSyntax variableDeclaration) {
            initializer = variableDeclaration.Initializer;
        } else if (declarationSyntax is PropertyDeclarationSyntax propertyDeclaration) {
            initializer = propertyDeclaration.Initializer;
        }
        if (initializer == null) {
            return false;
        }

        IOperation initializerOperation = semanticModel.GetOperation(initializer.Value);
        if (initializerOperation.ConstantValue.HasValue && initializerOperation.ConstantValue.Value == null) {
            return false;
        }

        CPPOwnershipKind ownership = ExpressionClassifier.Classify(
            initializerOperation,
            analysis.MethodSummaries.Summaries);
        if (ownership != CPPOwnershipKind.Owned) {
            AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                "CPPOWN006",
                initializer,
                member,
                $"Native-owned member initializer receives a {ownership.ToString().ToLowerInvariant()} value.",
                "Initialize the member only with a fresh or explicitly owned value."));
        }

        return true;
    }

    /// <summary>
    /// Determines whether one method references the owned member or is its containing type's disposal boundary.
    /// </summary>
    /// <param name="methodDeclaration">Method source to inspect.</param>
    /// <param name="semanticModel">Semantic model for member references.</param>
    /// <param name="member">Owned member being validated.</param>
    /// <param name="method">Resolved method symbol.</param>
    /// <returns><c>true</c> when the method can affect the member proof.</returns>
    static bool MethodReferencesMember(
        SyntaxNode methodDeclaration,
        SemanticModel semanticModel,
        ISymbol member,
        IMethodSymbol method) {
        bool isDispose = SymbolEqualityComparer.Default.Equals(method.ContainingType, member.ContainingType) &&
            string.Equals(method.Name, "Dispose", StringComparison.Ordinal) &&
            method.Parameters.Length == 0;
        if (isDispose) {
            return true;
        }

        foreach (IdentifierNameSyntax identifier in methodDeclaration.DescendantNodes().OfType<IdentifierNameSyntax>()) {
            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier).Symbol, member)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Propagates one member's released state through a method and validates each replacement and disposal exit.
    /// </summary>
    /// <param name="member">Owned member being tracked.</param>
    /// <param name="methodDeclaration">Method declaration to validate.</param>
    /// <param name="semanticModel">Semantic model for method operations.</param>
    /// <param name="method">Resolved method symbol.</param>
    /// <param name="isDispose">Whether the method is the required disposal boundary.</param>
    /// <param name="memberStartsInitialized">Whether a declaration initializer creates the member before constructors run.</param>
    /// <param name="analysis">Local ownership plans used for assignment classification.</param>
    /// <param name="diagnostics">Aggregate member diagnostics.</param>
    void ValidateMethod(
        ISymbol member,
        SyntaxNode methodDeclaration,
        SemanticModel semanticModel,
        IMethodSymbol method,
        bool isDispose,
        bool memberStartsInitialized,
        CPPOwnershipAnalysisResult analysis,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        bool delegatesToThisConstructor = methodDeclaration is ConstructorDeclarationSyntax constructorDeclaration &&
            constructorDeclaration.Initializer?.IsKind(SyntaxKind.ThisConstructorInitializer) == true;
        bool startsReleased = method.MethodKind == MethodKind.Constructor &&
            !memberStartsInitialized &&
            !delegatesToThisConstructor;
        ControlFlowGraph graph = ControlFlowGraph.Create(methodDeclaration, semanticModel);
        Dictionary<BasicBlock, bool> outputStates = ResolveReleaseStates(
            graph,
            member,
            semanticModel,
            startsReleased);
        foreach (BasicBlock block in graph.Blocks) {
            if (!block.IsReachable || !CanResolveInput(block, outputStates)) {
                continue;
            }

            bool released = ResolveInputState(
                graph,
                block,
                outputStates,
                startsReleased,
                member,
                semanticModel);
            foreach (SyntaxNode syntax in GetMemberSyntax(block)) {
                if (syntax is InvocationExpressionSyntax invocation && IsMemberRelease(invocation, semanticModel, member)) {
                    released = true;
                } else if (syntax is AssignmentExpressionSyntax assignment && IsMemberAssignment(assignment, semanticModel, member)) {
                    ValidateAssignedOwnership(assignment, semanticModel, method, analysis, diagnostics);
                    if (!released) {
                        AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                            "CPPOWN007",
                            assignment,
                            method,
                            $"Native-owned member '{member.Name}' is replaced before its prior value is released.",
                            "Release the existing member value on every path before assigning its replacement."));
                    }
                    released = false;
                }
            }
        }

        ValidateFinalizationRegions(graph, member, semanticModel, method, analysis, outputStates, diagnostics);

        if (isDispose) {
            BasicBlock exitBlock = graph.Blocks.Single(block => block.Kind == BasicBlockKind.Exit);
            bool releasedAtExit = ResolveInputState(
                graph,
                exitBlock,
                outputStates,
                false,
                member,
                semanticModel);
            if (!releasedAtExit) {
                AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                    "CPPOWN007",
                    methodDeclaration,
                    method,
                    $"Dispose does not release native-owned member '{member.Name}' on every normal exit.",
                    "Release the member before every normal Dispose exit."));
            }
        }
    }

    /// <summary>
    /// Computes whether the member is released at each block exit to a fixed point.
    /// </summary>
    /// <param name="graph">Method control-flow graph.</param>
    /// <param name="member">Owned member being tracked.</param>
    /// <param name="semanticModel">Semantic model for block operations.</param>
    /// <param name="startsReleased">Whether the method begins without a prior member value, as constructors do.</param>
    /// <returns>Released state at each reachable block output.</returns>
    static Dictionary<BasicBlock, bool> ResolveReleaseStates(
        ControlFlowGraph graph,
        ISymbol member,
        SemanticModel semanticModel,
        bool startsReleased) {
        Dictionary<BasicBlock, bool> outputStates = [];
        int iterationLimit = Math.Max(8, graph.Blocks.Length * 8);
        for (int iteration = 0; iteration < iterationLimit; iteration++) {
            bool changed = false;
            foreach (BasicBlock block in graph.Blocks) {
                if (!block.IsReachable || !CanResolveInput(block, outputStates)) {
                    continue;
                }

                bool released = ResolveInputState(
                    graph,
                    block,
                    outputStates,
                    startsReleased,
                    member,
                    semanticModel);
                foreach (SyntaxNode syntax in GetMemberSyntax(block)) {
                    if (syntax is InvocationExpressionSyntax invocation && IsMemberRelease(invocation, semanticModel, member)) {
                        released = true;
                    } else if (syntax is AssignmentExpressionSyntax assignment && IsMemberAssignment(assignment, semanticModel, member)) {
                        released = false;
                    }
                }

                if (!outputStates.TryGetValue(block, out bool previous) || previous != released) {
                    outputStates[block] = released;
                    changed = true;
                }
            }
            if (!changed) {
                return outputStates;
            }
        }

        throw new InvalidOperationException("Native-owned member control-flow validation did not converge.");
    }

    /// <summary>
    /// Determines whether enough predecessor state exists to process one basic block.
    /// </summary>
    /// <param name="block">Block whose input should be checked.</param>
    /// <param name="outputStates">Known predecessor output states.</param>
    /// <returns><c>true</c> for entry blocks or blocks with at least one known reachable predecessor.</returns>
    static bool CanResolveInput(BasicBlock block, IReadOnlyDictionary<BasicBlock, bool> outputStates) {
        return block.Kind == BasicBlockKind.Entry ||
            block.Predecessors.Any(predecessor => predecessor.Source.IsReachable && outputStates.ContainsKey(predecessor.Source));
    }

    /// <summary>
    /// Computes the must-be-released state entering one block by intersecting reachable predecessors.
    /// </summary>
    /// <param name="graph">Method graph containing finalization regions.</param>
    /// <param name="block">Block receiving predecessor states.</param>
    /// <param name="outputStates">Known predecessor output states.</param>
    /// <param name="startsReleased">Entry state used by constructors.</param>
    /// <param name="member">Owned member whose finalization operations should be applied.</param>
    /// <param name="semanticModel">Semantic model for finalization operations.</param>
    /// <returns><c>true</c> only when every known incoming path has released the member.</returns>
    static bool ResolveInputState(
        ControlFlowGraph graph,
        BasicBlock block,
        IReadOnlyDictionary<BasicBlock, bool> outputStates,
        bool startsReleased,
        ISymbol member,
        SemanticModel semanticModel) {
        if (block.Kind == BasicBlockKind.Entry) {
            return startsReleased;
        }

        bool[] predecessorStates = block.Predecessors
            .Where(predecessor => predecessor.Source.IsReachable && outputStates.ContainsKey(predecessor.Source))
            .Select(predecessor => ApplyMemberFinalizationRegions(
                graph,
                predecessor,
                outputStates[predecessor.Source],
                member,
                semanticModel))
            .ToArray();
        return predecessorStates.Length > 0 && predecessorStates.All(released => released);
    }

    /// <summary>
    /// Applies member release and replacement operations in finally regions carried by one branch.
    /// </summary>
    /// <param name="graph">Method graph containing the finalization blocks.</param>
    /// <param name="branch">Branch whose finalization regions execute.</param>
    /// <param name="released">Member release state leaving the branch source.</param>
    /// <param name="member">Owned member being tracked.</param>
    /// <param name="semanticModel">Semantic model for finalization operations.</param>
    /// <returns>The member release state after every finally region executes.</returns>
    static bool ApplyMemberFinalizationRegions(
        ControlFlowGraph graph,
        ControlFlowBranch branch,
        bool released,
        ISymbol member,
        SemanticModel semanticModel) {
        foreach (ControlFlowRegion finallyRegion in branch.FinallyRegions) {
            released = ApplyMemberFinalizationRegion(
                graph,
                finallyRegion,
                released,
                member,
                semanticModel);
        }

        return released;
    }

    /// <summary>
    /// Propagates must-be-released state through one finally-region CFG.
    /// </summary>
    /// <param name="graph">Method graph containing the finally region.</param>
    /// <param name="finallyRegion">Finally region to execute.</param>
    /// <param name="sourceReleased">Release state entering the region.</param>
    /// <param name="member">Owned member being tracked.</param>
    /// <param name="semanticModel">Semantic model for region operations.</param>
    /// <returns><c>true</c> only when every executable region exit has released the member.</returns>
    static bool ApplyMemberFinalizationRegion(
        ControlFlowGraph graph,
        ControlFlowRegion finallyRegion,
        bool sourceReleased,
        ISymbol member,
        SemanticModel semanticModel) {
        Dictionary<BasicBlock, bool> regionOutputs = [];
        int regionBlockCount = finallyRegion.LastBlockOrdinal - finallyRegion.FirstBlockOrdinal + 1;
        int iterationLimit = Math.Max(8, regionBlockCount * 8);
        for (int iteration = 0; iteration < iterationLimit; iteration++) {
            bool changed = false;
            for (int blockOrdinal = finallyRegion.FirstBlockOrdinal;
                 blockOrdinal <= finallyRegion.LastBlockOrdinal;
                 blockOrdinal++) {
                BasicBlock block = graph.Blocks[blockOrdinal];
                List<bool> incomingStates = [];
                if (blockOrdinal == finallyRegion.FirstBlockOrdinal) {
                    incomingStates.Add(sourceReleased);
                }
                foreach (ControlFlowBranch predecessor in block.Predecessors) {
                    if (predecessor.Source.Ordinal >= finallyRegion.FirstBlockOrdinal &&
                        predecessor.Source.Ordinal <= finallyRegion.LastBlockOrdinal &&
                        regionOutputs.TryGetValue(predecessor.Source, out bool predecessorReleased)) {
                        incomingStates.Add(predecessorReleased);
                    }
                }
                if (incomingStates.Count == 0) {
                    continue;
                }

                bool released = incomingStates.All(value => value);
                foreach (SyntaxNode syntax in GetMemberSyntax(block)) {
                    if (syntax is InvocationExpressionSyntax invocation && IsMemberRelease(invocation, semanticModel, member)) {
                        released = true;
                    } else if (syntax is AssignmentExpressionSyntax assignment && IsMemberAssignment(assignment, semanticModel, member)) {
                        released = false;
                    }
                }
                if (!regionOutputs.TryGetValue(block, out bool priorReleased) || priorReleased != released) {
                    regionOutputs[block] = released;
                    changed = true;
                }
            }
            if (!changed) {
                break;
            }
            if (iteration == iterationLimit - 1) {
                throw new InvalidOperationException("Native-owned member finalization analysis did not converge.");
            }
        }

        bool[] exitStates = regionOutputs
            .Where(output => GetSuccessorBranches(output.Key).Any(successor =>
                successor.Destination == null ||
                successor.Destination.Ordinal < finallyRegion.FirstBlockOrdinal ||
                successor.Destination.Ordinal > finallyRegion.LastBlockOrdinal))
            .Select(output => output.Value)
            .ToArray();
        if (exitStates.Length == 0 && regionOutputs.TryGetValue(graph.Blocks[finallyRegion.LastBlockOrdinal], out bool lastReleased)) {
            return lastReleased;
        }

        return exitStates.Length > 0 && exitStates.All(value => value);
    }

    /// <summary>
    /// Validates owned-member assignments that execute through Roslyn finalization edges.
    /// </summary>
    /// <param name="graph">Method graph containing finalization blocks.</param>
    /// <param name="member">Owned member being tracked.</param>
    /// <param name="semanticModel">Semantic model for finalization operations.</param>
    /// <param name="method">Containing method symbol.</param>
    /// <param name="analysis">Local ownership plans used for assignment classification.</param>
    /// <param name="outputStates">Stable block output release states.</param>
    /// <param name="diagnostics">Aggregate member diagnostics.</param>
    void ValidateFinalizationRegions(
        ControlFlowGraph graph,
        ISymbol member,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPOwnershipAnalysisResult analysis,
        IReadOnlyDictionary<BasicBlock, bool> outputStates,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        foreach (BasicBlock block in graph.Blocks) {
            if (!outputStates.TryGetValue(block, out bool released)) {
                continue;
            }

            foreach (ControlFlowBranch branch in GetSuccessorBranches(block)) {
                foreach (ControlFlowRegion finallyRegion in branch.FinallyRegions) {
                    ValidateMemberFinalizationRegion(
                        graph,
                        finallyRegion,
                        released,
                        member,
                        semanticModel,
                        method,
                        analysis,
                        diagnostics);
                }
            }
        }
    }

    /// <summary>
    /// Validates replacements inside one finally region using the region's branch-sensitive must-release state.
    /// </summary>
    /// <param name="graph">Method graph containing the finally region.</param>
    /// <param name="finallyRegion">Finally region to validate.</param>
    /// <param name="sourceReleased">Release state entering the region.</param>
    /// <param name="member">Owned member being tracked.</param>
    /// <param name="semanticModel">Semantic model for region operations.</param>
    /// <param name="method">Containing method symbol.</param>
    /// <param name="analysis">Local ownership transitions used for assigned locals.</param>
    /// <param name="diagnostics">Aggregate member diagnostics.</param>
    void ValidateMemberFinalizationRegion(
        ControlFlowGraph graph,
        ControlFlowRegion finallyRegion,
        bool sourceReleased,
        ISymbol member,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPOwnershipAnalysisResult analysis,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        Dictionary<BasicBlock, bool> regionOutputs = [];
        int regionBlockCount = finallyRegion.LastBlockOrdinal - finallyRegion.FirstBlockOrdinal + 1;
        int iterationLimit = Math.Max(8, regionBlockCount * 8);
        for (int iteration = 0; iteration < iterationLimit; iteration++) {
            bool changed = false;
            for (int blockOrdinal = finallyRegion.FirstBlockOrdinal;
                 blockOrdinal <= finallyRegion.LastBlockOrdinal;
                 blockOrdinal++) {
                BasicBlock block = graph.Blocks[blockOrdinal];
                List<bool> incomingStates = [];
                if (blockOrdinal == finallyRegion.FirstBlockOrdinal) {
                    incomingStates.Add(sourceReleased);
                }
                foreach (ControlFlowBranch predecessor in block.Predecessors) {
                    if (predecessor.Source.Ordinal >= finallyRegion.FirstBlockOrdinal &&
                        predecessor.Source.Ordinal <= finallyRegion.LastBlockOrdinal &&
                        regionOutputs.TryGetValue(predecessor.Source, out bool predecessorReleased)) {
                        incomingStates.Add(predecessorReleased);
                    }
                }
                if (incomingStates.Count == 0) {
                    continue;
                }

                bool released = incomingStates.All(value => value);
                foreach (SyntaxNode syntax in GetMemberSyntax(block)) {
                    if (syntax is InvocationExpressionSyntax invocation && IsMemberRelease(invocation, semanticModel, member)) {
                        released = true;
                    } else if (syntax is AssignmentExpressionSyntax assignment && IsMemberAssignment(assignment, semanticModel, member)) {
                        ValidateAssignedOwnership(assignment, semanticModel, method, analysis, diagnostics);
                        if (!released) {
                            AddDiagnostic(diagnostics, DiagnosticFactory.Create(
                                "CPPOWN007",
                                assignment,
                                method,
                                $"Native-owned member '{member.Name}' is replaced before its prior value is released.",
                                "Release the existing member value on every path before assigning its replacement."));
                        }
                        released = false;
                    }
                }
                if (!regionOutputs.TryGetValue(block, out bool priorReleased) || priorReleased != released) {
                    regionOutputs[block] = released;
                    changed = true;
                }
            }
            if (!changed) {
                return;
            }
            if (iteration == iterationLimit - 1) {
                throw new InvalidOperationException("Native-owned member finalization validation did not converge.");
            }
        }
    }

    /// <summary>
    /// Enumerates the non-null outgoing branches for one basic block.
    /// </summary>
    /// <param name="block">Block whose successors should be inspected.</param>
    /// <returns>Fall-through and conditional branches without duplicates.</returns>
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
    /// Validates that one value assigned into an owned member is itself proven owned.
    /// </summary>
    /// <param name="assignmentSyntax">Owned-member assignment source.</param>
    /// <param name="semanticModel">Semantic model for the assigned expression.</param>
    /// <param name="method">Containing method symbol.</param>
    /// <param name="analysis">Local ownership plans used for direct local references.</param>
    /// <param name="diagnostics">Aggregate member diagnostics.</param>
    void ValidateAssignedOwnership(
        AssignmentExpressionSyntax assignmentSyntax,
        SemanticModel semanticModel,
        IMethodSymbol method,
        CPPOwnershipAnalysisResult analysis,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        IAssignmentOperation assignment = semanticModel.GetOperation(assignmentSyntax) as IAssignmentOperation;
        CPPOwnershipKind ownership = ExpressionClassifier.Classify(
            assignment.Value,
            analysis.MethodSummaries.Summaries);
        IOperation assignedValue = UnwrapConversion(assignment.Value);
        if (assignedValue is IParameterReferenceOperation parameterReference &&
            IntrinsicCatalog.TryGetParameterOwnership(parameterReference.Parameter, out CPPParameterOwnershipKind parameterOwnership) &&
            parameterOwnership == CPPParameterOwnershipKind.TakesOwnership) {
            ownership = CPPOwnershipKind.Owned;
        } else if (assignedValue is ILocalReferenceOperation &&
            analysis.EmissionPlan.TryGetTransition(assignmentSyntax, out CPPOwnershipTransition transition) &&
            transition.Kind == CPPOwnershipTransitionKind.Transfer) {
            ownership = CPPOwnershipKind.Owned;
        }
        if (ownership == CPPOwnershipKind.Owned) {
            return;
        }

        AddDiagnostic(diagnostics, DiagnosticFactory.Create(
            "CPPOWN006",
            assignmentSyntax,
            method,
            $"Native-owned member assignment receives a {ownership.ToString().ToLowerInvariant()} value.",
            "Assign only a fresh or explicitly owned value to a native-owned member."));
    }

    /// <summary>
    /// Determines whether one assignment targets the exact owned member symbol.
    /// </summary>
    /// <param name="assignmentSyntax">Assignment source syntax.</param>
    /// <param name="semanticModel">Semantic model for the assignment.</param>
    /// <param name="member">Owned member to match.</param>
    /// <returns><c>true</c> when the assignment targets the member.</returns>
    static bool IsMemberAssignment(
        AssignmentExpressionSyntax assignmentSyntax,
        SemanticModel semanticModel,
        ISymbol member) {
        IAssignmentOperation assignment = semanticModel.GetOperation(assignmentSyntax) as IAssignmentOperation;
        ISymbol targetMember = ResolveMemberSymbol(assignment?.Target);
        return SymbolEqualityComparer.Default.Equals(targetMember, member);
    }

    /// <summary>
    /// Determines whether one native cleanup invocation releases the exact owned member symbol.
    /// </summary>
    /// <param name="invocationSyntax">Invocation source syntax.</param>
    /// <param name="semanticModel">Semantic model for the invocation.</param>
    /// <param name="member">Owned member to match.</param>
    /// <returns><c>true</c> when the invocation releases the member.</returns>
    static bool IsMemberRelease(
        InvocationExpressionSyntax invocationSyntax,
        SemanticModel semanticModel,
        ISymbol member) {
        IInvocationOperation invocation = semanticModel.GetOperation(invocationSyntax) as IInvocationOperation;
        if (invocation == null ||
            invocation.Arguments.Length == 0 ||
            !string.Equals(invocation.TargetMethod.ContainingType?.Name, "NativeOwnership", StringComparison.Ordinal) ||
            (!string.Equals(invocation.TargetMethod.Name, "Delete", StringComparison.Ordinal) &&
             !string.Equals(invocation.TargetMethod.Name, "Release", StringComparison.Ordinal) &&
             !string.Equals(invocation.TargetMethod.Name, "DisposeAndDelete", StringComparison.Ordinal) &&
             !string.Equals(invocation.TargetMethod.Name, "DisposeAndRelease", StringComparison.Ordinal))) {
            return false;
        }

        ISymbol argumentMember = ResolveMemberSymbol(UnwrapConversion(invocation.Arguments[0].Value));
        return SymbolEqualityComparer.Default.Equals(argumentMember, member);
    }

    /// <summary>
    /// Collects member assignments and native cleanup calls represented by one basic block.
    /// </summary>
    /// <param name="block">Basic block to inspect.</param>
    /// <returns>Unique member-relevant syntax in source order.</returns>
    static IReadOnlyList<SyntaxNode> GetMemberSyntax(BasicBlock block) {
        HashSet<SyntaxNode> syntaxValues = [];
        foreach (IOperation operation in block.Operations) {
            foreach (IOperation descendant in operation.DescendantsAndSelf()) {
                if (descendant is IAssignmentOperation && descendant.Syntax is AssignmentExpressionSyntax assignment) {
                    syntaxValues.Add(assignment);
                } else if (descendant is IInvocationOperation && descendant.Syntax is InvocationExpressionSyntax invocation) {
                    syntaxValues.Add(invocation);
                }
            }
        }

        return syntaxValues.OrderBy(syntax => syntax.SpanStart)
            .ThenByDescending(syntax => syntax.Span.Length)
            .ToArray();
    }

    /// <summary>
    /// Removes transparent conversions from an operation.
    /// </summary>
    /// <param name="operation">Operation to unwrap.</param>
    /// <returns>The first non-conversion operation.</returns>
    static IOperation UnwrapConversion(IOperation operation) {
        while (true) {
            if (operation is IConversionOperation conversion) {
                operation = conversion.Operand;
            } else if (operation is IParenthesizedOperation parenthesized) {
                operation = parenthesized.Operand;
            } else {
                break;
            }
        }

        return operation;
    }

    /// <summary>
    /// Resolves a field or property symbol from one operation.
    /// </summary>
    /// <param name="operation">Member-reference operation.</param>
    /// <returns>The referenced field or property, or null.</returns>
    static ISymbol ResolveMemberSymbol(IOperation operation) {
        if (operation is IFieldReferenceOperation fieldReference) {
            return fieldReference.Field;
        } else if (operation is IPropertyReferenceOperation propertyReference) {
            return propertyReference.Property;
        }

        return null;
    }

    /// <summary>
    /// Adds one diagnostic unless its code and exact source coordinates were already reported.
    /// </summary>
    /// <param name="diagnostics">Aggregate member diagnostics.</param>
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
    /// <param name="contractName">Contract name without the suffix.</param>
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
