// Software implementations of the C math names system/math.hpp calls when HE_CPP_USE_STD_MATH is 0.
//
// The runner and the generated-output build copy every runtime .cpp into every output tree, hosted
// ones included, so this file must define no symbols at all on a hosted build: there the platform
// libm owns sin, cos, ... and a second definition would collide. freestanding_math.hpp reports that
// by defining HE_CPP_FREESTANDING_MATH_HOSTED (a usable <math.h> and not the llvm-mos 65816
// toolchain, whose libm supplies only floorf), which is the case for MSVC and for every host
// compiler the C# compile tests use, whatever the copied helcpp_config.hpp selects. The profile is
// probed the same way freestanding_hooks_default.cpp probes it so a freestanding runtime profile or
// an explicit HE_CPP_FREESTANDING_MATH_SOFTWARE request (the accuracy smoke sets that one on the
// command line for both of its translation units) still builds the software routines.
#if defined(__has_include)
#if __has_include("helcpp_config.hpp")
#include "helcpp_config.hpp"
#endif
#endif

#include "freestanding_math.hpp"

#if !defined(HE_CPP_FREESTANDING_MATH_HOSTED) \
    || (defined(HE_CPP_RUNTIME_FREESTANDING) && HE_CPP_RUNTIME_FREESTANDING) \
    || defined(HE_CPP_FREESTANDING_MATH_SOFTWARE)

#include <stdint.h>
#include <string.h>

namespace he_cpp_freestanding_math {
namespace {

constexpr uint64_t SignMask = 0x8000000000000000ULL;
constexpr uint64_t MagnitudeMask = 0x7FFFFFFFFFFFFFFFULL;
constexpr uint64_t MantissaMask = 0x000FFFFFFFFFFFFFULL;
constexpr uint64_t InfinityBits = 0x7FF0000000000000ULL;
constexpr uint64_t QuietNanBits = 0x7FF8000000000000ULL;
// Halving the biased exponent in place: (bits >> 1) halves the unbiased exponent and this addend
// puts the bias back, which seeds Newton iteration within about 6 percent of the square root.
constexpr uint64_t SquareRootSeed = 0x1FF8000000000000ULL;
constexpr int32_t ExponentBias = 1023;
constexpr int32_t ExponentFieldAllOnes = 0x7FF;

// 2 pi and pi / 2 as a rounded double plus the part that does not fit, so the multiples subtracted
// during argument reduction carry the accuracy of a constant with more than 53 bits.
constexpr double TwoPiHigh = 6.283185307179586;
constexpr double TwoPiLow = 2.4492935982947064e-16;
constexpr double HalfPiHigh = 1.5707963267948966;
constexpr double HalfPiLow = 6.123233995736766e-17;
constexpr double InverseTwoPi = 0.15915494309189535;
constexpr double TwoOverPi = 0.6366197723675814;
constexpr double Pi = 3.141592653589793;
constexpr double QuarterPi = 0.7853981633974483;
constexpr double ThreeQuartersPi = 2.356194490192345;
constexpr double PiOverSix = 0.5235987755982988;
constexpr double SquareRootOfThree = 1.7320508075688772;
constexpr double TangentOfFifteenDegrees = 0.2679491924311227;
constexpr double SquareRootOfTwo = 1.4142135623730951;
constexpr double Ln2High = 0.6931471805599453;
constexpr double Ln2Low = 2.3190468138462996e-17;
constexpr double TwoPower54 = 18014398509481984.0;
constexpr double TwoPowerMinus27 = 7.450580596923828e-9;
constexpr double TwoPower1000 = 1.0715086071862673e301;
constexpr double TwoPowerMinus1000 = 9.332636185032189e-302;

/// <summary>Reads the IEEE 754 bit pattern of one double.</summary>
uint64_t ToBits(double value) {
    uint64_t bits = 0;
    memcpy(&bits, &value, sizeof bits);
    return bits;
}

/// <summary>Rebuilds one double from an IEEE 754 bit pattern.</summary>
double FromBits(uint64_t bits) {
    double value = 0.0;
    memcpy(&value, &bits, sizeof value);
    return value;
}

/// <summary>Returns a quiet not-a-number without relying on a hosted math header.</summary>
double QuietNan() {
    return FromBits(QuietNanBits);
}

/// <summary>Returns negative infinity.</summary>
double NegativeInfinity() {
    return FromBits(InfinityBits | SignMask);
}

/// <summary>Reports whether one value is not-a-number.</summary>
bool IsNan(double value) {
    return (ToBits(value) & MagnitudeMask) > InfinityBits;
}

/// <summary>Reports whether the sign bit is set, which separates negative zero from positive zero.</summary>
bool IsNegative(double value) {
    return (ToBits(value) & SignMask) != 0;
}

/// <summary>Returns the raw biased exponent field, zero for zero and for subnormal values.</summary>
int32_t RawExponent(double value) {
    return static_cast<int32_t>((ToBits(value) >> 52) & static_cast<uint64_t>(ExponentFieldAllOnes));
}

/// <summary>Returns the unbiased exponent of a finite non-zero magnitude, subnormals included.</summary>
int32_t UnbiasedExponent(double value) {
    const int32_t field = RawExponent(value);
    if (field != 0) {
        return field - ExponentBias;
    }
    uint64_t mantissa = ToBits(value) & MantissaMask;
    int32_t exponent = -1022;
    while ((mantissa & 0x0010000000000000ULL) == 0) {
        mantissa <<= 1;
        exponent -= 1;
    }
    return exponent;
}

/// <summary>Multiplies by a power of two, which is exact unless the result overflows or underflows.</summary>
double ScaleByPowerOfTwo(double value, int32_t exponent) {
    while (exponent > 1000) {
        value *= TwoPower1000;
        exponent -= 1000;
    }
    while (exponent < -1000) {
        value *= TwoPowerMinus1000;
        exponent += 1000;
    }
    return value * FromBits(static_cast<uint64_t>(exponent + ExponentBias) << 52);
}

}

/// <summary>Returns the magnitude of one value by clearing the sign bit.</summary>
double Fabs(double value) {
    return FromBits(ToBits(value) & MagnitudeMask);
}

/// <summary>Rounds toward negative infinity, exactly, for every magnitude.</summary>
double Floor(double value) {
    const uint64_t bits = ToBits(value);
    const int32_t exponent = RawExponent(value) - ExponentBias;
    if (exponent >= 52) {
        // Already integral: every magnitude at or above 2^52, and the infinities and the nans.
        return value;
    }
    if (exponent < 0) {
        if (value == 0.0) {
            return value;
        }
        return (bits & SignMask) != 0 ? -1.0 : 0.0;
    }
    const uint64_t fraction = MantissaMask >> exponent;
    if ((bits & fraction) == 0) {
        return value;
    }
    const double truncated = FromBits(bits & ~fraction);
    return (bits & SignMask) != 0 ? truncated - 1.0 : truncated;
}

/// <summary>Rounds toward positive infinity, exactly, for every magnitude.</summary>
double Ceil(double value) {
    const uint64_t bits = ToBits(value);
    const int32_t exponent = RawExponent(value) - ExponentBias;
    if (exponent >= 52) {
        return value;
    }
    if (exponent < 0) {
        if (value == 0.0) {
            return value;
        }
        return (bits & SignMask) != 0 ? -0.0 : 1.0;
    }
    const uint64_t fraction = MantissaMask >> exponent;
    if ((bits & fraction) == 0) {
        return value;
    }
    const double truncated = FromBits(bits & ~fraction);
    return (bits & SignMask) != 0 ? truncated : truncated + 1.0;
}

/// <summary>Returns 1 unless the exponent field is all ones, which marks the infinities and nans.</summary>
int Finite(double value) {
    return RawExponent(value) != ExponentFieldAllOnes ? 1 : 0;
}

/// <summary>
/// Returns the square root. The exponent is halved through the bit pattern for a seed within about
/// 6 percent, which four Newton steps refine to the last bit or two.
/// </summary>
double Sqrt(double value) {
    if (IsNan(value)) {
        return value;
    }
    if (value == 0.0) {
        return value;
    }
    if (IsNegative(value)) {
        return QuietNan();
    }
    if (Finite(value) == 0) {
        return value;
    }
    double scale = 1.0;
    if (RawExponent(value) == 0) {
        // Subnormal: 2^54 lifts it into the normal range and 2^-27 takes the square root back down.
        value *= TwoPower54;
        scale = TwoPowerMinus27;
    }
    double estimate = FromBits((ToBits(value) >> 1) + SquareRootSeed);
    estimate = 0.5 * (estimate + value / estimate);
    estimate = 0.5 * (estimate + value / estimate);
    estimate = 0.5 * (estimate + value / estimate);
    estimate = 0.5 * (estimate + value / estimate);
    return scale * estimate;
}

namespace {

/// <summary>The sine Taylor series through the thirteenth power, for arguments in [-pi/4, pi/4].</summary>
double SineCore(double value) {
    const double square = value * value;
    const double series =
        -0.16666666666666666 +
        square * (0.008333333333333333 +
        square * (-1.984126984126984e-4 +
        square * (2.7557319223985893e-6 +
        square * (-2.505210838544172e-8 +
        square * 1.6059043836821613e-10))));
    return value + value * square * series;
}

/// <summary>The cosine Taylor series through the fourteenth power, for arguments in [-pi/4, pi/4].</summary>
double CosineCore(double value) {
    const double square = value * value;
    const double series =
        -0.5 +
        square * (0.041666666666666664 +
        square * (-0.001388888888888889 +
        square * (2.48015873015873e-5 +
        square * (-2.755731922398589e-7 +
        square * (2.08767569878681e-9 +
        square * (-1.1470745597729725e-11))))));
    return 1.0 + square * series;
}

/// <summary>One angle reduced into [-pi/4, pi/4] together with the quadrant it came from.</summary>
struct ReducedAngle {
    double remainder;
    int32_t quadrant;
};

/// <summary>
/// Subtracts the nearest multiple of 2 pi and then the nearest multiple of pi / 2, each in two
/// pieces so the constant carries more than 53 bits, leaving a remainder in [-pi/4, pi/4]. The
/// quadrant is wrapped in floating point rather than through an integer cast, which keeps the
/// conversion in range for every argument the callers below let through.
/// </summary>
ReducedAngle ReduceAngle(double value) {
    const double turns = Floor(value * InverseTwoPi + 0.5);
    double remainder = (value - turns * TwoPiHigh) - turns * TwoPiLow;
    const double quarters = Floor(remainder * TwoOverPi + 0.5);
    remainder = (remainder - quarters * HalfPiHigh) - quarters * HalfPiLow;
    const double wrapped = quarters - 4.0 * Floor(quarters * 0.25);
    ReducedAngle reduced;
    reduced.remainder = remainder;
    reduced.quadrant = static_cast<int32_t>(wrapped);
    return reduced;
}

/// <summary>
/// Reports whether an angle can still be reduced usefully. Above 2^52 consecutive doubles are more
/// than one radian apart, so the argument carries no information about where in its period it sits
/// and there is no multi-word pi here to recover it: the trigonometric functions answer nan rather
/// than a confident wrong number. Below that the reduction stays in range but its error grows with
/// the magnitude of the angle, from about 2e-14 near 100 radians to about 2e-12 near 20000.
/// </summary>
bool IsReducibleAngle(double value) {
    return Fabs(value) < 4503599627370496.0;
}

/// <summary>The arc tangent Taylor series, for arguments no larger than tan(15 degrees).</summary>
double ArcTangentSeries(double value) {
    const double square = value * value;
    const double series =
        -0.3333333333333333 +
        square * (0.2 +
        square * (-0.14285714285714285 +
        square * (0.1111111111111111 +
        square * (-0.09090909090909091 +
        square * (0.07692307692307693 +
        square * (-0.06666666666666667 +
        square * (0.058823529411764705 +
        square * (-0.05263157894736842 +
        square * 0.047619047619047616))))))));
    return value + value * square * series;
}

/// <summary>
/// The arc tangent of a value in [0, 1]. Anything above tan(15 degrees) is folded down with
/// atan(t) = pi / 6 + atan((t * sqrt3 - 1) / (t + sqrt3)) so the series always converges quickly.
/// </summary>
double ArcTangentUnit(double value) {
    if (value > TangentOfFifteenDegrees) {
        const double folded = (value * SquareRootOfThree - 1.0) / (value + SquareRootOfThree);
        return PiOverSix + ArcTangentSeries(folded);
    }
    return ArcTangentSeries(value);
}

}

/// <summary>Returns the sine, reducing the argument and selecting the series by quadrant.</summary>
double Sin(double value) {
    if (Finite(value) == 0 || !IsReducibleAngle(value)) {
        return QuietNan();
    }
    const ReducedAngle reduced = ReduceAngle(value);
    switch (reduced.quadrant) {
        case 0: return SineCore(reduced.remainder);
        case 1: return CosineCore(reduced.remainder);
        case 2: return -SineCore(reduced.remainder);
        default: return -CosineCore(reduced.remainder);
    }
}

/// <summary>Returns the cosine, reducing the argument and selecting the series by quadrant.</summary>
double Cos(double value) {
    if (Finite(value) == 0 || !IsReducibleAngle(value)) {
        return QuietNan();
    }
    const ReducedAngle reduced = ReduceAngle(value);
    switch (reduced.quadrant) {
        case 0: return CosineCore(reduced.remainder);
        case 1: return -SineCore(reduced.remainder);
        case 2: return -CosineCore(reduced.remainder);
        default: return SineCore(reduced.remainder);
    }
}

/// <summary>Returns the tangent as the quotient of the sine and the cosine.</summary>
double Tan(double value) {
    if (Finite(value) == 0 || !IsReducibleAngle(value)) {
        return QuietNan();
    }
    return Sin(value) / Cos(value);
}

/// <summary>Returns the angle of the point (x, y), with the IEEE results for zero and infinity.</summary>
double Atan2(double y, double x) {
    if (IsNan(y) || IsNan(x)) {
        return QuietNan();
    }
    const bool negativeY = IsNegative(y);
    const bool negativeX = IsNegative(x);
    if (Finite(y) == 0) {
        if (Finite(x) == 0) {
            const double diagonal = negativeX ? ThreeQuartersPi : QuarterPi;
            return negativeY ? -diagonal : diagonal;
        }
        return negativeY ? -HalfPiHigh : HalfPiHigh;
    }
    if (Finite(x) == 0) {
        if (negativeX) {
            return negativeY ? -Pi : Pi;
        }
        return negativeY ? -0.0 : 0.0;
    }
    if (x == 0.0) {
        if (y != 0.0) {
            return negativeY ? -HalfPiHigh : HalfPiHigh;
        }
        if (negativeX) {
            return negativeY ? -Pi : Pi;
        }
        return negativeY ? -0.0 : 0.0;
    }
    const double ratio = Fabs(y / x);
    double angle = ratio > 1.0
        ? (HalfPiHigh - ArcTangentUnit(1.0 / ratio)) + HalfPiLow
        : ArcTangentUnit(ratio);
    if (negativeX) {
        angle = Pi - angle;
    }
    return negativeY ? -angle : angle;
}

/// <summary>Returns the arc sine through atan2(x, sqrt(1 - x * x)), which is nan outside [-1, 1].</summary>
double Asin(double value) {
    return Atan2(value, Sqrt(1.0 - value * value));
}

/// <summary>Returns the arc cosine as pi / 2 minus the arc sine.</summary>
double Acos(double value) {
    return HalfPiHigh - Asin(value);
}

/// <summary>
/// Returns the natural logarithm. The value splits into m * 2^e through the bit pattern, m is
/// brought into [sqrt(0.5), sqrt(2)) and log(m) comes from the series in s = (m - 1) / (m + 1).
/// </summary>
double Log(double value) {
    if (IsNan(value)) {
        return value;
    }
    if (value == 0.0) {
        return NegativeInfinity();
    }
    if (IsNegative(value)) {
        return QuietNan();
    }
    if (Finite(value) == 0) {
        return value;
    }
    int32_t exponent = 0;
    if (RawExponent(value) == 0) {
        value *= TwoPower54;
        exponent = -54;
    }
    exponent += RawExponent(value) - ExponentBias;
    double mantissa = FromBits((ToBits(value) & MantissaMask) | (static_cast<uint64_t>(ExponentBias) << 52));
    if (mantissa > SquareRootOfTwo) {
        mantissa *= 0.5;
        exponent += 1;
    }
    const double s = (mantissa - 1.0) / (mantissa + 1.0);
    const double square = s * s;
    const double series = s * (2.0 +
        square * (0.6666666666666666 +
        square * (0.4 +
        square * (0.2857142857142857 +
        square * (0.2222222222222222 +
        square * (0.18181818181818182 +
        square * (0.15384615384615385 +
        square * (0.13333333333333333 +
        square * 0.11764705882352941))))))));
    const double scaled = static_cast<double>(exponent);
    return scaled * Ln2High + (series + scaled * Ln2Low);
}

/// <summary>Returns the base-2 logarithm as the natural logarithm divided by ln 2.</summary>
double Log2(double value) {
    return Log(value) / Ln2High;
}

/// <summary>
/// Returns the remainder of value / divisor exactly. The divisor is scaled by a power of two up to
/// the magnitude of the running remainder and subtracted, which loses nothing because the two
/// operands then share a binade.
/// </summary>
double Fmod(double value, double divisor) {
    if (IsNan(value) || IsNan(divisor) || Finite(value) == 0 || divisor == 0.0) {
        return QuietNan();
    }
    if (Finite(divisor) == 0) {
        return value;
    }
    double remainder = Fabs(value);
    const double magnitude = Fabs(divisor);
    if (remainder < magnitude) {
        return value;
    }
    const int32_t divisorExponent = UnbiasedExponent(magnitude);
    while (remainder >= magnitude) {
        const int32_t shift = UnbiasedExponent(remainder) - divisorExponent;
        double scaled = ScaleByPowerOfTwo(magnitude, shift);
        if (scaled > remainder) {
            scaled = ScaleByPowerOfTwo(magnitude, shift - 1);
        }
        remainder -= scaled;
    }
    return IsNegative(value) ? -remainder : remainder;
}

}

// The C names are defined exactly when the header declared them: a target with no usable libm.
#if !defined(HE_CPP_FREESTANDING_MATH_HOSTED) \
    && (!defined(HE_CPP_FREESTANDING_MATH_SOFTWARE) || defined(__mos__))

extern "C" {

double ceil(double value) { return he_cpp_freestanding_math::Ceil(value); }
double floor(double value) { return he_cpp_freestanding_math::Floor(value); }
double fabs(double value) { return he_cpp_freestanding_math::Fabs(value); }
double acos(double value) { return he_cpp_freestanding_math::Acos(value); }
double asin(double value) { return he_cpp_freestanding_math::Asin(value); }
double sin(double value) { return he_cpp_freestanding_math::Sin(value); }
double cos(double value) { return he_cpp_freestanding_math::Cos(value); }
double tan(double value) { return he_cpp_freestanding_math::Tan(value); }
double sqrt(double value) { return he_cpp_freestanding_math::Sqrt(value); }
double log(double value) { return he_cpp_freestanding_math::Log(value); }
double log2(double value) { return he_cpp_freestanding_math::Log2(value); }
double fmod(double value, double divisor) { return he_cpp_freestanding_math::Fmod(value, divisor); }
double atan2(double y, double x) { return he_cpp_freestanding_math::Atan2(y, x); }
int finite(double value) { return he_cpp_freestanding_math::Finite(value); }

}

#endif

#endif
