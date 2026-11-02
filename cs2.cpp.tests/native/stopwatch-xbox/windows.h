#pragma once
#include <cstdint>
/// Native signed performance-counter storage provided by the Xbox Win32 ABI.
struct LARGE_INTEGER { std::int64_t QuadPart; };
using BOOL = int;
BOOL QueryPerformanceCounter(LARGE_INTEGER* value);
BOOL QueryPerformanceFrequency(LARGE_INTEGER* value);
