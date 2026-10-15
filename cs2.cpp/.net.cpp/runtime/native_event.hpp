#pragma once
#include "native_algorithm.hpp"
#include "native_runtime.hpp"
#include "native_module_pinned_callable.hpp"

#include <cstddef>
#include <type_traits>

/// <summary>
/// Represents a lightweight managed event bridge used by transpiled engine members during native execution.
/// </summary>
class Event {
public:
    template <typename TInstance, typename... TArgs>
    struct BoundHandler {
        TInstance* Instance;
        void (TInstance::*Method)(TArgs...);
    };

    /// <summary>Preserves member-method identity while keeping its defining module mapped.</summary>
    template <typename TInstance, typename... TArgs>
    struct PinnedBoundHandler {
        /// <summary>Owns code for as long as this removable token is retained.</summary>
        HeCppCodeModulePin Pin;
        /// <summary>Names the module whose callback code is retained by subscribers.</summary>
        const char* ModuleId;
        /// <summary>Retains the original identity used by event removal.</summary>
        BoundHandler<TInstance, TArgs...> Handler;

        /// <summary>Acquires code ownership before exposing the bound token.</summary>
        PinnedBoundHandler(const char* moduleId, BoundHandler<TInstance, TArgs...> handler)
            : Pin(moduleId), ModuleId(moduleId), Handler(handler) {
        }
    };

    /// <summary>Preserves free-function identity and owns the module containing its code.</summary>
    template <typename... TArgs>
    struct PinnedFunctionHandler {
        /// <summary>Owns code for as long as this removable token is retained.</summary>
        HeCppCodeModulePin Pin;
        /// <summary>Names the module whose callback code is retained by subscribers.</summary>
        const char* ModuleId;
        /// <summary>Stores the original function pointer used by event removal.</summary>
        void (*Handler)(TArgs...);

        /// <summary>Acquires code ownership before exposing the function token.</summary>
        PinnedFunctionHandler(const char* moduleId, void (*handler)(TArgs...))
            : Pin(moduleId), ModuleId(moduleId), Handler(handler) {
        }
    };

    /// <summary>
    /// Initializes an empty event bridge.
    /// </summary>
    Event() = default;

    /// <summary>
    /// Binds one instance and member-method pair into a removable event subscriber token.
    /// </summary>
    /// <typeparam name="TInstance">Owning instance type.</typeparam>
    /// <typeparam name="TArgs">Native handler argument types.</typeparam>
    /// <param name="instance">Owning instance that should receive the callback.</param>
    /// <param name="method">Instance method that should receive the callback.</param>
    /// <returns>A bound handler token that can be passed to event add or remove operators.</returns>
    template <typename TInstance, typename... TArgs>
    static auto Bind(TInstance* instance, void (TInstance::*method)(TArgs...)) {
        return BoundHandler<TInstance, TArgs...> { instance, method };
    }

    /// <summary>Binds a removable member callback with ownership of its defining module.</summary>
    template <typename TInstance, typename... TArgs>
    static auto BindPinned(const char* moduleId, TInstance* instance, void (TInstance::*method)(TArgs...)) {
        return PinnedBoundHandler<TInstance, TArgs...>(moduleId, Bind(instance, method));
    }

    /// <summary>Binds a removable free or static callback with ownership of its defining module.</summary>
    template <typename... TArgs>
    static auto PinFunction(const char* moduleId, void (*handler)(TArgs...)) {
        return PinnedFunctionHandler<TArgs...>(moduleId, handler);
    }

    /// <summary>Stores a pinned member callback without changing its removal identity.</summary>
    template <typename TInstance, typename... TArgs>
    Event& operator+=(const PinnedBoundHandler<TInstance, TArgs...>& token) {
        const auto handler = token.Handler;
        if (handler.Instance == nullptr || handler.Method == nullptr) {
            return *this;
        }
        Subscribers.push_back(HeCppOwnedPtr<Subscriber>(new Subscriber {
            sizeof...(TArgs),
            HeCppPinCodeModuleCallable(token.ModuleId, [handler](void** arguments) {
                InvokeBoundMethod(handler.Instance, handler.Method, arguments, he_cpp_alg::IndexSequenceFor<TArgs...> {});
            }),
            nullptr,
            [handler](const void* candidateInstance, const void* candidateMethod) {
                return candidateInstance != nullptr && candidateMethod != nullptr &&
                    static_cast<TInstance*>(const_cast<void*>(candidateInstance)) == handler.Instance &&
                    *static_cast<void (TInstance::*const*)(TArgs...)>(candidateMethod) == handler.Method;
            }
        }));
        return *this;
    }

    /// <summary>Stores a pinned free callback without changing its removal identity.</summary>
    template <typename... TArgs>
    Event& operator+=(const PinnedFunctionHandler<TArgs...>& token) {
        const auto handler = token.Handler;
        if (handler == nullptr) {
            return *this;
        }
        Subscribers.push_back(HeCppOwnedPtr<Subscriber>(new Subscriber {
            sizeof...(TArgs),
            HeCppPinCodeModuleCallable(token.ModuleId, [handler](void** arguments) {
                InvokeFunctionPointer(handler, arguments, he_cpp_alg::IndexSequenceFor<TArgs...> {});
            }),
            [handler](const void* candidateHandler) {
                return candidateHandler != nullptr &&
                    *static_cast<void (*const*)(TArgs...)>(candidateHandler) == handler;
            },
            nullptr
        }));
        return *this;
    }

    /// <summary>Removes pinned member callbacks by the same receiver and method as ordinary tokens.</summary>
    template <typename TInstance, typename... TArgs>
    Event& operator-=(const PinnedBoundHandler<TInstance, TArgs...>& token) {
        return *this -= token.Handler;
    }

    /// <summary>Removes pinned free callbacks by their original function pointer.</summary>
    template <typename... TArgs>
    Event& operator-=(const PinnedFunctionHandler<TArgs...>& token) {
        return *this -= token.Handler;
    }

    /// <summary>
    /// Registers one free or static function subscriber that can be invoked later with a matching argument list.
    /// </summary>
    /// <typeparam name="TArgs">Native handler argument types.</typeparam>
    /// <param name="handler">Free or static function handler being attached to the event.</param>
    /// <returns>The current event so chained subscriptions remain compilable.</returns>
    template <typename... TArgs>
    Event& operator+=(void (*handler)(TArgs...)) {
        if (handler == nullptr) {
            return *this;
        }

        Subscribers.push_back(HeCppOwnedPtr<Subscriber>(new Subscriber {
            sizeof...(TArgs),
            [handler](void** arguments) {
                InvokeFunctionPointer(handler, arguments, he_cpp_alg::IndexSequenceFor<TArgs...> {});
            },
            [handler](const void* candidateHandler) {
                return candidateHandler != nullptr &&
                    *static_cast<void (*const*)(TArgs...)>(candidateHandler) == handler;
            },
            nullptr
        }));
        return *this;
    }

    /// <summary>
    /// Registers one bound instance-method subscriber that can be invoked later with a matching argument list.
    /// </summary>
    /// <typeparam name="TInstance">Owning instance type.</typeparam>
    /// <typeparam name="TArgs">Native handler argument types.</typeparam>
    /// <param name="handler">Bound instance-method handler token being attached to the event.</param>
    /// <returns>The current event so chained subscriptions remain compilable.</returns>
    template <typename TInstance, typename... TArgs>
    Event& operator+=(BoundHandler<TInstance, TArgs...> handler) {
        if (handler.Instance == nullptr || handler.Method == nullptr) {
            return *this;
        }

        Subscribers.push_back(HeCppOwnedPtr<Subscriber>(new Subscriber {
            sizeof...(TArgs),
            [handler](void** arguments) {
                InvokeBoundMethod(handler.Instance, handler.Method, arguments, he_cpp_alg::IndexSequenceFor<TArgs...> {});
            },
            nullptr,
            [handler](const void* candidateInstance, const void* candidateMethod) {
                if (candidateInstance == nullptr || candidateMethod == nullptr) {
                    return false;
                }

                return static_cast<TInstance*>(const_cast<void*>(candidateInstance)) == handler.Instance &&
                    *static_cast<void (TInstance::*const*)(TArgs...)>(candidateMethod) == handler.Method;
            }
        }));
        return *this;
    }

    /// <summary>
    /// Accepts unsupported subscriber shapes so existing generated unbound instance-method subscriptions remain compilable.
    /// </summary>
    /// <typeparam name="THandler">Native handler shape provided by the caller.</typeparam>
    /// <param name="handler">Handler instance being attached to the event.</param>
    /// <returns>The current event so chained subscriptions remain compilable.</returns>
    template <typename THandler>
    Event& operator+=(THandler handler) {
        (void)handler;
        return *this;
    }

    /// <summary>
    /// Unregisters a subscriber from the event placeholder.
    /// </summary>
    /// <typeparam name="THandler">Native handler shape provided by the caller.</typeparam>
    /// <param name="handler">Handler instance being detached from the event.</param>
    /// <returns>The current event so chained removals remain compilable.</returns>
    template <typename THandler>
    Event& operator-=(THandler handler) {
        (void)handler;
        return *this;
    }

    /// <summary>
    /// Unregisters one free or static function subscriber from the event.
    /// </summary>
    /// <typeparam name="TArgs">Native handler argument types.</typeparam>
    /// <param name="handler">Free or static function handler being detached from the event.</param>
    /// <returns>The current event so chained removals remain compilable.</returns>
    template <typename... TArgs>
    Event& operator-=(void (*handler)(TArgs...)) {
        if (handler == nullptr) {
            return *this;
        }

        Subscribers.erase(
            he_cpp_alg::RemoveIf(
                Subscribers.begin(),
                Subscribers.end(),
                [handler](const HeCppOwnedPtr<Subscriber>& subscriber) {
                    if (subscriber->ArgumentCount != sizeof...(TArgs) || subscriber->MatchesFunction == nullptr) {
                        return false;
                    }

                    return subscriber->MatchesFunction(&handler);
                }),
            Subscribers.end());
        return *this;
    }

    /// <summary>
    /// Unregisters one bound instance-method subscriber from the event.
    /// </summary>
    /// <typeparam name="TInstance">Owning instance type.</typeparam>
    /// <typeparam name="TArgs">Native handler argument types.</typeparam>
    /// <param name="handler">Bound instance-method handler token being detached from the event.</param>
    /// <returns>The current event so chained removals remain compilable.</returns>
    template <typename TInstance, typename... TArgs>
    Event& operator-=(BoundHandler<TInstance, TArgs...> handler) {
        if (handler.Instance == nullptr || handler.Method == nullptr) {
            return *this;
        }

        Subscribers.erase(
            he_cpp_alg::RemoveIf(
                Subscribers.begin(),
                Subscribers.end(),
                [handler](const HeCppOwnedPtr<Subscriber>& subscriber) {
                    if (subscriber->ArgumentCount != sizeof...(TArgs) || subscriber->MatchesBound == nullptr) {
                        return false;
                    }

                    return subscriber->MatchesBound(handler.Instance, &handler.Method);
                }),
            Subscribers.end());
        return *this;
    }

    /// <summary>
    /// Invokes all subscribers that were registered with the same arity as the supplied argument list.
    /// </summary>
    /// <typeparam name="TArgs">Native argument shapes forwarded by the transpiled call site.</typeparam>
    /// <param name="args">Arguments supplied by the transpiled call site.</param>
    template <typename... TArgs>
    void Invoke(TArgs... args) {
        void* argumentPointers[sizeof...(TArgs) == 0 ? 1 : sizeof...(TArgs)] = { const_cast<void*>(static_cast<const void*>(he_cpp_alg::AddressOf(args)))... };
        for (HeCppOwnedPtr<Subscriber>& subscriber : Subscribers) {
            if (subscriber->ArgumentCount == sizeof...(TArgs)) {
                subscriber->Invoke(argumentPointers);
            }
        }
    }

private:
    /// <summary>
    /// Stores one type-erased free or static function subscriber.
    /// </summary>
    struct Subscriber {
        /// <summary>
        /// Number of arguments expected by the subscriber.
        /// </summary>
        std::size_t ArgumentCount;

        /// <summary>
        /// Type-erased invocation thunk that reads arguments from the packed invocation array.
        /// </summary>
        HeCppFunction<void(void**)> Invoke;

        /// <summary>
        /// Optional free-function matcher used by remove operations.
        /// </summary>
        HeCppFunction<bool(const void*)> MatchesFunction;

        /// <summary>
        /// Optional bound-subscriber matcher used by remove operations.
        /// </summary>
        HeCppFunction<bool(const void*, const void*)> MatchesBound;
    };

    /// <summary>
    /// Invokes one stored free or static function by unpacking the argument pointer array.
    /// </summary>
    /// <typeparam name="TArgs">Argument shapes supplied by the transpiled event invocation.</typeparam>
    /// <typeparam name="TIndexes">Compile-time argument indexes used to unpack the argument pointer array.</typeparam>
    /// <param name="handler">Free or static function subscriber.</param>
    /// <param name="arguments">Packed addresses of the invocation arguments.</param>
    template <typename... TArgs, std::size_t... TIndexes>
    static void InvokeFunctionPointer(void (*handler)(TArgs...), void** arguments, he_cpp_alg::IndexSequence<TIndexes...>) {
        handler((*static_cast<std::remove_reference_t<TArgs>*>(arguments[TIndexes]))...);
    }

    /// <summary>
    /// Invokes one stored bound instance-method subscriber by unpacking the argument pointer array.
    /// </summary>
    /// <typeparam name="TInstance">Owning instance type.</typeparam>
    /// <typeparam name="TArgs">Argument shapes supplied by the transpiled event invocation.</typeparam>
    /// <typeparam name="TIndexes">Compile-time argument indexes used to unpack the argument pointer array.</typeparam>
    /// <param name="instance">Owning instance receiving the callback.</param>
    /// <param name="method">Bound member method receiving the callback.</param>
    /// <param name="arguments">Packed addresses of the invocation arguments.</param>
    template <typename TInstance, typename... TArgs, std::size_t... TIndexes>
    static void InvokeBoundMethod(TInstance* instance, void (TInstance::*method)(TArgs...), void** arguments, he_cpp_alg::IndexSequence<TIndexes...>) {
        (instance->*method)((*static_cast<std::remove_reference_t<TArgs>*>(arguments[TIndexes]))...);
    }

    /// <summary>
    /// Subscribers currently attached to this event.
    /// </summary>
    HeCppVector<HeCppOwnedPtr<Subscriber>> Subscribers;
};
