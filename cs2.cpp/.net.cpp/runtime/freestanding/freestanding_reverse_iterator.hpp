#pragma once

namespace he_cpp_freestanding {
/// <summary>Traverses contiguous byte storage backwards without requiring the hosted standard iterator library.</summary>
template<typename TPointer> class FreestandingReverseIterator {
    /// <summary>Retains the forward iterator immediately after the byte returned by dereference.</summary>
    TPointer Current;
public:
    /// <summary>Creates a reverse position from a valid contiguous forward position.</summary>
    explicit FreestandingReverseIterator(TPointer position) : Current(position) {}
    /// <summary>Reads or mutates the preceding byte according to the pointer's const qualification.</summary>
    auto& operator*() const { return *(Current - 1); }
    /// <summary>Moves to the preceding byte without constructing a pointer before the first storage element.</summary>
    FreestandingReverseIterator& operator++() { --Current; return *this; }
    /// <summary>Compares two reverse positions from the same underlying byte range.</summary>
    bool operator==(const FreestandingReverseIterator& other) const { return Current == other.Current; }
    /// <summary>Detects unfinished traversal of a reverse byte range.</summary>
    bool operator!=(const FreestandingReverseIterator& other) const { return Current != other.Current; }
};
}
