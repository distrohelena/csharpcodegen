#pragma once
#include "../helcpp_config.hpp"
#include "native_hash.hpp"
#include "../system/math.hpp"
#include <cstdint>
#include <limits>
#include "native_string.hpp"
#include "native_binary16.hpp"

#if HE_CPP_REAL_STORAGE_BITS == 16
using he_real_storage = he_binary16;
#elif HE_CPP_REAL_STORAGE_BITS == 32
using he_real_storage = float;
#elif HE_CPP_REAL_STORAGE_BITS == 64
using he_real_storage = double;
#else
#error "The selected Real storage representation is not implemented."
#endif
#if HE_CPP_REAL_ARITHMETIC_BITS == 16
#include "binary16_arithmetic.hpp"
using he_real_eval = he_binary16;
#elif HE_CPP_REAL_ARITHMETIC_BITS <= 32
using he_real_eval = float;
#elif HE_CPP_REAL_ARITHMETIC_BITS == 64
using he_real_eval = double;
#else
#error "The selected Real arithmetic representation is not implemented."
#endif

/// <summary>Expands a stored scalar at the configured arithmetic boundary.</summary>
inline he_real_eval he_real_load(he_real_storage value) { return static_cast<he_real_eval>(value); }
/// <summary>Rounds an arithmetic result only when it reaches configured storage.</summary>
inline he_real_storage he_real_store(he_real_eval value) { return static_cast<he_real_storage>(value); }

/// <summary>Evaluates sqrt through the selected-width math interface without a generated double promotion.</summary>
inline he_real_eval he_cpp_real_sqrt(he_real_eval value) {
#if HE_CPP_USE_STD_MATH
    return std::sqrt(he_cpp_real_plain_value(value));
#elif HE_CPP_REAL_ARITHMETIC_BITS <= 32
    return ::sqrtf(value);
#else
    return ::sqrt(value);
#endif
}

/// <summary>Evaluates sin through the selected-width math interface without a generated double promotion.</summary>
inline he_real_eval he_cpp_real_sin(he_real_eval value) {
#if HE_CPP_USE_STD_MATH
    return std::sin(he_cpp_real_plain_value(value));
#elif HE_CPP_REAL_ARITHMETIC_BITS <= 32
    return ::sinf(value);
#else
    return ::sin(value);
#endif
}

/// <summary>Evaluates cos through the selected-width math interface without a generated double promotion.</summary>
inline he_real_eval he_cpp_real_cos(he_real_eval value) {
#if HE_CPP_USE_STD_MATH
    return std::cos(he_cpp_real_plain_value(value));
#elif HE_CPP_REAL_ARITHMETIC_BITS <= 32
    return ::cosf(value);
#else
    return ::cos(value);
#endif
}

/// <summary>Evaluates atan2 through the selected-width math interface without a generated double promotion.</summary>
inline he_real_eval he_cpp_real_atan2(he_real_eval y, he_real_eval x) {
#if HE_CPP_USE_STD_MATH
    return std::atan2(he_cpp_real_plain_value(y), he_cpp_real_plain_value(x));
#elif HE_CPP_REAL_ARITHMETIC_BITS <= 32
    return ::atan2f(y, x);
#else
    return ::atan2(y, x);
#endif
}

/// <summary>Evaluates remainder through the selected-width math interface without a generated double promotion.</summary>
inline he_real_eval he_cpp_real_remainder(he_real_eval left, he_real_eval right) {
#if HE_CPP_USE_STD_MATH
    return std::fmod(he_cpp_real_plain_value(left), he_cpp_real_plain_value(right));
#elif HE_CPP_REAL_ARITHMETIC_BITS <= 32
    return ::fmodf(left, right);
#else
    return ::fmod(left, right);
#endif
}

/// <summary>Dispatches tan at the configured native arithmetic width.</summary>
inline he_real_eval he_cpp_real_tan(he_real_eval value) {
#if HE_CPP_USE_STD_MATH
    return std::tan(he_cpp_real_plain_value(value));
#elif HE_CPP_REAL_ARITHMETIC_BITS <= 32
    return ::tanf(value);
#else
    return ::tan(value);
#endif
}

/// <summary>Dispatches asin at the configured native arithmetic width.</summary>
inline he_real_eval he_cpp_real_asin(he_real_eval value) {
#if HE_CPP_USE_STD_MATH
    return std::asin(he_cpp_real_plain_value(value));
#elif HE_CPP_REAL_ARITHMETIC_BITS <= 32
    return ::asinf(value);
#else
    return ::asin(value);
#endif
}

/// <summary>Dispatches floor at the configured native arithmetic width.</summary>
inline he_real_eval he_cpp_real_floor(he_real_eval value) {
#if HE_CPP_USE_STD_MATH
    return std::floor(he_cpp_real_plain_value(value));
#elif HE_CPP_REAL_ARITHMETIC_BITS <= 32
    return ::floorf(value);
#else
    return ::floor(value);
#endif
}

/// <summary>Dispatches ceiling at the configured native arithmetic width.</summary>
inline he_real_eval he_cpp_real_ceiling(he_real_eval value) {
#if HE_CPP_USE_STD_MATH
    return std::ceil(he_cpp_real_plain_value(value));
#elif HE_CPP_REAL_ARITHMETIC_BITS <= 32
    return ::ceilf(value);
#else
    return ::ceil(value);
#endif
}

struct RealEvaluation;

/// <summary>Native engine scalar whose physical field matches the selected storage width.</summary>
struct Real {
    he_real_storage Value{};
    Real() = default;
    Real(he_real_storage value) : Value(value) {}
#if HE_CPP_REAL_STORAGE_BITS == 16
    /// <summary>Accepts a fixed primitive in one conversion, without chaining two user-defined constructors.</summary>
    template <typename T, std::enable_if_t<std::is_arithmetic_v<T>, int> = 0>
    Real(T value) : Value(value) {}
#endif
    Real(const RealEvaluation& value);
    static RealEvaluation op_Implicit_to_RealEvaluation(Real value);
    static Real op_Implicit_to_Real(RealEvaluation value);
    static Real op_Implicit_to_Real(double value) { return FromDouble(value); }
    explicit operator float() const { return static_cast<float>(Value); }
    explicit operator double() const { return static_cast<double>(Value); }
    explicit operator int32_t() const { return static_cast<int32_t>(Value); }
    HeCppString ToString() const { return String::ToJoinString(he_cpp_real_plain_value(Value)); }
    int32_t CompareTo(Real other) const { return Value < other.Value ? -1 : Value > other.Value ? 1 : Value == other.Value ? 0 : Value != Value ? (other.Value != other.Value ? 0 : -1) : 1; }
    static Real FromDouble(double value) { return Real(static_cast<he_real_storage>(value)); }
    double ToDouble() const { return static_cast<double>(Value); }
    float ToSingle() const { return static_cast<float>(Value); }
    Real& operator+=(RealEvaluation value);
    Real& operator-=(RealEvaluation value);
    Real& operator*=(RealEvaluation value);
    Real& operator/=(RealEvaluation value);
    bool Equals(Real other) const { return Value == other.Value || (Value != Value && other.Value != other.Value); }
    bool Equals(void* other) const { return other != nullptr && Equals(*static_cast<Real*>(other)); }
    int32_t GetHashCode() const { return Value != Value ? 0 : he_cpp_get_hash_code(he_cpp_real_plain_value(Value)); }
};

/// <summary>Native transient arithmetic value, kept separate from stored scalar conversions.</summary>
struct RealEvaluation {
    he_real_eval Value{};
    RealEvaluation() = default;
    RealEvaluation(he_real_eval value) : Value(value) {}
#if HE_CPP_REAL_ARITHMETIC_BITS == 16
    template <typename T, std::enable_if_t<std::is_arithmetic_v<T>, int> = 0>
    RealEvaluation(T value) : Value(value) {}
#endif
    RealEvaluation(Real value) : Value(he_real_load(value.Value)) {}
    static RealEvaluation op_Implicit_to_RealEvaluation(double value) { return RealEvaluation(static_cast<he_real_eval>(value)); }
    explicit operator float() const { return static_cast<float>(Value); }
    explicit operator double() const { return static_cast<double>(Value); }
    explicit operator int32_t() const { return static_cast<int32_t>(Value); }
    double ToDouble() const { return static_cast<double>(Value); }
    HeCppString ToString() const { return String::ToJoinString(he_cpp_real_plain_value(Value)); }
    static RealEvaluation get_MaxValue() { return RealEvaluation(std::numeric_limits<he_real_eval>::max()); }
    static RealEvaluation get_MinValue() { return RealEvaluation(-std::numeric_limits<he_real_eval>::max()); }
    static bool IsNaN(RealEvaluation value) { return value.Value != value.Value; }
    static bool IsInfinity(RealEvaluation value) { return value.Value == std::numeric_limits<he_real_eval>::infinity() || value.Value == -std::numeric_limits<he_real_eval>::infinity(); }
    static bool IsFinite(RealEvaluation value) { return !IsNaN(value) && !IsInfinity(value); }
    int32_t CompareTo(RealEvaluation other) const { return Value < other.Value ? -1 : Value > other.Value ? 1 : Value == other.Value ? 0 : Value != Value ? (other.Value != other.Value ? 0 : -1) : 1; }
    RealEvaluation& operator+=(RealEvaluation value);
    RealEvaluation& operator-=(RealEvaluation value);
    RealEvaluation& operator*=(RealEvaluation value);
    RealEvaluation& operator/=(RealEvaluation value);
    bool Equals(RealEvaluation other) const { return Value == other.Value || (Value != Value && other.Value != other.Value); }
    bool Equals(void* other) const { return other != nullptr && Equals(*static_cast<RealEvaluation*>(other)); }
    int32_t GetHashCode() const { return Value != Value ? 0 : he_cpp_get_hash_code(he_cpp_real_plain_value(Value)); }
};
inline Real::Real(const RealEvaluation& value) : Value(he_real_store(value.Value)) {}
/// <summary>Implements the converter's semantic user-conversion call without narrowing the loaded value.</summary>
inline RealEvaluation Real::op_Implicit_to_RealEvaluation(Real value) { return RealEvaluation(value); }
/// <summary>Implements the converter's semantic destination conversion at the explicit store boundary.</summary>
inline Real Real::op_Implicit_to_Real(RealEvaluation value) { return Real(value); }

/// <summary>Evaluates transient operands without passing through stored Real.</summary>
inline RealEvaluation operator+(RealEvaluation left, RealEvaluation right) { return RealEvaluation(static_cast<he_real_eval>(left.Value + right.Value)); }
/// <summary>Loads stored operands before performing configured arithmetic.</summary>
inline RealEvaluation operator+(Real left, Real right) { return RealEvaluation(left) + RealEvaluation(right); }
/// <summary>Loads only the stored operand of a mixed arithmetic expression.</summary>
inline RealEvaluation operator+(RealEvaluation left, Real right) { return left + RealEvaluation(right); }
/// <summary>Preserves the transient operand of a mixed arithmetic expression.</summary>
inline RealEvaluation operator+(Real left, RealEvaluation right) { return RealEvaluation(left) + right; }
/// <summary>Evaluates transient operands without passing through stored Real.</summary>
inline RealEvaluation operator-(RealEvaluation left, RealEvaluation right) { return RealEvaluation(static_cast<he_real_eval>(left.Value - right.Value)); }
/// <summary>Loads stored operands before performing configured arithmetic.</summary>
inline RealEvaluation operator-(Real left, Real right) { return RealEvaluation(left) - RealEvaluation(right); }
/// <summary>Loads only the stored operand of a mixed arithmetic expression.</summary>
inline RealEvaluation operator-(RealEvaluation left, Real right) { return left - RealEvaluation(right); }
/// <summary>Preserves the transient operand of a mixed arithmetic expression.</summary>
inline RealEvaluation operator-(Real left, RealEvaluation right) { return RealEvaluation(left) - right; }
/// <summary>Evaluates transient operands without passing through stored Real.</summary>
inline RealEvaluation operator*(RealEvaluation left, RealEvaluation right) { return RealEvaluation(static_cast<he_real_eval>(left.Value * right.Value)); }
/// <summary>Loads stored operands before performing configured arithmetic.</summary>
inline RealEvaluation operator*(Real left, Real right) { return RealEvaluation(left) * RealEvaluation(right); }
/// <summary>Loads only the stored operand of a mixed arithmetic expression.</summary>
inline RealEvaluation operator*(RealEvaluation left, Real right) { return left * RealEvaluation(right); }
/// <summary>Preserves the transient operand of a mixed arithmetic expression.</summary>
inline RealEvaluation operator*(Real left, RealEvaluation right) { return RealEvaluation(left) * right; }
/// <summary>Evaluates transient operands without passing through stored Real.</summary>
inline RealEvaluation operator/(RealEvaluation left, RealEvaluation right) { return RealEvaluation(static_cast<he_real_eval>(left.Value / right.Value)); }
/// <summary>Loads stored operands before performing configured arithmetic.</summary>
inline RealEvaluation operator/(Real left, Real right) { return RealEvaluation(left) / RealEvaluation(right); }
/// <summary>Loads only the stored operand of a mixed arithmetic expression.</summary>
inline RealEvaluation operator/(RealEvaluation left, Real right) { return left / RealEvaluation(right); }
/// <summary>Preserves the transient operand of a mixed arithmetic expression.</summary>
inline RealEvaluation operator/(Real left, RealEvaluation right) { return RealEvaluation(left) / right; }
/// <summary>Negates without creating a stored intermediate.</summary>
inline RealEvaluation operator-(RealEvaluation value) { return RealEvaluation(-value.Value); }
/// <summary>Loads a stored value before negation.</summary>
inline RealEvaluation operator-(Real value) { return -RealEvaluation(value); }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator==(Real left, Real right) { return left.Value == right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator!=(Real left, Real right) { return left.Value != right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator<(Real left, Real right) { return left.Value < right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator>(Real left, Real right) { return left.Value > right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator<=(Real left, Real right) { return left.Value <= right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator>=(Real left, Real right) { return left.Value >= right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator==(RealEvaluation left, RealEvaluation right) { return left.Value == right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator!=(RealEvaluation left, RealEvaluation right) { return left.Value != right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator<(RealEvaluation left, RealEvaluation right) { return left.Value < right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator>(RealEvaluation left, RealEvaluation right) { return left.Value > right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator<=(RealEvaluation left, RealEvaluation right) { return left.Value <= right.Value; }
/// <summary>Compares the selected IEEE representation without converting to a fixed external type.</summary>
inline bool operator>=(RealEvaluation left, RealEvaluation right) { return left.Value >= right.Value; }

/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator+(Real left, double right) { return static_cast<RealEvaluation>(left) + static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator+(double left, Real right) { return static_cast<RealEvaluation>(left) + static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator-(Real left, double right) { return static_cast<RealEvaluation>(left) - static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator-(double left, Real right) { return static_cast<RealEvaluation>(left) - static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator*(Real left, double right) { return static_cast<RealEvaluation>(left) * static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator*(double left, Real right) { return static_cast<RealEvaluation>(left) * static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator/(Real left, double right) { return static_cast<RealEvaluation>(left) / static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator/(double left, Real right) { return static_cast<RealEvaluation>(left) / static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator==(Real left, double right) { return static_cast<RealEvaluation>(left) == static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator==(double left, Real right) { return static_cast<RealEvaluation>(left) == static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator!=(Real left, double right) { return static_cast<RealEvaluation>(left) != static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator!=(double left, Real right) { return static_cast<RealEvaluation>(left) != static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator<(Real left, double right) { return static_cast<RealEvaluation>(left) < static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator<(double left, Real right) { return static_cast<RealEvaluation>(left) < static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator>(Real left, double right) { return static_cast<RealEvaluation>(left) > static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator>(double left, Real right) { return static_cast<RealEvaluation>(left) > static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator<=(Real left, double right) { return static_cast<RealEvaluation>(left) <= static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator<=(double left, Real right) { return static_cast<RealEvaluation>(left) <= static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator>=(Real left, double right) { return static_cast<RealEvaluation>(left) >= static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator>=(double left, Real right) { return static_cast<RealEvaluation>(left) >= static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator+(RealEvaluation left, double right) { return static_cast<RealEvaluation>(left) + static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator+(double left, RealEvaluation right) { return static_cast<RealEvaluation>(left) + static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator-(RealEvaluation left, double right) { return static_cast<RealEvaluation>(left) - static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator-(double left, RealEvaluation right) { return static_cast<RealEvaluation>(left) - static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator*(RealEvaluation left, double right) { return static_cast<RealEvaluation>(left) * static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator*(double left, RealEvaluation right) { return static_cast<RealEvaluation>(left) * static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator/(RealEvaluation left, double right) { return static_cast<RealEvaluation>(left) / static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline RealEvaluation operator/(double left, RealEvaluation right) { return static_cast<RealEvaluation>(left) / static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator==(RealEvaluation left, double right) { return static_cast<RealEvaluation>(left) == static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator==(double left, RealEvaluation right) { return static_cast<RealEvaluation>(left) == static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator!=(RealEvaluation left, double right) { return static_cast<RealEvaluation>(left) != static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator!=(double left, RealEvaluation right) { return static_cast<RealEvaluation>(left) != static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator<(RealEvaluation left, double right) { return static_cast<RealEvaluation>(left) < static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator<(double left, RealEvaluation right) { return static_cast<RealEvaluation>(left) < static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator>(RealEvaluation left, double right) { return static_cast<RealEvaluation>(left) > static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator>(double left, RealEvaluation right) { return static_cast<RealEvaluation>(left) > static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator<=(RealEvaluation left, double right) { return static_cast<RealEvaluation>(left) <= static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator<=(double left, RealEvaluation right) { return static_cast<RealEvaluation>(left) <= static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator>=(RealEvaluation left, double right) { return static_cast<RealEvaluation>(left) >= static_cast<RealEvaluation>(right); }
/// <summary>Loads a fixed primitive operand directly at arithmetic precision.</summary>
inline bool operator>=(double left, RealEvaluation right) { return static_cast<RealEvaluation>(left) >= static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator==(Real left, RealEvaluation right) { return static_cast<RealEvaluation>(left) == static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator==(RealEvaluation left, Real right) { return static_cast<RealEvaluation>(left) == static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator!=(Real left, RealEvaluation right) { return static_cast<RealEvaluation>(left) != static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator!=(RealEvaluation left, Real right) { return static_cast<RealEvaluation>(left) != static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator<(Real left, RealEvaluation right) { return static_cast<RealEvaluation>(left) < static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator<(RealEvaluation left, Real right) { return static_cast<RealEvaluation>(left) < static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator>(Real left, RealEvaluation right) { return static_cast<RealEvaluation>(left) > static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator>(RealEvaluation left, Real right) { return static_cast<RealEvaluation>(left) > static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator<=(Real left, RealEvaluation right) { return static_cast<RealEvaluation>(left) <= static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator<=(RealEvaluation left, Real right) { return static_cast<RealEvaluation>(left) <= static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator>=(Real left, RealEvaluation right) { return static_cast<RealEvaluation>(left) >= static_cast<RealEvaluation>(right); }
/// <summary>Compares mixed stored and evaluation values without narrowing.</summary>
inline bool operator>=(RealEvaluation left, Real right) { return static_cast<RealEvaluation>(left) >= static_cast<RealEvaluation>(right); }

inline Real& Real::operator+=(RealEvaluation value) { *this = static_cast<RealEvaluation>(*this) + value; return *this; }
inline Real& Real::operator-=(RealEvaluation value) { *this = static_cast<RealEvaluation>(*this) - value; return *this; }
inline Real& Real::operator*=(RealEvaluation value) { *this = static_cast<RealEvaluation>(*this) * value; return *this; }
inline Real& Real::operator/=(RealEvaluation value) { *this = static_cast<RealEvaluation>(*this) / value; return *this; }
inline RealEvaluation& RealEvaluation::operator+=(RealEvaluation value) { *this = static_cast<RealEvaluation>(*this) + value; return *this; }
inline RealEvaluation& RealEvaluation::operator-=(RealEvaluation value) { *this = static_cast<RealEvaluation>(*this) - value; return *this; }
inline RealEvaluation& RealEvaluation::operator*=(RealEvaluation value) { *this = static_cast<RealEvaluation>(*this) * value; return *this; }
inline RealEvaluation& RealEvaluation::operator/=(RealEvaluation value) { *this = static_cast<RealEvaluation>(*this) / value; return *this; }
/// <summary>Reads the selected IEEE sign bit without aliasing or assuming host byte order.</summary>
inline bool he_cpp_real_signbit(he_real_eval value) {
    using Bits = std::conditional_t<sizeof(he_real_eval) == sizeof(uint16_t), uint16_t,
        std::conditional_t<sizeof(he_real_eval) == sizeof(uint32_t), uint32_t, uint64_t>>;
    Bits bits{};
    const unsigned char* source = reinterpret_cast<const unsigned char*>(&value);
    unsigned char* destination = reinterpret_cast<unsigned char*>(&bits);
    for (unsigned int index = 0; index < sizeof(value); ++index) {
        destination[index] = source[index];
    }
    return (bits >> (sizeof(value) * 8 - 1)) != 0;
}

/// <summary>Engine configurable math with native selected-width primitives and evaluation intermediates.</summary>
struct RealMath {
    /// <summary>Bounds arithmetic values without introducing a stored scalar intermediate.</summary>
    static RealEvaluation Clamp(RealEvaluation value, RealEvaluation minimum, RealEvaluation maximum) {
        if (minimum > maximum) { he_cpp_raise(ArgumentException("The minimum cannot exceed the maximum.")); }
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }
    static RealEvaluation get_PI() { return RealEvaluation(static_cast<he_real_eval>(3.1415926535897932384626433832795)); }
    static RealEvaluation Tan(RealEvaluation value) { return RealEvaluation(he_cpp_real_tan(value.Value)); }
    static RealEvaluation Asin(RealEvaluation value) { return RealEvaluation(he_cpp_real_asin(value.Value)); }
    static RealEvaluation Abs(RealEvaluation value) { return value.Value == he_real_eval(0) ? RealEvaluation(0) : value.Value < he_real_eval(0) ? -value : value; }
    static bool IsPositiveInfinity(RealEvaluation value) { return value.Value == std::numeric_limits<he_real_eval>::infinity(); }
    static RealEvaluation Floor(RealEvaluation value) { return RealEvaluation(he_cpp_real_floor(value.Value)); }
    static RealEvaluation Ceiling(RealEvaluation value) { return RealEvaluation(he_cpp_real_ceiling(value.Value)); }
    static RealEvaluation Round(RealEvaluation value) { return Round(value, MidpointRounding::ToEven); }
    static RealEvaluation Round(RealEvaluation value, MidpointRounding mode) {
        if (!RealEvaluation::IsFinite(value) || value.Value == he_real_eval(0)) return value;
        he_real_eval lower = he_cpp_real_floor(value.Value);
        he_real_eval fraction = value.Value - lower;
        he_real_eval result = fraction < he_real_eval(0.5) ? lower : fraction > he_real_eval(0.5) ? lower + he_real_eval(1)
            : mode == MidpointRounding::AwayFromZero ? (value.Value < he_real_eval(0) ? lower : lower + he_real_eval(1))
            : he_cpp_real_remainder(lower, he_real_eval(2)) == he_real_eval(0) ? lower : lower + he_real_eval(1);
        return RealEvaluation(result == he_real_eval(0) && value.Value < he_real_eval(0) ? -he_real_eval(0) : result);
    }
    static RealEvaluation Min(RealEvaluation left, RealEvaluation right) { return left.Value == right.Value ? (he_cpp_real_signbit(left.Value) ? left : right) : left.Value < right.Value || left.Value != left.Value ? left : right; }
    static RealEvaluation Max(RealEvaluation left, RealEvaluation right) { return left.Value == right.Value ? (he_cpp_real_signbit(left.Value) ? right : left) : left.Value > right.Value || left.Value != left.Value ? left : right; }
    static int32_t Min(int32_t left, int32_t right) { return left < right ? left : right; }
    static int32_t Max(int32_t left, int32_t right) { return left > right ? left : right; }
    static int32_t Sign(RealEvaluation value) {
        if (RealEvaluation::IsNaN(value)) { he_cpp_raise(ArithmeticException()); }
        return value.Value < he_real_eval(0) ? -1 : value.Value > he_real_eval(0) ? 1 : 0;
    }
    static RealEvaluation Lerp(RealEvaluation start, RealEvaluation end, RealEvaluation amount) { return start + (end - start) * amount; }
    static RealEvaluation Sqrt(RealEvaluation value) { return RealEvaluation(he_cpp_real_sqrt(value.Value)); }
    static RealEvaluation Sin(RealEvaluation value) { return RealEvaluation(he_cpp_real_sin(value.Value)); }
    static RealEvaluation Cos(RealEvaluation value) { return RealEvaluation(he_cpp_real_cos(value.Value)); }
    static RealEvaluation Atan2(RealEvaluation y, RealEvaluation x) { return RealEvaluation(he_cpp_real_atan2(y.Value, x.Value)); }
    static RealEvaluation Remainder(RealEvaluation left, RealEvaluation right) { return RealEvaluation(he_cpp_real_remainder(left.Value, right.Value)); }
};
static_assert(sizeof(Real) * 8 == HE_CPP_REAL_STORAGE_BITS, "Real storage layout differs from the declared build contract.");
static_assert(sizeof(RealEvaluation) * 8 == HE_CPP_REAL_ARITHMETIC_BITS, "Real evaluation layout differs from the declared build contract.");
