#pragma once

// Codegen-owned provider for targets without a hosted C++ library. Selected
// by the freestanding runtime profile through HE_CPP_RUNTIME_PROVIDER_HEADER;
// a platform may point that macro at its own header instead.

#include "freestanding_function.hpp"
#include "freestanding_hash.hpp"
#include "freestanding_hash_map.hpp"
#include "freestanding_hash_set.hpp"
#include "freestanding_hooks.hpp"
#include "freestanding_shared_ptr.hpp"
#include "freestanding_string.hpp"
#include "freestanding_vector.hpp"

namespace he_cpp_custom {
using String = he_cpp_freestanding::FreestandingString;
template <class T> using Vector = he_cpp_freestanding::FreestandingVector<T>;
template <class K, class V, class H, class E> using UnorderedMap = he_cpp_freestanding::FreestandingHashMap<K, V, H, E>;
template <class T, class H, class E> using UnorderedSet = he_cpp_freestanding::FreestandingHashSet<T, H, E>;
template <class T> using Hash = he_cpp_freestanding::FreestandingHash<T>;
template <class T> using Function = he_cpp_freestanding::FreestandingFunction<T>;
template <class T> using SharedPtr = he_cpp_freestanding::FreestandingSharedPtr<T>;
}
