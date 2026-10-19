namespace cs2.cpp.tests.TestHelpers;

/// <summary>Serializes actual-engine numeric conversion fixtures sharing the fixed platform scratch output.</summary>
[CollectionDefinition(CPPRealNumericCollection.Name, DisableParallelization = true)]
public sealed class CPPRealNumericCollection {
    /// <summary>Shared workspace ownership prevents one conversion fixture deleting another fixture's generated sources.</summary>
    public const string Name = "Actual engine numeric conversion";
}
