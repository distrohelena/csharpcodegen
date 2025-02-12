#ifndef ACTION_HPP
#define ACTION_HPP

template<typename T>
class Action {
private:
    using FuncType = void(*)(T);
    FuncType func = nullptr;

public:
    // Default constructor
    Action() = default;

    // Constructor for function pointers
    explicit Action(FuncType f);

    // Invoke stored function
    void operator()(T arg) const;

    // Checks if the Action is valid
    explicit operator bool() const;
};

// Include implementation for template functions
#include "Action.tpp"

#endif // ACTION_HPP
