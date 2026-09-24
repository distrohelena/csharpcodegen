namespace cs2.cpp;

/// <summary>
/// Defines the stable diagnostic code strings reported while analyzing and lowering P/Invoke declarations to direct
/// native calls. Consumers (tooling, tests, and suppression lists) match on these codes, so their text must never change
/// once shipped.
/// </summary>
public static class CPPPInvokeDiagnosticCodes {
    /// <summary>
    /// Reported when a <c>LibraryImportAttribute</c> declaration is encountered but direct lowering does not support it,
    /// so the import falls back to the existing marshaling path instead of a native forwarder.
    /// </summary>
    public const string LibraryImportNotSupported = "CPPPINV001";

    /// <summary>
    /// Reported when a parameter, return type, or field type in a P/Invoke signature cannot be lowered to a native ABI
    /// representation understood by the direct-call emitter.
    /// </summary>
    public const string UnsupportedType = "CPPPINV002";

    /// <summary>
    /// Reported when a <c>DllImportAttribute</c> or <c>LibraryImportAttribute</c> uses a marshaling setting (such as a
    /// custom marshaler or string encoding) that direct lowering does not implement.
    /// </summary>
    public const string UnsupportedImportSetting = "CPPPINV003";

    /// <summary>
    /// Reported when a P/Invoke declaration specifies a calling convention other than the ones direct lowering can
    /// forward (<see cref="CPPPInvokeCallingConvention.StdCall"/> or <see cref="CPPPInvokeCallingConvention.Cdecl"/>).
    /// </summary>
    public const string UnsupportedCallingConvention = "CPPPINV004";

    /// <summary>
    /// Reported when two P/Invoke declarations share an entry point and library but lower to incompatible native
    /// signatures, making it impossible to emit a single forwarder for both.
    /// </summary>
    public const string ConflictingImportSignature = "CPPPINV005";

    /// <summary>
    /// Reported when the same entry point symbol is declared as imported from more than one native library, which
    /// direct lowering cannot resolve to a single link target.
    /// </summary>
    public const string SymbolImportedFromMultipleLibraries = "CPPPINV006";

    /// <summary>
    /// Reported when a method marked as a native callback target (for example via
    /// <c>UnmanagedCallersOnlyAttribute</c>) has a shape that direct lowering cannot turn into a trampoline, such as an
    /// instance method or a method with an unsupported modifier.
    /// </summary>
    public const string InvalidCallbackMethod = "CPPPINV007";

    /// <summary>
    /// Reported when a native callback method uses a parameter or return type that direct lowering cannot represent in
    /// the generated trampoline signature.
    /// </summary>
    public const string UnsupportedCallbackType = "CPPPINV008";
}
