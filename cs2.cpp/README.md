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
rtk dotnet test /mnt/c/dev/csharpcodegen/cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPHelengineCoreAuditTests" -v minimal
```

## Real `helengine.core` audit

The backend does not expose a dedicated CLI yet. Use a temporary console runner that references `cs2.cpp`, loads the real project, and writes generated output plus `cpp-conversion-report.json`.

Project target:

```text
/mnt/c/dev/helengine/engine/helengine.core/helengine.core.csproj
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
