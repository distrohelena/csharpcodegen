#pragma once

#include "native_deferred_static.hpp"

/// <summary>Registers source/module cleanup separately from constant-initialized value storage; the non-constexpr constructor preserves GCC lifetime reference counting for header-defined generic statics.</summary>
template <typename TValue>
class HeCppDeferredStaticLifetime {
    /// <summary>References already-live static storage without constructing or resetting its deferred value.</summary>
    HeCppDeferredStaticStorage<TValue>& Storage;

public:
    /// <summary>Registers ownership without changing a value an earlier startup reader may have already constructed.</summary>
    /// <param name="storage">Constant-initialized storage that outlives this finalization registration.</param>
    explicit HeCppDeferredStaticLifetime(HeCppDeferredStaticStorage<TValue>& storage) : Storage(storage) {}
    /// <summary>Prevents copying the right to finalize a shared static value.</summary>
    HeCppDeferredStaticLifetime(const HeCppDeferredStaticLifetime&) = delete;
    /// <summary>Prevents replacing a finalizer's storage owner.</summary>
    HeCppDeferredStaticLifetime& operator=(const HeCppDeferredStaticLifetime&) = delete;
    /// <summary>Destroys the stored value when its owning source/module lifetime ends.</summary>
    ~HeCppDeferredStaticLifetime() { Storage.Destroy(); }
};
