#pragma once

#include <cstddef>
#include <cstdint>
#include <type_traits>

#include "native_exceptions.hpp"
#include "native_runtime.hpp"
#include "native_string.hpp"
#include "../system/collections/generic/key_value_pair.hpp"

class StringComparer;

template<typename TKey>
class NativeDictionaryHash {
public:
    std::size_t operator()(const TKey& key) const {
        if constexpr (std::is_pointer_v<TKey>) {
            return HeCppHash<TKey>{}(key);
        } else if constexpr (requires(TKey value) { value.GetHashCode(); }) {
            return static_cast<std::size_t>(const_cast<TKey&>(key).GetHashCode());
        } else {
            return HeCppHash<TKey>{}(key);
        }
    }
};

template<typename TKey>
class NativeDictionaryEqual {
public:
    bool operator()(const TKey& left, const TKey& right) const {
        if constexpr (std::is_pointer_v<TKey>) {
            return left == right;
        } else if constexpr (requires(TKey value) { value.Equals(right); }) {
            return const_cast<TKey&>(left).Equals(right);
        } else {
            return left == right;
        }
    }
};

template<typename TKey, typename TValue>
class Dictionary : public HeCppUnorderedMap<TKey, TValue, NativeDictionaryHash<TKey>, NativeDictionaryEqual<TKey>> {
    using Base = HeCppUnorderedMap<TKey, TValue, NativeDictionaryHash<TKey>, NativeDictionaryEqual<TKey>>;

    /// <summary>Adapts an STL entry to the managed KeyValuePair shape.</summary>
    class ManagedIterator {
        typename Base::const_iterator Iterator;

    public:
        explicit ManagedIterator(typename Base::const_iterator iterator)
            : Iterator(iterator) {
        }

        KeyValuePair<TKey, TValue> operator*() const {
            return KeyValuePair<TKey, TValue>(Iterator->first, Iterator->second);
        }

        ManagedIterator& operator++() {
            ++Iterator;
            return *this;
        }

        bool operator!=(const ManagedIterator& other) const {
            return Iterator != other.Iterator;
        }
    };

    /// <summary>
    /// Tracks whether this dictionary owns its pointer values and must delete them on removal and destruction.
    /// </summary>
    bool OwnsValuesFlag = false;

    /// <summary>
    /// Deletes one stored value when this dictionary owns its pointer values.
    /// </summary>
    void DeleteOwnedValue(const TValue& value) {
        if constexpr (std::is_pointer_v<TValue>) {
            if (OwnsValuesFlag) {
                delete value;
            }
        }
    }

    /// <summary>
    /// Deletes every stored value when this dictionary owns its pointer values.
    /// </summary>
    void DeleteOwnedValues() {
        if constexpr (std::is_pointer_v<TValue>) {
            if (OwnsValuesFlag) {
                for (const auto& pair : static_cast<const Base&>(*this)) {
                    delete pair.second;
                }
            }
        }
    }

public:
    using Base::Base;

    explicit Dictionary(const StringComparer&) {
    }

    ~Dictionary() {
        DeleteOwnedValues();
        this->clear();
    }

    ManagedIterator begin() { return ManagedIterator(Base::begin()); }
    ManagedIterator end() { return ManagedIterator(Base::end()); }
    ManagedIterator begin() const { return ManagedIterator(Base::begin()); }
    ManagedIterator end() const { return ManagedIterator(Base::end()); }

    /// <summary>
    /// Gets whether this dictionary owns its pointer values.
    /// </summary>
    bool get_OwnsValues() const {
        return OwnsValuesFlag;
    }

    /// <summary>
    /// Releases this dictionary's ownership claim over its values so another verified owner can assume cleanup responsibility.
    /// </summary>
    void DetachOwned() {
        OwnsValuesFlag = false;
    }

    // value may be a reference into this dictionary's own storage (dict.set_Item(a, dict.get_Item(b)),
    // the Dictionary-level shape of dict[a] = dict[b]): operator[] below may insert a new key and
    // rehash the underlying table, which invalidates every reference into the table taken before that
    // call -- including this parameter, if the caller's argument expression read straight out of the
    // table. Copy into an independent local before touching the table, so the copy-assignment always
    // reads valid memory regardless of any rehash operator[] performs.
    void Add(const TKey& key, const TValue& value) {
        if constexpr (std::is_pointer_v<TValue>) {
            if (OwnsValuesFlag) {
                he_cpp_raise(InvalidOperationException("Cannot insert a borrowed value into a dictionary that owns its values."));
            }
        }

        TValue copy(value);
        (*this)[key] = he_cpp_alg::Move(copy);
    }

    /// <summary>
    /// Stores one value whose ownership transfers to this dictionary; the dictionary deletes it on removal and destruction.
    /// </summary>
    void AddOwned(const TKey& key, const TValue& value) {
        static_assert(std::is_pointer_v<TValue>, "AddOwned requires pointer values.");
        if (!OwnsValuesFlag && !this->empty()) {
            he_cpp_raise(InvalidOperationException("Cannot insert an owned value into a dictionary that already borrows its values."));
        }

        OwnsValuesFlag = true;
        TValue copy(value);
        auto iterator = this->find(key);
        if (iterator != Base::end() && iterator->second != copy) {
            delete iterator->second;
        }

        (*this)[key] = he_cpp_alg::Move(copy);
    }

    TValue& get_Item(const TKey& key) {
        return (*this)[key];
    }

    const TValue& get_Item(const TKey& key) const {
        return this->at(key);
    }

    void set_Item(const TKey& key, const TValue& value) {
        TValue copy(value);
        if constexpr (std::is_pointer_v<TValue>) {
            auto iterator = this->find(key);
            if (iterator != Base::end() && iterator->second != copy) {
                DeleteOwnedValue(iterator->second);
            }
        }

        (*this)[key] = he_cpp_alg::Move(copy);
    }

    bool ContainsKey(const TKey& key) const {
        return this->find(key) != Base::end();
    }

    bool Remove(const TKey& key) {
        auto iterator = this->find(key);
        if (iterator == Base::end()) {
            return false;
        }

        TValue removedValue = iterator->second;
        this->erase(iterator);
        DeleteOwnedValue(removedValue);
        return true;
    }

    void Clear() {
        DeleteOwnedValues();
        this->clear();
    }

    bool TryGetValue(const TKey& key, TValue& value) const {
        auto iterator = this->find(key);
        if (iterator == Base::end()) {
            return false;
        }

        value = iterator->second;
        return true;
    }

    HeCppVector<TKey> Keys() const {
        HeCppVector<TKey> keys;
        keys.reserve(this->size());
        for (const auto& pair : static_cast<const Base&>(*this)) {
            keys.push_back(pair.first);
        }

        return keys;
    }

    int32_t Count() const {
        return static_cast<int32_t>(this->size());
    }

    int32_t get_Count() const {
        return Count();
    }
};
