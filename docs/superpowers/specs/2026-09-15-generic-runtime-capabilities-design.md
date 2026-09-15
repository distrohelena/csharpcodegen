# Generic C++ runtime capabilities

## Approved intent

The generated runtime must support restricted targets through generic options
owned by csharpcodegen. The user approved this boundary on 2026-09-15. A platform
selects capabilities and supplies SDK hooks; codegen must not contain PS1 checks
or rewrite generated output. Existing PS1 scene export is a separate checkpoint.

## Design

Use the existing runtime profile flags as the source of truth. Resolve generic
caller options after preset defaults so any platform can override capabilities.
String construction and type emission must use the same resolved string type.
Runtime templates must choose standard or caller-supplied storage consistently.
Do not inject alternate implementations into namespace std.

Provide a documented custom runtime header contract for replacement strings,
vectors, dictionaries, hashing, and fatal failure handling. Keep managed-facing
String/List/Dictionary behavior in the shared templates. Allocation is owned by
the selected implementation and platform, using the existing native allocation
boundary. A missing required provider must cause a clear error.

When exceptions are disabled, explicit unrecoverable throws terminate through a
non-returning failure hook. Catch/rethrow behavior must not be silently erased:
diagnose unsupported recovery. Keep ordinary finally cleanup through the scope
guard. When RTTI is disabled, do not emit dynamic_cast or typeid; permit proven
static conversions and diagnose operations requiring unavailable runtime type
information. Do not fabricate successful casts or silently discard type names.

Preserve hosted behavior where capabilities are enabled. Existing defaults that
declare facilities disabled while emitting them need explicit migration evidence;
do not hide the discrepancy with an unrelated platform special case.

## Scope and validation

Implement the capability contract, shared collection/string/failure runtime,
generator emission and focused regression tests. Verify custom and standard
configurations by compiling C++ and executing host fixtures. Compile the custom
fixture with the pinned PS1 compiler and no exceptions/RTTI as an additional
consumer check. A host-only fake provider does not establish freestanding support.

Test string lifetime/concatenation, collection growth and lookup, failure hooks,
option precedence, missing providers and unsupported recovery/type operations.
Run related existing tests to detect changes to hosted behavior. Report remaining
runtime facilities that require host services explicitly. This work does not by
itself prove a full generated Helengine Core links or fits PS1 retail RAM.

## Alternatives considered

A PS1-only generator branch would duplicate semantics and violates the approved
boundary. A hosted C++ library port changes the toolchain and is unnecessary for
the generic capability contract. The selected provider approach shares lowering
and managed semantics while allowing each target to choose suitable storage.
