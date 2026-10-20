using codegen;

namespace cs2.cpp.tests;

/// <summary>Verifies native numeric selection and generated contract metadata independently of primitive types.</summary>
public sealed class CPPRealNumericPolicyTests {
    /// <summary>A conversion without overrides resolves to the documented numeric default.</summary>
    [Fact]
    public void DefaultUses32By32() {
        CPPRealNumericPolicy policy = CPPNumericPolicyResolver.Resolve(CPPConversionOptions.CreateDefault());
        Assert.Equal(32, policy.StorageBits);
        Assert.Equal(32, policy.ArithmeticBits);
        Assert.Equal("real-v1-s32-a32", policy.Identity);
    }

    /// <summary>Generic CLI options resolve a build-local policy without mutating another conversion.</summary>
    [Fact]
    public void ExplicitOptionsRemainIndependent() {
        CPPConversionOptions wide = CPPConversionOptions.CreateDefault();
        wide.PlatformOptionValues = new Dictionary<string, string> { ["real-storage-bits"] = "64", ["real-arithmetic-bits"] = "64" };
        Assert.Equal("real-v1-s64-a64", CPPNumericPolicyResolver.Resolve(wide).Identity);
        Assert.Equal("real-v1-s32-a32", CPPNumericPolicyResolver.Resolve(CPPConversionOptions.CreateDefault()).Identity);
    }

    /// <summary>Invalid widths and unsupported half arithmetic cannot silently become another policy.</summary>
    [Theory]
    [InlineData("8")] [InlineData("24")] [InlineData("128")] [InlineData("")] [InlineData("float")]
    public void MalformedNumericOptionFails(string value) {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string> { ["real-storage-bits"] = value };
        Assert.Throws<ArgumentException>(() => CPPNumericPolicyResolver.Resolve(options));
    }

    /// <summary>Unsupported mixed half policies fail instead of silently selecting a wider evaluator.</summary>
    [Fact]
    public void UnsupportedHalfCombinationFailsExplicitly() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string> { ["real-storage-bits"] = "16", ["real-arithmetic-bits"] = "64" };
        Assert.Throws<NotSupportedException>(() => CPPNumericPolicyResolver.Resolve(options));
    }

    /// <summary>Generated configuration records both widths and the representation identity.</summary>
    [Fact]
    public void ConfigIdentityIncludesBothWidths() {
        string output = @"C:\temp\numeric-precision\windows\policy-fixture";
        Directory.CreateDirectory(output);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string> { ["real-storage-bits"] = "32", ["real-arithmetic-bits"] = "64" };
        string path = CPPGeneratedConfigWriter.Write(output, options,
            new CPPRuntimeRequirementRegistrar(new CPPRuntimeRequirementCatalog(), new CPPConversionReport()));
        string config = File.ReadAllText(path);
        Assert.Contains("#define HE_CPP_REAL_STORAGE_BITS 32", config);
        Assert.Contains("#define HE_CPP_REAL_ARITHMETIC_BITS 64", config);
        Assert.Contains("#define HE_CPP_REAL_REPRESENTATION_VERSION 1", config);
        Assert.Contains("real-v1-s32-a64", config);
    }
}
