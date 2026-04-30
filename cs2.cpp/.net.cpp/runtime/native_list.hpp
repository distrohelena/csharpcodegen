#pragma once

#include <algorithm>
#include <cstdint>
#include <vector>

#include "array.hpp"
#include "native_string.hpp"

template<typename T>
class List : public std::vector<T> {
public:
    using std::vector<T>::vector;

    explicit List(const Array<T>* values) {
        if (values == nullptr || values->Length <= 0 || values->Data == nullptr) {
            return;
        }

        this->reserve(values->Length);
        for (int32_t index = 0; index < values->Length; index++) {
            this->push_back((*values)[index]);
        }
    }

    void Add(const T& value) {
        this->push_back(value);
    }

    void Clear() {
        this->clear();
    }

    bool Remove(const T& value) {
        typename std::vector<T>::iterator iterator = std::find(this->begin(), this->end(), value);
        if (iterator == this->end()) {
            return false;
        }

        this->erase(iterator);
        return true;
    }

    int32_t Count() const {
        return static_cast<int32_t>(this->size());
    }

    Array<T>* ToArray() const {
        Array<T>* values = new Array<T>(Count());
        for (int32_t index = 0; index < values->Length; index++) {
            (*values)[index] = (*this)[index];
        }

        return values;
    }
};
