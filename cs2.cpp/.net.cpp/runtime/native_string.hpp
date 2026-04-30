#pragma once

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <stdexcept>
#include <string>

/// <summary>
/// Defines the comparison modes required by transpiled managed string helpers.
/// </summary>
enum class StringComparison {
    Ordinal,
    OrdinalIgnoreCase
};

/// <summary>
/// Provides lightweight managed-style static string helpers required by transpiled code paths.
/// </summary>
class String {
public:
    inline static const std::string Empty = std::string();

    /// <summary>
    /// Determines whether the supplied string is empty.
    /// </summary>
    /// <param name="value">String value to inspect.</param>
    /// <returns>True when the string contains no characters; otherwise false.</returns>
    static bool IsNullOrEmpty(const std::string& value) {
        return value.empty();
    }

    /// <summary>
    /// Determines whether the supplied string is null, empty, or consists only of whitespace characters.
    /// </summary>
    /// <param name="value">String value to inspect.</param>
    /// <returns>True when the string is empty or whitespace-only; otherwise false.</returns>
    static bool IsNullOrWhiteSpace(const std::string& value) {
        if (value.empty()) {
            return true;
        }

        for (unsigned char character : value) {
            if (!std::isspace(character)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two strings using the requested comparison mode.
    /// </summary>
    /// <param name="left">Left-hand string.</param>
    /// <param name="right">Right-hand string.</param>
    /// <param name="comparison">Comparison mode.</param>
    /// <returns>True when both strings are equal for the selected comparison.</returns>
    static bool Equals(const std::string& left, const std::string& right, StringComparison comparison = StringComparison::Ordinal) {
        if (comparison == StringComparison::OrdinalIgnoreCase) {
            return ToLowerInvariant(left) == ToLowerInvariant(right);
        }

        return left == right;
    }

    /// <summary>
    /// Determines whether a string starts with the supplied prefix.
    /// </summary>
    /// <param name="value">String to inspect.</param>
    /// <param name="prefix">Prefix to compare.</param>
    /// <param name="comparison">Comparison mode.</param>
    /// <returns>True when the string starts with the prefix; otherwise false.</returns>
    static bool StartsWith(const std::string& value, const std::string& prefix, StringComparison comparison = StringComparison::Ordinal) {
        if (prefix.size() > value.size()) {
            return false;
        }

        return Equals(value.substr(0, prefix.size()), prefix, comparison);
    }

    /// <summary>
    /// Determines whether a string ends with the supplied suffix.
    /// </summary>
    /// <param name="value">String to inspect.</param>
    /// <param name="suffix">Suffix to compare.</param>
    /// <param name="comparison">Comparison mode.</param>
    /// <returns>True when the string ends with the suffix; otherwise false.</returns>
    static bool EndsWith(const std::string& value, const std::string& suffix, StringComparison comparison = StringComparison::Ordinal) {
        if (suffix.size() > value.size()) {
            return false;
        }

        return Equals(value.substr(value.size() - suffix.size()), suffix, comparison);
    }

    /// <summary>
    /// Trims leading and trailing ASCII whitespace characters from a string.
    /// </summary>
    /// <param name="value">String to trim.</param>
    /// <returns>Trimmed string copy.</returns>
    static std::string Trim(const std::string& value) {
        size_t start = 0;
        while (start < value.size() && std::isspace(static_cast<unsigned char>(value[start]))) {
            start++;
        }

        size_t end = value.size();
        while (end > start && std::isspace(static_cast<unsigned char>(value[end - 1]))) {
            end--;
        }

        return value.substr(start, end - start);
    }

    /// <summary>
    /// Extracts a substring from the specified start index to the end of the string.
    /// </summary>
    /// <param name="value">Source string.</param>
    /// <param name="startIndex">Zero-based start index.</param>
    /// <returns>Substring copy.</returns>
    static std::string Substring(const std::string& value, int32_t startIndex) {
        if (startIndex < 0 || static_cast<size_t>(startIndex) > value.size()) {
            throw std::out_of_range("startIndex");
        }

        return value.substr(static_cast<size_t>(startIndex));
    }

    /// <summary>
    /// Extracts a substring with the specified length.
    /// </summary>
    /// <param name="value">Source string.</param>
    /// <param name="startIndex">Zero-based start index.</param>
    /// <param name="length">Requested substring length.</param>
    /// <returns>Substring copy.</returns>
    static std::string Substring(const std::string& value, int32_t startIndex, int32_t length) {
        if (startIndex < 0 || length < 0 || static_cast<size_t>(startIndex) > value.size()) {
            throw std::out_of_range("startIndex");
        }

        size_t safeStartIndex = static_cast<size_t>(startIndex);
        if (safeStartIndex + static_cast<size_t>(length) > value.size()) {
            throw std::out_of_range("length");
        }

        return value.substr(safeStartIndex, static_cast<size_t>(length));
    }

    /// <summary>
    /// Determines whether a character is an ASCII digit.
    /// </summary>
    /// <param name="value">Character to inspect.</param>
    /// <returns>True when the character is a digit; otherwise false.</returns>
    static bool IsDigit(char value) {
        return std::isdigit(static_cast<unsigned char>(value)) != 0;
    }

    /// <summary>
    /// Converts a string to lowercase using invariant ASCII casing rules.
    /// </summary>
    /// <param name="value">String to transform.</param>
    /// <returns>Lowercase string copy.</returns>
    static std::string ToLowerInvariant(const std::string& value) {
        std::string lowered = value;
        std::transform(lowered.begin(), lowered.end(), lowered.begin(), [](unsigned char character) {
            return static_cast<char>(std::tolower(character));
        });
        return lowered;
    }

    /// <summary>
    /// Concatenates string values from a collection using the supplied separator.
    /// </summary>
    /// <typeparam name="TCollection">Iterable collection type that yields string-compatible values.</typeparam>
    /// <param name="separator">Separator inserted between adjacent values.</param>
    /// <param name="values">Collection to concatenate.</param>
    /// <returns>The concatenated string.</returns>
    template <typename TCollection>
    static std::string Join(const std::string& separator, const TCollection* values) {
        if (values == nullptr) {
            return std::string();
        }

        std::string result;
        bool isFirst = true;

        for (const auto& value : *values) {
            if (!isFirst) {
                result += separator;
            }

            result += value;
            isFirst = false;
        }

        return result;
    }
};
