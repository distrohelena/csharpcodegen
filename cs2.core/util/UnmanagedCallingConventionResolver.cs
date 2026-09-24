using Microsoft.CodeAnalysis;
using System.Reflection.Metadata;

namespace cs2.core {
    /// <summary>
    /// The single source of the rules that map a Roslyn function-pointer signature's calling convention to an
    /// <see cref="UnmanagedCallingConventionKind"/>. The variable-type model (which picks the C++ wrapper template) and the
    /// P/Invoke type lowerer (which picks the native mirror type) both consume it, so a wrapper value and the mirror type it
    /// is reinterpreted to can never disagree on the convention.
    /// </summary>
    public static class UnmanagedCallingConventionResolver {
        /// <summary>
        /// Metadata name of the modifier type that selects the stdcall convention in <c>unmanaged[Stdcall]</c>.
        /// </summary>
        const string CallConvStdcallName = "CallConvStdcall";

        /// <summary>
        /// Metadata name of the modifier type that selects the cdecl convention in <c>unmanaged[Cdecl]</c>.
        /// </summary>
        const string CallConvCdeclName = "CallConvCdecl";

        /// <summary>
        /// Classifies a function-pointer signature's calling convention. <c>StdCall</c> and <c>CDecl</c> map directly;
        /// <c>Unmanaged</c> maps to stdcall with no modifier or with exactly <c>CallConvStdcall</c>, to cdecl with exactly
        /// <c>CallConvCdecl</c>, and is unsupported otherwise; the managed default maps to managed; every other
        /// convention is unsupported.
        /// </summary>
        /// <param name="signature">Signature of the function-pointer type to classify.</param>
        /// <returns>The classified calling convention.</returns>
        public static UnmanagedCallingConventionKind Resolve(IMethodSymbol signature) {
            if (signature == null) {
                throw new ArgumentNullException(nameof(signature));
            }

            switch (signature.CallingConvention) {
                case SignatureCallingConvention.Default:
                    return UnmanagedCallingConventionKind.Managed;
                case SignatureCallingConvention.StdCall:
                    return UnmanagedCallingConventionKind.StdCall;
                case SignatureCallingConvention.CDecl:
                    return UnmanagedCallingConventionKind.Cdecl;
                case SignatureCallingConvention.Unmanaged:
                    if (signature.UnmanagedCallingConventionTypes.Length == 0) {
                        return UnmanagedCallingConventionKind.StdCall;
                    } else if (signature.UnmanagedCallingConventionTypes.Length == 1 && signature.UnmanagedCallingConventionTypes[0].Name == CallConvStdcallName) {
                        return UnmanagedCallingConventionKind.StdCall;
                    } else if (signature.UnmanagedCallingConventionTypes.Length == 1 && signature.UnmanagedCallingConventionTypes[0].Name == CallConvCdeclName) {
                        return UnmanagedCallingConventionKind.Cdecl;
                    }
                    return UnmanagedCallingConventionKind.Unsupported;
                default:
                    return UnmanagedCallingConventionKind.Unsupported;
            }
        }
    }
}
