#pragma once

#include <cstdint>

/// <summary>
/// Defines the indexed non-mutating collection contract shared by native arrays, lists, and read-only list wrappers.
/// </summary>
template<typename T>
class IReadOnlyList {
public:
    /// <summary>
    /// Traverses one read-only list through its stable indexed contract without depending on a concrete container iterator type.
    /// </summary>
    class ConstIterator {
        /// <summary>
        /// References the collection currently being traversed.
        /// </summary>
        const IReadOnlyList<T>* Source;

        /// <summary>
        /// Stores the current zero-based collection index.
        /// </summary>
        int32_t Index;

    public:
        /// <summary>
        /// Creates one iterator at the supplied collection index.
        /// </summary>
        ConstIterator(const IReadOnlyList<T>* source, int32_t index)
            : Source(source), Index(index) {
        }

        /// <summary>
        /// Returns the current collection element without granting mutation access.
        /// </summary>
        const T& operator*() const {
            return Source->get_Item(Index);
        }

        /// <summary>
        /// Advances this iterator to the next collection index.
        /// </summary>
        ConstIterator& operator++() {
            ++Index;
            return *this;
        }

        /// <summary>
        /// Compares two iterator positions for range traversal.
        /// </summary>
        bool operator!=(const ConstIterator& other) const {
            return Source != other.Source || Index != other.Index;
        }
    };

    /// <summary>
    /// Releases one read-only contract through its interface pointer.
    /// </summary>
    virtual ~IReadOnlyList() = default;

    /// <summary>
    /// Returns the current number of elements exposed by the collection.
    /// </summary>
    virtual int32_t get_Count() const = 0;

    /// <summary>
    /// Returns one element without granting mutation access.
    /// </summary>
    virtual const T& get_Item(int32_t index) const = 0;

    /// <summary>
    /// Returns the first indexed position in this collection.
    /// </summary>
    ConstIterator begin() const {
        return ConstIterator(this, 0);
    }

    /// <summary>
    /// Returns the sentinel position immediately after this collection's final element.
    /// </summary>
    ConstIterator end() const {
        return ConstIterator(this, get_Count());
    }
};
