# C++ Native Ownership Analysis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add semantic native ownership analysis to the C++ generator so factory-returned allocations are cleaned up or explicitly transferred, while ambiguous ownership stops code generation with a source-located hard error.

**Architecture:** A C++-specific pipeline stage analyzes the preprocessed Roslyn compilations before class lowering. It resolves method ownership summaries to a call-graph fixed point, performs per-method control-flow analysis, and produces immutable emission plans consumed by `CPPConversiorProcessor`; generated C++ is never scanned or rewritten to repair ownership.

**Tech Stack:** C# 13, .NET 9/10, Roslyn semantic operations and `ControlFlowGraph`, xUnit, the existing `he_cpp_make_scope_exit` native helper, Helengine's PS2 build waiter, and PCSX2.

## Global Constraints

- Ownership classifications are exactly `Owned`, `Borrowed`, and `Unknown`; `Unknown` is a hard codegen error.
- There is no legacy mode, warning fallback, platform waiver, or generated-code post-processing.
- The same semantic ownership rules apply to every C++ target.
- The first implementation tracks containers and directly allocated objects, not ownership of objects stored inside collections.
- Existing `NativeOwnership` helpers and `NativeNoEscape` remain supported.
- `CPPGeneratedOwnershipValidator` remains a narrow generated-contract validator and is not expanded into semantic text scanning.
- Source-visible annotations are assertions validated against inferred behavior; they cannot override contradictory code.
- Use one class or enum per file and add substantive XML comments to every class, field, property, constructor, and method.
- Use PascalCase fields, no tuples, no nullable annotations, and braces on the same line as declarations and control statements.
- Work directly on the main checkout. Do not create a worktree.
- Preserve the unrelated generated-function-profiling edits currently present in `csharpcodegen`.
- Keep test fixtures and build artifacts under repository `scratch/` or `C:\dev\helworks\builds`; do not direct them to `%TEMP%`.
- Every shell command begins with `rtk.exe`.

---

### Task 1: Route test artifacts to a workspace-owned directory

**Files:**
- Create: `cs2.cpp.tests/TestHelpers/CPPTestEnvironment.cs`
- Create: `cs2.cpp.tests/CPPTestEnvironmentTests.cs`

**Interfaces:**
- Produces: a test-assembly module initializer that sets `TEMP` and `TMP` to `<repository>/scratch/test-temp` before any converter fixture calls `Path.GetTempPath()`.

- [ ] **Step 1: Write the failing workspace-root test**

```csharp
[Fact]
public void ModuleInitializer_RoutesTemporaryOutputIntoRepositoryScratch() {
    string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    string expectedRoot = Path.Combine(repositoryRoot, "scratch", "test-temp");

    Assert.Equal(expectedRoot, Path.TrimEndingDirectorySeparator(Path.GetTempPath()));
    Assert.True(Directory.Exists(expectedRoot));
}
```

- [ ] **Step 2: Run the focused test and verify it fails against the system temp path**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter FullyQualifiedName~CPPTestEnvironmentTests --no-restore
```

Expected: FAIL because `Path.GetTempPath()` still resolves under `%TEMP%`.

- [ ] **Step 3: Add the test-assembly module initializer**

```csharp
namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Routes test-owned generated projects, native sources, and logs into the repository's ignored scratch directory.
/// </summary>
internal static class CPPTestEnvironment {
    /// <summary>
    /// Configures the process temporary-directory contract before xUnit creates any test fixtures.
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize() {
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string scratchRoot = Path.Combine(repositoryRoot, "scratch", "test-temp");
        Directory.CreateDirectory(scratchRoot);
        Environment.SetEnvironmentVariable("TEMP", scratchRoot);
        Environment.SetEnvironmentVariable("TMP", scratchRoot);
    }
}
```

- [ ] **Step 4: Run the focused test**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter FullyQualifiedName~CPPTestEnvironmentTests --no-restore
```

Expected: PASS, with fixture files under `scratch/test-temp`.

- [ ] **Step 5: Commit the workspace-owned test root**

```powershell
rtk.exe git add cs2.cpp.tests/TestHelpers/CPPTestEnvironment.cs cs2.cpp.tests/CPPTestEnvironmentTests.cs
rtk.exe git commit -m "test: keep cpp fixtures in workspace scratch"
```

---

### Task 2: Add source ownership contract attributes

**Files:**
- Create: `cs2.attributes/NativeOwnedReturnAttribute.cs`
- Create: `cs2.attributes/NativeBorrowedReturnAttribute.cs`
- Create: `cs2.attributes/NativeTakesOwnershipAttribute.cs`
- Create: `cs2.attributes/NativeOwnedMemberAttribute.cs`
- Modify: `cs2.cpp/CPPGeneratedTypeEmissionPolicy.cs`
- Create: `cs2.cpp.tests/CPPNativeOwnershipAttributeTests.cs`
- Create: `cs2.cpp.tests/CPPGeneratedOwnershipContractEmissionPolicyTests.cs`

**Interfaces:**
- Produces: `[NativeOwnedReturn]` for methods and properties whose callers own non-null results.
- Produces: `[NativeBorrowedReturn]` for methods and properties whose callers borrow non-null results.
- Produces: `[NativeTakesOwnership]` for parameters that assume native cleanup responsibility.
- Produces: `[NativeOwnedMember]` for fields and properties whose containing type proves replacement and teardown cleanup.

- [ ] **Step 1: Write failing attribute metadata tests**

```csharp
[Fact]
public void NativeOwnedReturnAttribute_TargetsMethodsAndProperties() {
    AttributeUsageAttribute usage = typeof(NativeOwnedReturnAttribute)
        .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
        .Cast<AttributeUsageAttribute>()
        .Single();

    Assert.Equal(AttributeTargets.Method | AttributeTargets.Property, usage.ValidOn);
    Assert.False(usage.AllowMultiple);
    Assert.False(usage.Inherited);
}

[Fact]
public void NativeTakesOwnershipAttribute_TargetsParameters() {
    AttributeUsageAttribute usage = typeof(NativeTakesOwnershipAttribute)
        .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
        .Cast<AttributeUsageAttribute>()
        .Single();

    Assert.Equal(AttributeTargets.Parameter, usage.ValidOn);
}
```

Repeat the exact target assertions for `NativeBorrowedReturnAttribute` and `NativeOwnedMemberAttribute` (`Field | Property`).

- [ ] **Step 2: Run the focused tests and confirm they fail because the attributes do not exist**

Run:

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter FullyQualifiedName~CPPNativeOwnershipAttributeTests --no-restore
```

Expected: compilation failure naming the four missing attribute types.

- [ ] **Step 3: Implement the four zero-state metadata attributes**

Each file follows this shape with its exact `AttributeTargets` value:

```csharp
namespace cs2.attributes;

/// <summary>
/// Declares that generated native callers assume ownership of each non-null value returned by the annotated method or property.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NativeOwnedReturnAttribute : Attribute {
    /// <summary>
    /// Initializes the compile-time ownership contract.
    /// </summary>
    public NativeOwnedReturnAttribute() {
    }
}
```

Use equivalent substantive documentation for the other three contracts.

- [ ] **Step 4: Write and run a failing emission-policy test**

Create a conversion fixture referencing `cs2.attributes`, annotate one method with each contract, and assert that generated output does not contain standalone `NativeOwnedReturnAttribute.hpp`, `NativeBorrowedReturnAttribute.hpp`, `NativeTakesOwnershipAttribute.hpp`, or `NativeOwnedMemberAttribute.hpp` files.

Run:

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter FullyQualifiedName~CPPGeneratedOwnershipContractEmissionPolicyTests --no-restore
```

Expected: FAIL because the new metadata types are not excluded yet.

- [ ] **Step 5: Exclude all contract attributes from generated runtime type emission**

Add both leaf and qualified identities to `CPPGeneratedTypeEmissionPolicy.ExcludedTypeNames`:

```csharp
"NativeOwnedReturnAttribute",
"NativeBorrowedReturnAttribute",
"NativeTakesOwnershipAttribute",
"NativeOwnedMemberAttribute",
"cs2.attributes.NativeOwnedReturnAttribute",
"cs2.attributes.NativeBorrowedReturnAttribute",
"cs2.attributes.NativeTakesOwnershipAttribute",
"cs2.attributes.NativeOwnedMemberAttribute",
```

- [ ] **Step 6: Run both focused test classes**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPNativeOwnershipAttributeTests|FullyQualifiedName~CPPGeneratedOwnershipContractEmissionPolicyTests" --no-restore
```

Expected: PASS.

- [ ] **Step 7: Commit the contract vocabulary**

```powershell
rtk.exe git add cs2.attributes cs2.cpp/CPPGeneratedTypeEmissionPolicy.cs cs2.cpp.tests/CPPNativeOwnershipAttributeTests.cs cs2.cpp.tests/CPPGeneratedOwnershipContractEmissionPolicyTests.cs
rtk.exe git commit -m "feat: add native ownership contracts"
```

---

### Task 3: Add ownership value types and the intrinsic catalog

**Files:**
- Create: `cs2.cpp/ownership/CPPOwnershipKind.cs`
- Create: `cs2.cpp/ownership/CPPOwnershipLifecycle.cs`
- Create: `cs2.cpp/ownership/CPPParameterOwnershipKind.cs`
- Create: `cs2.cpp/ownership/CPPOwnershipTransitionKind.cs`
- Create: `cs2.cpp/ownership/CPPIntrinsicOwnershipCatalog.cs`
- Create: `cs2.cpp/ownership/CPPMethodOwnershipKey.cs`
- Create: `cs2.cpp.tests/TestHelpers/OwnershipRoslynTestHelper.cs`
- Create: `cs2.cpp.tests/CPPIntrinsicOwnershipCatalogTests.cs`
- Create: `cs2.cpp.tests/CPPMethodOwnershipKeyTests.cs`

**Interfaces:**
- Produces: `CPPOwnershipKind { Owned, Borrowed, Unknown }`.
- Produces: `CPPOwnershipLifecycle { Live, Released, Transferred, ScopeCleanup }`.
- Produces: `CPPParameterOwnershipKind { Unknown, NoEscape, TakesOwnership }`.
- Produces: `CPPOwnershipTransitionKind { Acquire, Replace, Release, Transfer, ScopeCleanup }`.
- Produces: `bool CPPIntrinsicOwnershipCatalog.TryGetReturnOwnership(IMethodSymbol method, out CPPOwnershipKind ownership)`.
- Produces: `bool CPPIntrinsicOwnershipCatalog.TryGetParameterOwnership(IParameterSymbol parameter, out CPPParameterOwnershipKind ownership)`.
- Produces: `string CPPMethodOwnershipKey.Create(IMethodSymbol method)` including assembly identity and original-definition signature.
- Produces: focused Roslyn test helpers for resolving invocations, declarations, containing methods, and compilations without duplicating semantic-model setup.

- [ ] **Step 1: Write failing catalog tests for known shared and allocating framework calls**

```csharp
[Theory]
[InlineData("System.Array.Empty<int>()", CPPOwnershipKind.Borrowed)]
[InlineData("System.Linq.Enumerable.Empty<int>()", CPPOwnershipKind.Borrowed)]
[InlineData("new int[4].ToArray()", CPPOwnershipKind.Owned)]
[InlineData("new int[4].Clone()", CPPOwnershipKind.Owned)]
public void TryGetReturnOwnership_ClassifiesKnownFrameworkCalls(string expressionText, CPPOwnershipKind expected) {
    IMethodSymbol method = OwnershipRoslynTestHelper.ResolveInvocation(expressionText);
    CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

    Assert.True(catalog.TryGetReturnOwnership(method, out CPPOwnershipKind ownership));
    Assert.Equal(expected, ownership);
}
```

Also assert an unrelated external factory returns `false` and `Unknown`, rather than being guessed.

- [ ] **Step 2: Write failing stable-key tests**

Resolve overloaded methods and assert keys differ by parameter type while the same method reached through source and metadata has the same assembly-qualified key.

```csharp
Assert.NotEqual(CPPMethodOwnershipKey.Create(intOverload), CPPMethodOwnershipKey.Create(stringOverload));
Assert.StartsWith("Fixture|", CPPMethodOwnershipKey.Create(intOverload), StringComparison.Ordinal);
```

- [ ] **Step 3: Run the focused tests and confirm missing-type failures**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPIntrinsicOwnershipCatalogTests|FullyQualifiedName~CPPMethodOwnershipKeyTests" --no-restore
```

Expected: compilation failure for the new ownership model types.

- [ ] **Step 4: Implement the enums, stable method key, and explicit intrinsic rules**

`CPPMethodOwnershipKey.Create` must use `method.OriginalDefinition`, `method.ContainingAssembly.Identity.Name`, and a `SymbolDisplayFormat` that includes containing type, method name, generic arity, parameter types, and `ref`/`out` modifiers.

Add `OwnershipRoslynTestHelper` as a public static test type wrapping the existing `RoslynTestHelper.CreateCompilation`. Its exact public methods are:

```csharp
public static CSharpCompilation CreateCompilation(string source, string filePath = "Fixture.cs");
public static IMethodSymbol ResolveInvocation(string expressionText);
public static SyntaxNode ResolveNode(CSharpCompilation compilation, string marker);
public static IMethodSymbol ResolveContainingMethod(CSharpCompilation compilation, SyntaxNode node);
```

`CPPIntrinsicOwnershipCatalog` must begin with these reviewed rules:

```csharp
if (IsMethod(method, "System.Array", "Empty")) {
    ownership = CPPOwnershipKind.Borrowed;
    return true;
} else if (IsMethod(method, "System.Linq.Enumerable", "Empty")) {
    ownership = CPPOwnershipKind.Borrowed;
    return true;
} else if (IsMethod(method, "System.Linq.Enumerable", "ToArray") ||
           IsMethod(method, "System.Linq.Enumerable", "ToList") ||
           IsMethod(method, "System.Array", "Clone")) {
    ownership = CPPOwnershipKind.Owned;
    return true;
}

ownership = CPPOwnershipKind.Unknown;
return false;
```

Parameter rules must recognize existing `NativeNoEscapeAttribute` and new `NativeTakesOwnershipAttribute` by semantic attribute class name, including their optional `Attribute` suffix.

- [ ] **Step 5: Run the focused tests**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPIntrinsicOwnershipCatalogTests|FullyQualifiedName~CPPMethodOwnershipKeyTests" --no-restore
```

Expected: PASS.

- [ ] **Step 6: Commit the ownership vocabulary and catalog**

```powershell
rtk.exe git add cs2.cpp/ownership cs2.cpp.tests/TestHelpers/OwnershipRoslynTestHelper.cs cs2.cpp.tests/CPPIntrinsicOwnershipCatalogTests.cs cs2.cpp.tests/CPPMethodOwnershipKeyTests.cs
rtk.exe git commit -m "feat: classify intrinsic native ownership"
```

---

### Task 4: Add precise ownership diagnostics

**Files:**
- Create: `cs2.cpp/ownership/CPPOwnershipDiagnosticFactory.cs`
- Modify: `cs2.cpp/model/CPPConversionDiagnostic.cs`
- Modify: `cs2.cpp/model/CPPConversionReport.cs`
- Modify: `cs2.cpp/CPPConversionReportWriter.cs`
- Modify: `cs2.cpp.tests/CPPConversionDiagnosticsTests.cs`
- Create: `cs2.cpp.tests/CPPOwnershipDiagnosticFactoryTests.cs`

**Interfaces:**
- Produces: `CPPConversionDiagnostic CPPOwnershipDiagnosticFactory.Create(string code, SyntaxNode node, ISymbol member, string message, string recommendation)`.
- Extends: `CPPConversionDiagnostic.LineNumber` and `ColumnNumber`, both one-based and `0` only when no source location exists.
- Extends: `CPPConversionReport.AddDiagnostic` with optional line and column arguments.

- [ ] **Step 1: Write failing source-location and serialization tests**

```csharp
[Fact]
public void Create_UsesOneBasedSourceCoordinates() {
    string source = "class Widget { void Run() { System.Collections.Generic.List<int> values = null; } }";
    CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation(source, "Widget.cs");
    SyntaxNode node = OwnershipRoslynTestHelper.ResolveNode(compilation, "List<int> values");
    ISymbol member = OwnershipRoslynTestHelper.ResolveContainingMethod(compilation, node);

    CPPConversionDiagnostic diagnostic = new CPPOwnershipDiagnosticFactory().Create(
        "CPPOWN001",
        node,
        member,
        "Ownership cannot be inferred.",
        "Declare an owned or borrowed return contract.");

    Assert.Equal("Widget.cs", Path.GetFileName(diagnostic.FilePath));
    Assert.True(diagnostic.LineNumber > 0);
    Assert.True(diagnostic.ColumnNumber > 0);
    Assert.Equal(CPPDiagnosticSeverity.Error, diagnostic.Severity);
}
```

Update the report-writer test to assert JSON contains `lineNumber`, `columnNumber`, and `diagnosticsSchema: cpp-conversion-report.v2`.

- [ ] **Step 2: Run the focused tests and verify failures**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPOwnershipDiagnosticFactoryTests|FullyQualifiedName~CPPConversionDiagnosticsTests" --no-restore
```

Expected: FAIL because source coordinate properties and the factory do not exist.

- [ ] **Step 3: Implement the diagnostic factory and report fields**

Use `node.GetLocation().GetLineSpan()` and convert Roslyn's zero-based coordinates to one-based values. Set the containing type and member from the supplied symbol. Put ownership origin and invalid sink in the diagnostic message, not in unstructured console output.

Update both serialized diagnostic projections and deterministic ordering to include line and column. Bump `diagnosticsVersion` to `2` and `diagnosticsSchema` to `cpp-conversion-report.v2`.

- [ ] **Step 4: Run the focused tests**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPOwnershipDiagnosticFactoryTests|FullyQualifiedName~CPPConversionDiagnosticsTests" --no-restore
```

Expected: PASS.

- [ ] **Step 5: Commit precise ownership diagnostics**

```powershell
rtk.exe git add cs2.cpp/ownership/CPPOwnershipDiagnosticFactory.cs cs2.cpp/model/CPPConversionDiagnostic.cs cs2.cpp/model/CPPConversionReport.cs cs2.cpp/CPPConversionReportWriter.cs cs2.cpp.tests/CPPConversionDiagnosticsTests.cs cs2.cpp.tests/CPPOwnershipDiagnosticFactoryTests.cs
rtk.exe git commit -m "feat: report source-located ownership errors"
```

---

### Task 5: Infer method return ownership to a fixed point

**Files:**
- Create: `cs2.cpp/ownership/CPPMethodOwnershipSummary.cs`
- Create: `cs2.cpp/ownership/CPPMethodOwnershipSummaryResolution.cs`
- Create: `cs2.cpp/ownership/CPPMethodOwnershipSummaryResolver.cs`
- Create: `cs2.cpp/ownership/CPPOwnershipExpressionClassifier.cs`
- Create: `cs2.cpp.tests/CPPMethodOwnershipSummaryResolverTests.cs`

**Interfaces:**
- Produces: `CPPMethodOwnershipSummary` with `MethodKey`, `RequiresReturnOwnership`, `ReturnOwnership`, and parameter contracts keyed by ordinal.
- Produces: `CPPMethodOwnershipSummaryResolution.Summaries`, `.Diagnostics`, and `.HasErrors`.
- Produces: `CPPMethodOwnershipSummaryResolution CPPMethodOwnershipSummaryResolver.Resolve(IReadOnlyList<Compilation> compilations)`.
- Produces: `CPPOwnershipKind CPPOwnershipExpressionClassifier.Classify(IOperation operation, IReadOnlyDictionary<string, CPPMethodOwnershipSummary> summaries)`.

- [ ] **Step 1: Write failing tests for fresh, borrowed, null, nested, and mixed returns**

Use one Roslyn compilation containing these methods:

```csharp
static List<int> Fresh() {
    return new List<int>();
}

static List<int> Nested() {
    return Fresh();
}

static List<int> MaybeFresh(bool enabled) {
    return enabled ? Fresh() : null;
}

static List<int> Borrowed(List<int> cached) {
    return cached;
}

static List<int> Mixed(bool enabled, List<int> cached) {
    return enabled ? new List<int>() : cached;
}
```

Assert `Fresh`, `Nested`, and `MaybeFresh` are owned; `Borrowed` is borrowed; and `Mixed` produces `CPPOWN005` at the mixed return.

- [ ] **Step 2: Add failing tests for recursion, explicit contracts, and contradictions**

Cover mutually recursive methods whose base return establishes owned classification, an external method marked `[NativeOwnedReturn]`, and a source-visible `[NativeBorrowedReturn]` method that returns `new List<int>()`. The contradiction must produce `CPPOWN006`.

Add source-visible parameter cases:

```csharp
static void ReadOnly(List<int> values) {
    Use(values.Count);
}

static void Destroy(List<int> values) {
    NativeOwnership.Delete(values);
}
```

Infer `ReadOnly` parameter `0` as `NoEscape` and `Destroy` parameter `0` as `TakesOwnership`. An explicitly declared `NativeNoEscape` parameter that stores its value in a field must produce `CPPOWN006`.

- [ ] **Step 3: Run the resolver tests and verify missing implementation failures**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter FullyQualifiedName~CPPMethodOwnershipSummaryResolverTests --no-restore
```

Expected: compilation failure for the missing summary resolver types.

- [ ] **Step 4: Implement expression classification**

Classification must unwrap conversions and parentheses, then apply these rules in order:

```csharp
if (operation is IObjectCreationOperation ||
    operation is IArrayCreationOperation ||
    operation is ICollectionExpressionOperation) {
    return CPPOwnershipKind.Owned;
} else if (operation.ConstantValue.HasValue && operation.ConstantValue.Value == null) {
    return CPPOwnershipKind.Unknown;
} else if (operation is IParameterReferenceOperation ||
           operation is IFieldReferenceOperation ||
           operation is IPropertyReferenceOperation ||
           operation is IInstanceReferenceOperation) {
    return CPPOwnershipKind.Borrowed;
} else if (operation is IInvocationOperation invocation) {
    return ResolveInvocationOwnership(invocation.TargetMethod, summaries);
}
```

Conditional expressions merge null with the non-null branch, preserve equal kinds, and return `Unknown` for owned/borrowed mixtures. Backend array-to-`IReadOnlyList<T>` materialization must remain `Owned`, preserving the behavior currently hidden in `DoesMemberReturnArrayAsOwnedNativeList`.

- [ ] **Step 5: Implement fixed-point summary resolution**

Enumerate all method, constructor, accessor, operator, and local-function declarations from every compilation. Seed intrinsic and declared contracts, then repeatedly recompute unresolved source-visible return summaries until no summary changes. Use `CPPMethodOwnershipKey` for cross-project calls.

During the same fixed-point pass, summarize source-visible parameters. A parameter is `NoEscape` when every use remains within the call and all downstream argument positions are also `NoEscape`. It is `TakesOwnership` when every ownership-bearing path explicitly releases it or transfers it into a verified owned member. Returning, storing, capturing, or forwarding it to an unresolved boundary leaves the parameter `Unknown`. Validate explicit parameter annotations against the inferred summary.

For each method after convergence:

```csharp
if (ownedReturnCount > 0 && borrowedReturnCount > 0) {
    diagnostics.Add(DiagnosticFactory.Create("CPPOWN005", mixedReturnSyntax, method, message, recommendation));
} else if (ownedReturnCount > 0) {
    summary.SetReturnOwnership(CPPOwnershipKind.Owned);
} else if (borrowedReturnCount > 0) {
    summary.SetReturnOwnership(CPPOwnershipKind.Borrowed);
} else {
    summary.SetReturnOwnership(CPPOwnershipKind.Unknown);
}
```

Validate declared contracts after inference and emit `CPPOWN006` on contradiction.

- [ ] **Step 6: Run the resolver tests**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter FullyQualifiedName~CPPMethodOwnershipSummaryResolverTests --no-restore
```

Expected: PASS.

- [ ] **Step 7: Commit method summary inference**

```powershell
rtk.exe git add cs2.cpp/ownership cs2.cpp.tests/CPPMethodOwnershipSummaryResolverTests.cs
rtk.exe git commit -m "feat: infer native return ownership"
```

---

### Task 6: Analyze local ownership lifecycle and produce emission plans

**Files:**
- Create: `cs2.cpp/ownership/CPPOwnershipTransition.cs`
- Create: `cs2.cpp/ownership/CPPLocalOwnershipPlan.cs`
- Create: `cs2.cpp/ownership/CPPOwnershipEmissionPlan.cs`
- Create: `cs2.cpp/ownership/CPPOwnershipAnalysisResult.cs`
- Create: `cs2.cpp/ownership/CPPLocalOwnershipAnalyzer.cs`
- Create: `cs2.cpp.tests/CPPLocalOwnershipAnalyzerTests.cs`

**Interfaces:**
- Produces: `CPPLocalOwnershipPlan` with the declaration syntax, initial ownership, stable ownership-flag name, and whether a scope guard is required.
- Produces: `CPPOwnershipTransition` with syntax, local declaration, transition kind, and resulting ownership/lifecycle state.
- Produces: `CPPOwnershipEmissionPlan.TryGetLocalPlan(VariableDeclaratorSyntax declaration, out CPPLocalOwnershipPlan plan)`.
- Produces: `CPPOwnershipEmissionPlan.TryGetTransition(SyntaxNode syntax, out CPPOwnershipTransition transition)`.
- Produces: `CPPOwnershipAnalysisResult CPPLocalOwnershipAnalyzer.Analyze(IReadOnlyList<Compilation> compilations, CPPMethodOwnershipSummaryResolution summaries)`.

- [ ] **Step 1: Write failing linear-lifecycle tests**

Cover:

```csharp
List<int> values = new List<int>();              // Acquire, scope cleanup
List<int> built = Build();                       // Owned factory, scope cleanup
List<int> shared = cache;                        // Borrowed, no cleanup
NativeOwnership.Delete(values);                  // Release, guard disarm
Take([NativeTakesOwnership] built);              // Transfer, guard disarm
return created;                                  // Transfer from owned-return method
```

Assert exact plan and transition kinds. Assert use after `Delete`, use after `Take`, and delete of `shared` produce `CPPOWN004`, `CPPOWN004`, and `CPPOWN003` respectively.

- [ ] **Step 2: Write failing unknown-boundary tests**

An owned local passed to an unannotated external parameter must produce `CPPOWN001`; the same local passed to a source-visible helper inferred as `NoEscape` must remain live and receive scope cleanup. Assigning it to an ordinary field must produce `CPPOWN002`; returning an unclassified external pointer must produce `CPPOWN001`.

- [ ] **Step 3: Run the focused analyzer tests and verify missing implementation failures**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter FullyQualifiedName~CPPLocalOwnershipAnalyzerTests --no-restore
```

Expected: compilation failure for the plan and analyzer types.

- [ ] **Step 4: Implement CFG-based state propagation**

Create `ControlFlowGraph` from each source-visible `IMethodBodyOperation`. Track relevant locals by symbol with `SymbolEqualityComparer.Default`. Process declarations, assignments, invocations, returns, and explicit ownership helpers in block order.

The transfer table is exact:

```text
Owned source assigned to local          -> Acquire
Owned source replaces owned local       -> Replace
NativeOwnership.Delete/Release          -> Release
Owned argument to NativeTakesOwnership  -> Transfer
Owned local returned by owned method    -> Transfer
Live owned local at lexical scope exit  -> ScopeCleanup
```

Every local that may become owned receives a stable flag name generated from its sanitized identifier and declaration span: `__owns_<name>_<spanStartHex>`.

- [ ] **Step 5: Implement local safety diagnostics**

Before reading a local, reject `Released` or `Transferred` with `CPPOWN004`. Before explicit deletion, require `Owned/Live` or report `CPPOWN003`. Before passing live ownership to an unknown parameter, report `CPPOWN001`. Before an ordinary escape, report `CPPOWN002`.

- [ ] **Step 6: Run the analyzer tests**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter FullyQualifiedName~CPPLocalOwnershipAnalyzerTests --no-restore
```

Expected: PASS.

- [ ] **Step 7: Commit local lifecycle analysis**

```powershell
rtk.exe git add cs2.cpp/ownership cs2.cpp.tests/CPPLocalOwnershipAnalyzerTests.cs
rtk.exe git commit -m "feat: analyze local native ownership"
```

---

### Task 7: Complete branch, loop, capture, reassignment, and owned-member analysis

**Files:**
- Create: `cs2.cpp/ownership/CPPOwnershipStateMerger.cs`
- Create: `cs2.cpp/ownership/CPPOwnedMemberContractValidator.cs`
- Modify: `cs2.cpp/ownership/CPPLocalOwnershipAnalyzer.cs`
- Modify: `cs2.cpp/ownership/CPPOwnershipAnalysisResult.cs`
- Create: `cs2.cpp.tests/CPPOwnershipControlFlowTests.cs`
- Create: `cs2.cpp.tests/CPPOwnedMemberContractValidatorTests.cs`

**Interfaces:**
- Produces: `CPPOwnershipStateMerger.Merge` for deterministic CFG joins.
- Produces: `IReadOnlyList<CPPConversionDiagnostic> CPPOwnedMemberContractValidator.Validate(IReadOnlyList<Compilation> compilations, CPPOwnershipAnalysisResult analysis)`.
- Extends: local plans with owned reassignment and null-assignment transitions.

- [ ] **Step 1: Write failing branch and loop tests**

Cover these exact outcomes:

- Owned on both branches merges to live owned.
- Released on both branches merges to released.
- Owned on one branch and transferred on the other produces `CPPOWN009` when control rejoins.
- An owned loop-carried value that is replaced only after cleanup is valid.
- A loop overwrite without cleanup produces `CPPOWN008`.
- Null replacement releases the current value and leaves the ownership flag false.
- Early returns leave live values to scope guards.
- Throw edges and `try`/`finally` exits preserve exactly-once scope cleanup.

- [ ] **Step 2: Write failing capture tests**

Capture a live owned local in a lambda, delegate, and local function. Each must produce `CPPOWN002` unless the capture is invoked and destroyed entirely inside a proven no-escape expression; the initial implementation rejects the capture rather than guessing.

- [ ] **Step 3: Write failing owned-member proof tests**

A valid owned member fixture must:

1. Mark the field `[NativeOwnedMember]`.
2. Release or dispose-and-release the previous value before every replacement.
3. Release or dispose-and-release the member on every normal exit from `Dispose()`.

Assert missing replacement cleanup produces `CPPOWN007`, missing `Dispose()` cleanup produces `CPPOWN007`, and annotating a borrowed assignment as owned produces `CPPOWN006`.

- [ ] **Step 4: Run the control-flow and member tests and verify failures**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPOwnershipControlFlowTests|FullyQualifiedName~CPPOwnedMemberContractValidatorTests" --no-restore
```

Expected: FAIL on unimplemented merge and member validation behavior.

- [ ] **Step 5: Implement conservative state merging and reassignment**

`CPPOwnershipStateMerger` returns the identical state when all incoming states match. It permits null/uninitialized plus owned only when the emission plan has a false-initialized guard. All other ownership or lifecycle disagreement returns `Unknown`, and the analyzer emits `CPPOWN009` at the join source.

For replacement, record `Replace` only after proving the right side is owned. A borrowed or unknown replacement of an owned local is a hard error. A null assignment records `Release` without `delete` only when the existing value was already transferred or released; otherwise it must first destroy the owned value.

- [ ] **Step 6: Implement owned-member contract validation**

Use Roslyn symbols, not member names alone. Verify every assignment into `[NativeOwnedMember]`, every prior-value cleanup, and all `Dispose()` normal exits through control-flow state. Reject ordinary fields and properties as ownership sinks. Do not synthesize destructors or cleanup methods.

- [ ] **Step 7: Run the focused tests**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPOwnershipControlFlowTests|FullyQualifiedName~CPPOwnedMemberContractValidatorTests" --no-restore
```

Expected: PASS.

- [ ] **Step 8: Commit complete semantic validation**

```powershell
rtk.exe git add cs2.cpp/ownership cs2.cpp.tests/CPPOwnershipControlFlowTests.cs cs2.cpp.tests/CPPOwnedMemberContractValidatorTests.cs
rtk.exe git commit -m "feat: validate ownership control flow"
```

---

### Task 8: Wire ownership analysis into the C++ conversion pipeline

**Files:**
- Create: `cs2.cpp/ownership/CPPOwnershipAnalysisCoordinator.cs`
- Create: `cs2.cpp/CPPOwnershipAnalysisStage.cs`
- Modify: `cs2.cpp/CPPCodeConverter.cs`
- Modify: `cs2.cpp/CPPResetConversionStateStage.cs`
- Modify: `cs2.cpp.tests/CPPCodeConverterPipelineTests.cs`
- Create: `cs2.cpp.tests/CPPOwnershipAnalysisStageTests.cs`

**Interfaces:**
- Produces: `CPPOwnershipAnalysisResult CPPOwnershipAnalysisCoordinator.Analyze(IReadOnlyList<Compilation> compilations)`.
- Produces: `CPPCodeConverter.OwnershipAnalysisResult` as internal read-only run state.
- Produces: `CPPCodeConverter.SetOwnershipAnalysisResult(CPPOwnershipAnalysisResult result)` requiring a non-null, error-free result.
- Inserts: `CPPOwnershipAnalysisStage` after `DocumentPreprocessingStage` and before `ClassProcessingStage`.

- [ ] **Step 1: Update the pipeline-order test first**

Expected stage order becomes:

```csharp
Assert.Equal([
    "CPPResetConversionStateStage",
    "ApplyPreprocessorSymbolsStage",
    "CPPPreprocessorFilterStage",
    "CPPAssemblyMetadataStage",
    "DocumentPreprocessingStage",
    "CPPOwnershipAnalysisStage",
    "ClassProcessingStage",
    "ProgramSortingStage"
], stageNames);
```

- [ ] **Step 2: Write failing hard-error stage tests**

Create one project with an unannotated external pointer factory. Assert `converter.AddCsproj(projectPath)` throws, `converter.Report` contains `CPPOWN001`, and `converter.Program` has not entered class processing. Add a valid owned-factory project and assert the analysis result is stored.

- [ ] **Step 3: Run the stage tests and verify failures**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPCodeConverterPipelineTests|FullyQualifiedName~CPPOwnershipAnalysisStageTests" --no-restore
```

Expected: FAIL because the stage is absent.

- [ ] **Step 4: Implement compilation collection and coordinator execution**

The stage walks the active project and its transitive project references once, obtains each Roslyn `Compilation`, invokes the coordinator, appends all diagnostics to `Owner.Report`, and throws `InvalidOperationException` when `HasErrors` is true. The exception message starts with the first diagnostic code and source location.

Do not emit or write generated files from this stage.

- [ ] **Step 5: Store and reset analysis run state**

`CPPResetConversionStateStage` clears `OwnershipAnalysisResult`. `CPPCodeConverter.SetOwnershipAnalysisResult` rejects null or error-bearing results. `CPPConversiorProcessor` will consume this state in Task 9.

- [ ] **Step 6: Run the pipeline tests**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPCodeConverterPipelineTests|FullyQualifiedName~CPPOwnershipAnalysisStageTests" --no-restore
```

Expected: PASS.

- [ ] **Step 7: Commit pipeline integration**

```powershell
rtk.exe git add cs2.cpp/ownership/CPPOwnershipAnalysisCoordinator.cs cs2.cpp/CPPOwnershipAnalysisStage.cs cs2.cpp/CPPCodeConverter.cs cs2.cpp/CPPResetConversionStateStage.cs cs2.cpp.tests/CPPCodeConverterPipelineTests.cs cs2.cpp.tests/CPPOwnershipAnalysisStageTests.cs
rtk.exe git commit -m "feat: gate cpp lowering on ownership analysis"
```

---

### Task 9: Emit cleanup, release, transfer, and reassignment from semantic plans

**Files:**
- Modify: `cs2.cpp/CPPConversiorProcessor.cs`
- Create: `cs2.cpp.tests/CPPOwnershipEmissionTests.cs`
- Modify: `cs2.cpp.tests/CPPCompileValidationRegressionTests.cs`

**Interfaces:**
- Consumes: `CPPCodeConverter.OwnershipAnalysisResult`.
- Replaces: `TryProcessNonEscapingManagedLocalDeclarationStatement` and all ownership decisions made by `ShouldDeleteManagedLocalAtScopeExit`.
- Produces: plan-driven declaration guards, ownership-flag disarms, explicit releases, return transfers, and safe reassignment.

- [ ] **Step 1: Write failing declaration and factory-return emission tests**

For a local initialized by `Build()` where `Build()` returns a fresh `List<int>`, assert generated C++ contains:

```cpp
List<int32_t> *values = Widget::Build();
bool __owns_values_... = true;
auto __localDeleteGuard_... = he_cpp_make_scope_exit([&]() {
    if (__owns_values_...) {
        delete values;
    }
});
```

Also assert the factory itself does not delete the list before returning it.

- [ ] **Step 2: Write failing transfer and explicit-release emission tests**

Assert:

- Returning an owned local sets its ownership flag to false before `return`.
- Passing it to `[NativeTakesOwnership]` sets the flag false before the call.
- `NativeOwnership.Delete` and `Release` delete exactly once and set the flag false.
- A borrowed local emits no ownership flag and no delete guard.
- Owned reassignment evaluates the new allocation first, deletes the old value, assigns the new value, and leaves the flag true.
- Null replacement deletes the old value, stores `nullptr`, and sets the flag false.
- A declaration containing multiple owned declarators emits one independent flag and guard per local.

- [ ] **Step 3: Run emission tests and confirm they fail against syntax heuristics**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter FullyQualifiedName~CPPOwnershipEmissionTests --no-restore
```

Expected: FAIL because factory-return locals do not yet receive guards or disarm transitions.

- [ ] **Step 4: Replace declaration heuristics with emission-plan lookup**

Rename the declaration path to `TryProcessOwnedManagedLocalDeclarationStatement`. Process the declaration once, then emit one false/true ownership flag and one scope guard for every `CPPLocalOwnershipPlan` attached to its declarators.

The guard body is exactly:

```csharp
lines.Add($"bool {plan.OwnershipFlagName} = {initialValue};\n");
lines.Add($"auto {guardName} = he_cpp_make_scope_exit([&]() {{\n");
lines.Add($"if ({plan.OwnershipFlagName}) {{\n");
lines.Add("delete ");
lines.Add(plan.LocalName);
lines.Add(";\n}\n});\n");
```

- [ ] **Step 5: Emit semantic transitions at their exact syntax sites**

Intercept ownership-aware return statements, invocation arguments, explicit `NativeOwnership` statements, and assignments. Emit transfer disarms before ownership enters the callee or return value. Emit explicit-release disarms immediately after native deletion. Use the transition stored for that syntax node; do not repeat escape analysis in the emitter.

- [ ] **Step 6: Remove the old ownership matcher**

Delete the ownership decision paths centered on:

- `ShouldDeleteManagedLocalAtScopeExit`
- `IsOwnedManagedLocalInitializerExpression`
- `DoesLocalEscapeScope`
- `HasExplicitNativeOwnershipRelease`
- `DoesMemberReturnArrayAsOwnedNativeList`

Move the array-to-list ownership rule into the semantic classifier before removing the old method. Keep unrelated managed allocation and lowering helpers still used outside ownership decisions.

- [ ] **Step 7: Update old regression expectations**

Change the source-visible unannotated-helper test from “does not emit a guard” to expecting inferred `NoEscape` and a caller scope guard. Change the constructor escape fixture to expect `CPPOWN002` unless it provides a complete `[NativeOwnedMember]` replacement-and-`Dispose()` proof. Preserve direct local, array, explicit-release, and array-backed read-only-list coverage under the new semantic path.

- [ ] **Step 8: Run focused emission and legacy-regression tests**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPOwnershipEmissionTests|FullyQualifiedName~CPPCompileValidationRegressionTests.WriteOutput_WithNonEscapingManaged|FullyQualifiedName~CPPCompileValidationRegressionTests.WriteOutput_WithEscapingManaged|FullyQualifiedName~CPPCompileValidationRegressionTests.WriteOutput_WithExplicitNativeOwnership|FullyQualifiedName~CPPCompileValidationRegressionTests.WriteOutput_WithArrayBackedReadOnlyList" --no-restore
```

Expected: PASS.

- [ ] **Step 9: Commit semantic ownership emission**

```powershell
rtk.exe git add cs2.cpp/CPPConversiorProcessor.cs cs2.cpp.tests/CPPOwnershipEmissionTests.cs cs2.cpp.tests/CPPCompileValidationRegressionTests.cs
rtk.exe git commit -m "feat: emit semantic native ownership cleanup"
```

---

### Task 10: Lock the text-effect leak regression and generated C++ compilation

**Files:**
- Create: `cs2.cpp.tests/TestHelpers/CPPOwnershipConversionTestWorkspace.cs`
- Create: `cs2.cpp.tests/TestHelpers/CPPOwnershipConversionOutput.cs`
- Create: `cs2.cpp.tests/CPPTextRenderEffectOwnershipRegressionTests.cs`
- Create: `cs2.cpp.tests/CPPOwnershipGeneratedCompilationTests.cs`

**Interfaces:**
- Produces: a test conversion workspace rooted at `scratch/ownership-tests/<test-id>`.
- Verifies: `TextRenderEffectPassBuilder.Build` is owned-return and its conditional local is deleted by `EmitText`.
- Verifies: representative generated ownership C++ compiles through the existing compile harness.

- [ ] **Step 1: Add a workspace-owned conversion test helper**

The helper derives repository root from `AppContext.BaseDirectory`, creates fixtures under `scratch/ownership-tests`, references `cs2.attributes`, runs `CPPCodeConverter.AddCsproj` and `WriteOutput`, and returns the generated output path, text, report, and converter. It never calls `Path.GetTempPath()`.

- [ ] **Step 2: Write the exact text-effect regression test**

Use a minimal source fixture preserving the real ownership shape:

```csharp
static List<TextRenderEffectPass> Build(ITextDrawable2D drawable) {
    List<TextRenderEffectPass> passes = new List<TextRenderEffectPass>(6);
    passes.Add(new TextRenderEffectPass());
    return passes;
}

void EmitText(ITextDrawable2D text) {
    bool hasTextEffects = text.Enabled;
    List<TextRenderEffectPass> effectPasses = hasTextEffects
        ? TextRenderEffectPassBuilder.Build(text)
        : null;

    if (hasTextEffects) {
        Use(effectPasses.Count);
    }
}
```

Assert `TextRenderEffectPassBuilder.cpp` transfers without deletion and `RenderCommandListBuilder2D.cpp` contains one guarded `delete effectPasses;`.

- [ ] **Step 3: Write generated compile cases**

Compile generated output for direct cleanup, owned factory cleanup, return transfer, takes-ownership transfer, explicit release, reassignment, and branch cleanup. Assert the compile harness exits successfully and generated source contains no duplicate delete for any local.

- [ ] **Step 4: Run the regression and compile tests**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --filter "FullyQualifiedName~CPPTextRenderEffectOwnershipRegressionTests|FullyQualifiedName~CPPOwnershipGeneratedCompilationTests" --no-restore
```

Expected: PASS.

- [ ] **Step 5: Run the complete C++ generator test project**

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --no-restore
```

Expected: all tests pass. Any old test relying on ambiguous ownership must be converted to an explicit valid contract or changed to assert the correct `CPPOWN` hard error; do not weaken analysis.

- [ ] **Step 6: Commit end-to-end ownership coverage**

```powershell
rtk.exe git add cs2.cpp.tests/TestHelpers/CPPOwnershipConversionTestWorkspace.cs cs2.cpp.tests/TestHelpers/CPPOwnershipConversionOutput.cs cs2.cpp.tests/CPPTextRenderEffectOwnershipRegressionTests.cs cs2.cpp.tests/CPPOwnershipGeneratedCompilationTests.cs
rtk.exe git commit -m "test: lock native ownership regressions"
```

---

### Task 11: Validate and burn down full-engine ownership errors

**Files:**
- Inspect: `C:/dev/helworks/helengine/engine/**/*.cs`
- Inspect generated output: `C:/dev/helworks/builds/demodisc/ps2/ownership-validation`
- Modify only source files directly named by `CPPOWN001` through `CPPOWN009` diagnostics, if the full conversion exposes real ambiguous boundaries.

**Interfaces:**
- Consumes: the completed hard-error ownership analyzer.
- Produces: a full engine conversion with zero ownership diagnostics.
- Verifies: generated `RenderCommandListBuilder2D.cpp` owns and deletes `effectPasses`.

- [ ] **Step 1: Run the full engine C++ conversion into a visible build directory**

Run the real PS2 platform build entry point, which performs the full engine conversion before native compilation:

```powershell
rtk.exe proxy powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine\scripts\build-platform.ps1 -Project C:\dev\helprojs\demodisc\project.heproj -Platform ps2 -Output C:\dev\helworks\builds\demodisc\ps2\ownership-validation
```

Retain the conversion report and build log under the same build root. Expected first result: either a completed PS2 build or a deterministic list of `CPPOWN001` through `CPPOWN009` errors with file, line, and column before native compilation.

- [ ] **Step 2: Resolve diagnostics according to their proven API contract**

Apply only these corrections:

- Fresh allocation returned to caller: allow inference or add `[NativeOwnedReturn]` only at a non-analyzable boundary.
- Cache, singleton, parameter, field, or shared empty value: add `[NativeBorrowedReturn]` only at a non-analyzable boundary.
- Callee retains and cleans an argument: add `[NativeTakesOwnership]` to that parameter and prove member cleanup.
- Callee uses an argument only during the call: use existing `[NativeNoEscape]`.
- Mixed cached/fresh returns: split or redesign the API so one method has one ownership contract.
- Unowned member escape, use-after-transfer, borrowed deletion, or overwrite: fix the source lifecycle; do not annotate around it.

After each source correction, rerun the conversion and require the diagnostic count to decrease without introducing new codes.

- [ ] **Step 3: Verify the known generated caller and factory**

Inspect the generated files with:

```powershell
rtk.exe grep "effectPasses|__owns_effectPasses|delete effectPasses" C:\dev\helworks\builds\demodisc\ps2\ownership-validation -m 80
```

Expected: the builder factory transfers the returned list without deleting it, while `RenderCommandListBuilder2D::EmitText` has exactly one guarded caller cleanup.

- [ ] **Step 4: Run engine tests affected by any source contract edits**

For every modified engine project, run its exact `.csproj` test project with `rtk.exe dotnet test`. At minimum, when the known text path changes or receives metadata, run:

```powershell
rtk.exe dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter FullyQualifiedName~TextRenderEffectPassBuilderTests --no-restore
```

Expected: PASS.

- [ ] **Step 5: Commit only actual engine ownership-contract corrections**

If no engine source files changed, skip this commit. Otherwise, stage only files named by resolved ownership diagnostics and commit in `C:\dev\helworks\helengine`:

```powershell
rtk.exe git commit -m "fix: declare native ownership boundaries"
```

Do not stage unrelated engine changes.

---

### Task 12: Build DemoDisc and prove PS2 heap stability

**Files:**
- Build output: `C:/dev/helworks/builds/demodisc/ps2/ownership-soak`
- Generated source inspection: build-owned generated C++ under that output root
- Runtime log: `C:/dev/helworks/helengine-ps2/tmp/pcsx2-launcher/pcsx2-emulog.txt`
- No source modification is expected in this task.

**Interfaces:**
- Consumes: the local `csharpcodegen`, `helengine`, `helengine-ps2`, and DemoDisc main checkouts.
- Produces: a full DemoDisc PS2 ISO with main menu and all referenced scenes.
- Acceptance signal: four PS2 `memory diag` samples with stable scene/object/texture counts and no continuing per-frame heap growth or `std::bad_alloc`.

- [ ] **Step 1: Build through the deterministic build waiter**

Run from `C:\dev\helworks\helengine-ps2`:

```powershell
rtk.exe dotnet run --project C:\dev\helworks\helengine\tools\build-waiter\helengine.buildwaiter.csproj -- --output C:\dev\helworks\builds\demodisc\ps2\ownership-soak --require game.iso --require disc/SYSTEM.CNF --require disc/HELENGIN.ELF -- powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine\scripts\build-platform.ps1 -Project C:\dev\helprojs\demodisc\project.heproj -Platform ps2 -Output C:\dev\helworks\builds\demodisc\ps2\ownership-soak
```

Expected: the waiter exits successfully only after fresh, non-empty ISO, ELF, and system configuration artifacts exist.

- [ ] **Step 2: Launch the new ISO using the repository launcher**

```powershell
rtk.exe proxy powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine-ps2\scripts\launch_in_emulator.ps1 -ArtifactPath C:\dev\helworks\builds\demodisc\ps2\ownership-soak\game.iso
```

Expected: the launcher closes prior PCSX2 instances, prints the exact artifact timestamp and process ID, and launches this ISO.

- [ ] **Step 3: Collect the deterministic four-sample memory soak**

Monitor `pcsx2-emulog.txt` in commentary-visible intervals no longer than 60 seconds. Stop waiting only after four `memory diag heapUsed=` lines have appeared; the PS2 host emits one initial sample and three additional samples at 60-second intervals.

At each interval run:

```powershell
rtk.exe grep "memory diag heapUsed=|std::bad_alloc|frame exception" C:\dev\helworks\helengine-ps2\tmp\pcsx2-launcher\pcsx2-emulog.txt -m 40
```

Expected: four memory samples, no `std::bad_alloc`, and no frame exception.

- [ ] **Step 4: Evaluate heap plateau**

Record the four `heapUsed`, `deltaHeapUsed`, `scenes`, `ent`, `d2`, `d3`, `rtTex`, and `fontTex` values. Pass only when initialization growth settles and later samples do not repeat the previous roughly two-megabyte growth pattern while counts remain stable.

If heap still grows materially, use the ownership diagnostic and generated caller plan to identify the next allocation origin; do not add PS2-only cleanup.

- [ ] **Step 5: Run final repository verification and report commits**

From `C:\dev\helworks\csharpcodegen`:

```powershell
rtk.exe dotnet test cs2.cpp.tests/cs2.cpp.tests.csproj --no-restore
rtk.exe git status --short
rtk.exe git log --oneline -10
```

Expected: all tests pass, only pre-existing unrelated profiling edits remain unstaged, and the ownership task commits are visible in order.
