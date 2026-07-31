# C++ Ownership Runtime Project Boundary Design

**Status:** Approved design

**Date:** 2026-07-31

## Context

Windows native builds currently invoke code generation directly on `helengine.shader.csproj`. That project includes both runtime shader types and editor-only shader compilation code under `shaders/compilation`. Consequently, compiler, parser, and cache methods enter native conversion even though packaged games never execute them.

The ownership analyzer correctly reports ambiguity in the source it receives, but the source inventory is wrong. Adding native ownership attributes to editor compiler APIs would encode meaningless runtime contracts and create annotation clutter.

## Decision

The managed project graph will express the runtime/editor boundary before code generation:

- `helengine.shader` contains only runtime shader assets, material types, runtime content processors, and runtime rendering contracts.
- `helengine.shader.compilation` contains shader source parsing, preprocessing, compile requests, compile caches, compile services, and compiler-facing models.
- Editor and shader-building projects reference `helengine.shader.compilation` when they need compilation services.
- Native platform regeneration continues to codegen `helengine.shader` and therefore never receives editor compiler source.

Ownership validation remains a hard gate for every project actually supplied to native code generation. It does not need exclusions or compiler-specific exceptions because editor compiler code is absent from the runtime project graph.

## Project structure

`engine/helengine.shader/helengine.shader.csproj` will remove `shaders/compilation/**/*.cs` from its compile items.

Four types currently stored under that directory are runtime contracts rather than compiler implementation: `ShaderCompileTarget`, `ShaderTargetNames`, `ShaderBindingPolicy`, and `ShaderBindingPolicies`. They will move to `engine/helengine.shader/shaders/runtime` before the compilation glob is excluded. Runtime package loading and renderer binding-slot lookup therefore remain owned by `helengine.shader` without creating a dependency from runtime shader code back to the compiler project.

`engine/helengine.shader.compilation/helengine.shader.compilation.csproj` will explicitly link the remaining `../helengine.shader/shaders/compilation/**/*.cs` files and reference `helengine.shader`. Compiler implementation source remains in its existing conceptual folder; only the four misplaced runtime contracts move.

Projects that compile shaders will reference the compilation project directly. Projects that only consume runtime shader assets retain only the runtime project reference.

This direct-consumer rule also applies to out-of-tree platform builders. The current PS Vita and Wii U builders implement shader compiler backends and must reference `helengine.shader.compilation`; platform builders that only stage already-generated runtime sources do not gain that dependency.

## Native build inventory

The existing generated-core regeneration service will continue invoking codegen per declared runtime project. Its project list must include `helengine.shader` and must not include `helengine.shader.compilation`.

A build-graph test will hard-fail if the compilation project is ever added to a native platform regeneration set. A generated-output test will also assert that representative compiler types such as `ShaderCompileService` and `HlslShaderBindingParser` are absent.

No generated file deletion, post-processing, source rewriting, type-name blacklist, platform waiver, or ownership suppression will be introduced.

## Ownership inference

Ordinary ownership remains inferred:

- fresh objects, arrays, and materialized collections are owned;
- fields, properties, parameters, caches, singletons, and shared empty values are borrowed unless their analyzable behavior proves a transfer;
- local cleanup is emitted automatically when ownership remains local;
- explicit attributes are reserved for genuine runtime API boundaries that cannot be inferred.

Ownership edits added solely because editor compilation code entered runtime conversion will be reverted. Runtime shader fixes remain only where emitted code has a real lifetime requirement.

## Dependency rules

The runtime shader project cannot reference the compilation project.

The compilation project references the runtime shader project because compile requests and results use runtime shader enums and metadata types.

Editor, DirectX compiler integration, Vulkan compiler integration, validation tooling, and tests may reference both projects. Runtime-only content, files, and renderer abstractions must not gain a compilation-project dependency unless they execute managed shader compilation outside packaged native games.

Circular project references are hard errors and must be resolved by moving shared runtime contracts into `helengine.shader`, not by merging the projects again.

## Rollback

The following diagnostic-only changes will be removed unless independent runtime evidence requires them:

- ownership contracts on `IShaderCompileCache` and `ShaderMemoryCompileCache`;
- compile-key deletion added to `ShaderCompileService`;
- owned-source lifetime changes added to `ShaderCompileRequest`;
- compile-service ownership contract tests introduced during the mistaken native compiler pass.

Runtime material, runtime shader definition, and serialized shader asset changes will be reviewed individually against the runtime project conversion rather than reverted wholesale.

## Verification

Focused tests will prove:

- `helengine.shader` compiles without any `shaders/compilation` source;
- runtime target-name and binding-policy contracts remain available from `helengine.shader`;
- `helengine.shader.compilation` compiles and exposes the compiler APIs to managed editor consumers;
- runtime projects do not reference the compilation project;
- editor/compiler consumers have the required explicit project reference;
- PS Vita and Wii U shader builder projects and their focused tests compile against the explicit compilation project;
- generated native shader output contains runtime shader types but no compiler/parser/cache types;
- ownership diagnostics from compiler-only methods cannot occur during runtime shader conversion.

Repository validation will then:

1. run focused project-boundary and ownership tests;
2. build the exact Windows DemoDisc native artifact into a workspace-owned output directory;
3. verify the required executable exists and is newer than the build start marker;
4. launch that exact Windows executable from its packaged working directory;
5. verify it remains alive through splash, loading, and menu startup;
6. only after Windows passes, resume PS2 native validation.

## Acceptance criteria

- Native codegen never receives editor shader compiler source.
- Managed editor shader compilation still builds and passes focused tests.
- No compiler-only native ownership attributes remain from this investigation.
- Runtime ownership errors remain hard failures.
- Generated Windows shader output contains runtime shader support and excludes compiler support.
- The exact newly generated Windows DemoDisc artifact builds and launches before PS2 work resumes.
