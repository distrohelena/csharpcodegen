#ifndef FUNC_TPP
#define FUNC_TPP

#include "func.hpp"

template<typename TResult>
Func<TResult>::Func(FuncType value) : func(value) {}

template<typename TResult>
template<typename TCallable>
Func<TResult>::Func(TCallable value) : func(value) {}

template<typename TResult>
TResult Func<TResult>::operator()() const {
    return func();
}

template<typename TResult>
Func<TResult>::operator bool() const {
    return static_cast<bool>(func);
}

template<typename TArg, typename TResult>
Func<TArg, TResult>::Func(FuncType value) : func(value) {}

template<typename TArg, typename TResult>
template<typename TCallable>
Func<TArg, TResult>::Func(TCallable value) : func(value) {}

template<typename TArg, typename TResult>
TResult Func<TArg, TResult>::operator()(TArg arg) const {
    return func(arg);
}

template<typename TArg, typename TResult>
Func<TArg, TResult>::operator bool() const {
    return static_cast<bool>(func);
}

#endif // FUNC_TPP
