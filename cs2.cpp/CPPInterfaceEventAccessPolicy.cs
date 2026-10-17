using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Gives each native interface event contract a stable accessor identity independent of its implementation storage.
/// </summary>
public static class CPPInterfaceEventAccessPolicy {
    /// <summary>
    /// Builds a collision-free accessor name from the declaring interface definition and event name.
    /// </summary>
    /// <param name="eventSymbol">Interface event whose native accessor is required.</param>
    /// <returns>An identifier shared by declaration, implementation and subscription emission.</returns>
    public static string GetAccessorName(IEventSymbol eventSymbol) {
        if (eventSymbol == null || eventSymbol.ContainingType.TypeKind != TypeKind.Interface) {
            throw new ArgumentException("A declared interface event is required.", nameof(eventSymbol));
        }

        string interfaceIdentity = eventSymbol.ContainingType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string encodedIdentity = string.Concat(interfaceIdentity.Select(character => char.IsLetterOrDigit(character)
            ? character.ToString()
            : "_" + ((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
        return $"get_event_{encodedIdentity}_{eventSymbol.Name}";
    }
}
