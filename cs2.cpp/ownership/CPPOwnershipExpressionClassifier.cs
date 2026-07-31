using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace cs2.cpp;

/// <summary>
/// Classifies ownership-producing and borrowing expressions using Roslyn semantics and resolved method contracts.
/// </summary>
public sealed class CPPOwnershipExpressionClassifier {
    /// <summary>
    /// Stores reviewed ownership contracts for framework methods without source bodies.
    /// </summary>
    readonly CPPIntrinsicOwnershipCatalog IntrinsicCatalog;

    /// <summary>
    /// Initializes an expression classifier with the default intrinsic ownership catalog.
    /// </summary>
    public CPPOwnershipExpressionClassifier()
        : this(new CPPIntrinsicOwnershipCatalog()) {
    }

    /// <summary>
    /// Initializes an expression classifier with one explicit intrinsic ownership catalog.
    /// </summary>
    /// <param name="intrinsicCatalog">Reviewed framework ownership contracts.</param>
    public CPPOwnershipExpressionClassifier(CPPIntrinsicOwnershipCatalog intrinsicCatalog) {
        IntrinsicCatalog = intrinsicCatalog ?? throw new ArgumentNullException(nameof(intrinsicCatalog));
    }

    /// <summary>
    /// Classifies one semantic expression without guessing when its source or boundary contract is unresolved.
    /// </summary>
    /// <param name="operation">Roslyn operation representing the value-producing expression.</param>
    /// <param name="summaries">Current fixed-point method summaries.</param>
    /// <returns>Owned, borrowed, or unknown native lifetime behavior.</returns>
    public CPPOwnershipKind Classify(
        IOperation operation,
        IReadOnlyDictionary<string, CPPMethodOwnershipSummary> summaries) {
        if (operation == null) {
            return CPPOwnershipKind.Unknown;
        }
        if (summaries == null) {
            throw new ArgumentNullException(nameof(summaries));
        }

        if (operation is IConversionOperation conversionOperation) {
            return Classify(conversionOperation.Operand, summaries);
        } else if (operation is IParenthesizedOperation parenthesizedOperation) {
            return Classify(parenthesizedOperation.Operand, summaries);
        } else if (operation is IObjectCreationOperation ||
                   operation is IArrayCreationOperation ||
                   operation is ICollectionExpressionOperation) {
            return CPPOwnershipKind.Owned;
        } else if (IsNullOperation(operation)) {
            return CPPOwnershipKind.Unknown;
        } else if (operation is IConditionalOperation conditionalOperation) {
            return MergeConditionalOwnership(
                Classify(conditionalOperation.WhenTrue, summaries),
                IsNullOperation(conditionalOperation.WhenTrue),
                Classify(conditionalOperation.WhenFalse, summaries),
                IsNullOperation(conditionalOperation.WhenFalse));
        } else if (operation is IInvocationOperation invocationOperation) {
            return ResolveInvocationOwnership(invocationOperation.TargetMethod, summaries);
        } else if (operation is IPropertyReferenceOperation propertyReferenceOperation) {
            return ResolvePropertyOwnership(propertyReferenceOperation.Property, summaries);
        } else if (operation is IParameterReferenceOperation ||
                   operation is IFieldReferenceOperation ||
                   operation is IInstanceReferenceOperation) {
            return CPPOwnershipKind.Borrowed;
        }

        return CPPOwnershipKind.Unknown;
    }

    /// <summary>
    /// Tries to resolve an explicit owned- or borrowed-return assertion on one method or associated property.
    /// </summary>
    /// <param name="method">Method whose declared return contract should be inspected.</param>
    /// <param name="ownership">Declared ownership when exactly one recognized contract is present.</param>
    /// <returns><c>true</c> when a recognized return contract is present.</returns>
    public bool TryGetDeclaredReturnOwnership(IMethodSymbol method, out CPPOwnershipKind ownership) {
        if (method == null) {
            ownership = CPPOwnershipKind.Unknown;
            return false;
        }

        bool hasOwnedContract = HasAttribute(method, "NativeOwnedReturn") ||
            HasAttribute(method.AssociatedSymbol, "NativeOwnedReturn");
        bool hasBorrowedContract = HasAttribute(method, "NativeBorrowedReturn") ||
            HasAttribute(method.AssociatedSymbol, "NativeBorrowedReturn");
        if (hasOwnedContract == hasBorrowedContract) {
            ownership = CPPOwnershipKind.Unknown;
            return false;
        }

        ownership = hasOwnedContract ? CPPOwnershipKind.Owned : CPPOwnershipKind.Borrowed;
        return true;
    }

    /// <summary>
    /// Resolves ownership for one invocation from intrinsics, explicit boundary contracts, or source summaries.
    /// </summary>
    /// <param name="method">Invoked method.</param>
    /// <param name="summaries">Current fixed-point method summaries.</param>
    /// <returns>The resolved invocation result ownership.</returns>
    CPPOwnershipKind ResolveInvocationOwnership(
        IMethodSymbol method,
        IReadOnlyDictionary<string, CPPMethodOwnershipSummary> summaries) {
        if (IntrinsicCatalog.TryGetReturnOwnership(method, out CPPOwnershipKind intrinsicOwnership)) {
            return intrinsicOwnership;
        }
        if (TryGetDeclaredReturnOwnership(method, out CPPOwnershipKind declaredOwnership)) {
            return declaredOwnership;
        }

        string methodKey = CPPMethodOwnershipKey.Create(method);
        return summaries.TryGetValue(methodKey, out CPPMethodOwnershipSummary summary)
            ? summary.ReturnOwnership
            : CPPOwnershipKind.Unknown;
    }

    /// <summary>
    /// Resolves property ownership from an explicit property contract or its source getter summary.
    /// </summary>
    /// <param name="property">Referenced property.</param>
    /// <param name="summaries">Current fixed-point method summaries.</param>
    /// <returns>The resolved property value ownership.</returns>
    CPPOwnershipKind ResolvePropertyOwnership(
        IPropertySymbol property,
        IReadOnlyDictionary<string, CPPMethodOwnershipSummary> summaries) {
        if (property?.GetMethod != null &&
            TryGetDeclaredReturnOwnership(property.GetMethod, out CPPOwnershipKind declaredOwnership)) {
            return declaredOwnership;
        }
        if (property?.GetMethod != null) {
            string methodKey = CPPMethodOwnershipKey.Create(property.GetMethod);
            if (summaries.TryGetValue(methodKey, out CPPMethodOwnershipSummary summary) &&
                summary.ReturnOwnership != CPPOwnershipKind.Unknown) {
                return summary.ReturnOwnership;
            }
        }

        return CPPOwnershipKind.Borrowed;
    }

    /// <summary>
    /// Merges conditional branch ownership while treating null as absence of a non-null contract.
    /// </summary>
    /// <param name="trueOwnership">Ownership of the true branch.</param>
    /// <param name="trueIsNull">Whether the true branch is null.</param>
    /// <param name="falseOwnership">Ownership of the false branch.</param>
    /// <param name="falseIsNull">Whether the false branch is null.</param>
    /// <returns>The uniform non-null branch ownership, or unknown when branches conflict.</returns>
    static CPPOwnershipKind MergeConditionalOwnership(
        CPPOwnershipKind trueOwnership,
        bool trueIsNull,
        CPPOwnershipKind falseOwnership,
        bool falseIsNull) {
        if (trueIsNull) {
            return falseOwnership;
        } else if (falseIsNull) {
            return trueOwnership;
        } else if (trueOwnership == falseOwnership) {
            return trueOwnership;
        }

        return CPPOwnershipKind.Unknown;
    }

    /// <summary>
    /// Determines whether one operation is a null or default-null value.
    /// </summary>
    /// <param name="operation">Operation to inspect.</param>
    /// <returns><c>true</c> when the operation has a constant null value.</returns>
    static bool IsNullOperation(IOperation operation) {
        return operation != null && operation.ConstantValue.HasValue && operation.ConstantValue.Value == null;
    }

    /// <summary>
    /// Determines whether one symbol carries an ownership attribute name with or without the conventional suffix.
    /// </summary>
    /// <param name="symbol">Symbol whose attributes should be inspected.</param>
    /// <param name="contractName">Ownership contract name without the suffix.</param>
    /// <returns><c>true</c> when the requested contract is present.</returns>
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
