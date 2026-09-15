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
    HeCppString buffer;

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
        buffer.append(value);
        Length = static_cast<int32_t>(buffer.size());
    }

    /// <summary>Initializes the builder from a nullable native character sequence.</summary>
    explicit StringBuilder(const char* value) {
        if (value != nullptr) {
            buffer.append(value);
        }
        Length = static_cast<int32_t>(buffer.size());
    }

#if HE_CPP_USE_STD_STRING
    /// <summary>Preserves the hosted string-view constructor without requiring an owned temporary string.</summary>
    explicit StringBuilder(std::string_view value) {
        buffer.append(value.data(), value.size());
        Length = static_cast<int32_t>(buffer.size());
    }

    /// <summary>Appends a hosted string view without changing its explicit length.</summary>
    StringBuilder& Append(std::string_view value) {
        buffer.append(value.data(), value.size());
        Length = static_cast<int32_t>(buffer.size());
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
        !std::is_same_v<T, char> && !std::is_same_v<T, int32_t> && !std::is_same_v<T, uint32_t>, int> = 0>
    StringBuilder& Append(T value) {
        buffer.append(String::ToJoinString(value));
        Length = static_cast<int32_t>(buffer.size());
        return *this;
    }

    /// <summary>
    /// Appends a single character and returns the builder for chaining.
    /// </summary>
    /// <param name="value">Character to append.</param>
    /// <returns>The current builder instance.</returns>
    StringBuilder& Append(char value) {
        buffer.push_back(value);
        Length = static_cast<int32_t>(buffer.size());
        return *this;
    }

    /// <summary>
    /// Appends a string view and returns the builder for chaining.
    /// </summary>
    /// <param name="value">String content to append.</param>
    /// <returns>The current builder instance.</returns>
    StringBuilder& Append(const HeCppString& value) {
        buffer.append(value);
        Length = static_cast<int>(buffer.size());
        return *this;
    }

    /// <summary>Appends a nullable native character sequence and updates the managed length.</summary>
    StringBuilder& Append(const char* value) {
        if (value != nullptr) {
            buffer.append(value);
        }
        Length = static_cast<int32_t>(buffer.size());
        return *this;
    }

    /// <summary>
    /// Appends an integer value and returns the builder for chaining.
    /// </summary>
    /// <param name="value">Integer value to append.</param>
    /// <returns>The current builder instance.</returns>
    StringBuilder& Append(int32_t value) {
        buffer.append(String::ToJoinString(value));
        Length = static_cast<int32_t>(buffer.size());
        return *this;
    }

    /// <summary>
    /// Appends an unsigned integer value and returns the builder for chaining.
    /// </summary>
    /// <param name="value">Unsigned integer value to append.</param>
    /// <returns>The current builder instance.</returns>
    StringBuilder& Append(uint32_t value) {
        buffer.append(String::ToJoinString(value));
        Length = static_cast<int32_t>(buffer.size());
        return *this;
    }

    /// <summary>
    /// Appends a newline sequence and returns the builder for chaining.
    /// </summary>
    /// <returns>The current builder instance.</returns>
    StringBuilder& AppendLine() {
        buffer.push_back('\n');
        Length = static_cast<int32_t>(buffer.size());
        return *this;
    }

    /// <summary>
    /// Appends a string followed by a newline sequence and returns the builder for chaining.
    /// </summary>
    /// <param name="value">String content to append before the newline.</param>
    /// <returns>The current builder instance.</returns>
    StringBuilder& AppendLine(const HeCppString& value) {
        buffer.append(value);
        buffer.push_back('\n');
        Length = static_cast<int32_t>(buffer.size());
        return *this;
    }

    /// <summary>Appends a nullable native character sequence followed by a newline.</summary>
    StringBuilder& AppendLine(const char* value) {
        if (value != nullptr) {
            buffer.append(value);
        }
        buffer.push_back('\n');
        Length = static_cast<int32_t>(buffer.size());
        return *this;
    }

    /// <summary>
    /// Truncates the builder to the specified length.
    /// </summary>
    void set_Length(int32_t value) {
        if (value < 0) {
            value = 0;
        }

        std::size_t size = static_cast<std::size_t>(value);
        if (size < buffer.size()) {
            buffer.resize(size);
        }
        Length = static_cast<int32_t>(buffer.size());
    }

    /// <summary>
    /// Materializes a substring of the built string value.
    /// </summary>
    /// <param name="startIndex">Start index of the substring.</param>
    /// <param name="length">Length of the substring.</param>
    /// <returns>A substring of the accumulated string content.</returns>
    HeCppString ToString(int32_t startIndex, int32_t length) const {
        if (startIndex < 0) {
            startIndex = 0;
        }
        if (length < 0) {
            length = 0;
        }

        std::size_t start = static_cast<std::size_t>(startIndex);
        if (start >= buffer.size()) {
            return HeCppString();
        }

        std::size_t count = static_cast<std::size_t>(length);
        if (start + count > buffer.size()) {
            count = buffer.size() - start;
        }

        return buffer.substr(start, count);
    }

    /// <summary>
    /// Materializes the built string value.
    /// </summary>
    /// <returns>A copy of the accumulated string content.</returns>
    HeCppString ToString() const {
        return buffer;
    }
};
