#pragma once

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
$GENERATED_FUNCTION_PROFILING_SPIN_LOCK_INCLUDE$

/// <summary>
/// Represents the managed SpinLock helper surface expected by transpiled multithreaded coordination code.
/// </summary>
class SpinLock {
public:
    /// <summary>
    /// Initializes one unlocked spin lock instance.
    /// </summary>
    SpinLock()
        : Locked(false) {
    }

    /// <summary>
    /// Acquires the lock and reports the acquisition result through the managed-style by-reference flag.
    /// </summary>
    /// <param name="lockTaken">Receives <c>true</c> once the lock has been acquired.</param>
    void Enter(bool& lockTaken) {
        $GENERATED_FUNCTION_PROFILING_SPIN_LOCK_BEFORE_ENTER$
        while (Locked.exchange(true, std::memory_order_acquire)) {
        }

        $GENERATED_FUNCTION_PROFILING_SPIN_LOCK_AFTER_ENTER$
        lockTaken = true;
    }

    /// <summary>
    /// Releases the lock.
    /// </summary>
    void Exit() {
        Locked.store(false, std::memory_order_release);
        $GENERATED_FUNCTION_PROFILING_SPIN_LOCK_AFTER_EXIT$
    }

private:
    std::atomic<bool> Locked;
    $GENERATED_FUNCTION_PROFILING_SPIN_LOCK_FIELD$
};
