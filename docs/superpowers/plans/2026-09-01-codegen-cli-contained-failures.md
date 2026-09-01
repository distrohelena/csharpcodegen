# Codegen CLI Contained Failures Plan

> **Worker:** Implement with `superpowers:test-driven-development`; do not run the installed `codegen.exe` again until the contained-failure build is verified.

**Goal:** Make conversion failures normal CLI failures: write diagnostics to stderr, return nonzero, and never escape `Main` as an unhandled exception that Windows can surface as an Application Error MessageBox.

**Root cause:** `codegen/Program.cs` invokes `CodeConverter.AddCsproj` through reflection and has no top-level exception boundary. Ownership diagnostics therefore arrive wrapped in `TargetInvocationException`, escape `Main`, print as `Unhandled exception`, and repeatedly trigger Windows crash UI during iterative DemoDisc packaging.

**Architecture:** Split the entrypoint into a tiny `Main` and a testable CLI execution method. Put one final `try/catch (Exception)` around parsing, option construction, reflection invocation, and output writing. Unwrap nested `TargetInvocationException` wrappers for a useful converter diagnostic, write a deterministic `Codegen failed: ...` message to stderr, and return a distinct nonzero conversion-failure code. Do not swallow process-corruption exceptions explicitly, show UI, alter converter ownership rules, or change successful output semantics.

**Files:**

- Modify: `codegen/Program.cs`
- Add or modify: `cs2.cpp.tests/CodegenCliFailureBoundaryTests.cs`
- If required for direct test access, add: `codegen/AssemblyInfo.cs` with test-only `InternalsVisibleTo`

## Task 1: Add a failing entrypoint-boundary test

- [ ] Create a minimal temporary C# project that deterministically triggers a converter ownership diagnostic.
- [ ] Invoke the testable CLI execution method in-process while capturing stderr.
- [ ] Require a nonzero exit code, the underlying diagnostic text, no `TargetInvocationException`/`Unhandled exception` prefix, and no exception escaping the call.
- [ ] Record the meaningful red result against the current unguarded entrypoint.

## Task 2: Contain conversion exceptions

- [ ] Preserve current parse-validation exit behavior and successful `C++ conversion completed.` output.
- [ ] Add one top-level conversion failure boundary returning a stable nonzero exit code.
- [ ] Unwrap reflection invocation wrappers without discarding the original converter diagnostic message.
- [ ] Write only to stderr; do not call `MessageBox`, Windows Error Reporting APIs, or environment-wide UI suppression.

## Task 3: Verify the executable behavior

- [ ] Run the focused failure-boundary test and existing CLI parser tests.
- [ ] Build `codegen/codegen.csproj` Release in this isolated worktree.
- [ ] Launch this worktree's newly built executable against the retained DemoDisc gameplay project, where the next ownership diagnostic is expected. Require a normal nonzero exit, captured stderr diagnostic, no `Unhandled exception`, and no surviving `codegen`, `WerFault`, or Application Error process/window.
- [ ] Do not use the installed executable for this verification.

## Task 4: Commit narrowly and hand off

- [ ] Run `rtk git diff --check` and the relevant `cs2.cpp.tests` filters.
- [ ] Stage only the plan-approved source/test files and commit as `Contain codegen CLI conversion failures`.
- [ ] Report the verified worktree executable path so the Windows-only local platform manifest can temporarily point at it for the remaining DemoDisc diagnostics.

