# Runtime capability integration fixture

This fixture exercises the same shared runtime with standard storage and a
consumer-owned EASTL provider. The provider lives in the test, not in codegen.
Supply your existing EASTL/EABase checkout and a C++20 compiler:

```sh
sh tests/runtime-capabilities-integration/run.sh /absolute/build-output /absolute/dependencies
```

The dependencies directory must contain `EASTL/include`, `EASTL/source`, and
`EABase/include/Common`. `CXX` chooses the host compiler. Optional `TARGET_CXX`
also compiles the custom fixture to an object with exceptions and RTTI disabled.
That target object is compile evidence only: it is not a linked application or
a console memory-budget measurement. The host executable checks value ownership,
string growth/copy, single-separator `String::Split` semantics, collections, and
failure policy.

`custom/helcpp_config.hpp` and `hosted/helcpp_config.hpp` are deliberate test
inputs. Production configurations come from the generator. No generated output
is patched by this test.

## Generated-code check

Generate the authored `fixture/Fixture.csproj` through the codegen CLI with a
generic platform and the following options (use an external output directory):

```text
--cpp --project <fixture/Fixture.csproj> --output <generated-output>
--compiler gcc --platform generic --runtime custom-retro
--set generated-math-convention=engine-row-vector --set pointer-size-bytes=4
--set load-native-runtime-metadata=false
--set codegen-runtime-provider-header=eastl_provider.hpp
--set codegen-use-std-unordered-set=false --set codegen-use-std-function=false
--set codegen-use-std-shared-ptr=false --set codegen-use-std-chrono=false
```

Set `GENERATED_OUTPUT` to that generated directory when running `run.sh`.
The runner builds `StringGate.cpp` with `generated-smoke.cpp`, executes normal
behavior and both fatal paths, and cross-compiles the generated class when
`TARGET_CXX` is supplied. It does not compile the full generated unity harness,
which currently includes hosted I/O sources even for this small fixture.

## Freestanding variant

The `freestanding/` config selects the codegen-owned provider under
`cs2.cpp/.net.cpp/runtime/freestanding/`. The `poison/` directory is placed
first on the include path so any hosted header include fails on the host.
With `TARGET_CXX` set to the SNES toolchain driver
(`/usr/lib/llvm-mos-65816/bin/mos-snes-far-clang++` in the
`helengine-snes-toolchain` Docker image) the same sources are cross-compiled
to objects as freestanding compile evidence.

### Generated-code check under the freestanding runtime

Generate the same authored `fixture/Fixture.csproj` a second time, now with
`--runtime freestanding` instead of `--runtime custom-retro` (no
`codegen-runtime-provider-header` or storage overrides: the freestanding
resolver injects the provider and math header defaults itself):

```text
--cpp --project <fixture/Fixture.csproj> --output <generated-freestanding-output>
--compiler gcc --platform generic --runtime freestanding
--set generated-math-convention=engine-row-vector --set pointer-size-bytes=4
--set load-native-runtime-metadata=false
```

Set `GENERATED_FREESTANDING_OUTPUT` to that generated directory when running
`run.sh`. The generated output ships its own `helcpp_config.hpp`
(`HE_CPP_RUNTIME_FREESTANDING 1`, `HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 0`, and
the two default `runtime/freestanding/...` headers), so this check does not
add `-I"$fixture/freestanding"`; the generated config is the one in effect.
The runner builds `StringGate.cpp` with `generated-smoke.cpp` and
`freestanding_hooks_default.cpp` behind the `poison/` directory, executes
normal behavior and both fatal paths, and cross-compiles the generated class
when `TARGET_CXX` is supplied. `freestanding_math.cpp` is linked here even
though this fixture never reaches `system/math.hpp`: the generated
`helcpp_config.hpp` sets `HE_CPP_RUNTIME_FREESTANDING`, so
`freestanding_math.hpp` selects the software path on the host as well and the
file supplies the C math names itself instead of deferring to a platform libm
the target does not have. Any generated freestanding tree that does reach
`system/math.hpp` must put it on the link line for the same reason.

Two conversion settings this fixture does not need, but a real freestanding
conversion does. Generated code that dispatches a generic implementation
through an abstract base (helengine's `EngineBinaryReader`) needs
`--set codegen-use-rtti=true`, as the PS1 platform definition sets, or the
conversion stops with `CPP1001`; compiling that output for the 65816 with
`-fno-rtti` costs about 122 `dynamic_cast`/`typeid` errors, so the RTTI-bearing
translation units must be built without that flag. And the
`native-core-boot-freestanding` preset keeps the 4-byte pointer default, so an
SNES conversion passes `--set pointer-size-bytes=2` together with
`--platform generic --set generated-math-convention=native-column-vector`;
`--platform retroppc` is rejected without them.

