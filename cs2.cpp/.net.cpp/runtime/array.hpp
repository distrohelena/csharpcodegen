#pragma once

#include <cstdint>

template<typename T>
class Array {
public:
    int32_t Length;
    T* Data;

    Array()
        : Length(0), Data(nullptr) {
    }

    explicit Array(int32_t length)
        : Length(length), Data(length > 0 ? new T[length] : nullptr) {
    }

    T& operator[](int32_t index) {
        return Data[index];
    }

    const T& operator[](int32_t index) const {
        return Data[index];
    }
};
