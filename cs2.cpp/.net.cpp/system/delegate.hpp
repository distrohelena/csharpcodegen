#ifndef HE_CPP_SYSTEM_DELEGATE_HPP
#define HE_CPP_SYSTEM_DELEGATE_HPP

#include "../runtime/native_algorithm.hpp"
#include "../runtime/native_runtime.hpp"

template <typename TResult, typename... TArgs>
class Delegate {
public:
    using FuncType = HeCppFunction<TResult(TArgs...)>;

    Delegate() = default;

    explicit Delegate(FuncType value)
        : func(he_cpp_alg::Move(value)) {
    }

    template <typename TCallable>
    explicit Delegate(TCallable value)
        : func(he_cpp_alg::Move(value)) {
    }

    TResult operator()(TArgs... args) const {
        return func(he_cpp_alg::Forward<TArgs>(args)...);
    }

    explicit operator bool() const {
        return static_cast<bool>(func);
    }
private:
    FuncType func{};
};

#endif
