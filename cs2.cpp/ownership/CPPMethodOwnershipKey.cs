using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Builds stable assembly-qualified method identities for ownership summaries shared across Roslyn compilations.
/// </summary>
public static class CPPMethodOwnershipKey {
    /// <summary>
    /// Stores the source signature format used for every ownership method identity.
    /// </summary>
    static readonly SymbolDisplayFormat MethodDisplayFormat = new SymbolDisplayFormat(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>
    /// Creates a deterministic key from the original method definition and containing assembly identity.
    /// </summary>
    /// <param name="method">Method symbol whose ownership summary requires a stable identity.</param>
    /// <returns>Assembly-qualified original-definition signature.</returns>
    public static string Create(IMethodSymbol method) {
        if (method == null) {
            throw new ArgumentNullException(nameof(method));
        }

        IMethodSymbol originalMethod = method.OriginalDefinition;
        string assemblyName = originalMethod.ContainingAssembly?.Identity.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(assemblyName)) {
            if (originalMethod.MethodKind == MethodKind.FunctionPointerSignature) {
                return "<function-pointer>|" + originalMethod.ToDisplayString(MethodDisplayFormat);
            }

            throw new InvalidOperationException($"Method '{method}' does not belong to a named assembly.");
        }

        return assemblyName + "|" + originalMethod.ToDisplayString(MethodDisplayFormat);
    }
}
