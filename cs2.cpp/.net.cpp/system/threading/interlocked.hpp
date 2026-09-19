#ifndef HE_CPP_SYSTEM_THREADING_INTERLOCKED_HPP
#define HE_CPP_SYSTEM_THREADING_INTERLOCKED_HPP

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

#include "../../runtime/native_runtime.hpp"

#include <atomic>

class Interlocked {
public:
    template <typename T>
    static T Increment(T& location) {
        std::atomic_ref<T> atomicLocation(location);
        return atomicLocation.fetch_add(static_cast<T>(1), std::memory_order_acq_rel) + static_cast<T>(1);
    }

    template <typename T>
    static T Decrement(T& location) {
        std::atomic_ref<T> atomicLocation(location);
        return atomicLocation.fetch_sub(static_cast<T>(1), std::memory_order_acq_rel) - static_cast<T>(1);
    }

    template <typename T>
    static T Add(T& location, T value) {
        std::atomic_ref<T> atomicLocation(location);
        return atomicLocation.fetch_add(value, std::memory_order_acq_rel) + value;
    }

    template <typename T>
    static T CompareExchange(T& location, T value, T comparand) {
        std::atomic_ref<T> atomicLocation(location);
        atomicLocation.compare_exchange_strong(comparand, value, std::memory_order_acq_rel);
        return comparand;
    }
};

#endif
