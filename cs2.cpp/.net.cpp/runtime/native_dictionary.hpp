#pragma once

#include <cstddef>
#include <cstdint>
#include <functional>
#include <type_traits>
#include <unordered_map>
#include <vector>

#include "native_exceptions.hpp"
#include "native_string.hpp"

class StringComparer;

template<typename TKey>
class NativeDictionaryHash {
public:
    std::size_t operator()(const TKey& key) const {
        if constexpr (std::is_pointer_v<TKey>) {
            return std::hash<TKey>{}(key);
        } else if constexpr (requires(TKey value) { value.GetHashCode(); }) {
            return static_cast<std::size_t>(const_cast<TKey&>(key).GetHashCode());
        } else {
            return std::hash<TKey>{}(key);
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
class Dictionary : public std::unordered_map<TKey, TValue, NativeDictionaryHash<TKey>, NativeDictionaryEqual<TKey>> {
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
                for (const auto& pair : *this) {
                    delete pair.second;
                }
            }
        }
    }

public:
    using std::unordered_map<TKey, TValue, NativeDictionaryHash<TKey>, NativeDictionaryEqual<TKey>>::unordered_map;

    explicit Dictionary(const StringComparer&) {
    }

    ~Dictionary() {
        DeleteOwnedValues();
        this->clear();
    }

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

    void Add(const TKey& key, const TValue& value) {
        if constexpr (std::is_pointer_v<TValue>) {
            if (OwnsValuesFlag) {
                throw InvalidOperationException("Cannot insert a borrowed value into a dictionary that owns its values.");
            }
        }

        this->insert_or_assign(key, value);
    }

    /// <summary>
    /// Stores one value whose ownership transfers to this dictionary; the dictionary deletes it on removal and destruction.
    /// </summary>
    void AddOwned(const TKey& key, const TValue& value) {
        static_assert(std::is_pointer_v<TValue>, "AddOwned requires pointer values.");
        if (!OwnsValuesFlag && !this->empty()) {
            throw InvalidOperationException("Cannot insert an owned value into a dictionary that already borrows its values.");
        }

        OwnsValuesFlag = true;
        auto iterator = this->find(key);
        if (iterator != this->end() && iterator->second != value) {
            delete iterator->second;
        }

        this->insert_or_assign(key, value);
    }

    TValue& get_Item(const TKey& key) {
        return (*this)[key];
    }

    const TValue& get_Item(const TKey& key) const {
        return this->at(key);
    }

    void set_Item(const TKey& key, const TValue& value) {
        if constexpr (std::is_pointer_v<TValue>) {
            auto iterator = this->find(key);
            if (iterator != this->end() && iterator->second != value) {
                DeleteOwnedValue(iterator->second);
            }
        }

        this->insert_or_assign(key, value);
    }

    bool ContainsKey(const TKey& key) const {
        return this->find(key) != this->end();
    }

    bool Remove(const TKey& key) {
        auto iterator = this->find(key);
        if (iterator == this->end()) {
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
        if (iterator == this->end()) {
            return false;
        }

        value = iterator->second;
        return true;
    }

    std::vector<TKey> Keys() const {
        std::vector<TKey> keys;
        keys.reserve(this->size());
        for (const auto& pair : *this) {
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
