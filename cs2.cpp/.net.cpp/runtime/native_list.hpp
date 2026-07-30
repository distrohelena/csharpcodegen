#pragma once

#include <algorithm>
#include <cstdint>
#include <string>
#include <type_traits>
#include <vector>

#include "array.hpp"
#include "native_exceptions.hpp"
#include "native_string.hpp"

template<typename T>
class List;

template<typename T>
class ReadOnlyCollection;

/// <summary>
/// Defines the non-mutating list contract used for managed read-only list and collection references.
/// </summary>
template<typename T>
class IReadOnlyList {
public:
    /// <summary>
    /// Names the stable constant iterator used by read-only range traversal.
    /// </summary>
    using ConstIterator = typename std::vector<T>::const_iterator;

    /// <summary>
    /// Releases one read-only contract through its interface pointer.
    /// </summary>
    virtual ~IReadOnlyList() = default;

    /// <summary>
    /// Returns the current number of elements exposed by the live view.
    /// </summary>
    virtual int32_t get_Count() const = 0;

    /// <summary>
    /// Returns one element without granting mutation access.
    /// </summary>
    virtual const T& get_Item(int32_t index) const = 0;

    /// <summary>
    /// Returns the beginning of the current live sequence.
    /// </summary>
    virtual ConstIterator begin() const = 0;

    /// <summary>
    /// Returns the end of the current live sequence.
    /// </summary>
    virtual ConstIterator end() const = 0;
};

template<typename T>
class NativeListEqual {
public:
    bool operator()(const T& left, const T& right) const {
        if constexpr (std::is_pointer_v<T>) {
            return left == right;
        } else if constexpr (requires(T value) { value.Equals(right); }) {
            return const_cast<T&>(left).Equals(right);
        } else {
            return left == right;
        }
    }
};

template<typename T>
class List : public std::vector<T>, public IReadOnlyList<T> {
public:
    List()
        : std::vector<T>() {
    }

    explicit List(int32_t capacity)
        : std::vector<T>() {
        if (capacity > 0) {
            this->reserve(static_cast<size_t>(capacity));
        }
    }

    List(std::initializer_list<T> values)
        : std::vector<T>(values) {
    }

    explicit List(const std::vector<T>& values)
        : std::vector<T>(values) {
    }

    explicit List(const Array<T>* values) {
        if (values == nullptr || values->Length <= 0 || values->Data == nullptr) {
            return;
        }

        this->reserve(values->Length);
        for (int32_t index = 0; index < values->Length; index++) {
            this->push_back((*values)[index]);
        }
    }

    void Add(const T& value) {
        this->push_back(value);
    }

    void Clear() {
        this->clear();
    }

    /// <summary>
    /// Allocates a distinct live wrapper that exposes this list through the non-mutating contract.
    /// </summary>
    ReadOnlyCollection<T>* AsReadOnly();

    bool Contains(const T& value) const {
        NativeListEqual<T> equal;
        return std::find_if(this->begin(), this->end(), [&](const T& candidate) { return equal(candidate, value); }) != this->end();
    }

    bool Remove(const T& value) {
        NativeListEqual<T> equal;
        typename std::vector<T>::iterator iterator = std::find_if(this->begin(), this->end(), [&](const T& candidate) { return equal(candidate, value); });
        if (iterator == this->end()) {
            return false;
        }

        this->erase(iterator);
        return true;
    }

    int32_t Count() const {
        return static_cast<int32_t>(this->size());
    }

    T& get_Item(int32_t index) {
        return (*this)[static_cast<size_t>(index)];
    }

    const T& get_Item(int32_t index) const override {
        return (*this)[static_cast<size_t>(index)];
    }

    void set_Item(int32_t index, const T& value) {
        (*this)[static_cast<size_t>(index)] = value;
    }

    int32_t get_Count() const override {
        return Count();
    }

    /// <summary>
    /// Returns a constant iterator for read-only traversal of this list.
    /// </summary>
    typename IReadOnlyList<T>::ConstIterator begin() const override {
        return std::vector<T>::begin();
    }

    /// <summary>
    /// Returns a mutable iterator for internal list operations.
    /// </summary>
    typename std::vector<T>::iterator begin() {
        return std::vector<T>::begin();
    }

    /// <summary>
    /// Returns the constant end iterator for read-only traversal of this list.
    /// </summary>
    typename IReadOnlyList<T>::ConstIterator end() const override {
        return std::vector<T>::end();
    }

    /// <summary>
    /// Returns the mutable end iterator for internal list operations.
    /// </summary>
    typename std::vector<T>::iterator end() {
        return std::vector<T>::end();
    }

    int32_t Capacity() const {
        return static_cast<int32_t>(this->capacity());
    }

    int32_t get_Capacity() const {
        return Capacity();
    }

    void SetCapacity(int32_t capacity) {
        if (capacity <= 0) {
            return;
        }

        if (capacity > Capacity()) {
            this->reserve(static_cast<size_t>(capacity));
        }
    }

    void set_Capacity(int32_t capacity) {
        SetCapacity(capacity);
    }

    void Insert(int32_t index, const T& value) {
        if (index < 0) {
            index = 0;
        }

        if (index >= Count()) {
            this->push_back(value);
            return;
        }

        this->insert(this->begin() + index, value);
    }

    void RemoveAt(int32_t index) {
        if (index < 0 || index >= Count()) {
            return;
        }

        this->erase(this->begin() + index);
    }

    Array<T>* ToArray() const {
        Array<T>* values = new Array<T>(Count());
        for (int32_t index = 0; index < values->Length; index++) {
            (*values)[index] = (*this)[index];
        }

        return values;
    }
};

/// <summary>
/// Provides a live read-only view over one managed-style list while rejecting every mutating collection operation.
/// </summary>
template<typename T>
class ReadOnlyCollection : public IReadOnlyList<T> {
    /// <summary>
    /// References the mutable source whose current contents remain visible through this wrapper.
    /// </summary>
    const List<T>* Source;

public:
    /// <summary>
    /// Creates a live read-only wrapper over one required source list.
    /// </summary>
    explicit ReadOnlyCollection(const List<T>* source)
        : Source(source) {
        if (Source == nullptr) {
            throw ArgumentNullException("source");
        }
    }

    /// <summary>
    /// Returns the source list's current element count.
    /// </summary>
    int32_t get_Count() const override {
        return Source->get_Count();
    }

    /// <summary>
    /// Returns one current source element without exposing a mutable reference.
    /// </summary>
    const T& get_Item(int32_t index) const override {
        return Source->get_Item(index);
    }

    /// <summary>
    /// Returns the beginning of the source list's current sequence.
    /// </summary>
    typename IReadOnlyList<T>::ConstIterator begin() const override {
        return Source->begin();
    }

    /// <summary>
    /// Returns the end of the source list's current sequence.
    /// </summary>
    typename IReadOnlyList<T>::ConstIterator end() const override {
        return Source->end();
    }

    /// <summary>
    /// Rejects attempts to append through the managed read-only collection surface.
    /// </summary>
    void Add(const T& value) {
        (void)value;
        throw NotSupportedException();
    }

    /// <summary>
    /// Rejects attempts to clear through the managed read-only collection surface.
    /// </summary>
    void Clear() {
        throw NotSupportedException();
    }

    /// <summary>
    /// Rejects attempts to remove a value through the managed read-only collection surface.
    /// </summary>
    bool Remove(const T& value) {
        (void)value;
        throw NotSupportedException();
    }

    /// <summary>
    /// Rejects attempts to replace an indexed value through the managed read-only list surface.
    /// </summary>
    void set_Item(int32_t index, const T& value) {
        (void)index;
        (void)value;
        throw NotSupportedException();
    }

    /// <summary>
    /// Rejects attempts to insert through the managed read-only list surface.
    /// </summary>
    void Insert(int32_t index, const T& value) {
        (void)index;
        (void)value;
        throw NotSupportedException();
    }

    /// <summary>
    /// Rejects attempts to remove an indexed value through the managed read-only list surface.
    /// </summary>
    void RemoveAt(int32_t index) {
        (void)index;
        throw NotSupportedException();
    }
};

template<typename T>
/// <summary>
/// Creates the managed live read-only wrapper for this list.
/// </summary>
ReadOnlyCollection<T>* List<T>::AsReadOnly() {
    return new ReadOnlyCollection<T>(this);
}
