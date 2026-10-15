using cs2.core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp;

/// <summary>
/// Allows native string getters to borrow only stable storage, while computed and polymorphic getters return values.
/// </summary>
internal static class CPPPropertyGetterStoragePolicy {
    /// <summary>
    /// Checks that a getter has a stable field-backed result and no interface or virtual contract that permits a temporary result.
    /// </summary>
    /// <param name="owner">Converted type declaring the property.</param>
    /// <param name="property">Property whose getter is being emitted.</param>
    /// <returns>Whether the getter can safely expose a constant reference to native storage.</returns>
    internal static bool CanBorrowStorage(ConversionClass owner, ConversionVariable property) {
        if (owner.DeclarationType == MemberDeclarationType.Interface || HasPolymorphicContract(owner, property)) {
            return false;
        }

        ExpressionSyntax expression = property.ArrowExpression;
        if (property.GetBlock != null) {
            if (property.GetBlock.Statements.Count != 1 ||
                property.GetBlock.Statements[0] is not ReturnStatementSyntax returnStatement) {
                return false;
            }
            expression = returnStatement.Expression;
        }

        if (expression == null) {
            return true;
        }

        while (expression is ParenthesizedExpressionSyntax parenthesized) {
            expression = parenthesized.Expression;
        }

        if (property.Semantic == null ||
            property.Semantic.GetSymbolInfo(expression).Symbol is not IFieldSymbol field ||
            field.IsConst) {
            return false;
        }

        return field.IsStatic || expression is IdentifierNameSyntax ||
            expression is MemberAccessExpressionSyntax member &&
            (member.Expression is ThisExpressionSyntax || member.Expression is BaseExpressionSyntax);
    }

    /// <summary>
    /// Identifies contracts whose implementations can compute fresh strings, requiring one consistent value-return ABI.
    /// </summary>
    /// <param name="owner">Type declaring the getter.</param>
    /// <param name="property">Property model being inspected.</param>
    /// <returns>Whether virtual dispatch or an interface requires a value-return signature.</returns>
    static bool HasPolymorphicContract(ConversionClass owner, ConversionVariable property) {
        INamedTypeSymbol ownerSymbol = owner.TypeSymbol;
        if (ownerSymbol == null) {
            return property.IsOverride || property.IsAbstract;
        }

        IPropertySymbol propertySymbol = null;
        for (INamedTypeSymbol declaringType = ownerSymbol; declaringType != null; declaringType = declaringType.BaseType) {
            propertySymbol = declaringType.GetMembers(property.Name).OfType<IPropertySymbol>().FirstOrDefault();
            if (propertySymbol != null) {
                break;
            }
        }
        if (propertySymbol == null) {
            return false;
        }
        if (propertySymbol.IsVirtual || propertySymbol.IsOverride || propertySymbol.IsAbstract) {
            return true;
        }

        foreach (INamedTypeSymbol interfaceSymbol in ownerSymbol.AllInterfaces) {
            foreach (IPropertySymbol interfaceProperty in interfaceSymbol.GetMembers().OfType<IPropertySymbol>()) {
                if (SymbolEqualityComparer.Default.Equals(
                    ownerSymbol.FindImplementationForInterfaceMember(interfaceProperty), propertySymbol)) {
                    return true;
                }
            }
        }

        return false;
    }
}
