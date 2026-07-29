#pragma once

#include <cstddef>
#include <cstdint>
#include <type_traits>
#include <unordered_set>

#include "native_hash.hpp"

class StringComparer;

template<typename TValue>
class NativeHashSetHash {
public:
    std::size_t operator()(const TValue& value) const {
        return static_cast<std::size_t>(he_cpp_get_hash_code(value));
    }
};

template<typename TValue>
class NativeHashSetEqual {
public:
    bool operator()(const TValue& left, const TValue& right) const {
        if constexpr (std::is_pointer_v<TValue>) {
            return left == right;
        } else if constexpr (requires(TValue value) { value.Equals(right); }) {
            return const_cast<TValue&>(left).Equals(right);
        } else {
            return left == right;
        }
    }
};

template<typename TValue>
class HashSet : public std::unordered_set<TValue, NativeHashSetHash<TValue>, NativeHashSetEqual<TValue>> {
public:
    using std::unordered_set<TValue, NativeHashSetHash<TValue>, NativeHashSetEqual<TValue>>::unordered_set;

    explicit HashSet(const StringComparer&) {
    }

    bool Add(const TValue& value) {
        return this->insert(value).second;
    }

    void Clear() {
        this->clear();
    }

    bool Contains(const TValue& value) const {
        return this->find(value) != this->end();
    }

    bool Remove(const TValue& value) {
        return this->erase(value) > 0;
    }

    int32_t Count() const {
        return static_cast<int32_t>(this->size());
    }

    int32_t get_Count() const {
        return Count();
    }
};
