#ifndef HE_CPP_RUNTIME_FUNCTION_POINTER_HPP
#define HE_CPP_RUNTIME_FUNCTION_POINTER_HPP

#include <cstddef>

#include "native_algorithm.hpp"

template <typename TReturn, typename... TArgs>
class FunctionPointer {
public:
    using PointerType = TReturn(*)(TArgs...);

    constexpr FunctionPointer() noexcept = default;

    constexpr FunctionPointer(std::nullptr_t) noexcept : pointer(nullptr) {
    }

    constexpr FunctionPointer(PointerType value) noexcept : pointer(value) {
    }

    constexpr FunctionPointer& operator=(std::nullptr_t) noexcept {
        pointer = nullptr;
        return *this;
    }

    constexpr FunctionPointer& operator=(PointerType value) noexcept {
        pointer = value;
        return *this;
    }

    constexpr explicit operator bool() const noexcept {
        return pointer != nullptr;
    }

    constexpr bool operator==(std::nullptr_t) const noexcept {
        return pointer == nullptr;
    }

    constexpr bool operator!=(std::nullptr_t) const noexcept {
        return pointer != nullptr;
    }

    constexpr PointerType get() const noexcept {
        return pointer;
    }

    TReturn operator()(TArgs... args) const {
        return pointer(he_cpp_alg::Forward<TArgs>(args)...);
    }
private:
    PointerType pointer = nullptr;
};

template <typename TReturn, typename... TArgs>
constexpr bool operator==(std::nullptr_t, const FunctionPointer<TReturn, TArgs...>& value) noexcept {
    return value == nullptr;
}

template <typename TReturn, typename... TArgs>
constexpr bool operator!=(std::nullptr_t, const FunctionPointer<TReturn, TArgs...>& value) noexcept {
    return value != nullptr;
}

#endif
