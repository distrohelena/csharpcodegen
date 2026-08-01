using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Identifies managed types whose C++ representation carries a native pointer lifetime that ownership analysis must prove.
/// </summary>
public static class CPPOwnershipTypeClassifier {
    /// <summary>
    /// Determines whether a managed type lowers to a native pointer that requires ownership classification.
    /// </summary>
    /// <param name="type">Managed type to inspect.</param>
    /// <returns><c>true</c> for ownership-bearing reference and array types other than strings.</returns>
    public static bool RequiresClassification(ITypeSymbol type) {
        if (type == null || type.SpecialType == SpecialType.System_Void || type.SpecialType == SpecialType.System_String) {
            return false;
        }
        if (type is IArrayTypeSymbol) {
            return true;
        }
        if (type is ITypeParameterSymbol typeParameter) {
            return typeParameter.HasReferenceTypeConstraint ||
                typeParameter.ConstraintTypes.Any(constraintType =>
                    constraintType is IArrayTypeSymbol ||
                    constraintType.TypeKind == TypeKind.Class ||
                    constraintType.TypeKind == TypeKind.Delegate);
        }

        return type.IsReferenceType;
    }
}
