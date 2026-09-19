#pragma once

#include "../native_algorithm.hpp"
#include "freestanding_hash.hpp"
#include "freestanding_hooks.hpp"

#include <stddef.h>
#include <string.h>

namespace he_cpp_freestanding {

/// <summary>
/// Heap-backed byte string with the member set the shared runtime templates use.
/// Storage is always null terminated. No small-string buffer.
/// </summary>
class FreestandingString {
public:
    using value_type = char;
    using size_type = size_t;
    using iterator = char*;
    using const_iterator = const char*;
    static constexpr size_t npos = static_cast<size_t>(-1);

    FreestandingString() : Data(EmptyBuffer()), Length(0), Capacity(0) {}
    FreestandingString(const char* text) : FreestandingString() { assign(text, text == nullptr ? 0 : strlen(text)); }
    FreestandingString(const char* text, size_t count) : FreestandingString() { assign(text, count); }
    FreestandingString(size_t count, char character) : FreestandingString() {
        reserve(count);
        memset(Data, static_cast<unsigned char>(character), count);
        Length = count;
        Data[Length] = '\0';
    }
    FreestandingString(const FreestandingString& other) : FreestandingString() { assign(other.Data, other.Length); }
    FreestandingString(FreestandingString&& other) noexcept : Data(other.Data), Length(other.Length), Capacity(other.Capacity) {
        other.Data = EmptyBuffer();
        other.Length = 0;
        other.Capacity = 0;
    }
    ~FreestandingString() { Release(); }

    FreestandingString& operator=(const FreestandingString& other) {
        if (this != &other) assign(other.Data, other.Length);
        return *this;
    }
    FreestandingString& operator=(FreestandingString&& other) noexcept {
        if (this != &other) {
            Release();
            Data = other.Data; Length = other.Length; Capacity = other.Capacity;
            other.Data = EmptyBuffer(); other.Length = 0; other.Capacity = 0;
        }
        return *this;
    }
    FreestandingString& operator=(const char* text) { return assign(text); }

    size_t size() const { return Length; }
    size_t length() const { return Length; }
    size_t capacity() const { return Capacity; }
    bool empty() const { return Length == 0; }
    const char* c_str() const { return Data; }
    const char* data() const { return Data; }
    char* data() { return Data; }
    char& operator[](size_t index) { return Data[index]; }
    const char& operator[](size_t index) const { return Data[index]; }
    char& at(size_t index) { if (index >= Length) he_cpp_custom::Fail("String index out of range"); return Data[index]; }
    const char& at(size_t index) const { if (index >= Length) he_cpp_custom::Fail("String index out of range"); return Data[index]; }
    char& front() { return Data[0]; }
    const char& front() const { return Data[0]; }
    char& back() { return Data[Length - 1]; }
    const char& back() const { return Data[Length - 1]; }
    iterator begin() { return Data; }
    iterator end() { return Data + Length; }
    const_iterator begin() const { return Data; }
    const_iterator end() const { return Data + Length; }

    void reserve(size_t newCapacity) {
        if (newCapacity <= Capacity) return;
        if (newCapacity == npos) he_cpp_custom::Fail("Container allocation size overflow");
        char* buffer = static_cast<char*>(he_cpp_custom::Allocate(newCapacity + 1));
        memcpy(buffer, Data, Length + 1);
        if (Capacity != 0) he_cpp_custom::Free(Data);
        Data = buffer;
        Capacity = newCapacity;
    }
    void resize(size_t newLength) { resize(newLength, '\0'); }
    void resize(size_t newLength, char fill) {
        if (newLength > Length) {
            Grow(newLength);
            memset(Data + Length, static_cast<unsigned char>(fill), newLength - Length);
        }
        Length = newLength;
        Data[Length] = '\0';
    }
    void clear() { Length = 0; Data[0] = '\0'; }

    FreestandingString& assign(const char* text) { return assign(text, text == nullptr ? 0 : strlen(text)); }
    FreestandingString& assign(const char* text, size_t count) {
        Grow(count);
        if (count != 0) memmove(Data, text, count);
        Length = count;
        Data[Length] = '\0';
        return *this;
    }
    FreestandingString& append(const char* text) { return append(text, text == nullptr ? 0 : strlen(text)); }
    FreestandingString& append(const char* text, size_t count) {
        if (text == nullptr || count == 0) return *this;
        // text may point inside our own storage (s += s, s.append(s)); Grow can free that storage to
        // reallocate, so take an independent copy first whenever the source aliases the live range.
        if (text >= Data && text < Data + Length) {
            FreestandingString source(text, count);
            Grow(Length + count);
            memcpy(Data + Length, source.Data, count);
        } else {
            Grow(Length + count);
            memcpy(Data + Length, text, count);
        }
        Length += count;
        Data[Length] = '\0';
        return *this;
    }
    FreestandingString& append(const FreestandingString& other) { return append(other.Data, other.Length); }
    FreestandingString& operator+=(const char* text) { return append(text); }
    FreestandingString& operator+=(const FreestandingString& other) { return append(other); }
    FreestandingString& operator+=(char character) { push_back(character); return *this; }
    void push_back(char character) { Grow(Length + 1); Data[Length++] = character; Data[Length] = '\0'; }
    void pop_back() { if (Length != 0) { --Length; Data[Length] = '\0'; } }

    FreestandingString substr(size_t position, size_t count = npos) const {
        if (position > Length) he_cpp_custom::Fail("String substr position out of range");
        size_t available = Length - position;
        return FreestandingString(Data + position, count < available ? count : available);
    }
    size_t find(char character, size_t position = 0) const {
        for (size_t index = position; index < Length; ++index) if (Data[index] == character) return index;
        return npos;
    }
    size_t find(const char* text, size_t position = 0) const {
        if (position > Length) return npos;
        size_t needle = text == nullptr ? 0 : strlen(text);
        if (needle == 0) return position;
        if (needle > Length - position) return npos;
        for (size_t index = position; index + needle <= Length; ++index) {
            if (memcmp(Data + index, text, needle) == 0) return index;
        }
        return npos;
    }
    size_t find(const FreestandingString& text, size_t position = 0) const { return find(text.Data, position); }
    // strchr matches the terminating NUL of characters, so an embedded NUL in Data would otherwise
    // report a hit against every set; skip those explicitly, and treat a null set as matching nothing
    // rather than handing strchr a null pointer.
    size_t find_first_of(const char* characters, size_t position = 0) const {
        if (characters == nullptr) return npos;
        for (size_t index = position; index < Length; ++index) {
            if (Data[index] != '\0' && strchr(characters, Data[index]) != nullptr) return index;
        }
        return npos;
    }
    /// <summary>Returns the last index at or before position holding any character of the set, or npos.</summary>
    size_t find_last_of(const char* characters, size_t position = npos) const {
        if (Length == 0 || characters == nullptr) return npos;
        size_t index = position < Length - 1 ? position : Length - 1;
        for (;; --index) {
            if (Data[index] != '\0' && strchr(characters, Data[index]) != nullptr) return index;
            if (index == 0) break;
        }
        return npos;
    }
    /// <summary>Returns the last index at or before position holding character, or npos.</summary>
    size_t find_last_of(char character, size_t position = npos) const {
        if (Length == 0) return npos;
        size_t index = position < Length - 1 ? position : Length - 1;
        for (;; --index) {
            if (Data[index] == character) return index;
            if (index == 0) break;
        }
        return npos;
    }
    FreestandingString& erase(size_t position, size_t count = npos) {
        if (position > Length) he_cpp_custom::Fail("String erase position out of range");
        size_t available = Length - position;
        size_t removed = count < available ? count : available;
        memmove(Data + position, Data + position + removed, available - removed + 1);
        Length -= removed;
        return *this;
    }
    FreestandingString& insert(size_t position, const char* text) {
        if (position > Length) he_cpp_custom::Fail("String insert position out of range");
        if (text == nullptr) return *this;
        size_t count = strlen(text);
        if (count == 0) return *this;
        // text may point inside our own storage (s.insert(0, s.c_str())); take an independent copy
        // before Grow/the tail shift can relocate or overwrite the bytes it points at.
        if (text >= Data && text < Data + Length) {
            FreestandingString source(text, count);
            Grow(Length + count);
            memmove(Data + position + count, Data + position, Length - position + 1);
            memcpy(Data + position, source.Data, count);
        } else {
            Grow(Length + count);
            memmove(Data + position + count, Data + position, Length - position + 1);
            memcpy(Data + position, text, count);
        }
        Length += count;
        return *this;
    }
    // text may point inside our own storage (r.replace(pos, count, r.c_str() + n)): erase() runs a
    // memmove before insert() ever reads text, so insert()'s own alias guard checks text against
    // [Data, Data + Length) *after* that memmove has already shifted or overwritten whatever text
    // pointed at -- too late. Detect the alias here, before erase() touches anything, and take an
    // independent copy of the source text up front.
    FreestandingString& replace(size_t position, size_t count, const char* text) {
        if (text != nullptr && text >= Data && text < Data + Length) {
            FreestandingString source(text);
            erase(position, count);
            return insert(position, source.Data);
        }
        erase(position, count);
        return insert(position, text);
    }
    // text may be *this (r.replace(pos, count, r)): check identity explicitly before forwarding to
    // the const char* overload above, which only ever sees a raw pointer and cannot tell it apart from
    // an unrelated string that merely happens to share the same address.
    FreestandingString& replace(size_t position, size_t count, const FreestandingString& text) {
        if (&text == this) return replace(position, count, Data);
        return replace(position, count, text.Data);
    }
    void swap(FreestandingString& other) noexcept {
        he_cpp_alg::Swap(Data, other.Data);
        he_cpp_alg::Swap(Length, other.Length);
        he_cpp_alg::Swap(Capacity, other.Capacity);
    }
    int compare(const FreestandingString& other) const { return compare(other.Data, other.Length); }
    int compare(const char* text) const { return compare(text, text == nullptr ? 0 : strlen(text)); }

private:
    char* Data;
    size_t Length;
    size_t Capacity;

    static char* EmptyBuffer() {
        static char empty[1] = { '\0' };
        return empty;
    }
    void Release() {
        if (Capacity != 0) he_cpp_custom::Free(Data);
        Data = EmptyBuffer();
        Length = 0;
        Capacity = 0;
    }
    void Grow(size_t required) {
        if (required <= Capacity) return;
        size_t next = Capacity < 8 ? 8 : Capacity;
        while (next < required) {
            if (next > npos / 2) he_cpp_custom::Fail("Container capacity overflow");
            next = next * 2;
        }
        reserve(next);
    }
    int compare(const char* text, size_t count) const {
        size_t common = Length < count ? Length : count;
        int result = common == 0 ? 0 : memcmp(Data, text, common);
        if (result != 0) return result;
        if (Length == count) return 0;
        return Length < count ? -1 : 1;
    }
};

inline bool operator==(const FreestandingString& left, const FreestandingString& right) { return left.compare(right) == 0; }
inline bool operator!=(const FreestandingString& left, const FreestandingString& right) { return !(left == right); }
inline bool operator<(const FreestandingString& left, const FreestandingString& right) { return left.compare(right) < 0; }
inline bool operator==(const FreestandingString& left, const char* right) { return left.compare(right) == 0; }
inline bool operator!=(const FreestandingString& left, const char* right) { return !(left == right); }
inline bool operator==(const char* left, const FreestandingString& right) { return right.compare(left) == 0; }
inline bool operator!=(const char* left, const FreestandingString& right) { return !(right == left); }
inline FreestandingString operator+(const FreestandingString& left, const FreestandingString& right) { FreestandingString result(left); result.append(right); return result; }
inline FreestandingString operator+(const FreestandingString& left, const char* right) { FreestandingString result(left); result.append(right); return result; }
inline FreestandingString operator+(const char* left, const FreestandingString& right) { FreestandingString result(left); result.append(right); return result; }
inline FreestandingString operator+(const FreestandingString& left, char right) { FreestandingString result(left); result.push_back(right); return result; }

/// <summary>Hashes the string's characters so equal strings hash equally.</summary>
template <>
struct FreestandingHash<FreestandingString> {
    size_t operator()(const FreestandingString& value) const { return HashBytes(value.data(), value.size()); }
};

}
