#pragma once
#include <cstddef>
#if !defined(__GNUC__) && !defined(__clang__)
#include <cstring>
#endif

/// <summary>Provides byte operations without requiring hosted C++ headers on freestanding compilers.</summary>
namespace he_cpp_memory {
/// <summary>Copies bytes between non-overlapping regions.</summary>
inline void* Copy(void* destination, const void* source, std::size_t count) {
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_memcpy(destination, source, count);
#else
    return std::memcpy(destination, source, count);
#endif
}
/// <summary>Copies bytes safely when the source and destination overlap.</summary>
inline void* Move(void* destination, const void* source, std::size_t count) {
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_memmove(destination, source, count);
#else
    return std::memmove(destination, source, count);
#endif
}
/// <summary>Fills a region with the low byte of the supplied value.</summary>
inline void* Set(void* destination, int value, std::size_t count) {
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_memset(destination, value, count);
#else
    return std::memset(destination, value, count);
#endif
}
/// <summary>Compares byte regions lexicographically using unsigned byte values.</summary>
inline int Compare(const void* left, const void* right, std::size_t count) {
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_memcmp(left, right, count);
#else
    return std::memcmp(left, right, count);
#endif
}
}

