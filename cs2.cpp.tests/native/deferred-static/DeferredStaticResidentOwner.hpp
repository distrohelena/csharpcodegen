#pragma once
#include "DeferredStaticTrackedValue.hpp"
/// <summary>Reproduces the shared runtime empty-string lifetime across the executable and a loadable module.</summary>
class DeferredStaticResidentOwner {
    /// <summary>Shares one value's storage through ordinary weak symbol resolution.</summary>
    inline static HeCppDeferredStaticStorage<DeferredStaticTrackedValue> Storage;
    /// <summary>Retains the shared value while its resident translation unit is alive.</summary>
    inline static HeCppDeferredStaticLifetime<DeferredStaticTrackedValue> Lifetime{Storage};
public:
    /// <summary>Returns the same resident value before and after library retirement.</summary>
    static const DeferredStaticTrackedValue& Get() {
        (void)&Lifetime;
        static const DeferredStaticTrackedValue& value = Storage.Initialize(&ResidentDestructions);
        return value;
    }
};
