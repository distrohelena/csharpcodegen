using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Describes one native mirror struct generated for a managed value type that crosses a P/Invoke boundary by value:
/// its generated C++ name, the managed struct it mirrors, its packing, and its fields in layout order.
/// </summary>
public sealed class CPPPInvokeMirrorStruct {
    /// <summary>
    /// Creates a mirror struct description.
    /// </summary>
    /// <param name="mirrorName">Name of the generated native struct type.</param>
    /// <param name="structType">Managed struct type this mirror struct was generated from.</param>
    /// <param name="pack">Explicit struct packing in bytes, or <c>0</c> to use the platform default packing.</param>
    /// <param name="fields">Mirror fields in layout order.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="mirrorName"/>, <paramref name="structType"/>, or <paramref name="fields"/> is null.
    /// </exception>
    public CPPPInvokeMirrorStruct(string mirrorName, INamedTypeSymbol structType, int pack, IReadOnlyList<CPPPInvokeMirrorField> fields) {
        MirrorName = mirrorName ?? throw new ArgumentNullException(nameof(mirrorName));
        StructType = structType ?? throw new ArgumentNullException(nameof(structType));
        Pack = pack;
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    /// <summary>
    /// Gets the name of the generated native struct type.
    /// </summary>
    public string MirrorName { get; }

    /// <summary>
    /// Gets the managed struct type this mirror struct was generated from.
    /// </summary>
    public INamedTypeSymbol StructType { get; }

    /// <summary>
    /// Gets the explicit struct packing in bytes, or <c>0</c> to use the platform default packing.
    /// </summary>
    public int Pack { get; }

    /// <summary>
    /// Gets the mirror fields in layout order.
    /// </summary>
    public IReadOnlyList<CPPPInvokeMirrorField> Fields { get; }

    /// <summary>
    /// Gets a stable key identifying the managed struct type this mirror describes, used to look up the mirror for a
    /// given source type across the plan. Computed from the type's original (unconstructed generic) definition so that
    /// distinct constructions of the same generic struct share one mirror key.
    /// </summary>
    public string StructTypeKey => StructType.OriginalDefinition.ToDisplayString();
}
