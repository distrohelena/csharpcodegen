#pragma once

#include "native_module_lifetime.hpp"
#include <type_traits>
#include <utility>

/// <summary>Owns a callable and keeps its defining code alive until callable destruction ends.</summary>
template <typename TCallable>
class HeCppModulePinnedCallable {
    /// <summary>Remains alive until after the callable's destructor has executed.</summary>
    HeCppCodeModulePin Pin;

    /// <summary>Stores the original callable with its captures and invocation semantics.</summary>
    TCallable Callable;

public:
    /// <summary>Acquires code ownership before constructing the callable storage.</summary>
    template <typename TValue>
    HeCppModulePinnedCallable(const char* moduleId, TValue&& callable)
        : Pin(moduleId), Callable(std::forward<TValue>(callable)) {
    }

    /// <summary>Retains code before copying callable storage into another owner.</summary>
    HeCppModulePinnedCallable(const HeCppModulePinnedCallable&) = default;

    /// <summary>Transfers the pin before moving callable storage while code remains mapped.</summary>
    HeCppModulePinnedCallable(HeCppModulePinnedCallable&&) = default;

    /// <summary>Replaces callable storage while both its previous and replacement modules are pinned.</summary>
    HeCppModulePinnedCallable& operator=(const HeCppModulePinnedCallable& other)
        requires std::is_copy_assignable_v<TCallable> {
        if (this != &other) {
            Callable = other.Callable;
            Pin = other.Pin;
        }
        return *this;
    }

    /// <summary>Moves replacement storage before retiring the previous callable's code pin.</summary>
    HeCppModulePinnedCallable& operator=(HeCppModulePinnedCallable&& other)
        requires std::is_move_assignable_v<TCallable> {
        if (this != &other) {
            Callable = std::move(other.Callable);
            Pin = std::move(other.Pin);
        }
        return *this;
    }

    /// <summary>Forwards mutable invocation and preserves argument and return references.</summary>
    template <typename... TArgs>
    decltype(auto) operator()(TArgs&&... args) {
        return Callable(std::forward<TArgs>(args)...);
    }

    /// <summary>Forwards const invocation for standard and custom delegate containers.</summary>
    template <typename... TArgs>
    decltype(auto) operator()(TArgs&&... args) const {
        return Callable(std::forward<TArgs>(args)...);
    }
};

/// <summary>Wraps an emitted callback so storing it outside a scene retains its defining module.</summary>
template <typename TCallable>
auto HeCppPinCodeModuleCallable(const char* moduleId, TCallable&& callable) {
    return HeCppModulePinnedCallable<typename std::decay<TCallable>::type>(
        moduleId, std::forward<TCallable>(callable));
}
