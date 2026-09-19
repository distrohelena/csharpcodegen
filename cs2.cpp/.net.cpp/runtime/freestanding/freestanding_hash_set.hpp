#pragma once

#include "freestanding_hash_table.hpp"

namespace he_cpp_freestanding {

/// <summary>Unordered set front over the open-addressing table.</summary>
template <typename TValue, typename THash, typename TEqual>
class FreestandingHashSet {
    struct KeyOf { const TValue& operator()(const TValue& entry) const { return entry; } };
    using Table = FreestandingHashTable<TValue, TValue, KeyOf, THash, TEqual>;

public:
    using value_type = TValue;
    using iterator = typename Table::Iterator;
    using const_iterator = typename Table::ConstIterator;
    using InsertResult = typename Table::InsertResult;

    size_t size() const { return Storage.size(); }
    bool empty() const { return Storage.empty(); }
    iterator begin() { return Storage.begin(); }
    iterator end() { return Storage.end(); }
    const_iterator begin() const { return Storage.begin(); }
    const_iterator end() const { return Storage.end(); }
    iterator find(const TValue& value) { return Storage.Find(value); }
    const_iterator find(const TValue& value) const { return Storage.Find(value); }
    size_t count(const TValue& value) const { return Storage.Find(value) != Storage.end() ? 1 : 0; }
    bool contains(const TValue& value) const { return count(value) != 0; }
    // value is copied up front, before Storage.Emplace is called: it may alias this table's own
    // storage (set.insert(*set.begin())), and Storage.Emplace's EnsureRoom can rehash -- freeing the
    // old Entries array -- before the build lambda below would otherwise read the original reference.
    InsertResult insert(const TValue& value) {
        TValue copy(value);
        return Storage.Emplace(copy, [&]() { return he_cpp_alg::Move(copy); });
    }
    template <typename... TArgs>
    InsertResult emplace(TArgs&&... args) {
        TValue value(he_cpp_alg::Forward<TArgs>(args)...);
        return Storage.Emplace(value, [&]() { return he_cpp_alg::Move(value); });
    }
    size_t erase(const TValue& value) { return Storage.Erase(value); }
    iterator erase(iterator position) { return Storage.Erase(position); }
    void clear() { Storage.clear(); }
    void reserve(size_t expected) { Storage.reserve(expected); }
    void swap(FreestandingHashSet& other) noexcept { Storage.Swap(other.Storage); }

private:
    Table Storage;
};

}
