#pragma once

#include <cstddef>
#include <type_traits>
#include "native_algorithm.hpp"

/// <summary>Stores managed string nullness independently of the consumer-selected byte string; native interoperation explicitly uses raw() or c_str().</summary>
template <typename TRawString>
class HeCppManagedString {
    /// <summary>Owns the bytes through the selected runtime string provider.</summary>
    TRawString Storage;
    /// <summary>Distinguishes a managed null reference from a present string containing zero bytes.</summary>
    bool IsNull = true;

    /// <summary>Unwraps managed string arguments for storage operations while preserving ordinary native arguments.</summary>
    template <typename TValue>
    static decltype(auto) RawArgument(TValue&& value) {
        if constexpr (std::is_same_v<std::decay_t<TValue>, HeCppManagedString>) {
            return value.raw();
        } else if constexpr (std::is_same_v<std::decay_t<TValue>, std::nullptr_t>) {
            return "";
        } else if constexpr (std::is_array_v<std::remove_reference_t<TValue>>) {
            return he_cpp_alg::Forward<TValue>(value);
        } else if constexpr (std::is_same_v<std::decay_t<TValue>, const char*> || std::is_same_v<std::decay_t<TValue>, char*>) {
            return value == nullptr ? "" : value;
        } else {
            return he_cpp_alg::Forward<TValue>(value);
        }
    }

public:
    /// <summary>Exposes the provider's character type to native container algorithms.</summary>
    using value_type = typename TRawString::value_type;
    /// <summary>Uses the provider's native size and index representation.</summary>
    using size_type = typename TRawString::size_type;
    /// <summary>Exposes mutable byte iteration without exposing mutable raw storage.</summary>
    using iterator = typename TRawString::iterator;
    /// <summary>Exposes immutable byte iteration to native algorithms.</summary>
    using const_iterator = typename TRawString::const_iterator;
    /// <summary>Exposes mutable reverse byte iteration.</summary>
    using reverse_iterator = typename TRawString::reverse_iterator;
    /// <summary>Exposes immutable reverse byte iteration.</summary>
    using const_reverse_iterator = typename TRawString::const_reverse_iterator;
    /// <summary>Retains the provider's sentinel for unsuccessful searches and open-ended ranges.</summary>
    inline static constexpr size_type npos = TRawString::npos;

    /// <summary>Initializes a managed null reference with empty native backing storage.</summary>
    HeCppManagedString() = default;
    /// <summary>Initializes an explicitly null managed reference.</summary>
    HeCppManagedString(std::nullptr_t) {}
    /// <summary>Copies both the byte contents and managed null state.</summary>
    HeCppManagedString(const HeCppManagedString&) = default;
    /// <summary>Moves byte ownership while preserving the transferred managed null state.</summary>
    HeCppManagedString(HeCppManagedString&&) = default;
    /// <summary>Constructs a present managed string from native byte storage, including native empty strings.</summary>
    HeCppManagedString(const TRawString& value) : Storage(value), IsNull(false) {}
    /// <summary>Transfers native byte storage into a present managed string.</summary>
    HeCppManagedString(TRawString&& value) : Storage(he_cpp_alg::Move(value)), IsNull(false) {}
    /// <summary>Copies a zero-terminated native string, interpreting a null pointer as managed null.</summary>
    HeCppManagedString(const char* value) : Storage(value == nullptr ? "" : value), IsNull(value == nullptr) {}
    /// <summary>Copies an explicit native byte range; a zero length still produces a present empty string.</summary>
    HeCppManagedString(const char* value, size_type count) : Storage(value == nullptr ? "" : value, count), IsNull(false) {}
    /// <summary>Creates a present string containing count copies of one character, including count zero.</summary>
    HeCppManagedString(size_type count, char value) : Storage(count, value), IsNull(false) {}
    /// <summary>Copies a native iterator range into a present managed string.</summary>
    template <typename TIterator, typename = std::enable_if_t<!std::is_integral_v<TIterator>>>
    HeCppManagedString(TIterator first, TIterator last) : Storage(first, last), IsNull(false) {}
    /// <summary>Copies a substring into a present managed string.</summary>
    HeCppManagedString(const HeCppManagedString& value, size_type offset, size_type count = npos) : Storage(value.Storage, offset, count), IsNull(false) {}
    /// <summary>Copies bytes and null state from another managed string.</summary>
    HeCppManagedString& operator=(const HeCppManagedString&) = default;
    /// <summary>Moves byte ownership and null state from another managed string.</summary>
    HeCppManagedString& operator=(HeCppManagedString&&) = default;
    /// <summary>Replaces this value with managed null and clears its native bytes.</summary>
    HeCppManagedString& operator=(std::nullptr_t) { Storage.clear(); IsNull = true; return *this; }
    /// <summary>Assigns native text, preserving null pointer semantics.</summary>
    HeCppManagedString& operator=(const char* value) { return *this = HeCppManagedString(value); }
    /// <summary>Copies native storage as a present managed string.</summary>
    HeCppManagedString& operator=(const TRawString& value) { Storage = value; IsNull = false; return *this; }
    /// <summary>Moves native storage as a present managed string.</summary>
    HeCppManagedString& operator=(TRawString&& value) { Storage = he_cpp_alg::Move(value); IsNull = false; return *this; }
    /// <summary>Assigns one character as a present managed string.</summary>
    HeCppManagedString& operator=(char value) { Storage.assign(1, value); IsNull = false; return *this; }

    /// <summary>Reports whether this value represents managed null, independently of its length.</summary>
    bool is_null() const { return IsNull; }
    /// <summary>Provides an explicit immutable bridge to the provider's native string type.</summary>
    const TRawString& raw() const { return Storage; }
    /// <summary>Returns the native byte count; null-aware helpers may inspect the empty backing of null.</summary>
    size_type size() const { return Storage.size(); }
    /// <summary>Returns the native byte count with standard string naming.</summary>
    size_type length() const { return Storage.length(); }
    /// <summary>Returns the allocated byte capacity of the native backing.</summary>
    size_type capacity() const { return Storage.capacity(); }
    /// <summary>Reports whether the native backing contains no bytes; use is_null() to distinguish null.</summary>
    bool empty() const { return Storage.empty(); }
    /// <summary>Provides a zero-terminated native bridge, including an empty bridge for managed null.</summary>
    const char* c_str() const { return Storage.c_str(); }
    /// <summary>Provides immutable contiguous native bytes.</summary>
    const char* data() const { return Storage.data(); }
    /// <summary>Provides mutable contiguous bytes for an already allocated native buffer.</summary>
    auto data() { return Storage.data(); }
    /// <summary>Reads one native byte without range validation.</summary>
    const char& operator[](size_type index) const { return Storage[index]; }
    /// <summary>Allows mutation of one existing native byte without range validation.</summary>
    char& operator[](size_type index) { return Storage[index]; }
    /// <summary>Reads one native byte with the provider's range validation.</summary>
    const char& at(size_type index) const { return Storage.at(index); }
    /// <summary>Mutates one native byte with the provider's range validation.</summary>
    char& at(size_type index) { return Storage.at(index); }
    /// <summary>Reads the first byte of nonempty storage.</summary>
    const char& front() const { return Storage.front(); }
    /// <summary>Mutates the first byte of nonempty storage.</summary>
    char& front() { return Storage.front(); }
    /// <summary>Reads the last byte of nonempty storage.</summary>
    const char& back() const { return Storage.back(); }
    /// <summary>Mutates the last byte of nonempty storage.</summary>
    char& back() { return Storage.back(); }
    /// <summary>Begins mutable iteration over native bytes.</summary>
    iterator begin() { return Storage.begin(); }
    /// <summary>Ends mutable iteration over native bytes.</summary>
    iterator end() { return Storage.end(); }
    /// <summary>Begins immutable iteration over native bytes.</summary>
    const_iterator begin() const { return Storage.begin(); }
    /// <summary>Ends immutable iteration over native bytes.</summary>
    const_iterator end() const { return Storage.end(); }
    /// <summary>Begins explicit immutable iteration over native bytes.</summary>
    const_iterator cbegin() const { return Storage.cbegin(); }
    /// <summary>Ends explicit immutable iteration over native bytes.</summary>
    const_iterator cend() const { return Storage.cend(); }
    /// <summary>Begins mutable reverse iteration over native bytes.</summary>
    reverse_iterator rbegin() { return Storage.rbegin(); }
    /// <summary>Ends mutable reverse iteration over native bytes.</summary>
    reverse_iterator rend() { return Storage.rend(); }
    /// <summary>Begins immutable reverse iteration over native bytes.</summary>
    const_reverse_iterator rbegin() const { return Storage.rbegin(); }
    /// <summary>Ends immutable reverse iteration over native bytes.</summary>
    const_reverse_iterator rend() const { return Storage.rend(); }
    /// <summary>Reserves native capacity without changing the managed value.</summary>
    void reserve(size_type count) { Storage.reserve(count); }
    /// <summary>Changes byte length and materializes a present string even when the new length is zero.</summary>
    void resize(size_type count, char value = char()) { Storage.resize(count, value); IsNull = false; }
    /// <summary>Materializes a present empty managed string.</summary>
    void clear() { Storage.clear(); IsNull = false; }
    /// <summary>Assigns managed null without interpreting it as a present empty string.</summary>
    HeCppManagedString& assign(std::nullptr_t) { return *this = nullptr; }
    /// <summary>Assigns both contents and managed null state from a managed string.</summary>
    HeCppManagedString& assign(const HeCppManagedString& value) { return *this = value; }
    /// <summary>Copies null state from a mutable managed string without selecting the native range assignment overload.</summary>
    HeCppManagedString& assign(HeCppManagedString& value) { return *this = value; }
    /// <summary>Transfers null state from a temporary managed string without materializing null as empty.</summary>
    HeCppManagedString& assign(HeCppManagedString&& value) { return *this = he_cpp_alg::Move(value); }
    /// <summary>Assigns native text and preserves null pointer semantics.</summary>
    HeCppManagedString& assign(const char* value) { return *this = value; }
    /// <summary>Assigns one native or managed value, preserving null pointer and managed null semantics.</summary>
    template <typename TValue>
    HeCppManagedString& assign(TValue&& value) {
        if constexpr (std::is_same_v<std::decay_t<TValue>, HeCppManagedString> ||
            std::is_same_v<std::decay_t<TValue>, std::nullptr_t> ||
            std::is_same_v<std::decay_t<TValue>, const char*> || std::is_same_v<std::decay_t<TValue>, char*>) {
            return *this = he_cpp_alg::Forward<TValue>(value);
        } else {
            Storage.assign(he_cpp_alg::Forward<TValue>(value));
            IsNull = false;
            return *this;
        }
    }
    /// <summary>Assigns a native range or fill and materializes a present managed string.</summary>
    template <typename TFirst, typename TSecond, typename... TArguments>
    HeCppManagedString& assign(TFirst&& first, TSecond&& second, TArguments&&... arguments) {
        Storage.assign(RawArgument(he_cpp_alg::Forward<TFirst>(first)), RawArgument(he_cpp_alg::Forward<TSecond>(second)), RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...);
        IsNull = false;
        return *this;
    }
    /// <summary>Appends native or managed bytes, treating managed null as empty and materializing the result.</summary>
    template <typename... TArguments>
    HeCppManagedString& append(TArguments&&... arguments) { Storage.append(RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...); IsNull = false; return *this; }
    /// <summary>Appends managed text, interpreting null as empty.</summary>
    HeCppManagedString& operator+=(const HeCppManagedString& value) { return append(value); }
    /// <summary>Appends zero-terminated native text, interpreting a null pointer as empty.</summary>
    HeCppManagedString& operator+=(const char* value) { return append(value == nullptr ? "" : value); }
    /// <summary>Appends one byte and materializes the result.</summary>
    HeCppManagedString& operator+=(char value) { push_back(value); return *this; }
    /// <summary>Concatenates managed null as empty while materializing the result.</summary>
    HeCppManagedString& operator+=(std::nullptr_t) { IsNull = false; return *this; }
    /// <summary>Appends one byte and marks the result as present.</summary>
    void push_back(char value) { Storage.push_back(value); IsNull = false; }
    /// <summary>Removes the final byte of nonempty native storage.</summary>
    void pop_back() { Storage.pop_back(); IsNull = false; }
    /// <summary>Copies a native byte range into a present managed string.</summary>
    HeCppManagedString substr(size_type offset = 0, size_type count = npos) const { return Storage.substr(offset, count); }
    /// <summary>Finds a forward occurrence using the provider's native search overloads.</summary>
    template <typename... TArguments>
    size_type find(TArguments&&... arguments) const { return Storage.find(RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...); }
    /// <summary>Finds a backward occurrence using the provider's native search overloads.</summary>
    template <typename... TArguments>
    size_type rfind(TArguments&&... arguments) const { return Storage.rfind(RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...); }
    /// <summary>Finds the first byte present in the supplied native or managed character set.</summary>
    template <typename... TArguments>
    size_type find_first_of(TArguments&&... arguments) const { return Storage.find_first_of(RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...); }
    /// <summary>Finds the last byte present in the supplied native or managed character set.</summary>
    template <typename... TArguments>
    size_type find_last_of(TArguments&&... arguments) const { return Storage.find_last_of(RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...); }
    /// <summary>Finds the first byte absent from the supplied native or managed character set.</summary>
    template <typename... TArguments>
    size_type find_first_not_of(TArguments&&... arguments) const { return Storage.find_first_not_of(RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...); }
    /// <summary>Finds the last byte absent from the supplied native or managed character set.</summary>
    template <typename... TArguments>
    size_type find_last_not_of(TArguments&&... arguments) const { return Storage.find_last_not_of(RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...); }
    /// <summary>Erases an indexed byte range and retains a present managed result.</summary>
    HeCppManagedString& erase(size_type offset = 0, size_type count = npos) { Storage.erase(offset, count); IsNull = false; return *this; }
    /// <summary>Erases one byte through a native iterator and returns the following iterator.</summary>
    iterator erase(const_iterator position) { IsNull = false; return Storage.erase(position); }
    /// <summary>Erases a native iterator range and returns the following iterator.</summary>
    iterator erase(const_iterator first, const_iterator last) { IsNull = false; return Storage.erase(first, last); }
    /// <summary>Inserts native or managed bytes at an indexed position.</summary>
    template <typename... TArguments>
    HeCppManagedString& insert(size_type offset, TArguments&&... arguments) { Storage.insert(offset, RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...); IsNull = false; return *this; }
    /// <summary>Inserts one byte at a native iterator position.</summary>
    iterator insert(const_iterator position, char value) { IsNull = false; return Storage.insert(position, value); }
    /// <summary>Inserts repeated bytes at a native iterator position.</summary>
    iterator insert(const_iterator position, size_type count, char value) { IsNull = false; return Storage.insert(position, count, value); }
    /// <summary>Inserts an iterator range at a native iterator position.</summary>
    template <typename TIterator>
    iterator insert(const_iterator position, TIterator first, TIterator last) { IsNull = false; return Storage.insert(position, first, last); }
    /// <summary>Replaces a native byte range with supplied managed or native text.</summary>
    template <typename... TArguments>
    HeCppManagedString& replace(TArguments&&... arguments) { Storage.replace(RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...); IsNull = false; return *this; }
    /// <summary>Exchanges both native contents and the independent managed null flags.</summary>
    void swap(HeCppManagedString& value) { Storage.swap(value.Storage); he_cpp_alg::Swap(IsNull, value.IsNull); }
    /// <summary>Compares managed values ordinally with null ordered before every present value.</summary>
    int compare(const HeCppManagedString& value) const { return IsNull != value.IsNull ? (IsNull ? -1 : 1) : Storage.compare(value.Storage); }
    /// <summary>Compares a native string while honoring its null pointer as managed null.</summary>
    int compare(const char* value) const { return compare(HeCppManagedString(value)); }
    /// <summary>Compares indexed native byte ranges using the provider's comparison overloads.</summary>
    template <typename... TArguments>
    int compare(size_type offset, TArguments&&... arguments) const { return Storage.compare(offset, RawArgument(he_cpp_alg::Forward<TArguments>(arguments))...); }
    /// <summary>Tests managed equality, preserving the difference between null and a present empty string.</summary>
    friend bool operator==(const HeCppManagedString& left, const HeCppManagedString& right) { return left.IsNull == right.IsNull && left.Storage == right.Storage; }
    /// <summary>Tests managed inequality using both null state and bytes.</summary>
    friend bool operator!=(const HeCppManagedString& left, const HeCppManagedString& right) { return !(left == right); }
    /// <summary>Orders managed strings ordinally with null first.</summary>
    friend bool operator<(const HeCppManagedString& left, const HeCppManagedString& right) { return left.compare(right) < 0; }
    /// <summary>Orders managed strings ordinally with null first.</summary>
    friend bool operator>(const HeCppManagedString& left, const HeCppManagedString& right) { return left.compare(right) > 0; }
    /// <summary>Tests inclusive ascending managed string order.</summary>
    friend bool operator<=(const HeCppManagedString& left, const HeCppManagedString& right) { return left.compare(right) <= 0; }
    /// <summary>Tests inclusive descending managed string order.</summary>
    friend bool operator>=(const HeCppManagedString& left, const HeCppManagedString& right) { return left.compare(right) >= 0; }
    /// <summary>Concatenates two managed values, treating null operands as empty and always returning a present string.</summary>
    friend HeCppManagedString operator+(const HeCppManagedString& left, const HeCppManagedString& right) { HeCppManagedString result(left.Storage); result.append(right); return result; }
    /// <summary>Appends one native byte to managed text and returns a present string.</summary>
    friend HeCppManagedString operator+(const HeCppManagedString& left, char right) { HeCppManagedString result(left.Storage); result += right; return result; }
    /// <summary>Prepends one native byte to managed text and returns a present string.</summary>
    friend HeCppManagedString operator+(char left, const HeCppManagedString& right) { HeCppManagedString result(1, left); result += right; return result; }
};
