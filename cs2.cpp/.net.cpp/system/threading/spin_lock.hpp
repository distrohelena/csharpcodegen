#pragma once

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
