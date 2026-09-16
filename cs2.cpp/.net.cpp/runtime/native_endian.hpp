#pragma once

#include <cstddef>
#include <cstdint>

/// <summary>
/// Host byte order and the byte-order decision binary serialization depends on.
/// </summary>
/// <remarks>
/// A wire format has its own byte order, chosen by the caller, and the CPU has
/// its own. Converting between them means swapping exactly when the two
/// disagree. Code that assumes the host is little-endian reads and writes
/// byte-swapped values on a big-endian target, silently and only there, which
/// is why the decision lives in one place.
///
/// The two orders are independent and must not be conflated.
/// HE_CPP_PLATFORM_IS_LITTLE_ENDIAN describes the byte order of serialized
/// data, from the platform's PlatformSerializationEndianness, and says nothing
/// about the CPU: the Nintendo 64 is a big-endian machine whose cooked data is
/// declared little-endian, which is a legitimate configuration. Only the
/// compiler can answer what the CPU is, so only the compiler is asked.
/// </remarks>
namespace he_cpp_endian {

/// <summary>True when the CPU stores the least significant byte first.</summary>
#if defined(__BYTE_ORDER__) && defined(__ORDER_LITTLE_ENDIAN__)
inline constexpr bool HostIsLittleEndian =
    __BYTE_ORDER__ == __ORDER_LITTLE_ENDIAN__;
#elif defined(_MSC_VER)
// MSVC targets x86, x64 and ARM in little-endian mode only.
inline constexpr bool HostIsLittleEndian = true;
#else
#error "Cannot determine the CPU byte order: this compiler defines neither __BYTE_ORDER__ nor _MSC_VER."
#endif

/// <summary>
/// True when a value in the given wire byte order must be byte-swapped to
/// match the given host byte order. Both orders are parameters so the rule can
/// be checked for a host the test is not running on.
/// </summary>
constexpr bool NeedsSwapBetween(bool wireIsLittleEndian,
                                bool hostIsLittleEndian) {
    return wireIsLittleEndian != hostIsLittleEndian;
}

/// <summary>
/// True when a value in the given wire byte order must be byte-swapped on this
/// host.
/// </summary>
constexpr bool NeedsSwap(bool wireIsLittleEndian) {
    return NeedsSwapBetween(wireIsLittleEndian, HostIsLittleEndian);
}

/// <summary>Reverses the byte order of the buffer in place.</summary>
inline void ReverseBytes(std::uint8_t* bytes, std::size_t size) {
    if (size < 2) {
        return;
    }
    for (std::size_t low = 0, high = size - 1; low < high; ++low, --high) {
        const std::uint8_t swap = bytes[low];
        bytes[low] = bytes[high];
        bytes[high] = swap;
    }
}

} // namespace he_cpp_endian
