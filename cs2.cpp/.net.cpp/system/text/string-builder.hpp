#pragma once

#include <cstddef>
#include <cstdint>
#include "../../runtime/native_string.hpp"
#if HE_CPP_USE_STD_STRING
#include <string_view>
#endif

/// <summary>
/// Provides a lightweight append-oriented string builder for transpiled managed code.
/// </summary>
class StringBuilder {
    HeCppString buffer = "";

public:
    int32_t Length;

    /// <summary>
    /// Initializes an empty builder.
    /// </summary>
    StringBuilder() : Length(0) {}

    /// <summary>
    /// Initializes a builder with a reserved capacity hint.
    /// </summary>
    /// <param name="capacity">Expected character capacity for the composed string.</param>
    explicit StringBuilder(int32_t capacity) {
        if (capacity > 0) {
            buffer.reserve(static_cast<std::size_t>(capacity));
        }
        Length = 0;
    }

    /// <summary>
    /// Initializes a builder from an existing string value.
    /// </summary>
    /// <param name="value">Initial text content for the builder.</param>
    explicit StringBuilder(const HeCppString& value) {
        buffer += value;
        Length = he_cpp_string_length(buffer);
    }

    /// <summary>Initializes the builder from a nullable native character sequence.</summary>
    explicit StringBuilder(const char* value) {
        if (value != nullptr) {
            buffer += value;
        }
        Length = he_cpp_string_length(buffer);
    }

#if HE_CPP_USE_STD_STRING
    /// <summary>Accepts native hosted storage without ambiguity between managed and view constructors.</summary>
    explicit StringBuilder(const HeCppRawString& value) : StringBuilder(HeCppString(value)) {}

    /// <summary>Appends native hosted storage through the managed-value implementation.</summary>
    StringBuilder& Append(const HeCppRawString& value) { return Append(HeCppString(value)); }

    /// <summary>Appends native hosted storage and a newline through the managed-value implementation.</summary>
    StringBuilder& AppendLine(const HeCppRawString& value) { return AppendLine(HeCppString(value)); }

    /// <summary>Preserves the hosted string-view constructor without requiring an owned temporary string.</summary>
    explicit StringBuilder(std::string_view value) {
        buffer.append(value.data(), value.size());
        Length = he_cpp_string_length(buffer);
    }

    /// <summary>Appends a hosted string view without changing its explicit length.</summary>
    StringBuilder& Append(std::string_view value) {
        buffer.append(value.data(), value.size());
        Length = he_cpp_string_length(buffer);
        return *this;
    }

    /// <summary>Appends a hosted string view and the same newline used by the managed helper.</summary>
    StringBuilder& AppendLine(std::string_view value) {
        Append(value);
        return AppendLine();
    }
#endif

    /// <summary>Accepts native integer representations that differ from int32_t on the target ABI, including integer literals.</summary>
    template<typename T, std::enable_if_t<std::is_integral_v<T> &&
        !std::is_same_v<T, char> && !std::is_same_v<T, char16_t> && !std::is_same_v<T, int32_t> && !std::is_same_v<T, uint32_t>, int> = 0>
    StringBuilder& Append(T value) {
        buffer.append(String::ToJoinString(value));
        Length = he_cpp_string_length(buffer);
        return *this;
    }

    /// <summary>
    /// Appends a single character and returns the builder for chaining.
    /// </summary>
    /// <param name="value">Character to append.</param>
    /// <returns>The current builder instance.</returns>
    StringBuilder& Append(char16_t value) {
        buffer += value; Length = he_cpp_string_length(buffer); return *this;
    }

    /// <summary>Preserves native single-byte append callers while managed characters use char16_t.</summary>
    StringBuilder& Append(char value) {
        buffer.push_back(value);
        Length = he_cpp_string_length(buffer);
        return *this;
    }

    /// <summary>
    /// Appends a string view and returns the builder for chaining.
    /// </summary>
    /// <param name="value">String content to append.</param>
    /// <returns>The current builder instance.</returns>
    StringBuilder& Append(const HeCppString& value) {
        buffer += value;
        Length = he_cpp_string_length(buffer);
        return *this;
    }

    /// <summary>Appends a nullable native character sequence and updates the managed length.</summary>
    StringBuilder& Append(const char* value) {
        if (value != nullptr) {
            buffer += value;
        }
        Length = he_cpp_string_length(buffer);
        return *this;
    }

    /// <summary>
    /// Appends an integer value and returns the builder for chaining.
    /// </summary>
    /// <param name="value">Integer value to append.</param>
    /// <returns>The current builder instance.</returns>
    StringBuilder& Append(int32_t value) {
        buffer.append(String::ToJoinString(value));
        Length = he_cpp_string_length(buffer);
        return *this;
    }

    /// <summary>
    /// Appends an unsigned integer value and returns the builder for chaining.
    /// </summary>
    /// <param name="value">Unsigned integer value to append.</param>
    /// <returns>The current builder instance.</returns>
    StringBuilder& Append(uint32_t value) {
        buffer.append(String::ToJoinString(value));
        Length = he_cpp_string_length(buffer);
        return *this;
    }

    /// <summary>
    /// Appends a newline sequence and returns the builder for chaining.
    /// </summary>
    /// <returns>The current builder instance.</returns>
    StringBuilder& AppendLine() {
        buffer.push_back('\n');
        Length = he_cpp_string_length(buffer);
        return *this;
    }

    /// <summary>
    /// Appends a string followed by a newline sequence and returns the builder for chaining.
    /// </summary>
    /// <param name="value">String content to append before the newline.</param>
    /// <returns>The current builder instance.</returns>
    StringBuilder& AppendLine(const HeCppString& value) {
        buffer += value;
        buffer.push_back('\n');
        Length = he_cpp_string_length(buffer);
        return *this;
    }

    /// <summary>Appends a nullable native character sequence followed by a newline.</summary>
    StringBuilder& AppendLine(const char* value) {
        if (value != nullptr) {
            buffer += value;
        }
        buffer.push_back('\n');
        Length = he_cpp_string_length(buffer);
        return *this;
    }

    /// <summary>
    /// Gets the number of characters currently in the builder.
    /// </summary>
    /// <returns>The accumulated content length.</returns>
    int32_t get_Length() const {
        return Length;
    }

    /// <summary>
    /// Truncates the builder to the specified length.
    /// </summary>
    void set_Length(int32_t value) {
        if (value < 0) { he_cpp_raise(ArgumentOutOfRangeException("value")); }
        if (value < Length) { buffer = String::Substring(buffer, 0, value); }
        else { while (Length < value) { buffer += static_cast<char16_t>(0); ++Length; } }
        Length = he_cpp_string_length(buffer);
    }

    /// <summary>Reads one managed UTF16 unit from the accumulated builder value.</summary>
    char16_t get_Chars(int32_t index) const { return he_cpp_string_char_at(buffer, index); }
    /// <summary>Replaces one builder UTF16 unit without changing the logical length.</summary>
    void set_Chars(int32_t index, char16_t value) { buffer = String::Substring(buffer, 0, index) + value + String::Substring(buffer, index + 1); }
    /// <summary>Supports the generated default indexer with the same UTF16 semantics.</summary>
    char16_t get_Item(int32_t index) const { return get_Chars(index); }
    /// <summary>Supports generated default-indexer assignment without byte mutation.</summary>
    void set_Item(int32_t index, char16_t value) { set_Chars(index, value); }

    /// <summary>
    /// Materializes a substring of the built string value.
    /// </summary>
    /// <param name="startIndex">Start index of the substring.</param>
    /// <param name="length">Length of the substring.</param>
    /// <returns>A substring of the accumulated string content.</returns>
    HeCppString ToString(int32_t startIndex, int32_t length) const { return String::Substring(buffer, startIndex, length); }

    /// <summary>
    /// Materializes the built string value.
    /// </summary>
    /// <returns>A copy of the accumulated string content.</returns>
    HeCppString ToString() const {
        return buffer;
    }
};
