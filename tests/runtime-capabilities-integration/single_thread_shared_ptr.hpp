#pragma once

#include <cstddef>
#include <type_traits>

// Isolated fixture ownership adapter. Production PS1 code owns its implementation
// in helengine-ps1-impl/src/platform/ps1/Ps1SharedPtr.hpp.
namespace single_thread_fixture_detail {

/// <summary>Stores the type-erased lifetime operations for one shared owner set.</summary>
struct ControlBlock {
    std::size_t References;
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
        delete static_cast<TypedControlBlock*>(block);
    }
};

}

namespace single_thread_fixture {

/// <summary>
/// Provides single-thread shared ownership with one custom deleter invocation
/// when the final fixture owner is released.
/// </summary>
template <typename T>
class SingleThreadSharedPtr {
    template <typename>
    friend class SingleThreadSharedPtr;

public:
    /// <summary>Creates an empty owner.</summary>
    SingleThreadSharedPtr()
        : Value(nullptr),
          Control(nullptr) {
    }

    /// <summary>Creates an empty owner from null.</summary>
    SingleThreadSharedPtr(std::nullptr_t)
        : Value(nullptr),
          Control(nullptr) {
    }

    /// <summary>Creates one owner with a copied custom deleter.</summary>
    template <typename U, typename TDeleter, typename std::enable_if<std::is_convertible<U*, T*>::value, int>::type = 0>
    explicit SingleThreadSharedPtr(U* value, const TDeleter& deleter)
        : Value(value),
          Control(new single_thread_fixture_detail::TypedControlBlock<U, TDeleter>(value, deleter)) {
    }

    /// <summary>Shares ownership with another owner of the same type.</summary>
    SingleThreadSharedPtr(const SingleThreadSharedPtr& other)
        : Value(other.Value),
          Control(other.Control) {
        AddReference();
    }

    /// <summary>Shares ownership through a pointer conversion.</summary>
    template <typename U, typename std::enable_if<std::is_convertible<U*, T*>::value, int>::type = 0>
    SingleThreadSharedPtr(const SingleThreadSharedPtr<U>& other)
        : Value(other.Value),
          Control(other.Control) {
        AddReference();
    }

    /// <summary>Moves one owner without changing the reference count.</summary>
    SingleThreadSharedPtr(SingleThreadSharedPtr&& other)
        : Value(other.Value),
          Control(other.Control) {
        other.Value = nullptr;
        other.Control = nullptr;
    }

    /// <summary>Releases the owned reference.</summary>
    ~SingleThreadSharedPtr() {
        Release();
    }

    /// <summary>Shares ownership through copy assignment.</summary>
    SingleThreadSharedPtr& operator=(const SingleThreadSharedPtr& other) {
        if (this != &other) {
            SingleThreadSharedPtr copy(other);
            swap(copy);
        }
        return *this;
    }

    /// <summary>Moves ownership through move assignment.</summary>
    SingleThreadSharedPtr& operator=(SingleThreadSharedPtr&& other) {
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
    std::size_t use_count() const {
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
    void swap(SingleThreadSharedPtr& other) {
        T* value = Value;
        Value = other.Value;
        other.Value = value;

        single_thread_fixture_detail::ControlBlock* control = Control;
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
        single_thread_fixture_detail::ControlBlock* control = Control;
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
    single_thread_fixture_detail::ControlBlock* Control;
};

}
