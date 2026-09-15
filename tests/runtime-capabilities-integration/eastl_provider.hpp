#pragma once

#include <EASTL/string.h>
#include <EASTL/vector.h>
#include <EASTL/hash_map.h>

// Consumer-owned adapter: the generator has no knowledge of EASTL or an SDK.
namespace he_cpp_custom {
using String = eastl::string;
template<class T> using Vector = eastl::vector<T>;
template<class K, class V, class H, class E>
using UnorderedMap = eastl::hash_map<K, V, H, E>;
template<class T> using Hash = eastl::hash<T>;

// Supplied by the test host; a console consumer supplies its own fatal handler.
[[noreturn]] void Fail(const char* message);
}
