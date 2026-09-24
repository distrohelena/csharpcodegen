namespace cs2.cpp;

/// <summary>
/// Describes one native forwarder generated for a P/Invoke entry point: which library it links against, the exported
/// symbol it calls, its lowered signature, and every managed method id that resolves to this forwarder (multiple
/// declarations with identical library, entry point, and signature share a single forwarder).
/// </summary>
public sealed class CPPPInvokeImport {
    /// <summary>
    /// Creates an import description for one native forwarder. <see cref="MethodIds"/> starts empty; callers add every
    /// managed method id that resolves to this forwarder after construction.
    /// </summary>
    /// <param name="libraryNamespace">Normalized native library identifier, as produced by
    /// <see cref="CPPPInvokeLibraryNameNormalizer.Normalize(string)"/>.</param>
    /// <param name="entryPoint">Exported native symbol name to call.</param>
    /// <param name="signature">Lowered native signature of the entry point.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="libraryNamespace"/>, <paramref name="entryPoint"/>, or <paramref name="signature"/> is null.
    /// </exception>
    public CPPPInvokeImport(string libraryNamespace, string entryPoint, CPPPInvokeSignature signature) {
        LibraryNamespace = libraryNamespace ?? throw new ArgumentNullException(nameof(libraryNamespace));
        EntryPoint = entryPoint ?? throw new ArgumentNullException(nameof(entryPoint));
        Signature = signature ?? throw new ArgumentNullException(nameof(signature));
    }

    /// <summary>
    /// Gets the normalized native library identifier this import links against, and the C++ namespace its forwarder is
    /// declared in.
    /// </summary>
    public string LibraryNamespace { get; }

    /// <summary>
    /// Gets the exported native symbol name this forwarder calls.
    /// </summary>
    public string EntryPoint { get; }

    /// <summary>
    /// Gets the lowered native signature of the entry point.
    /// </summary>
    public CPPPInvokeSignature Signature { get; }

    /// <summary>
    /// Gets the mutable list of managed method documentation-comment ids (see
    /// <see cref="CPPPInvokeMethodIds.Get(Microsoft.CodeAnalysis.IMethodSymbol)"/>) that resolve to this forwarder.
    /// Populated by the analysis stage after construction, since several declarations can share one forwarder.
    /// </summary>
    public List<string> MethodIds { get; } = new List<string>();

    /// <summary>
    /// Gets the fully qualified C++ name of the generated forwarder function, in the form
    /// <c>he_pinvoke::&lt;LibraryNamespace&gt;::&lt;EntryPoint&gt;</c>.
    /// </summary>
    public string ForwarderQualifiedName => "he_pinvoke::" + LibraryNamespace + "::" + EntryPoint;
}
