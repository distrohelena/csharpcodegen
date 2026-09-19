#pragma once

#include "../native_algorithm.hpp"
#include "freestanding_hooks.hpp"

#include <initializer_list>
#include <new>
#include <stddef.h>
#include <type_traits>

namespace he_cpp_freestanding {

/// <summary>
/// Contiguous growable array. Elements are constructed in place with placement
/// new and moved on growth; capacity doubles from a minimum of four. insert
/// never default-constructs: it shifts by moving the existing tail forward.
/// The single-argument resize(newLength), when growing, default-constructs
/// the new elements, so it requires T to be default-constructible; every
/// element type the runtime stores today (HeCppOwnedPtr, values, pointers)
/// satisfies that. Shrinking, and the two-argument resize(newLength, fill),
/// never require a default constructor.
/// </summary>
template <typename T>
class FreestandingVector {
public:
    using value_type = T;
    using size_type = size_t;
    using iterator = T*;
    using const_iterator = const T*;

    FreestandingVector() : Data(nullptr), Length(0), Capacity(0) {}
    FreestandingVector(std::initializer_list<T> items) : FreestandingVector() {
        reserve(items.size());
        for (const T& item : items) push_back(item);
    }
    FreestandingVector(const FreestandingVector& other) : FreestandingVector() {
        reserve(other.Length);
        for (size_t index = 0; index < other.Length; ++index) new (Data + index) T(other.Data[index]);
        Length = other.Length;
    }
    FreestandingVector(FreestandingVector&& other) noexcept : Data(other.Data), Length(other.Length), Capacity(other.Capacity) {
        other.Data = nullptr; other.Length = 0; other.Capacity = 0;
    }
    ~FreestandingVector() { clear(); if (Data != nullptr) he_cpp_custom::Free(Data); }

    FreestandingVector& operator=(const FreestandingVector& other) {
        if (this != &other) { FreestandingVector copy(other); swap(copy); }
        return *this;
    }
    FreestandingVector& operator=(FreestandingVector&& other) noexcept {
        if (this != &other) { FreestandingVector moved(he_cpp_alg::Move(other)); swap(moved); }
        return *this;
    }

    size_t size() const { return Length; }
    size_t capacity() const { return Capacity; }
    bool empty() const { return Length == 0; }
    T* data() { return Data; }
    const T* data() const { return Data; }
    T& operator[](size_t index) { return Data[index]; }
    const T& operator[](size_t index) const { return Data[index]; }
    T& at(size_t index) { if (index >= Length) he_cpp_custom::Fail("Vector index out of range"); return Data[index]; }
    const T& at(size_t index) const { if (index >= Length) he_cpp_custom::Fail("Vector index out of range"); return Data[index]; }
    T& front() { return Data[0]; }
    const T& front() const { return Data[0]; }
    T& back() { return Data[Length - 1]; }
    const T& back() const { return Data[Length - 1]; }
    iterator begin() { return Data; }
    iterator end() { return Data + Length; }
    const_iterator begin() const { return Data; }
    const_iterator end() const { return Data + Length; }

    void reserve(size_t newCapacity) {
        if (newCapacity <= Capacity) return;
        if (newCapacity > static_cast<size_t>(-1) / sizeof(T)) he_cpp_custom::Fail("Container allocation size overflow");
        T* buffer = static_cast<T*>(he_cpp_custom::Allocate(newCapacity * sizeof(T)));
        for (size_t index = 0; index < Length; ++index) {
            new (buffer + index) T(he_cpp_alg::Move(Data[index]));
            Data[index].~T();
        }
        if (Data != nullptr) he_cpp_custom::Free(Data);
        Data = buffer;
        Capacity = newCapacity;
    }
    // value may alias one of our own elements (v.push_back(v[0]), v.push_back(v.back())); Grow can
    // free our storage to reallocate, so take an independent copy/move first whenever it does.
    void push_back(const T& value) {
        if (&value >= Data && &value < Data + Length) {
            T copy(value);
            Grow(Length + 1);
            new (Data + Length) T(he_cpp_alg::Move(copy));
        } else {
            Grow(Length + 1);
            new (Data + Length) T(value);
        }
        ++Length;
    }
    void push_back(T&& value) {
        if (&value >= Data && &value < Data + Length) {
            T copy(he_cpp_alg::Move(value));
            Grow(Length + 1);
            new (Data + Length) T(he_cpp_alg::Move(copy));
        } else {
            Grow(Length + 1);
            new (Data + Length) T(he_cpp_alg::Move(value));
        }
        ++Length;
    }
    template <typename... TArgs>
    T& emplace_back(TArgs&&... args) {
        Grow(Length + 1);
        new (Data + Length) T(he_cpp_alg::Forward<TArgs>(args)...);
        return Data[Length++];
    }
    void pop_back() { if (Length != 0) { --Length; Data[Length].~T(); } }
    void clear() { for (size_t index = 0; index < Length; ++index) Data[index].~T(); Length = 0; }
    void resize(size_t newLength) {
        if (newLength < Length) {
            for (size_t index = newLength; index < Length; ++index) Data[index].~T();
            Length = newLength;
            return;
        }
        if constexpr (std::is_default_constructible_v<T>) {
            Grow(newLength);
            for (size_t index = Length; index < newLength; ++index) new (Data + index) T();
            Length = newLength;
        } else if (newLength != Length) {
            he_cpp_custom::Fail("Vector resize requires a default-constructible element type");
        }
    }
    void resize(size_t newLength, const T& fill) {
        if (newLength < Length) {
            for (size_t index = newLength; index < Length; ++index) Data[index].~T();
            Length = newLength;
            return;
        }
        // fill may alias one of our own elements (v.resize(n, v[1])); Grow can free our storage to
        // reallocate, so take an independent copy first whenever it does.
        if (&fill >= Data && &fill < Data + Length) {
            T copy(fill);
            Grow(newLength);
            for (size_t index = Length; index < newLength; ++index) new (Data + index) T(copy);
        } else {
            Grow(newLength);
            for (size_t index = Length; index < newLength; ++index) new (Data + index) T(fill);
        }
        Length = newLength;
    }
    iterator erase(const_iterator position) { return erase(position, position + 1); }
    iterator erase(const_iterator first, const_iterator last) {
        size_t start = static_cast<size_t>(first - Data);
        size_t count = static_cast<size_t>(last - first);
        if (count == 0) return Data + start;
        for (size_t index = start; index + count < Length; ++index) Data[index] = he_cpp_alg::Move(Data[index + count]);
        for (size_t index = Length - count; index < Length; ++index) Data[index].~T();
        Length -= count;
        return Data + start;
    }
    iterator insert(const_iterator position, const T& value) {
        size_t index = static_cast<size_t>(position - Data);
        T copy(value);
        Grow(Length + 1);
        if (index == Length) {
            new (Data + Length) T(he_cpp_alg::Move(copy));
        } else {
            new (Data + Length) T(he_cpp_alg::Move(Data[Length - 1]));
            for (size_t slot = Length - 1; slot > index; --slot) Data[slot] = he_cpp_alg::Move(Data[slot - 1]);
            Data[index] = he_cpp_alg::Move(copy);
        }
        ++Length;
        return Data + index;
    }
    void swap(FreestandingVector& other) noexcept {
        he_cpp_alg::Swap(Data, other.Data);
        he_cpp_alg::Swap(Length, other.Length);
        he_cpp_alg::Swap(Capacity, other.Capacity);
    }

private:
    T* Data;
    size_t Length;
    size_t Capacity;

    void Grow(size_t required) {
        if (required <= Capacity) return;
        size_t next = Capacity < 4 ? 4 : Capacity;
        while (next < required) {
            if (next > static_cast<size_t>(-1) / 2) he_cpp_custom::Fail("Container capacity overflow");
            next = next * 2;
        }
        reserve(next);
    }
};

}
