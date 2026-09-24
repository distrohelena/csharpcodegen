namespace cs2.cpp;

/// <summary>
/// Describes the fully lowered native ABI shape of a P/Invoke method: its return type, parameter list, and calling
/// convention. Two signatures with an equal <see cref="MirrorKey"/> are ABI-compatible and can share one generated
/// function-pointer type or forwarder prototype.
/// </summary>
public sealed class CPPPInvokeSignature {
    /// <summary>
    /// Creates a lowered signature description.
    /// </summary>
    /// <param name="returnType">Lowered native type of the method's return value.</param>
    /// <param name="parameters">Lowered parameters in declaration order.</param>
    /// <param name="callingConvention">Native calling convention the signature uses.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="returnType"/> or <paramref name="parameters"/> is null.
    /// </exception>
    public CPPPInvokeSignature(CPPPInvokeLoweredType returnType, IReadOnlyList<CPPPInvokeParameter> parameters, CPPPInvokeCallingConvention callingConvention) {
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        CallingConvention = callingConvention;
    }

    /// <summary>
    /// Gets the lowered native type of the method's return value.
    /// </summary>
    public CPPPInvokeLoweredType ReturnType { get; }

    /// <summary>
    /// Gets the lowered parameters in declaration order.
    /// </summary>
    public IReadOnlyList<CPPPInvokeParameter> Parameters { get; }

    /// <summary>
    /// Gets the native calling convention the signature uses.
    /// </summary>
    public CPPPInvokeCallingConvention CallingConvention { get; }

    /// <summary>
    /// Gets the calling-convention macro emitted before the function name in prototypes and function-pointer types.
    /// </summary>
    public string CallingConventionMacro => CallingConvention == CPPPInvokeCallingConvention.StdCall ? "HE_CPP_STDCALL" : "HE_CPP_CDECL";

    /// <summary>
    /// Gets a stable text key that is equal for two signatures exactly when their lowered native ABI is equal: the
    /// calling-convention macro, the return type text, and the ref-lowered parameter type texts joined in order.
    /// </summary>
    public string MirrorKey => CallingConventionMacro + " " + ReturnType.MirrorTypeText + "(" + string.Join(",", Parameters.Select(parameter => parameter.MirrorParameterText)) + ")";
}
