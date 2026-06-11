#pragma once

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <malloc.h>

/// <summary>
/// Provides a portable aligned unmanaged allocation surface compatible with System.Runtime.InteropServices.NativeMemory.
/// </summary>
class NativeMemory {
public:
    /// <summary>
    /// Allocates one aligned unmanaged memory block.
    /// </summary>
    /// <param name="byteCount">Number of bytes requested by the caller.</param>
    /// <param name="alignment">Required alignment in bytes.</param>
    /// <returns>Aligned unmanaged pointer on success; otherwise null.</returns>
    static void* AlignedAlloc(uintptr_t byteCount, uintptr_t alignment) {
        size_t normalizedAlignment = std::max<size_t>(static_cast<size_t>(alignment), alignof(void*));
        size_t normalizedByteCount = static_cast<size_t>(byteCount);
        size_t alignedByteCount = normalizedByteCount;
        if (normalizedAlignment > 0) {
            size_t remainder = normalizedByteCount % normalizedAlignment;
            if (remainder != 0) {
                alignedByteCount += normalizedAlignment - remainder;
            }
        }

#if defined(_MSC_VER)
        return _aligned_malloc(alignedByteCount, normalizedAlignment);
#else
        size_t metadataSize = sizeof(void*);
        size_t totalByteCount = alignedByteCount + normalizedAlignment + metadataSize;
        void* baseAllocation = std::malloc(totalByteCount);
        if (baseAllocation == nullptr) {
            return nullptr;
        }

        uintptr_t baseAddress = reinterpret_cast<uintptr_t>(baseAllocation);
        uintptr_t minimumAlignedAddress = baseAddress + metadataSize;
        uintptr_t remainder = minimumAlignedAddress % normalizedAlignment;
        uintptr_t alignedAddress = remainder == 0
            ? minimumAlignedAddress
            : minimumAlignedAddress + (normalizedAlignment - remainder);
        void** metadataSlot = reinterpret_cast<void**>(alignedAddress - metadataSize);
        *metadataSlot = baseAllocation;
        return reinterpret_cast<void*>(alignedAddress);
#endif
    }

    /// <summary>
    /// Releases one aligned unmanaged memory block allocated by <see cref="AlignedAlloc"/>.
    /// </summary>
    /// <param name="value">Pointer returned by one prior aligned allocation.</param>
    static void AlignedFree(void* value) {
        if (value == nullptr) {
            return;
        }

#if defined(_MSC_VER)
        _aligned_free(value);
#else
        void* baseAllocation = *(reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(value) - sizeof(void*)));
        std::free(baseAllocation);
#endif
    }
};
