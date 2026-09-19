#pragma once

#include "native_algorithm.hpp"

namespace he_cpp_runtime_detail {

/// <summary>
/// Move-only owning pointer used by the shared runtime where the standard
/// library's unique_ptr is unavailable. Deletes the pointee with delete.
/// </summary>
template <typename T>
class OwnedPtr {
public:
    OwnedPtr() noexcept : Pointer(nullptr) {}
    explicit OwnedPtr(T* pointer) noexcept : Pointer(pointer) {}
    OwnedPtr(const OwnedPtr&) = delete;
    OwnedPtr& operator=(const OwnedPtr&) = delete;
    OwnedPtr(OwnedPtr&& other) noexcept : Pointer(other.Pointer) { other.Pointer = nullptr; }
    OwnedPtr& operator=(OwnedPtr&& other) noexcept {
        if (this != &other) {
            reset(other.Pointer);
            other.Pointer = nullptr;
        }
        return *this;
    }
    ~OwnedPtr() { reset(); }

    /// <summary>Returns the owned pointer without releasing ownership.</summary>
    T* get() const noexcept { return Pointer; }
    /// <summary>Gives up ownership and returns the pointer.</summary>
    T* release() noexcept { T* pointer = Pointer; Pointer = nullptr; return pointer; }
    /// <summary>Deletes the current pointee and takes ownership of the new one.</summary>
    void reset(T* pointer = nullptr) noexcept { T* previous = Pointer; Pointer = pointer; delete previous; }
    T* operator->() const noexcept { return Pointer; }
    T& operator*() const noexcept { return *Pointer; }
    explicit operator bool() const noexcept { return Pointer != nullptr; }

private:
    T* Pointer;
};

}

template <typename T>
using HeCppOwnedPtr = he_cpp_runtime_detail::OwnedPtr<T>;
