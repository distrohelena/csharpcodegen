#ifndef HE_CPP_SYSTEM_THREADING_VOLATILE_HPP
#define HE_CPP_SYSTEM_THREADING_VOLATILE_HPP

// Probe the generated configuration first so this header reports its own diagnostic
// before the provider chain is pulled in.
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

class Volatile {
public:
    template <typename T>
    static T Read(T& location) {
        std::atomic_ref<T> atomicLocation(location);
        return atomicLocation.load(std::memory_order_acquire);
    }

    template <typename T, typename TValue>
    static void Write(T& location, TValue value) {
        std::atomic_ref<T> atomicLocation(location);
        atomicLocation.store(static_cast<T>(value), std::memory_order_release);
    }
};

#endif
