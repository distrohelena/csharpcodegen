#pragma once

#include <cstdint>
#include <unordered_map>

#include "native_string.hpp"

template<typename TKey, typename TValue>
class Dictionary : public std::unordered_map<TKey, TValue> {
public:
    using std::unordered_map<TKey, TValue>::unordered_map;

    void Add(const TKey& key, const TValue& value) {
        this->insert_or_assign(key, value);
    }

    bool ContainsKey(const TKey& key) const {
        return this->find(key) != this->end();
    }

    int32_t Count() const {
        return static_cast<int32_t>(this->size());
    }
};
