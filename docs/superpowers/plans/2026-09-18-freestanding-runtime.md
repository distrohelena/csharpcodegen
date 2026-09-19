# Freestanding Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make generated C++ and the shared runtime compile on a freestanding C++20 toolchain (llvm-mos-65816 for the SNES) through a new `freestanding` runtime profile with a codegen-owned provider and software math, proven by host tests and a cross-compile gate.

**Architecture:** The shared runtime stops depending on hosted headers: a small `native_algorithm.hpp` replaces the handful of `<algorithm>`/`<utility>`/`<memory>` helpers it used, every remaining hosted include sits behind the capability that needs it, and hosted-only services fail loudly. A new `runtime/freestanding/` directory holds the provider (string, vector, hash map and set, hash, function, shared pointer, hooks) and portable software math. On the C# side a `Freestanding` runtime kind, a `native-core-boot-freestanding` preset and a `ForbidHostedServices` restriction wire it into the CLI and config writer.

**Tech Stack:** C# (.NET 9, xunit) for the generator; C++20 for the runtime; `sh` integration runner with host `g++` and the SNES toolchain `mos-snes-far-clang++` in the `helengine-snes-toolchain` Docker image.

**Spec:** `docs/superpowers/specs/2026-09-18-freestanding-runtime-design.md`

## Global Constraints

- Repository `C:\dev\helworks\csharpcodegen`, branch `master`, direct commits, no branches or worktrees. Follow `AGENTS.md`: one class per file, substantive XML comments on every C# member, PascalCase fields, braces on the same line, no tuples, no nullable annotations. Never patch generated output.
- Commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. Conventional subjects as in the repo history (`feat(cs2.cpp): …`, `feat(cpp-runtime): …`, `test(…): …`, `docs: …`).
- The freestanding toolchain provides only these C++ headers: `algorithm` (only `min_element`/`max_element`), `array` (no aggregate init), `initializer_list`, `iterator`, `limits` (`numeric_limits<double>::has_infinity` is false and `infinity()` returns 0), `new`, `type_traits` (no `remove_const_t`; use `remove_cv_t`), `typeinfo`, `utility` (only `pair` and `forward`; no `move`, `swap`, `index_sequence`), `exception`, plus C headers `stdint.h`, `stddef.h`, `string.h`, `stdlib.h`, `stdio.h`, `ctype.h`, `errno.h`, `math.h` (declares nothing usable). No `<string>`, `<vector>`, `<functional>`, `<memory>`, `<cstring>`, `<cmath>`, `<cctype>`, `<cstdlib>`? (`cstdlib`, `cstdio`, `cstdint`, `cstddef`, `climits`, `cstdarg` exist), `<chrono>`, `<atomic>`, `<thread>`, `<mutex>`, `<condition_variable>`, `<random>`, `<charconv>`, `<string_view>`, `<stdexcept>`.
- Freestanding provider files may include only `<new>`, `<type_traits>`, `<utility>`, `<initializer_list>`, `<limits>`, `<stdint.h>`, `<stddef.h>`, `<string.h>`, `<stdlib.h>`.
- All new C++ compiles with `-std=c++20 -fno-exceptions -fno-rtti -Wall -Wextra` on host `g++` and with `/usr/lib/llvm-mos-65816/bin/mos-snes-far-clang++ -std=c++20 -fno-exceptions -fno-rtti -Os` in the Docker image `helengine-snes-toolchain` (run with `MSYS_NO_PATHCONV=1 docker run --rm -v "/c/dev/helworks:/hw" -w /hw/csharpcodegen helengine-snes-toolchain …` from Git Bash).
- Integration runner: `sh tests/runtime-capabilities-integration/run.sh <output-dir> /c/dev/helworks/helengine-ps1/third_party/nugget/third_party` with host `g++` on PATH. Output dirs go under `C:\dev\helworks\builds\csharpcodegen\<name>`, never `%TEMP%`.
- Unit tests: `dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~<ClassName>"`. Run the full `cs2.cpp.tests` suite once before the final commit of each C# task.
- Do not inject anything into namespace `std`. All polyfills live in `he_cpp_alg` or `he_cpp_freestanding`.
- Byte-cap unknown-size command output: `COMMAND 2>&1 | tail -c 4000`.
- Deviation from the spec, decided while planning: the runtime owns `HeCppOwnedPtr` itself (no `OwnedPtr` provider contract, so the PS1 and EASTL providers need no change), and the measurement uses a new `native-core-boot-freestanding` preset because a named preset overwrites the runtime profile on the CLI. Task 7 updates the spec text for both.

---

## File Structure

| Path | Responsibility |
|---|---|
| `cs2.cpp/.net.cpp/runtime/native_algorithm.hpp` | `he_cpp_alg`: move, forward, swap, min, max, find_if, remove_if, replace, copy_n, fill_n, transform, distance, address_of, index sequences, invoke, infinity/NaN helpers. No hosted includes. |
| `cs2.cpp/.net.cpp/runtime/native_owned_ptr.hpp` | `HeCppOwnedPtr<T>`: the runtime's own move-only owning pointer. |
| `cs2.cpp/.net.cpp/runtime/native_runtime.hpp` | Capability-gated hosted includes; `HE_CPP_RUNTIME_HAS_HOSTED_SERVICES` default; hash selector guard. |
| `cs2.cpp/.net.cpp/runtime/native_event.hpp`, `native_span.hpp`, `native_hash.hpp`, `native_string.hpp`, `native_list.hpp`, `array.hpp`, `native_datetime.hpp`, `finally.hpp`, `function_pointer.hpp`, `native_memory_ops.hpp` | Use `he_cpp_alg` and `HeCppOwnedPtr`; drop `<algorithm>`, `<utility>`, `<memory>`, `<functional>`, `<array>` includes; gate `<chrono>`. |
| `cs2.cpp/.net.cpp/system/delegate.hpp`, `number.hpp`, `io/memory-stream.cpp`, `io/file-stream.cpp`, `io/path.cpp`, `numerics/vector.hpp` | Same substitutions in the system layer; C header spellings. |
| `cs2.cpp/.net.cpp/system/threading/*.hpp`, `random.hpp`, `guid.hpp`, `runtime/intrinsics/**` | Hosted-services-only guard. |
| `cs2.cpp/.net.cpp/runtime/freestanding/freestanding_hooks.hpp`, `freestanding_hooks_default.cpp` | Provider hooks: `Allocate`, `Free`, `Fail`, `MonotonicMicroseconds`; default allocation over `malloc`/`free`. |
| `cs2.cpp/.net.cpp/runtime/freestanding/freestanding_string.hpp` | `FreestandingString`. |
| `cs2.cpp/.net.cpp/runtime/freestanding/freestanding_vector.hpp` | `FreestandingVector<T>`. |
| `cs2.cpp/.net.cpp/runtime/freestanding/freestanding_hash.hpp` | `FreestandingHash<T>` (FNV-1a). |
| `cs2.cpp/.net.cpp/runtime/freestanding/freestanding_hash_table.hpp`, `freestanding_hash_map.hpp`, `freestanding_hash_set.hpp` | Open-addressing table and its map/set fronts. |
| `cs2.cpp/.net.cpp/runtime/freestanding/freestanding_function.hpp` | `FreestandingFunction<Signature>`. |
| `cs2.cpp/.net.cpp/runtime/freestanding/freestanding_shared_ptr.hpp` | `FreestandingSharedPtr<T>` (moved from the test fixture). |
| `cs2.cpp/.net.cpp/runtime/freestanding/freestanding_provider.hpp` | The `he_cpp_custom` contract aliases. |
| `cs2.cpp/.net.cpp/runtime/freestanding/freestanding_math.hpp`, `freestanding_math.cpp` | Portable C math with C linkage. |
| `tests/runtime-capabilities-integration/freestanding/helcpp_config.hpp` | Config for the freestanding variant. |
| `tests/runtime-capabilities-integration/poison/<header>` | `#error` headers for every hosted name. |
| `tests/runtime-capabilities-integration/freestanding_provider_smoke.cpp`, `freestanding_math_smoke.cpp`, `algorithm_smoke.cpp` | Behavioural tests. |
| `tests/runtime-capabilities-integration/run.sh`, `README.md` | Freestanding variant, poison gate, `TARGET_CXX` freestanding cross-compile. |
| `cs2.cpp/model/CPPRuntimeKind.cs`, `CPPRuntimeProfile.cs`, `CPPRestrictionProfile.cs` | `Freestanding` kind and factory; `ForbidHostedServices`. |
| `cs2.cpp/CPPRuntimeOptionResolver.cs`, `CPPGeneratedConfigWriter.cs`, `CPPRestrictionValidator.cs`, `CPPConversionPresetCatalog.cs`, `codegen/CodegenCliOptionsBuilder.cs`, `codegen/Program.cs` | Defaults, defines, validation, preset, CLI. |
| `cs2.cpp.tests/CPPFreestandingRuntimeProfileTests.cs`, `CPPRestrictionValidatorTests.cs`, `CPPGeneratedConfigWriterTests.cs`, `CPPConversionPresetCatalogTests.cs` | Unit tests. |
| `docs/generic-runtime-capabilities.md`, `docs/superpowers/reports/<date>-freestanding-core-compile.md` | Documentation and the final measurement. |

---

### Task 1: `native_algorithm.hpp` polyfills replace the hosted helpers the runtime uses

**Files:**
- Create: `cs2.cpp/.net.cpp/runtime/native_algorithm.hpp`, `tests/runtime-capabilities-integration/algorithm_smoke.cpp`
- Modify: `cs2.cpp/.net.cpp/runtime/native_runtime.hpp:3-5,150-160,247-260`, `runtime/native_event.hpp:4-10,53,80,135-140,163-168,184-185,226,240,247`, `runtime/native_span.hpp:3-10,156,160,297`, `runtime/native_string.hpp:3-7,303,346`, `runtime/native_list.hpp:3-6,178,183,191,196,363,371,379`, `runtime/array.hpp:3,48,73,97,120`, `runtime/finally.hpp:17,28,54`, `runtime/function_pointer.hpp:47`, `system/delegate.hpp:5,15,20,24`, `system/io/memory-stream.cpp`, `system/io/file-stream.cpp:227,366`, `system/io/path.cpp:5-6,32,160`, `system/numerics/vector.hpp:9,46-81,637,651`, `tests/runtime-capabilities-integration/run.sh`

**Interfaces:**
- Produces: namespace `he_cpp_alg` with
  `template<class T> constexpr std::remove_reference_t<T>&& Move(T&&)`,
  `template<class T> constexpr T&& Forward(std::remove_reference_t<T>&)` and the rvalue overload,
  `template<class T> void Swap(T&, T&)`,
  `template<class T> constexpr const T& Min(const T&, const T&)`, `Max`,
  `template<class It, class Pred> It FindIf(It first, It last, Pred)`,
  `template<class It, class Pred> It RemoveIf(It first, It last, Pred)`,
  `template<class It, class T> void Replace(It first, It last, const T& oldValue, const T& newValue)`,
  `template<class In, class Size, class Out> Out CopyN(In first, Size count, Out out)`,
  `template<class Out, class Size, class T> Out FillN(Out first, Size count, const T&)`,
  `template<class In, class Out, class Op> Out Transform(In first, In last, Out out, Op)`,
  `template<class It> ptrdiff_t Distance(It first, It last)`,
  `template<class T> T* AddressOf(T&)`,
  `template<size_t... I> struct IndexSequence {}`, `template<size_t N> using MakeIndexSequence`, `template<class... T> using IndexSequenceFor`,
  `template<class F, class... A> decltype(auto) Invoke(F&&, A&&...)` supporting member function pointers with pointer or reference receivers,
  `template<class T> constexpr T Infinity()`, `template<class T> constexpr T QuietNaN()`.

- [ ] **Step 1: Write the failing smoke test**

`tests/runtime-capabilities-integration/algorithm_smoke.cpp`:

```cpp
#include "runtime/native_algorithm.hpp"
#include <stddef.h>

struct Counter {
    int Value;
    int Double(int factor) { return Value * factor; }
};

struct MoveOnly {
    int* Target;
    explicit MoveOnly(int* target) : Target(target) {}
    MoveOnly(MoveOnly&& other) noexcept : Target(other.Target) { other.Target = nullptr; }
    MoveOnly(const MoveOnly&) = delete;
};

template <size_t... Indexes>
int SumIndexes(he_cpp_alg::IndexSequence<Indexes...>) {
    return (0 + ... + static_cast<int>(Indexes));
}

int algorithm_smoke() {
    int a = 1, b = 2;
    he_cpp_alg::Swap(a, b);
    if (a != 2 || b != 1) return 1;
    if (he_cpp_alg::Min(3, 4) != 3 || he_cpp_alg::Max(3, 4) != 4) return 2;
    int values[6] = {5, 1, 4, 1, 3, 1};
    int* found = he_cpp_alg::FindIf(values, values + 6, [](int v) { return v == 4; });
    if (found != values + 2) return 3;
    if (he_cpp_alg::Distance(values, found) != 2) return 4;
    int* newEnd = he_cpp_alg::RemoveIf(values, values + 6, [](int v) { return v == 1; });
    if (newEnd != values + 3 || values[0] != 5 || values[1] != 4 || values[2] != 3) return 5;
    he_cpp_alg::Replace(values, values + 3, 4, 9);
    if (values[1] != 9) return 6;
    int copy[3] = {0, 0, 0};
    he_cpp_alg::CopyN(values, 3, copy);
    if (copy[0] != 5 || copy[2] != 3) return 7;
    he_cpp_alg::FillN(copy, 3, 7);
    if (copy[0] != 7 || copy[2] != 7) return 8;
    he_cpp_alg::Transform(values, values + 3, copy, [](int v) { return v + 1; });
    if (copy[0] != 6 || copy[1] != 10) return 9;
    int target = 0;
    MoveOnly source(&target);
    MoveOnly moved(he_cpp_alg::Move(source));
    if (moved.Target != &target || source.Target != nullptr) return 10;
    if (he_cpp_alg::AddressOf(target) != &target) return 11;
    if (SumIndexes(he_cpp_alg::MakeIndexSequence<4>{}) != 6) return 12;
    if (SumIndexes(he_cpp_alg::IndexSequenceFor<int, int, int>{}) != 3) return 13;
    Counter counter{21};
    if (he_cpp_alg::Invoke(&Counter::Double, &counter, 2) != 42) return 14;
    if (he_cpp_alg::Invoke(&Counter::Double, counter, 3) != 63) return 15;
    if (he_cpp_alg::Invoke([](int v) { return v + 1; }, 1) != 2) return 16;
    double infinity = he_cpp_alg::Infinity<double>();
    double nan = he_cpp_alg::QuietNaN<double>();
    if (!(infinity > 1.0e308) || nan == nan) return 17;
    return 0;
}

#if defined(HE_CPP_TEST_HOST)
int main() { return algorithm_smoke(); }
#endif
```

Append to `run.sh`, right before the final `echo 'Runtime capability fixtures passed.'`:

```sh
"$cxx" -std=c++20 -fno-exceptions -fno-rtti -Wall -Wextra -Werror -DHE_CPP_TEST_HOST \
    -I"$fixture/hosted" -I"$runtime" \
    "$fixture/algorithm_smoke.cpp" -o "$output/algorithm-smoke"
"$output/algorithm-smoke"
```

- [ ] **Step 2: Run the runner to verify it fails**

Run: `cd /c/dev/helworks/csharpcodegen && sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task1 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 1500`

Expected: fails compiling `algorithm_smoke.cpp` with `runtime/native_algorithm.hpp: No such file or directory`.

- [ ] **Step 3: Write the polyfill header**

`cs2.cpp/.net.cpp/runtime/native_algorithm.hpp`:

```cpp
#pragma once

// Freestanding replacements for the few <algorithm>, <utility> and <memory>
// helpers the shared runtime uses. Targets such as the 65816 toolchain ship
// only a partial standard library (no std::move, std::min, std::find_if,
// std::index_sequence), so the runtime routes through these names on every
// target. Nothing here touches namespace std.

#include <limits>
#include <stddef.h>
#include <type_traits>

namespace he_cpp_alg {

/// <summary>Casts a value to an rvalue reference so it can be moved from.</summary>
template <typename T>
constexpr std::remove_reference_t<T>&& Move(T&& value) noexcept {
    return static_cast<std::remove_reference_t<T>&&>(value);
}

/// <summary>Forwards an lvalue as the deduced reference type.</summary>
template <typename T>
constexpr T&& Forward(std::remove_reference_t<T>& value) noexcept {
    return static_cast<T&&>(value);
}

/// <summary>Forwards an rvalue as the deduced reference type.</summary>
template <typename T>
constexpr T&& Forward(std::remove_reference_t<T>&& value) noexcept {
    static_assert(!std::is_lvalue_reference_v<T>, "Cannot forward an rvalue as an lvalue.");
    return static_cast<T&&>(value);
}

/// <summary>Exchanges two values through a temporary.</summary>
template <typename T>
void Swap(T& left, T& right) {
    T temporary(Move(left));
    left = Move(right);
    right = Move(temporary);
}

/// <summary>Returns the smaller of two values, the first on ties.</summary>
template <typename T>
constexpr const T& Min(const T& left, const T& right) {
    return right < left ? right : left;
}

/// <summary>Returns the larger of two values, the first on ties.</summary>
template <typename T>
constexpr const T& Max(const T& left, const T& right) {
    return left < right ? right : left;
}

/// <summary>Returns the first iterator whose element satisfies the predicate, or last.</summary>
template <typename TIterator, typename TPredicate>
TIterator FindIf(TIterator first, TIterator last, TPredicate predicate) {
    for (; first != last; ++first) {
        if (predicate(*first)) {
            return first;
        }
    }
    return last;
}

/// <summary>Moves the elements that do not satisfy the predicate to the front and returns the new end.</summary>
template <typename TIterator, typename TPredicate>
TIterator RemoveIf(TIterator first, TIterator last, TPredicate predicate) {
    TIterator writer = first;
    for (; first != last; ++first) {
        if (!predicate(*first)) {
            if (writer != first) {
                *writer = Move(*first);
            }
            ++writer;
        }
    }
    return writer;
}

/// <summary>Replaces every element equal to oldValue with newValue.</summary>
template <typename TIterator, typename T>
void Replace(TIterator first, TIterator last, const T& oldValue, const T& newValue) {
    for (; first != last; ++first) {
        if (*first == oldValue) {
            *first = newValue;
        }
    }
}

/// <summary>Copies count elements from first to output and returns the output end.</summary>
template <typename TInput, typename TSize, typename TOutput>
TOutput CopyN(TInput first, TSize count, TOutput output) {
    for (TSize index = 0; index < count; ++index) {
        *output = *first;
        ++output;
        ++first;
    }
    return output;
}

/// <summary>Assigns value to count elements starting at first and returns the end.</summary>
template <typename TOutput, typename TSize, typename T>
TOutput FillN(TOutput first, TSize count, const T& value) {
    for (TSize index = 0; index < count; ++index) {
        *first = value;
        ++first;
    }
    return first;
}

/// <summary>Applies operation to each element and writes the results to output.</summary>
template <typename TInput, typename TOutput, typename TOperation>
TOutput Transform(TInput first, TInput last, TOutput output, TOperation operation) {
    for (; first != last; ++first) {
        *output = operation(*first);
        ++output;
    }
    return output;
}

/// <summary>Counts the steps from first to last.</summary>
template <typename TIterator>
ptrdiff_t Distance(TIterator first, TIterator last) {
    ptrdiff_t count = 0;
    for (; first != last; ++first) {
        ++count;
    }
    return count;
}

/// <summary>Returns the address of a reference even when operator& is overloaded.</summary>
template <typename T>
T* AddressOf(T& value) noexcept {
    return __builtin_addressof(value);
}

/// <summary>Compile-time index pack used to unpack argument arrays.</summary>
template <size_t... Indexes>
struct IndexSequence {
    static constexpr size_t Size = sizeof...(Indexes);
};

namespace detail {

template <size_t N, size_t... Indexes>
struct MakeIndexSequenceImpl : MakeIndexSequenceImpl<N - 1, N - 1, Indexes...> {
};

template <size_t... Indexes>
struct MakeIndexSequenceImpl<0, Indexes...> {
    using Type = IndexSequence<Indexes...>;
};

}

/// <summary>IndexSequence of 0..N-1.</summary>
template <size_t N>
using MakeIndexSequence = typename detail::MakeIndexSequenceImpl<N>::Type;

/// <summary>IndexSequence with one index per type in the pack.</summary>
template <typename... T>
using IndexSequenceFor = MakeIndexSequence<sizeof...(T)>;

/// <summary>Invokes a member function pointer on a pointer receiver.</summary>
template <typename TResult, typename TClass, typename... TParams, typename TReceiver, typename... TArgs,
          std::enable_if_t<std::is_pointer_v<std::remove_reference_t<TReceiver>>, int> = 0>
decltype(auto) Invoke(TResult (TClass::*method)(TParams...), TReceiver&& receiver, TArgs&&... args) {
    return (receiver->*method)(Forward<TArgs>(args)...);
}

/// <summary>Invokes a member function pointer on a reference receiver.</summary>
template <typename TResult, typename TClass, typename... TParams, typename TReceiver, typename... TArgs,
          std::enable_if_t<!std::is_pointer_v<std::remove_reference_t<TReceiver>>, int> = 0>
decltype(auto) Invoke(TResult (TClass::*method)(TParams...), TReceiver&& receiver, TArgs&&... args) {
    return (receiver.*method)(Forward<TArgs>(args)...);
}

/// <summary>Invokes a const member function pointer on a pointer receiver.</summary>
template <typename TResult, typename TClass, typename... TParams, typename TReceiver, typename... TArgs,
          std::enable_if_t<std::is_pointer_v<std::remove_reference_t<TReceiver>>, int> = 0>
decltype(auto) Invoke(TResult (TClass::*method)(TParams...) const, TReceiver&& receiver, TArgs&&... args) {
    return (receiver->*method)(Forward<TArgs>(args)...);
}

/// <summary>Invokes a const member function pointer on a reference receiver.</summary>
template <typename TResult, typename TClass, typename... TParams, typename TReceiver, typename... TArgs,
          std::enable_if_t<!std::is_pointer_v<std::remove_reference_t<TReceiver>>, int> = 0>
decltype(auto) Invoke(TResult (TClass::*method)(TParams...) const, TReceiver&& receiver, TArgs&&... args) {
    return (receiver.*method)(Forward<TArgs>(args)...);
}

/// <summary>Invokes any other callable with the given arguments.</summary>
template <typename TCallable, typename... TArgs,
          std::enable_if_t<!std::is_member_function_pointer_v<std::remove_cv_t<std::remove_reference_t<TCallable>>>, int> = 0>
decltype(auto) Invoke(TCallable&& callable, TArgs&&... args) {
    return Forward<TCallable>(callable)(Forward<TArgs>(args)...);
}

/// <summary>Positive infinity even where numeric_limits reports none.</summary>
template <typename T>
constexpr T Infinity() {
    if constexpr (std::numeric_limits<T>::has_infinity) {
        return std::numeric_limits<T>::infinity();
    } else {
        return static_cast<T>(__builtin_inf());
    }
}

/// <summary>Quiet NaN even where numeric_limits reports none.</summary>
template <typename T>
constexpr T QuietNaN() {
    if constexpr (std::numeric_limits<T>::has_quiet_NaN) {
        return std::numeric_limits<T>::quiet_NaN();
    } else {
        return static_cast<T>(__builtin_nan(""));
    }
}

}
```

- [ ] **Step 4: Run the smoke test alone to verify it passes**

Run: `cd /c/dev/helworks/csharpcodegen && g++ -std=c++20 -fno-exceptions -fno-rtti -Wall -Wextra -Werror -DHE_CPP_TEST_HOST -Itests/runtime-capabilities-integration/hosted -Ics2.cpp/.net.cpp tests/runtime-capabilities-integration/algorithm_smoke.cpp -o /c/dev/helworks/builds/csharpcodegen/rc-task1/algorithm-smoke && /c/dev/helworks/builds/csharpcodegen/rc-task1/algorithm-smoke; echo "exit=$?"`

Expected: `exit=0`.

- [ ] **Step 5: Route the shared runtime through `he_cpp_alg`**

Apply these exact substitutions (each file also gains `#include "native_algorithm.hpp"` with the right relative path next to its other runtime includes, and loses the hosted include named):

| File | Remove include | Replace |
|---|---|---|
| `runtime/native_runtime.hpp` | keep `<functional>` and `<memory>` for now (Task 2 gates them) | lines 247-260: replace the whole `he_cpp_bind_front` block with the single non-`bind_front` form using `he_cpp_alg::Invoke` and `he_cpp_alg::Forward`; delete the `__cpp_lib_bind_front` branch |
| `runtime/native_event.hpp` | `<algorithm>`, `<array>` | `std::remove_if` → `he_cpp_alg::RemoveIf`; `std::index_sequence_for<TArgs...>` → `he_cpp_alg::IndexSequenceFor<TArgs...>`; `std::index_sequence<TIndexes...>` → `he_cpp_alg::IndexSequence<TIndexes...>`; `std::addressof` → `he_cpp_alg::AddressOf`; line 184 `std::array<void*, sizeof...(TArgs)> argumentPointers { … };` → `void* argumentPointers[sizeof...(TArgs) == 0 ? 1 : sizeof...(TArgs)] = { const_cast<void*>(static_cast<const void*>(he_cpp_alg::AddressOf(args)))... };` and `argumentPointers.data()` → `argumentPointers` |
| `runtime/native_span.hpp` | `<algorithm>`, `<memory>`, `<utility>` | `std::copy_n` → `he_cpp_alg::CopyN`, `std::fill_n` → `he_cpp_alg::FillN`, `std::min` → `he_cpp_alg::Min` |
| `runtime/native_string.hpp` | `<algorithm>`, `<utility>` | `std::transform` → `he_cpp_alg::Transform`, `std::replace` → `he_cpp_alg::Replace`; any `std::move`/`std::forward` → `he_cpp_alg::Move`/`Forward`; `std::remove_const_t` → `std::remove_cv_t` |
| `runtime/native_list.hpp` | `<algorithm>` | `std::find_if` → `he_cpp_alg::FindIf`, `std::distance` → `he_cpp_alg::Distance` |
| `runtime/array.hpp` | `<algorithm>` | `std::min` → `he_cpp_alg::Min` |
| `runtime/finally.hpp`, `runtime/function_pointer.hpp`, `system/delegate.hpp` | `<utility>` | `std::move` → `he_cpp_alg::Move`, `std::forward` → `he_cpp_alg::Forward` |
| `system/io/memory-stream.cpp`, `system/io/file-stream.cpp` | `<algorithm>` | `std::min` → `he_cpp_alg::Min`; `std::replace` in file-stream.cpp line 41 stays inside the hosted-file-system block, replace it too |
| `system/io/path.cpp` | `<algorithm>`, `<cstdlib>` → `<stdlib.h>` | `std::replace` → `he_cpp_alg::Replace` |
| `system/numerics/vector.hpp` | `<algorithm>` | `std::min`/`std::max` → `he_cpp_alg::Min`/`Max` |
| everywhere under `runtime/` and `system/` | | `std::remove_const_t` → `std::remove_cv_t` |

Then: `grep -rnE "std::(move|forward|swap|min|max|find_if|remove_if|replace|copy_n|fill_n|transform|distance|addressof|index_sequence|make_index_sequence|index_sequence_for|invoke|bind_front|remove_const_t)\b" cs2.cpp/.net.cpp/runtime cs2.cpp/.net.cpp/system | grep -vE "threading/|intrinsics/|regular_expressions/|random.hpp"` must print nothing (the `#if HE_CPP_USE_STD_*` blocks that select `std::function`/`std::shared_ptr`/`std::hash` types are not in this list).

- [ ] **Step 6: Run the full runner to verify every existing variant still passes**

Run: `cd /c/dev/helworks/csharpcodegen && sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task1 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 1500`

Expected: ends with `Runtime capability fixtures passed.` (hosted, hosted-no-unwind, custom, mixed selections, math modes and the new algorithm smoke all run).

- [ ] **Step 7: Run the C# regression tests that compile generated output**

Run: `cd /c/dev/helworks/csharpcodegen && dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPCompileValidationRegressionTests|FullyQualifiedName~CPPRuntimeCapabilityConversionTests" 2>&1 | tail -c 1500`

Expected: `Passed!` with zero failures.

- [ ] **Step 8: Commit**

```bash
git add cs2.cpp/.net.cpp/runtime/native_algorithm.hpp tests/runtime-capabilities-integration/algorithm_smoke.cpp tests/runtime-capabilities-integration/run.sh cs2.cpp/.net.cpp/runtime cs2.cpp/.net.cpp/system
git commit -F - <<'EOF'
feat(cpp-runtime): replace hosted algorithm and utility helpers with he_cpp_alg

The 65816 toolchain's <algorithm> and <utility> lack std::move, std::min,
std::find_if and index sequences, so the shared runtime now routes through
its own freestanding polyfills on every target.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 2: Capability-gated hosted includes and the hosted-services guard

**Files:**
- Create: `cs2.cpp/.net.cpp/runtime/native_owned_ptr.hpp`, `tests/runtime-capabilities-integration/poison/` (one file per hosted header), `tests/runtime-capabilities-integration/freestanding/helcpp_config.hpp`, `tests/runtime-capabilities-integration/hosted_services_guard.cpp`
- Modify: `runtime/native_runtime.hpp:3-5,150-160`, `runtime/native_event.hpp` (`std::unique_ptr` sites), `runtime/native_hash.hpp:5`, `runtime/native_datetime.hpp:3,33-36`, `runtime/native_memory_ops.hpp:4`, `system/number.hpp` (infinity/NaN sites), `system/guid.hpp:1-13`, `system/random.hpp:1-6`, `system/threading/*.hpp`, `system/runtime/intrinsics/vector128.hpp`, `vector256.hpp`, `vector512.hpp`, `x86/*.hpp`, `system/text/regular_expressions/regex.hpp`, `tests/runtime-capabilities-integration/run.sh`, `docs/generic-runtime-capabilities.md`

**Interfaces:**
- Consumes: `he_cpp_alg::Move`.
- Produces: `template<class T> using HeCppOwnedPtr = he_cpp_runtime_detail::OwnedPtr<T>` with `get`, `release`, `reset`, `operator->`, `operator*`, `explicit operator bool`, move only; macro `HE_CPP_RUNTIME_HAS_HOSTED_SERVICES` (defaults to 1 in `native_runtime.hpp` when the config does not define it); hosted-services-only headers fail with `#error` when it is 0. The `freestanding/helcpp_config.hpp` variant is the config every later freestanding fixture build uses.

- [ ] **Step 1: Write the poison headers and the failing guard test**

Create `tests/runtime-capabilities-integration/poison/` with these files, each containing exactly the two lines below with its own name: `string`, `vector`, `functional`, `memory`, `unordered_map`, `unordered_set`, `stdexcept`, `chrono`, `atomic`, `thread`, `mutex`, `condition_variable`, `random`, `charconv`, `string_view`, `cmath`, `cstring`, `cctype`, `cerrno`, `regex`, `algorithm`, `utility`, `array`, `iostream`, `sstream`, `fstream`, `filesystem`.

```cpp
#pragma once
#error "hosted header included under the freestanding runtime"
```

(`algorithm`, `utility` and `array` are poisoned too: the runtime must not need them after Task 1, and the 65816 versions are unusable anyway.)

Create `tests/runtime-capabilities-integration/freestanding/helcpp_config.hpp`:

```cpp
#pragma once
#define HE_CPP_RUNTIME_FREESTANDING 1
#define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 0
#define HE_CPP_USE_STD_STRING 0
#define HE_CPP_USE_STD_VECTOR 0
#define HE_CPP_USE_STD_UNORDERED_MAP 0
#define HE_CPP_USE_STD_UNORDERED_SET 0
#define HE_CPP_USE_STD_FUNCTION 0
#define HE_CPP_USE_STD_SHARED_PTR 0
#define HE_CPP_USE_STD_CHRONO 0
#define HE_CPP_USE_STD_MATH 0
#define HE_CPP_USE_HOSTED_FILE_SYSTEM 0
#define HE_CPP_USE_EXCEPTIONS 0
#define HE_CPP_USE_RTTI 0
#define HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES 1
#define HE_CPP_RUNTIME_PROVIDER_HEADER "runtime/freestanding/freestanding_provider.hpp"
#define HE_CPP_RUNTIME_MATH_HEADER "runtime/freestanding/freestanding_math.hpp"
```

Create `tests/runtime-capabilities-integration/hosted_services_guard.cpp`:

```cpp
// Must fail to compile under the freestanding config: threading is a hosted service.
#include "system/threading/interlocked.hpp"
int main() { return 0; }
```

Append to `run.sh` before the final echo (this block will fail until the provider exists in Task 3; that is expected and the reason the block is written first):

```sh
freestanding_flags="-std=c++20 -fno-exceptions -fno-rtti -Wall -Wextra -Werror -DHE_CPP_TEST_HOST"
freestanding_includes="-I$fixture/poison -I$fixture/freestanding -I$fixture -I$runtime"

if "$cxx" $freestanding_flags $freestanding_includes \
    -c "$fixture/hosted_services_guard.cpp" -o "$output/hosted-services-guard.o" \
    >"$output/hosted-services-guard.log" 2>&1; then
    echo 'Hosted threading service unexpectedly compiled under the freestanding runtime.' >&2
    exit 1
fi
grep -q 'hosted threading or OS facilities' "$output/hosted-services-guard.log"

"$cxx" $freestanding_flags $freestanding_includes \
    "$fixture/algorithm_smoke.cpp" -o "$output/freestanding-algorithm-smoke"
"$output/freestanding-algorithm-smoke"
```

- [ ] **Step 2: Run the runner to verify it fails on the guard message**

Run: `cd /c/dev/helworks/csharpcodegen && sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task2 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 800`

Expected: the `grep -q 'hosted threading or OS facilities'` fails (the log contains the poison `#error` for `<atomic>` instead), so the runner exits non-zero after the guard step.

- [ ] **Step 3: Add the owned pointer**

`cs2.cpp/.net.cpp/runtime/native_owned_ptr.hpp`:

```cpp
#pragma once

#include "native_algorithm.hpp"

namespace he_cpp_runtime_detail {

/// <summary>
/// Move-only owning pointer used by the shared runtime where the standard
/// library's unique_ptr is unavailable. Deletes the pointee with delete.
/// </summary>
template <typename T>
class OwnedPtr {
public:
    OwnedPtr() noexcept : Pointer(nullptr) {}
    explicit OwnedPtr(T* pointer) noexcept : Pointer(pointer) {}
    OwnedPtr(const OwnedPtr&) = delete;
    OwnedPtr& operator=(const OwnedPtr&) = delete;
    OwnedPtr(OwnedPtr&& other) noexcept : Pointer(other.Pointer) { other.Pointer = nullptr; }
    OwnedPtr& operator=(OwnedPtr&& other) noexcept {
        if (this != &other) {
            reset(other.Pointer);
            other.Pointer = nullptr;
        }
        return *this;
    }
    ~OwnedPtr() { reset(); }

    /// <summary>Returns the owned pointer without releasing ownership.</summary>
    T* get() const noexcept { return Pointer; }
    /// <summary>Gives up ownership and returns the pointer.</summary>
    T* release() noexcept { T* pointer = Pointer; Pointer = nullptr; return pointer; }
    /// <summary>Deletes the current pointee and takes ownership of the new one.</summary>
    void reset(T* pointer = nullptr) noexcept { T* previous = Pointer; Pointer = pointer; delete previous; }
    T* operator->() const noexcept { return Pointer; }
    T& operator*() const noexcept { return *Pointer; }
    explicit operator bool() const noexcept { return Pointer != nullptr; }

private:
    T* Pointer;
};

}

template <typename T>
using HeCppOwnedPtr = he_cpp_runtime_detail::OwnedPtr<T>;
```

- [ ] **Step 4: Gate the hosted includes and add the hosted-services macro**

In `runtime/native_runtime.hpp`:

- Replace lines 3-5 (`#include <functional>`, `<type_traits>`, `<memory>`) with:

```cpp
#include <type_traits>
#include "native_algorithm.hpp"
#include "native_owned_ptr.hpp"
```

- After the capability defaults block (after the `HE_CPP_USE_STD_SHARED_PTR` default), add:

```cpp
#ifndef HE_CPP_RUNTIME_HAS_HOSTED_SERVICES
#define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 1
#endif
```

- Change the standard includes block so that `<functional>` is included when `HE_CPP_USE_STD_FUNCTION || HE_CPP_USE_STD_STRING` (std::hash needs it), and `<memory>` when `HE_CPP_USE_STD_SHARED_PTR`.
- Wrap the `HashSelector<TValue, true> : std::hash<TValue>` specialization (lines ~150-153) in `#if HE_CPP_USE_STD_STRING … #endif`.

In `runtime/native_event.hpp`: remove `<functional>` and `<memory>`; every `std::unique_ptr<Subscriber>` becomes `HeCppOwnedPtr<Subscriber>`.

In `runtime/native_hash.hpp`: remove `<functional>` and `<utility>` (native_runtime already provides the hash alias).

In `runtime/native_datetime.hpp`: include `<chrono>` only under `#if HE_CPP_USE_STD_CHRONO`; include `"native_runtime.hpp"`; in `CurrentUnixMilliseconds` add:

```cpp
#if HE_CPP_USE_STD_CHRONO
        using namespace std::chrono;
        return duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
#else
        // Providers expose a monotonic clock only; DateTime::Now measures elapsed time, not wall time.
        return static_cast<int64_t>(he_cpp_custom::MonotonicMicroseconds() / 1000u);
#endif
```

In `runtime/native_memory_ops.hpp`: `#include <cstring>` → `#include <string.h>` and the three `std::mem*` calls → `::mem*`.

In `system/number.hpp`: every `std::numeric_limits<T>::infinity()` → `he_cpp_alg::Infinity<T>()` and `quiet_NaN()` → `he_cpp_alg::QuietNaN<T>()` (include `"../runtime/native_algorithm.hpp"`).

Hosted-services-only headers. Add at the very top (after `#pragma once`/guard) of `system/guid.hpp`, `system/random.hpp`, `system/threading/interlocked.hpp`, `spin_lock.hpp`, `spin_wait.hpp`, `thread.hpp`, `auto_reset_event.hpp`, `volatile.hpp`, `system/runtime/intrinsics/vector128.hpp`, `vector256.hpp`, `vector512.hpp`, `x86/sse.hpp`, `sse41.hpp`, `avx.hpp`, `avx2.hpp`, `system/text/regular_expressions/regex.hpp`:

```cpp
#include "../../runtime/native_runtime.hpp"   // adjust the relative path per file
#if !HE_CPP_RUNTIME_HAS_HOSTED_SERVICES
#error "This runtime service needs hosted threading or OS facilities and is unavailable under the freestanding runtime."
#endif
```

- [ ] **Step 5: Run the runner; the guard passes, the smoke passes, existing variants pass**

Run: `cd /c/dev/helworks/csharpcodegen && sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task2 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 1200`

Expected: `Runtime capability fixtures passed.` The guard log contains `hosted threading or OS facilities`; `freestanding-algorithm-smoke` exits 0 (it includes only `native_algorithm.hpp`, which must survive the poison directory).

- [ ] **Step 6: Run the C# tests that compile generated output**

Run: `cd /c/dev/helworks/csharpcodegen && dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPCompileValidationRegressionTests|FullyQualifiedName~CPPRuntimeCapabilityConversionTests" 2>&1 | tail -c 1200`

Expected: `Passed!`.

- [ ] **Step 7: Document the macro and the owned pointer**

In `docs/generic-runtime-capabilities.md`, after the provider contract section add:

```markdown
## Hosted services

`HE_CPP_RUNTIME_HAS_HOSTED_SERVICES` (default 1) marks targets with threads,
atomics, an OS random device and x86 intrinsics. The `freestanding` runtime
sets it to 0, and including `system/threading/*`, `system/random.hpp`,
`system/guid.hpp`, `system/runtime/intrinsics/*` or the regex runtime then
fails with a clear `#error`. The runtime owns `HeCppOwnedPtr<T>` for its own
move-only ownership; providers do not supply it.
```

- [ ] **Step 8: Commit**

```bash
git add cs2.cpp/.net.cpp tests/runtime-capabilities-integration docs/generic-runtime-capabilities.md
git commit -F - <<'EOF'
feat(cpp-runtime): gate hosted includes behind capabilities and guard hosted services

Adds HeCppOwnedPtr, the HE_CPP_RUNTIME_HAS_HOSTED_SERVICES macro, the
poison-include test directory and the freestanding fixture config.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 3: Freestanding hooks, hash, string and vector

**Files:**
- Create: `cs2.cpp/.net.cpp/runtime/freestanding/freestanding_hooks.hpp`, `freestanding_hooks_default.cpp`, `freestanding_hash.hpp`, `freestanding_string.hpp`, `freestanding_vector.hpp`, `tests/runtime-capabilities-integration/freestanding_provider_smoke.cpp`
- Modify: `tests/runtime-capabilities-integration/run.sh`

**Interfaces:**
- Consumes: `he_cpp_alg`.
- Produces, in namespace `he_cpp_freestanding`: `FreestandingString`, `template<class T> class FreestandingVector`, `template<class T> struct FreestandingHash`; in `he_cpp_custom`: `void* Allocate(size_t)`, `void Free(void*)`, `[[noreturn]] void Fail(const char*)`, `uint64_t MonotonicMicroseconds()` (declarations; Task 5 assembles the provider header). `FreestandingString::npos == static_cast<size_t>(-1)`.

- [ ] **Step 1: Write the failing provider smoke test (string and vector part)**

`tests/runtime-capabilities-integration/freestanding_provider_smoke.cpp`:

```cpp
#include "runtime/freestanding/freestanding_string.hpp"
#include "runtime/freestanding/freestanding_vector.hpp"
#include "runtime/freestanding/freestanding_hash.hpp"
#include <string.h>

using he_cpp_freestanding::FreestandingString;
using he_cpp_freestanding::FreestandingVector;
using he_cpp_freestanding::FreestandingHash;

struct Tracked {
    inline static int Alive = 0;
    int Id;
    explicit Tracked(int id) : Id(id) { ++Alive; }
    Tracked(const Tracked& other) : Id(other.Id) { ++Alive; }
    Tracked(Tracked&& other) noexcept : Id(other.Id) { other.Id = -1; ++Alive; }
    Tracked& operator=(const Tracked&) = default;
    ~Tracked() { --Alive; }
};

int string_smoke() {
    FreestandingString empty;
    if (!empty.empty() || empty.size() != 0 || empty.c_str()[0] != '\0') return 1;
    FreestandingString text("cube");
    for (int index = 0; index < 40; ++index) text += "_scene";
    if (text.size() != 244 || text.length() != 244) return 2;
    FreestandingString copy = text;
    text[0] = 'C';
    if (copy[0] != 'c' || text[0] != 'C' || copy.size() != 244) return 3;
    FreestandingString moved(static_cast<FreestandingString&&>(copy));
    if (moved.size() != 244 || copy.size() != 0) return 4;
    if (FreestandingString("abc").compare(FreestandingString("abd")) >= 0) return 5;
    if (!(FreestandingString("abc") == "abc") || FreestandingString("abc") != FreestandingString("abc")) return 6;
    if (!(FreestandingString("abc") < FreestandingString("abd"))) return 7;
    FreestandingString hello("hello world");
    if (hello.find("world") != 6 || hello.find('z') != FreestandingString::npos) return 8;
    if (hello.find_first_of("ow") != 4) return 9;
    if (hello.substr(6) != "world" || hello.substr(0, 5) != "hello" || hello.substr(11).size() != 0) return 10;
    hello.erase(5, 6);
    if (hello != "hello") return 11;
    hello.insert(5, " there");
    if (hello != "hello there") return 12;
    hello.replace(0, 5, "HELLO");
    if (hello != "HELLO there") return 13;
    hello.push_back('!');
    if (hello.back() != '!' || hello.front() != 'H') return 14;
    hello.pop_back();
    hello.resize(5);
    if (hello != "HELLO") return 15;
    hello.resize(7, 'x');
    if (hello != "HELLOxx") return 16;
    hello.reserve(100);
    if (hello.capacity() < 100 || hello != "HELLOxx") return 17;
    hello.clear();
    if (!hello.empty()) return 18;
    FreestandingString a("a"), b("b");
    a.swap(b);
    if (a != "b" || b != "a") return 19;
    FreestandingString joined = FreestandingString("x") + "y" + FreestandingString("z");
    if (joined != "xyz") return 20;
    FreestandingString counted(3, 'q');
    if (counted != "qqq") return 21;
    FreestandingString ranged("abcdef", 3);
    if (ranged != "abc") return 22;
    int chars = 0;
    for (char c : ranged) { (void)c; ++chars; }
    if (chars != 3) return 23;
    ranged.assign("zz");
    ranged.append("yy");
    if (ranged != "zzyy" || ranged.at(1) != 'z') return 24;
    if (strcmp(ranged.data(), "zzyy") != 0) return 25;
    return 0;
}

int vector_smoke() {
    {
        FreestandingVector<Tracked> items;
        for (int index = 0; index < 20; ++index) items.push_back(Tracked(index));
        if (items.size() != 20 || Tracked::Alive != 20) return 30;
        items.emplace_back(20);
        if (items.back().Id != 20 || items.front().Id != 0 || items[5].Id != 5) return 31;
        items.erase(items.begin() + 5);
        if (items.size() != 20 || items[5].Id != 6 || Tracked::Alive != 20) return 32;
        items.erase(items.begin(), items.begin() + 3);
        if (items.size() != 17 || items[0].Id != 3) return 33;
        items.insert(items.begin() + 1, Tracked(99));
        if (items[1].Id != 99 || items[2].Id != 4) return 34;
        items.pop_back();
        if (items.size() != 17) return 35;
        items.resize(5);
        if (items.size() != 5 || Tracked::Alive != 5) return 36;
        items.resize(8, Tracked(7));
        if (items.size() != 8 || items[7].Id != 7) return 37;
        int sum = 0;
        for (const Tracked& item : items) sum += item.Id;
        if (sum != 3 + 99 + 4 + 5 + 6 + 7 * 3) return 38;
        FreestandingVector<Tracked> copy = items;
        if (copy.size() != 8 || Tracked::Alive != 16) return 39;
        FreestandingVector<Tracked> moved(static_cast<FreestandingVector<Tracked>&&>(copy));
        if (moved.size() != 8 || copy.size() != 0 || Tracked::Alive != 16) return 40;
        moved.swap(copy);
        if (copy.size() != 8 || moved.size() != 0) return 41;
        copy.clear();
        if (!copy.empty() || Tracked::Alive != 8) return 42;
        items.reserve(1000);
        if (items.capacity() < 1000 || items.size() != 8) return 43;
        if (items.data() != &items[0]) return 44;
    }
    if (Tracked::Alive != 0) return 45;
    FreestandingVector<int> numbers{1, 2, 3};
    if (numbers.size() != 3 || numbers[2] != 3) return 46;
    return 0;
}

int hash_smoke() {
    FreestandingHash<int> hashInt;
    FreestandingHash<FreestandingString> hashString;
    FreestandingHash<const char*> hashPointer;
    if (hashInt(1) == hashInt(2)) return 50;
    if (hashString(FreestandingString("cube")) != hashString(FreestandingString("cube"))) return 51;
    if (hashString(FreestandingString("cube")) == hashString(FreestandingString("cubf"))) return 52;
    const char* p = "x";
    if (hashPointer(p) != hashPointer(p)) return 53;
    return 0;
}

int freestanding_provider_smoke() {
    int result = string_smoke();
    if (result != 0) return result;
    result = vector_smoke();
    if (result != 0) return result;
    return hash_smoke();
}

#if defined(HE_CPP_TEST_HOST)
#include <stdlib.h>
#include <stdio.h>
namespace he_cpp_custom {
[[noreturn]] void Fail(const char* message) { fputs(message, stderr); exit(73); }
uint64_t MonotonicMicroseconds() { return 0; }
}
int main() { return freestanding_provider_smoke(); }
#endif
```

Append to `run.sh` after the freestanding algorithm smoke:

```sh
"$cxx" $freestanding_flags $freestanding_includes \
    "$fixture/freestanding_provider_smoke.cpp" \
    "$runtime/runtime/freestanding/freestanding_hooks_default.cpp" \
    -o "$output/freestanding-provider-smoke"
"$output/freestanding-provider-smoke"
```

- [ ] **Step 2: Run the runner to verify it fails**

Run: `cd /c/dev/helworks/csharpcodegen && sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task3 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 600`

Expected: `freestanding_string.hpp: No such file or directory`.

- [ ] **Step 3: Write the hooks**

`runtime/freestanding/freestanding_hooks.hpp`:

```cpp
#pragma once

#include <stddef.h>
#include <stdint.h>

// Hooks the freestanding runtime needs from the platform. Allocate and Free
// have weak defaults over malloc/free in freestanding_hooks_default.cpp;
// Fail and MonotonicMicroseconds must be defined by the platform.
namespace he_cpp_custom {
void* Allocate(size_t size);
void Free(void* memory);
[[noreturn]] void Fail(const char* message);
uint64_t MonotonicMicroseconds();
}
```

`runtime/freestanding/freestanding_hooks_default.cpp`:

```cpp
#include "freestanding_hooks.hpp"

#include <stdlib.h>

#if defined(__GNUC__) || defined(__clang__)
#define HE_CPP_FREESTANDING_WEAK __attribute__((weak))
#else
#define HE_CPP_FREESTANDING_WEAK
#endif

namespace he_cpp_custom {

/// <summary>Default allocator: the C heap. A platform overrides by defining a strong symbol.</summary>
HE_CPP_FREESTANDING_WEAK void* Allocate(size_t size) {
    void* memory = malloc(size == 0 ? 1 : size);
    if (memory == nullptr) {
        Fail("Out of memory");
    }
    return memory;
}

/// <summary>Default release for memory obtained from Allocate.</summary>
HE_CPP_FREESTANDING_WEAK void Free(void* memory) {
    free(memory);
}

}
```

- [ ] **Step 4: Write the hash**

`runtime/freestanding/freestanding_hash.hpp`:

```cpp
#pragma once

#include <stddef.h>
#include <stdint.h>
#include <type_traits>

namespace he_cpp_freestanding {

/// <summary>FNV-1a over a byte range; 32-bit so it is cheap on 8/16-bit targets.</summary>
inline size_t HashBytes(const void* data, size_t length) {
    const unsigned char* bytes = static_cast<const unsigned char*>(data);
    uint32_t hash = 2166136261u;
    for (size_t index = 0; index < length; ++index) {
        hash ^= bytes[index];
        hash *= 16777619u;
    }
    return static_cast<size_t>(hash);
}

/// <summary>Hash for integral, enum and pointer keys: their object bytes. Strings specialise below.</summary>
template <typename T>
struct FreestandingHash {
    static_assert(std::is_integral_v<T> || std::is_enum_v<T> || std::is_pointer_v<T>,
                  "FreestandingHash supports integral, enum, pointer and FreestandingString keys.");
    size_t operator()(const T& value) const {
        return HashBytes(&value, sizeof(T));
    }
};

}
```

(The `FreestandingString` specialisation is added at the bottom of `freestanding_string.hpp` in Step 5, which includes this header.)

- [ ] **Step 5: Write the string**

`runtime/freestanding/freestanding_string.hpp`:

```cpp
#pragma once

#include "../native_algorithm.hpp"
#include "freestanding_hash.hpp"
#include "freestanding_hooks.hpp"

#include <stddef.h>
#include <stdint.h>
#include <string.h>

namespace he_cpp_freestanding {

/// <summary>
/// Heap-backed byte string with the member set the shared runtime templates use.
/// Storage is always null terminated. No small-string buffer.
/// </summary>
class FreestandingString {
public:
    using value_type = char;
    using size_type = size_t;
    using iterator = char*;
    using const_iterator = const char*;
    static constexpr size_t npos = static_cast<size_t>(-1);

    FreestandingString() : Data(EmptyBuffer()), Length(0), Capacity(0) {}
    FreestandingString(const char* text) : FreestandingString() { assign(text, text == nullptr ? 0 : strlen(text)); }
    FreestandingString(const char* text, size_t count) : FreestandingString() { assign(text, count); }
    FreestandingString(size_t count, char character) : FreestandingString() {
        reserve(count);
        memset(Data, static_cast<unsigned char>(character), count);
        Length = count;
        Data[Length] = '\0';
    }
    FreestandingString(const FreestandingString& other) : FreestandingString() { assign(other.Data, other.Length); }
    FreestandingString(FreestandingString&& other) noexcept : Data(other.Data), Length(other.Length), Capacity(other.Capacity) {
        other.Data = EmptyBuffer();
        other.Length = 0;
        other.Capacity = 0;
    }
    ~FreestandingString() { Release(); }

    FreestandingString& operator=(const FreestandingString& other) {
        if (this != &other) assign(other.Data, other.Length);
        return *this;
    }
    FreestandingString& operator=(FreestandingString&& other) noexcept {
        if (this != &other) {
            Release();
            Data = other.Data; Length = other.Length; Capacity = other.Capacity;
            other.Data = EmptyBuffer(); other.Length = 0; other.Capacity = 0;
        }
        return *this;
    }
    FreestandingString& operator=(const char* text) { return assign(text); }

    size_t size() const { return Length; }
    size_t length() const { return Length; }
    size_t capacity() const { return Capacity; }
    bool empty() const { return Length == 0; }
    const char* c_str() const { return Data; }
    const char* data() const { return Data; }
    char* data() { return Data; }
    char& operator[](size_t index) { return Data[index]; }
    const char& operator[](size_t index) const { return Data[index]; }
    char& at(size_t index) { if (index >= Length) he_cpp_custom::Fail("String index out of range"); return Data[index]; }
    const char& at(size_t index) const { if (index >= Length) he_cpp_custom::Fail("String index out of range"); return Data[index]; }
    char& front() { return Data[0]; }
    const char& front() const { return Data[0]; }
    char& back() { return Data[Length - 1]; }
    const char& back() const { return Data[Length - 1]; }
    iterator begin() { return Data; }
    iterator end() { return Data + Length; }
    const_iterator begin() const { return Data; }
    const_iterator end() const { return Data + Length; }

    void reserve(size_t newCapacity) {
        if (newCapacity <= Capacity) return;
        char* buffer = static_cast<char*>(he_cpp_custom::Allocate(newCapacity + 1));
        memcpy(buffer, Data, Length + 1);
        Release();
        Data = buffer;
        Capacity = newCapacity;
    }
    void resize(size_t newLength) { resize(newLength, '\0'); }
    void resize(size_t newLength, char fill) {
        if (newLength > Length) {
            Grow(newLength);
            memset(Data + Length, static_cast<unsigned char>(fill), newLength - Length);
        }
        Length = newLength;
        Data[Length] = '\0';
    }
    void clear() { Length = 0; Data[0] = '\0'; }

    FreestandingString& assign(const char* text) { return assign(text, text == nullptr ? 0 : strlen(text)); }
    FreestandingString& assign(const char* text, size_t count) {
        Grow(count);
        if (count != 0) memmove(Data, text, count);
        Length = count;
        Data[Length] = '\0';
        return *this;
    }
    FreestandingString& append(const char* text) { return append(text, text == nullptr ? 0 : strlen(text)); }
    FreestandingString& append(const char* text, size_t count) {
        Grow(Length + count);
        if (count != 0) memcpy(Data + Length, text, count);
        Length += count;
        Data[Length] = '\0';
        return *this;
    }
    FreestandingString& append(const FreestandingString& other) { return append(other.Data, other.Length); }
    FreestandingString& operator+=(const char* text) { return append(text); }
    FreestandingString& operator+=(const FreestandingString& other) { return append(other); }
    FreestandingString& operator+=(char character) { push_back(character); return *this; }
    void push_back(char character) { Grow(Length + 1); Data[Length++] = character; Data[Length] = '\0'; }
    void pop_back() { if (Length != 0) { --Length; Data[Length] = '\0'; } }

    FreestandingString substr(size_t position, size_t count = npos) const {
        if (position > Length) he_cpp_custom::Fail("String substr position out of range");
        size_t available = Length - position;
        return FreestandingString(Data + position, count < available ? count : available);
    }
    size_t find(char character, size_t position = 0) const {
        for (size_t index = position; index < Length; ++index) if (Data[index] == character) return index;
        return npos;
    }
    size_t find(const char* text, size_t position = 0) const {
        size_t needle = strlen(text);
        if (needle == 0) return position <= Length ? position : npos;
        if (needle > Length) return npos;
        for (size_t index = position; index + needle <= Length; ++index) {
            if (memcmp(Data + index, text, needle) == 0) return index;
        }
        return npos;
    }
    size_t find(const FreestandingString& text, size_t position = 0) const { return find(text.Data, position); }
    size_t find_first_of(const char* characters, size_t position = 0) const {
        for (size_t index = position; index < Length; ++index) if (strchr(characters, Data[index]) != nullptr) return index;
        return npos;
    }
    FreestandingString& erase(size_t position, size_t count = npos) {
        if (position > Length) he_cpp_custom::Fail("String erase position out of range");
        size_t available = Length - position;
        size_t removed = count < available ? count : available;
        memmove(Data + position, Data + position + removed, available - removed + 1);
        Length -= removed;
        return *this;
    }
    FreestandingString& insert(size_t position, const char* text) {
        if (position > Length) he_cpp_custom::Fail("String insert position out of range");
        size_t count = strlen(text);
        Grow(Length + count);
        memmove(Data + position + count, Data + position, Length - position + 1);
        memcpy(Data + position, text, count);
        Length += count;
        return *this;
    }
    FreestandingString& replace(size_t position, size_t count, const char* text) {
        erase(position, count);
        return insert(position, text);
    }
    void swap(FreestandingString& other) noexcept {
        he_cpp_alg::Swap(Data, other.Data);
        he_cpp_alg::Swap(Length, other.Length);
        he_cpp_alg::Swap(Capacity, other.Capacity);
    }
    int compare(const FreestandingString& other) const { return compare(other.Data, other.Length); }
    int compare(const char* text) const { return compare(text, strlen(text)); }

private:
    char* Data;
    size_t Length;
    size_t Capacity;

    static char* EmptyBuffer() {
        static char empty[1] = { '\0' };
        return empty;
    }
    void Release() {
        if (Capacity != 0) he_cpp_custom::Free(Data);
        Data = EmptyBuffer();
        Length = 0;
        Capacity = 0;
    }
    void Grow(size_t required) {
        if (required <= Capacity) return;
        size_t next = Capacity < 8 ? 8 : Capacity;
        while (next < required) next = next * 2;
        reserve(next);
    }
    int compare(const char* text, size_t count) const {
        size_t common = Length < count ? Length : count;
        int result = common == 0 ? 0 : memcmp(Data, text, common);
        if (result != 0) return result;
        if (Length == count) return 0;
        return Length < count ? -1 : 1;
    }
};

inline bool operator==(const FreestandingString& left, const FreestandingString& right) { return left.compare(right) == 0; }
inline bool operator!=(const FreestandingString& left, const FreestandingString& right) { return !(left == right); }
inline bool operator<(const FreestandingString& left, const FreestandingString& right) { return left.compare(right) < 0; }
inline bool operator==(const FreestandingString& left, const char* right) { return left.compare(right) == 0; }
inline bool operator!=(const FreestandingString& left, const char* right) { return !(left == right); }
inline bool operator==(const char* left, const FreestandingString& right) { return right.compare(left) == 0; }
inline bool operator!=(const char* left, const FreestandingString& right) { return !(right == left); }
inline FreestandingString operator+(const FreestandingString& left, const FreestandingString& right) { FreestandingString result(left); result.append(right); return result; }
inline FreestandingString operator+(const FreestandingString& left, const char* right) { FreestandingString result(left); result.append(right); return result; }
inline FreestandingString operator+(const char* left, const FreestandingString& right) { FreestandingString result(left); result.append(right); return result; }
inline FreestandingString operator+(const FreestandingString& left, char right) { FreestandingString result(left); result.push_back(right); return result; }

/// <summary>Hashes the string's characters so equal strings hash equally.</summary>
template <>
struct FreestandingHash<FreestandingString> {
    size_t operator()(const FreestandingString& value) const { return HashBytes(value.data(), value.size()); }
};

}
```

- [ ] **Step 6: Write the vector**

`runtime/freestanding/freestanding_vector.hpp`:

```cpp
#pragma once

#include "../native_algorithm.hpp"
#include "freestanding_hooks.hpp"

#include <initializer_list>
#include <new>
#include <stddef.h>
#include <string.h>
#include <type_traits>

namespace he_cpp_freestanding {

/// <summary>
/// Contiguous growable array. Elements are constructed in place with placement
/// new and moved on growth; capacity doubles from a minimum of four.
/// </summary>
template <typename T>
class FreestandingVector {
public:
    using value_type = T;
    using size_type = size_t;
    using iterator = T*;
    using const_iterator = const T*;

    FreestandingVector() : Data(nullptr), Length(0), Capacity(0) {}
    FreestandingVector(std::initializer_list<T> items) : FreestandingVector() {
        reserve(items.size());
        for (const T& item : items) push_back(item);
    }
    FreestandingVector(const FreestandingVector& other) : FreestandingVector() {
        reserve(other.Length);
        for (size_t index = 0; index < other.Length; ++index) new (Data + index) T(other.Data[index]);
        Length = other.Length;
    }
    FreestandingVector(FreestandingVector&& other) noexcept : Data(other.Data), Length(other.Length), Capacity(other.Capacity) {
        other.Data = nullptr; other.Length = 0; other.Capacity = 0;
    }
    ~FreestandingVector() { clear(); if (Data != nullptr) he_cpp_custom::Free(Data); }

    FreestandingVector& operator=(const FreestandingVector& other) {
        if (this != &other) { FreestandingVector copy(other); swap(copy); }
        return *this;
    }
    FreestandingVector& operator=(FreestandingVector&& other) noexcept {
        if (this != &other) { FreestandingVector moved(he_cpp_alg::Move(other)); swap(moved); }
        return *this;
    }

    size_t size() const { return Length; }
    size_t capacity() const { return Capacity; }
    bool empty() const { return Length == 0; }
    T* data() { return Data; }
    const T* data() const { return Data; }
    T& operator[](size_t index) { return Data[index]; }
    const T& operator[](size_t index) const { return Data[index]; }
    T& at(size_t index) { if (index >= Length) he_cpp_custom::Fail("Vector index out of range"); return Data[index]; }
    const T& at(size_t index) const { if (index >= Length) he_cpp_custom::Fail("Vector index out of range"); return Data[index]; }
    T& front() { return Data[0]; }
    const T& front() const { return Data[0]; }
    T& back() { return Data[Length - 1]; }
    const T& back() const { return Data[Length - 1]; }
    iterator begin() { return Data; }
    iterator end() { return Data + Length; }
    const_iterator begin() const { return Data; }
    const_iterator end() const { return Data + Length; }

    void reserve(size_t newCapacity) {
        if (newCapacity <= Capacity) return;
        T* buffer = static_cast<T*>(he_cpp_custom::Allocate(newCapacity * sizeof(T)));
        for (size_t index = 0; index < Length; ++index) {
            new (buffer + index) T(he_cpp_alg::Move(Data[index]));
            Data[index].~T();
        }
        if (Data != nullptr) he_cpp_custom::Free(Data);
        Data = buffer;
        Capacity = newCapacity;
    }
    void push_back(const T& value) { Grow(Length + 1); new (Data + Length) T(value); ++Length; }
    void push_back(T&& value) { Grow(Length + 1); new (Data + Length) T(he_cpp_alg::Move(value)); ++Length; }
    template <typename... TArgs>
    T& emplace_back(TArgs&&... args) {
        Grow(Length + 1);
        new (Data + Length) T(he_cpp_alg::Forward<TArgs>(args)...);
        return Data[Length++];
    }
    void pop_back() { if (Length != 0) { --Length; Data[Length].~T(); } }
    void clear() { for (size_t index = 0; index < Length; ++index) Data[index].~T(); Length = 0; }
    void resize(size_t newLength) {
        if (newLength < Length) { for (size_t index = newLength; index < Length; ++index) Data[index].~T(); }
        else { Grow(newLength); for (size_t index = Length; index < newLength; ++index) new (Data + index) T(); }
        Length = newLength;
    }
    void resize(size_t newLength, const T& fill) {
        if (newLength < Length) { for (size_t index = newLength; index < Length; ++index) Data[index].~T(); }
        else { Grow(newLength); for (size_t index = Length; index < newLength; ++index) new (Data + index) T(fill); }
        Length = newLength;
    }
    iterator erase(const_iterator position) { return erase(position, position + 1); }
    iterator erase(const_iterator first, const_iterator last) {
        size_t start = static_cast<size_t>(first - Data);
        size_t count = static_cast<size_t>(last - first);
        for (size_t index = start; index + count < Length; ++index) Data[index] = he_cpp_alg::Move(Data[index + count]);
        for (size_t index = Length - count; index < Length; ++index) Data[index].~T();
        Length -= count;
        return Data + start;
    }
    iterator insert(const_iterator position, const T& value) {
        size_t index = static_cast<size_t>(position - Data);
        T copy(value);
        Grow(Length + 1);
        new (Data + Length) T();
        for (size_t slot = Length; slot > index; --slot) Data[slot] = he_cpp_alg::Move(Data[slot - 1]);
        Data[index] = he_cpp_alg::Move(copy);
        ++Length;
        return Data + index;
    }
    void swap(FreestandingVector& other) noexcept {
        he_cpp_alg::Swap(Data, other.Data);
        he_cpp_alg::Swap(Length, other.Length);
        he_cpp_alg::Swap(Capacity, other.Capacity);
    }

private:
    T* Data;
    size_t Length;
    size_t Capacity;

    void Grow(size_t required) {
        if (required <= Capacity) return;
        size_t next = Capacity < 4 ? 4 : Capacity;
        while (next < required) next = next * 2;
        reserve(next);
    }
};

}
```

Note for `insert`: it requires `T` to be default-constructible, which every element type the runtime stores satisfies today (`HeCppOwnedPtr`, values, pointers). Document that in the class comment.

- [ ] **Step 7: Run the runner to verify the smoke passes**

Run: `cd /c/dev/helworks/csharpcodegen && sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task3 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 600`

Expected: `Runtime capability fixtures passed.` with `freestanding-provider-smoke` exiting 0.

- [ ] **Step 8: Cross-compile the smoke with the SNES toolchain**

Run: `cd /c/dev/helworks/csharpcodegen && MSYS_NO_PATHCONV=1 docker run --rm -v "/c/dev/helworks:/hw" -w /hw/csharpcodegen helengine-snes-toolchain bash -c '/usr/lib/llvm-mos-65816/bin/mos-snes-far-clang++ -std=c++20 -fno-exceptions -fno-rtti -Os -Wall -Wextra -Itests/runtime-capabilities-integration/poison -Itests/runtime-capabilities-integration/freestanding -Itests/runtime-capabilities-integration -Ics2.cpp/.net.cpp -c tests/runtime-capabilities-integration/freestanding_provider_smoke.cpp -o /hw/builds/csharpcodegen/rc-task3/provider-smoke-snes.o 2>&1 | head -40; ls -la /hw/builds/csharpcodegen/rc-task3/provider-smoke-snes.o'`

Expected: an object file and no errors. If the compiler rejects something the host accepted, fix the provider, not the test.

- [ ] **Step 9: Commit**

```bash
git add cs2.cpp/.net.cpp/runtime/freestanding tests/runtime-capabilities-integration/freestanding_provider_smoke.cpp tests/runtime-capabilities-integration/run.sh
git commit -F - <<'EOF'
feat(cpp-runtime): add freestanding hooks, hash, string and vector

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 4: Freestanding hash map and set

**Files:**
- Create: `runtime/freestanding/freestanding_hash_table.hpp`, `freestanding_hash_map.hpp`, `freestanding_hash_set.hpp`
- Modify: `tests/runtime-capabilities-integration/freestanding_provider_smoke.cpp`

**Interfaces:**
- Produces: `template<class K, class V, class H, class E> class FreestandingHashMap` with nested `struct KeyValue { K first; V second; }`, iterator over `KeyValue` with `->first`/`->second`, and members `find`, `emplace(key, value)`, `insert(KeyValue)`, `insert_or_assign`, `try_emplace`, `erase(key)`, `erase(iterator)`, `count`, `contains`, `operator[]`, `begin`, `end`, `size`, `empty`, `clear`, `reserve`; `template<class T, class H, class E> class FreestandingHashSet` with `find`, `insert(value)`, `emplace`, `erase(value)`, `erase(iterator)`, `count`, `contains`, `begin`, `end`, `size`, `empty`, `clear`, `reserve`. `emplace`/`insert` return a `struct { iterator first; bool second; }` named `InsertResult`.

- [ ] **Step 1: Add the failing map and set tests**

Add to `freestanding_provider_smoke.cpp` (new includes at top: `"runtime/freestanding/freestanding_hash_map.hpp"`, `"runtime/freestanding/freestanding_hash_set.hpp"`; a `struct IntEqual { bool operator()(int a, int b) const { return a == b; } };` and `struct StringEqual { bool operator()(const FreestandingString& a, const FreestandingString& b) const { return a == b; } };`):

```cpp
int map_smoke() {
    using Map = he_cpp_freestanding::FreestandingHashMap<int, Tracked, FreestandingHash<int>, IntEqual>;
    {
        Map map;
        for (int index = 0; index < 100; ++index) {
            auto result = map.emplace(index, Tracked(index * 10));
            if (!result.second || result.first->first != index) return 60;
        }
        if (map.size() != 100 || Tracked::Alive != 100) return 61;
        if (map.emplace(5, Tracked(0)).second) return 62;
        auto found = map.find(42);
        if (found == map.end() || found->second.Id != 420) return 63;
        if (map.find(1000) != map.end() || map.count(1000) != 0 || !map.contains(42)) return 64;
        if (map.erase(42) != 1 || map.erase(42) != 0 || map.size() != 99 || map.find(42) != map.end()) return 65;
        map.insert_or_assign(7, Tracked(777));
        if (map[7].Id != 777) return 66;
        map[500];
        if (map.size() != 100 || map.find(500) == map.end()) return 67;
        int visited = 0, sum = 0;
        for (const auto& entry : map) { ++visited; sum += entry.first; }
        if (visited != 100 || sum != (99 * 100 / 2) - 42 + 500) return 68;
        for (auto iterator = map.begin(); iterator != map.end();) {
            if (iterator->first % 2 == 0) iterator = map.erase(iterator); else ++iterator;
        }
        if (map.size() != 50) return 69;
        for (const auto& entry : map) if (entry.first % 2 == 0) return 70;
        map.reserve(4096);
        if (map.size() != 50 || map.find(1) == map.end()) return 71;
        Map copy = map;
        if (copy.size() != 50 || copy.find(3) == copy.end()) return 72;
        map.clear();
        if (!map.empty() || copy.size() != 50) return 73;
    }
    if (Tracked::Alive != 0) return 74;
    he_cpp_freestanding::FreestandingHashMap<FreestandingString, int, FreestandingHash<FreestandingString>, StringEqual> names;
    names.emplace(FreestandingString("alpha"), 1);
    names[FreestandingString("beta")] = 2;
    if (names.find(FreestandingString("alpha"))->second != 1 || names[FreestandingString("beta")] != 2) return 75;
    return 0;
}

int set_smoke() {
    he_cpp_freestanding::FreestandingHashSet<int, FreestandingHash<int>, IntEqual> set;
    for (int index = 0; index < 50; ++index) if (!set.insert(index).second) return 80;
    if (set.insert(10).second || set.size() != 50) return 81;
    if (!set.contains(10) || set.count(10) != 1 || set.find(99) != set.end()) return 82;
    if (set.erase(10) != 1 || set.contains(10)) return 83;
    int sum = 0;
    for (int value : set) sum += value;
    if (sum != (49 * 50 / 2) - 10) return 84;
    set.clear();
    if (!set.empty()) return 85;
    return 0;
}
```

and extend `freestanding_provider_smoke()` to call `map_smoke()` then `set_smoke()` after `hash_smoke()`.

- [ ] **Step 2: Run the runner to verify it fails**

Run: `cd /c/dev/helworks/csharpcodegen && sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task4 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 500`

Expected: `freestanding_hash_map.hpp: No such file or directory`.

- [ ] **Step 3: Write the table**

`runtime/freestanding/freestanding_hash_table.hpp`:

```cpp
#pragma once

#include "../native_algorithm.hpp"
#include "freestanding_hooks.hpp"

#include <new>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

namespace he_cpp_freestanding {

/// <summary>
/// Open-addressing hash table with linear probing and tombstones. TEntry
/// carries the stored object; TKeyOf extracts the key to hash and compare.
/// Rehashes at three quarters load; capacity is a power of two from 8.
/// </summary>
template <typename TEntry, typename TKey, typename TKeyOf, typename THash, typename TEqual>
class FreestandingHashTable {
    enum class SlotState : uint8_t { Empty, Occupied, Deleted };

public:
    /// <summary>Forward iterator over occupied slots.</summary>
    class Iterator {
    public:
        Iterator(FreestandingHashTable* table, size_t index) : Table(table), Index(index) { SkipUnoccupied(); }
        TEntry& operator*() const { return Table->Entries[Index]; }
        TEntry* operator->() const { return Table->Entries + Index; }
        Iterator& operator++() { ++Index; SkipUnoccupied(); return *this; }
        bool operator==(const Iterator& other) const { return Index == other.Index; }
        bool operator!=(const Iterator& other) const { return Index != other.Index; }
        size_t SlotIndex() const { return Index; }
    private:
        FreestandingHashTable* Table;
        size_t Index;
        void SkipUnoccupied() { while (Index < Table->Capacity && Table->States[Index] != SlotState::Occupied) ++Index; }
    };
    using ConstIterator = Iterator;

    /// <summary>Result of an insertion: the slot iterator and whether a new entry was created.</summary>
    struct InsertResult {
        Iterator first;
        bool second;
    };

    FreestandingHashTable() : Entries(nullptr), States(nullptr), Capacity(0), Count(0), Tombstones(0) {}
    FreestandingHashTable(const FreestandingHashTable& other) : FreestandingHashTable() {
        reserve(other.Count);
        for (size_t index = 0; index < other.Capacity; ++index) {
            if (other.States[index] == SlotState::Occupied) InsertUnique(other.Entries[index]);
        }
    }
    FreestandingHashTable(FreestandingHashTable&& other) noexcept
        : Entries(other.Entries), States(other.States), Capacity(other.Capacity), Count(other.Count), Tombstones(other.Tombstones) {
        other.Entries = nullptr; other.States = nullptr; other.Capacity = 0; other.Count = 0; other.Tombstones = 0;
    }
    ~FreestandingHashTable() { Destroy(); }
    FreestandingHashTable& operator=(const FreestandingHashTable& other) {
        if (this != &other) { FreestandingHashTable copy(other); Swap(copy); }
        return *this;
    }
    FreestandingHashTable& operator=(FreestandingHashTable&& other) noexcept {
        if (this != &other) { FreestandingHashTable moved(he_cpp_alg::Move(other)); Swap(moved); }
        return *this;
    }

    size_t size() const { return Count; }
    bool empty() const { return Count == 0; }
    Iterator begin() { return Iterator(this, 0); }
    Iterator end() { return Iterator(this, Capacity); }
    Iterator begin() const { return Iterator(const_cast<FreestandingHashTable*>(this), 0); }
    Iterator end() const { return Iterator(const_cast<FreestandingHashTable*>(this), Capacity); }

    Iterator Find(const TKey& key) const {
        size_t index;
        return FindSlot(key, index) ? Iterator(const_cast<FreestandingHashTable*>(this), index) : end();
    }
    template <typename TBuild>
    InsertResult Emplace(const TKey& key, TBuild build) {
        size_t index;
        if (FindSlot(key, index)) return InsertResult{ Iterator(this, index), false };
        EnsureRoom();
        index = ProbeForInsert(key);
        new (Entries + index) TEntry(build());
        States[index] = SlotState::Occupied;
        ++Count;
        return InsertResult{ Iterator(this, index), true };
    }
    size_t Erase(const TKey& key) {
        size_t index;
        if (!FindSlot(key, index)) return 0;
        EraseSlot(index);
        return 1;
    }
    Iterator Erase(Iterator position) {
        size_t index = position.SlotIndex();
        EraseSlot(index);
        return Iterator(this, index + 1);
    }
    void clear() {
        for (size_t index = 0; index < Capacity; ++index) {
            if (States[index] == SlotState::Occupied) Entries[index].~TEntry();
            States[index] = SlotState::Empty;
        }
        Count = 0;
        Tombstones = 0;
    }
    void reserve(size_t expected) {
        size_t needed = 8;
        while (needed * 3 / 4 < expected) needed *= 2;
        if (needed > Capacity) Rehash(needed);
    }
    void Swap(FreestandingHashTable& other) noexcept {
        he_cpp_alg::Swap(Entries, other.Entries);
        he_cpp_alg::Swap(States, other.States);
        he_cpp_alg::Swap(Capacity, other.Capacity);
        he_cpp_alg::Swap(Count, other.Count);
        he_cpp_alg::Swap(Tombstones, other.Tombstones);
    }

private:
    TEntry* Entries;
    SlotState* States;
    size_t Capacity;
    size_t Count;
    size_t Tombstones;

    static size_t HashKey(const TKey& key) { return THash{}(key); }
    static bool KeysEqual(const TKey& left, const TKey& right) { return TEqual{}(left, right); }

    bool FindSlot(const TKey& key, size_t& found) const {
        if (Capacity == 0) return false;
        size_t mask = Capacity - 1;
        size_t index = HashKey(key) & mask;
        for (size_t probe = 0; probe < Capacity; ++probe) {
            SlotState state = States[index];
            if (state == SlotState::Empty) return false;
            if (state == SlotState::Occupied && KeysEqual(TKeyOf{}(Entries[index]), key)) { found = index; return true; }
            index = (index + 1) & mask;
        }
        return false;
    }
    size_t ProbeForInsert(const TKey& key) const {
        size_t mask = Capacity - 1;
        size_t index = HashKey(key) & mask;
        while (States[index] == SlotState::Occupied) index = (index + 1) & mask;
        if (States[index] == SlotState::Deleted) const_cast<FreestandingHashTable*>(this)->Tombstones--;
        return index;
    }
    void EnsureRoom() {
        if (Capacity == 0) { Rehash(8); return; }
        if ((Count + Tombstones + 1) * 4 > Capacity * 3) Rehash(Count * 2 < 8 ? 8 : NextPowerOfTwo(Count * 2 + 1));
    }
    static size_t NextPowerOfTwo(size_t value) { size_t result = 8; while (result < value) result *= 2; return result; }
    void Rehash(size_t newCapacity) {
        TEntry* oldEntries = Entries;
        SlotState* oldStates = States;
        size_t oldCapacity = Capacity;
        Entries = static_cast<TEntry*>(he_cpp_custom::Allocate(newCapacity * sizeof(TEntry)));
        States = static_cast<SlotState*>(he_cpp_custom::Allocate(newCapacity * sizeof(SlotState)));
        memset(States, 0, newCapacity * sizeof(SlotState));
        Capacity = newCapacity;
        Count = 0;
        Tombstones = 0;
        for (size_t index = 0; index < oldCapacity; ++index) {
            if (oldStates[index] == SlotState::Occupied) {
                InsertUnique(he_cpp_alg::Move(oldEntries[index]));
                oldEntries[index].~TEntry();
            }
        }
        if (oldEntries != nullptr) he_cpp_custom::Free(oldEntries);
        if (oldStates != nullptr) he_cpp_custom::Free(oldStates);
    }
    template <typename TSource>
    void InsertUnique(TSource&& entry) {
        size_t index = ProbeForInsert(TKeyOf{}(entry));
        new (Entries + index) TEntry(he_cpp_alg::Forward<TSource>(entry));
        States[index] = SlotState::Occupied;
        ++Count;
    }
    void EraseSlot(size_t index) {
        Entries[index].~TEntry();
        States[index] = SlotState::Deleted;
        --Count;
        ++Tombstones;
    }
    void Destroy() {
        clear();
        if (Entries != nullptr) he_cpp_custom::Free(Entries);
        if (States != nullptr) he_cpp_custom::Free(States);
        Entries = nullptr; States = nullptr; Capacity = 0;
    }
};

}
```

- [ ] **Step 4: Write the map and set fronts**

`runtime/freestanding/freestanding_hash_map.hpp`:

```cpp
#pragma once

#include "freestanding_hash_table.hpp"

namespace he_cpp_freestanding {

/// <summary>Key/value pair stored by the map; named to match the pair members the runtime reads.</summary>
template <typename TKey, typename TValue>
struct FreestandingKeyValue {
    TKey first;
    TValue second;
};

/// <summary>Unordered map front over the open-addressing table.</summary>
template <typename TKey, typename TValue, typename THash, typename TEqual>
class FreestandingHashMap {
    using Entry = FreestandingKeyValue<TKey, TValue>;
    struct KeyOf { const TKey& operator()(const Entry& entry) const { return entry.first; } };
    using Table = FreestandingHashTable<Entry, TKey, KeyOf, THash, TEqual>;

public:
    using key_type = TKey;
    using mapped_type = TValue;
    using value_type = Entry;
    using iterator = typename Table::Iterator;
    using const_iterator = typename Table::ConstIterator;
    using InsertResult = typename Table::InsertResult;

    size_t size() const { return Storage.size(); }
    bool empty() const { return Storage.empty(); }
    iterator begin() { return Storage.begin(); }
    iterator end() { return Storage.end(); }
    const_iterator begin() const { return Storage.begin(); }
    const_iterator end() const { return Storage.end(); }
    iterator find(const TKey& key) { return Storage.Find(key); }
    const_iterator find(const TKey& key) const { return Storage.Find(key); }
    size_t count(const TKey& key) const { return Storage.Find(key) != Storage.end() ? 1 : 0; }
    bool contains(const TKey& key) const { return count(key) != 0; }
    template <typename TK, typename TV>
    InsertResult emplace(TK&& key, TV&& value) {
        return Storage.Emplace(key, [&]() { return Entry{ TKey(he_cpp_alg::Forward<TK>(key)), TValue(he_cpp_alg::Forward<TV>(value)) }; });
    }
    template <typename TK, typename... TArgs>
    InsertResult try_emplace(TK&& key, TArgs&&... args) {
        return Storage.Emplace(key, [&]() { return Entry{ TKey(he_cpp_alg::Forward<TK>(key)), TValue(he_cpp_alg::Forward<TArgs>(args)...) }; });
    }
    InsertResult insert(const Entry& entry) { return Storage.Emplace(entry.first, [&]() { return entry; }); }
    template <typename TV>
    InsertResult insert_or_assign(const TKey& key, TV&& value) {
        InsertResult result = Storage.Emplace(key, [&]() { return Entry{ key, TValue(he_cpp_alg::Forward<TV>(value)) }; });
        if (!result.second) result.first->second = TValue(he_cpp_alg::Forward<TV>(value));
        return result;
    }
    TValue& operator[](const TKey& key) { return Storage.Emplace(key, [&]() { return Entry{ key, TValue() }; }).first->second; }
    size_t erase(const TKey& key) { return Storage.Erase(key); }
    iterator erase(iterator position) { return Storage.Erase(position); }
    void clear() { Storage.clear(); }
    void reserve(size_t expected) { Storage.reserve(expected); }
    void swap(FreestandingHashMap& other) noexcept { Storage.Swap(other.Storage); }

private:
    Table Storage;
};

}
```

`runtime/freestanding/freestanding_hash_set.hpp`:

```cpp
#pragma once

#include "freestanding_hash_table.hpp"

namespace he_cpp_freestanding {

/// <summary>Unordered set front over the open-addressing table.</summary>
template <typename TValue, typename THash, typename TEqual>
class FreestandingHashSet {
    struct KeyOf { const TValue& operator()(const TValue& entry) const { return entry; } };
    using Table = FreestandingHashTable<TValue, TValue, KeyOf, THash, TEqual>;

public:
    using value_type = TValue;
    using iterator = typename Table::Iterator;
    using const_iterator = typename Table::ConstIterator;
    using InsertResult = typename Table::InsertResult;

    size_t size() const { return Storage.size(); }
    bool empty() const { return Storage.empty(); }
    iterator begin() { return Storage.begin(); }
    iterator end() { return Storage.end(); }
    const_iterator begin() const { return Storage.begin(); }
    const_iterator end() const { return Storage.end(); }
    iterator find(const TValue& value) { return Storage.Find(value); }
    const_iterator find(const TValue& value) const { return Storage.Find(value); }
    size_t count(const TValue& value) const { return Storage.Find(value) != Storage.end() ? 1 : 0; }
    bool contains(const TValue& value) const { return count(value) != 0; }
    InsertResult insert(const TValue& value) { return Storage.Emplace(value, [&]() { return value; }); }
    template <typename... TArgs>
    InsertResult emplace(TArgs&&... args) { TValue value(he_cpp_alg::Forward<TArgs>(args)...); return insert(value); }
    size_t erase(const TValue& value) { return Storage.Erase(value); }
    iterator erase(iterator position) { return Storage.Erase(position); }
    void clear() { Storage.clear(); }
    void reserve(size_t expected) { Storage.reserve(expected); }
    void swap(FreestandingHashSet& other) noexcept { Storage.Swap(other.Storage); }

private:
    Table Storage;
};

}
```

- [ ] **Step 5: Run the runner to verify the smoke passes**

Run: `cd /c/dev/helworks/csharpcodegen && sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task4 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 500`

Expected: `Runtime capability fixtures passed.`

- [ ] **Step 6: Cross-compile the smoke with the SNES toolchain**

Run the same Docker command as Task 3 Step 8 with output `provider-smoke-snes.o` under `rc-task4`. Expected: an object, no errors.

- [ ] **Step 7: Commit**

```bash
git add cs2.cpp/.net.cpp/runtime/freestanding tests/runtime-capabilities-integration/freestanding_provider_smoke.cpp
git commit -F - <<'EOF'
feat(cpp-runtime): add freestanding open-addressing hash map and set

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 5: Freestanding function, shared pointer and the provider header

**Files:**
- Create: `runtime/freestanding/freestanding_function.hpp`, `freestanding_shared_ptr.hpp`, `freestanding_provider.hpp`
- Modify: `tests/runtime-capabilities-integration/freestanding_provider_smoke.cpp`, `run.sh`, `README.md`

**Interfaces:**
- Produces: `he_cpp_freestanding::FreestandingFunction<R(Args...)>` (default constructible, constructible from any callable, copyable, movable, `explicit operator bool`, `operator()`, empty call fails through `Fail`); `he_cpp_freestanding::FreestandingSharedPtr<T>` with the same members as the fixture's `SingleThreadSharedPtr`; `runtime/freestanding/freestanding_provider.hpp` declaring in `he_cpp_custom`: `String`, `Vector<T>`, `UnorderedMap<K,V,H,E>`, `UnorderedSet<T,H,E>`, `Hash<T>`, `Function<Sig>`, `SharedPtr<T>` plus the four hooks. From this task on, the full existing smoke, services, streams and shared_ptr fixtures build under the freestanding config.

- [ ] **Step 1: Add the failing tests**

Add to `freestanding_provider_smoke.cpp` (include `"runtime/freestanding/freestanding_function.hpp"` and `"runtime/freestanding/freestanding_shared_ptr.hpp"`):

```cpp
int function_smoke() {
    using Fn = he_cpp_freestanding::FreestandingFunction<int(int)>;
    Fn empty;
    if (empty) return 90;
    int captured = 5;
    Fn add([captured](int value) { return value + captured; });
    if (!add || add(1) != 6) return 91;
    Fn copy = add;
    captured = 100;
    if (copy(1) != 6) return 92;
    Fn moved(static_cast<Fn&&>(copy));
    if (!moved || copy || moved(2) != 7) return 93;
    Tracked::Alive = 0;
    {
        Tracked big(1), bigger(2), biggest(3);
        Fn heavy([big, bigger, biggest](int value) { return value + big.Id + bigger.Id + biggest.Id; });
        if (heavy(0) != 6) return 94;
        Fn heavyCopy = heavy;
        if (heavyCopy(1) != 7) return 95;
        heavy = Fn();
        if (heavy) return 96;
    }
    if (Tracked::Alive != 0) return 97;
    return 0;
}

int shared_ptr_smoke() {
    Tracked::Alive = 0;
    {
        he_cpp_freestanding::FreestandingSharedPtr<Tracked> owner(new Tracked(42));
        if (owner.get() == nullptr || owner.use_count() != 1) return 100;
        he_cpp_freestanding::FreestandingSharedPtr<Tracked> copy(owner);
        if (owner.use_count() != 2 || copy.get() != owner.get()) return 101;
        he_cpp_freestanding::FreestandingSharedPtr<Tracked> moved(static_cast<he_cpp_freestanding::FreestandingSharedPtr<Tracked>&&>(copy));
        if (copy.get() != nullptr || moved.use_count() != 2) return 102;
        moved.reset();
        if (owner.use_count() != 1 || Tracked::Alive != 1) return 103;
    }
    if (Tracked::Alive != 0) return 104;
    return 0;
}
```

and call both from `freestanding_provider_smoke()` after `set_smoke()`.

Append to `run.sh` after the provider smoke: the existing fixtures under the freestanding config.

```sh
"$cxx" $freestanding_flags $freestanding_includes \
    "$fixture/smoke.cpp" "$runtime/runtime/freestanding/freestanding_hooks_default.cpp" \
    -o "$output/freestanding-smoke"
"$output/freestanding-smoke"
result=0
"$output/freestanding-smoke" fail >"$output/freestanding-fatal.stdout" 2>"$output/freestanding-fatal.stderr" || result=$?
test "$result" -eq 73
grep -q 'expected runtime failure' "$output/freestanding-fatal.stderr"

"$cxx" $freestanding_flags $freestanding_includes \
    "$fixture/services.cpp" "$runtime/runtime/freestanding/freestanding_hooks_default.cpp" \
    -o "$output/freestanding-services"
"$output/freestanding-services"

"$cxx" $freestanding_flags $freestanding_includes \
    "$fixture/streams.cpp" "$fixture/streams-main.cpp" \
    "$runtime/system/io/memory-stream.cpp" \
    "$runtime/system/io/file-stream.cpp" \
    "$runtime/system/io/binary-reader.cpp" \
    "$runtime/system/io/binary-writer.cpp" \
    "$runtime/runtime/freestanding/freestanding_hooks_default.cpp" \
    -o "$output/freestanding-streams"
"$output/freestanding-streams"

"$cxx" $freestanding_flags $freestanding_includes \
    "$fixture/number_parse_smoke.cpp" "$runtime/runtime/freestanding/freestanding_hooks_default.cpp" \
    -o "$output/freestanding-number-parse"
"$output/freestanding-number-parse"

"$cxx" $freestanding_flags $freestanding_includes \
    "$fixture/debug_fail.cpp" "$runtime/runtime/freestanding/freestanding_hooks_default.cpp" \
    -o "$output/freestanding-debug-fail"
result=0
"$output/freestanding-debug-fail" >"$output/freestanding-debug-fail.stdout" 2>"$output/freestanding-debug-fail.stderr" || result=$?
test "$result" -eq 73
grep -q 'debug failure' "$output/freestanding-debug-fail.stderr"
```

`smoke.cpp`, `services.cpp`, `streams.cpp`, `debug_fail.cpp` and `number_parse_smoke.cpp` define `he_cpp_custom::Fail` and `MonotonicMicroseconds` under `HE_CPP_TEST_HOST` already (check each; where one only defines `Fail`, add a `MonotonicMicroseconds` returning 0 inside its `HE_CPP_TEST_HOST` block). The `smoke.cpp` `fail` path must keep exit code 73 and print `expected runtime failure`.

- [ ] **Step 2: Run the runner to verify it fails**

Run: `cd /c/dev/helworks/csharpcodegen && sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task5 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 500`

Expected: `freestanding_function.hpp: No such file or directory`.

- [ ] **Step 3: Write the function**

`runtime/freestanding/freestanding_function.hpp`:

```cpp
#pragma once

#include "../native_algorithm.hpp"
#include "freestanding_hooks.hpp"

#include <new>
#include <stddef.h>
#include <type_traits>

namespace he_cpp_freestanding {

template <typename TSignature>
class FreestandingFunction;

/// <summary>
/// Type-erased callable. Callables up to InlineBytes with pointer alignment
/// live inline; larger ones live on the heap. Copy and move are supported;
/// invoking an empty function fails through he_cpp_custom::Fail.
/// </summary>
template <typename TResult, typename... TArgs>
class FreestandingFunction<TResult(TArgs...)> {
    static constexpr size_t InlineBytes = 2 * sizeof(void*) + 8;

    struct Operations {
        TResult (*Invoke)(void* storage, TArgs&&... args);
        void (*CopyTo)(const void* source, void* destination);
        void (*MoveTo)(void* source, void* destination);
        void (*Destroy)(void* storage);
        bool Heap;
    };

    template <typename TCallable, bool Inline>
    struct Model;

    template <typename TCallable>
    struct Model<TCallable, true> {
        static TCallable* Get(void* storage) { return static_cast<TCallable*>(storage); }
        static TResult Invoke(void* storage, TArgs&&... args) { return (*Get(storage))(he_cpp_alg::Forward<TArgs>(args)...); }
        static void CopyTo(const void* source, void* destination) { new (destination) TCallable(*static_cast<const TCallable*>(source)); }
        static void MoveTo(void* source, void* destination) { new (destination) TCallable(he_cpp_alg::Move(*static_cast<TCallable*>(source))); static_cast<TCallable*>(source)->~TCallable(); }
        static void Destroy(void* storage) { Get(storage)->~TCallable(); }
        static const Operations* Table() { static const Operations table{ &Invoke, &CopyTo, &MoveTo, &Destroy, false }; return &table; }
    };

    template <typename TCallable>
    struct Model<TCallable, false> {
        static TCallable* Get(void* storage) { return *static_cast<TCallable**>(storage); }
        static TResult Invoke(void* storage, TArgs&&... args) { return (*Get(storage))(he_cpp_alg::Forward<TArgs>(args)...); }
        static void CopyTo(const void* source, void* destination) {
            TCallable* copy = static_cast<TCallable*>(he_cpp_custom::Allocate(sizeof(TCallable)));
            new (copy) TCallable(**static_cast<TCallable* const*>(source));
            *static_cast<TCallable**>(destination) = copy;
        }
        static void MoveTo(void* source, void* destination) { *static_cast<TCallable**>(destination) = *static_cast<TCallable**>(source); *static_cast<TCallable**>(source) = nullptr; }
        static void Destroy(void* storage) { TCallable* callable = Get(storage); if (callable != nullptr) { callable->~TCallable(); he_cpp_custom::Free(callable); } }
        static const Operations* Table() { static const Operations table{ &Invoke, &CopyTo, &MoveTo, &Destroy, true }; return &table; }
    };

public:
    FreestandingFunction() : Table(nullptr) {}
    template <typename TCallable, std::enable_if_t<!std::is_same_v<std::remove_cv_t<std::remove_reference_t<TCallable>>, FreestandingFunction>, int> = 0>
    FreestandingFunction(TCallable callable) : Table(nullptr) {
        using Stored = std::remove_cv_t<std::remove_reference_t<TCallable>>;
        constexpr bool inline_ = sizeof(Stored) <= InlineBytes && alignof(Stored) <= alignof(void*);
        if constexpr (inline_) {
            new (Storage) Stored(he_cpp_alg::Move(callable));
        } else {
            Stored* heap = static_cast<Stored*>(he_cpp_custom::Allocate(sizeof(Stored)));
            new (heap) Stored(he_cpp_alg::Move(callable));
            *reinterpret_cast<Stored**>(Storage) = heap;
        }
        Table = Model<Stored, inline_>::Table();
    }
    FreestandingFunction(const FreestandingFunction& other) : Table(other.Table) { if (Table != nullptr) Table->CopyTo(other.Storage, Storage); }
    FreestandingFunction(FreestandingFunction&& other) noexcept : Table(other.Table) { if (Table != nullptr) { Table->MoveTo(other.Storage, Storage); other.Table = nullptr; } }
    ~FreestandingFunction() { Reset(); }
    FreestandingFunction& operator=(const FreestandingFunction& other) { if (this != &other) { FreestandingFunction copy(other); SwapWith(copy); } return *this; }
    FreestandingFunction& operator=(FreestandingFunction&& other) noexcept { if (this != &other) { Reset(); Table = other.Table; if (Table != nullptr) { Table->MoveTo(other.Storage, Storage); other.Table = nullptr; } } return *this; }

    explicit operator bool() const { return Table != nullptr; }
    TResult operator()(TArgs... args) const {
        if (Table == nullptr) he_cpp_custom::Fail("Invoked an empty function");
        return Table->Invoke(const_cast<unsigned char*>(Storage), he_cpp_alg::Forward<TArgs>(args)...);
    }

private:
    alignas(void*) unsigned char Storage[InlineBytes];
    const Operations* Table;

    void Reset() { if (Table != nullptr) { Table->Destroy(Storage); Table = nullptr; } }
    void SwapWith(FreestandingFunction& other) {
        FreestandingFunction temporary(he_cpp_alg::Move(other));
        other = he_cpp_alg::Move(*this);
        *this = he_cpp_alg::Move(temporary);
    }
};

}
```

- [ ] **Step 4: Write the shared pointer**

Copy `tests/runtime-capabilities-integration/single_thread_shared_ptr.hpp` to `runtime/freestanding/freestanding_shared_ptr.hpp`, rename the namespace `single_thread_fixture_detail` to `he_cpp_freestanding::detail`, the class `SingleThreadSharedPtr` to `FreestandingSharedPtr` inside `he_cpp_freestanding`, replace `#include <cstddef>` with `#include <stddef.h>`, replace `std::size_t` with `size_t`, and replace every `delete`/`new` of control blocks with `he_cpp_custom::Allocate`/`Free` plus placement new and explicit destructor calls (include `freestanding_hooks.hpp` and `<new>`). Keep the fixture file in place; `eastl_provider.hpp` keeps using it.

- [ ] **Step 5: Write the provider header**

`runtime/freestanding/freestanding_provider.hpp`:

```cpp
#pragma once

// Codegen-owned provider for targets without a hosted C++ library. Selected
// by the freestanding runtime profile through HE_CPP_RUNTIME_PROVIDER_HEADER;
// a platform may point that macro at its own header instead.

#include "freestanding_function.hpp"
#include "freestanding_hash.hpp"
#include "freestanding_hash_map.hpp"
#include "freestanding_hash_set.hpp"
#include "freestanding_hooks.hpp"
#include "freestanding_shared_ptr.hpp"
#include "freestanding_string.hpp"
#include "freestanding_vector.hpp"

namespace he_cpp_custom {
using String = he_cpp_freestanding::FreestandingString;
template <class T> using Vector = he_cpp_freestanding::FreestandingVector<T>;
template <class K, class V, class H, class E> using UnorderedMap = he_cpp_freestanding::FreestandingHashMap<K, V, H, E>;
template <class T, class H, class E> using UnorderedSet = he_cpp_freestanding::FreestandingHashSet<T, H, E>;
template <class T> using Hash = he_cpp_freestanding::FreestandingHash<T>;
template <class T> using Function = he_cpp_freestanding::FreestandingFunction<T>;
template <class T> using SharedPtr = he_cpp_freestanding::FreestandingSharedPtr<T>;
}
```

- [ ] **Step 6: Run the runner; fix whatever the shared templates need from the provider**

Run: `cd /c/dev/helworks/csharpcodegen && sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task5 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 3000`

Expected: `Runtime capability fixtures passed.` This step is where the shared templates (`native_string.hpp`, `native_list.hpp`, `native_dictionary.hpp`, `native_hash_set.hpp`, `native_event.hpp`, streams) reveal any member the provider still lacks or any hosted include the poison directory catches. Add the missing member to the freestanding type, or gate the include, and re-run. Do not weaken the poison directory and do not add hosted includes.

- [ ] **Step 7: Cross-compile the freestanding fixture objects with the SNES toolchain**

Add to `run.sh` inside the existing `if [ -n "${TARGET_CXX:-}" ]; then` block:

```sh
    target_freestanding_flags="-std=c++20 -fno-exceptions -fno-rtti -Os"
    for source in smoke services streams number_parse_smoke debug_fail freestanding_provider_smoke algorithm_smoke; do
        "$TARGET_CXX" $target_freestanding_flags $freestanding_includes \
            -c "$fixture/$source.cpp" -o "$output/$source-freestanding-target.o"
    done
    for source in memory-stream file-stream binary-reader binary-writer; do
        "$TARGET_CXX" $target_freestanding_flags $freestanding_includes \
            -c "$runtime/system/io/$source.cpp" -o "$output/$source-freestanding-target.o"
    done
    "$TARGET_CXX" $target_freestanding_flags $freestanding_includes \
        -c "$runtime/runtime/freestanding/freestanding_hooks_default.cpp" -o "$output/hooks-default-freestanding-target.o"
```

(`freestanding_flags` carries `-DHE_CPP_TEST_HOST`; the target loop deliberately does not, so no host `main` or `exit` is compiled.)

Run: `cd /c/dev/helworks/csharpcodegen && MSYS_NO_PATHCONV=1 docker run --rm -v "/c/dev/helworks:/hw" -w /hw/csharpcodegen helengine-snes-toolchain bash -c 'apt-get install -y -qq g++ >/dev/null 2>&1; CXX=g++ TARGET_CXX=/usr/lib/llvm-mos-65816/bin/mos-snes-far-clang++ sh tests/runtime-capabilities-integration/run.sh /hw/builds/csharpcodegen/rc-task5-target /hw/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 2500'`

Expected: `Runtime capability fixtures passed.` and `*-freestanding-target.o` files present. If `apt-get` cannot install `g++` in the container, run the host part on Windows as before and the `TARGET_CXX` loop manually in the container with the same flags; report which path was used.

- [ ] **Step 8: Update the fixture README**

In `tests/runtime-capabilities-integration/README.md` add a section:

```markdown
## Freestanding variant

The `freestanding/` config selects the codegen-owned provider under
`cs2.cpp/.net.cpp/runtime/freestanding/`. The `poison/` directory is placed
first on the include path so any hosted header include fails on the host.
With `TARGET_CXX` set to the SNES toolchain driver
(`/usr/lib/llvm-mos-65816/bin/mos-snes-far-clang++` in the
`helengine-snes-toolchain` Docker image) the same sources are cross-compiled
to objects as freestanding compile evidence.
```

- [ ] **Step 9: Commit**

```bash
git add cs2.cpp/.net.cpp/runtime/freestanding tests/runtime-capabilities-integration
git commit -F - <<'EOF'
feat(cpp-runtime): complete the freestanding provider with function and shared pointer

The existing smoke, services, streams and failure fixtures now build under
the freestanding configuration on the host behind poison includes and
cross-compile with the 65816 toolchain.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 6: Freestanding software math

**Files:**
- Create: `runtime/freestanding/freestanding_math.hpp`, `freestanding_math.cpp`, `tests/runtime-capabilities-integration/freestanding_math_smoke.cpp`
- Modify: `run.sh`

**Interfaces:**
- Produces, with C linkage: `ceil`, `floor`, `fabs`, `acos`, `asin`, `sin`, `cos`, `tan`, `sqrt`, `log`, `log2`, `fmod`, `atan2` (`double`), `int finite(double)`. When a hosted `<math.h>` exists (`__has_include(<math.h>)` and `HE_CPP_TEST_HOST`), the header forwards to it and the `.cpp` compiles to nothing, so host tests of everything else use the platform libm. Under `HE_CPP_FREESTANDING_MATH_SOFTWARE` (set by the smoke test and by the freestanding target build) the software implementations are used.

- [ ] **Step 1: Write the failing accuracy test**

`tests/runtime-capabilities-integration/freestanding_math_smoke.cpp`:

```cpp
#define HE_CPP_FREESTANDING_MATH_SOFTWARE 1
#include "runtime/freestanding/freestanding_math.hpp"

#include <math.h>   // host reference values only
#include <stdio.h>

namespace soft = he_cpp_freestanding_math;

static int Check(const char* name, double actual, double expected, double tolerance) {
    double error = actual - expected;
    if (error < 0) error = -error;
    double scale = expected < 0 ? -expected : expected;
    if (scale < 1.0) scale = 1.0;
    if (error / scale > tolerance) {
        printf("%s: got %.17g expected %.17g\n", name, actual, expected);
        return 1;
    }
    return 0;
}

int main() {
    int failures = 0;
    for (int index = -200; index <= 200; ++index) {
        double x = index * 0.05;
        failures += Check("sin", soft::Sin(x), ::sin(x), 1e-9);
        failures += Check("cos", soft::Cos(x), ::cos(x), 1e-9);
        failures += Check("atan2", soft::Atan2(x, 1.5), ::atan2(x, 1.5), 1e-9);
        failures += Check("atan2b", soft::Atan2(1.5, x), ::atan2(1.5, x), 1e-9);
        failures += Check("fmod", soft::Fmod(x, 0.7), ::fmod(x, 0.7), 1e-12);
        failures += Check("floor", soft::Floor(x), ::floor(x), 0.0);
        failures += Check("ceil", soft::Ceil(x), ::ceil(x), 0.0);
        failures += Check("fabs", soft::Fabs(x), ::fabs(x), 0.0);
        if (x > -1.0 && x < 1.0) {
            failures += Check("asin", soft::Asin(x), ::asin(x), 1e-9);
            failures += Check("acos", soft::Acos(x), ::acos(x), 1e-9);
        }
        if (x > 0.0) {
            failures += Check("sqrt", soft::Sqrt(x), ::sqrt(x), 1e-12);
            failures += Check("log", soft::Log(x), ::log(x), 1e-9);
            failures += Check("log2", soft::Log2(x), ::log2(x), 1e-9);
        }
        if (x > -1.5 && x < 1.5) failures += Check("tan", soft::Tan(x), ::tan(x), 1e-8);
    }
    failures += Check("sqrt-large", soft::Sqrt(1.0e12), 1.0e6, 1e-12);
    failures += Check("sin-large", soft::Sin(1000.0), ::sin(1000.0), 1e-7);
    if (soft::Sqrt(-1.0) == soft::Sqrt(-1.0)) { puts("sqrt(-1) must be NaN"); ++failures; }
    if (soft::Finite(soft::Sqrt(-1.0)) != 0 || soft::Finite(1.0) != 1) { puts("finite"); ++failures; }
    if (soft::Log(0.0) > -1.0e300) { puts("log(0) must be -inf"); ++failures; }
    if (::fmod(5.0, 0.0) == ::fmod(5.0, 0.0) || soft::Fmod(5.0, 0.0) == soft::Fmod(5.0, 0.0)) { puts("fmod by zero must be NaN"); ++failures; }
    return failures == 0 ? 0 : 1;
}
```

Append to `run.sh` before the final echo:

```sh
"$cxx" -std=c++20 -fno-exceptions -fno-rtti -Wall -Wextra -Werror -DHE_CPP_TEST_HOST \
    -I"$runtime" "$fixture/freestanding_math_smoke.cpp" "$runtime/runtime/freestanding/freestanding_math.cpp" \
    -o "$output/freestanding-math-smoke" -lm
"$output/freestanding-math-smoke"
```

- [ ] **Step 2: Run the runner to verify it fails**

Expected: `freestanding_math.hpp: No such file or directory`.

- [ ] **Step 3: Write the header**

`runtime/freestanding/freestanding_math.hpp`:

```cpp
#pragma once

// Math surface for HE_CPP_USE_STD_MATH=0. system/math.hpp calls the C names
// (::sin, ::sqrt, ...). On a host with <math.h> the platform libm serves them;
// on a freestanding target the software versions below are exported with C
// linkage. HE_CPP_FREESTANDING_MATH_SOFTWARE forces the software versions and
// exposes them in he_cpp_freestanding_math for accuracy tests.

#if !defined(HE_CPP_FREESTANDING_MATH_SOFTWARE)
#if defined(__has_include)
#if __has_include(<math.h>) && !defined(__mos__)
#define HE_CPP_FREESTANDING_MATH_HOSTED 1
#endif
#endif
#endif

#if defined(HE_CPP_FREESTANDING_MATH_HOSTED)
#include <math.h>
#else
namespace he_cpp_freestanding_math {
double Ceil(double value);
double Floor(double value);
double Fabs(double value);
double Sqrt(double value);
double Sin(double value);
double Cos(double value);
double Tan(double value);
double Asin(double value);
double Acos(double value);
double Atan2(double y, double x);
double Log(double value);
double Log2(double value);
double Fmod(double value, double divisor);
int Finite(double value);
}
#if !defined(HE_CPP_FREESTANDING_MATH_SOFTWARE) || defined(__mos__)
extern "C" {
double ceil(double value);
double floor(double value);
double fabs(double value);
double acos(double value);
double asin(double value);
double sin(double value);
double cos(double value);
double tan(double value);
double sqrt(double value);
double log(double value);
double log2(double value);
double fmod(double value, double divisor);
double atan2(double y, double x);
int finite(double value);
}
#endif
#endif
```

Note: `__mos__` is defined by the llvm-mos compiler; on that target the C names are exported even though `<math.h>` exists, because its libm defines none of them except `floorf`.

- [ ] **Step 4: Write the implementations**

`runtime/freestanding/freestanding_math.cpp` — portable double routines. Requirements the implementer must meet (the smoke test measures them): argument reduction for `Sin`/`Cos` by `2π` using a high-precision constant split (`6.283185307179586` and its correction `2.4492935982947064e-16`) then a minimax or Taylor series on `[-π/4, π/4]` with quadrant selection; `Tan = Sin/Cos`; `Sqrt` by exponent halving through the IEEE bit pattern (`memcpy` to `uint64_t`) followed by four Newton iterations, NaN for negatives, `0` for `0`, `inf` for `inf`; `Log` by decomposing into `m × 2^e` through the bit pattern, then `log(m)` from the series in `(m-1)/(m+1)` and adding `e × ln 2`; `Log2 = Log(x) / ln 2`; `-inf` for zero and NaN for negatives; `Asin` via `Atan2(x, Sqrt(1 - x²))`, `Acos = π/2 - Asin`; `Atan2` by octant reduction and a polynomial for `atan` on `[0, 1]` with the standard quadrant fix-ups and correct results for zero and infinite arguments; `Fmod` by repeated scaled subtraction using the bit pattern exponent (exact, like fdlibm), NaN when the divisor is zero or an operand is not finite; `Floor`/`Ceil`/`Fabs` through the bit pattern with exact results for magnitudes above `2^52`; `Finite` returns 1 unless the exponent field is all ones. The file starts with `#include "freestanding_math.hpp"` and `#if !defined(HE_CPP_FREESTANDING_MATH_HOSTED)` so it compiles to nothing on a host build that uses libm, and ends with the `extern "C"` definitions forwarding to the `he_cpp_freestanding_math` functions when those C names are exported. Only `<stdint.h>` and `<string.h>` may be included.

- [ ] **Step 5: Run the runner to verify accuracy**

Run the runner. Expected: `Runtime capability fixtures passed.` with `freestanding-math-smoke` printing nothing and exiting 0. Tighten the implementation, not the tolerances, if a check prints.

- [ ] **Step 6: Cross-compile the math and the math fixture with the SNES toolchain**

Add to the `TARGET_CXX` block in `run.sh`:

```sh
    "$TARGET_CXX" $target_freestanding_flags -I"$runtime" \
        -c "$runtime/runtime/freestanding/freestanding_math.cpp" -o "$output/freestanding-math-target.o"
    "$TARGET_CXX" $target_freestanding_flags $freestanding_includes \
        -c "$fixture/math_extensions.cpp" -o "$output/math-extensions-freestanding-target.o"
```

`math_extensions.cpp` includes `system/math.hpp`, which under the freestanding config includes `freestanding_math.hpp` through `HE_CPP_RUNTIME_MATH_HEADER`; that is the real consumer check. Run the Docker command from Task 5 Step 7 again. Expected: objects present, no errors.

- [ ] **Step 7: Commit**

```bash
git add cs2.cpp/.net.cpp/runtime/freestanding/freestanding_math.hpp cs2.cpp/.net.cpp/runtime/freestanding/freestanding_math.cpp tests/runtime-capabilities-integration/freestanding_math_smoke.cpp tests/runtime-capabilities-integration/run.sh
git commit -F - <<'EOF'
feat(cpp-runtime): add portable software math for freestanding targets

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 7: Freestanding runtime kind, preset, restriction and CLI

**Files:**
- Create: `cs2.cpp.tests/CPPFreestandingRuntimeProfileTests.cs`
- Modify: `cs2.cpp/model/CPPRuntimeKind.cs`, `cs2.cpp/model/CPPRuntimeProfile.cs`, `cs2.cpp/model/CPPRestrictionProfile.cs`, `cs2.cpp/CPPRuntimeOptionResolver.cs`, `cs2.cpp/CPPGeneratedConfigWriter.cs`, `cs2.cpp/CPPRestrictionValidator.cs`, `cs2.cpp/CPPConversionPresetCatalog.cs`, `codegen/CodegenCliOptionsBuilder.cs:183-191`, `codegen/Program.cs:32`, `cs2.cpp.tests/CPPRestrictionValidatorTests.cs`, `cs2.cpp.tests/CPPConversionPresetCatalogTests.cs`, `docs/generic-runtime-capabilities.md`, `docs/superpowers/specs/2026-09-18-freestanding-runtime-design.md`

**Interfaces:**
- Produces: `CPPRuntimeKind.Freestanding`; `CPPRuntimeProfile.CreateFreestanding()` (Name `freestanding`, DefineName `HE_CPP_RUNTIME_FREESTANDING`, every `UseStd*` false, `UseStdMath` false, `UseHostedFileSystem` false, `UseExceptions` false, `UseRtti` false); `CPPRestrictionProfile.ForbidHostedServices`; `CPPRestrictionValidator.HostedServiceRequirementNames` (static readonly `string[]`: `Interlocked`, `Volatile`, `SpinLock`, `SpinWait`, `AutoResetEvent`, `Thread`, `Random`, `Guid`, `NativeVector128`, `NativeVector256`, `NativeVector512`, `Sse`, `Sse41`, `Avx`, `Avx2`); preset id `native-core-boot-freestanding`; CLI `--runtime freestanding`; config defines `HE_CPP_RUNTIME_FREESTANDING 1` and `HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 0|1`; resolver defaults `codegen-runtime-provider-header` and `codegen-runtime-math-header` for the freestanding kind when the caller gave none.

- [ ] **Step 1: Write the failing unit tests**

`cs2.cpp.tests/CPPFreestandingRuntimeProfileTests.cs`:

```csharp
using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the freestanding runtime profile, its resolver defaults, its generated config defines and its preset.
/// </summary>
public sealed class CPPFreestandingRuntimeProfileTests {
    /// <summary>The freestanding profile disables every hosted facility.</summary>
    [Fact]
    public void CreateFreestanding_DisablesEveryHostedFacility() {
        CPPRuntimeProfile profile = CPPRuntimeProfile.CreateFreestanding();

        Assert.Equal(CPPRuntimeKind.Freestanding, profile.Kind);
        Assert.Equal("freestanding", profile.Name);
        Assert.Equal("HE_CPP_RUNTIME_FREESTANDING", profile.DefineName);
        Assert.False(profile.UseStdString);
        Assert.False(profile.UseStdVector);
        Assert.False(profile.UseStdUnorderedMap);
        Assert.False(profile.UseStdUnorderedSet);
        Assert.False(profile.UseStdFunction);
        Assert.False(profile.UseStdChrono);
        Assert.False(profile.UseStdSharedPtr);
        Assert.False(profile.UseStdMath);
        Assert.False(profile.UseHostedFileSystem);
        Assert.False(profile.UseExceptions);
        Assert.False(profile.UseRtti);
    }

    /// <summary>The resolver supplies the codegen-owned provider and math headers when the caller gives none.</summary>
    [Fact]
    public void Resolve_FreestandingDefaultsProviderAndMathHeaders() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.RuntimeProfile = CPPRuntimeProfile.CreateFreestanding();
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        CPPRuntimeOptionResolver.Resolve(options);

        Assert.Equal("runtime/freestanding/freestanding_provider.hpp", CPPRuntimeOptionResolver.GetProviderHeader(options));
        Assert.Equal("runtime/freestanding/freestanding_math.hpp", options.PlatformOptionValues[CPPCodegenOptionNames.RuntimeMathHeader]);
    }

    /// <summary>A caller-supplied provider header wins over the freestanding default.</summary>
    [Fact]
    public void Resolve_FreestandingKeepsCallerProviderHeader() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.RuntimeProfile = CPPRuntimeProfile.CreateFreestanding();
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.RuntimeProviderHeader] = "platform/snes/SnesRuntimeProvider.hpp"
        };

        CPPRuntimeOptionResolver.Resolve(options);

        Assert.Equal("platform/snes/SnesRuntimeProvider.hpp", CPPRuntimeOptionResolver.GetProviderHeader(options));
    }

    /// <summary>The generated config announces the freestanding runtime and the absence of hosted services.</summary>
    [Fact]
    public void Write_FreestandingEmitsRuntimeAndHostedServiceDefines() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.RuntimeProfile = CPPRuntimeProfile.CreateFreestanding();
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(new CPPRuntimeRequirementCatalog(), report);
        registrar.RegisterDefaults(options);
        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));

        string filePath = CPPGeneratedConfigWriter.Write(outputFolder, options, registrar);
        string output = File.ReadAllText(filePath);

        Assert.Contains("#define HE_CPP_RUNTIME_FREESTANDING 1", output);
        Assert.Contains("#define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 0", output);
        Assert.Contains("#define HE_CPP_USE_STD_MATH 0", output);
        Assert.Contains("#define HE_CPP_RUNTIME_PROVIDER_HEADER \"runtime/freestanding/freestanding_provider.hpp\"", output);
        Assert.Contains("#define HE_CPP_RUNTIME_MATH_HEADER \"runtime/freestanding/freestanding_math.hpp\"", output);
    }

    /// <summary>Hosted profiles keep announcing hosted services.</summary>
    [Fact]
    public void Write_StlLiteEmitsHostedServicesEnabled() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(new CPPRuntimeRequirementCatalog(), report);
        registrar.RegisterDefaults(options);
        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));

        string output = File.ReadAllText(CPPGeneratedConfigWriter.Write(outputFolder, options, registrar));

        Assert.Contains("#define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 1", output);
    }

    /// <summary>The freestanding core-boot preset combines the stripped core with the freestanding runtime and forbids hosted services.</summary>
    [Fact]
    public void Resolve_NativeCoreBootFreestandingPreset() {
        CPPConversionPreset preset = CPPConversionPresetCatalog.Resolve("native-core-boot-freestanding");

        Assert.Equal(CPPRuntimeKind.Freestanding, preset.RuntimeProfile.Kind);
        Assert.True(preset.RestrictionProfile.ForbidHostedServices);
        Assert.True(preset.RestrictionProfile.ForbidShaders);
        Assert.True(preset.RestrictionProfile.ForbidRegex);
        Assert.Equal("retroppc", preset.PlatformProfile.Name.Replace("-headless", string.Empty));
    }
}
```

Add to `cs2.cpp.tests/CPPRestrictionValidatorTests.cs`:

```csharp
    /// <summary>
    /// Ensures hosted-service runtime helpers are rejected when the restriction profile forbids them.
    /// </summary>
    [Theory]
    [InlineData("Interlocked")]
    [InlineData("Thread")]
    [InlineData("Random")]
    [InlineData("NativeVector128")]
    public void Validate_WhenHostedServicesAreForbiddenAndRegistered_ReturnsDiagnostic(string requirementName) {
        CPPRuntimeRequirementCatalog catalog = new CPPRuntimeRequirementCatalog();
        Assert.True(catalog.TryGet(requirementName, out CPPRuntimeRequirementDefinition definition));
        CPPRestrictionProfile profile = new CPPRestrictionProfile {
            Name = "freestanding-boot",
            ForbidHostedServices = true
        };

        CPPRestrictionValidationResult result = CPPRestrictionValidator.Validate(new CPPBuildUsageReport(), [definition], profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("freestanding-boot", StringComparison.Ordinal) && diagnostic.Contains(requirementName, StringComparison.Ordinal) && diagnostic.Contains("hosted services", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures ordinary runtime helpers pass when only hosted services are forbidden.
    /// </summary>
    [Fact]
    public void Validate_WhenHostedServicesAreForbiddenAndOnlyStringIsRegistered_IsValid() {
        CPPRuntimeRequirementCatalog catalog = new CPPRuntimeRequirementCatalog();
        Assert.True(catalog.TryGet("NativeString", out CPPRuntimeRequirementDefinition definition));
        CPPRestrictionProfile profile = new CPPRestrictionProfile { Name = "freestanding-boot", ForbidHostedServices = true };

        CPPRestrictionValidationResult result = CPPRestrictionValidator.Validate(new CPPBuildUsageReport(), [definition], profile);

        Assert.True(result.IsValid);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /c/dev/helworks/csharpcodegen && dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPFreestandingRuntimeProfileTests|FullyQualifiedName~CPPRestrictionValidatorTests" 2>&1 | tail -c 1500`

Expected: build errors naming `CreateFreestanding`, `Freestanding` and `ForbidHostedServices`.

- [ ] **Step 3: Add the kind, the profile factory and the restriction switch**

`cs2.cpp/model/CPPRuntimeKind.cs`: add `Freestanding` after `CustomRetro` with an XML comment "Codegen-owned provider and math for targets without a hosted C++ library."

`cs2.cpp/model/CPPRuntimeProfile.cs`: add

```csharp
        /// <summary>
        /// Creates the freestanding runtime profile: no hosted standard library facilities at all, served by the codegen-owned provider.
        /// </summary>
        /// <returns>The freestanding runtime profile.</returns>
        public static CPPRuntimeProfile CreateFreestanding() {
            return new CPPRuntimeProfile {
                Kind = CPPRuntimeKind.Freestanding,
                Name = "freestanding",
                DefineName = "HE_CPP_RUNTIME_FREESTANDING",
                UseStdString = false,
                UseStdVector = false,
                UseStdUnorderedMap = false,
                UseStdUnorderedSet = false,
                UseStdFunction = false,
                UseStdChrono = false,
                UseStdSharedPtr = false,
                UseStdMath = false,
                UseHostedFileSystem = false,
                UseExceptions = false,
                UseRtti = false
            };
        }
```

`cs2.cpp/model/CPPRestrictionProfile.cs`: add `public bool ForbidHostedServices { get; set; }` with the comment "Rejects runtime helpers that need threads, atomics, an OS random device or x86 intrinsics."

- [ ] **Step 4: Resolver defaults, config defines, validator, preset, CLI**

`CPPRuntimeOptionResolver.Resolve`: before the math-header check, add

```csharp
            if (options.RuntimeProfile.Kind == CPPRuntimeKind.Freestanding) {
                Dictionary<string, string> defaulted = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
                if (!defaulted.TryGetValue(CPPCodegenOptionNames.RuntimeProviderHeader, out string providerHeader) || string.IsNullOrWhiteSpace(providerHeader)) {
                    defaulted[CPPCodegenOptionNames.RuntimeProviderHeader] = FreestandingProviderHeader;
                }
                if (!defaulted.TryGetValue(CPPCodegenOptionNames.RuntimeMathHeader, out string mathHeader) || string.IsNullOrWhiteSpace(mathHeader)) {
                    defaulted[CPPCodegenOptionNames.RuntimeMathHeader] = FreestandingMathHeader;
                }
                options.PlatformOptionValues = defaulted;
                values = defaulted;
            }
```

with two constants on the class: `public const string FreestandingProviderHeader = "runtime/freestanding/freestanding_provider.hpp";` and `public const string FreestandingMathHeader = "runtime/freestanding/freestanding_math.hpp";`. Place this block before the `UseStd*` overrides are read so the later checks see the defaults.

`CPPGeneratedConfigWriter.Write`: after the `HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM` line add

```csharp
                $"#define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES {ToDefineValue(options.RuntimeProfile.Kind != CPPRuntimeKind.Freestanding)}"
```

`CPPRestrictionValidator`: add

```csharp
        /// <summary>
        /// Runtime requirement names that need hosted threading, OS or x86 facilities.
        /// </summary>
        public static readonly string[] HostedServiceRequirementNames = [
            "Interlocked", "Volatile", "SpinLock", "SpinWait", "AutoResetEvent", "Thread",
            "Random", "Guid", "NativeVector128", "NativeVector256", "NativeVector512", "Sse", "Sse41", "Avx", "Avx2"
        ];
```

and, after the regex block:

```csharp
            if (profile.ForbidHostedServices) {
                foreach (CPPRuntimeRequirementDefinition definition in registeredRequirements ?? Array.Empty<CPPRuntimeRequirementDefinition>()) {
                    if (HostedServiceRequirementNames.Contains(definition.Name, StringComparer.Ordinal)) {
                        result.Diagnostics.Add($"Restriction profile '{profile.Name}' forbids hosted services, but runtime requirement '{definition.Name}' was registered.");
                    }
                }
            }
```

`CPPConversionPresetCatalog`: add `CreateNativeCoreBootFreestandingPreset()` identical to `CreateNativeCoreBootPreset()` except `Id = "native-core-boot-freestanding"`, `RuntimeProfile = CPPRuntimeProfile.CreateFreestanding()`, `RestrictionProfile.Name = "native-core-boot-freestanding"`, `ForbidRegex = true`, `ForbidHostedServices = true`; register it wherever `native-core-boot` is registered (the id list and the resolve switch).

`codegen/CodegenCliOptionsBuilder.CreateRuntimeProfile`: add `else if (string.Equals(runtimeProfileName, "freestanding", StringComparison.OrdinalIgnoreCase)) { return CPPRuntimeProfile.CreateFreestanding(); }`.

`codegen/Program.cs:32`: add `[--runtime stl-lite|custom-retro|freestanding]` to the usage line.

- [ ] **Step 5: Run the new tests and the related suites**

Run: `cd /c/dev/helworks/csharpcodegen && dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPFreestandingRuntimeProfileTests|FullyQualifiedName~CPPRestrictionValidatorTests|FullyQualifiedName~CPPConversionPresetCatalogTests|FullyQualifiedName~CPPRuntimeOptionResolverTests|FullyQualifiedName~CPPGeneratedConfigWriterTests" 2>&1 | tail -c 1500`

Expected: `Passed!`. If `CPPConversionPresetCatalogTests` asserts an exact list of preset ids, add the new id to that expectation.

- [ ] **Step 6: Run the whole cs2.cpp.tests suite once**

Run: `cd /c/dev/helworks/csharpcodegen && dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj 2>&1 | tail -c 1200`

Expected: `Passed!` with zero failures.

- [ ] **Step 7: Update the docs and the spec**

`docs/generic-runtime-capabilities.md`: add a row-less paragraph under the option table:

```markdown
`--runtime freestanding` selects `CPPRuntimeProfile.CreateFreestanding()`: every
hosted facility off, the codegen-owned provider `runtime/freestanding/freestanding_provider.hpp`
and math `runtime/freestanding/freestanding_math.hpp` as defaults, both overridable
with the usual options. The `native-core-boot-freestanding` preset pairs it with
the stripped core restrictions plus `ForbidHostedServices`.
```

`docs/superpowers/specs/2026-09-18-freestanding-runtime-design.md`: in "Provider library" remove the `OwnedPtr<T>` row and add the sentence "The runtime owns `HeCppOwnedPtr` itself; providers do not supply an owned pointer."; in "Hosted-header hygiene" delete the sentence about the PS1 and EASTL fixtures gaining an alias; in "Runtime profile" replace "No new preset is added in this sub-project." with "A `native-core-boot-freestanding` preset is added because a named preset overwrites the CLI runtime profile and the measurement task needs both."

- [ ] **Step 8: Commit**

```bash
git add cs2.cpp codegen cs2.cpp.tests docs
git commit -F - <<'EOF'
feat(cs2.cpp): add the freestanding runtime kind, preset and hosted-services restriction

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 8: Generated-code check under the freestanding runtime

**Files:**
- Modify: `tests/runtime-capabilities-integration/run.sh`, `README.md`

**Interfaces:**
- Consumes: the CLI `--runtime freestanding`; `GENERATED_OUTPUT` and `TARGET_CXX` runner variables.

- [ ] **Step 1: Generate the fixture with the freestanding runtime**

Run:

```bash
cd /c/dev/helworks/csharpcodegen && dotnet build codegen/codegen.csproj -c Release 2>&1 | tail -3
mkdir -p /c/dev/helworks/builds/csharpcodegen/rc-task8-generated
./codegen/bin/Release/net9.0/codegen.exe --cpp --project tests/runtime-capabilities-integration/fixture/Fixture.csproj --output /c/dev/helworks/builds/csharpcodegen/rc-task8-generated --compiler gcc --platform generic --runtime freestanding --set generated-math-convention=engine-row-vector --set pointer-size-bytes=4 --set load-native-runtime-metadata=false 2>&1 | tail -c 1500
ls /c/dev/helworks/builds/csharpcodegen/rc-task8-generated | head; grep -E "RUNTIME_FREESTANDING|PROVIDER_HEADER|MATH_HEADER|HOSTED_SERVICES" /c/dev/helworks/builds/csharpcodegen/rc-task8-generated/helcpp_config.hpp
```

Expected: `StringGate.cpp`, `StringGate.hpp`, `helcpp_config.hpp` present with `HE_CPP_RUNTIME_FREESTANDING 1`, the two default headers and `HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 0`.

- [ ] **Step 2: Add the freestanding generated check to the runner**

Inside the existing `if [ -n "${GENERATED_OUTPUT:-}" ]; then` block, add a second variable `GENERATED_FREESTANDING_OUTPUT` handled the same way:

```sh
if [ -n "${GENERATED_FREESTANDING_OUTPUT:-}" ]; then
    "$cxx" $freestanding_flags -I"$fixture/poison" -I"$fixture" -I"$GENERATED_FREESTANDING_OUTPUT" -I"$runtime" \
        "$fixture/generated-smoke.cpp" "$GENERATED_FREESTANDING_OUTPUT/StringGate.cpp" \
        "$runtime/runtime/freestanding/freestanding_hooks_default.cpp" \
        -o "$output/generated-freestanding"
    "$output/generated-freestanding"
    for failure in fail null; do
        result=0
        "$output/generated-freestanding" "$failure" || result=$?
        test "$result" -eq 73
    done
    if [ -n "${TARGET_CXX:-}" ]; then
        "$TARGET_CXX" $target_freestanding_flags -I"$fixture/poison" -I"$fixture" -I"$GENERATED_FREESTANDING_OUTPUT" -I"$runtime" \
            -c "$GENERATED_FREESTANDING_OUTPUT/StringGate.cpp" -o "$output/generated-freestanding-target.o"
    fi
fi
```

The generated output ships its own `helcpp_config.hpp`, so this block does not add `-I"$fixture/freestanding"`. `generated-smoke.cpp` defines `Fail` under `HE_CPP_TEST_HOST`; add a `MonotonicMicroseconds` returning 0 to that block.

- [ ] **Step 3: Run the runner with the generated output on host and target**

Run: `cd /c/dev/helworks/csharpcodegen && GENERATED_FREESTANDING_OUTPUT=/c/dev/helworks/builds/csharpcodegen/rc-task8-generated sh tests/runtime-capabilities-integration/run.sh /c/dev/helworks/builds/csharpcodegen/rc-task8 /c/dev/helworks/helengine-ps1/third_party/nugget/third_party 2>&1 | tail -c 800`

Expected: `Runtime capability fixtures passed.` Then the Docker variant with `TARGET_CXX` (as in Task 5 Step 7, adding `GENERATED_FREESTANDING_OUTPUT=/hw/builds/csharpcodegen/rc-task8-generated`). Expected: `generated-freestanding-target.o` present.

- [ ] **Step 4: Document and commit**

Add the generation command from Step 1 and the `GENERATED_FREESTANDING_OUTPUT` variable to `README.md` under the freestanding section.

```bash
git add tests/runtime-capabilities-integration
git commit -F - <<'EOF'
test(runtime): check generated code under the freestanding runtime on host and target

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 9: Measure the generated engine core on the SNES compiler

**Files:**
- Create: `docs/superpowers/reports/<today>-freestanding-core-compile.md`

**Interfaces:**
- Consumes: preset `native-core-boot-freestanding`, the helengine feature catalog at `C:\dev\helworks\helengine\engine\helengine.editor\codegen\features\helengine-feature-catalog.json`, the engine core project `C:\dev\helworks\helengine\engine\helengine.core\helengine.core.csproj`.

- [ ] **Step 1: Generate the engine core**

The editor's own invocation shape lives in `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorGeneratedCoreRegenerationService.cs` (method building the argument list around line 388). Reproduce it with the CLI:

```bash
cd /c/dev/helworks/csharpcodegen && mkdir -p /c/dev/helworks/builds/csharpcodegen/core-freestanding
./codegen/bin/Release/net9.0/codegen.exe --cpp \
  --project /c/dev/helworks/helengine/engine/helengine.core/helengine.core.csproj \
  --output /c/dev/helworks/builds/csharpcodegen/core-freestanding \
  --feature-catalog /c/dev/helworks/helengine/engine/helengine.editor/codegen/features/helengine-feature-catalog.json \
  --platform retroppc --language cpp --endianness big \
  --preset native-core-boot-freestanding \
  --set include-project-defined-preprocessor-symbols=false \
  --set write-conversion-report=true 2>&1 | tail -c 3000
```

If the converter reports restriction diagnostics (hosted services reached from authored code) or conversion errors, record them verbatim in the report; they are findings, not blockers. If `--platform retroppc` is rejected, use the platform id the `native-core-boot` preset declares (`CreateCustomHeadless("retroppc", …)`) via `--platform generic --set pointer-size-bytes=2 --set generated-math-convention=native-column-vector` and note it.

- [ ] **Step 2: Compile the unity file with the SNES compiler**

```bash
ls /c/dev/helworks/builds/csharpcodegen/core-freestanding | head -5; ls /c/dev/helworks/builds/csharpcodegen/core-freestanding/*.cpp | wc -l
MSYS_NO_PATHCONV=1 docker run --rm -v "/c/dev/helworks:/hw" -w /hw/builds/csharpcodegen/core-freestanding helengine-snes-toolchain bash -c '/usr/lib/llvm-mos-65816/bin/mos-snes-far-clang++ -std=c++20 -fno-exceptions -fno-rtti -Os -ferror-limit=0 -I. -c generated_unity.cpp -o core-freestanding.o 2>&1 | tee compile.log | grep -c "error:"; grep "error:" compile.log | sed -E "s/^[^:]+:[0-9]+:[0-9]+: error: //" | sort | uniq -c | sort -rn | head -40; ls -la core-freestanding.o 2>/dev/null && /usr/lib/llvm-mos-65816/bin/llvm-size core-freestanding.o'
```

(`generated_unity.cpp` is the compile harness the generator writes; if the output uses `helengine_core_unity.cpp` instead, compile that.)

- [ ] **Step 3: Write the report**

`docs/superpowers/reports/<today>-freestanding-core-compile.md` with: the exact generation command and its diagnostics; the error count and the top categories from Step 2 grouped as (a) provider or runtime gaps, (b) pointer size and address-space issues, (c) compiler limitations, (d) authored-code hosted services; the object size from `llvm-size` if it compiled; the list of remaining hosted facilities; and a short "what sub-project 2 must solve" section. Keep it factual; no fixes in this task.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/reports
git commit -F - <<'EOF'
docs: record the generated engine core's compile result on the 65816 toolchain

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

## Self-review notes

- Spec coverage: runtime profile (Task 7), restriction (Task 7), provider library incl. hooks/hash/string/vector (Task 3), map/set (Task 4), function/shared_ptr/provider header (Task 5), math (Task 6), hosted-header hygiene and `HE_CPP_RUNTIME_HAS_HOSTED_SERVICES` (Tasks 1, 2), tests: unit (Task 7), poison-include host gate (Task 2 onward), behavioural (Tasks 3-6), cross-compile (Tasks 3-6, 8), generated-code check (Task 8), final measurement (Task 9), acceptance (all), process (Global Constraints).
- Deviations from the spec recorded in Global Constraints and applied to the spec text in Task 7 Step 7.
- Type consistency: `he_cpp_alg` names used in Tasks 2-6 match Task 1; `FreestandingHash<T>` is the class name in Task 3 and the `Hash` alias target in Task 5; `InsertResult{first, second}` is used by the map tests in Task 4 and defined in the table; `he_cpp_custom::Allocate/Free/Fail/MonotonicMicroseconds` are declared in Task 3 and used in Tasks 3-6; `freestanding_flags`/`freestanding_includes`/`target_freestanding_flags` are defined in Task 2 and Task 5 before use.
