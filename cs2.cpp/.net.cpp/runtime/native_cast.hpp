#pragma once

#include <type_traits>

#include "native_runtime.hpp"

/// <summary>
/// Provides the lightweight cast helper used by transpiled declaration-pattern lowering.
/// </summary>
template <typename TTarget, typename TSource>
inline auto he_cpp_try_cast(TSource* source)
    -> std::conditional_t<std::is_pointer_v<TTarget>, TTarget, TTarget*> {
    if (source == nullptr) {
        return nullptr;
    }

    if constexpr (std::is_pointer_v<TTarget>) {
#if HE_CPP_USE_RTTI
        return dynamic_cast<TTarget>(source);
#else
        static_assert(std::is_convertible_v<TSource*, TTarget>, "A runtime cast requires RTTI unless the source and target pointer types have a statically valid conversion.");
        return static_cast<TTarget>(source);
#endif
    } else {
#if HE_CPP_USE_RTTI
        return dynamic_cast<TTarget*>(source);
#else
        static_assert(std::is_convertible_v<TSource*, TTarget*>, "A runtime cast requires RTTI unless the source and target pointer types have a statically valid conversion.");
        return static_cast<TTarget*>(source);
#endif
    }
}
