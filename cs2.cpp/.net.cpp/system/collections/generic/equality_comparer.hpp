#pragma once

#include <cstdint>
#include <functional>
#include <type_traits>

template <typename T>
class EqualityComparer {
public:
    static EqualityComparer<T>* get_Default() {
        static EqualityComparer<T> Instance;
        return &Instance;
    }

    int32_t GetHashCode(T& value) {
        if constexpr (std::is_pointer_v<T>) {
            if (value == nullptr) {
                return 0;
            }

            if constexpr (requires(std::remove_pointer_t<T>& instance) { instance.GetHashCode(); }) {
                return value->GetHashCode();
            } else {
                return static_cast<int32_t>(std::hash<T>{}(value));
            }
        } else if constexpr (std::is_arithmetic_v<T> || std::is_enum_v<T>) {
            return static_cast<int32_t>(std::hash<T>{}(value));
        } else if constexpr (requires(T& instance) { instance.GetHashCode(); }) {
            return value.GetHashCode();
        } else {
            return static_cast<int32_t>(std::hash<T>{}(value));
        }
    }

    bool Equals(T& left, T& right) {
        if constexpr (std::is_pointer_v<T>) {
            return left == right;
        } else if constexpr (std::is_arithmetic_v<T> || std::is_enum_v<T>) {
            return left == right;
        } else if constexpr (requires(T& a, T& b) { a.Equals(b); }) {
            return left.Equals(right);
        } else {
            return left == right;
        }
    }
};
