using cs2.attributes;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the source-level metadata contracts used to describe native ownership at non-analyzable boundaries.
/// </summary>
public sealed class CPPNativeOwnershipAttributeTests {
    /// <summary>
    /// Ensures owned-return contracts can annotate methods and properties without being inherited or repeated.
    /// </summary>
    [Fact]
    public void NativeOwnedReturnAttribute_TargetsMethodsAndProperties() {
        AttributeUsageAttribute usage = ResolveUsage<NativeOwnedReturnAttribute>();

        Assert.Equal(AttributeTargets.Method | AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    /// <summary>
    /// Ensures borrowed-return contracts can annotate methods and properties without being inherited or repeated.
    /// </summary>
    [Fact]
    public void NativeBorrowedReturnAttribute_TargetsMethodsAndProperties() {
        AttributeUsageAttribute usage = ResolveUsage<NativeBorrowedReturnAttribute>();

        Assert.Equal(AttributeTargets.Method | AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    /// <summary>
    /// Ensures ownership-transfer contracts apply only to method parameters.
    /// </summary>
    [Fact]
    public void NativeTakesOwnershipAttribute_TargetsParameters() {
        AttributeUsageAttribute usage = ResolveUsage<NativeTakesOwnershipAttribute>();

        Assert.Equal(AttributeTargets.Parameter, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    /// <summary>
    /// Ensures owned-member contracts can annotate fields and properties without being inherited or repeated.
    /// </summary>
    [Fact]
    public void NativeOwnedMemberAttribute_TargetsFieldsAndProperties() {
        AttributeUsageAttribute usage = ResolveUsage<NativeOwnedMemberAttribute>();

        Assert.Equal(AttributeTargets.Field | AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    /// <summary>
    /// Resolves the single usage declaration attached to one ownership metadata attribute.
    /// </summary>
    /// <typeparam name="TAttribute">Ownership attribute whose legal declaration targets are under test.</typeparam>
    /// <returns>The attribute usage metadata declared by the ownership contract.</returns>
    static AttributeUsageAttribute ResolveUsage<TAttribute>() where TAttribute : Attribute {
        return typeof(TAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();
    }
}
