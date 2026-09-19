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

/// <summary>Hash for integral, enum and pointer keys: their object bytes. Strings specialise below.</summary>
template <typename T>
struct FreestandingHash {
    static_assert(std::is_integral_v<T> || std::is_enum_v<T> || std::is_pointer_v<T>,
                  "FreestandingHash supports integral, enum, pointer and FreestandingString keys.");
    size_t operator()(const T& value) const {
        return HashBytes(&value, sizeof(T));
    }
};

}
