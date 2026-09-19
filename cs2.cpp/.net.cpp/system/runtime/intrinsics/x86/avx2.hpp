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

class Avx2 {
public:
    static bool get_IsSupported() {
        return false;
    }

    template <typename TValue, typename TShift>
    static Vector128_1<TValue> ShiftRightLogicalVariable(const Vector128_1<TValue>& value, const Vector128_1<TShift>& shift) {
        Vector128_1<TValue> result;
        for (int32_t laneIndex = 0; laneIndex < Vector128_1<TValue>::LaneCount; ++laneIndex) {
            uint32_t laneValue = static_cast<uint32_t>(value.Values[laneIndex]);
            uint32_t shiftCount = static_cast<uint32_t>(shift.Values[laneIndex]) & 31u;
            result.Values[laneIndex] = static_cast<TValue>(laneValue >> shiftCount);
        }

        return result;
    }
};
