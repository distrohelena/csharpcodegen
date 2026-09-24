using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Describes one managed type after it has been lowered to its native P/Invoke ABI representation: the classification
/// used to pick lowering rules, and the literal C++ type text an emitter writes into forwarders, trampolines, and
/// mirror structs.
/// </summary>
public sealed class CPPPInvokeLoweredType {
    /// <summary>
    /// Creates a lowered type description.
    /// </summary>
    /// <param name="kind">Native shape classification for the lowered value.</param>
    /// <param name="mirrorTypeText">C++ type text to emit wherever this lowered type is referenced.</param>
    /// <param name="sourceType">
    /// The managed type symbol this lowering was derived from. May be <c>null</c> for synthetic lowered types (such as
    /// <c>void</c>) that do not correspond to a single source symbol.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="mirrorTypeText"/> is null.</exception>
    public CPPPInvokeLoweredType(CPPPInvokeValueKind kind, string mirrorTypeText, ITypeSymbol sourceType) {
        Kind = kind;
        MirrorTypeText = mirrorTypeText ?? throw new ArgumentNullException(nameof(mirrorTypeText));
        SourceType = sourceType;
    }

    /// <summary>
    /// Gets the native shape classification that determines how this lowered type participates in signature and
    /// struct-layout lowering.
    /// </summary>
    public CPPPInvokeValueKind Kind { get; }

    /// <summary>
    /// Gets the literal C++ type text (for example <c>int32_t</c> or <c>void*</c>) an emitter writes wherever this
    /// lowered type is referenced.
    /// </summary>
    public string MirrorTypeText { get; }

    /// <summary>
    /// Gets the managed type symbol this lowering was derived from, or <c>null</c> for synthetic lowered types that do
    /// not correspond to a single source symbol.
    /// </summary>
    public ITypeSymbol SourceType { get; }
}
