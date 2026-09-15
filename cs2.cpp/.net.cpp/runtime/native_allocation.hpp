#pragma once

#include <cstddef>

#if !defined(__STDC_HOSTED__) || (__STDC_HOSTED__ != 0)
#include <cstdlib>
#else
// Freestanding consumers provide the standard C allocation ABI without a
// hosted C++ wrapper header.
extern "C" void* malloc(std::size_t);
extern "C" void free(void*);
#endif

namespace he_cpp_memory {

inline void* Allocate(std::size_t byteCount) {
#if !defined(__STDC_HOSTED__) || (__STDC_HOSTED__ != 0)
    return std::malloc(byteCount);
#else
    return ::malloc(byteCount);
#endif
}

inline void Free(void* value) {
#if !defined(__STDC_HOSTED__) || (__STDC_HOSTED__ != 0)
    std::free(value);
#else
    ::free(value);
#endif
}

}
