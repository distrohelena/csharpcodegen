#pragma once

#include <cstdint>
#include <vector>

#include "native_string.hpp"

template<typename T>
class List : public std::vector<T> {
public:
    using std::vector<T>::vector;

    void Add(const T& value) {
        this->push_back(value);
    }

    int32_t Count() const {
        return static_cast<int32_t>(this->size());
    }
};
