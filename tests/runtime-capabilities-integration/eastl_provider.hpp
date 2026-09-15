#pragma once

#include <EASTL/string.h>
#include <EASTL/vector.h>
#include <EASTL/hash_map.h>
#include <EASTL/hash_set.h>
#include <EASTL/functional.h>
#include "single_thread_shared_ptr.hpp"
#include <cstdint>

// Consumer-owned adapter: the generator has no knowledge of EASTL or an SDK.
namespace he_cpp_custom {
using String = eastl::string;
template<class T> using Vector = eastl::vector<T>;
template<class K, class V, class H, class E>
using UnorderedMap = eastl::hash_map<K, V, H, E>;
template<class T, class H, class E>
using UnorderedSet = eastl::hash_set<T, H, E>;
template<class T> using Hash = eastl::hash<T>;
template<class T> using Function = eastl::function<T>;
template<class T> using SharedPtr = single_thread_fixture::SingleThreadSharedPtr<T>;
std::uint64_t MonotonicMicroseconds();

// Supplied by the test host; a console consumer supplies its own fatal handler.
[[noreturn]] void Fail(const char* message);
}
