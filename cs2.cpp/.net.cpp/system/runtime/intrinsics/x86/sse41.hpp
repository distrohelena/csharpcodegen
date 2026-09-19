#pragma once

// Probe the optional generated configuration directly so this header fails with its own
// diagnostic even when the freestanding runtime has not wired up a custom provider yet:
// otherwise native_runtime.hpp's unconditional provider-header include would abort the
// translation unit first with a "file not found" error instead of this one.
#if defined(__has_include)
#if __has_include("helcpp_config.hpp")
#include "helcpp_config.hpp"
#endif
#endif

#ifndef HE_CPP_RUNTIME_HAS_HOSTED_SERVICES
#define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 1
#endif

#if !HE_CPP_RUNTIME_HAS_HOSTED_SERVICES
#error "This runtime service needs hosted threading or OS facilities and is unavailable under the freestanding runtime."
#endif

#include "runtime/native_runtime.hpp"

#include "../vector128.hpp"

class Sse41 {
public:
    static bool get_IsSupported() {
        return false;
    }

    template <typename T>
    static Vector128_1<T> Blend(const Vector128_1<T>& left, const Vector128_1<T>& right, int32_t control) {
        Vector128_1<T> result;
        for (int32_t laneIndex = 0; laneIndex < Vector128_1<T>::LaneCount; ++laneIndex) {
            bool useRight = ((control >> laneIndex) & 1) != 0;
            result.Values[laneIndex] = useRight ? right.Values[laneIndex] : left.Values[laneIndex];
        }
        return result;
    }
};
