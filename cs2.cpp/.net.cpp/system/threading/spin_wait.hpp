#ifndef HE_CPP_SYSTEM_THREADING_SPIN_WAIT_HPP
#define HE_CPP_SYSTEM_THREADING_SPIN_WAIT_HPP

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

#include <chrono>
#include <cstdint>
#include <thread>

class SpinWait {
public:
    SpinWait() = default;

    void Reset() {
        count = 0;
    }

    void SpinOnce(int32_t sleep1Threshold = -1) {
        if (sleep1Threshold >= 0 && count >= sleep1Threshold) {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        } else {
            std::this_thread::yield();
        }

        ++count;
    }
private:
    int32_t count = 0;
};

#endif
