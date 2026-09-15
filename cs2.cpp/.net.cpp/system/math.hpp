#ifndef MATH_HPP
#define MATH_HPP

#include "../runtime/native_runtime.hpp"
#include "../runtime/native_exceptions.hpp"
#include <type_traits>

#ifndef HE_CPP_USE_STD_MATH
#define HE_CPP_USE_STD_MATH 1
#endif

#if HE_CPP_USE_STD_MATH
#include <cmath>
#else
#if !defined(HE_CPP_RUNTIME_MATH_HEADER)
#error "A custom math header is required when standard math is disabled. Define HE_CPP_RUNTIME_MATH_HEADER to a header that supplies the selected C math functions."
#endif
#include HE_CPP_RUNTIME_MATH_HEADER
#endif

namespace he_cpp_math_detail {

/// <summary>Rounds one value toward positive infinity through the selected math provider.</summary>
inline double Ceiling(double value) {
#if HE_CPP_USE_STD_MATH
    return std::ceil(value);
#else
    return ::ceil(value);
#endif
}

/// <summary>Rounds one value toward negative infinity through the selected math provider.</summary>
inline double Floor(double value) {
#if HE_CPP_USE_STD_MATH
    return std::floor(value);
#else
    return ::floor(value);
#endif
}

/// <summary>Returns the absolute value through the selected math provider.</summary>
inline double Absolute(double value) {
#if HE_CPP_USE_STD_MATH
    return std::fabs(value);
#else
    return ::fabs(value);
#endif
}

/// <summary>Returns the inverse cosine through the selected math provider.</summary>
inline double ArcCosine(double value) {
#if HE_CPP_USE_STD_MATH
    return std::acos(value);
#else
    return ::acos(value);
#endif
}

/// <summary>Returns the inverse sine through the selected math provider.</summary>
inline double ArcSine(double value) {
#if HE_CPP_USE_STD_MATH
    return std::asin(value);
#else
    return ::asin(value);
#endif
}

/// <summary>Returns the sine through the selected math provider.</summary>
inline double Sine(double value) {
#if HE_CPP_USE_STD_MATH
    return std::sin(value);
#else
    return ::sin(value);
#endif
}

/// <summary>Returns the cosine through the selected math provider.</summary>
inline double Cosine(double value) {
#if HE_CPP_USE_STD_MATH
    return std::cos(value);
#else
    return ::cos(value);
#endif
}

/// <summary>Returns the square root through the selected math provider.</summary>
inline double SquareRoot(double value) {
#if HE_CPP_USE_STD_MATH
    return std::sqrt(value);
#else
    return ::sqrt(value);
#endif
}

/// <summary>Returns the base-two logarithm through the selected math provider.</summary>
inline double LogarithmBaseTwo(double value) {
#if HE_CPP_USE_STD_MATH
    return std::log2(value);
#else
    return ::log2(value);
#endif
}

/// <summary>Returns the remainder used to determine an exact even integral value.</summary>
inline double Remainder(double value, double divisor) {
#if HE_CPP_USE_STD_MATH
    return std::fmod(value, divisor);
#else
    return ::fmod(value, divisor);
#endif
}

/// <summary>Tests whether a value is finite through the selected math provider.</summary>
inline bool IsFinite(double value) {
#if HE_CPP_USE_STD_MATH
    return std::isfinite(value);
#else
    return ::finite(value) != 0;
#endif
}

/// <summary>Computes managed midpoint-to-even rounding without hosted nearbyint.</summary>
inline double RoundToEven(double value) {
#if HE_CPP_USE_STD_MATH
    return std::nearbyint(value);
#else
    if (!IsFinite(value)) {
        return value;
    }

    if (value >= 0.0) {
        const double lower = Floor(value);
        const double fraction = value - lower;
        if (fraction < 0.5) {
            return lower;
        }
        if (fraction > 0.5) {
            return lower + 1.0;
        }
        return Remainder(lower, 2.0) == 0.0 ? lower : lower + 1.0;
    }

    const double upper = Ceiling(value);
    const double fraction = upper - value;
    if (fraction < 0.5) {
        return upper;
    }
    if (fraction > 0.5) {
        return upper - 1.0;
    }
    return Remainder(upper, 2.0) == 0.0 ? upper : upper - 1.0;
#endif
}

/// <summary>Returns the two-argument arctangent through the selected math provider.</summary>
inline double ArcTangent2(double y, double x) {
#if HE_CPP_USE_STD_MATH
    return std::atan2(y, x);
#else
    return ::atan2(y, x);
#endif
}

}

/// <summary>Identifies the midpoint rule used by managed rounding helpers.</summary>
enum class MidpointRounding {
    ToEven,
    AwayFromZero
};

/// <summary>Provides managed double-precision math operations through the selected runtime provider.</summary>
class Math {
public:
    inline static constexpr double PI = 3.14159265358979323846;

    template <typename TValue, typename TMin, typename TMax>
    static std::common_type_t<TValue, TMin, TMax> Clamp(TValue value, TMin minValue, TMax maxValue) {
        using TResult = std::common_type_t<TValue, TMin, TMax>;
        const TResult candidate = static_cast<TResult>(value);
        const TResult minimum = static_cast<TResult>(minValue);
        const TResult maximum = static_cast<TResult>(maxValue);
        return candidate < minimum ? minimum : maximum < candidate ? maximum : candidate;
    }

    template <typename TValue>
    static double Ceiling(TValue value) {
        return he_cpp_math_detail::Ceiling(static_cast<double>(value));
    }

    template <typename TValue>
    static double Floor(TValue value) {
        return he_cpp_math_detail::Floor(static_cast<double>(value));
    }

    template <typename TValue>
    static TValue Max(TValue left, TValue right) {
        return left < right ? right : left;
    }

    template <typename TLeft, typename TRight>
    static std::common_type_t<TLeft, TRight> Max(TLeft left, TRight right) {
        using TResult = std::common_type_t<TLeft, TRight>;
        const TResult convertedLeft = static_cast<TResult>(left);
        const TResult convertedRight = static_cast<TResult>(right);
        return convertedLeft < convertedRight ? convertedRight : convertedLeft;
    }

    template <typename TValue>
    static TValue Min(TValue left, TValue right) {
        return right < left ? right : left;
    }

    template <typename TLeft, typename TRight>
    static std::common_type_t<TLeft, TRight> Min(TLeft left, TRight right) {
        using TResult = std::common_type_t<TLeft, TRight>;
        const TResult convertedLeft = static_cast<TResult>(left);
        const TResult convertedRight = static_cast<TResult>(right);
        return convertedRight < convertedLeft ? convertedRight : convertedLeft;
    }

    template <typename TValue>
    static TValue MinMagnitude(TValue left, TValue right) {
        TValue leftMagnitude = static_cast<TValue>(Abs(left));
        TValue rightMagnitude = static_cast<TValue>(Abs(right));
        if (leftMagnitude < rightMagnitude) {
            return left;
        }

        if (rightMagnitude < leftMagnitude) {
            return right;
        }

        return Min(left, right);
    }

    template <typename TValue>
    static double Abs(TValue value) {
        return he_cpp_math_detail::Absolute(static_cast<double>(value));
    }

    template <typename TValue>
    static double Acos(TValue value) {
        return he_cpp_math_detail::ArcCosine(static_cast<double>(value));
    }

    /// <summary>Returns inverse sine through the selected mathematical provider.</summary>
    template <typename TValue>
    static double Asin(TValue value) {
        return he_cpp_math_detail::ArcSine(static_cast<double>(value));
    }

    /// <summary>Returns minus one, zero or one; NaN follows the configured failure policy.</summary>
    template <typename TValue>
    static int32_t Sign(TValue value) {
        if constexpr (std::is_floating_point_v<TValue>) {
            if (value != value) { he_cpp_raise(ArithmeticException()); }
        }
        return (value > static_cast<TValue>(0)) - (value < static_cast<TValue>(0));
    }
    template <typename TValue>
    static double Round(TValue value) {
        return he_cpp_math_detail::RoundToEven(static_cast<double>(value));
    }

    template <typename TValue>
    static double Round(TValue value, MidpointRounding midpointRounding) {
        const double promotedValue = static_cast<double>(value);

        switch (midpointRounding) {
            case MidpointRounding::AwayFromZero:
                return promotedValue >= 0.0
                    ? he_cpp_math_detail::Floor(promotedValue + 0.5)
                    : he_cpp_math_detail::Ceiling(promotedValue - 0.5);
            default:
                return he_cpp_math_detail::RoundToEven(promotedValue);
        }
    }

    template <typename TValue>
    static double Sin(TValue value) {
        return he_cpp_math_detail::Sine(static_cast<double>(value));
    }

    template <typename TValue>
    static double Cos(TValue value) {
        return he_cpp_math_detail::Cosine(static_cast<double>(value));
    }

    template <typename TValue>
    static double Sqrt(TValue value) {
        return he_cpp_math_detail::SquareRoot(static_cast<double>(value));
    }

    template <typename TValue>
    static double Log2(TValue value) {
        return he_cpp_math_detail::LogarithmBaseTwo(static_cast<double>(value));
    }

    template <typename TValue>
    static bool IsFinite(TValue value) {
        return he_cpp_math_detail::IsFinite(static_cast<double>(value));
    }

    template <typename TValue>
    static double Tan(TValue value) {
#if HE_CPP_USE_STD_MATH
        return std::tan(static_cast<double>(value));
#else
        return ::tan(static_cast<double>(value));
#endif
    }

    template <typename TY, typename TX>
    static double Atan2(TY y, TX x) {
        return he_cpp_math_detail::ArcTangent2(static_cast<double>(y), static_cast<double>(x));
    }
};

/// <summary>Provides managed single-precision math operations backed by the double-precision provider.</summary>
class MathF {
public:
    /// <summary>Returns inverse sine rounded to single precision.</summary>
    template <typename TValue>
    static float Asin(TValue value) { return static_cast<float>(Math::Asin(value)); }

    /// <summary>Returns the sign using the shared managed arithmetic rules.</summary>
    template <typename TValue>
    static int32_t Sign(TValue value) { return Math::Sign(value); }
    template <typename TValue, typename TMin, typename TMax>
    static float Clamp(TValue value, TMin minValue, TMax maxValue) {
        return static_cast<float>(Math::Clamp(value, minValue, maxValue));
    }

    template <typename TValue>
    static float Ceiling(TValue value) {
        return static_cast<float>(Math::Ceiling(value));
    }

    template <typename TValue>
    static float Floor(TValue value) {
        return static_cast<float>(Math::Floor(value));
    }

    template <typename TValue>
    static float Max(TValue left, TValue right) {
        return static_cast<float>(Math::Max(left, right));
    }

    template <typename TLeft, typename TRight>
    static float Max(TLeft left, TRight right) {
        return static_cast<float>(Math::Max(left, right));
    }

    template <typename TValue>
    static float Min(TValue left, TValue right) {
        return static_cast<float>(Math::Min(left, right));
    }

    template <typename TLeft, typename TRight>
    static float Min(TLeft left, TRight right) {
        return static_cast<float>(Math::Min(left, right));
    }

    template <typename TValue>
    static float MinMagnitude(TValue left, TValue right) {
        return static_cast<float>(Math::MinMagnitude(left, right));
    }

    template <typename TValue>
    static float Abs(TValue value) {
        return static_cast<float>(Math::Abs(value));
    }

    template <typename TValue>
    static float Acos(TValue value) {
        return static_cast<float>(Math::Acos(value));
    }

    template <typename TValue>
    static float Round(TValue value) {
        return static_cast<float>(Math::Round(value));
    }

    template <typename TValue>
    static float Round(TValue value, MidpointRounding midpointRounding) {
        return static_cast<float>(Math::Round(value, midpointRounding));
    }

    template <typename TValue>
    static float Sin(TValue value) {
        return static_cast<float>(Math::Sin(value));
    }

    template <typename TValue>
    static float Cos(TValue value) {
        return static_cast<float>(Math::Cos(value));
    }

    template <typename TValue>
    static float Sqrt(TValue value) {
        return static_cast<float>(Math::Sqrt(value));
    }

    template <typename TValue>
    static float Log2(TValue value) {
        return static_cast<float>(Math::Log2(value));
    }

    template <typename TValue>
    static bool IsFinite(TValue value) {
        return Math::IsFinite(value);
    }

    template <typename TValue>
    static float Tan(TValue value) {
        return static_cast<float>(Math::Tan(value));
    }

    template <typename TY, typename TX>
    static float Atan2(TY y, TX x) {
        return static_cast<float>(Math::Atan2(y, x));
    }
};

#endif // MATH_HPP

