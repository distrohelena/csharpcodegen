#pragma once

#include "../native_algorithm.hpp"
#include "freestanding_hooks.hpp"

#include <new>
#include <stddef.h>
#include <type_traits>

namespace he_cpp_freestanding {

template <typename TSignature>
class FreestandingFunction;

/// <summary>
/// Type-erased callable. Callables up to InlineBytes with pointer alignment
/// live inline; larger ones live on the heap. Copy and move are supported;
/// invoking an empty function fails through he_cpp_custom::Fail.
/// </summary>
template <typename TResult, typename... TArgs>
class FreestandingFunction<TResult(TArgs...)> {
    static constexpr size_t InlineBytes = 2 * sizeof(void*) + 8;

    struct Operations {
        TResult (*Invoke)(void* storage, TArgs&&... args);
        void (*CopyTo)(const void* source, void* destination);
        void (*MoveTo)(void* source, void* destination);
        void (*Destroy)(void* storage);
        bool Heap;
    };

    template <typename TCallable, bool Inline>
    struct Model;

    template <typename TCallable>
    struct Model<TCallable, true> {
        static TCallable* Get(void* storage) { return static_cast<TCallable*>(storage); }
        static TResult Invoke(void* storage, TArgs&&... args) { return (*Get(storage))(he_cpp_alg::Forward<TArgs>(args)...); }
        static void CopyTo(const void* source, void* destination) { new (destination) TCallable(*static_cast<const TCallable*>(source)); }
        static void MoveTo(void* source, void* destination) { new (destination) TCallable(he_cpp_alg::Move(*static_cast<TCallable*>(source))); static_cast<TCallable*>(source)->~TCallable(); }
        static void Destroy(void* storage) { Get(storage)->~TCallable(); }
        static const Operations* Table() { static const Operations table{ &Invoke, &CopyTo, &MoveTo, &Destroy, false }; return &table; }
    };

    template <typename TCallable>
    struct Model<TCallable, false> {
        static TCallable* Get(void* storage) { return *static_cast<TCallable**>(storage); }
        static TResult Invoke(void* storage, TArgs&&... args) { return (*Get(storage))(he_cpp_alg::Forward<TArgs>(args)...); }
        static void CopyTo(const void* source, void* destination) {
            TCallable* copy = static_cast<TCallable*>(he_cpp_custom::Allocate(sizeof(TCallable)));
            new (copy) TCallable(**static_cast<TCallable* const*>(source));
            *static_cast<TCallable**>(destination) = copy;
        }
        static void MoveTo(void* source, void* destination) { *static_cast<TCallable**>(destination) = *static_cast<TCallable**>(source); *static_cast<TCallable**>(source) = nullptr; }
        static void Destroy(void* storage) { TCallable* callable = Get(storage); if (callable != nullptr) { callable->~TCallable(); he_cpp_custom::Free(callable); } }
        static const Operations* Table() { static const Operations table{ &Invoke, &CopyTo, &MoveTo, &Destroy, true }; return &table; }
    };

public:
    FreestandingFunction() : Table(nullptr) {}
    FreestandingFunction(std::nullptr_t) noexcept : Table(nullptr) {}
    template <typename TCallable,
              std::enable_if_t<!std::is_same_v<std::remove_cv_t<std::remove_reference_t<TCallable>>, FreestandingFunction> &&
                                    !std::is_same_v<std::remove_cv_t<std::remove_reference_t<TCallable>>, std::nullptr_t>,
                                int> = 0>
    FreestandingFunction(TCallable callable) : Table(nullptr) {
        using Stored = std::remove_cv_t<std::remove_reference_t<TCallable>>;
        constexpr bool inline_ = sizeof(Stored) <= InlineBytes && alignof(Stored) <= alignof(void*);
        if constexpr (inline_) {
            new (Storage) Stored(he_cpp_alg::Move(callable));
        } else {
            Stored* heap = static_cast<Stored*>(he_cpp_custom::Allocate(sizeof(Stored)));
            new (heap) Stored(he_cpp_alg::Move(callable));
            *reinterpret_cast<Stored**>(Storage) = heap;
        }
        Table = Model<Stored, inline_>::Table();
    }
    FreestandingFunction(const FreestandingFunction& other) : Table(other.Table) { if (Table != nullptr) Table->CopyTo(other.Storage, Storage); }
    FreestandingFunction(FreestandingFunction&& other) noexcept : Table(other.Table) { if (Table != nullptr) { Table->MoveTo(other.Storage, Storage); other.Table = nullptr; } }
    ~FreestandingFunction() { Reset(); }
    FreestandingFunction& operator=(const FreestandingFunction& other) {
        if (this != &other) {
            FreestandingFunction copy(other);
            SwapWith(copy);
        }
        return *this;
    }
    FreestandingFunction& operator=(FreestandingFunction&& other) noexcept { if (this != &other) { Reset(); Table = other.Table; if (Table != nullptr) { Table->MoveTo(other.Storage, Storage); other.Table = nullptr; } } return *this; }
    FreestandingFunction& operator=(std::nullptr_t) noexcept { Reset(); return *this; }

    explicit operator bool() const { return Table != nullptr; }
    bool operator==(std::nullptr_t) const noexcept { return Table == nullptr; }
    TResult operator()(TArgs... args) const {
        if (Table == nullptr) he_cpp_custom::Fail("Invoked an empty function");
        return Table->Invoke(const_cast<unsigned char*>(Storage), he_cpp_alg::Forward<TArgs>(args)...);
    }

private:
    alignas(void*) unsigned char Storage[InlineBytes];
    const Operations* Table;

    void Reset() { if (Table != nullptr) { Table->Destroy(Storage); Table = nullptr; } }
    // Swaps two functions without recursing through operator= (which itself builds a copy and swaps
    // it in): move *this into a temporary via the move constructor, then move-transfer other into
    // *this and the temporary into other, each time going straight through MoveTo/Reset rather than
    // through operator=.
    void SwapWith(FreestandingFunction& other) {
        FreestandingFunction temporary(he_cpp_alg::Move(*this));
        MoveFrom(other, *this);
        MoveFrom(temporary, other);
    }
    static void MoveFrom(FreestandingFunction& source, FreestandingFunction& destination) {
        destination.Reset();
        destination.Table = source.Table;
        if (destination.Table != nullptr) {
            destination.Table->MoveTo(source.Storage, destination.Storage);
            source.Table = nullptr;
        }
    }
};

}
