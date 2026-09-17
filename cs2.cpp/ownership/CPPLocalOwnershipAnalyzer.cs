using cs2.core.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace cs2.cpp;

/// <summary>
/// Coordinates native pointer ownership analysis for every source method and owned member before C++ lowering.
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
    /// Analyzes all source method bodies and owned members in the supplied compilations.
    /// </summary>
    /// <param name="compilations">Roslyn compilations participating in one generated native program.</param>
    /// <param name="summaries">Previously resolved method return and parameter ownership contracts.</param>
    /// <returns>Local plans, ownership transitions, and all hard semantic errors.</returns>
    public CPPOwnershipAnalysisResult Analyze(
        IReadOnlyList<Compilation> compilations,
        CPPMethodOwnershipSummaryResolution summaries) {
        return Analyze(compilations, summaries, 1);
    }

    /// <summary>
    /// Analyzes all source method bodies and owned members, running each syntax tree on a pool worker and merging in tree order.
    /// </summary>
    /// <param name="compilations">Roslyn compilations participating in one generated native program.</param>
    /// <param name="summaries">Previously resolved method return and parameter ownership contracts.</param>
    /// <param name="workerCount">Dedicated threads used for per-tree analysis.</param>
    /// <returns>Local plans, ownership transitions, and all hard semantic errors.</returns>
    public CPPOwnershipAnalysisResult Analyze(
        IReadOnlyList<Compilation> compilations,
        CPPMethodOwnershipSummaryResolution summaries,
        int workerCount) {
        if (compilations == null) {
            throw new ArgumentNullException(nameof(compilations));
        }
        if (summaries == null) {
            throw new ArgumentNullException(nameof(summaries));
        }

        List<Compilation> treeCompilations = [];
        List<SyntaxTree> trees = [];
        foreach (Compilation compilation in compilations) {
            if (compilation == null) {
                throw new ArgumentException("Ownership analysis cannot contain a null compilation.", nameof(compilations));
            }

            foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
                treeCompilations.Add(compilation);
                trees.Add(syntaxTree);
            }
        }

        CPPOwnershipTreeAnalysis[] analyses = new CPPOwnershipTreeAnalysis[trees.Count];
        ConversionWorkerPool pool = new ConversionWorkerPool(workerCount);
        pool.Run(trees.Count, (workerIndex, itemIndex) => {
            analyses[itemIndex] = AnalyzeTree(treeCompilations[itemIndex], trees[itemIndex], summaries);
        });

        Dictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans = [];
        List<CPPOwnershipTransition> transitions = [];
        List<CPPConversionDiagnostic> diagnostics = [];
        for (int index = 0; index < analyses.Length; index++) {
            CPPOwnershipTreeAnalysis analysis = analyses[index];
            foreach (KeyValuePair<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlan in analysis.LocalPlans) {
                localPlans[localPlan.Key] = localPlan.Value;
            }

            transitions.AddRange(analysis.Transitions);
            diagnostics.AddRange(analysis.Diagnostics);
        }

        CPPOwnershipAnalysisResult localResult = new CPPOwnershipAnalysisResult(
            summaries,
            new CPPOwnershipEmissionPlan(localPlans, transitions),
            diagnostics);
        IReadOnlyList<CPPConversionDiagnostic> memberDiagnostics = new CPPOwnedMemberContractValidator(
            ExpressionClassifier,
            new CPPIntrinsicOwnershipCatalog(),
            DiagnosticFactory).Validate(compilations, localResult);
        return new CPPOwnershipAnalysisResult(
            summaries,
            localResult.EmissionPlan,
            diagnostics.Concat(memberDiagnostics).ToArray());
    }

    /// <summary>
    /// Analyzes every executable declaration in one syntax tree into a private aggregate.
    /// </summary>
    /// <param name="compilation">Compilation that owns the tree.</param>
    /// <param name="syntaxTree">Tree to analyze.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <returns>Plans, transitions and diagnostics for this tree only.</returns>
    CPPOwnershipTreeAnalysis AnalyzeTree(Compilation compilation, SyntaxTree syntaxTree, CPPMethodOwnershipSummaryResolution summaries) {
        CPPOwnershipTreeAnalysis analysis = new CPPOwnershipTreeAnalysis();
        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
        SyntaxNode root = syntaxTree.GetRoot();
        foreach (BaseMethodDeclarationSyntax methodDeclaration in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>()) {
            AnalyzeExecutable(methodDeclaration, semanticModel, summaries, analysis.LocalPlans, analysis.Transitions, analysis.Diagnostics);
        }
        foreach (AccessorDeclarationSyntax accessorDeclaration in root.DescendantNodes().OfType<AccessorDeclarationSyntax>()) {
            AnalyzeExecutable(accessorDeclaration, semanticModel, summaries, analysis.LocalPlans, analysis.Transitions, analysis.Diagnostics);
        }

        return analysis;
    }

    /// <summary>
    /// Builds and analyzes the Roslyn control-flow graph for one executable method body.
    /// </summary>
    /// <param name="executableDeclaration">Source executable declaration to analyze.</param>
    /// <param name="semanticModel">Semantic model for the method source tree.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="localPlans">Mutable aggregate of local emission plans.</param>
    /// <param name="transitions">Mutable aggregate of ownership transitions.</param>
    /// <param name="diagnostics">Mutable aggregate of hard ownership errors.</param>
    void AnalyzeExecutable(
        SyntaxNode executableDeclaration,
        SemanticModel semanticModel,
        CPPMethodOwnershipSummaryResolution summaries,
        IDictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        if (!HasExecutableBody(executableDeclaration)) {
            return;
        }

        IMethodSymbol method = semanticModel.GetDeclaredSymbol(executableDeclaration) as IMethodSymbol;
        if (method == null) {
            return;
        }

        ControlFlowGraph controlFlowGraph = ControlFlowGraph.Create(executableDeclaration, semanticModel);
        CPPMethodControlFlowOwnershipAnalyzer methodAnalyzer = new(
            ExpressionClassifier,
            DiagnosticFactory);
        methodAnalyzer.Analyze(
            executableDeclaration,
            semanticModel,
            controlFlowGraph,
            method,
            summaries,
            localPlans,
            transitions,
            diagnostics);
        AnalyzeLocalFunctions(
            executableDeclaration,
            semanticModel,
            controlFlowGraph,
            methodAnalyzer,
            summaries,
            localPlans,
            transitions,
            diagnostics);
    }

    /// <summary>
    /// Analyzes local-function CFGs exposed by their containing Roslyn control-flow graph.
    /// </summary>
    /// <param name="executableDeclaration">Executable declaration containing local functions.</param>
    /// <param name="semanticModel">Semantic model for local-function symbols.</param>
    /// <param name="parentGraph">Containing executable's control-flow graph.</param>
    /// <param name="methodAnalyzer">CFG analyzer shared with the containing executable.</param>
    /// <param name="summaries">Resolved ownership contracts.</param>
    /// <param name="localPlans">Mutable aggregate of local emission plans.</param>
    /// <param name="transitions">Mutable aggregate of ownership transitions.</param>
    /// <param name="diagnostics">Mutable aggregate of hard ownership errors.</param>
    void AnalyzeLocalFunctions(
        SyntaxNode executableDeclaration,
        SemanticModel semanticModel,
        ControlFlowGraph parentGraph,
        CPPMethodControlFlowOwnershipAnalyzer methodAnalyzer,
        CPPMethodOwnershipSummaryResolution summaries,
        IDictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans,
        ICollection<CPPOwnershipTransition> transitions,
        ICollection<CPPConversionDiagnostic> diagnostics) {
        foreach (LocalFunctionStatementSyntax localFunction in executableDeclaration.DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()) {
            SyntaxNode nearestExecutableAncestor = localFunction.Ancestors()
                .First(ancestor => ReferenceEquals(ancestor, executableDeclaration) || ancestor is LocalFunctionStatementSyntax);
            if (!ReferenceEquals(nearestExecutableAncestor, executableDeclaration)) {
                continue;
            }

            IMethodSymbol localMethod = semanticModel.GetDeclaredSymbol(localFunction) as IMethodSymbol;
            if (localMethod == null) {
                continue;
            }

            ControlFlowGraph localGraph = parentGraph.GetLocalFunctionControlFlowGraph(localMethod);
            methodAnalyzer.Analyze(
                localFunction,
                semanticModel,
                localGraph,
                localMethod,
                summaries,
                localPlans,
                transitions,
                diagnostics);
            AnalyzeLocalFunctions(
                localFunction,
                semanticModel,
                localGraph,
                methodAnalyzer,
                summaries,
                localPlans,
                transitions,
                diagnostics);
        }
    }

    /// <summary>
    /// Determines whether one supported executable declaration has a block or expression body.
    /// </summary>
    /// <param name="declaration">Method, constructor, accessor, operator, destructor, or local function declaration.</param>
    /// <returns><c>true</c> when Roslyn can build a control-flow graph for the declaration.</returns>
    static bool HasExecutableBody(SyntaxNode declaration) {
        if (declaration is BaseMethodDeclarationSyntax methodDeclaration) {
            return methodDeclaration.Body != null || methodDeclaration.ExpressionBody != null;
        } else if (declaration is AccessorDeclarationSyntax accessorDeclaration) {
            return accessorDeclaration.Body != null || accessorDeclaration.ExpressionBody != null;
        }

        return false;
    }
}
