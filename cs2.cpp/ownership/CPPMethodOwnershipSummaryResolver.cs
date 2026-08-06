using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace cs2.cpp;

/// <summary>
/// Resolves source-visible native ownership contracts across a call graph until no method summary changes.
/// </summary>
public sealed class CPPMethodOwnershipSummaryResolver {
    /// <summary>
    /// Stores expression ownership rules shared by return analysis and call propagation.
    /// </summary>
    readonly CPPOwnershipExpressionClassifier ExpressionClassifier;

    /// <summary>
    /// Stores parameter ownership contracts for semantic attributes and framework boundaries.
    /// </summary>
    readonly CPPIntrinsicOwnershipCatalog IntrinsicCatalog;

    /// <summary>
    /// Creates actionable source-located diagnostics for unresolved or contradictory contracts.
    /// </summary>
    readonly CPPOwnershipDiagnosticFactory DiagnosticFactory;

    /// <summary>
    /// Stores semantic models for the active resolution run keyed by their source trees.
    /// </summary>
    IReadOnlyDictionary<SyntaxTree, SemanticModel> SemanticModels;

    /// <summary>
    /// Initializes a resolver with the standard ownership classifier, intrinsic catalog, and diagnostic factory.
    /// </summary>
    public CPPMethodOwnershipSummaryResolver()
        : this(new CPPOwnershipExpressionClassifier(), new CPPIntrinsicOwnershipCatalog(), new CPPOwnershipDiagnosticFactory()) {
    }

    /// <summary>
    /// Initializes a resolver with explicit semantic ownership collaborators.
    /// </summary>
    /// <param name="expressionClassifier">Classifier for value-producing expressions.</param>
    /// <param name="intrinsicCatalog">Reviewed framework and parameter contracts.</param>
    /// <param name="diagnosticFactory">Factory for source-located hard errors.</param>
    public CPPMethodOwnershipSummaryResolver(
        CPPOwnershipExpressionClassifier expressionClassifier,
        CPPIntrinsicOwnershipCatalog intrinsicCatalog,
        CPPOwnershipDiagnosticFactory diagnosticFactory) {
        ExpressionClassifier = expressionClassifier ?? throw new ArgumentNullException(nameof(expressionClassifier));
        IntrinsicCatalog = intrinsicCatalog ?? throw new ArgumentNullException(nameof(intrinsicCatalog));
        DiagnosticFactory = diagnosticFactory ?? throw new ArgumentNullException(nameof(diagnosticFactory));
    }

    /// <summary>
    /// Resolves return and parameter ownership summaries for all source-visible methods in the supplied compilation closure.
    /// </summary>
    /// <param name="compilations">Root and referenced Roslyn compilations participating in one conversion.</param>
    /// <returns>Fixed-point summaries and every ownership hard error discovered while validating them.</returns>
    public CPPMethodOwnershipSummaryResolution Resolve(IReadOnlyList<Compilation> compilations) {
        if (compilations == null) {
            throw new ArgumentNullException(nameof(compilations));
        }

        SemanticModels = CollectSemanticModels(compilations);
        Dictionary<string, IMethodSymbol> sourceMethods = CollectSourceMethods(compilations);
        Dictionary<string, CPPMethodOwnershipSummary> summaries = CreateInitialSummaries(sourceMethods);
        bool changed;
        do {
            changed = false;
            foreach (KeyValuePair<string, IMethodSymbol> sourceMethod in sourceMethods.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
                CPPMethodOwnershipSummary updatedSummary = ResolveMethodSummary(sourceMethod.Value, summaries, sourceMethods);
                if (!SummariesMatch(summaries[sourceMethod.Key], updatedSummary)) {
                    summaries[sourceMethod.Key] = updatedSummary;
                    changed = true;
                }
            }
        } while (changed);

        List<CPPConversionDiagnostic> diagnostics = ValidateResolvedSummaries(sourceMethods, summaries);
        return new CPPMethodOwnershipSummaryResolution(summaries, diagnostics);
    }

    /// <summary>
    /// Collects one semantic model for every source tree participating in ownership analysis.
    /// </summary>
    /// <param name="compilations">Compilation closure to enumerate.</param>
    /// <returns>Semantic models keyed by their exact source trees.</returns>
    static IReadOnlyDictionary<SyntaxTree, SemanticModel> CollectSemanticModels(IReadOnlyList<Compilation> compilations) {
        Dictionary<SyntaxTree, SemanticModel> semanticModels = new Dictionary<SyntaxTree, SemanticModel>();
        foreach (Compilation compilation in compilations) {
            if (compilation == null) {
                throw new ArgumentException("Ownership analysis received a null compilation.", nameof(compilations));
            }

            foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
                semanticModels[syntaxTree] = compilation.GetSemanticModel(syntaxTree);
            }
        }

        return semanticModels;
    }

    /// <summary>
    /// Collects every source method, constructor, accessor, operator, and local function once by stable identity.
    /// </summary>
    /// <param name="compilations">Compilation closure to enumerate.</param>
    /// <returns>Source method symbols keyed by stable ownership identity.</returns>
    static Dictionary<string, IMethodSymbol> CollectSourceMethods(IReadOnlyList<Compilation> compilations) {
        Dictionary<string, IMethodSymbol> methods = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
        foreach (Compilation compilation in compilations) {
            if (compilation == null) {
                throw new ArgumentException("Ownership analysis received a null compilation.", nameof(compilations));
            }

            foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
                SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
                foreach (SyntaxNode declaration in syntaxTree.GetRoot().DescendantNodesAndSelf().Where(IsMethodDeclarationSyntax)) {
                    IMethodSymbol method = ResolveDeclaredMethod(semanticModel, declaration);
                    if (method == null) {
                        continue;
                    }

                    string methodKey = CPPMethodOwnershipKey.Create(method);
                    methods[methodKey] = method;
                }
            }
        }

        return methods;
    }

    /// <summary>
    /// Creates provisional summaries from declared boundary contracts before source inference begins.
    /// </summary>
    /// <param name="sourceMethods">Source methods participating in fixed-point resolution.</param>
    /// <returns>Initial summaries keyed by stable method identity.</returns>
    Dictionary<string, CPPMethodOwnershipSummary> CreateInitialSummaries(
        IReadOnlyDictionary<string, IMethodSymbol> sourceMethods) {
        Dictionary<string, CPPMethodOwnershipSummary> summaries = new Dictionary<string, CPPMethodOwnershipSummary>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IMethodSymbol> sourceMethod in sourceMethods) {
            IMethodSymbol method = sourceMethod.Value;
            CPPOwnershipKind returnOwnership = ExpressionClassifier.TryGetDeclaredReturnOwnership(method, out CPPOwnershipKind declaredReturn)
                ? declaredReturn
                : CPPOwnershipKind.Unknown;
            Dictionary<int, CPPParameterOwnershipKind> parameterOwnership = new Dictionary<int, CPPParameterOwnershipKind>();
            foreach (IParameterSymbol parameter in method.Parameters) {
                if (IntrinsicCatalog.TryGetParameterOwnership(parameter, out CPPParameterOwnershipKind declaredParameter)) {
                    parameterOwnership[parameter.Ordinal] = declaredParameter;
                } else {
                    parameterOwnership[parameter.Ordinal] = CPPParameterOwnershipKind.Unknown;
                }
            }

            summaries[sourceMethod.Key] = new CPPMethodOwnershipSummary(
                sourceMethod.Key,
                CPPOwnershipTypeClassifier.RequiresClassification(method.ReturnType),
                returnOwnership,
                parameterOwnership);
        }

        return summaries;
    }

    /// <summary>
    /// Resolves one source method against the current call-graph summary state.
    /// </summary>
    /// <param name="method">Source method to analyze.</param>
    /// <param name="summaries">Current fixed-point summaries.</param>
    /// <param name="sourceMethods">Source-visible method identities.</param>
    /// <returns>The next monotonic summary for the method.</returns>
    CPPMethodOwnershipSummary ResolveMethodSummary(
        IMethodSymbol method,
        IReadOnlyDictionary<string, CPPMethodOwnershipSummary> summaries,
        IReadOnlyDictionary<string, IMethodSymbol> sourceMethods) {
        string methodKey = CPPMethodOwnershipKey.Create(method);
        bool hasBody = HasSourceBody(method);
        bool hasDeclaredReturn = ExpressionClassifier.TryGetDeclaredReturnOwnership(method, out CPPOwnershipKind declaredReturn);
        CPPOwnershipKind inferredReturn = hasBody && CPPOwnershipTypeClassifier.RequiresClassification(method.ReturnType)
            ? InferReturnOwnership(method, summaries, sourceMethods, out _, out _, out _)
            : CPPOwnershipKind.Unknown;
        CPPOwnershipKind returnOwnership = hasDeclaredReturn ? declaredReturn : inferredReturn;

        Dictionary<int, CPPParameterOwnershipKind> parameterOwnership = new Dictionary<int, CPPParameterOwnershipKind>();
        foreach (IParameterSymbol parameter in method.Parameters) {
            if (!CPPOwnershipTypeClassifier.RequiresClassification(parameter.Type)) {
                parameterOwnership[parameter.Ordinal] = CPPParameterOwnershipKind.Unknown;
                continue;
            }

            bool hasDeclaredParameter = IntrinsicCatalog.TryGetParameterOwnership(parameter, out CPPParameterOwnershipKind declaredParameter);
            CPPParameterOwnershipKind inferredParameter = hasBody
                ? InferParameterOwnership(method, parameter, summaries)
                : CPPParameterOwnershipKind.Unknown;
            parameterOwnership[parameter.Ordinal] = hasDeclaredParameter ? declaredParameter : inferredParameter;
        }

        return new CPPMethodOwnershipSummary(
            methodKey,
            CPPOwnershipTypeClassifier.RequiresClassification(method.ReturnType),
            returnOwnership,
            parameterOwnership);
    }

    /// <summary>
    /// Infers uniform non-null return ownership for one source method.
    /// </summary>
    /// <param name="method">Method whose return expressions should be analyzed.</param>
    /// <param name="summaries">Current fixed-point method summaries.</param>
    /// <param name="sourceMethods">Source-visible method identities used to distinguish deferred calls from unknown boundaries.</param>
    /// <param name="isMixed">Set when owned and borrowed non-null returns coexist.</param>
    /// <param name="hasUnknownBoundary">Set when a non-null return depends on an unclassified external boundary.</param>
    /// <param name="hasNonNullReturn">Set when at least one return path can produce a non-null value.</param>
    /// <returns>The inferred uniform return ownership.</returns>
    CPPOwnershipKind InferReturnOwnership(
        IMethodSymbol method,
        IReadOnlyDictionary<string, CPPMethodOwnershipSummary> summaries,
        IReadOnlyDictionary<string, IMethodSymbol> sourceMethods,
        out bool isMixed,
        out bool hasUnknownBoundary,
        out bool hasNonNullReturn) {
        if (ReturnsArrayAsOwnedNativeList(method)) {
            isMixed = false;
            hasUnknownBoundary = false;
            hasNonNullReturn = true;
            return CPPOwnershipKind.Owned;
        }

        int ownedCount = 0;
        int borrowedCount = 0;
        int unknownCount = 0;
        int deferredCount = 0;
        foreach (ExpressionSyntax returnExpression in GetReturnExpressions(method)) {
            SemanticModel semanticModel = ResolveSemanticModel(returnExpression.SyntaxTree);
            CollectReturnEvidence(
                method,
                returnExpression,
                semanticModel,
                summaries,
                sourceMethods,
                ref ownedCount,
                ref borrowedCount,
                ref unknownCount,
                ref deferredCount);
        }

        isMixed = ownedCount > 0 && borrowedCount > 0;
        hasUnknownBoundary = unknownCount > 0;
        hasNonNullReturn = ownedCount > 0 || borrowedCount > 0 || unknownCount > 0 || deferredCount > 0;
        if (isMixed || hasUnknownBoundary) {
            return CPPOwnershipKind.Unknown;
        } else if (ownedCount > 0) {
            return CPPOwnershipKind.Owned;
        } else if (borrowedCount > 0) {
            return CPPOwnershipKind.Borrowed;
        }

        return CPPOwnershipKind.Unknown;
    }

    /// <summary>
    /// Collects ownership evidence from one return expression, recursively expanding conditionals and local provenance.
    /// </summary>
    /// <param name="method">Method containing the return expression.</param>
    /// <param name="expression">Return expression or one conditional branch.</param>
    /// <param name="semanticModel">Semantic model that owns the expression.</param>
    /// <param name="summaries">Current fixed-point summaries.</param>
    /// <param name="sourceMethods">Source-visible method identities.</param>
    /// <param name="ownedCount">Owned evidence count.</param>
    /// <param name="borrowedCount">Borrowed evidence count.</param>
    /// <param name="unknownCount">Unclassified external evidence count.</param>
    /// <param name="deferredCount">Unresolved source-call evidence count.</param>
    void CollectReturnEvidence(
        IMethodSymbol method,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IReadOnlyDictionary<string, CPPMethodOwnershipSummary> summaries,
        IReadOnlyDictionary<string, IMethodSymbol> sourceMethods,
        ref int ownedCount,
        ref int borrowedCount,
        ref int unknownCount,
        ref int deferredCount) {
        if (expression == null) {
            return;
        }
        if (expression is ParenthesizedExpressionSyntax parenthesizedExpression) {
            CollectReturnEvidence(method, parenthesizedExpression.Expression, semanticModel, summaries, sourceMethods, ref ownedCount, ref borrowedCount, ref unknownCount, ref deferredCount);
            return;
        } else if (expression is CastExpressionSyntax castExpression) {
            CollectReturnEvidence(method, castExpression.Expression, semanticModel, summaries, sourceMethods, ref ownedCount, ref borrowedCount, ref unknownCount, ref deferredCount);
            return;
        } else if (expression is ConditionalExpressionSyntax conditionalExpression) {
            CollectReturnEvidence(method, conditionalExpression.WhenTrue, semanticModel, summaries, sourceMethods, ref ownedCount, ref borrowedCount, ref unknownCount, ref deferredCount);
            CollectReturnEvidence(method, conditionalExpression.WhenFalse, semanticModel, summaries, sourceMethods, ref ownedCount, ref borrowedCount, ref unknownCount, ref deferredCount);
            return;
        } else if (expression is SwitchExpressionSyntax switchExpression) {
            foreach (SwitchExpressionArmSyntax arm in switchExpression.Arms) {
                CollectReturnEvidence(method, arm.Expression, semanticModel, summaries, sourceMethods, ref ownedCount, ref borrowedCount, ref unknownCount, ref deferredCount);
            }
            return;
        }

        IOperation operation = semanticModel.GetOperation(expression);
        if (IsNullOperation(operation)) {
            return;
        }
        if (operation is ILocalReferenceOperation localReferenceOperation &&
            CPPLocalValueProvenanceResolver.TryResolveStableSourceExpression(
                method,
                localReferenceOperation.Local,
                semanticModel,
                out ExpressionSyntax sourceExpression)) {
            CollectReturnEvidence(method, sourceExpression, semanticModel, summaries, sourceMethods, ref ownedCount, ref borrowedCount, ref unknownCount, ref deferredCount);
            return;
        }

        CPPOwnershipKind ownership = ExpressionClassifier.Classify(operation, summaries);
        if (ownership == CPPOwnershipKind.Owned) {
            ownedCount++;
        } else if (ownership == CPPOwnershipKind.Borrowed) {
            borrowedCount++;
        } else if (operation is IInvocationOperation invocationOperation &&
                   sourceMethods.TryGetValue(CPPMethodOwnershipKey.Create(invocationOperation.TargetMethod), out IMethodSymbol deferredMethod) &&
                   HasSourceBody(deferredMethod)) {
            deferredCount++;
        } else {
            unknownCount++;
        }
    }

    /// <summary>
    /// Infers whether one source parameter remains local to the call, transfers ownership, or escapes ambiguously.
    /// </summary>
    /// <param name="method">Method containing the parameter.</param>
    /// <param name="parameter">Parameter whose uses should be summarized.</param>
    /// <param name="summaries">Current fixed-point method summaries.</param>
    /// <returns>The inferred parameter ownership behavior.</returns>
    CPPParameterOwnershipKind InferParameterOwnership(
        IMethodSymbol method,
        IParameterSymbol parameter,
        IReadOnlyDictionary<string, CPPMethodOwnershipSummary> summaries) {
        SyntaxNode methodDeclaration = GetMethodDeclaration(method);
        SemanticModel semanticModel = ResolveSemanticModel(methodDeclaration.SyntaxTree);
        bool takesOwnership = false;
        bool takesOwnershipRetained = false;
        foreach (IdentifierNameSyntax reference in methodDeclaration.DescendantNodes().OfType<IdentifierNameSyntax>()) {
            if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(reference).Symbol, parameter)) {
                continue;
            }
            if (IsInsideNameOfExpression(reference, semanticModel)) {
                continue;
            }
            if (reference.Ancestors().Any(IsNestedExecutableSyntax)) {
                return CPPParameterOwnershipKind.Escapes;
            }
            if (ReturnsParameterReference(reference, parameter, semanticModel)) {
                return CPPParameterOwnershipKind.Escapes;
            }
            AssignmentExpressionSyntax directAssignment = reference.AncestorsAndSelf()
                .OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(candidate =>
                    candidate.Right.Span.Contains(reference.Span) &&
                    AssignmentCarriesParameterIdentity(candidate.Right, parameter, semanticModel));
            if (directAssignment != null) {
                ISymbol destination = semanticModel.GetSymbolInfo(directAssignment.Left).Symbol;
                if (destination is IFieldSymbol || destination is IPropertySymbol) {
                    if (HasAttribute(destination, "NativeOwnedMember") &&
                        IntrinsicCatalog.TryGetParameterOwnership(parameter, out CPPParameterOwnershipKind memberDestinationOwnership) &&
                        memberDestinationOwnership == CPPParameterOwnershipKind.TakesOwnership) {
                        takesOwnership = true;
                        continue;
                    }

                    if (IsConstructedInstanceMemberAssignment(method, directAssignment, semanticModel)) {
                        return CPPParameterOwnershipKind.EscapesWithReturn;
                    }

                    return CPPParameterOwnershipKind.Escapes;
                }
            }

            ArgumentSyntax argument = reference.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
            if (argument == null) {
                continue;
            }
            if (semanticModel.GetOperation(argument) is not IArgumentOperation argumentOperation ||
                argumentOperation.Parameter == null) {
                return CPPParameterOwnershipKind.Unknown;
            }
            if (!IsDirectParameterArgument(argumentOperation, parameter)) {
                continue;
            }

            IMethodSymbol targetMethod = argumentOperation.Parameter.ContainingSymbol as IMethodSymbol;
            if (IsNativeOwnershipRelease(targetMethod)) {
                takesOwnership = true;
                continue;
            }
            if (IntrinsicCatalog.TryGetParameterOwnership(argumentOperation.Parameter, out CPPParameterOwnershipKind declaredOwnership)) {
                if (declaredOwnership == CPPParameterOwnershipKind.TakesOwnership) {
                    if (IntrinsicCatalog.IsOwnershipTransferringCollectionInsertion(targetMethod)) {
                        takesOwnershipRetained = true;
                    } else {
                        takesOwnership = true;
                    }
                } else if (declaredOwnership == CPPParameterOwnershipKind.RetainsBorrow) {
                    if (IntrinsicCatalog.TryGetParameterOwnership(parameter, out CPPParameterOwnershipKind sourceOwnership) &&
                        sourceOwnership == CPPParameterOwnershipKind.TakesOwnership) {
                        takesOwnership = true;
                        continue;
                    }

                    return CPPParameterOwnershipKind.Escapes;
                }
                continue;
            }
            if (targetMethod != null &&
                summaries.TryGetValue(CPPMethodOwnershipKey.Create(targetMethod), out CPPMethodOwnershipSummary targetSummary)) {
                CPPParameterOwnershipKind targetOwnership = targetSummary.GetParameterOwnership(argumentOperation.Parameter.Ordinal);
                if (targetOwnership == CPPParameterOwnershipKind.TakesOwnership) {
                    takesOwnership = true;
                    continue;
                } else if (targetOwnership == CPPParameterOwnershipKind.TakesOwnershipRetained) {
                    takesOwnershipRetained = true;
                    continue;
                } else if (targetOwnership == CPPParameterOwnershipKind.NoEscape) {
                    continue;
                } else if (targetOwnership == CPPParameterOwnershipKind.EscapesWithReturn) {
                    if (IsCallResultConfinedByUsing(argumentOperation)) {
                        continue;
                    }
                    if (IsCallResultReturned(argumentOperation) || method.MethodKind == MethodKind.Constructor) {
                        return CPPParameterOwnershipKind.EscapesWithReturn;
                    }

                    return CPPParameterOwnershipKind.Escapes;
                } else if (targetOwnership == CPPParameterOwnershipKind.Escapes) {
                    return CPPParameterOwnershipKind.Escapes;
                }
            }

            return CPPParameterOwnershipKind.Unknown;
        }

        if (takesOwnership) {
            return CPPParameterOwnershipKind.TakesOwnership;
        }

        return takesOwnershipRetained ? CPPParameterOwnershipKind.TakesOwnershipRetained : CPPParameterOwnershipKind.NoEscape;
    }

    /// <summary>
    /// Determines whether an assignment expression preserves one parameter's object identity instead of merely reading data from it.
    /// </summary>
    /// <param name="expression">Right-hand expression whose resulting reference may be stored.</param>
    /// <param name="parameter">Parameter whose identity must reach the assignment destination.</param>
    /// <param name="semanticModel">Semantic model used to resolve direct parameter references.</param>
    /// <returns><c>true</c> when the expression can evaluate to the original parameter reference.</returns>
    static bool AssignmentCarriesParameterIdentity(
        ExpressionSyntax expression,
        IParameterSymbol parameter,
        SemanticModel semanticModel) {
        if (expression is IdentifierNameSyntax identifier) {
            return SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier).Symbol, parameter);
        }
        if (expression is ParenthesizedExpressionSyntax parenthesized) {
            return AssignmentCarriesParameterIdentity(parenthesized.Expression, parameter, semanticModel);
        }
        if (expression is CastExpressionSyntax cast) {
            return AssignmentCarriesParameterIdentity(cast.Expression, parameter, semanticModel);
        }
        if (expression is PostfixUnaryExpressionSyntax postfix) {
            return AssignmentCarriesParameterIdentity(postfix.Operand, parameter, semanticModel);
        }
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.CoalesceExpression)) {
            return AssignmentCarriesParameterIdentity(binary.Left, parameter, semanticModel) ||
                AssignmentCarriesParameterIdentity(binary.Right, parameter, semanticModel);
        }
        if (expression is ConditionalExpressionSyntax conditional) {
            return AssignmentCarriesParameterIdentity(conditional.WhenTrue, parameter, semanticModel) ||
                AssignmentCarriesParameterIdentity(conditional.WhenFalse, parameter, semanticModel);
        }

        return false;
    }

    /// <summary>
    /// Determines whether a constructor stores its parameter only on the instance being constructed.
    /// </summary>
    /// <param name="method">Method containing the assignment.</param>
    /// <param name="assignment">Assignment that receives the parameter.</param>
    /// <param name="semanticModel">Semantic model that owns the assignment operation.</param>
    /// <returns><c>true</c> when the destination is an instance member on the constructed receiver.</returns>
    static bool IsConstructedInstanceMemberAssignment(
        IMethodSymbol method,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel) {
        if (method.MethodKind != MethodKind.Constructor ||
            semanticModel.GetOperation(assignment) is not ISimpleAssignmentOperation assignmentOperation) {
            return false;
        }

        IOperation instance = null;
        if (assignmentOperation.Target is IFieldReferenceOperation fieldReference && !fieldReference.Field.IsStatic) {
            instance = fieldReference.Instance;
        } else if (assignmentOperation.Target is IPropertyReferenceOperation propertyReference && !propertyReference.Property.IsStatic) {
            instance = propertyReference.Instance;
        }

        return instance is IInstanceReferenceOperation instanceReference &&
            instanceReference.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance;
    }

    /// <summary>
    /// Determines whether the object retaining one argument is immediately bounded by a using declaration or statement.
    /// </summary>
    /// <param name="argument">Argument whose target call produces the retaining object.</param>
    /// <returns><c>true</c> when the complete call initializes a local disposed by the containing method.</returns>
    static bool IsCallResultConfinedByUsing(IArgumentOperation argument) {
        IOperation call = ResolveContainingCall(argument);
        if (call == null) {
            return false;
        }

        VariableDeclaratorSyntax declaration = call.Syntax.AncestorsAndSelf()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(candidate => candidate.Initializer?.Value.Span.Contains(call.Syntax.Span) == true);
        if (declaration == null) {
            return false;
        }

        LocalDeclarationStatementSyntax localDeclaration = declaration.Ancestors().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (localDeclaration != null && !localDeclaration.UsingKeyword.IsKind(SyntaxKind.None)) {
            return true;
        }

        return declaration.Ancestors().OfType<UsingStatementSyntax>().Any();
    }

    /// <summary>
    /// Determines whether the object retaining one argument is returned directly to the current caller.
    /// </summary>
    /// <param name="argument">Argument whose target call produces the retaining object.</param>
    /// <returns><c>true</c> when the complete call contributes to a return expression.</returns>
    static bool IsCallResultReturned(IArgumentOperation argument) {
        IOperation call = ResolveContainingCall(argument);
        return call != null && call.Syntax.AncestorsAndSelf().OfType<ReturnStatementSyntax>()
            .Any(returnStatement => returnStatement.Expression?.Span.Contains(call.Syntax.Span) == true);
    }

    /// <summary>
    /// Resolves the invocation or object creation that owns one argument operation.
    /// </summary>
    /// <param name="argument">Argument operation to inspect.</param>
    /// <returns>The containing call operation, or null when Roslyn does not expose one.</returns>
    static IOperation ResolveContainingCall(IArgumentOperation argument) {
        IOperation operation = argument.Parent;
        while (operation is IConversionOperation || operation is IParenthesizedOperation) {
            operation = operation.Parent;
        }

        return operation is IInvocationOperation || operation is IObjectCreationOperation
            ? operation
            : null;
    }

    /// <summary>
    /// Determines whether one parameter identifier contributes only its source name to a <c>nameof</c> expression.
    /// </summary>
    /// <param name="reference">Parameter identifier use to inspect.</param>
    /// <param name="semanticModel">Semantic model that owns the identifier operation.</param>
    /// <returns><c>true</c> when the identifier is nested beneath a <c>nameof</c> operation that cannot evaluate or retain it.</returns>
    static bool IsInsideNameOfExpression(IdentifierNameSyntax reference, SemanticModel semanticModel) {
        IOperation operation = semanticModel.GetOperation(reference);
        while (operation != null) {
            if (operation.Kind == OperationKind.NameOf) {
                return true;
            }

            operation = operation.Parent;
        }

        return false;
    }

    /// <summary>
    /// Determines whether one argument passes the inspected parameter itself instead of merely using it inside a larger argument expression.
    /// </summary>
    /// <param name="argument">Semantic argument containing the source reference.</param>
    /// <param name="sourceParameter">Source parameter whose escape behavior is being inferred.</param>
    /// <returns><c>true</c> when conversion unwrapping exposes the source parameter as the complete argument value.</returns>
    static bool IsDirectParameterArgument(IArgumentOperation argument, IParameterSymbol sourceParameter) {
        IOperation value = argument.Value;
        while (value is IConversionOperation || value is IParenthesizedOperation) {
            if (value is IConversionOperation conversion) {
                value = conversion.Operand;
            } else {
                value = ((IParenthesizedOperation)value).Operand;
            }
        }

        return value is IParameterReferenceOperation parameterReference &&
            SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, sourceParameter);
    }

    /// <summary>
    /// Determines whether one symbol carries an ownership contract name with or without the conventional attribute suffix.
    /// </summary>
    /// <param name="symbol">Symbol whose source-visible attributes should be inspected.</param>
    /// <param name="contractName">Ownership contract name without the optional attribute suffix.</param>
    /// <returns><c>true</c> when the requested ownership contract is present; otherwise <c>false</c>.</returns>
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

    /// <summary>
    /// Validates final inferred summaries against hard-error and declared-contract requirements.
    /// </summary>
    /// <param name="sourceMethods">Source methods keyed by stable identity.</param>
    /// <param name="summaries">Final fixed-point summaries.</param>
    /// <returns>Ownership diagnostics in deterministic method order.</returns>
    List<CPPConversionDiagnostic> ValidateResolvedSummaries(
        IReadOnlyDictionary<string, IMethodSymbol> sourceMethods,
        IReadOnlyDictionary<string, CPPMethodOwnershipSummary> summaries) {
        List<CPPConversionDiagnostic> diagnostics = new List<CPPConversionDiagnostic>();
        foreach (KeyValuePair<string, IMethodSymbol> sourceMethod in sourceMethods.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            IMethodSymbol method = sourceMethod.Value;
            if (!HasSourceBody(method)) {
                continue;
            }

            SyntaxNode declaration = GetMethodDeclaration(method);
            bool requiresReturnOwnership = CPPOwnershipTypeClassifier.RequiresClassification(method.ReturnType);
            bool hasDeclaredReturn = ExpressionClassifier.TryGetDeclaredReturnOwnership(method, out CPPOwnershipKind declaredReturn);
            bool isMixed = false;
            bool hasUnknownBoundary = false;
            bool hasNonNullReturn = false;
            CPPOwnershipKind inferredReturn = requiresReturnOwnership
                ? InferReturnOwnership(method, summaries, sourceMethods, out isMixed, out hasUnknownBoundary, out hasNonNullReturn)
                : CPPOwnershipKind.Unknown;
            if (requiresReturnOwnership) {
                if (isMixed) {
                    diagnostics.Add(DiagnosticFactory.Create(
                        "CPPOWN005",
                        declaration,
                        method,
                        $"Method '{method.Name}' mixes owned and borrowed non-null return values.",
                        "Split the API so every non-null return path has one ownership contract."));
                } else if (hasDeclaredReturn && inferredReturn != CPPOwnershipKind.Unknown && inferredReturn != declaredReturn) {
                    diagnostics.Add(DiagnosticFactory.Create(
                        "CPPOWN006",
                        declaration,
                        method,
                        $"Declared {declaredReturn} return ownership contradicts inferred {inferredReturn} behavior for method '{method.Name}'.",
                        "Correct the source lifetime or change the annotation to match the inferred behavior."));
                } else if (!hasDeclaredReturn && hasUnknownBoundary && hasNonNullReturn) {
                    diagnostics.Add(DiagnosticFactory.Create(
                        "CPPOWN001",
                        declaration,
                        method,
                        $"Return ownership for method '{method.Name}' with return type '{method.ReturnType.ToDisplayString()}', type kind '{method.ReturnType.TypeKind}', special type '{method.ReturnType.SpecialType}', and reference flag '{method.ReturnType.IsReferenceType}' cannot be inferred because a non-null boundary is unclassified.",
                        "Declare owned or borrowed return ownership at the non-analyzable boundary."));
                }
            }

            foreach (IParameterSymbol parameter in method.Parameters.Where(parameter => CPPOwnershipTypeClassifier.RequiresClassification(parameter.Type))) {
                if (!IntrinsicCatalog.TryGetParameterOwnership(parameter, out CPPParameterOwnershipKind declaredParameter)) {
                    continue;
                }

                CPPParameterOwnershipKind inferredParameter = InferParameterOwnership(method, parameter, summaries);
                if (inferredParameter == declaredParameter) {
                    continue;
                }
                if (inferredParameter == CPPParameterOwnershipKind.Unknown) {
                    continue;
                }
                if (declaredParameter == CPPParameterOwnershipKind.TakesOwnership &&
                    inferredParameter == CPPParameterOwnershipKind.EscapesWithReturn) {
                    continue;
                }
                if (declaredParameter == CPPParameterOwnershipKind.TakesOwnership &&
                    inferredParameter == CPPParameterOwnershipKind.TakesOwnershipRetained) {
                    continue;
                }
                if (declaredParameter == CPPParameterOwnershipKind.RetainsBorrow &&
                    inferredParameter == CPPParameterOwnershipKind.TakesOwnershipRetained) {
                    continue;
                }
                if (declaredParameter == CPPParameterOwnershipKind.RetainsBorrow &&
                    (inferredParameter == CPPParameterOwnershipKind.Escapes ||
                     inferredParameter == CPPParameterOwnershipKind.EscapesWithReturn)) {
                    continue;
                }

                SyntaxNode parameterSyntax = parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? declaration;
                diagnostics.Add(DiagnosticFactory.Create(
                    "CPPOWN006",
                    parameterSyntax,
                    method,
                    $"Declared {declaredParameter} parameter ownership contradicts inferred {inferredParameter} behavior for '{parameter.Name}'.",
                    "Correct the parameter flow or change the annotation to match the inferred behavior."));
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// Determines whether two method summaries carry identical fixed-point state.
    /// </summary>
    /// <param name="left">Current summary.</param>
    /// <param name="right">Proposed summary.</param>
    /// <returns><c>true</c> when return and parameter contracts match.</returns>
    static bool SummariesMatch(CPPMethodOwnershipSummary left, CPPMethodOwnershipSummary right) {
        if (left.ReturnOwnership != right.ReturnOwnership ||
            left.RequiresReturnOwnership != right.RequiresReturnOwnership ||
            left.ParameterOwnership.Count != right.ParameterOwnership.Count) {
            return false;
        }

        foreach (KeyValuePair<int, CPPParameterOwnershipKind> parameter in left.ParameterOwnership) {
            if (right.GetParameterOwnership(parameter.Key) != parameter.Value) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether backend list-family lowering materializes a new native list from arrays returned by every source path.
    /// </summary>
    /// <param name="method">Source getter or method whose return boundary should be inspected.</param>
    /// <returns><c>true</c> when every returned expression has an array natural type and the declared return belongs to the list family.</returns>
    bool ReturnsArrayAsOwnedNativeList(IMethodSymbol method) {
        if (!IsListFamilyType(method.ReturnType)) {
            return false;
        }

        bool foundReturn = false;
        foreach (ExpressionSyntax expression in GetReturnExpressions(method)) {
            foundReturn = true;
            SemanticModel semanticModel = ResolveSemanticModel(expression.SyntaxTree);
            TypeInfo typeInfo = semanticModel.GetTypeInfo(expression);
            if (typeInfo.Type is not IArrayTypeSymbol) {
                return false;
            }
        }

        return foundReturn;
    }

    /// <summary>
    /// Determines whether one declared managed type lowers through the native list-family boundary.
    /// </summary>
    /// <param name="type">Declared method return type.</param>
    /// <returns><c>true</c> for supported mutable and read-only generic list interfaces.</returns>
    static bool IsListFamilyType(ITypeSymbol type) {
        if (type is not INamedTypeSymbol namedType) {
            return false;
        }

        string displayName = namedType.OriginalDefinition.ToDisplayString();
        return string.Equals(displayName, "System.Collections.Generic.List<T>", StringComparison.Ordinal) ||
            string.Equals(displayName, "System.Collections.Generic.IReadOnlyList<T>", StringComparison.Ordinal) ||
            string.Equals(displayName, "System.Collections.Generic.ICollection<T>", StringComparison.Ordinal) ||
            string.Equals(displayName, "System.Collections.Generic.IReadOnlyCollection<T>", StringComparison.Ordinal) ||
            string.Equals(displayName, "System.Collections.Generic.IEnumerable<T>", StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets every return expression belonging directly to one method body.
    /// </summary>
    /// <param name="method">Method whose returns should be enumerated.</param>
    /// <returns>Return expressions excluding nested lambdas and local functions.</returns>
    static IEnumerable<ExpressionSyntax> GetReturnExpressions(IMethodSymbol method) {
        SyntaxNode declaration = GetMethodDeclaration(method);
        ExpressionSyntax expressionBody = ResolveExpressionBody(declaration);
        if (expressionBody != null) {
            yield return expressionBody;
        }

        foreach (ReturnStatementSyntax returnStatement in declaration
            .DescendantNodes(node => ReferenceEquals(node, declaration) || !IsNestedExecutableSyntax(node))
            .OfType<ReturnStatementSyntax>()) {
            if (returnStatement.Expression != null) {
                yield return returnStatement.Expression;
            }
        }
    }

    /// <summary>
    /// Resolves an expression-bodied member expression from one supported method declaration syntax.
    /// </summary>
    /// <param name="declaration">Method-like declaration syntax.</param>
    /// <returns>The expression body when present; otherwise, <c>null</c>.</returns>
    static ExpressionSyntax ResolveExpressionBody(SyntaxNode declaration) {
        if (declaration is MethodDeclarationSyntax methodDeclaration) {
            return methodDeclaration.ExpressionBody?.Expression;
        } else if (declaration is LocalFunctionStatementSyntax localFunction) {
            return localFunction.ExpressionBody?.Expression;
        } else if (declaration is AccessorDeclarationSyntax accessorDeclaration) {
            return accessorDeclaration.ExpressionBody?.Expression;
        } else if (declaration is OperatorDeclarationSyntax operatorDeclaration) {
            return operatorDeclaration.ExpressionBody?.Expression;
        } else if (declaration is ConversionOperatorDeclarationSyntax conversionDeclaration) {
            return conversionDeclaration.ExpressionBody?.Expression;
        }

        return null;
    }

    /// <summary>
    /// Determines whether one method-like declaration has executable source.
    /// </summary>
    /// <param name="method">Method symbol to inspect.</param>
    /// <returns><c>true</c> when its declaring syntax contains a block or expression body.</returns>
    static bool HasSourceBody(IMethodSymbol method) {
        if (method?.DeclaringSyntaxReferences.Length != 1) {
            return false;
        }

        SyntaxNode declaration = GetMethodDeclaration(method);
        if (ResolveExpressionBody(declaration) != null) {
            return true;
        }

        return declaration switch {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body != null,
            ConstructorDeclarationSyntax constructorDeclaration => constructorDeclaration.Body != null,
            DestructorDeclarationSyntax destructorDeclaration => destructorDeclaration.Body != null,
            LocalFunctionStatementSyntax localFunction => localFunction.Body != null,
            AccessorDeclarationSyntax accessorDeclaration => accessorDeclaration.Body != null,
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.Body != null,
            ConversionOperatorDeclarationSyntax conversionDeclaration => conversionDeclaration.Body != null,
            _ => false
        };
    }

    /// <summary>
    /// Resolves the single source declaration for one source-visible method.
    /// </summary>
    /// <param name="method">Method whose declaration is required.</param>
    /// <returns>The source declaration syntax.</returns>
    static SyntaxNode GetMethodDeclaration(IMethodSymbol method) {
        SyntaxReference declarationReference = method?.DeclaringSyntaxReferences.FirstOrDefault();
        return declarationReference?.GetSyntax()
            ?? throw new InvalidOperationException($"Method '{method}' does not have source declaration syntax.");
    }

    /// <summary>
    /// Resolves the semantic model associated with one source tree in the active analysis run.
    /// </summary>
    /// <param name="syntaxTree">Source tree requiring semantic operations.</param>
    /// <returns>The semantic model for the requested source tree.</returns>
    SemanticModel ResolveSemanticModel(SyntaxTree syntaxTree) {
        if (SemanticModels == null) {
            throw new InvalidOperationException("Ownership semantic models have not been initialized for this resolution run.");
        }
        if (!SemanticModels.TryGetValue(syntaxTree, out SemanticModel semanticModel)) {
            throw new KeyNotFoundException("Ownership analysis could not locate the semantic model for one source tree.");
        }

        return semanticModel;
    }

    /// <summary>
    /// Determines whether a syntax node declares a method-like symbol collected by ownership analysis.
    /// </summary>
    /// <param name="node">Syntax node to inspect.</param>
    /// <returns><c>true</c> for supported method-like declarations.</returns>
    static bool IsMethodDeclarationSyntax(SyntaxNode node) {
        return node is BaseMethodDeclarationSyntax ||
            node is AccessorDeclarationSyntax ||
            node is LocalFunctionStatementSyntax;
    }

    /// <summary>
    /// Resolves one method symbol from a supported method-like declaration.
    /// </summary>
    /// <param name="semanticModel">Semantic model that owns the declaration.</param>
    /// <param name="declaration">Method-like declaration syntax.</param>
    /// <returns>The resolved method symbol, or <c>null</c> when Roslyn does not expose one.</returns>
    static IMethodSymbol ResolveDeclaredMethod(SemanticModel semanticModel, SyntaxNode declaration) {
        if (declaration is BaseMethodDeclarationSyntax baseMethodDeclaration) {
            return semanticModel.GetDeclaredSymbol(baseMethodDeclaration) as IMethodSymbol;
        } else if (declaration is AccessorDeclarationSyntax accessorDeclaration) {
            return semanticModel.GetDeclaredSymbol(accessorDeclaration) as IMethodSymbol;
        } else if (declaration is LocalFunctionStatementSyntax localFunction) {
            return semanticModel.GetDeclaredSymbol(localFunction) as IMethodSymbol;
        }

        return null;
    }

    /// <summary>
    /// Determines whether a syntax node introduces an executable scope nested inside the analyzed method.
    /// </summary>
    /// <param name="node">Syntax node to inspect.</param>
    /// <returns><c>true</c> for lambdas, anonymous methods, and local functions.</returns>
    static bool IsNestedExecutableSyntax(SyntaxNode node) {
        return node is AnonymousFunctionExpressionSyntax || node is LocalFunctionStatementSyntax;
    }

    /// <summary>
    /// Determines whether one operation is a constant null value.
    /// </summary>
    /// <param name="operation">Operation to inspect.</param>
    /// <returns><c>true</c> when its constant value is null.</returns>
    static bool IsNullOperation(IOperation operation) {
        return operation != null && operation.ConstantValue.HasValue && operation.ConstantValue.Value == null;
    }

    /// <summary>
    /// Determines whether one invocation explicitly destroys or releases its ownership-bearing argument.
    /// </summary>
    /// <param name="method">Invoked method.</param>
    /// <returns><c>true</c> for supported <c>NativeOwnership</c> cleanup helpers.</returns>
    static bool IsNativeOwnershipRelease(IMethodSymbol method) {
        if (!string.Equals(method?.ContainingType?.Name, "NativeOwnership", StringComparison.Ordinal)) {
            return false;
        }

        return string.Equals(method.Name, "Delete", StringComparison.Ordinal) ||
            string.Equals(method.Name, "DisposeAndDelete", StringComparison.Ordinal) ||
            string.Equals(method.Name, "Release", StringComparison.Ordinal) ||
            string.Equals(method.Name, "DisposeAndRelease", StringComparison.Ordinal) ||
            string.Equals(method.Name, "DeleteItemsAndRelease", StringComparison.Ordinal) ||
            string.Equals(method.Name, "DisposeItemsAndRelease", StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether one parameter reference is itself the value returned by its containing statement or expression body.
    /// </summary>
    /// <param name="reference">Parameter identifier use to inspect.</param>
    /// <param name="parameter">Parameter symbol represented by the identifier.</param>
    /// <param name="semanticModel">Semantic model that owns the parameter use.</param>
    /// <returns><c>true</c> only when the returned operation is the parameter after conversion unwrapping.</returns>
    static bool ReturnsParameterReference(
        IdentifierNameSyntax reference,
        IParameterSymbol parameter,
        SemanticModel semanticModel) {
        ExpressionSyntax returnExpression = reference.Ancestors()
            .OfType<ReturnStatementSyntax>()
            .Select(returnStatement => returnStatement.Expression)
            .FirstOrDefault(expression => expression != null);
        if (returnExpression == null) {
            returnExpression = reference.Ancestors()
                .OfType<ArrowExpressionClauseSyntax>()
                .Select(expressionBody => expressionBody.Expression)
                .FirstOrDefault();
        }
        if (returnExpression == null) {
            return false;
        }

        IOperation operation = semanticModel.GetOperation(returnExpression);
        while (operation is IConversionOperation conversionOperation) {
            operation = conversionOperation.Operand;
        }

        return operation is IParameterReferenceOperation parameterReferenceOperation &&
            SymbolEqualityComparer.Default.Equals(parameterReferenceOperation.Parameter, parameter);
    }
}
