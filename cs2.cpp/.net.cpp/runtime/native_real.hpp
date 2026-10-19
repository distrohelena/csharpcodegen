#pragma once
#include "../helcpp_config.hpp"
#include "native_hash.hpp"
#include "../system/math.hpp"
#include <cstdint>

#if HE_CPP_REAL_STORAGE_BITS == 32
using he_real_storage = float;
#elif HE_CPP_REAL_STORAGE_BITS == 64
using he_real_storage = double;
#else
#error "The selected Real storage representation is not implemented."
#endif
#if HE_CPP_REAL_ARITHMETIC_BITS == 32
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
    return std::sqrt(value);
#elif HE_CPP_REAL_ARITHMETIC_BITS == 32
    return ::sqrtf(value);
#else
    return ::sqrt(value);
#endif
}

/// <summary>Evaluates sin through the selected-width math interface without a generated double promotion.</summary>
inline he_real_eval he_cpp_real_sin(he_real_eval value) {
#if HE_CPP_USE_STD_MATH
    return std::sin(value);
#elif HE_CPP_REAL_ARITHMETIC_BITS == 32
    return ::sinf(value);
#else
    return ::sin(value);
#endif
}

/// <summary>Evaluates cos through the selected-width math interface without a generated double promotion.</summary>
inline he_real_eval he_cpp_real_cos(he_real_eval value) {
#if HE_CPP_USE_STD_MATH
    return std::cos(value);
#elif HE_CPP_REAL_ARITHMETIC_BITS == 32
    return ::cosf(value);
#else
    return ::cos(value);
#endif
}

/// <summary>Evaluates atan2 through the selected-width math interface without a generated double promotion.</summary>
inline he_real_eval he_cpp_real_atan2(he_real_eval y, he_real_eval x) {
#if HE_CPP_USE_STD_MATH
    return std::atan2(y, x);
#elif HE_CPP_REAL_ARITHMETIC_BITS == 32
    return ::atan2f(y, x);
#else
    return ::atan2(y, x);
#endif
}

/// <summary>Evaluates remainder through the selected-width math interface without a generated double promotion.</summary>
inline he_real_eval he_cpp_real_remainder(he_real_eval left, he_real_eval right) {
#if HE_CPP_USE_STD_MATH
    return std::fmod(left, right);
#elif HE_CPP_REAL_ARITHMETIC_BITS == 32
    return ::fmodf(left, right);
#else
    return ::fmod(left, right);
#endif
}

struct RealEvaluation;

/// <summary>Native engine scalar whose physical field matches the selected storage width.</summary>
struct Real {
    he_real_storage Value{};
    Real() = default;
    explicit Real(he_real_storage value) : Value(value) {}
    Real(const RealEvaluation& value);
    static RealEvaluation op_Implicit_to_RealEvaluation(Real value);
    static Real op_Implicit_to_Real(RealEvaluation value);
    static Real FromDouble(double value) { return Real(static_cast<he_real_storage>(value)); }
    double ToDouble() const { return static_cast<double>(Value); }
    float ToSingle() const { return static_cast<float>(Value); }
    bool Equals(Real other) const { return Value == other.Value || (Value != Value && other.Value != other.Value); }
    bool Equals(void* other) const { return other != nullptr && Equals(*static_cast<Real*>(other)); }
    int32_t GetHashCode() const { return Value != Value ? 0 : he_cpp_get_hash_code(Value); }
};

/// <summary>Native transient arithmetic value, kept separate from stored scalar conversions.</summary>
struct RealEvaluation {
    he_real_eval Value{};
    RealEvaluation() = default;
    explicit RealEvaluation(he_real_eval value) : Value(value) {}
    RealEvaluation(Real value) : Value(he_real_load(value.Value)) {}
    double ToDouble() const { return static_cast<double>(Value); }
    bool Equals(RealEvaluation other) const { return Value == other.Value || (Value != Value && other.Value != other.Value); }
    bool Equals(void* other) const { return other != nullptr && Equals(*static_cast<RealEvaluation*>(other)); }
    int32_t GetHashCode() const { return Value != Value ? 0 : he_cpp_get_hash_code(Value); }
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

/// <summary>Engine configurable math with native selected-width primitives and evaluation intermediates.</summary>
struct RealMath {
    static RealEvaluation Lerp(RealEvaluation start, RealEvaluation end, RealEvaluation amount) { return start + (end - start) * amount; }
    static RealEvaluation Sqrt(RealEvaluation value) { return RealEvaluation(he_cpp_real_sqrt(value.Value)); }
    static RealEvaluation Sin(RealEvaluation value) { return RealEvaluation(he_cpp_real_sin(value.Value)); }
    static RealEvaluation Cos(RealEvaluation value) { return RealEvaluation(he_cpp_real_cos(value.Value)); }
    static RealEvaluation Atan2(RealEvaluation y, RealEvaluation x) { return RealEvaluation(he_cpp_real_atan2(y.Value, x.Value)); }
    static RealEvaluation Remainder(RealEvaluation left, RealEvaluation right) { return RealEvaluation(he_cpp_real_remainder(left.Value, right.Value)); }
};
static_assert(sizeof(Real) * 8 == HE_CPP_REAL_STORAGE_BITS, "Real storage layout differs from the declared build contract.");
static_assert(sizeof(RealEvaluation) * 8 == HE_CPP_REAL_ARITHMETIC_BITS, "Real evaluation layout differs from the declared build contract.");
