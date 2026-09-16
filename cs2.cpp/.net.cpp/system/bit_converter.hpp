#pragma once

#include <cstdint>
#include "runtime/native_endian.hpp"
#include "runtime/native_memory_ops.hpp"
#include "../runtime/array.hpp"

/// <summary>
/// Provides the minimal managed BitConverter surface required by transpiled serializer code.
/// </summary>
class BitConverter {
public:
    /// <summary>
    /// True when this CPU stores the least significant byte first, matching
    /// System.BitConverter.IsLittleEndian. The conversions below reinterpret
    /// the bits of a value already in host order, so they need no swapping;
    /// callers that hand the bytes to a wire format are the ones that must
    /// know the order, and this is how they ask.
    /// </summary>
    static constexpr bool IsLittleEndian = he_cpp_endian::HostIsLittleEndian;

    /// <summary>
    /// Reinterprets the supplied integer bits as a single-precision floating point value.
    /// </summary>
    static float Int32BitsToSingle(int32_t value) {
        float result = 0.0f;
        he_cpp_memory::Copy(&result, &value, sizeof(result));
        return result;
    }

    /// <summary>
    /// Reinterprets the supplied integer bits as a double-precision floating point value.
    /// </summary>
    static double Int64BitsToDouble(int64_t value) {
        double result = 0.0;
        he_cpp_memory::Copy(&result, &value, sizeof(result));
        return result;
    }

    /// <summary>
    /// Reinterprets the supplied double-precision floating point value as a 64-bit integer bit pattern.
    /// </summary>
    static int64_t DoubleToInt64Bits(double value) {
        int64_t result = 0;
        he_cpp_memory::Copy(&result, &value, sizeof(result));
        return result;
    }

    /// <summary>
    /// Reinterprets the supplied single-precision floating point value as a 32-bit integer bit pattern.
    /// </summary>
    static int32_t SingleToInt32Bits(float value) {
        int32_t result = 0;
        he_cpp_memory::Copy(&result, &value, sizeof(result));
        return result;
    }

    /// <summary>
    /// Packs the supplied single-precision floating point value into a managed-style byte array.
    /// </summary>
    static Array<uint8_t>* GetBytes(float value) {
        Array<uint8_t>* bytes = new Array<uint8_t>(sizeof(value));
        he_cpp_memory::Copy(bytes->Data, &value, sizeof(value));
        return bytes;
    }
};
