#pragma once

#include "../native_algorithm.hpp"
#include "freestanding_hooks.hpp"

#include <new>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

namespace he_cpp_freestanding {

/// <summary>
/// Open-addressing hash table with linear probing and tombstones. TEntry
/// carries the stored object; TKeyOf extracts the key to hash and compare.
/// Rehashes at three quarters load; capacity is a power of two from 8.
/// </summary>
template <typename TEntry, typename TKey, typename TKeyOf, typename THash, typename TEqual>
class FreestandingHashTable {
    enum class SlotState : uint8_t { Empty, Occupied, Deleted };

public:
    /// <summary>Forward iterator over occupied slots.</summary>
    class Iterator {
    public:
        Iterator(FreestandingHashTable* table, size_t index) : Table(table), Index(index) { SkipUnoccupied(); }
        TEntry& operator*() const { return Table->Entries[Index]; }
        TEntry* operator->() const { return Table->Entries + Index; }
        Iterator& operator++() { ++Index; SkipUnoccupied(); return *this; }
        bool operator==(const Iterator& other) const { return Index == other.Index; }
        bool operator!=(const Iterator& other) const { return Index != other.Index; }
        size_t SlotIndex() const { return Index; }
    private:
        FreestandingHashTable* Table;
        size_t Index;
        // Capacity == 0 with States == nullptr on a default-constructed table: the loop condition
        // is checked before the array is touched, so this never dereferences a null States pointer.
        void SkipUnoccupied() { while (Index < Table->Capacity && Table->States[Index] != SlotState::Occupied) ++Index; }
    };
    using ConstIterator = Iterator;

    /// <summary>Result of an insertion: the slot iterator and whether a new entry was created.</summary>
    struct InsertResult {
        Iterator first;
        bool second;
    };

    FreestandingHashTable() : Entries(nullptr), States(nullptr), Capacity(0), Count(0), Tombstones(0) {}
    FreestandingHashTable(const FreestandingHashTable& other) : FreestandingHashTable() {
        reserve(other.Count);
        for (size_t index = 0; index < other.Capacity; ++index) {
            if (other.States[index] == SlotState::Occupied) InsertUnique(other.Entries[index]);
        }
    }
    FreestandingHashTable(FreestandingHashTable&& other) noexcept
        : Entries(other.Entries), States(other.States), Capacity(other.Capacity), Count(other.Count), Tombstones(other.Tombstones) {
        other.Entries = nullptr; other.States = nullptr; other.Capacity = 0; other.Count = 0; other.Tombstones = 0;
    }
    ~FreestandingHashTable() { Destroy(); }
    FreestandingHashTable& operator=(const FreestandingHashTable& other) {
        if (this != &other) { FreestandingHashTable copy(other); Swap(copy); }
        return *this;
    }
    FreestandingHashTable& operator=(FreestandingHashTable&& other) noexcept {
        if (this != &other) { FreestandingHashTable moved(he_cpp_alg::Move(other)); Swap(moved); }
        return *this;
    }

    size_t size() const { return Count; }
    bool empty() const { return Count == 0; }
    Iterator begin() { return Iterator(this, 0); }
    Iterator end() { return Iterator(this, Capacity); }
    Iterator begin() const { return Iterator(const_cast<FreestandingHashTable*>(this), 0); }
    Iterator end() const { return Iterator(const_cast<FreestandingHashTable*>(this), Capacity); }

    Iterator Find(const TKey& key) const {
        size_t index;
        return FindSlot(key, index) ? Iterator(const_cast<FreestandingHashTable*>(this), index) : end();
    }
    template <typename TBuild>
    InsertResult Emplace(const TKey& key, TBuild build) {
        size_t index;
        if (FindSlot(key, index)) return InsertResult{ Iterator(this, index), false };
        EnsureRoom();
        index = ProbeForInsert(key);
        new (Entries + index) TEntry(build());
        States[index] = SlotState::Occupied;
        ++Count;
        return InsertResult{ Iterator(this, index), true };
    }
    size_t Erase(const TKey& key) {
        size_t index;
        if (!FindSlot(key, index)) return 0;
        EraseSlot(index);
        return 1;
    }
    Iterator Erase(Iterator position) {
        size_t index = position.SlotIndex();
        EraseSlot(index);
        return Iterator(this, index + 1);
    }
    void clear() {
        for (size_t index = 0; index < Capacity; ++index) {
            if (States[index] == SlotState::Occupied) Entries[index].~TEntry();
            States[index] = SlotState::Empty;
        }
        Count = 0;
        Tombstones = 0;
    }
    void reserve(size_t expected) {
        size_t needed = 8;
        while (needed * 3 / 4 < expected) needed = CheckedDouble(needed);
        if (needed > Capacity) Rehash(needed);
    }
    void Swap(FreestandingHashTable& other) noexcept {
        he_cpp_alg::Swap(Entries, other.Entries);
        he_cpp_alg::Swap(States, other.States);
        he_cpp_alg::Swap(Capacity, other.Capacity);
        he_cpp_alg::Swap(Count, other.Count);
        he_cpp_alg::Swap(Tombstones, other.Tombstones);
    }

private:
    TEntry* Entries;
    SlotState* States;
    size_t Capacity;
    size_t Count;
    size_t Tombstones;

    static size_t HashKey(const TKey& key) { return THash{}(key); }
    static bool KeysEqual(const TKey& left, const TKey& right) { return TEqual{}(left, right); }

    /// <summary>Doubles value, failing rather than silently wrapping a 16-bit size_t.</summary>
    static size_t CheckedDouble(size_t value) {
        if (value > static_cast<size_t>(-1) / 2) he_cpp_custom::Fail("Hash table capacity overflow");
        return value * 2;
    }

    bool FindSlot(const TKey& key, size_t& found) const {
        if (Capacity == 0) return false;
        size_t mask = Capacity - 1;
        size_t index = HashKey(key) & mask;
        for (size_t probe = 0; probe < Capacity; ++probe) {
            SlotState state = States[index];
            if (state == SlotState::Empty) return false;
            if (state == SlotState::Occupied && KeysEqual(TKeyOf{}(Entries[index]), key)) { found = index; return true; }
            index = (index + 1) & mask;
        }
        return false;
    }
    // Non-const so it can decrement Tombstones when it reuses a Deleted slot. Finding an existing key
    // already happened in FindSlot before this is ever called, so this only has to find *a* usable
    // slot: it stops at the first non-Occupied slot -- Empty or Deleted -- which is always valid.
    size_t ProbeForInsert(const TKey& key) {
        size_t mask = Capacity - 1;
        size_t index = HashKey(key) & mask;
        while (States[index] == SlotState::Occupied) index = (index + 1) & mask;
        if (States[index] == SlotState::Deleted) --Tombstones;
        return index;
    }
    void EnsureRoom() {
        if (Capacity == 0) { Rehash(8); return; }
        if ((Count + Tombstones + 1) * 4 > Capacity * 3) Rehash(Count * 2 < 8 ? 8 : NextPowerOfTwo(Count * 2 + 1));
    }
    static size_t NextPowerOfTwo(size_t value) {
        size_t result = 8;
        while (result < value) result = CheckedDouble(result);
        return result;
    }
    void Rehash(size_t newCapacity) {
        if (newCapacity > static_cast<size_t>(-1) / sizeof(TEntry)) he_cpp_custom::Fail("Hash table allocation size overflow");
        if (newCapacity > static_cast<size_t>(-1) / sizeof(SlotState)) he_cpp_custom::Fail("Hash table allocation size overflow");
        TEntry* oldEntries = Entries;
        SlotState* oldStates = States;
        size_t oldCapacity = Capacity;
        Entries = static_cast<TEntry*>(he_cpp_custom::Allocate(newCapacity * sizeof(TEntry)));
        States = static_cast<SlotState*>(he_cpp_custom::Allocate(newCapacity * sizeof(SlotState)));
        memset(States, 0, newCapacity * sizeof(SlotState));
        Capacity = newCapacity;
        Count = 0;
        Tombstones = 0;
        for (size_t index = 0; index < oldCapacity; ++index) {
            if (oldStates[index] == SlotState::Occupied) {
                InsertUnique(he_cpp_alg::Move(oldEntries[index]));
                oldEntries[index].~TEntry();
            }
        }
        if (oldEntries != nullptr) he_cpp_custom::Free(oldEntries);
        if (oldStates != nullptr) he_cpp_custom::Free(oldStates);
    }
    template <typename TSource>
    void InsertUnique(TSource&& entry) {
        size_t index = ProbeForInsert(TKeyOf{}(entry));
        new (Entries + index) TEntry(he_cpp_alg::Forward<TSource>(entry));
        States[index] = SlotState::Occupied;
        ++Count;
    }
    void EraseSlot(size_t index) {
        Entries[index].~TEntry();
        States[index] = SlotState::Deleted;
        --Count;
        ++Tombstones;
    }
    void Destroy() {
        clear();
        if (Entries != nullptr) he_cpp_custom::Free(Entries);
        if (States != nullptr) he_cpp_custom::Free(States);
        Entries = nullptr; States = nullptr; Capacity = 0;
    }
};

}
