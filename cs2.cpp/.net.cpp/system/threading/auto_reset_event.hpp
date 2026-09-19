#ifndef HE_CPP_SYSTEM_THREADING_AUTO_RESET_EVENT_HPP
#define HE_CPP_SYSTEM_THREADING_AUTO_RESET_EVENT_HPP

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

#include <condition_variable>
#include <cstdint>
#include <mutex>

class AutoResetEvent {
public:
    explicit AutoResetEvent(bool initialState = false)
        : signaled(initialState) {
    }

    void Set() {
        {
            std::lock_guard<std::mutex> lock(mutex);
            signaled = true;
        }

        condition.notify_one();
    }

    void WaitOne() {
        std::unique_lock<std::mutex> lock(mutex);
        condition.wait(lock, [this]() {
            return signaled;
        });
        signaled = false;
    }

    void Dispose() {
    }

private:
    std::condition_variable condition;
    std::mutex mutex;
    bool signaled;
};

#endif
