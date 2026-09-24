namespace cs2.cpp;

/// <summary>
/// Classifies the native shape a managed P/Invoke type lowers to, driving both the mirror C++ type text an emitter
/// produces and which lowering rules apply to the value (for example, whether it participates in struct marshaling).
/// </summary>
public enum CPPPInvokeValueKind {
    /// <summary>
    /// The value carries no data; used only for return types of methods with no result.
    /// </summary>
    Void,

    /// <summary>
    /// A blittable primitive (integer, floating-point, or boolean-as-integer) that maps directly to a fixed-width
    /// native type such as <c>int32_t</c> or <c>double</c>.
    /// </summary>
    Primitive,

    /// <summary>
    /// A managed enum whose underlying primitive type is forwarded as the native ABI representation.
    /// </summary>
    Enum,

    /// <summary>
    /// A native pointer, handle, or reference-by-address value that lowers to <c>void*</c> or a typed pointer.
    /// </summary>
    Pointer,

    /// <summary>
    /// A value type whose fields are mirrored into a generated native struct with matching layout.
    /// </summary>
    Struct,

    /// <summary>
    /// A managed delegate or function pointer that lowers to a native function-pointer type carrying its own
    /// calling convention and signature.
    /// </summary>
    FunctionPointer
}
