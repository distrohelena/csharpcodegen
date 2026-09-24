using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Describes one parameter of a lowered P/Invoke signature: its managed name, lowered native type, and ref-passing
/// behavior, which together determine the exact text a forwarder or trampoline parameter list uses.
/// </summary>
public sealed class CPPPInvokeParameter {
    /// <summary>
    /// Creates a lowered parameter description.
    /// </summary>
    /// <param name="name">Parameter name as declared on the managed method.</param>
    /// <param name="type">Lowered native type of the parameter's value.</param>
    /// <param name="refKind">Managed ref-passing kind (none, ref, out, or in) for this parameter.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="type"/> is null.</exception>
    public CPPPInvokeParameter(string name, CPPPInvokeLoweredType type, RefKind refKind) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type ?? throw new ArgumentNullException(nameof(type));
        RefKind = refKind;
    }

    /// <summary>
    /// Gets the parameter name as declared on the managed method.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the lowered native type of the parameter's value, ignoring any by-reference passing.
    /// </summary>
    public CPPPInvokeLoweredType Type { get; }

    /// <summary>
    /// Gets the managed ref-passing kind (none, ref, out, or in) for this parameter. Any kind other than
    /// <see cref="Microsoft.CodeAnalysis.RefKind.None"/> means the parameter is passed as an address rather than by
    /// value.
    /// </summary>
    public RefKind RefKind { get; }

    /// <summary>
    /// Gets the C++ type text to use for this parameter in a forwarder or trampoline declaration: <c>"void*"</c> when
    /// the parameter is passed by reference (ref, out, or in), otherwise the parameter's lowered value type text.
    /// </summary>
    public string MirrorParameterText => RefKind != RefKind.None ? "void*" : Type.MirrorTypeText;

    /// <summary>
    /// Converts one lowered managed call-site argument into the native shape this forwarder parameter expects:
    /// by-reference arguments pass their address as <c>void*</c>, pointers are reinterpreted as <c>void*</c>, enums are
    /// cast to their underlying integer type, by-value structs are bit-copied into their mirror struct, function pointers
    /// are reinterpreted from their raw native address, and primitives pass through unchanged.
    /// </summary>
    /// <param name="argumentText">Fully lowered C++ text of the managed argument expression.</param>
    /// <returns>The C++ argument text to pass to the generated forwarder.</returns>
    /// <exception cref="InvalidOperationException">The parameter's lowered kind cannot appear in an argument position.</exception>
    public string FormatForwarderArgument(string argumentText) {
        if (RefKind != RefKind.None) {
            return $"reinterpret_cast<void*>(&({argumentText}))";
        }

        switch (Type.Kind) {
            case CPPPInvokeValueKind.Pointer:
                return $"reinterpret_cast<void*>({argumentText})";
            case CPPPInvokeValueKind.Enum:
                return $"static_cast<{Type.MirrorTypeText}>({argumentText})";
            case CPPPInvokeValueKind.Struct:
                return $"he_pinvoke_bit_copy<{Type.MirrorTypeText}>({argumentText})";
            case CPPPInvokeValueKind.FunctionPointer:
                return $"reinterpret_cast<{Type.MirrorTypeText}>(he_cpp_raw_function_pointer({argumentText}))";
            case CPPPInvokeValueKind.Primitive:
                return argumentText;
            default:
                throw new InvalidOperationException($"P/Invoke parameter '{Name}' has lowered kind '{Type.Kind}', which cannot be passed as an argument.");
        }
    }
}
