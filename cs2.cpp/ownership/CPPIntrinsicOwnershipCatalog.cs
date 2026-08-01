using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Provides reviewed ownership contracts for framework operations whose implementation bodies are unavailable to source analysis.
/// </summary>
public sealed class CPPIntrinsicOwnershipCatalog {
    /// <summary>
    /// Tries to classify the ownership of one framework method return value.
    /// </summary>
    /// <param name="method">Method whose returned native value requires a lifetime contract.</param>
    /// <param name="ownership">Resolved ownership when the method is a registered intrinsic; otherwise, <see cref="CPPOwnershipKind.Unknown"/>.</param>
    /// <returns><c>true</c> when the catalog contains an explicit return contract.</returns>
    public bool TryGetReturnOwnership(IMethodSymbol method, out CPPOwnershipKind ownership) {
        if (method == null) {
            ownership = CPPOwnershipKind.Unknown;
            return false;
        }

        if (IsMethod(method, "System.Array", "Empty") ||
            IsMethod(method, "System.Linq.Enumerable", "Empty")) {
            ownership = CPPOwnershipKind.Borrowed;
            return true;
        } else if (IsMethod(method, "System.Linq.Enumerable", "ToArray") ||
                   IsMethod(method, "System.Linq.Enumerable", "ToList") ||
                   IsMethod(method, "System.Array", "Clone") ||
                   IsMethod(method, "System.Collections.Generic.List<T>", "ToArray") ||
                   IsMethod(method, "System.Collections.Generic.List<T>", "AsReadOnly") ||
                   IsMethod(method, "System.Text.Encoding", "GetBytes") ||
                   IsMethod(method, "System.Security.Cryptography.SHA256", "HashData") ||
                   IsMethod(method, "System.IO.MemoryStream", "ToArray") ||
                   IsMethod(method, "System.IO.File", "OpenRead") ||
                   IsMethod(method, "System.String", "Split")) {
            ownership = CPPOwnershipKind.Owned;
            return true;
        }

        ownership = CPPOwnershipKind.Unknown;
        return false;
    }

    /// <summary>
    /// Tries to classify one method parameter from semantic native ownership metadata.
    /// </summary>
    /// <param name="parameter">Parameter whose argument lifetime behavior requires a contract.</param>
    /// <param name="ownership">Resolved parameter behavior when an ownership attribute is present.</param>
    /// <returns><c>true</c> when the parameter has a recognized ownership attribute.</returns>
    public bool TryGetParameterOwnership(IParameterSymbol parameter, out CPPParameterOwnershipKind ownership) {
        if (parameter == null) {
            ownership = CPPParameterOwnershipKind.Unknown;
            return false;
        }

        foreach (AttributeData attribute in parameter.GetAttributes()) {
            string attributeName = attribute.AttributeClass?.Name ?? string.Empty;
            if (MatchesAttributeName(attributeName, "NativeNoEscape")) {
                ownership = CPPParameterOwnershipKind.NoEscape;
                return true;
            } else if (MatchesAttributeName(attributeName, "NativeRetainsBorrow")) {
                ownership = CPPParameterOwnershipKind.RetainsBorrow;
                return true;
            } else if (MatchesAttributeName(attributeName, "NativeTakesOwnership")) {
                ownership = CPPParameterOwnershipKind.TakesOwnership;
                return true;
            }
        }

        IMethodSymbol method = parameter.ContainingSymbol as IMethodSymbol;
        if (IsMethod(method, "System.Collections.Generic.List<T>", "Add") ||
            IsMethod(method, "System.Collections.Generic.Dictionary<TKey, TValue>", "Add")) {
            ownership = CPPParameterOwnershipKind.RetainsBorrow;
            return true;
        } else if (IsMethod(method, "System.Array", "Copy") ||
            IsMethod(method, "System.Collections.Generic.List<T>", "AddRange") ||
            IsMethod(method, "System.Text.Encoding", "GetString") ||
            IsMethod(method, "System.Security.Cryptography.SHA256", "HashData") ||
            IsMethodOrOverride(method, "System.IO.Stream", "CopyTo") ||
            IsMethodOrOverride(method, "System.IO.Stream", "Write") ||
            IsMethod(method, "System.String", "Join") ||
            IsMethod(method, "System.String", "Split")) {
            ownership = CPPParameterOwnershipKind.NoEscape;
            return true;
        }

        ownership = CPPParameterOwnershipKind.Unknown;
        return false;
    }

    /// <summary>
    /// Determines whether one Roslyn method matches an exact containing type and method name.
    /// </summary>
    /// <param name="method">Method symbol to inspect.</param>
    /// <param name="containingTypeName">Fully qualified containing type name.</param>
    /// <param name="methodName">Exact source method name.</param>
    /// <returns><c>true</c> when both identities match.</returns>
    static bool IsMethod(IMethodSymbol method, string containingTypeName, string methodName) {
        if (method == null) {
            return false;
        }

        string resolvedContainingTypeName = method.ContainingType?.SpecialType == SpecialType.System_String
            ? "System.String"
            : method.ContainingType?.OriginalDefinition.ToDisplayString();
        return string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
            string.Equals(resolvedContainingTypeName, containingTypeName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether one Roslyn method or any overridden base declaration matches an exact type and method name.
    /// </summary>
    /// <param name="method">Method symbol whose override chain should be inspected.</param>
    /// <param name="containingTypeName">Fully qualified containing type name expected in the override chain.</param>
    /// <param name="methodName">Exact source method name.</param>
    /// <returns><c>true</c> when the method or one overridden declaration matches the requested identity.</returns>
    static bool IsMethodOrOverride(IMethodSymbol method, string containingTypeName, string methodName) {
        IMethodSymbol candidate = method;
        while (candidate != null) {
            if (IsMethod(candidate, containingTypeName, methodName)) {
                return true;
            }

            candidate = candidate.OverriddenMethod;
        }

        return false;
    }

    /// <summary>
    /// Determines whether an attribute class name matches one contract with or without the conventional suffix.
    /// </summary>
    /// <param name="attributeName">Semantic attribute class name.</param>
    /// <param name="contractName">Contract name without the <c>Attribute</c> suffix.</param>
    /// <returns><c>true</c> when the names identify the same contract.</returns>
    static bool MatchesAttributeName(string attributeName, string contractName) {
        return string.Equals(attributeName, contractName, StringComparison.Ordinal) ||
            string.Equals(attributeName, contractName + "Attribute", StringComparison.Ordinal);
    }
}
