#pragma once

// Freestanding replacements for the few <algorithm>, <utility> and <memory>
// helpers the shared runtime uses. Targets such as the 65816 toolchain ship
// only a partial standard library (no std::move, std::min, std::find_if,
// std::index_sequence), so the runtime routes through these names on every
// target. Nothing here touches namespace std.

#include <limits>
#include <stddef.h>
#include <type_traits>

namespace he_cpp_alg {

/// <summary>Casts a value to an rvalue reference so it can be moved from.</summary>
template <typename T>
constexpr std::remove_reference_t<T>&& Move(T&& value) noexcept {
    return static_cast<std::remove_reference_t<T>&&>(value);
}

/// <summary>Forwards an lvalue as the deduced reference type.</summary>
template <typename T>
constexpr T&& Forward(std::remove_reference_t<T>& value) noexcept {
    return static_cast<T&&>(value);
}

/// <summary>Forwards an rvalue as the deduced reference type.</summary>
template <typename T>
constexpr T&& Forward(std::remove_reference_t<T>&& value) noexcept {
    static_assert(!std::is_lvalue_reference_v<T>, "Cannot forward an rvalue as an lvalue.");
    return static_cast<T&&>(value);
}

/// <summary>Exchanges two values through a temporary.</summary>
template <typename T>
void Swap(T& left, T& right) {
    T temporary(Move(left));
    left = Move(right);
    right = Move(temporary);
}

/// <summary>Returns the smaller of two values, the first on ties.</summary>
template <typename T>
constexpr const T& Min(const T& left, const T& right) {
    return right < left ? right : left;
}

/// <summary>Returns the larger of two values, the first on ties.</summary>
template <typename T>
constexpr const T& Max(const T& left, const T& right) {
    return left < right ? right : left;
}

/// <summary>Returns the first iterator whose element satisfies the predicate, or last.</summary>
template <typename TIterator, typename TPredicate>
TIterator FindIf(TIterator first, TIterator last, TPredicate predicate) {
    for (; first != last; ++first) {
        if (predicate(*first)) {
            return first;
        }
    }
    return last;
}

/// <summary>Moves the elements that do not satisfy the predicate to the front and returns the new end.</summary>
template <typename TIterator, typename TPredicate>
TIterator RemoveIf(TIterator first, TIterator last, TPredicate predicate) {
    TIterator writer = first;
    for (; first != last; ++first) {
        if (!predicate(*first)) {
            if (writer != first) {
                *writer = Move(*first);
            }
            ++writer;
        }
    }
    return writer;
}

/// <summary>Replaces every element equal to oldValue with newValue.</summary>
template <typename TIterator, typename T>
void Replace(TIterator first, TIterator last, const T& oldValue, const T& newValue) {
    for (; first != last; ++first) {
        if (*first == oldValue) {
            *first = newValue;
        }
    }
}

/// <summary>Copies count elements from first to output and returns the output end.</summary>
template <typename TInput, typename TSize, typename TOutput>
TOutput CopyN(TInput first, TSize count, TOutput output) {
    for (TSize index = 0; index < count; ++index) {
        *output = *first;
        ++output;
        ++first;
    }
    return output;
}

/// <summary>Assigns value to count elements starting at first and returns the end.</summary>
template <typename TOutput, typename TSize, typename T>
TOutput FillN(TOutput first, TSize count, const T& value) {
    for (TSize index = 0; index < count; ++index) {
        *first = value;
        ++first;
    }
    return first;
}

/// <summary>Applies operation to each element and writes the results to output.</summary>
template <typename TInput, typename TOutput, typename TOperation>
TOutput Transform(TInput first, TInput last, TOutput output, TOperation operation) {
    for (; first != last; ++first) {
        *output = operation(*first);
        ++output;
    }
    return output;
}

/// <summary>Counts the steps from first to last.</summary>
template <typename TIterator>
ptrdiff_t Distance(TIterator first, TIterator last) {
    ptrdiff_t count = 0;
    for (; first != last; ++first) {
        ++count;
    }
    return count;
}

/// <summary>Returns the address of a reference even when operator& is overloaded.</summary>
template <typename T>
T* AddressOf(T& value) noexcept {
    return __builtin_addressof(value);
}

/// <summary>Compile-time index pack used to unpack argument arrays.</summary>
template <size_t... Indexes>
struct IndexSequence {
    static constexpr size_t Size = sizeof...(Indexes);
};

namespace detail {

template <size_t N, size_t... Indexes>
struct MakeIndexSequenceImpl : MakeIndexSequenceImpl<N - 1, N - 1, Indexes...> {
};

template <size_t... Indexes>
struct MakeIndexSequenceImpl<0, Indexes...> {
    using Type = IndexSequence<Indexes...>;
};

}

/// <summary>IndexSequence of 0..N-1.</summary>
template <size_t N>
using MakeIndexSequence = typename detail::MakeIndexSequenceImpl<N>::Type;

/// <summary>IndexSequence with one index per type in the pack.</summary>
template <typename... T>
using IndexSequenceFor = MakeIndexSequence<sizeof...(T)>;

/// <summary>Invokes a member function pointer on a pointer receiver.</summary>
template <typename TResult, typename TClass, typename... TParams, typename TReceiver, typename... TArgs,
          std::enable_if_t<std::is_pointer_v<std::remove_reference_t<TReceiver>>, int> = 0>
decltype(auto) Invoke(TResult (TClass::*method)(TParams...), TReceiver&& receiver, TArgs&&... args) {
    return (receiver->*method)(Forward<TArgs>(args)...);
}

/// <summary>Invokes a member function pointer on a reference receiver.</summary>
template <typename TResult, typename TClass, typename... TParams, typename TReceiver, typename... TArgs,
          std::enable_if_t<!std::is_pointer_v<std::remove_reference_t<TReceiver>>, int> = 0>
decltype(auto) Invoke(TResult (TClass::*method)(TParams...), TReceiver&& receiver, TArgs&&... args) {
    return (receiver.*method)(Forward<TArgs>(args)...);
}

/// <summary>Invokes a const member function pointer on a pointer receiver.</summary>
template <typename TResult, typename TClass, typename... TParams, typename TReceiver, typename... TArgs,
          std::enable_if_t<std::is_pointer_v<std::remove_reference_t<TReceiver>>, int> = 0>
decltype(auto) Invoke(TResult (TClass::*method)(TParams...) const, TReceiver&& receiver, TArgs&&... args) {
    return (receiver->*method)(Forward<TArgs>(args)...);
}

/// <summary>Invokes a const member function pointer on a reference receiver.</summary>
template <typename TResult, typename TClass, typename... TParams, typename TReceiver, typename... TArgs,
          std::enable_if_t<!std::is_pointer_v<std::remove_reference_t<TReceiver>>, int> = 0>
decltype(auto) Invoke(TResult (TClass::*method)(TParams...) const, TReceiver&& receiver, TArgs&&... args) {
    return (receiver.*method)(Forward<TArgs>(args)...);
}

/// <summary>Invokes any other callable with the given arguments.</summary>
template <typename TCallable, typename... TArgs,
          std::enable_if_t<!std::is_member_function_pointer_v<std::remove_cv_t<std::remove_reference_t<TCallable>>>, int> = 0>
decltype(auto) Invoke(TCallable&& callable, TArgs&&... args) {
    return Forward<TCallable>(callable)(Forward<TArgs>(args)...);
}

/// <summary>Positive infinity even where numeric_limits reports none.</summary>
template <typename T>
constexpr T Infinity() {
    if constexpr (std::numeric_limits<T>::has_infinity) {
        return std::numeric_limits<T>::infinity();
    } else {
        return static_cast<T>(__builtin_inf());
    }
}

/// <summary>Quiet NaN even where numeric_limits reports none.</summary>
template <typename T>
constexpr T QuietNaN() {
    if constexpr (std::numeric_limits<T>::has_quiet_NaN) {
        return std::numeric_limits<T>::quiet_NaN();
    } else {
        return static_cast<T>(__builtin_nan(""));
    }
}

}
