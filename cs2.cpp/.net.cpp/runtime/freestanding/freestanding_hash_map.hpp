#pragma once

#include "freestanding_hash_table.hpp"

namespace he_cpp_freestanding {

/// <summary>Key/value pair stored by the map; named to match the pair members the runtime reads.</summary>
template <typename TKey, typename TValue>
struct FreestandingKeyValue {
    TKey first;
    TValue second;
};

/// <summary>
/// Unordered map front over the open-addressing table. Any insertion (emplace, try_emplace,
/// insert, insert_or_assign, or an operator[] that creates a new key) may rehash the underlying
/// table, invalidating every iterator, pointer and reference into the map obtained before that
/// call -- including a TValue& returned by an earlier operator[] call. See
/// FreestandingHashTable's class comment for the mechanism.
/// </summary>
template <typename TKey, typename TValue, typename THash, typename TEqual>
class FreestandingHashMap {
    using Entry = FreestandingKeyValue<TKey, TValue>;
    struct KeyOf { const TKey& operator()(const Entry& entry) const { return entry.first; } };
    using Table = FreestandingHashTable<Entry, TKey, KeyOf, THash, TEqual>;

public:
    using key_type = TKey;
    using mapped_type = TValue;
    using value_type = Entry;
    using KeyValue = Entry;
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

    // These build lambdas may freely capture key/value by reference, even when they alias this
    // table's own storage (map.emplace(map.find(j)->first, map.find(k)->second)): Storage.Emplace
    // calls build() before it does anything that could rehash the table (see its comment), so every
    // read the lambda performs happens while the table is still exactly as the caller last saw it.
    template <typename TK, typename TV>
    InsertResult emplace(TK&& key, TV&& value) {
        return Storage.Emplace(key, [&]() { return Entry{ TKey(he_cpp_alg::Forward<TK>(key)), TValue(he_cpp_alg::Forward<TV>(value)) }; });
    }
    // Lazy: build() only runs when the key is absent, so an existing key costs zero TValue
    // constructions from args, however expensive they are to build.
    template <typename TK, typename... TArgs>
    InsertResult try_emplace(TK&& key, TArgs&&... args) {
        return Storage.Emplace(key, [&]() { return Entry{ TKey(he_cpp_alg::Forward<TK>(key)), TValue(he_cpp_alg::Forward<TArgs>(args)...) }; });
    }
    InsertResult insert(const Entry& entry) { return Storage.Emplace(entry.first, [&]() { return entry; }); }
    template <typename TV>
    InsertResult insert_or_assign(const TKey& key, TV&& value) {
        InsertResult result = Storage.Emplace(key, [&]() { return Entry{ key, TValue(he_cpp_alg::Forward<TV>(value)) }; });
        if (!result.second) result.first->second = TValue(he_cpp_alg::Forward<TV>(value));
        return result;
    }
    // The returned reference is invalidated by the map's next insertion (see the class comment):
    // map[k] = map[j] is unsafe for a class-typed TValue if that next insertion rehashes, since the
    // right-hand map[j] reference can dangle before the assignment reads it. Copy through a temporary
    // instead -- map[k] = TValue(map[j]) -- to materialise an independent value first.
    TValue& operator[](const TKey& key) { return Storage.Emplace(key, [&]() { return Entry{ key, TValue() }; }).first->second; }
    size_t erase(const TKey& key) { return Storage.Erase(key); }
    iterator erase(iterator position) { return Storage.Erase(position); }
    void clear() { Storage.clear(); }
    void reserve(size_t expected) { Storage.reserve(expected); }
    void swap(FreestandingHashMap& other) noexcept { Storage.Swap(other.Storage); }

private:
    Table Storage;
};

}
