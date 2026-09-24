namespace cs2.cpp;

/// <summary>
/// Describes one native callback trampoline generated for a managed method exposed to native code (for example a
/// method marked with <c>UnmanagedCallersOnlyAttribute</c>): the managed method it wraps, the generated trampoline's
/// native name, and its lowered signature.
/// </summary>
public sealed class CPPPInvokeCallback {
    /// <summary>
    /// Creates a callback trampoline description.
    /// </summary>
    /// <param name="methodId">Documentation-comment id of the managed method this trampoline calls.</param>
    /// <param name="trampolineName">Native function name of the generated trampoline.</param>
    /// <param name="signature">Lowered native signature the trampoline exposes to native callers.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="methodId"/>, <paramref name="trampolineName"/>, or <paramref name="signature"/> is null.
    /// </exception>
    public CPPPInvokeCallback(string methodId, string trampolineName, CPPPInvokeSignature signature) {
        MethodId = methodId ?? throw new ArgumentNullException(nameof(methodId));
        TrampolineName = trampolineName ?? throw new ArgumentNullException(nameof(trampolineName));
        Signature = signature ?? throw new ArgumentNullException(nameof(signature));
    }

    /// <summary>
    /// Gets the documentation-comment id (see
    /// <see cref="CPPPInvokeMethodIds.Get(Microsoft.CodeAnalysis.IMethodSymbol)"/>) of the managed method this
    /// trampoline calls.
    /// </summary>
    public string MethodId { get; }

    /// <summary>
    /// Gets the native function name of the generated trampoline, which native code passes a pointer to when
    /// registering the callback.
    /// </summary>
    public string TrampolineName { get; }

    /// <summary>
    /// Gets the lowered native signature the trampoline exposes to native callers.
    /// </summary>
    public CPPPInvokeSignature Signature { get; }
}
