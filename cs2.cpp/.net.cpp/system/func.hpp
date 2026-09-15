#ifndef FUNC_HPP
#define FUNC_HPP

#include "../runtime/native_runtime.hpp"

template <typename... TArgs>
class Func {
};

template <typename TResult>
class Func<TResult> {
public:
    using FuncType = HeCppFunction<TResult()>;

    Func() = default;

    explicit Func(FuncType value);

    template<typename TCallable>
    explicit Func(TCallable value);

    TResult operator()() const;

    explicit operator bool() const;

private:
    FuncType func{};
};

template <typename TArg, typename TResult>
class Func<TArg, TResult> {
public:
    using FuncType = HeCppFunction<TResult(TArg)>;

    Func() = default;

    explicit Func(FuncType value);

    template<typename TCallable>
    explicit Func(TCallable value);

    TResult operator()(TArg arg) const;

    explicit operator bool() const;

private:
    FuncType func{};
};

#include "func.tpp"

#endif // FUNC_HPP
