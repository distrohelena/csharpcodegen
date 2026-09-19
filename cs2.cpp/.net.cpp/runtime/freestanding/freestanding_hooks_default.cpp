#include "freestanding_hooks.hpp"

#include <stdlib.h>

// MinGW's PE/COFF backend does not honour __attribute__((weak)) on functions
// across translation units: a weak-only definition is left undefined at link
// time instead of satisfying the reference (verified with a two-TU repro on
// this host's GCC 16 / binutils 2.46). Emit a strong symbol there instead;
// every other GCC/Clang target (ELF, and the llvm-mos 65816 toolchain) keeps
// the real weak default so a platform can still override it with its own
// strong symbol.
#if (defined(__GNUC__) || defined(__clang__)) && !defined(__MINGW32__) && !defined(_WIN32)
#define HE_CPP_FREESTANDING_WEAK __attribute__((weak))
#else
#define HE_CPP_FREESTANDING_WEAK
#endif

namespace he_cpp_custom {

/// <summary>Default allocator: the C heap. A platform overrides by defining a strong symbol.</summary>
HE_CPP_FREESTANDING_WEAK void* Allocate(size_t size) {
    void* memory = malloc(size == 0 ? 1 : size);
    if (memory == nullptr) {
        Fail("Out of memory");
    }
    return memory;
}

/// <summary>Default release for memory obtained from Allocate.</summary>
HE_CPP_FREESTANDING_WEAK void Free(void* memory) {
    free(memory);
}

}
