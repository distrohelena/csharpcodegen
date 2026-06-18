# C# to C++

`cs2.cpp` is the profile-driven C++ backend for converting the portable engine core into a headless native codebase.

## Active profiles

The current default conversion target is:

- compiler: `msvc`
- platform: `windows-headless`
- runtime: `stl-lite`

These profiles are written into `cpp-conversion-report.json` so audit runs can be compared deterministically.

## Deterministic audit test

Use the fixture-backed audit test to validate report structure without depending on a specific local engine checkout:

```bash
rtk dotnet test /mnt/c/dev/csharpcodegen/cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPFixtureAuditTests" -v minimal
```

## Real project audit

The backend does not expose a dedicated CLI yet. Use a temporary console runner that references `cs2.cpp`, loads the real project, and writes generated output plus `cpp-conversion-report.json`.

Project target example:

```text
/mnt/c/dev/sample-engine/core/sample.core.csproj
```

Expected audit artifacts:

- generated output folder is created
- `helcpp_config.hpp` is written
- `cpp-conversion-report.json` is written
- unsupported syntax is summarized explicitly under `unsupportedSyntaxSummary`

The report contains:

- active compiler, platform, and runtime profiles
- emitted file and type counts
- error, warning, and info counts
- diagnostics grouped by source type and member
- unsupported syntax summary counts

## GameCube core-boot generation

Generate GameCube-targeted core output with the named preset:

```bash
rtk dotnet run --project /mnt/c/dev/helworks/csharpcodegen/codegen/codegen.csproj -- --cpp --project /mnt/c/dev/sample-engine/core/sample.core.csproj --output /mnt/c/dev/sample-output/generated-core-retroppc --platform retroppc --compiler gcc --endianness big --set generated-math-convention=native-column-vector --set pointer-size-bytes=4 --preset native-core-boot
```

Expected artifacts:

- `generated_unity.cpp`
- `helcpp_config.hpp`
- `cpp-conversion-report.json`
