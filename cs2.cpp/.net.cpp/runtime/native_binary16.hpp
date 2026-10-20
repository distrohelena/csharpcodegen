#pragma once
#include <cstdint>
#include <type_traits>

namespace he_cpp_binary16 {
    /// <summary>Reinterprets IEEE scalar bits without aliasing violations or hosted memcpy dependencies.</summary>
    template <typename To, typename From>
    inline To CopyBits(From value) {
        static_assert(sizeof(To) == sizeof(From));
        To result{};
        auto* destination = reinterpret_cast<unsigned char*>(&result);
        const auto* source = reinterpret_cast<const unsigned char*>(&value);
        for (unsigned index = 0; index < sizeof(To); ++index) { destination[index] = source[index]; }
        return result;
    }

    /// <summary>Rounds a shifted integer significand to nearest, choosing an even retained significand at ties.</summary>
    template <typename Word>
    inline Word RoundShiftEven(Word value, int shift) {
        Word rounded = value >> shift;
        const Word remainder = value & ((Word(1) << shift) - Word(1));
        const Word halfway = Word(1) << (shift - 1);
        if (remainder > halfway || (remainder == halfway && (rounded & Word(1)) != Word(0))) { ++rounded; }
        return rounded;
    }

    /// <summary>Converts either fixed float format directly into binary16, preserving signs and avoiding a second floating rounding.</summary>
    template <typename Word, int FractionBits, int ExponentBits, int Bias>
    inline std::uint16_t Encode(Word bits) {
        const auto sign = static_cast<std::uint16_t>((bits >> (FractionBits + ExponentBits - 15)) & Word(0x8000));
        const int exponent = static_cast<int>((bits >> FractionBits) & ((Word(1) << ExponentBits) - Word(1)));
        const Word fraction = bits & ((Word(1) << FractionBits) - Word(1));
        if (exponent == (1 << ExponentBits) - 1) {
            return static_cast<std::uint16_t>(sign | 0x7c00u | (fraction == 0 ? 0u : static_cast<unsigned>(fraction >> (FractionBits - 10)) | 0x0200u));
        }
        const int unbiased = exponent - Bias;
        if (unbiased > 15) { return static_cast<std::uint16_t>(sign | 0x7c00u); }
        if (unbiased < -25) { return sign; }
        const Word significand = fraction | (Word(1) << FractionBits);
        if (unbiased < -14) { return static_cast<std::uint16_t>(sign | RoundShiftEven(significand, FractionBits - 24 - unbiased)); }
        int halfExponent = unbiased + 15;
        Word rounded = RoundShiftEven(significand, FractionBits - 10);
        if (rounded == Word(0x0800)) { ++halfExponent; rounded = Word(0x0400); }
        if (halfExponent >= 31) { return static_cast<std::uint16_t>(sign | 0x7c00u); }
        return static_cast<std::uint16_t>(sign | (halfExponent << 10) | static_cast<unsigned>(rounded & Word(0x03ff)));
    }

    /// <summary>Expands every binary16 pattern exactly into binary32, including signed zero and subnormal values.</summary>
    inline float Decode(std::uint16_t bits) {
        const std::uint32_t sign = static_cast<std::uint32_t>(bits & 0x8000u) << 16;
        const std::uint32_t exponent = (bits >> 10) & 31u;
        std::uint32_t fraction = bits & 0x03ffu;
        std::uint32_t result;
        if (exponent == 31u) { result = sign | 0x7f800000u | (fraction << 13); }
        else if (exponent == 0u) {
            if (fraction == 0u) { result = sign; }
            else {
                int unbiased = -14;
                while ((fraction & 0x0400u) == 0u) { fraction <<= 1; --unbiased; }
                result = sign | (static_cast<std::uint32_t>(unbiased + 127) << 23) | ((fraction & 0x03ffu) << 13);
            }
        } else { result = sign | ((exponent + 112u) << 23) | (fraction << 13); }
        return CopyBits<float>(result);
    }
}

/// <summary>Owns exactly one IEEE binary16 encoding; arithmetic remains in the separately selected evaluation type.</summary>
struct he_binary16 {
    /// <summary>Stored IEEE encoding, with no retained wider copy.</summary>
    std::uint16_t Bits{};
    /// <summary>Constructs positive zero without conversion work.</summary>
    he_binary16() = default;
    /// <summary>Rounds a float expression once at the stored destination using only 32-bit bit operations.</summary>
    he_binary16(float value) : Bits(he_cpp_binary16::Encode<std::uint32_t, 23, 8, 127>(he_cpp_binary16::CopyBits<std::uint32_t>(value))) {}
    /// <summary>Rounds explicit fixed double input directly, preserving which side of a half midpoint it occupies.</summary>
    he_binary16(double value) : Bits(he_cpp_binary16::Encode<std::uint64_t, 52, 11, 1023>(he_cpp_binary16::CopyBits<std::uint64_t>(value))) {}
    /// <summary>Converts integral input through float; all integers in the finite half range are exact in binary32.</summary>
    template <typename T, std::enable_if_t<std::is_integral_v<T>, int> = 0>
    he_binary16(T value) : he_binary16(static_cast<float>(value)) {}
    /// <summary>Loads the value exactly into binary32 without introducing arithmetic or double helpers.</summary>
    operator float() const { return he_cpp_binary16::Decode(Bits); }
    /// <summary>Compares stored values by IEEE equality, preserving NaN and signed-zero semantics.</summary>
    bool operator==(he_binary16 right) const { return static_cast<float>(*this) == static_cast<float>(right); }
    /// <summary>Compares stored values by IEEE inequality, preserving NaN and signed-zero semantics.</summary>
    bool operator!=(he_binary16 right) const { return static_cast<float>(*this) != static_cast<float>(right); }
    /// <summary>Compares stored values by IEEE strict order, preserving NaN and signed-zero semantics.</summary>
    bool operator<(he_binary16 right) const { return static_cast<float>(*this) < static_cast<float>(right); }
    /// <summary>Compares stored values by IEEE reverse strict order, preserving NaN and signed-zero semantics.</summary>
    bool operator>(he_binary16 right) const { return static_cast<float>(*this) > static_cast<float>(right); }
    /// <summary>Compares stored values by IEEE non-strict order, preserving NaN and signed-zero semantics.</summary>
    bool operator<=(he_binary16 right) const { return static_cast<float>(*this) <= static_cast<float>(right); }
    /// <summary>Compares stored values by IEEE reverse non-strict order, preserving NaN and signed-zero semantics.</summary>
    bool operator>=(he_binary16 right) const { return static_cast<float>(*this) >= static_cast<float>(right); }
    /// <summary>Builds an owned value from an already encoded pattern without numeric conversion.</summary>
    static he_binary16 FromBits(std::uint16_t bits) { he_binary16 result; result.Bits = bits; return result; }
};
static_assert(sizeof(he_binary16) == 2 && alignof(he_binary16) == 2);

/// <summary>Leaves ordinary storage unchanged for fixed primitive formatting and hashing.</summary>
template <typename T>
inline T he_cpp_real_plain_value(T value) { return value; }
/// <summary>Expands half storage before using fixed primitive formatting or hashing contracts.</summary>
inline float he_cpp_real_plain_value(he_binary16 value) { return static_cast<float>(value); }
