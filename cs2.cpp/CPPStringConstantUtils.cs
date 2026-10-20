using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>Classifies managed string constants and chooses backing-storage names without source-member collisions.</summary>
internal static class CPPStringConstantUtils {
    /// <summary>Identifies managed constant string fields whose native backing must be initialized on demand.</summary>
    /// <param name="variable">Converted field declaration.</param>
    /// <returns>Whether the declaration requires shared deferred string storage.</returns>
    public static bool IsStringConstant(ConversionVariable variable) {
        return variable != null && variable.IsConst && variable.VarType?.Type == VariableDataType.String;
    }

    /// <summary>Chooses a storage field name without overlapping the generated getter or any source member.</summary>
    /// <param name="owner">Original declaring type.</param>
    /// <param name="fieldName">Managed constant field name.</param>
    /// <returns>Stable native storage identifier.</returns>
    public static string GetStorageName(INamedTypeSymbol owner, string fieldName) {
        return GetAvailableName(owner, "StringConstantStorage_" + fieldName);
    }

    /// <summary>Disambiguates a generated native identifier against all declared source members.</summary>
    /// <param name="owner">Declaring type used for collision checks.</param>
    /// <param name="candidate">Preferred generated identifier.</param>
    /// <returns>An identifier with deterministic suffixes when required.</returns>
    static string GetAvailableName(INamedTypeSymbol owner, string candidate) {
        while (owner != null && owner.GetMembers(candidate).Length > 0) {
            candidate += "_constant";
        }
        return candidate;
    }
}
