#pragma once
#include "native_deferred_static_lifetime.hpp"
extern "C" {
    extern int ModuleDestructions;
    extern int ResidentDestructions;
}
/// <summary>Counts actual value destruction independently of the library that stores the deferred object.</summary>
class DeferredStaticTrackedValue {
    /// <summary>References the host-owned counter, which remains observable after a module unloads.</summary>
    int* Destructions;
public:
    /// <summary>Provides a stable value to check across module reloads.</summary>
    int Value = 37;
    /// <summary>Associates this instance with the appropriate host counter.</summary>
    explicit DeferredStaticTrackedValue(int* destructions) : Destructions(destructions) {}
    /// <summary>Records cleanup even after the owning library's exported functions become inaccessible.</summary>
    ~DeferredStaticTrackedValue() { ++*Destructions; }
};
