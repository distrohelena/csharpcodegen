#pragma once

#include <stddef.h>
#include <stdint.h>

// Hooks the freestanding runtime needs from the platform. Allocate and Free
// have weak defaults over malloc/free in freestanding_hooks_default.cpp;
// Fail and MonotonicMicroseconds must be defined by the platform.
namespace he_cpp_custom {
void* Allocate(size_t size);
void Free(void* memory);
[[noreturn]] void Fail(const char* message);
uint64_t MonotonicMicroseconds();
}
