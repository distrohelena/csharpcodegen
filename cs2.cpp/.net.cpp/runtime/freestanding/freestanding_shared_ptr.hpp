#pragma once

#include "freestanding_hooks.hpp"

#include <new>
#include <stddef.h>
#include <type_traits>

// Codegen-owned single-threaded shared ownership adapter, transformed from the fixture
// tests/runtime-capabilities-integration/single_thread_shared_ptr.hpp: same shape, but the control
// block lives in he_cpp_custom::Allocate/Free storage (placement new plus an explicit destructor
// call) instead of global new/delete, matching every other freestanding provider type.
//
// The pointee is the exception, and deliberately so: the default deleter calls delete, because the
// pointer handed to this type comes from a new expression in generated code. Only the control block,
// which this file allocates itself, goes through the hooks.
namespace he_cpp_freestanding {

namespace detail {

/// <summary>Stores the type-erased lifetime operations for one shared owner set.</summary>
struct ControlBlock {
    size_t References;
    void (*DisposeObject)(ControlBlock*);
    void (*DestroyBlock)(ControlBlock*);
};

/// <summary>Owns one pointer and its custom deleter behind a fixture control block.</summary>
template <typename TValue, typename TDeleter>
struct TypedControlBlock final : ControlBlock {
    TValue* Value;
    TDeleter Deleter;

    /// <summary>Creates a single-owner control block and copies its deleter.</summary>
    TypedControlBlock(TValue* value, const TDeleter& deleter)
        : ControlBlock{1, &DisposeObject, &DestroyBlock},
          Value(value),
          Deleter(deleter) {
    }

    /// <summary>Invokes the stored deleter for the controlled pointer.</summary>
    static void DisposeObject(ControlBlock* block) {
        TypedControlBlock* typed = static_cast<TypedControlBlock*>(block);
        typed->Deleter(typed->Value);
    }

    /// <summary>Destroys the type-erased control block after object disposal.</summary>
    static void DestroyBlock(ControlBlock* block) {
        TypedControlBlock* typed = static_cast<TypedControlBlock*>(block);
        typed->~TypedControlBlock();
        he_cpp_custom::Free(typed);
    }
};

}

/// <summary>
/// Provides single-thread shared ownership with one custom deleter invocation
/// when the final owner is released.
/// </summary>
template <typename T>
class FreestandingSharedPtr {
    template <typename>
    friend class FreestandingSharedPtr;

public:
    /// <summary>Creates an empty owner.</summary>
    FreestandingSharedPtr()
        : Value(nullptr),
          Control(nullptr) {
    }

    /// <summary>Creates an empty owner from null.</summary>
    FreestandingSharedPtr(decltype(nullptr))
        : Value(nullptr),
          Control(nullptr) {
    }

    /// <summary>Creates one owner over a raw pointer with the default deleter (plain delete).</summary>
    template <typename U, typename std::enable_if<std::is_convertible<U*, T*>::value, int>::type = 0>
    explicit FreestandingSharedPtr(U* value)
        : FreestandingSharedPtr(value, [](U* pointer) { delete pointer; }) {
    }

    /// <summary>Creates one owner with a copied custom deleter.</summary>
    template <typename U, typename TDeleter, typename std::enable_if<std::is_convertible<U*, T*>::value, int>::type = 0>
    explicit FreestandingSharedPtr(U* value, const TDeleter& deleter)
        : Value(value),
          Control(nullptr) {
        using Block = detail::TypedControlBlock<U, TDeleter>;
        Block* block = static_cast<Block*>(he_cpp_custom::Allocate(sizeof(Block)));
        new (block) Block(value, deleter);
        Control = block;
    }

    /// <summary>Shares ownership with another owner of the same type.</summary>
    FreestandingSharedPtr(const FreestandingSharedPtr& other)
        : Value(other.Value),
          Control(other.Control) {
        AddReference();
    }

    /// <summary>Shares ownership through a pointer conversion.</summary>
    template <typename U, typename std::enable_if<std::is_convertible<U*, T*>::value, int>::type = 0>
    FreestandingSharedPtr(const FreestandingSharedPtr<U>& other)
        : Value(other.Value),
          Control(other.Control) {
        AddReference();
    }

    /// <summary>Moves one owner without changing the reference count.</summary>
    FreestandingSharedPtr(FreestandingSharedPtr&& other) noexcept
        : Value(other.Value),
          Control(other.Control) {
        other.Value = nullptr;
        other.Control = nullptr;
    }

    /// <summary>Releases the owned reference.</summary>
    ~FreestandingSharedPtr() {
        Release();
    }

    /// <summary>Shares ownership through copy assignment.</summary>
    FreestandingSharedPtr& operator=(const FreestandingSharedPtr& other) {
        if (this != &other) {
            FreestandingSharedPtr copy(other);
            swap(copy);
        }
        return *this;
    }

    /// <summary>Moves ownership through move assignment.</summary>
    FreestandingSharedPtr& operator=(FreestandingSharedPtr&& other) noexcept {
        if (this != &other) {
            Release();
            Value = other.Value;
            Control = other.Control;
            other.Value = nullptr;
            other.Control = nullptr;
        }
        return *this;
    }

    /// <summary>Returns the observed pointer without transferring ownership.</summary>
    T* get() const {
        return Value;
    }

    /// <summary>Returns the number of shared owners.</summary>
    size_t use_count() const {
        return Control == nullptr ? 0 : Control->References;
    }

    /// <summary>Reports whether an observed pointer is present.</summary>
    explicit operator bool() const {
        return Value != nullptr;
    }

    /// <summary>Releases this owner and leaves it empty.</summary>
    void reset() {
        Release();
    }

    /// <summary>Exchanges two owners without invoking either deleter.</summary>
    void swap(FreestandingSharedPtr& other) noexcept {
        T* value = Value;
        Value = other.Value;
        other.Value = value;

        detail::ControlBlock* control = Control;
        Control = other.Control;
        other.Control = control;
    }

private:
    /// <summary>Adds one owner to the control block when it exists.</summary>
    void AddReference() {
        if (Control != nullptr) {
            ++Control->References;
        }
    }

    /// <summary>Releases one owner and disposes the pointer at the final release.</summary>
    void Release() {
        detail::ControlBlock* control = Control;
        Value = nullptr;
        Control = nullptr;
        if (control == nullptr) {
            return;
        }

        --control->References;
        if (control->References == 0) {
            control->DisposeObject(control);
            control->DestroyBlock(control);
        }
    }

    T* Value;
    detail::ControlBlock* Control;
};

}
