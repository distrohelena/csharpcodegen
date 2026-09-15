#ifndef HE_CPP_SYSTEM_DELEGATE_HPP
#define HE_CPP_SYSTEM_DELEGATE_HPP

#include "../runtime/native_runtime.hpp"
#include <utility>

template <typename TResult, typename... TArgs>
class Delegate {
public:
    using FuncType = HeCppFunction<TResult(TArgs...)>;

    Delegate() = default;

    explicit Delegate(FuncType value)
        : func(std::move(value)) {
    }

    template <typename TCallable>
    explicit Delegate(TCallable value)
        : func(std::move(value)) {
    }

    TResult operator()(TArgs... args) const {
        return func(std::forward<TArgs>(args)...);
    }

    explicit operator bool() const {
        return static_cast<bool>(func);
    }
private:
    FuncType func{};
};

#endif
