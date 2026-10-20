#pragma once

/// <summary>Shares infrequent runtime operations instead of duplicating their bodies into generated callers.</summary>
#if defined(_MSC_VER)
#define HE_CPP_NOINLINE __declspec(noinline)
#elif defined(__GNUC__) || defined(__clang__)
#define HE_CPP_NOINLINE __attribute__((noinline))
#else
#define HE_CPP_NOINLINE
#endif
