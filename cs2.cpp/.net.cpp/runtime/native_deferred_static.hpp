#pragma once

#include <new>
#include "native_algorithm.hpp"

/// <summary>Holds constant-initialized storage for a guarded accessor; a separate HeCppDeferredStaticLifetime owns source/module cleanup without relying on function-local atexit callbacks.</summary>
template <typename TValue>
class HeCppDeferredStaticStorage {
    /// <summary>Reserves correctly aligned bytes before any dynamic initializer can request the value.</summary>
    alignas(TValue) unsigned char Storage[sizeof(TValue)] = {};
    /// <summary>Records successful placement construction so unused or failed initializations have no destructor side effects.</summary>
    bool Constructed = false;

public:
    /// <summary>Allows global backing storage to be initialized before every dynamic startup callback.</summary>
    constexpr HeCppDeferredStaticStorage() = default;
    /// <summary>Prevents copying bytes that own a separately constructed object's lifetime.</summary>
    HeCppDeferredStaticStorage(const HeCppDeferredStaticStorage&) = delete;
    /// <summary>Prevents assigning ownership of an in-place static value.</summary>
    HeCppDeferredStaticStorage& operator=(const HeCppDeferredStaticStorage&) = delete;
    /// <summary>Releases the constructed value when its explicit lifetime owner finalizes; the storage itself stays trivially destructible for constant initialization.</summary>
    void Destroy() {
        if (Constructed) {
            Constructed = false;
            std::launder(reinterpret_cast<TValue*>(Storage))->~TValue();
        }
    }

    /// <summary>Constructs once under the accessor's native local-static guard and returns the stored object without another allocation for ownership.</summary>
    /// <param name="value">Initializer forwarded to the stored value constructor.</param>
    /// <returns>Reference whose lifetime belongs to this storage owner.</returns>
    template <typename TInitializer>
    TValue& Initialize(TInitializer&& value) {
        TValue* result = new (Storage) TValue(he_cpp_alg::Forward<TInitializer>(value));
        Constructed = true;
        return *result;
    }
};
