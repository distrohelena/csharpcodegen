namespace cs2.cpp;

/// <summary>
/// Aggregates every native forwarder, callback trampoline, and mirror struct produced by lowering one compilation's
/// P/Invoke declarations, and indexes them for fast lookup by the emitters that consume the plan.
/// </summary>
public sealed class CPPPInvokePlan {
    /// <summary>
    /// Indexes <see cref="Imports"/> by every managed method id that resolves to each import, for O(1) lookup while
    /// emitting call sites.
    /// </summary>
    readonly Dictionary<string, CPPPInvokeImport> ImportsByMethodId = new Dictionary<string, CPPPInvokeImport>(StringComparer.Ordinal);

    /// <summary>
    /// Indexes <see cref="Callbacks"/> by their managed method id, for O(1) lookup while emitting trampoline
    /// registration sites.
    /// </summary>
    readonly Dictionary<string, CPPPInvokeCallback> CallbacksByMethodId = new Dictionary<string, CPPPInvokeCallback>(StringComparer.Ordinal);

    /// <summary>
    /// Indexes <see cref="MirrorStructs"/> by their source struct type key, for O(1) lookup while lowering fields and
    /// parameters that reference a mirrored struct type.
    /// </summary>
    readonly Dictionary<string, CPPPInvokeMirrorStruct> MirrorStructsByKey = new Dictionary<string, CPPPInvokeMirrorStruct>(StringComparer.Ordinal);

    /// <summary>
    /// Creates a plan from the imports, callbacks, and mirror structs produced by the analysis stage, and builds the
    /// lookup indexes used by <see cref="TryGetImport"/>, <see cref="TryGetCallback"/>, and
    /// <see cref="TryGetMirrorStruct"/>.
    /// </summary>
    /// <param name="imports">Every native forwarder produced by lowering.</param>
    /// <param name="callbacks">Every native callback trampoline produced by lowering.</param>
    /// <param name="mirrorStructs">Every native mirror struct produced by lowering.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="imports"/>, <paramref name="callbacks"/>, or <paramref name="mirrorStructs"/> is null.
    /// </exception>
    public CPPPInvokePlan(IReadOnlyList<CPPPInvokeImport> imports, IReadOnlyList<CPPPInvokeCallback> callbacks, IReadOnlyList<CPPPInvokeMirrorStruct> mirrorStructs) {
        Imports = imports ?? throw new ArgumentNullException(nameof(imports));
        Callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        MirrorStructs = mirrorStructs ?? throw new ArgumentNullException(nameof(mirrorStructs));
        LinkLibraries = imports.Select(import => import.LinkLibraryName).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
        foreach (CPPPInvokeImport import in imports) {
            foreach (string methodId in import.MethodIds) {
                ImportsByMethodId[methodId] = import;
            }
        }
        foreach (CPPPInvokeCallback callback in callbacks) {
            CallbacksByMethodId[callback.MethodId] = callback;
        }
        foreach (CPPPInvokeMirrorStruct mirrorStruct in mirrorStructs) {
            MirrorStructsByKey[mirrorStruct.StructTypeKey] = mirrorStruct;
        }
    }

    /// <summary>
    /// Gets every native forwarder produced by lowering.
    /// </summary>
    public IReadOnlyList<CPPPInvokeImport> Imports { get; }

    /// <summary>
    /// Gets every native callback trampoline produced by lowering.
    /// </summary>
    public IReadOnlyList<CPPPInvokeCallback> Callbacks { get; }

    /// <summary>
    /// Gets every native mirror struct produced by lowering.
    /// </summary>
    public IReadOnlyList<CPPPInvokeMirrorStruct> MirrorStructs { get; }

    /// <summary>
    /// Gets the distinct original library base names (<see cref="CPPPInvokeImport.LinkLibraryName"/>) referenced by
    /// <see cref="Imports"/>, sorted ordinally. This is the exact set of libraries the generated project must link
    /// against.
    /// </summary>
    public IReadOnlyList<string> LinkLibraries { get; }

    /// <summary>
    /// Gets a value indicating whether this plan contains at least one native forwarder, used to decide whether the
    /// generated project needs the P/Invoke runtime support at all.
    /// </summary>
    public bool HasNativeImports => Imports.Count > 0;

    /// <summary>
    /// Looks up the native forwarder that a managed method resolves to.
    /// </summary>
    /// <param name="methodId">Documentation-comment id of the managed method to resolve.</param>
    /// <param name="import">The resolved forwarder, if found; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if a forwarder was found for <paramref name="methodId"/>; otherwise <c>false</c>.</returns>
    public bool TryGetImport(string methodId, out CPPPInvokeImport import) {
        return ImportsByMethodId.TryGetValue(methodId, out import);
    }

    /// <summary>
    /// Looks up the native callback trampoline generated for a managed method.
    /// </summary>
    /// <param name="methodId">Documentation-comment id of the managed callback method to resolve.</param>
    /// <param name="callback">The resolved trampoline, if found; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if a trampoline was found for <paramref name="methodId"/>; otherwise <c>false</c>.</returns>
    public bool TryGetCallback(string methodId, out CPPPInvokeCallback callback) {
        return CallbacksByMethodId.TryGetValue(methodId, out callback);
    }

    /// <summary>
    /// Looks up the native mirror struct generated for a managed struct type.
    /// </summary>
    /// <param name="structTypeKey">Struct type key, as produced by <see cref="CPPPInvokeMirrorStruct.StructTypeKey"/>.</param>
    /// <param name="mirrorStruct">The resolved mirror struct, if found; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if a mirror struct was found for <paramref name="structTypeKey"/>; otherwise <c>false</c>.</returns>
    public bool TryGetMirrorStruct(string structTypeKey, out CPPPInvokeMirrorStruct mirrorStruct) {
        return MirrorStructsByKey.TryGetValue(structTypeKey, out mirrorStruct);
    }
}
