#pragma once

#include <stddef.h>
#include <stdint.h>

// Hooks the freestanding runtime needs from the platform. Allocate and Free
// have weak defaults over malloc/free in freestanding_hooks_default.cpp, so a
// platform can override them with a strong symbol; on MinGW/Windows hosts that
// default is instead a strong definition, since that toolchain's PE/COFF
// linker does not resolve a weak-only function definition against an
// undefined reference from another translation unit. Fail and
// MonotonicMicroseconds must be defined by the platform. The block Allocate
// returns must be suitably aligned for any object the runtime places in it --
// at least alignof(max_align_t) on the target -- since the containers construct
// arbitrary element types straight into it with placement new.
namespace he_cpp_custom {
void* Allocate(size_t size);
void Free(void* memory);
[[noreturn]] void Fail(const char* message);
uint64_t MonotonicMicroseconds();
}
