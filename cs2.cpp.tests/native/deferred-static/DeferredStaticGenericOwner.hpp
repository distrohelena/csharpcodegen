#pragma once
#include "DeferredStaticTrackedValue.hpp"
/// <summary>Reproduces the header-visible static definitions emitted for a generic C# constant owner.</summary>
template <typename T>
class DeferredStaticGenericOwner {
    /// <summary>Keeps value bytes ready before dynamic startup callbacks.</summary>
    static HeCppDeferredStaticStorage<DeferredStaticTrackedValue> Storage;
    /// <summary>Registers cleanup with each translation unit/module that instantiates this owner.</summary>
    static HeCppDeferredStaticLifetime<DeferredStaticTrackedValue> Lifetime;
public:
    /// <summary>Constructs the module-owned value once while retaining its cleanup registration.</summary>
    static const DeferredStaticTrackedValue& Get() {
        (void)&Lifetime;
        static const DeferredStaticTrackedValue& value = Storage.Initialize(&ModuleDestructions);
        return value;
    }
};
template <typename T> HeCppDeferredStaticStorage<DeferredStaticTrackedValue> DeferredStaticGenericOwner<T>::Storage;
template <typename T> HeCppDeferredStaticLifetime<DeferredStaticTrackedValue> DeferredStaticGenericOwner<T>::Lifetime(Storage);
