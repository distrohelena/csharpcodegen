#pragma once

#include "freestanding_hash_table.hpp"

namespace he_cpp_freestanding {

/// <summary>
/// Unordered set front over the open-addressing table. Any insertion (insert or emplace) may
/// rehash the underlying table, invalidating every iterator, pointer and reference into the set
/// obtained before that call. See FreestandingHashTable's class comment for the mechanism.
/// </summary>
template <typename TValue, typename THash, typename TEqual>
class FreestandingHashSet {
    struct KeyOf { const TValue& operator()(const TValue& entry) const { return entry; } };
    using Table = FreestandingHashTable<TValue, TValue, KeyOf, THash, TEqual>;

public:
    using value_type = TValue;
    using iterator = typename Table::Iterator;
    using const_iterator = typename Table::ConstIterator;
    using InsertResult = typename Table::InsertResult;

    FreestandingHashSet() = default;
    /// <summary>Reserves room for expected values up front. Explicit because generated code
    /// direct-initialises a HashSet from an element count, never converts one implicitly.</summary>
    explicit FreestandingHashSet(size_t expected) { reserve(expected); }

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
    // value may alias this table's own storage (set.insert(*set.begin())): Storage.Emplace calls
    // build() before it does anything that could rehash the table (see its comment), so the copy the
    // build lambda makes below always reads valid memory regardless of any later rehash.
    InsertResult insert(const TValue& value) { return Storage.Emplace(value, [&]() { return value; }); }
    // Builds value locally first (so it is independent of the table's own storage even when args
    // aliased it), then moves it into Emplace's build lambda: a successful insert moves the already-
    // built value into the table instead of taking the extra copy that routing through insert(const
    // TValue&) would.
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
