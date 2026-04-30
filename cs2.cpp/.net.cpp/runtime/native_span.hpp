#pragma once

#include <cstddef>

template <typename T>
class Span {
public:
    T* Data;
    size_t Length;

    Span()
        : Data(nullptr),
          Length(0) {
    }

    Span(T* data, size_t length)
        : Data(data),
          Length(length) {
    }

    template <size_t N>
    Span(T (&buffer)[N])
        : Data(buffer),
          Length(N) {
    }

    Span Slice(size_t offset) const {
        return Span(Data + offset, Length - offset);
    }
};
