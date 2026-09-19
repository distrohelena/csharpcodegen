#pragma once

#include "freestanding_hash_table.hpp"

namespace he_cpp_freestanding {

/// <summary>Key/value pair stored by the map; named to match the pair members the runtime reads.</summary>
template <typename TKey, typename TValue>
struct FreestandingKeyValue {
    TKey first;
    TValue second;
};

/// <summary>Unordered map front over the open-addressing table.</summary>
template <typename TKey, typename TValue, typename THash, typename TEqual>
class FreestandingHashMap {
    using Entry = FreestandingKeyValue<TKey, TValue>;
    struct KeyOf { const TKey& operator()(const Entry& entry) const { return entry.first; } };
    using Table = FreestandingHashTable<Entry, TKey, KeyOf, THash, TEqual>;

public:
    using key_type = TKey;
    using mapped_type = TValue;
    using value_type = Entry;
    using iterator = typename Table::Iterator;
    using const_iterator = typename Table::ConstIterator;
    using InsertResult = typename Table::InsertResult;

    size_t size() const { return Storage.size(); }
    bool empty() const { return Storage.empty(); }
    iterator begin() { return Storage.begin(); }
    iterator end() { return Storage.end(); }
    const_iterator begin() const { return Storage.begin(); }
    const_iterator end() const { return Storage.end(); }
    iterator find(const TKey& key) { return Storage.Find(key); }
    const_iterator find(const TKey& key) const { return Storage.Find(key); }
    size_t count(const TKey& key) const { return Storage.Find(key) != Storage.end() ? 1 : 0; }
    bool contains(const TKey& key) const { return count(key) != 0; }

    // key and value are copied into locals up front, before Storage.Emplace is called at all. Either
    // argument can alias this table's own storage (map.emplace(map.find(j)->first, map.find(k)->second)),
    // and Storage.Emplace's EnsureRoom can rehash -- freeing the old Entries array -- before the build
    // lambda below would otherwise read the original (now possibly dangling) key/value references.
    // Building from these independent, stack-local copies keeps every read safe regardless of rehashing.
    template <typename TK, typename TV>
    InsertResult emplace(TK&& key, TV&& value) {
        TKey keyCopy(he_cpp_alg::Forward<TK>(key));
        TValue valueCopy(he_cpp_alg::Forward<TV>(value));
        return Storage.Emplace(keyCopy, [&]() { return Entry{ he_cpp_alg::Move(keyCopy), he_cpp_alg::Move(valueCopy) }; });
    }
    template <typename TK, typename... TArgs>
    InsertResult try_emplace(TK&& key, TArgs&&... args) {
        TKey keyCopy(he_cpp_alg::Forward<TK>(key));
        TValue valueCopy(he_cpp_alg::Forward<TArgs>(args)...);
        return Storage.Emplace(keyCopy, [&]() { return Entry{ he_cpp_alg::Move(keyCopy), he_cpp_alg::Move(valueCopy) }; });
    }
    // entry may itself be a reference into this table (insert(*find(other))); copy it before doing
    // anything that could rehash the table out from under that reference.
    InsertResult insert(const Entry& entry) {
        Entry copy(entry);
        return Storage.Emplace(copy.first, [&]() { return he_cpp_alg::Move(copy); });
    }
    template <typename TV>
    InsertResult insert_or_assign(const TKey& key, TV&& value) {
        TKey keyCopy(key);
        TValue valueCopy(he_cpp_alg::Forward<TV>(value));
        InsertResult result = Storage.Emplace(keyCopy, [&]() { return Entry{ he_cpp_alg::Move(keyCopy), he_cpp_alg::Move(valueCopy) }; });
        if (!result.second) result.first->second = he_cpp_alg::Move(valueCopy);
        return result;
    }
    TValue& operator[](const TKey& key) {
        TKey keyCopy(key);
        return Storage.Emplace(keyCopy, [&]() { return Entry{ he_cpp_alg::Move(keyCopy), TValue() }; }).first->second;
    }
    size_t erase(const TKey& key) { return Storage.Erase(key); }
    iterator erase(iterator position) { return Storage.Erase(position); }
    void clear() { Storage.clear(); }
    void reserve(size_t expected) { Storage.reserve(expected); }
    void swap(FreestandingHashMap& other) noexcept { Storage.Swap(other.Storage); }

private:
    Table Storage;
};

}
