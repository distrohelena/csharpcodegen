#ifndef ACTION_TPP
#define ACTION_TPP

#include "Action.hpp"

// Function pointer constructor
template<typename T>
Action<T>::Action(FuncType f) : func(f) {}

// Invoke function
template<typename T>
void Action<T>::operator()(T arg) const {
    if (func) {
        func(arg);
    }
}

// Check if Action is valid
template<typename T>
Action<T>::operator bool() const {
    return func != nullptr;
}

#endif // ACTION_TPP
