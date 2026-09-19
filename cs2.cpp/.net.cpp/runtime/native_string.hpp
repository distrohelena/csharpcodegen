#pragma once

#include <cstddef>
#include <cstdint>
#include <type_traits>

#include "native_algorithm.hpp"
#include "native_exceptions.hpp"
#include "native_runtime.hpp"
#include "array.hpp"

#if HE_CPP_USE_RTTI
#include <typeinfo>
#endif

#if HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING
#include <stdexcept>
#endif

/// <summary>
/// Defines the comparison modes required by transpiled managed string helpers.
/// </summary>
enum class StringComparison {
    Ordinal,
    OrdinalIgnoreCase
};

enum class StringSplitOptions {
    None,
    RemoveEmptyEntries
};

template <typename TValue, typename = void>
struct he_cpp_has_to_string_method : std::false_type {
};

template <typename TValue>
struct he_cpp_has_to_string_method<TValue, std::void_t<decltype(std::declval<TValue*>()->ToString())>> : std::true_type {
};

/// <summary>
/// Provides lightweight managed-style static string helpers required by transpiled code paths.
/// </summary>
class String {
    /// <summary>
    /// Determines whether one byte is ASCII whitespace without depending on locale headers.
    /// </summary>
    static bool IsAsciiWhitespace(unsigned char value) {
        return value == static_cast<unsigned char>(' ') ||
            value == static_cast<unsigned char>('\t') ||
            value == static_cast<unsigned char>('\r') ||
            value == static_cast<unsigned char>('\n') ||
            value == static_cast<unsigned char>('\f') ||
            value == static_cast<unsigned char>('\v');
    }

    /// <summary>
    /// Determines whether one byte is an ASCII decimal digit.
    /// </summary>
    static bool IsAsciiDigit(unsigned char value) {
        return value >= static_cast<unsigned char>('0') && value <= static_cast<unsigned char>('9');
    }

    /// <summary>
    /// Converts one ASCII uppercase letter to lowercase while preserving every other byte.
    /// </summary>
    static char ToLowerAscii(unsigned char value) {
        if (value >= static_cast<unsigned char>('A') && value <= static_cast<unsigned char>('Z')) {
            return static_cast<char>(value + static_cast<unsigned char>('a' - 'A'));
        }

        return static_cast<char>(value);
    }

    /// <summary>
    /// Converts one ASCII lowercase letter to uppercase while preserving every other byte.
    /// </summary>
    static char ToUpperAscii(unsigned char value) {
        if (value >= static_cast<unsigned char>('a') && value <= static_cast<unsigned char>('z')) {
            return static_cast<char>(value - static_cast<unsigned char>('a' - 'A'));
        }

        return static_cast<char>(value);
    }

    /// <summary>
    /// Detects a floating-point NaN without depending on the hosted math library.
    /// </summary>
    template <typename TValue>
    static bool IsNaN(TValue value) {
        return value != value;
    }

    /// <summary>
    /// Detects an IEEE infinity without depending on the hosted math library.
    /// </summary>
    template <typename TValue>
    static bool IsInfinite(TValue value) {
        if (value == static_cast<TValue>(0)) {
            return false;
        }

        TValue doubled = static_cast<TValue>(value + value);
        return doubled == value;
    }

    /// <summary>
    /// Reports an invalid string range while preserving the hosted standard exception type when that capability is enabled.
    /// </summary>
    template <typename TResult>
    [[noreturn]] static TResult RaiseOutOfRange(const char* parameterName) {
#if HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING
        throw std::out_of_range(parameterName);
#else
        he_cpp_raise_value<TResult>(ArgumentOutOfRangeException(parameterName));
#endif
    }

public:
    inline static const HeCppString Empty = HeCppString();

    /// <summary>
    /// Determines whether the supplied string is empty.
    /// </summary>
    /// <param name="value">String value to inspect.</param>
    /// <returns>True when the string contains no characters; otherwise false.</returns>
    static bool IsNullOrEmpty(const HeCppString& value) {
        return value.empty();
    }

    /// <summary>
    /// Determines whether the supplied string is null, empty, or consists only of whitespace characters.
    /// </summary>
    /// <param name="value">String value to inspect.</param>
    /// <returns>True when the string is empty or whitespace-only; otherwise false.</returns>
    static bool IsNullOrWhiteSpace(const HeCppString& value) {
        if (value.empty()) {
            return true;
        }

        for (unsigned char character : value) {
            if (!IsAsciiWhitespace(character)) {
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
    static bool Equals(const HeCppString& left, const HeCppString& right, StringComparison comparison = StringComparison::Ordinal) {
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
    static bool StartsWith(const HeCppString& value, const HeCppString& prefix, StringComparison comparison = StringComparison::Ordinal) {
        if (prefix.size() > value.size()) {
            return false;
        }

        return Equals(value.substr(0, prefix.size()), prefix, comparison);
    }

    /// <summary>
    /// Determines whether a string starts with the supplied character.
    /// </summary>
    /// <param name="value">String to inspect.</param>
    /// <param name="prefix">Prefix character to compare.</param>
    /// <returns>True when the string starts with the character; otherwise false.</returns>
    static bool StartsWith(const HeCppString& value, char prefix) {
        return !value.empty() && value.front() == prefix;
    }

    /// <summary>
    /// Determines whether a string ends with the supplied suffix.
    /// </summary>
    /// <param name="value">String to inspect.</param>
    /// <param name="suffix">Suffix to compare.</param>
    /// <param name="comparison">Comparison mode.</param>
    /// <returns>True when the string ends with the suffix; otherwise false.</returns>
    static bool EndsWith(const HeCppString& value, const HeCppString& suffix, StringComparison comparison = StringComparison::Ordinal) {
        if (suffix.size() > value.size()) {
            return false;
        }

        return Equals(value.substr(value.size() - suffix.size()), suffix, comparison);
    }

    /// <summary>
    /// Determines whether a string ends with the supplied character.
    /// </summary>
    /// <param name="value">String to inspect.</param>
    /// <param name="suffix">Suffix character to compare.</param>
    /// <returns>True when the string ends with the character; otherwise false.</returns>
    static bool EndsWith(const HeCppString& value, char suffix) {
        return !value.empty() && value.back() == suffix;
    }

    /// <summary>
    /// Trims leading and trailing ASCII whitespace characters from a string.
    /// </summary>
    /// <param name="value">String to trim.</param>
    /// <returns>Trimmed string copy.</returns>
    static HeCppString Trim(const HeCppString& value) {
        size_t start = 0;
        while (start < value.size() && IsAsciiWhitespace(static_cast<unsigned char>(value[start]))) {
            start++;
        }

        size_t end = value.size();
        while (end > start && IsAsciiWhitespace(static_cast<unsigned char>(value[end - 1]))) {
            end--;
        }

        return value.substr(start, end - start);
    }

    static HeCppString TrimStart(const HeCppString& value) {
        size_t start = 0;
        while (start < value.size() && IsAsciiWhitespace(static_cast<unsigned char>(value[start]))) {
            start++;
        }

        return value.substr(start);
    }

    /// <summary>
    /// Extracts a substring from the specified start index to the end of the string.
    /// </summary>
    /// <param name="value">Source string.</param>
    /// <returns>Original string copy.</returns>
    static HeCppString Substring(const HeCppString& value) {
        return value;
    }

    /// <summary>
    /// Extracts a substring from the specified start index to the end of the string.
    /// </summary>
    /// <param name="value">Source string.</param>
    /// <param name="startIndex">Zero-based start index.</param>
    /// <returns>Substring copy.</returns>
    static HeCppString Substring(const HeCppString& value, int32_t startIndex) {
        if (startIndex < 0 || static_cast<size_t>(startIndex) > value.size()) {
            return RaiseOutOfRange<HeCppString>("startIndex");
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
    static HeCppString Substring(const HeCppString& value, int32_t startIndex, int32_t length) {
        if (startIndex < 0 || length < 0 || static_cast<size_t>(startIndex) > value.size()) {
            return RaiseOutOfRange<HeCppString>("startIndex");
        }

        size_t safeStartIndex = static_cast<size_t>(startIndex);
        if (safeStartIndex + static_cast<size_t>(length) > value.size()) {
            return RaiseOutOfRange<HeCppString>("length");
        }

        return value.substr(safeStartIndex, static_cast<size_t>(length));
    }

    /// <summary>
    /// Determines whether a character is an ASCII digit.
    /// </summary>
    /// <param name="value">Character to inspect.</param>
    /// <returns>True when the character is a digit; otherwise false.</returns>
    static bool IsDigit(char value) {
        return IsAsciiDigit(static_cast<unsigned char>(value));
    }

    /// <summary>
    /// Converts a string to lowercase using invariant ASCII casing rules.
    /// </summary>
    /// <param name="value">String to transform.</param>
    /// <returns>Lowercase string copy.</returns>
    static HeCppString ToLowerInvariant(const HeCppString& value) {
        HeCppString lowered = value;
        he_cpp_alg::Transform(lowered.begin(), lowered.end(), lowered.begin(), [](unsigned char character) {
            return ToLowerAscii(character);
        });
        return lowered;
    }

    static char ToUpper(char value) {
        return ToUpperAscii(static_cast<unsigned char>(value));
    }

    static HeCppString Remove(const HeCppString& value, int32_t startIndex) {
        if (startIndex < 0 || static_cast<size_t>(startIndex) > value.size()) {
            return RaiseOutOfRange<HeCppString>("startIndex");
        }

        return value.substr(0, static_cast<size_t>(startIndex));
    }

    static HeCppString Remove(const HeCppString& value, int32_t startIndex, int32_t count) {
        if (startIndex < 0 || count < 0 || static_cast<size_t>(startIndex) > value.size()) {
            return RaiseOutOfRange<HeCppString>("startIndex");
        }

        size_t safeStartIndex = static_cast<size_t>(startIndex);
        size_t safeCount = static_cast<size_t>(count);
        if (safeStartIndex + safeCount > value.size()) {
            return RaiseOutOfRange<HeCppString>("count");
        }

        return value.substr(0, safeStartIndex) + value.substr(safeStartIndex + safeCount);
    }

    static HeCppString Insert(const HeCppString& value, int32_t startIndex, const HeCppString& insertion) {
        if (startIndex < 0 || static_cast<size_t>(startIndex) > value.size()) {
            return RaiseOutOfRange<HeCppString>("startIndex");
        }

        size_t safeStartIndex = static_cast<size_t>(startIndex);
        return value.substr(0, safeStartIndex) + insertion + value.substr(safeStartIndex);
    }

    static HeCppString Replace(const HeCppString& value, char oldValue, char newValue) {
        HeCppString updatedValue = value;
        he_cpp_alg::Replace(updatedValue.begin(), updatedValue.end(), oldValue, newValue);
        return updatedValue;
    }

    static HeCppString Replace(const HeCppString& value, const HeCppString& oldValue, const HeCppString& newValue) {
        if (oldValue.empty()) {
            return value;
        }

        HeCppString updatedValue = value;
        size_t searchIndex = 0;
        while (searchIndex < updatedValue.size()) {
            size_t matchIndex = updatedValue.find(oldValue, searchIndex);
            if (matchIndex == HeCppString::npos) {
                break;
            }

            updatedValue.replace(matchIndex, oldValue.size(), newValue);
            searchIndex = matchIndex + newValue.size();
        }

        return updatedValue;
    }

    /// <summary>
    /// Concatenates string values from a collection using the supplied separator.
    /// </summary>
    /// <typeparam name="TCollection">Iterable collection type that yields string-compatible values.</typeparam>
    /// <param name="separator">Separator inserted between adjacent values.</param>
    /// <param name="values">Collection to concatenate.</param>
    /// <returns>The concatenated string.</returns>
    template <typename TCollection>
    static HeCppString Join(const HeCppString& separator, const TCollection* values) {
        if (values == nullptr) {
            return HeCppString();
        }

        HeCppString result;
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

    template <typename... TValues>
    static HeCppString Join(const HeCppString& separator, const TValues&... values) {
        HeCppString result;
        bool isFirst = true;

        auto appendValue = [&](const auto& currentValue) {
            if (!isFirst) {
                result += separator;
            }

            result += ToJoinString(currentValue);
            isFirst = false;
        };

        (appendValue(values), ...);
        return result;
    }

    template <typename TCollection>
    static auto Join(const HeCppString& separator, const TCollection* values) -> decltype(values->begin(), values->end(), HeCppString()) {
        if (values == nullptr) {
            return HeCppString();
        }

        HeCppString result;
        bool isFirst = true;
        for (const auto& currentValue : *values) {
            if (!isFirst) {
                result += separator;
            }

            result += ToJoinString(currentValue);
            isFirst = false;
        }

        return result;
    }

    static HeCppString JoinArray(const HeCppString& separator, const Array<HeCppString>* values) {
        if (values == nullptr || values->Length <= 0) {
            return HeCppString();
        }

        HeCppString result;
        for (int32_t index = 0; index < values->Length; index++) {
            if (index > 0) {
                result += separator;
            }

            result += (*values)[index];
        }

        return result;
    }

    template <typename... TValues>
    static HeCppString Concat(const TValues&... values) {
        HeCppString result;
        auto appendValue = [&](const auto& currentValue) {
            result += ToJoinString(currentValue);
        };

        (appendValue(values), ...);
        return result;
    }

    /// <summary>
    /// Splits a string on one character while preserving empty segments by default.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, char separator, StringSplitOptions options = StringSplitOptions::None) {
        Array<char> separators({ separator });
        return Split(value, &separators, INT32_MAX, options);
    }

    /// <summary>
    /// Splits a string on the supplied character set while preserving empty segments by default.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, const Array<char>* separators, StringSplitOptions options = StringSplitOptions::None) {
        return Split(value, separators, INT32_MAX, options);
    }

    /// <summary>
    /// Splits a string on one character with the requested maximum number of returned segments.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, char separator, int32_t count) {
        Array<char> separators({ separator });
        return Split(value, &separators, count, StringSplitOptions::None);
    }

    /// <summary>
    /// Splits a string on one character with a segment count and empty-entry policy.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, char separator, int32_t count, StringSplitOptions options) {
        Array<char> separators({ separator });
        return Split(value, &separators, count, options);
    }

    /// <summary>
    /// Splits a string on the supplied character set with the requested maximum segment count.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, const Array<char>* separators, int32_t count) {
        return Split(value, separators, count, StringSplitOptions::None);
    }

    /// <summary>
    /// Implements the shared count and empty-entry rules for all character-based string splits.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, const Array<char>* separators, int32_t count, StringSplitOptions options) {
        if (count == 0) {
            return new Array<HeCppString>(0);
        }
        if (count < 0) {
            return he_cpp_raise_value<Array<HeCppString>*>(ArgumentOutOfRangeException("count"));
        }
        HeCppVector<HeCppString> parts;
        size_t segmentStart = 0;
        int32_t remainingParts = count;

        while (segmentStart <= value.size()) {
            if (remainingParts == 1) {
                if (options == StringSplitOptions::RemoveEmptyEntries) {
                    // .NET skips empty entries before capturing the remainder as the final segment.
                    while (segmentStart < value.size() && FindNextSeparator(value, separators, segmentStart) == segmentStart) {
                        segmentStart++;
                    }
                }

                HeCppString finalPart = value.substr(segmentStart);
                if (!(options == StringSplitOptions::RemoveEmptyEntries && finalPart.empty())) {
                    parts.push_back(finalPart);
                }

                break;
            }

            size_t separatorIndex = FindNextSeparator(value, separators, segmentStart);
            HeCppString part = separatorIndex == HeCppString::npos
                ? value.substr(segmentStart)
                : value.substr(segmentStart, separatorIndex - segmentStart);

            if (!(options == StringSplitOptions::RemoveEmptyEntries && part.empty())) {
                parts.push_back(part);
                remainingParts--;
            }

            if (separatorIndex == HeCppString::npos) {
                break;
            }

            segmentStart = separatorIndex + 1;
        }

        Array<HeCppString>* result = new Array<HeCppString>(static_cast<int32_t>(parts.size()));
        for (int32_t index = 0; index < result->Length; index++) {
            (*result)[index] = parts[static_cast<size_t>(index)];
        }

        return result;
    }
    static size_t FindNextSeparator(const HeCppString& value, const Array<char>* separators, size_t startIndex) {
        if (separators == nullptr || separators->Length <= 0) {
            return value.find_first_of(" \t\r\n", startIndex);
        }

        for (size_t index = startIndex; index < value.size(); index++) {
            for (int32_t separatorIndex = 0; separatorIndex < separators->Length; separatorIndex++) {
                if (value[index] == (*separators)[separatorIndex]) {
                    return index;
                }
            }
        }

        return HeCppString::npos;
    }

    static HeCppString ToJoinString(const HeCppString& value) {
        return value;
    }

    static HeCppString ToJoinString(const char* value) {
        return value == nullptr ? HeCppString() : HeCppString(value);
    }

    static HeCppString ToJoinString(char value) {
        return HeCppString(1, value);
    }

    template <typename TValue>
    static std::enable_if_t<std::is_arithmetic_v<TValue>, HeCppString> ToJoinString(const TValue& value) {
        if constexpr (std::is_same_v<TValue, bool>) {
            return value ? "True" : "False";
        } else {
            HeCppString builder;
            AppendArithmeticToString(builder, value);
            return builder;
        }
    }

    template <typename TValue>
    static std::enable_if_t<std::is_pointer_v<TValue>, HeCppString> ToJoinString(const TValue& value) {
        if (value == nullptr) {
            return HeCppString();
        }

        using PointeeType = std::remove_pointer_t<TValue>;
        if constexpr (he_cpp_has_to_string_method<PointeeType>::value) {
            return value->ToString();
        }

#if HE_CPP_USE_RTTI
        return HeCppString(typeid(PointeeType).name());
#else
        static_assert(he_cpp_has_to_string_method<PointeeType>::value, "String formatting for a pointer without ToString requires RTTI.");
        return HeCppString();
#endif
    }

    template <typename TValue>
    static std::enable_if_t<!std::is_pointer_v<TValue> && !std::is_arithmetic_v<TValue>, HeCppString> ToJoinString(const TValue& value) {
        return value;
    }

private:
    /// <summary>
    /// Appends an arithmetic value using a lightweight managed-style formatting path.
    /// </summary>
    template <typename TValue>
    static std::enable_if_t<std::is_integral_v<TValue> && !std::is_same_v<TValue, bool>, void> AppendArithmeticToString(HeCppString& builder, TValue value) {
        AppendIntegralToString(builder, value);
    }

    /// <summary>
    /// Appends a floating-point value using a lightweight managed-style formatting path.
    /// </summary>
    template <typename TValue>
    static std::enable_if_t<std::is_floating_point_v<TValue>, void> AppendArithmeticToString(HeCppString& builder, TValue value) {
        AppendFloatingPointToString(builder, value);
    }

    /// <summary>
    /// Appends an unsigned integral value in base-10 without locale-aware standard library formatting.
    /// </summary>
    template <typename TValue>
    static std::enable_if_t<std::is_integral_v<TValue> && std::is_unsigned_v<TValue>, void> AppendIntegralToString(HeCppString& builder, TValue value) {
        char digits[32];
        int32_t digitCount = 0;
        TValue remainingValue = value;

        do {
            TValue digit = remainingValue % static_cast<TValue>(10);
            digits[digitCount++] = static_cast<char>('0' + digit);
            remainingValue /= static_cast<TValue>(10);
        } while (remainingValue != 0);

        while (digitCount > 0) {
            builder += digits[--digitCount];
        }
    }

    /// <summary>
    /// Appends a signed integral value in base-10 without locale-aware standard library formatting.
    /// </summary>
    template <typename TValue>
    static std::enable_if_t<std::is_integral_v<TValue> && std::is_signed_v<TValue>, void> AppendIntegralToString(HeCppString& builder, TValue value) {
        using UnsignedValue = std::make_unsigned_t<TValue>;
        if (value < 0) {
            builder += '-';
            UnsignedValue magnitude = static_cast<UnsignedValue>(-(value + 1));
            magnitude += 1;
            AppendIntegralToString(builder, magnitude);
            return;
        }

        AppendIntegralToString(builder, static_cast<UnsignedValue>(value));
    }

    /// <summary>
    /// Appends a floating-point value while preserving managed-friendly literals for special values.
    /// </summary>
    template <typename TValue>
    static void AppendFloatingPointToString(HeCppString& builder, TValue value) {
        if (IsNaN(value)) {
            builder += "NaN";
            return;
        }

        if (IsInfinite(value)) {
            builder += value < static_cast<TValue>(0) ? "-Infinity" : "Infinity";
            return;
        }

        if (value == static_cast<TValue>(0)) {
            builder += "0";
            return;
        }

        double absoluteValue = static_cast<double>(value);
        if (absoluteValue < 0.0) {
            builder += '-';
            absoluteValue = -absoluteValue;
        }

        int32_t precision = std::is_same_v<TValue, float> ? 9 : 17;
        int32_t integerDigitCount = CountIntegerDigits(absoluteValue);
        if (absoluteValue < 0.0001 || integerDigitCount > precision) {
            AppendScientificFloatingPoint(builder, absoluteValue, precision);
            return;
        }

        AppendFixedFloatingPoint(builder, absoluteValue, precision - integerDigitCount);
    }

    /// <summary>
    /// Appends a floating-point value using scientific notation.
    /// </summary>
    static void AppendScientificFloatingPoint(HeCppString& builder, double positiveValue, int32_t precision) {
        int32_t exponent = 0;
        while (positiveValue >= 10.0) {
            positiveValue /= 10.0;
            exponent++;
        }

        while (positiveValue < 1.0) {
            positiveValue *= 10.0;
            exponent--;
        }

        AppendFixedFloatingPoint(builder, positiveValue, precision - 1);
        builder += 'e';
        if (exponent >= 0) {
            builder += '+';
        }

        AppendIntegralToString(builder, exponent);
    }

    /// <summary>
    /// Appends a floating-point value using fixed notation and trims trailing zeroes.
    /// </summary>
    static void AppendFixedFloatingPoint(HeCppString& builder, double positiveValue, int32_t fractionalDigitCount) {
        uint64_t integerPart = static_cast<uint64_t>(positiveValue);
        AppendIntegralToString(builder, integerPart);
        if (fractionalDigitCount <= 0) {
            return;
        }

        double fractionalPart = positiveValue - static_cast<double>(integerPart);
        if (fractionalPart <= 0.0) {
            return;
        }

        std::size_t fractionalStartIndex = builder.size();
        builder += '.';

        for (int32_t digitIndex = 0; digitIndex < fractionalDigitCount; digitIndex++) {
            fractionalPart *= 10.0;
            int32_t digit = static_cast<int32_t>(fractionalPart);
            if (digit > 9) {
                digit = 9;
            }

            builder += static_cast<char>('0' + digit);
            fractionalPart -= static_cast<double>(digit);
            if (fractionalPart <= 0.0) {
                break;
            }
        }

        while (builder.size() > fractionalStartIndex + 1 && builder.back() == '0') {
            builder.pop_back();
        }

        if (!builder.empty() && builder.back() == '.') {
            builder.pop_back();
        }
    }

    /// <summary>
    /// Counts the number of decimal digits in the integer portion of a positive floating-point value.
    /// </summary>
    static int32_t CountIntegerDigits(double positiveValue) {
        int32_t digitCount = 1;
        while (positiveValue >= 10.0) {
            positiveValue /= 10.0;
            digitCount++;
        }

        return digitCount;
    }
};
