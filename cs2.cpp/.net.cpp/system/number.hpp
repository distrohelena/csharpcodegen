#pragma once

#include "../runtime/native_exceptions.hpp"

#include <charconv>
#include <cmath>
#include <cstdint>
#include <functional>
#include <limits>
#include <string>
#include <type_traits>

/// <summary>
/// Provides lightweight managed numeric helpers used by transpiled static primitive calls.
/// </summary>
class Number {
public:
    inline static constexpr double Epsilon = 2.2204460492503131e-16;

    /// <summary>
    /// Attempts to parse a signed 32-bit integer from text.
    /// </summary>
    /// <param name="text">Source text to parse.</param>
    /// <param name="value">Parsed value when the conversion succeeds.</param>
    /// <returns>True when parsing succeeds; otherwise false.</returns>
    static bool TryParse(const std::string& text, int32_t& value) {
        const char* begin = text.data();
        const char* end = begin + text.size();
        std::from_chars_result result = std::from_chars(begin, end, value);
        return result.ec == std::errc() && result.ptr == end;
    }

    /// <summary>
    /// Determines whether the supplied single-precision value is positive infinity.
    /// </summary>
    /// <param name="value">Value to inspect.</param>
    /// <returns>True when the value is positive infinity; otherwise false.</returns>
    static bool IsPositiveInfinity(float value) {
        return std::isinf(value) && value > 0.0f;
    }

    /// <summary>
    /// Determines whether the supplied double-precision value is positive infinity.
    /// </summary>
    /// <param name="value">Value to inspect.</param>
    /// <returns>True when the value is positive infinity; otherwise false.</returns>
    static bool IsPositiveInfinity(double value) {
        return std::isinf(value) && value > 0.0;
    }

    /// <summary>
    /// Determines whether the supplied single-precision value is not-a-number.
    /// </summary>
    /// <param name="value">Value to inspect.</param>
    /// <returns>True when the value is NaN; otherwise false.</returns>
    static bool IsNaN(float value) {
        return std::isnan(value);
    }

    /// <summary>
    /// Determines whether the supplied double-precision value is not-a-number.
    /// </summary>
    /// <param name="value">Value to inspect.</param>
    /// <returns>True when the value is NaN; otherwise false.</returns>
    static bool IsNaN(double value) {
        return std::isnan(value);
    }

    /// <summary>
    /// Determines whether the supplied single-precision value is infinite.
    /// </summary>
    /// <param name="value">Value to inspect.</param>
    /// <returns>True when the value is infinite; otherwise false.</returns>
    static bool IsInfinity(float value) {
        return std::isinf(value);
    }

    /// <summary>
    /// Determines whether the supplied double-precision value is infinite.
    /// </summary>
    /// <param name="value">Value to inspect.</param>
    /// <returns>True when the value is infinite; otherwise false.</returns>
    static bool IsInfinity(double value) {
        return std::isinf(value);
    }

    /// <summary>
    /// Compares two integral or Boolean primitive values using managed value equality semantics.
    /// </summary>
    /// <typeparam name="T">Primitive type shared by both operands.</typeparam>
    /// <param name="left">Left primitive operand.</param>
    /// <param name="right">Right primitive operand.</param>
    /// <returns>True when both operands represent the same value; otherwise false.</returns>
    template <typename T>
    static bool Equals(const T& left, const T& right) {
        return left == right;
    }

    /// <summary>
    /// Compares two single-precision values while preserving managed equality for not-a-number values.
    /// </summary>
    /// <param name="left">Left single-precision operand.</param>
    /// <param name="right">Right single-precision operand.</param>
    /// <returns>True when both values are equal or both are not-a-number; otherwise false.</returns>
    static bool Equals(float left, float right) {
        return left == right || (std::isnan(left) && std::isnan(right));
    }

    /// <summary>
    /// Compares two double-precision values while preserving managed equality for not-a-number values.
    /// </summary>
    /// <param name="left">Left double-precision operand.</param>
    /// <param name="right">Right double-precision operand.</param>
    /// <returns>True when both values are equal or both are not-a-number; otherwise false.</returns>
    static bool Equals(double left, double right) {
        return left == right || (std::isnan(left) && std::isnan(right));
    }

    /// <summary>
    /// Evaluates primitive operands passed through a cross-type managed <c>Equals(object)</c> overload and reports unequal boxed types.
    /// </summary>
    /// <typeparam name="TLeft">Primitive receiver type.</typeparam>
    /// <typeparam name="TRight">Primitive argument type.</typeparam>
    /// <param name="left">Primitive receiver value.</param>
    /// <param name="right">Primitive argument value evaluated before comparison.</param>
    /// <returns>False because this helper is emitted only after overload resolution proves the managed primitive types differ.</returns>
    template <typename TLeft, typename TRight>
    static bool EqualsObject(const TLeft& left, const TRight& right) {
        (void)left;
        (void)right;
        return false;
    }

    /// <summary>
    /// Applies managed checked prefix increment semantics to one fixed-width integral value.
    /// </summary>
    /// <typeparam name="T">Integral operand type.</typeparam>
    /// <param name="value">Assignable value to validate and increment.</param>
    /// <returns>The incremented value.</returns>
    template <typename T>
    static T CheckedPreIncrement(T& value) {
        if (value == std::numeric_limits<T>::max()) {
            throw OverflowException();
        }

        value = static_cast<T>(value + static_cast<T>(1));
        return value;
    }

    /// <summary>
    /// Applies managed checked postfix increment semantics to one fixed-width integral value.
    /// </summary>
    /// <typeparam name="T">Integral operand type.</typeparam>
    /// <param name="value">Assignable value to validate and increment.</param>
    /// <returns>The value captured before incrementing.</returns>
    template <typename T>
    static T CheckedPostIncrement(T& value) {
        if (value == std::numeric_limits<T>::max()) {
            throw OverflowException();
        }

        T originalValue = value;
        value = static_cast<T>(value + static_cast<T>(1));
        return originalValue;
    }

    /// <summary>
    /// Applies managed checked prefix decrement semantics to one fixed-width integral value.
    /// </summary>
    /// <typeparam name="T">Integral operand type.</typeparam>
    /// <param name="value">Assignable value to validate and decrement.</param>
    /// <returns>The decremented value.</returns>
    template <typename T>
    static T CheckedPreDecrement(T& value) {
        if (value == std::numeric_limits<T>::lowest()) {
            throw OverflowException();
        }

        value = static_cast<T>(value - static_cast<T>(1));
        return value;
    }

    /// <summary>
    /// Applies managed checked postfix decrement semantics to one fixed-width integral value.
    /// </summary>
    /// <typeparam name="T">Integral operand type.</typeparam>
    /// <param name="value">Assignable value to validate and decrement.</param>
    /// <returns>The value captured before decrementing.</returns>
    template <typename T>
    static T CheckedPostDecrement(T& value) {
        if (value == std::numeric_limits<T>::lowest()) {
            throw OverflowException();
        }

        T originalValue = value;
        value = static_cast<T>(value - static_cast<T>(1));
        return originalValue;
    }

    /// <summary>
    /// Applies managed checked same-type integral addition and writes the result only after proving it is representable.
    /// </summary>
    /// <typeparam name="T">Integral type shared by the target and value.</typeparam>
    /// <param name="left">Assignable target value.</param>
    /// <param name="right">Value to add to the target.</param>
    /// <returns>The validated sum written to the target.</returns>
    template <typename T>
    static T CheckedAddAssign(T& left, const T& right) {
        if constexpr (std::is_unsigned_v<T>) {
            if (right > std::numeric_limits<T>::max() - left) {
                throw OverflowException();
            }
        } else {
            if ((right > static_cast<T>(0) && left > std::numeric_limits<T>::max() - right) ||
                (right < static_cast<T>(0) && left < std::numeric_limits<T>::lowest() - right)) {
                throw OverflowException();
            }
        }

        left = static_cast<T>(left + right);
        return left;
    }

    /// <summary>
    /// Returns a positive-infinity value for the requested floating-point type.
    /// </summary>
    /// <typeparam name="T">Floating-point type whose positive infinity should be returned.</typeparam>
    /// <returns>Positive-infinity constant for <typeparamref name="T"/>.</returns>
    template <typename T>
    static T PositiveInfinity() {
        return std::numeric_limits<T>::infinity();
    }

    /// <summary>
    /// Returns a negative-infinity value for the requested floating-point type.
    /// </summary>
    /// <typeparam name="T">Floating-point type whose negative infinity should be returned.</typeparam>
    /// <returns>Negative-infinity constant for <typeparamref name="T"/>.</returns>
    template <typename T>
    static T NegativeInfinity() {
        return -std::numeric_limits<T>::infinity();
    }

    /// <summary>
    /// Returns a quiet NaN value for the requested floating-point type.
    /// </summary>
    /// <typeparam name="T">Floating-point type whose NaN constant should be returned.</typeparam>
    /// <returns>Quiet NaN constant for <typeparamref name="T"/>.</returns>
    template <typename T>
    static T NaN() {
        return std::numeric_limits<T>::quiet_NaN();
    }

    /// <summary>
    /// Produces a stable integer hash code for a primitive value using the native standard hash surface.
    /// </summary>
    /// <typeparam name="T">Primitive value type to hash.</typeparam>
    /// <param name="value">Value whose hash code should be produced.</param>
    /// <returns>Signed 32-bit hash code for the supplied value.</returns>
    template <typename T>
    static int32_t GetHashCode(const T& value) {
        return static_cast<int32_t>(std::hash<T>{}(value));
    }
};
