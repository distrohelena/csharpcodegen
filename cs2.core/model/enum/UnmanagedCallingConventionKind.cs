namespace cs2.core {
    /// <summary>
    /// Classifies the calling convention of a C# <c>delegate*</c> signature into the conventions the C++ backend can
    /// lower. Both the function-pointer wrapper type and the native mirror type are chosen from this single
    /// classification, so the two always agree on the convention.
    /// </summary>
    public enum UnmanagedCallingConventionKind {
        /// <summary>
        /// A managed <c>delegate*&lt;...&gt;</c> pointer that uses the platform default convention and cannot be called from native code.
        /// </summary>
        Managed,

        /// <summary>
        /// An unmanaged pointer with the callee-clean <c>__stdcall</c> convention: <c>unmanaged[Stdcall]</c>, or bare
        /// <c>unmanaged</c>, which is stdcall on the supported Windows targets.
        /// </summary>
        StdCall,

        /// <summary>
        /// An unmanaged pointer with the caller-clean <c>__cdecl</c> convention: <c>unmanaged[Cdecl]</c>.
        /// </summary>
        Cdecl,

        /// <summary>
        /// An unmanaged pointer whose convention the backend does not lower (thiscall, fastcall, several convention
        /// modifiers, or any other modifier type).
        /// </summary>
        Unsupported
    }
}
