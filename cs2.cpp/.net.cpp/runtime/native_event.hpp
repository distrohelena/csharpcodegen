#pragma once

#include <algorithm>
#include <array>
#include <cstddef>
#include <functional>
#include <memory>
#include <type_traits>
#include <utility>
#include <vector>

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

        Subscribers.push_back(Subscriber {
            sizeof...(TArgs),
            [handler](void** arguments) {
                InvokeFunctionPointer(handler, arguments, std::index_sequence_for<TArgs...> {});
            },
            [handler](const void* candidateHandler) {
                return candidateHandler != nullptr &&
                    *static_cast<void (*const*)(TArgs...)>(candidateHandler) == handler;
            },
            nullptr
        });
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

        Subscribers.push_back(Subscriber {
            sizeof...(TArgs),
            [handler](void** arguments) {
                InvokeBoundMethod(handler.Instance, handler.Method, arguments, std::index_sequence_for<TArgs...> {});
            },
            nullptr,
            [handler](const void* candidateInstance, const void* candidateMethod) {
                if (candidateInstance == nullptr || candidateMethod == nullptr) {
                    return false;
                }

                return static_cast<TInstance*>(const_cast<void*>(candidateInstance)) == handler.Instance &&
                    *static_cast<void (TInstance::*const*)(TArgs...)>(candidateMethod) == handler.Method;
            }
        });
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
            std::remove_if(
                Subscribers.begin(),
                Subscribers.end(),
                [handler](const Subscriber& subscriber) {
                    if (subscriber.ArgumentCount != sizeof...(TArgs) || subscriber.MatchesFunction == nullptr) {
                        return false;
                    }

                    return subscriber.MatchesFunction(&handler);
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
            std::remove_if(
                Subscribers.begin(),
                Subscribers.end(),
                [handler](const Subscriber& subscriber) {
                    if (subscriber.ArgumentCount != sizeof...(TArgs) || subscriber.MatchesBound == nullptr) {
                        return false;
                    }

                    return subscriber.MatchesBound(handler.Instance, &handler.Method);
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
        std::array<void*, sizeof...(TArgs)> argumentPointers { const_cast<void*>(static_cast<const void*>(std::addressof(args)))... };
        for (Subscriber& subscriber : Subscribers) {
            if (subscriber.ArgumentCount == sizeof...(TArgs)) {
                subscriber.Invoke(argumentPointers.data());
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
        std::function<void(void**)> Invoke;

        /// <summary>
        /// Optional free-function matcher used by remove operations.
        /// </summary>
        std::function<bool(const void*)> MatchesFunction;

        /// <summary>
        /// Optional bound-subscriber matcher used by remove operations.
        /// </summary>
        std::function<bool(const void*, const void*)> MatchesBound;
    };

    /// <summary>
    /// Invokes one stored free or static function by unpacking the argument pointer array.
    /// </summary>
    /// <typeparam name="TArgs">Argument shapes supplied by the transpiled event invocation.</typeparam>
    /// <typeparam name="TIndexes">Compile-time argument indexes used to unpack the argument pointer array.</typeparam>
    /// <param name="handler">Free or static function subscriber.</param>
    /// <param name="arguments">Packed addresses of the invocation arguments.</param>
    template <typename... TArgs, std::size_t... TIndexes>
    static void InvokeFunctionPointer(void (*handler)(TArgs...), void** arguments, std::index_sequence<TIndexes...>) {
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
    static void InvokeBoundMethod(TInstance* instance, void (TInstance::*method)(TArgs...), void** arguments, std::index_sequence<TIndexes...>) {
        (instance->*method)((*static_cast<std::remove_reference_t<TArgs>*>(arguments[TIndexes]))...);
    }

    /// <summary>
    /// Subscribers currently attached to this event.
    /// </summary>
    std::vector<Subscriber> Subscribers;
};
