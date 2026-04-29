using cs2.core;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that unsupported syntax encountered by the C++ processor is reported through converter diagnostics.
/// </summary>
public class CPPConversiorProcessorDiagnosticsTests {
    /// <summary>
    /// Ensures unsupported expressions emit a structured diagnostic with the current type and member context.
    /// </summary>
    [Fact]
    public void ProcessExpression_WithStackAlloc_ReportsUnsupportedConstruct() {
        string source = """
            public class BufferBuilder {
                public int[] Create() {
                    return stackalloc int[4];
                }
            }
            """;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), CreateTestOptions());
        CPPConversiorProcessor processor = new CPPConversiorProcessor(converter);
        var compilation = RoslynTestHelper.CreateCompilation(source, filePath: "BufferBuilder.cs");
        SemanticModel semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        StackAllocArrayCreationExpressionSyntax expression = compilation.SyntaxTrees.Single()
            .GetRoot()
            .DescendantNodes()
            .OfType<StackAllocArrayCreationExpressionSyntax>()
            .Single();
        CPPLayerContext context = CreateContext("Create");
        List<string> lines = new List<string>();

        ExpressionResult result = processor.ProcessExpression(semanticModel, context, expression, lines);

        Assert.False(result.Processed);
        CPPConversionDiagnostic diagnostic = Assert.Single(converter.Report.Diagnostics);
        Assert.Equal("BufferBuilder", diagnostic.SourceTypeName);
        Assert.Equal("Create", diagnostic.SourceMemberName);
        Assert.Equal(nameof(SyntaxKind.StackAllocArrayCreationExpression), diagnostic.SyntaxKind);
        Assert.Contains("does not yet support expression syntax", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures stackalloc-backed span locals lower to native buffers without emitting unsupported diagnostics.
    /// </summary>
    [Fact]
    public void ProcessDeclaration_WithStackAllocSpan_DoesNotReportUnsupportedConstruct() {
        string source = """
            using System;

            public class BufferBuilder {
                public byte ReadByte() {
                    Span<byte> buffer = stackalloc byte[sizeof(int)];
                    return buffer[0];
                }
            }
            """;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), CreateTestOptions());
        TestableCPPConversiorProcessor processor = new TestableCPPConversiorProcessor(converter);
        var compilation = RoslynTestHelper.CreateCompilation(source, filePath: "BufferBuilder.cs");
        SemanticModel semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        VariableDeclarationSyntax declaration = compilation.SyntaxTrees.Single()
            .GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclarationSyntax>()
            .Single();
        CPPLayerContext context = CreateContext("ReadByte");
        List<string> lines = new List<string>();

        processor.ProcessDeclarationForTest(semanticModel, context, declaration, lines);

        string output = string.Concat(lines);
        Assert.Empty(converter.Report.Diagnostics);
        Assert.Contains("uint8_t buffer[sizeof(int32_t)]", output, StringComparison.Ordinal);
        Assert.DoesNotContain("const ", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures unsupported statements emit a structured diagnostic with the current type and member context.
    /// </summary>
    [Fact]
    public void ProcessStatement_WithUnsafeStatement_ReportsUnsupportedConstruct() {
        string source = """
            public class BufferBuilder {
                public void Fill() {
                    unsafe {
                    }
                }
            }
            """;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), CreateTestOptions());
        TestableCPPConversiorProcessor processor = new TestableCPPConversiorProcessor(converter);
        var compilation = RoslynTestHelper.CreateCompilation(source, allowUnsafe: true, filePath: "BufferBuilder.cs");
        SemanticModel semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        UnsafeStatementSyntax statement = compilation.SyntaxTrees.Single()
            .GetRoot()
            .DescendantNodes()
            .OfType<UnsafeStatementSyntax>()
            .Single();
        CPPLayerContext context = CreateContext("Fill");
        List<string> lines = new List<string>();

        ExpressionResult result = processor.ProcessStatementForTest(semanticModel, context, statement, lines);

        Assert.False(result.Processed);
        CPPConversionDiagnostic diagnostic = Assert.Single(converter.Report.Diagnostics);
        Assert.Equal("BufferBuilder", diagnostic.SourceTypeName);
        Assert.Equal("Fill", diagnostic.SourceMemberName);
        Assert.Equal(nameof(SyntaxKind.UnsafeStatement), diagnostic.SyntaxKind);
        Assert.Contains("does not yet support statement syntax", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures supported object creation does not emit a false unsupported-expression diagnostic.
    /// </summary>
    [Fact]
    public void ProcessExpression_WithObjectCreation_DoesNotReportUnsupportedConstruct() {
        string source = """
            public class Widget {
            }

            public class BufferBuilder {
                public Widget Create() {
                    return new Widget();
                }
            }
            """;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), CreateTestOptions());
        CPPConversiorProcessor processor = new CPPConversiorProcessor(converter);
        var compilation = RoslynTestHelper.CreateCompilation(source, filePath: "BufferBuilder.cs");
        SemanticModel semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        ObjectCreationExpressionSyntax expression = compilation.SyntaxTrees.Single()
            .GetRoot()
            .DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Single();
        CPPLayerContext context = CreateContext("Create");
        List<string> lines = new List<string>();

        ExpressionResult result = processor.ProcessExpression(semanticModel, context, expression, lines);

        Assert.True(result.Processed);
        Assert.Empty(converter.Report.Diagnostics);
    }

    /// <summary>
    /// Ensures lowered block, local declaration, and return statements do not emit wrapper diagnostics.
    /// </summary>
    [Fact]
    public void ProcessStatement_WithSupportedBlockStatements_DoesNotReportUnsupportedConstruct() {
        string source = """
            public class Widget {
            }

            public class BufferBuilder {
                public Widget Create() {
                    Widget instance = new Widget();
                    return instance;
                }
            }
            """;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), CreateTestOptions());
        TestableCPPConversiorProcessor processor = new TestableCPPConversiorProcessor(converter);
        var compilation = RoslynTestHelper.CreateCompilation(source, filePath: "BufferBuilder.cs");
        SemanticModel semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        BlockSyntax statement = compilation.SyntaxTrees.Single()
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "Create")
            .Body!;
        CPPLayerContext context = CreateContext("Create");
        List<string> lines = new List<string>();

        ExpressionResult result = processor.ProcessStatementForTest(semanticModel, context, statement, lines);

        Assert.True(result.Processed);
        Assert.Empty(converter.Report.Diagnostics);
    }

    /// <summary>
    /// Creates the current class and function context used for processor diagnostic tests.
    /// </summary>
    /// <returns>A layer context with the active class and function frames pushed.</returns>
    static CPPLayerContext CreateContext(string functionName) {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        CPPLayerContext context = new CPPLayerContext(program);
        ConversionClass conversionClass = new ConversionClass {
            Name = "BufferBuilder"
        };
        ConversionFunction conversionFunction = new ConversionFunction {
            Name = functionName
        };

        context.AddClass(conversionClass);
        context.AddFunction(new FunctionStack(conversionFunction));
        return context;
    }

    /// <summary>
    /// Creates focused converter options that avoid external runtime metadata tooling during unit tests.
    /// </summary>
    /// <returns>The option set used by the processor diagnostic tests.</returns>
    static CPPConversionOptions CreateTestOptions() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        return options;
    }

    /// <summary>
    /// Exposes protected statement processing for focused diagnostics tests.
    /// </summary>
    sealed class TestableCPPConversiorProcessor : CPPConversiorProcessor {
        /// <summary>
        /// Initializes the test processor wrapper.
        /// </summary>
        /// <param name="converter">Converter that receives diagnostics recorded by the processor.</param>
        public TestableCPPConversiorProcessor(CPPCodeConverter converter)
            : base(converter) {
        }

        /// <summary>
        /// Processes a single statement and returns the resulting lowering state for assertions.
        /// </summary>
        /// <param name="semanticModel">Semantic model associated with the statement.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="statement">Statement to process.</param>
        /// <param name="lines">Output line buffer.</param>
        /// <returns>The result returned by the base statement processor.</returns>
        public ExpressionResult ProcessStatementForTest(SemanticModel semanticModel, CPPLayerContext context, StatementSyntax statement, List<string> lines) {
            return ProcessStatement(semanticModel, context, statement, lines);
        }

        /// <summary>
        /// Processes a variable declaration and appends the lowered C++ tokens to the supplied buffer.
        /// </summary>
        /// <param name="semanticModel">Semantic model associated with the declaration.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="declaration">Declaration to lower.</param>
        /// <param name="lines">Output line buffer.</param>
        public void ProcessDeclarationForTest(SemanticModel semanticModel, CPPLayerContext context, VariableDeclarationSyntax declaration, List<string> lines) {
            ProcessDeclaration(semanticModel, context, declaration, lines);
        }
    }
}
