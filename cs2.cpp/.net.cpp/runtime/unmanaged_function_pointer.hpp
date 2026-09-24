#ifndef HE_CPP_RUNTIME_UNMANAGED_FUNCTION_POINTER_HPP
#define HE_CPP_RUNTIME_UNMANAGED_FUNCTION_POINTER_HPP

#include <cstddef>

#include "native_algorithm.hpp"
#include "native_calling_convention.hpp"

template <typename TReturn, typename... TArgs>
class StdcallFunctionPointer {
public:
    using PointerType = TReturn(HE_CPP_STDCALL*)(TArgs...);

    constexpr StdcallFunctionPointer() noexcept = default;

    constexpr StdcallFunctionPointer(std::nullptr_t) noexcept : pointer(nullptr) {
    }

    constexpr StdcallFunctionPointer(PointerType value) noexcept : pointer(value) {
    }

    constexpr StdcallFunctionPointer& operator=(std::nullptr_t) noexcept {
        pointer = nullptr;
        return *this;
    }

    constexpr StdcallFunctionPointer& operator=(PointerType value) noexcept {
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
constexpr bool operator==(std::nullptr_t, const StdcallFunctionPointer<TReturn, TArgs...>& value) noexcept {
    return value == nullptr;
}

template <typename TReturn, typename... TArgs>
constexpr bool operator!=(std::nullptr_t, const StdcallFunctionPointer<TReturn, TArgs...>& value) noexcept {
    return value != nullptr;
}

template <typename TReturn, typename... TArgs>
class CdeclFunctionPointer {
public:
    using PointerType = TReturn(HE_CPP_CDECL*)(TArgs...);

    constexpr CdeclFunctionPointer() noexcept = default;

    constexpr CdeclFunctionPointer(std::nullptr_t) noexcept : pointer(nullptr) {
    }

    constexpr CdeclFunctionPointer(PointerType value) noexcept : pointer(value) {
    }

    constexpr CdeclFunctionPointer& operator=(std::nullptr_t) noexcept {
        pointer = nullptr;
        return *this;
    }

    constexpr CdeclFunctionPointer& operator=(PointerType value) noexcept {
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
constexpr bool operator==(std::nullptr_t, const CdeclFunctionPointer<TReturn, TArgs...>& value) noexcept {
    return value == nullptr;
}

template <typename TReturn, typename... TArgs>
constexpr bool operator!=(std::nullptr_t, const CdeclFunctionPointer<TReturn, TArgs...>& value) noexcept {
    return value != nullptr;
}

template <typename TReturn, typename... TArgs>
constexpr typename StdcallFunctionPointer<TReturn, TArgs...>::PointerType he_cpp_raw_function_pointer(const StdcallFunctionPointer<TReturn, TArgs...>& value) noexcept {
    return value.get();
}

template <typename TReturn, typename... TArgs>
constexpr typename CdeclFunctionPointer<TReturn, TArgs...>::PointerType he_cpp_raw_function_pointer(const CdeclFunctionPointer<TReturn, TArgs...>& value) noexcept {
    return value.get();
}

template <typename TPointer>
constexpr TPointer* he_cpp_raw_function_pointer(TPointer* value) noexcept {
    return value;
}

#endif
