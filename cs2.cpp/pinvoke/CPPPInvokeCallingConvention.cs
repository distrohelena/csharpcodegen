namespace cs2.cpp;

/// <summary>
/// Identifies the native calling convention a lowered P/Invoke signature must use, which controls both the emitted
/// function-pointer macro and how the forwarder is declared in generated C++.
/// </summary>
public enum CPPPInvokeCallingConvention {
    /// <summary>
    /// The callee cleans the stack (Windows API convention, <c>__stdcall</c>). This is the default for
    /// <c>DllImportAttribute</c> and <c>LibraryImportAttribute</c> declarations that do not specify otherwise.
    /// </summary>
    StdCall,

    /// <summary>
    /// The caller cleans the stack (<c>__cdecl</c>), the default native calling convention on most non-Windows
    /// platforms and for variadic native functions.
    /// </summary>
    Cdecl
}
