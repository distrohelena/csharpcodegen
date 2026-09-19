#pragma once

#include <stddef.h>
#include <stdint.h>
#include <type_traits>

namespace he_cpp_freestanding {

/// <summary>FNV-1a over a byte range; 32-bit so it is cheap on 8/16-bit targets.</summary>
inline size_t HashBytes(const void* data, size_t length) {
    const unsigned char* bytes = static_cast<const unsigned char*>(data);
    uint32_t hash = 2166136261u;
    for (size_t index = 0; index < length; ++index) {
        hash ^= bytes[index];
        hash *= 16777619u;
    }
    return static_cast<size_t>(hash);
}

/// <summary>
/// Hash for integral, floating-point, enum and pointer keys: their object bytes. Strings specialise
/// below. Floating-point zero is normalised first: +0.0 and -0.0 compare equal, so a valid hash must
/// return the same value for both (he_cpp_get_hash_code(0.0f) and (-0.0f) is exercised by the smoke
/// test); NaN carries no such requirement, since a NaN never compares equal to anything, itself
/// included.
/// </summary>
template <typename T>
struct FreestandingHash {
    static_assert(std::is_integral_v<T> || std::is_enum_v<T> || std::is_pointer_v<T> || std::is_floating_point_v<T>,
                  "FreestandingHash supports integral, floating-point, enum, pointer and FreestandingString keys.");
    size_t operator()(const T& value) const {
        if constexpr (std::is_floating_point_v<T>) {
            T normalized = value == static_cast<T>(0) ? static_cast<T>(0) : value;
            return HashBytes(&normalized, sizeof(T));
        } else {
            return HashBytes(&value, sizeof(T));
        }
    }
};

}
