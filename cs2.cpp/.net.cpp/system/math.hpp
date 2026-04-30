#ifndef MATH_HPP
#define MATH_HPP

#include <algorithm>
#include <cmath>

enum class MidpointRounding {
    ToEven,
    AwayFromZero
};

class Math {
public:
    template <typename TValue>
    static TValue Clamp(TValue value, TValue minValue, TValue maxValue) {
        return std::clamp(value, minValue, maxValue);
    }

    template <typename TValue>
    static double Ceiling(TValue value) {
        return std::ceil(static_cast<double>(value));
    }

    template <typename TValue>
    static TValue Max(TValue left, TValue right) {
        return std::max(left, right);
    }

    template <typename TValue>
    static TValue Min(TValue left, TValue right) {
        return std::min(left, right);
    }

    template <typename TValue>
    static double Round(TValue value) {
        return std::nearbyint(static_cast<double>(value));
    }

    template <typename TValue>
    static double Round(TValue value, MidpointRounding midpointRounding) {
        double promotedValue = static_cast<double>(value);

        switch (midpointRounding) {
            case MidpointRounding::AwayFromZero:
                return promotedValue >= 0.0
                    ? std::floor(promotedValue + 0.5)
                    : std::ceil(promotedValue - 0.5);
            default:
                return std::nearbyint(promotedValue);
        }
    }

    template <typename TValue>
    static double Sin(TValue value) {
        return std::sin(static_cast<double>(value));
    }

    template <typename TValue>
    static double Cos(TValue value) {
        return std::cos(static_cast<double>(value));
    }

    template <typename TValue>
    static double Sqrt(TValue value) {
        return std::sqrt(static_cast<double>(value));
    }

    template <typename TValue>
    static double Tan(TValue value) {
        return std::tan(static_cast<double>(value));
    }
};

class MathF {
public:
    template <typename TValue>
    static float Clamp(TValue value, TValue minValue, TValue maxValue) {
        return static_cast<float>(Math::Clamp(value, minValue, maxValue));
    }

    template <typename TValue>
    static float Ceiling(TValue value) {
        return static_cast<float>(Math::Ceiling(value));
    }

    template <typename TValue>
    static float Max(TValue left, TValue right) {
        return static_cast<float>(Math::Max(left, right));
    }

    template <typename TValue>
    static float Min(TValue left, TValue right) {
        return static_cast<float>(Math::Min(left, right));
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
    static float Tan(TValue value) {
        return static_cast<float>(Math::Tan(value));
    }
};

#endif // MATH_HPP
