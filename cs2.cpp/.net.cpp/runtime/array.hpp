#pragma once

#include <cstdint>
#include <initializer_list>

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

    Array(std::initializer_list<T> values)
        : Length(static_cast<int32_t>(values.size())), Data(values.size() > 0 ? new T[values.size()] : nullptr) {
        int32_t index = 0;
        for (const T& value : values) {
            Data[index++] = value;
        }
    }

    static Array<T>* Empty() {
        static Array<T> EmptyArray(0);
        return &EmptyArray;
    }

    T& operator[](int32_t index) {
        return Data[index];
    }

    const T& operator[](int32_t index) const {
        return Data[index];
    }
};
