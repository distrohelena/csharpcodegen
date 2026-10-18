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
    /// <summary>Provides a present empty managed string, distinct from the default null value.</summary>
    inline static const HeCppString Empty = HeCppString("");

    /// <summary>Determines whether a managed string is null without conflating it with an empty value.</summary>
    static bool IsNull(const HeCppString& value) {
        return value.is_null();
    }

    /// <summary>
    /// Determines whether the supplied string is null or contains no characters.
    /// </summary>
    /// <param name="value">String value to inspect.</param>
    /// <returns>True for managed null and present empty strings; otherwise false.</returns>
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
        if (left.is_null() || right.is_null()) {
            return left.is_null() && right.is_null();
        }

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
        he_cpp_require_string_value(value);
        if (prefix.is_null()) {
            he_cpp_raise(ArgumentNullException("prefix"));
        }
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
        he_cpp_require_string_value(value);
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
        he_cpp_require_string_value(value);
        if (suffix.is_null()) {
            he_cpp_raise(ArgumentNullException("suffix"));
        }
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
        he_cpp_require_string_value(value);
        return !value.empty() && value.back() == suffix;
    }

    /// <summary>
    /// Trims leading and trailing ASCII whitespace characters from a string.
    /// </summary>
    /// <param name="value">String to trim.</param>
    /// <returns>Trimmed string copy.</returns>
    static HeCppString Trim(const HeCppString& value) {
        he_cpp_require_string_value(value);
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
        he_cpp_require_string_value(value);
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
        he_cpp_require_string_value(value);
        return value;
    }

    /// <summary>
    /// Extracts a substring from the specified start index to the end of the string.
    /// </summary>
    /// <param name="value">Source string.</param>
    /// <param name="startIndex">Zero-based start index.</param>
    /// <returns>Substring copy.</returns>
    static HeCppString Substring(const HeCppString& value, int32_t startIndex) {
        he_cpp_require_string_value(value);
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
        he_cpp_require_string_value(value);
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
        he_cpp_require_string_value(value);
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
        he_cpp_require_string_value(value);
        if (startIndex < 0 || static_cast<size_t>(startIndex) > value.size()) {
            return RaiseOutOfRange<HeCppString>("startIndex");
        }

        return value.substr(0, static_cast<size_t>(startIndex));
    }

    static HeCppString Remove(const HeCppString& value, int32_t startIndex, int32_t count) {
        he_cpp_require_string_value(value);
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
        he_cpp_require_string_value(value);
        if (insertion.is_null()) {
            he_cpp_raise(ArgumentNullException("insertion"));
        }
        if (startIndex < 0 || static_cast<size_t>(startIndex) > value.size()) {
            return RaiseOutOfRange<HeCppString>("startIndex");
        }

        size_t safeStartIndex = static_cast<size_t>(startIndex);
        return value.substr(0, safeStartIndex) + insertion + value.substr(safeStartIndex);
    }

    static HeCppString Replace(const HeCppString& value, char oldValue, char newValue) {
        he_cpp_require_string_value(value);
        HeCppString updatedValue = value;
        he_cpp_alg::Replace(updatedValue.begin(), updatedValue.end(), oldValue, newValue);
        return updatedValue;
    }

    /// <summary>
    /// Replaces every non-overlapping occurrence, without processing text inserted by a previous match; an empty search value is invalid.
    /// </summary>
    static HeCppString Replace(const HeCppString& value, const HeCppString& oldValue, const HeCppString& newValue) {
        he_cpp_require_string_value(value);
        if (oldValue.is_null()) {
            he_cpp_raise(ArgumentNullException("oldValue"));
        }
        if (oldValue.empty()) {
            return he_cpp_raise_value<HeCppString>(ArgumentException("oldValue must not be empty"));
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
    template <typename... TValues>
    static HeCppString Join(const HeCppString& separator, const TValues&... values) {
        HeCppString result = "";
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
    static auto Join(const HeCppString& separator, TCollection* values) -> decltype(values->begin(), values->end(), HeCppString("")) {
        if (values == nullptr) {
            return HeCppString("");
        }

        HeCppString result = "";
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
            return HeCppString("");
        }

        HeCppString result = "";
        for (int32_t index = 0; index < values->Length; index++) {
            if (index > 0) {
                result += separator;
            }

            result += (*values)[index];
        }

        return result;
    }

    /// <summary>Concatenates an iterable managed collection as values rather than formatting its native pointer.</summary>
    template <typename TCollection>
    static auto Concat(TCollection* values) -> decltype(values->begin(), values->end(), HeCppString("")) {
        return Join(HeCppString(""), values);
    }

    template <typename... TValues>
    static HeCppString Concat(const TValues&... values) {
        HeCppString result = "";
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
        he_cpp_require_string_value(value);
        Array<char> separators({ separator });
        return Split(value, &separators, INT32_MAX, options);
    }

    /// <summary>
    /// Splits a string on the supplied character set while preserving empty segments by default.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, const Array<char>* separators, StringSplitOptions options = StringSplitOptions::None) {
        he_cpp_require_string_value(value);
        return Split(value, separators, INT32_MAX, options);
    }

    /// <summary>
    /// Splits a string on one character with the requested maximum number of returned segments.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, char separator, int32_t count) {
        he_cpp_require_string_value(value);
        Array<char> separators({ separator });
        return Split(value, &separators, count, StringSplitOptions::None);
    }

    /// <summary>
    /// Splits a string on one character with a segment count and empty-entry policy.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, char separator, int32_t count, StringSplitOptions options) {
        he_cpp_require_string_value(value);
        Array<char> separators({ separator });
        return Split(value, &separators, count, options);
    }

    /// <summary>
    /// Splits a string on the supplied character set with the requested maximum segment count.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, const Array<char>* separators, int32_t count) {
        he_cpp_require_string_value(value);
        return Split(value, separators, count, StringSplitOptions::None);
    }

    /// <summary>
    /// Implements the shared count and empty-entry rules for all character-based string splits.
    /// </summary>
    static Array<HeCppString>* Split(const HeCppString& value, const Array<char>* separators, int32_t count, StringSplitOptions options) {
        he_cpp_require_string_value(value);
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

    /// Reports invalid numeric formats through the configured native exception contract, or terminates exception-free runtimes.
    [[noreturn]] static void RejectNumericFormat(const char* message) {
#if HE_CPP_USE_EXCEPTIONS
        throw new ::ArgumentException(message);
#else
        he_cpp_runtime_detail::DefaultFail(message);
#endif
    }

    /// Formats primitive D/X integers and F/G/E binary64 values without hosted stdio or locale services.
    template <typename TValue>
    static std::enable_if_t<std::is_arithmetic_v<TValue>, HeCppString> FormatNumeric(TValue value, const HeCppString& format) {
        if (format.empty()) return ToJoinString(value);
        const char specifier = format[0];
        int precision = 0;
        for (std::size_t index = 1; index < format.size(); ++index) {
            if (format[index] < '0' || format[index] > '9' || precision > 99) RejectNumericFormat("Unsupported numeric format precision.");
            precision = precision * 10 + (format[index] - '0');
        }
        if (precision > 99) RejectNumericFormat("Numeric precision must not exceed 99.");
        if (specifier == 'D' || specifier == 'd' || specifier == 'X' || specifier == 'x') {
            if constexpr (!std::is_integral_v<TValue> || std::is_same_v<TValue, bool>) {
                RejectNumericFormat("Integral numeric formats require an integer value.");
            } else {
                using Unsigned = std::make_unsigned_t<TValue>;
                const bool hexadecimal = specifier == 'X' || specifier == 'x';
                const bool negative = !hexadecimal && std::is_signed_v<TValue> && value < 0;
                Unsigned magnitude = static_cast<Unsigned>(value);
                if (negative) magnitude = static_cast<Unsigned>(Unsigned(0) - magnitude);
                const Unsigned radix = hexadecimal ? 16 : 10;
                const char* alphabet = specifier == 'X' ? "0123456789ABCDEF" : "0123456789abcdef";
                char digits[64];
                int count = 0;
                do {
                    digits[count++] = alphabet[magnitude % radix];
                    magnitude /= radix;
                } while (magnitude != 0);
                HeCppString result = negative ? "-" : "";
                for (int padding = count; padding < precision; ++padding) result += '0';
                while (count != 0) result += digits[--count];
                return result;
            }
        }
        if (specifier != 'F' && specifier != 'f' && specifier != 'E' && specifier != 'e' && specifier != 'G' && specifier != 'g') {
            RejectNumericFormat("Unsupported native numeric format specifier.");
        }
        if (format.size() == 1) precision = specifier == 'F' || specifier == 'f' ? 2 : specifier == 'E' || specifier == 'e' ? 6 : 9;
        if ((specifier == 'G' || specifier == 'g') && precision == 0) precision = 9;
        return FormatBinaryNumeric(static_cast<double>(value), specifier, precision);
    }

    static HeCppString ToJoinString(const HeCppString& value) {
        return value.is_null() ? Empty : value;
    }

    static HeCppString ToJoinString(const char* value) {
        return value == nullptr ? HeCppString("") : HeCppString(value);
    }

    /// <summary>Formats a null reference as a present empty string for joining and concatenation.</summary>
    static HeCppString ToJoinString(std::nullptr_t) {
        return Empty;
    }

    static HeCppString ToJoinString(char value) {
        return HeCppString(1, value);
    }

    template <typename TValue>
    static std::enable_if_t<std::is_arithmetic_v<TValue>, HeCppString> ToJoinString(const TValue& value) {
        if constexpr (std::is_same_v<TValue, bool>) {
            return value ? "True" : "False";
        } else {
            HeCppString builder = "";
            AppendArithmeticToString(builder, value);
            return builder;
        }
    }

    /// <summary>Formats an integral value with exactly the managed D6 minimum digit count, preserving the sign and full unsigned magnitude.</summary>
    template <typename TValue>
    static std::enable_if_t<std::is_integral_v<TValue> && !std::is_same_v<TValue, bool>, HeCppString> ToInvariantDecimal6(TValue value) {
        HeCppString digits = "";
        AppendIntegralToString(digits, value);
        bool negative = !digits.empty() && digits[0] == '-';
        std::size_t count = digits.size() - (negative ? 1 : 0);
        HeCppString result = negative ? "-" : "";
        while (count++ < 6) {
            result += '0';
        }
        for (std::size_t index = negative ? 1 : 0; index < digits.size(); index++) {
            result += digits[index];
        }
        return result;
    }

    /// <summary>Formats an integral F0/F1 value without converting large integers to floating point or losing trailing fractional zeroes.</summary>
    template <typename TValue>
    static std::enable_if_t<std::is_integral_v<TValue> && !std::is_same_v<TValue, bool>, HeCppString> ToInvariantFixed(TValue value, int32_t precision) {
        HeCppString result = "";
        AppendIntegralToString(result, value);
        if (precision == 1) {
            result += ".0";
        } else if (precision != 0) {
            he_cpp_raise(NotSupportedException("Native invariant fixed formatting supports only F0 and F1."));
        }
        return result;
    }

    /// <summary>Formats binary32/binary64 F0/F1 values by exact integer decomposition and ties-to-even rounding, including negative zero and the complete finite exponent range.</summary>
    template <typename TValue>
    static std::enable_if_t<std::is_floating_point_v<TValue>, HeCppString> ToInvariantFixed(TValue value, int32_t precision) {
        if (precision != 0 && precision != 1) {
            he_cpp_raise(NotSupportedException("Native invariant fixed formatting supports only F0 and F1."));
        }
        double binaryValue = static_cast<double>(value);
        uint64_t bits;
        __builtin_memcpy(&bits, &binaryValue, sizeof(bits));
        bool negative = (bits >> 63) != 0;
        uint32_t biasedExponent = static_cast<uint32_t>((bits >> 52) & 0x7ff);
        uint64_t significand = bits & 0x000fffffffffffffULL;
        if (biasedExponent == 0x7ff) {
            return significand != 0 ? HeCppString("NaN") : negative ? HeCppString("-Infinity") : HeCppString("Infinity");
        }
        int32_t exponent = biasedExponent == 0 ? -1074 : static_cast<int32_t>(biasedExponent) - 1075;
        if (biasedExponent != 0) {
            significand |= 0x0010000000000000ULL;
        }
        if (precision == 1) {
            significand *= 5;
            exponent++;
        }
        if (exponent < 0) {
            int32_t shift = -exponent;
            if (shift >= 64) {
                significand = 0;
            } else {
                uint64_t quotient = significand >> shift;
                uint64_t remainder = significand & ((1ULL << shift) - 1);
                uint64_t half = 1ULL << (shift - 1);
                if (remainder > half || (remainder == half && (quotient & 1) != 0)) {
                    quotient++;
                }
                significand = quotient;
            }
            exponent = 0;
        }
        // Binary64's largest finite number needs 309 decimal digits, or 310 after the exact F1 scaling.
        uint8_t digits[312];
        int32_t digitCount = 0;
        do {
            digits[digitCount++] = static_cast<uint8_t>(significand % 10);
            significand /= 10;
        } while (significand != 0);
        for (int32_t bit = 0; bit < exponent; bit++) {
            uint8_t carry = 0;
            for (int32_t digit = 0; digit < digitCount; digit++) {
                uint8_t doubled = static_cast<uint8_t>(digits[digit] * 2 + carry);
                digits[digit] = doubled % 10;
                carry = doubled / 10;
            }
            if (carry != 0) {
                digits[digitCount++] = carry;
            }
        }
        while (digitCount <= precision) {
            digits[digitCount++] = 0;
        }
        HeCppString result = negative ? "-" : "";
        for (int32_t digit = digitCount - 1; digit >= 0; digit--) {
            result += static_cast<char>('0' + digits[digit]);
            if (precision == 1 && digit == 1) {
                result += '.';
            }
        }
        return result;
    }

    template <typename TValue>
    static std::enable_if_t<std::is_pointer_v<TValue>, HeCppString> ToJoinString(const TValue& value) {
        if (value == nullptr) {
            return HeCppString("");
        }

        using PointeeType = std::remove_pointer_t<TValue>;
        if constexpr (he_cpp_has_to_string_method<PointeeType>::value) {
            return value->ToString();
        }

#if HE_CPP_USE_RTTI
        return HeCppString(typeid(PointeeType).name());
#else
        static_assert(he_cpp_has_to_string_method<PointeeType>::value, "String formatting for a pointer without ToString requires RTTI.");
        return HeCppString("");
#endif
    }

    template <typename TValue>
    static std::enable_if_t<!std::is_pointer_v<TValue> && !std::is_arithmetic_v<TValue>, HeCppString> ToJoinString(const TValue& value) {
        return value;
    }

private:
    /// Multiplies little-endian decimal digits by two or five during exact binary64 decomposition.
    static void MultiplyNumericDigits(uint8_t* digits, int& count, int multiplier) {
        int carry = 0;
        for (int index = 0; index < count; ++index) {
            const int product = digits[index] * multiplier + carry;
            digits[index] = static_cast<uint8_t>(product % 10);
            carry = product / 10;
        }
        if (carry != 0) digits[count++] = static_cast<uint8_t>(carry);
    }

    /// Divides decimal digits by a power of ten with exact round-to-nearest, ties-to-even behavior.
    static void RoundNumericDigits(uint8_t* digits, int& count, int& scale, int removed) {
        if (removed <= 0) return;
        bool roundUp = false;
        if (removed <= count) {
            const int firstDiscarded = digits[removed - 1];
            bool sticky = false;
            for (int index = 0; index < removed - 1; ++index) sticky = sticky || digits[index] != 0;
            roundUp = firstDiscarded > 5 || (firstDiscarded == 5 && (sticky || (removed < count && (digits[removed] & 1) != 0)));
        }
        int remaining = count - removed;
        if (remaining <= 0) {
            count = 1;
            digits[0] = roundUp ? 1 : 0;
        } else {
            for (int index = 0; index < remaining; ++index) digits[index] = digits[index + removed];
            count = remaining;
            if (roundUp) {
                int index = 0;
                while (index < count && digits[index] == 9) digits[index++] = 0;
                if (index == count) digits[count++] = 1;
                else ++digits[index];
            }
        }
        scale -= removed;
    }

    /// Emits fixed decimal punctuation, padding missing positions without floating-point arithmetic.
    static void AppendNumericFixed(HeCppString& result, const uint8_t* digits, int count, int scale, int precision) {
        const int exponent = count - scale - 1;
        for (int power = exponent > 0 ? exponent : 0; power >= -precision; --power) {
            if (power == -1) result += '.';
            const int index = power + scale;
            result += static_cast<char>('0' + (index >= 0 && index < count ? digits[index] : 0));
        }
    }

    /// Emits scientific notation with an explicit sign and at least two decimal exponent digits.
    static void AppendNumericScientific(HeCppString& result, const uint8_t* digits, int count, int exponent, int precision, char marker) {
        result += static_cast<char>('0' + digits[count - 1]);
        if (precision != 0) result += '.';
        for (int index = 1; index <= precision; ++index) {
            result += static_cast<char>('0' + (index < count ? digits[count - index - 1] : 0));
        }
        result += marker;
        result += exponent < 0 ? '-' : '+';
        if (exponent < 0) exponent = -exponent;
        if (exponent < 10) result += '0';
        AppendIntegralToString(result, exponent);
    }

    /// Preserves the existing printf-compatible F/E/G contract using exact binary64 decimal expansion.
    static HeCppString FormatBinaryNumeric(double value, char specifier, int precision) {
        uint64_t bits;
        __builtin_memcpy(&bits, &value, sizeof(bits));
        const bool negative = (bits >> 63) != 0;
        const bool uppercase = specifier == 'E' || specifier == 'G';
        const uint32_t biasedExponent = static_cast<uint32_t>((bits >> 52) & 0x7ff);
        uint64_t significand = bits & 0x000fffffffffffffULL;
        HeCppString result = negative ? "-" : "";
        if (biasedExponent == 0x7ff) {
            result += significand != 0 ? (uppercase ? "NAN" : "nan") : (uppercase ? "INF" : "inf");
            return result;
        }
        int binaryExponent = biasedExponent == 0 ? -1074 : static_cast<int>(biasedExponent) - 1075;
        if (biasedExponent != 0) significand |= 0x0010000000000000ULL;
        // The exact expansion needs at most 767 decimal digits; leave space for rounding carry.
        uint8_t digits[800];
        int count = 0;
        do {
            digits[count++] = static_cast<uint8_t>(significand % 10);
            significand /= 10;
        } while (significand != 0);
        int scale = 0;
        if (count != 1 || digits[0] != 0) {
            const int multiplier = binaryExponent < 0 ? 5 : 2;
            const int steps = binaryExponent < 0 ? -binaryExponent : binaryExponent;
            for (int index = 0; index < steps; ++index) MultiplyNumericDigits(digits, count, multiplier);
            if (binaryExponent < 0) scale = -binaryExponent;
        }
        const bool fixed = specifier == 'F' || specifier == 'f';
        const bool general = specifier == 'G' || specifier == 'g';
        RoundNumericDigits(digits, count, scale, fixed ? scale - precision : count - precision - (general ? 0 : 1));
        const bool zero = count == 1 && digits[0] == 0;
        const int exponent = zero ? 0 : count - scale - 1;
        if (zero) scale = 0;
        const bool scientific = !fixed && (!general || exponent < -4 || exponent >= precision);
        if (scientific) {
            AppendNumericScientific(result, digits, count, exponent, general ? precision - 1 : precision, uppercase ? 'E' : 'e');
        } else {
            AppendNumericFixed(result, digits, count, scale, fixed ? precision : (precision > exponent + 1 ? precision - exponent - 1 : 0));
        }
        if (general) {
            // Remove insignificant fractional zeros before any scientific exponent.
            const std::size_t exponentStart = scientific ? result.find(uppercase ? 'E' : 'e') : result.size();
            const std::size_t point = result.find('.');
            if (point != HeCppString::npos && point < exponentStart) {
                std::size_t end = exponentStart;
                while (end > point + 1 && result[end - 1] == '0') --end;
                if (end == point + 1) --end;
                result.erase(end, exponentStart - end);
            }
        }
        if (result.size() >= 768) RejectNumericFormat("Formatted numeric value exceeds bounded storage.");
        return result;
    }

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
