# Repository Guidelines

## Project Structure & Module Organization
- `cs2.core/`: Roslyn-based core (models, processors, utilities) shared by backends.
- `cs2.ts/`: TypeScript backend and TS runtime under `.net.ts/` (helpers copied on build).
- `cs2.cpp/`: C++ backend and C++ runtime under `.net.cpp/`.
- `cs2.cpp.symbols/`: C++ helper project for symbol extraction; requires Doxygen in `PATH`.
- Samples/Tests: `codegen.sample/` (usage example), `codegen.testproj/` (smoke tests).
- Solution: `codegen.sln` aggregates all projects.

## Build, Test, and Development Commands
- `dotnet build codegen.sln -c Debug` — build all projects (requires .NET 9 SDK).
- `dotnet run --project codegen.testproj` — run smoke tests (binary IO, memory stream).
- `dotnet run --project codegen.sample` — run the sample app.
- TypeScript helpers: `cd cs2.ts/.net.ts && npm install && npx tsc` — validate TS runtime.
- C++ backend: ensure `doxygen` is installed and on `PATH` before using `cs2.cpp`.

## Coding Style & Naming Conventions
- C#: 4-space indent, braces on same line, nullable by project settings.
- Naming: PascalCase for types/methods; camelCase for locals/parameters.
- Namespaces: `cs2.core`, `cs2.ts`, `cs2.cpp` to scope code by module.
- File/module organization: place new types under `model/`, utilities under `util/`.
- Backend class prefixes: mirror target (e.g., `TypeScript*`, `CPP*`).

## Testing Guidelines
- Use `codegen.testproj` for smoke tests; add files as `*Test.cs` and invoke from `Program.cs`.
- Prefer deterministic console output to validate behavior (e.g., BinaryReader/Writer roundtrips).
- Aim to cover new rules, type mappings, and edge cases you introduce.

## Commit & Pull Request Guidelines
- Commits: imperative, concise (e.g., "Fix constructors", "Add IDisposable").
- One logical change per commit; do not commit `bin/`, `obj/`, or generated artifacts.
- PRs: include a clear description, affected backend(s), reproduction/verification steps
  (e.g., `dotnet run --project codegen.testproj`), and sample output where helpful. Link issues.

## Security & Configuration Tips
- Prereqs: .NET 9 SDK; Node.js (for TS runtime/tools); Doxygen (for C++ symbols).
- Do not commit large test data or generated files. Keep paths portable across OSes.

