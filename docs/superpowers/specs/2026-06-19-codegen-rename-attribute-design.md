# CodeGenRename Attribute Design

## Goal

Add a source-level type rename contract that lets C# projects request a specific generated native type name without relying on CLI remaps, generated-output rewrites, or downstream compatibility hacks.

## Current Problem

`csharpcodegen` currently has two naming controls for generated C++ types:

- the default emitted-name algorithm
- CLI/configured type remaps

That is not enough for repo-authored source that needs a stable, local override at the declaration site.

The current failure mode is visible in C++ generation when unrelated source types share the same emitted short name. The generator can sometimes recover through remap configuration, but that pushes a source-owned naming decision into build orchestration and makes the rename contract easy to miss.

The missing capability is a small source-level declaration contract.

## Requirements

- A project can annotate a declared type with a codegen rename attribute.
- The rename contract lives in a dedicated attributes-only assembly, not in `cs2.core` or any backend project.
- The first pass supports declared types only.
- Supported targets are `class`, `struct`, `interface`, `enum`, and `delegate`.
- The C++ backend must honor the requested emitted type name.
- Other backends may ignore the attribute for now.
- Existing CLI/configured type remaps must keep working.
- The generator must still reject duplicate final emitted type names when two types resolve to the same final name.

## Non-Goals

- No member-level rename support in this change.
- No generated-output post-processing.
- No backend-specific attributes assemblies.
- No requirement that TypeScript or Go adopt the attribute in this pass.
- No silent fallback when the requested name is invalid or conflicts with another final emitted type name.

## Recommended Approach

Create a new `cs2.attributes` project containing only source-consumable attributes, starting with `CodeGenRenameAttribute`.

The generator should read the attribute from Roslyn type metadata during preprocessing or emitted-name resolution and carry the resolved type rename through the shared type model used by the C++ backend. The C++ backend should then use the resolved emitted type name as its canonical type identifier before file naming, include generation, or collision validation.

This keeps the ownership boundary correct:

- user code references only the attributes assembly
- generator implementation stays in generator projects
- final generated names are decided semantically before files are emitted

## Architecture

### 1. Attributes Assembly

Add a new project, `cs2.attributes`, with no Roslyn dependency and no backend logic.

Initial contents:

- `CodeGenRenameAttribute`

Attribute contract:

- namespace should be stable and generator-owned
- constructor takes one required emitted-name string
- `AttributeUsage` targets type declarations only

This assembly is the only dependency user projects need for source-level naming overrides.

### 2. Shared Type Metadata

`csharpcodegen` already tracks declared type metadata through preprocessing and conversion models. The emitted-name decision should gain one additional source:

1. explicit source attribute rename
2. configured CLI/type-remap rename
3. default emitted-name algorithm

The exact precedence should favor the source attribute over the default algorithm. If a configured remap is still intended to override source annotations globally, that must be made explicit in implementation and covered by tests. The safer default for this feature is:

1. configured remap
2. source attribute
3. default emitted name

That preserves build-level override power while still giving source declarations a local contract.

### 3. Validation

The generator must validate the requested emitted name before writing output.

Validation rules:

- name must not be null, empty, or whitespace
- name must be a valid generated type identifier for the target backend
- two source types must not end up with the same final emitted type name

Failures should be reported as generator errors with the declaring source type identified in the message.

### 4. C++ Backend Integration

The C++ backend should consume the resolved final emitted type name from shared type metadata rather than performing its own attribute lookup in scattered emitter code.

That final name must flow through:

- generated class/type names
- file names derived from emitted type names
- include references
- collision detection and diagnostics

The backend should not special-case `CodeGenRename` in multiple places if one resolved-name path can own the decision.

## Testing

Minimum coverage:

- attribute assembly builds and can be referenced from a sample/test project
- a type annotated with `[CodeGenRename("DesiredName")]` emits `DesiredName` in C++
- unannotated types keep existing behavior
- configured type remaps still work
- duplicate final emitted names fail with a clear error
- invalid attribute payloads fail with a clear error

Preferred test shape:

- add focused `cs2.cpp.tests` coverage using small in-memory or sample-project inputs
- avoid broad integration churn when one focused conversion assertion is enough

## Risks

- Type-name resolution may currently be split across preprocessing and backend emission; implementation should consolidate rather than duplicate rename logic.
- The existing leaf-name alias behavior for configured remaps may interact with attribute-driven names; tests must cover collisions and precedence explicitly.
- If other backends later adopt the attribute, they should consume the same resolved shared type metadata rather than reinterpreting Roslyn attributes independently.

## Result

After this change, source authors can request stable generated type names directly in C# through a tiny shared attributes assembly, while `csharpcodegen` remains the sole owner of final emitted output shape.
