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
string growth/copy, collections, and failure policy.

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
```

Set `GENERATED_OUTPUT` to that generated directory when running `run.sh`.
The runner builds `StringGate.cpp` with `generated-smoke.cpp`, executes normal
behavior and both fatal paths, and cross-compiles the generated class when
`TARGET_CXX` is supplied. It does not compile the full generated unity harness,
which currently includes hosted I/O sources even for this small fixture.
