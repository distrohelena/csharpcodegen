using cs2.core;
using cs2.cpp;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies capability-sensitive lowering in the C++ conversion processor and emitter.
/// </summary>
public class CPPRuntimeCapabilityConversionTests {
    /// <summary>
    /// Ensures exception-bearing try statements report an explicit unsupported conversion when exceptions are disabled.
    /// </summary>
    [Fact]
    public void ProcessTryCatch_WhenExceptionsAreDisabled_ReportsDiagnosticAndOmitsCatchSyntax() {
        string source = """
            using System;

            public class Gate {
                public void Run() {
                    try {
                        throw new Exception();
                    } catch (Exception) {
                    }
                }
            }
            """;

        CPPConversionOptions options = CreateOptions(false, true);
        options.CollectDiagnostics = false;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        TestableCPPConversiorProcessor processor = new TestableCPPConversiorProcessor(converter);
        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        SemanticModel semantic = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        TryStatementSyntax statement = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<TryStatementSyntax>().Single();
        List<string> lines = new List<string>();

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => processor.ProcessStatementForTest(semantic, CreateContext("Run"), statement, lines));

        Assert.Contains("exception recovery", error.Message, StringComparison.OrdinalIgnoreCase);
        CPPConversionDiagnostic diagnostic = Assert.Single(converter.Report.Diagnostics);
        Assert.Equal("CPP1001", diagnostic.Code);
    }

    /// <summary>
    /// Ensures a bare rethrow is rejected when no C++ exception state exists.
    /// </summary>
    [Fact]
    public void ProcessRethrow_WhenExceptionsAreDisabled_ReportsCapabilityViolation() {
        string source = """
            using System;

            public class Gate {
                public void Fail() {
                    try {
                        throw new Exception();
                    } catch {
                        throw;
                    }
                }
            }
            """;

        CPPConversionOptions options = CreateOptions(false, true);
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        TestableCPPConversiorProcessor processor = new TestableCPPConversiorProcessor(converter);
        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        SemanticModel semantic = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        ThrowStatementSyntax statement = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<ThrowStatementSyntax>().Single(item => item.Expression == null);
        List<string> lines = new List<string>();

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => processor.ProcessStatementForTest(semantic, CreateContext("Fail"), statement, lines));

        Assert.Contains("rethrow", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("CPP1001", Assert.Single(converter.Report.Diagnostics).Code);
    }

    /// <summary>
    /// Ensures finally-only lowering remains ordinary scope cleanup when exceptions are disabled.
    /// </summary>
    [Fact]
    public void ProcessFinallyOnly_WhenExceptionsAreDisabled_PreservesScopeGuard() {
        string source = """
            public class Gate {
                public void Run() {
                    try {
                    } finally {
                    }
                }
            }
            """;

        CPPConversionOptions options = CreateOptions(false, true);
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        TestableCPPConversiorProcessor processor = new TestableCPPConversiorProcessor(converter);
        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        SemanticModel semantic = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        TryStatementSyntax statement = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<TryStatementSyntax>().Single();
        List<string> lines = new List<string>();

        processor.ProcessStatementForTest(semantic, CreateContext("Run"), statement, lines);

        string output = string.Concat(lines);
        Assert.Contains("he_cpp_make_scope_exit", output, StringComparison.Ordinal);
        Assert.DoesNotContain("try {", output, StringComparison.Ordinal);
        Assert.Empty(converter.Report.Diagnostics);
    }

    /// <summary>
    /// Ensures throw statements use the non-returning runtime failure helper when exceptions are disabled.
    /// </summary>
    [Fact]
    public void ProcessThrow_WhenExceptionsAreDisabled_UsesRuntimeFailureHelper() {
        string source = """
            using System;

            public class Gate {
                public void Fail() {
                    throw new Exception();
                }
            }
            """;

        CPPConversionOptions options = CreateOptions(false, true);
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        TestableCPPConversiorProcessor processor = new TestableCPPConversiorProcessor(converter);
        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        SemanticModel semantic = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        ThrowStatementSyntax statement = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<ThrowStatementSyntax>().Single();
        List<string> lines = new List<string>();

        processor.ProcessStatementForTest(semantic, CreateContext("Fail"), statement, lines);

        string output = string.Concat(lines);
        Assert.Contains("he_cpp_raise(", output, StringComparison.Ordinal);
        Assert.DoesNotContain("throw ", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures RTTI-dependent generic dispatch is diagnosed and does not emit compiler RTTI syntax.
    /// </summary>
    [Fact]
    public void ProcessSwitchPattern_WhenRttiIsDisabled_ReportsDiagnosticWithoutDynamicCast() {
        string source = """
            public class Gate {
                public bool IsText(object value) {
                    return value switch {
                        string _ => true,
                        _ => false
                    };
                }
            }
            """;

        CPPConversionOptions options = CreateOptions(true, false);
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        TestableCPPConversiorProcessor processor = new TestableCPPConversiorProcessor(converter);
        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        SemanticModel semantic = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        SwitchExpressionSyntax expression = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<SwitchExpressionSyntax>().Single();
        List<string> lines = new List<string>();

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => processor.ProcessExpressionForTest(semantic, CreateContext("IsText"), expression, lines));

        Assert.Contains("RTTI", error.Message, StringComparison.OrdinalIgnoreCase);
        CPPConversionDiagnostic diagnostic = Assert.Single(converter.Report.Diagnostics);
        Assert.Equal("CPP1001", diagnostic.Code);
    }

    /// <summary>
    /// Ensures generated class output uses the provider string alias when standard string storage is disabled.
    /// </summary>
    [Fact]
    public void WriteOutput_WithProviderStringOption_UsesHeCppStringInGeneratedClass() {
        string rootPath = Environment.GetEnvironmentVariable("CS2_CPP_CAPABILITY_ARTIFACT_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "cs2cpp-runtime-capability-tests", Guid.NewGuid().ToString("N"));
        rootPath = Path.GetFullPath(rootPath);
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string sourcePath = Path.Combine(rootPath, "Fixture.cs");
        string outputPath = Path.Combine(rootPath, "out");
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(sourcePath, """
            public class GateHolder {
                public StringGate StringGate;
                public void Set(StringGate value) { StringGate = value ?? throw new System.ArgumentNullException(nameof(value)); }
                public double Modulo(double left, double right) { return left % right; }
            }
            public class StringGate {
                public System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<string, StringGate>> Nested = new();
                public string Name;

                public StringGate(string name) {
                    Name = name;
                }

                public string Join(string suffix) {
                    return Name + suffix;
                }

                public string Format(int value) {
                    return $"{Name}:{value}";
                }

                public int Classify(string value) {
                    return value switch {
                        "x" => 1,
                        _ => 0
                    };
                }
            }
            """);

        CPPConversionOptions options = CreateOptions(false, true);
        options.WriteConversionReport = true;
        Dictionary<string, string> platformOptions = new Dictionary<string, string>(options.PlatformOptionValues, StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.RuntimeProviderHeader] = "platform/runtime.hpp",
            [CPPCodegenOptionNames.UseStdString] = "false",
            [CPPCodegenOptionNames.UseStdMath] = "false",
            [CPPCodegenOptionNames.RuntimeMathHeader] = "fixture_math.hpp"
        };
        options.PlatformOptionValues = platformOptions;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);

        string generatedClassText = string.Join("\n", Directory.GetFiles(outputPath, "StringGate.*", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        string holderText = File.ReadAllText(Path.Combine(outputPath, "GateHolder.cpp"));
        Assert.Contains("he_cpp_raise_value<::StringGate*>", holderText);
        Assert.Contains("he_cpp_math_detail::Remainder(", holderText);
        string configText = File.ReadAllText(Path.Combine(outputPath, CPPGeneratedConfigWriter.DefaultFileName));
        Assert.Contains("HeCppString", generatedClassText, StringComparison.Ordinal);
        Assert.DoesNotContain("std::string", generatedClassText, StringComparison.Ordinal);
        Assert.Contains("#define HE_CPP_RUNTIME_PROVIDER_HEADER \"platform/runtime.hpp\"", configText, StringComparison.Ordinal);
        Assert.Contains("#define HE_CPP_USE_STD_STRING 0", configText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures single-character string splitting lowers to the native helper and names the defaulted option member.
    /// </summary>
    [Fact]
    public void WriteOutput_WithSingleCharacterSplit_LowersToNativeSplitWithNamedDefaultOption() {
        string rootPath = Environment.GetEnvironmentVariable("CS2_CPP_CAPABILITY_ARTIFACT_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "cs2cpp-runtime-capability-tests", Guid.NewGuid().ToString("N"));
        rootPath = Path.GetFullPath(Path.Combine(rootPath, "single-character-split"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string sourcePath = Path.Combine(rootPath, "Fixture.cs");
        string outputPath = Path.Combine(rootPath, "out");
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(sourcePath, """
            public class SplitGate {
                public string[] Lines(string content) {
                    return content.Split('\n');
                }

                public string[] Words(string content) {
                    return content.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                }
            }
            """);

        CPPConversionOptions options = CreateOptions(false, true);
        Dictionary<string, string> platformOptions = new Dictionary<string, string>(options.PlatformOptionValues, StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.RuntimeProviderHeader] = "platform/runtime.hpp",
            [CPPCodegenOptionNames.UseStdString] = "false"
        };
        options.PlatformOptionValues = platformOptions;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);

        string gateText = File.ReadAllText(Path.Combine(outputPath, "SplitGate.cpp"));
        Assert.Matches(@"String::Split\(content, (static_cast<char>\()?'\\n'\)?, StringSplitOptions::None\)", gateText);
        Assert.Matches(@"String::Split\(content, (static_cast<char>\()?' '\)?, StringSplitOptions::RemoveEmptyEntries\)", gateText);
        Assert.DoesNotContain("StringSplitOptions::0", gateText, StringComparison.Ordinal);
        Assert.DoesNotContain("content.Split(", gateText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures conversion errors stop artifact emission even when fail-fast mode is disabled.
    /// </summary>
    [Fact]
    public void WriteOutput_WithUnsupportedConstructAndFailFastDisabled_ThrowsAfterReportingError() {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-runtime-capability-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string sourcePath = Path.Combine(rootPath, "Fixture.cs");
        string outputPath = Path.Combine(rootPath, "out");
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(sourcePath, """
            using System;

            public class UnsupportedGate {
                public void Run() {
                    try {
                        throw new Exception();
                    } catch (Exception) {
                    }
                }
            }
            """);

        CPPConversionOptions options = CreateOptions(false, true);
        options.FailOnError = false;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);

        Assert.Throws<NotSupportedException>(() => converter.WriteOutput(outputPath));
        Assert.True(converter.Report.HasErrors);
    }

    /// <summary>
    /// Creates options with explicit exception and RTTI capabilities for focused lowering tests.
    /// </summary>
    /// <param name="useExceptions">Whether generated C++ may use exception unwinding.</param>
    /// <param name="useRtti">Whether generated C++ may use compiler RTTI.</param>
    /// <returns>A focused option set that avoids native metadata probing.</returns>
    static CPPConversionOptions CreateOptions(bool useExceptions, bool useRtti) {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.UseExceptions] = useExceptions.ToString(),
            [CPPCodegenOptionNames.UseRtti] = useRtti.ToString()
        };
        return options;
    }

    /// <summary>
    /// Creates a focused project file for converter output tests.
    /// </summary>
    /// <returns>A minimal SDK-style project file.</returns>
    static string CreateProjectFile() {
        return """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """;
    }

    /// <summary>
    /// Creates the active class and function context required by direct processor tests.
    /// </summary>
    /// <param name="functionName">Function name placed on the active conversion frame.</param>
    /// <returns>A C++ layer context for the supplied function.</returns>
    static CPPLayerContext CreateContext(string functionName) {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        CPPLayerContext context = new CPPLayerContext(program);
        context.AddClass(new ConversionClass { Name = "Gate" });
        context.AddFunction(new FunctionStack(new ConversionFunction { Name = functionName }));
        return context;
    }

    /// <summary>
    /// Exposes protected processor operations for capability-focused tests.
    /// </summary>
    sealed class TestableCPPConversiorProcessor : CPPConversiorProcessor {
        /// <summary>
        /// Initializes the test processor.
        /// </summary>
        /// <param name="converter">Converter receiving diagnostics.</param>
        public TestableCPPConversiorProcessor(CPPCodeConverter converter)
            : base(converter) {
        }

        /// <summary>
        /// Processes one statement for assertions.
        /// </summary>
        /// <param name="semanticModel">Semantic model for the source.</param>
        /// <param name="context">Active C++ lowering context.</param>
        /// <param name="statement">Statement to lower.</param>
        /// <param name="lines">Output token buffer.</param>
        /// <returns>The processor result.</returns>
        public ExpressionResult ProcessStatementForTest(SemanticModel semanticModel, CPPLayerContext context, StatementSyntax statement, List<string> lines) {
            return ProcessStatement(semanticModel, context, statement, lines);
        }

        /// <summary>
        /// Processes one expression for assertions.
        /// </summary>
        /// <param name="semanticModel">Semantic model for the source.</param>
        /// <param name="context">Active C++ lowering context.</param>
        /// <param name="expression">Expression to lower.</param>
        /// <param name="lines">Output token buffer.</param>
        /// <returns>The processor result.</returns>
        public ExpressionResult ProcessExpressionForTest(SemanticModel semanticModel, CPPLayerContext context, ExpressionSyntax expression, List<string> lines) {
            return ProcessExpression(semanticModel, context, expression, lines);
        }
    }
}



