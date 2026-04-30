#pragma once

/// <summary>
/// Provides the lightweight cast helper used by transpiled declaration-pattern lowering.
/// </summary>
template <typename TTarget, typename TSource>
inline TTarget* he_cpp_try_cast(TSource* source) {
    if (source == nullptr) {
        return nullptr;
    }

    return static_cast<TTarget*>(source);
}
