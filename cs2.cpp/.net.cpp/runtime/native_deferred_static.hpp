#pragma once

#include <new>
#include "native_algorithm.hpp"

/// <summary>Owns constant-initialized storage whose value is constructed by a guarded accessor and destroyed by its source/module finalizer, without relying on atexit for a function-local object.</summary>
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
    /// <summary>Destroys a successfully constructed value when the owning source/module finalizes.</summary>
    ~HeCppDeferredStaticStorage() {
        if (Constructed) {
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
