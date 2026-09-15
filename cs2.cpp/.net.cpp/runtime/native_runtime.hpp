#pragma once

#include <functional>
#include <type_traits>

// The generated configuration is optional for direct hosted consumers.  When it
// is present, its capability values remain the single source of truth.
#if defined(__has_include)
#if __has_include("helcpp_config.hpp")
#include "helcpp_config.hpp"
#endif
#endif

#ifndef HE_CPP_USE_STD_STRING
#define HE_CPP_USE_STD_STRING 1
#endif

#ifndef HE_CPP_USE_STD_VECTOR
#define HE_CPP_USE_STD_VECTOR 1
#endif

#ifndef HE_CPP_USE_STD_UNORDERED_MAP
#define HE_CPP_USE_STD_UNORDERED_MAP 1
#endif

#ifndef HE_CPP_USE_EXCEPTIONS
#define HE_CPP_USE_EXCEPTIONS 1
#endif

#ifndef HE_CPP_USE_RTTI
#define HE_CPP_USE_RTTI 1
#endif

#if !HE_CPP_USE_STD_STRING || !HE_CPP_USE_STD_VECTOR || !HE_CPP_USE_STD_UNORDERED_MAP
#if !defined(HE_CPP_RUNTIME_PROVIDER_HEADER)
#error "A custom runtime provider is required when standard string, vector, or unordered_map storage is disabled. Define HE_CPP_RUNTIME_PROVIDER_HEADER to a header that declares he_cpp_custom::String, Vector<T>, UnorderedMap<K,V,H,E>, Hash<T>, and Fail(const char*)."
#endif
#endif

#if defined(HE_CPP_RUNTIME_PROVIDER_HEADER)
#include HE_CPP_RUNTIME_PROVIDER_HEADER
#endif

#if HE_CPP_USE_STD_STRING
#include <string>
#endif

#if HE_CPP_USE_STD_VECTOR
#include <vector>
#endif

#if HE_CPP_USE_STD_UNORDERED_MAP
#include <unordered_map>
#endif

#if defined(HE_CPP_RUNTIME_PROVIDER_HEADER) && (!HE_CPP_USE_STD_UNORDERED_MAP || !HE_CPP_USE_STD_STRING)
#define HE_CPP_RUNTIME_USE_CUSTOM_HASH 1
#else
#define HE_CPP_RUNTIME_USE_CUSTOM_HASH 0
#endif

#if !defined(HE_CPP_RUNTIME_PROVIDER_HEADER)
#if defined(__has_include)
#if __has_include(<cstdlib>)
#include <cstdlib>
#define HE_CPP_RUNTIME_HAS_STD_ABORT 1
#endif
#endif
#endif

#ifndef HE_CPP_RUNTIME_HAS_STD_ABORT
#define HE_CPP_RUNTIME_HAS_STD_ABORT 0
#endif

#if HE_CPP_USE_STD_STRING
using HeCppString = std::string;
#else
using HeCppString = he_cpp_custom::String;
#endif

#if HE_CPP_USE_STD_VECTOR
template <typename TValue>
using HeCppVector = std::vector<TValue>;
#else
template <typename TValue>
using HeCppVector = he_cpp_custom::Vector<TValue>;
#endif

#if HE_CPP_USE_STD_UNORDERED_MAP
template <typename TKey, typename TValue, typename THash, typename TEqual>
using HeCppUnorderedMap = std::unordered_map<TKey, TValue, THash, TEqual>;
#else
template <typename TKey, typename TValue, typename THash, typename TEqual>
using HeCppUnorderedMap = he_cpp_custom::UnorderedMap<TKey, TValue, THash, TEqual>;
#endif

#if HE_CPP_RUNTIME_USE_CUSTOM_HASH
namespace he_cpp_runtime_detail {

/// <summary>
/// Selects standard hashing for the selected standard string while routing every other custom-storage type through the provider.
/// </summary>
template <typename TValue, bool UseStandardHash>
struct HashSelector;

template <typename TValue>
struct HashSelector<TValue, true> : std::hash<TValue> {
};

template <typename TValue>
struct HashSelector<TValue, false> : he_cpp_custom::Hash<TValue> {
};

}

template <typename TValue>
using HeCppHash = he_cpp_runtime_detail::HashSelector<
    TValue,
    HE_CPP_USE_STD_STRING && std::is_same_v<TValue, HeCppString>>;
#else
template <typename TValue>
using HeCppHash = std::hash<TValue>;
#endif

namespace he_cpp_runtime_detail {

/// <summary>
/// Terminates a runtime operation that cannot return a value when no provider supplies a fatal hook.
/// </summary>
[[noreturn]] inline void DefaultFail(const char* message) {
    (void)message;
#if HE_CPP_RUNTIME_HAS_STD_ABORT
    std::abort();
#elif defined(__GNUC__) || defined(__clang__)
    __builtin_trap();
#else
#error "The selected runtime has no standard abort facility and this compiler has no supported trap intrinsic. Provide HE_CPP_RUNTIME_PROVIDER_HEADER with he_cpp_custom::Fail(const char*)."
#endif
}

/// <summary>
/// Raises one runtime exception or routes it to the configured non-returning failure hook.
/// </summary>
/// <typeparam name="TException">Exception value type raised by the runtime operation.</typeparam>
/// <param name="exception">Exception value carrying the managed failure message.</param>
template <typename TException>
[[noreturn]] inline void Raise(const TException& exception) {
#if HE_CPP_USE_EXCEPTIONS
    throw exception;
#else
#if defined(HE_CPP_RUNTIME_PROVIDER_HEADER)
    he_cpp_custom::Fail(exception.what());
#else
    DefaultFail(exception.what());
#endif
#endif
}

}

/// <summary>
/// Raises one runtime exception using C++ exceptions or the configured fatal provider hook.
/// </summary>
/// <typeparam name="TException">Exception value type raised by the runtime operation.</typeparam>
/// <param name="exception">Exception value carrying the managed failure message.</param>
template <typename TException>
[[noreturn]] inline void he_cpp_raise(const TException& exception) {
    he_cpp_runtime_detail::Raise(exception);
}

/// <summary>
/// Raises one runtime exception from a value-returning function without inventing a fallback result.
/// </summary>
/// <typeparam name="TResult">Result type of the surrounding value-returning function.</typeparam>
/// <typeparam name="TException">Exception value type raised by the runtime operation.</typeparam>
/// <param name="exception">Exception value carrying the managed failure message.</param>
template <typename TResult, typename TException>
[[noreturn]] inline TResult he_cpp_raise_value(const TException& exception) {
    he_cpp_runtime_detail::Raise(exception);
}
