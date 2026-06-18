using System.Text.Json;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Covers end-to-end feature-pruned output generation for the C++ backend.
/// </summary>
public class CPPFeaturePruningEndToEndTests {
    /// <summary>
    /// Verifies that force-disabling shaders removes shader-tagged generated files from output.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenShadersAreDisabled_DoesNotEmitShaderTypes() {
        string source = """
namespace helengine {
    public class ShaderAsset {
    }
}

namespace helengine.core.scene {
    public class SceneNode {
    }
}
""";

        string outputPath = RunConversion(
            source,
            CPPBuildFeatureProfile.CreateDefault().WithMode("shaders", CPPFeatureMode.Disabled));

        Assert.False(File.Exists(Path.Combine(outputPath, "ShaderAsset.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "ShaderAsset.cpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "SceneNode.hpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "SceneNode.cpp")));
    }

    /// <summary>
    /// Verifies that force-disabling the debug overlay removes both overlay output and its feature-owned runtime helper.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenDebugOverlayIsDisabled_DoesNotEmitOverlayRuntimeHelpers() {
        string source = """
using System.Text;

namespace helengine {
    public class DebugOverlayComponent {
        public string BuildText() {
            StringBuilder builder = new StringBuilder();
            builder.Append("debug");
            return builder.ToString();
        }
    }
}

namespace helengine.core.scene {
    public class SceneNode {
    }
}
""";

        string outputPath = RunConversion(
            source,
            CPPBuildFeatureProfile.CreateDefault().WithMode("debug_overlay", CPPFeatureMode.Disabled));

        Assert.False(File.Exists(Path.Combine(outputPath, "DebugOverlayComponent.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "DebugOverlayComponent.cpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "system", "text", "string-builder.hpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "SceneNode.hpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "SceneNode.cpp")));
    }

    /// <summary>
    /// Verifies that strict presets fail conversion before output is copied when forbidden runtime JSON systems are reachable.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenPresetForbidsRuntimeJson_FailsBeforeCopyingOutput() {
        string source = """
namespace helengine.core.content {
    public class RuntimeManifestJsonReader {
    }
}

namespace helengine.core.runtime {
    public class RuntimeBootstrap {
        public helengine.core.content.RuntimeManifestJsonReader Reader;
    }
}
""";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => RunConversionWithPreset(source, "ps2-lite"));
        Assert.Contains("ps2-lite", exception.Message, StringComparison.Ordinal);
        Assert.Contains("runtime_json", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the stripped native core-boot preset writes generic custom-platform config metadata and disables shader-only output.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenPresetIsNativeCoreBoot_WritesGenericConfigAndDisablesShaders() {
        string source = """
namespace helengine {
    public class ShaderAsset {
    }
}

namespace helengine.core.scene {
    public class SceneNode {
    }
}
""";

        string outputPath = RunConversionWithPreset(source, "native-core-boot");
        string configPath = Path.Combine(outputPath, "helcpp_config.hpp");
        string config = File.ReadAllText(configPath);

        Assert.Contains("#define HE_CPP_COMPILER_GCC 1", config);
        Assert.Contains("#define HE_CPP_PLATFORM_RETROPPC 1", config);
        Assert.Contains("#define HE_CPP_PLATFORM_IS_LITTLE_ENDIAN 0", config);
        Assert.Contains("#define HE_CPP_PLATFORM_IS_WINDOWS_HOST 0", config);
        Assert.Contains("#define HE_CPP_FEATURE_SHADERS 0", config);
        Assert.Contains("#define HE_CPP_FEATURE_DEBUG_OVERLAY 0", config);
    }

    /// <summary>
    /// Verifies that native-column-vector conversions specialize the generated <c>helengine.float4x4</c> method bodies during emission instead of rewriting emitted files afterward.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenPresetIsNativeCoreBoot_SpecializesFloat4x4BodiesDuringEmission() {
        string source = """
namespace helengine {
    public struct float3 {
        public float X;
        public float Y;
        public float Z;

        public static float3 Normalize(float3 value) {
            return value;
        }

        public static float3 Cross(float3 left, float3 right) {
            return left;
        }

        public static float Dot(float3 left, float3 right) {
            return 0f;
        }

        public static float3 operator -(float3 left, float3 right) {
            return left;
        }
    }

    public struct float4 {
        public float X;
        public float Y;
        public float Z;
        public float W;
    }

    public struct float4x4 {
        public float M11;
        public float M12;
        public float M13;
        public float M14;
        public float M21;
        public float M22;
        public float M23;
        public float M24;
        public float M31;
        public float M32;
        public float M33;
        public float M34;
        public float M41;
        public float M42;
        public float M43;
        public float M44;

        public static void CreateLookAt(ref float3 cameraPosition, ref float3 cameraTarget, ref float3 cameraUpVector, out float4x4 result) {
            result = new float4x4();
        }

        public static void CreateOrthographicOffCenter(float left, float right, float bottom, float top, float zNearPlane, float zFarPlane, out float4x4 result) {
            result = new float4x4();
        }

        public static void CreatePerspectiveFieldOfView(float fieldOfView, float aspectRatio, float nearPlaneDistance, float farPlaneDistance, out float4x4 result) {
            result = new float4x4();
        }

        public static void CreateTranslation(float x, float y, float z, out float4x4 result) {
            result = new float4x4();
        }

        public static void CreateTranslation(ref float3 position, out float4x4 result) {
            result = new float4x4();
        }

        public static void Multiply(ref float4x4 matrix1, ref float4x4 matrix2, out float4x4 result) {
            result = new float4x4();
        }

        public static void CreateFromQuaternion(ref float4 quaternion, out float4x4 result) {
            result = new float4x4();
        }
    }
}
""";

        string outputPath = RunConversionWithPreset(source, "native-core-boot");
        string float4x4Source = File.ReadAllText(Path.Combine(outputPath, "float4x4.cpp"));

        Assert.Contains("result.M41 = -float3::Dot(vector2, cameraPosition);", float4x4Source);
        Assert.Contains("result.M43 = static_cast<float>((static_cast<double>(zNearPlane) / (static_cast<double>(zNearPlane) - static_cast<double>(zFarPlane))));", float4x4Source);
        Assert.Contains("result.M43 = -1.0f;", float4x4Source);
        Assert.Contains("result.M14 = x;", float4x4Source);
        Assert.Contains("result.M24 = position.Y;", float4x4Source);
        Assert.Contains("float m11 = (((matrix2.M11 * matrix1.M11) + (matrix2.M12 * matrix1.M21)) + (matrix2.M13 * matrix1.M31)) + (matrix2.M14 * matrix1.M41);", float4x4Source);
        Assert.Contains("result.M12 = 2.0f * (num6 + num5);", float4x4Source);
        Assert.DoesNotContain("result.M41 = x;", float4x4Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generic custom platform file-system hooks are emitted through generated config and the shared file runtime template instead of platform-owned rewrite passes.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenCustomPlatformDeclaresNativeFileSystem_HooksSharedFileRuntimeTemplate() {
        string source = """
namespace helengine {
    public sealed class SceneNode {
    }
}
""";

        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-custom-platform-file-hook-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string sourcePath = Path.Combine(rootPath, "Fixture.cs");
        string outputPath = Path.Combine(rootPath, "out");

        Directory.CreateDirectory(rootPath);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(sourcePath, source);

        CPPConversionOptions options = new CPPConversionOptions {
            CompilerProfile = CPPCompilerProfile.CreateGcc(),
            PlatformProfile = CPPPlatformProfile.CreateCustomHeadless("retroppc", false, CPPGeneratedMathConventionKind.NativeColumnVector, 4),
            RuntimeProfile = CPPRuntimeProfile.CreateStlLite(),
            CollectDiagnostics = true,
            BuildFeatureProfile = CPPBuildFeatureProfile.CreateDefault().WithMode("shaders", CPPFeatureMode.Disabled).WithMode("debug_overlay", CPPFeatureMode.Disabled),
            RestrictionProfile = new CPPRestrictionProfile {
                Name = "native-core-boot",
                ForbidShaders = true,
                ForbidRuntimeJson = true,
                ForbidReflectionLikeRuntime = true,
                ForbidDebugOnlySystems = true
            },
            IncludeProjectDefinedPreprocessorSymbols = false,
            AdditionalPreprocessorSymbols = [
                "HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION",
                "HELENGINE_CODEGEN_DISABLE_MENU_REFLECTION"
            ],
            LoadNativeRuntimeMetadata = false,
            WriteConversionReport = true,
            FeatureCatalog = CPPTestFeatureCatalogFactory.CreateHelengineCatalog(),
            PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["native-file-system-header"] = "\"platform/gamecube/GameCubeDiscFileSystem.hpp\"",
                ["native-file-system-type"] = "helengine::gamecube::GameCubeDiscFileSystem"
            }
        };

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);

        string configOutput = File.ReadAllText(Path.Combine(outputPath, "helcpp_config.hpp"));
        string fileSource = File.ReadAllText(Path.Combine(outputPath, "system", "io", "file.cpp"));
        string fileStreamSource = File.ReadAllText(Path.Combine(outputPath, "system", "io", "file-stream.cpp"));
        Assert.Contains("#define HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM 1", configOutput);
        Assert.Contains("#define HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_HEADER \"platform/gamecube/GameCubeDiscFileSystem.hpp\"", configOutput);
        Assert.Contains("#define HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE helengine::gamecube::GameCubeDiscFileSystem", configOutput);
        Assert.Contains("#include HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_HEADER", fileSource);
        Assert.Contains("HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::CanHandlePath(fileName)", fileSource);
        Assert.Contains("HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::Exists(fileName)", fileSource);
        Assert.Contains("HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::OpenRead(filePath)", fileSource);
        Assert.DoesNotContain("HE_CPP_PLATFORM_GAMECUBE", fileSource, StringComparison.Ordinal);
        Assert.Contains("#include HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_HEADER", fileStreamSource);
        Assert.Contains("mode == FileMode::Open && HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::CanHandlePath(path)", fileStreamSource);
        Assert.Contains("std::unique_ptr<FileStream> customStream(HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::OpenRead(path));", fileStreamSource);
        Assert.DoesNotContain("HE_CPP_PLATFORM_GAMECUBE", fileStreamSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the stripped native core-boot preset keeps the raw shader asset data contract needed by runtime helpers even while the shader feature is disabled.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenPresetIsNativeCoreBoot_KeepsShaderAssetDataContract() {
        string source = """
namespace helengine {
    public class ShaderAsset {
        public string Id;
    }

    public class SceneNode {
        public ShaderAsset Asset;
    }
}
""";

        string outputPath = RunConversionWithPreset(source, "native-core-boot");

        Assert.True(File.Exists(Path.Combine(outputPath, "ShaderAsset.hpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "ShaderAsset.cpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "SceneNode.hpp")));
    }

    /// <summary>
    /// Verifies that the stripped native core-boot preset applies the reflection-disabling preprocessor symbols before type emission.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenPresetIsNativeCoreBoot_DoesNotEmitRuntimeScriptReflectionTypes() {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-gamecube-reflection-pruning-tests", Guid.NewGuid().ToString("N"));
        string referencedProjectPath = Path.Combine(rootPath, "Referenced", "Referenced.csproj");
        string rootProjectPath = Path.Combine(rootPath, "Root", "Root.csproj");
        string outputPath = Path.Combine(rootPath, "out");

        Directory.CreateDirectory(Path.GetDirectoryName(referencedProjectPath) ?? throw new InvalidOperationException("Referenced project directory path must resolve."));
        Directory.CreateDirectory(Path.GetDirectoryName(rootProjectPath) ?? throw new InvalidOperationException("Root project directory path must resolve."));
        File.WriteAllText(referencedProjectPath, CreateProjectFile());
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(referencedProjectPath) ?? throw new InvalidOperationException("Referenced project directory path must resolve."), "RuntimeContracts.cs"),
            """
namespace helengine {
    public interface IScriptTypeResolver {
    }

    public interface IMenuDefinitionProvider {
    }

    public interface IRuntimeComponentDeserializer {
    }
}
""");
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(referencedProjectPath) ?? throw new InvalidOperationException("Referenced project directory path must resolve."), "AutomaticScriptComponentRuntimeDeserializer.cs"),
            """
#if !HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION
namespace helengine {
    public sealed class AutomaticScriptComponentRuntimeDeserializer : IRuntimeComponentDeserializer {
    }
}
#endif
""");
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(referencedProjectPath) ?? throw new InvalidOperationException("Referenced project directory path must resolve."), "ScriptTypeResolver.cs"),
            """
#if !HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION
namespace helengine {
    public sealed class ScriptTypeResolver : IScriptTypeResolver {
    }
}
#endif
""");
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(referencedProjectPath) ?? throw new InvalidOperationException("Referenced project directory path must resolve."), "MenuDefinitionProviderResolver.cs"),
            """
namespace helengine {
    public class MenuDefinitionProviderResolver {
        readonly IScriptTypeResolver scriptTypeResolver;

        public MenuDefinitionProviderResolver(IScriptTypeResolver scriptTypeResolver = null) {
            this.scriptTypeResolver = scriptTypeResolver;
        }

        public IMenuDefinitionProvider Resolve(string providerTypeName) {
#if HELENGINE_CODEGEN_DISABLE_MENU_REFLECTION
            throw new InvalidOperationException("Menu definition provider reflection is not available in generated native builds.");
#else
            object instance = Activator.CreateInstance(typeof(object));
            return (IMenuDefinitionProvider)instance;
#endif
        }
    }
}
""");
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(referencedProjectPath) ?? throw new InvalidOperationException("Referenced project directory path must resolve."), "RuntimeComponentRegistry.cs"),
            """
namespace helengine {
    public sealed class RuntimeComponentRegistry {
        public static RuntimeComponentRegistry CreateDefault() {
            return new RuntimeComponentRegistry();
        }

        public IRuntimeComponentDeserializer GetDeserializer(string componentTypeId) {
#if HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION
            throw new InvalidOperationException($"Player builds do not support serialized component type '{componentTypeId}' yet.");
#else
            return new AutomaticScriptComponentRuntimeDeserializer();
#endif
        }
    }
}
""");
        File.WriteAllText(
            rootProjectPath,
            CreateProjectFileWithReference(Path.GetRelativePath(
                Path.GetDirectoryName(rootProjectPath) ?? throw new InvalidOperationException("Root project directory path must resolve."),
                referencedProjectPath)));
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(rootProjectPath) ?? throw new InvalidOperationException("Root project directory path must resolve."), "SceneNode.cs"),
            """
using helengine;

namespace helengine {
    public class SceneNode {
        public RuntimeComponentRegistry Registry = RuntimeComponentRegistry.CreateDefault();
        public MenuDefinitionProviderResolver Resolver = new MenuDefinitionProviderResolver();
    }
}
""");

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.PresetId = "native-core-boot";
        options.FeatureCatalog = CPPTestFeatureCatalogFactory.CreateHelengineCatalog();

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(rootProjectPath);
        converter.WriteOutput(outputPath);

        Assert.False(File.Exists(Path.Combine(outputPath, "AutomaticScriptComponentRuntimeDeserializer.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "AutomaticScriptComponentRuntimeDeserializer.cpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "ScriptTypeResolver.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "ScriptTypeResolver.cpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "RuntimeComponentRegistry.hpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "MenuDefinitionProviderResolver.hpp")));
        string resolverSource = File.ReadAllText(Path.Combine(outputPath, "MenuDefinitionProviderResolver.cpp"));
        Assert.DoesNotContain("Activator::CreateInstance", resolverSource);
    }

    /// <summary>
    /// Verifies that the stripped native core-boot preset keeps the stream-reader runtime helper when emitted content processors still depend on direct UTF-8 text reads.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenPresetIsNativeCoreBoot_KeepsStreamReaderForTextContentProcessor() {
        string source = """
using System;
using System.IO;

namespace helengine {
    public class TextContent {
        public string Text { get; set; }
    }

    public interface IContentProcessor {
        object ReadObject(Stream stream);
    }

    public interface IContentProcessor<T> : IContentProcessor {
        T Read(Stream stream);
    }

    public class ContentManager {
        public TextContentProcessor TextProcessor = new TextContentProcessor();
    }

    public class TextContentProcessor : IContentProcessor<TextContent> {
        public Type OutputType => typeof(TextContent);

        public TextContent Read(Stream stream) {
            using StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 1024, true);
            return new TextContent {
                Text = reader.ReadToEnd()
            };
        }

        object IContentProcessor.ReadObject(Stream stream) {
            return Read(stream);
        }
    }
}
""";

        string outputPath = RunConversionWithPreset(source, "native-core-boot");

        Assert.True(File.Exists(Path.Combine(outputPath, "TextContentProcessor.hpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "system", "io", "stream-reader.hpp")));
    }

    /// <summary>
    /// Verifies that editor-only inspector attribute sources are removed from generated stripped-native output before the unity translation unit is authored.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenPresetIsNativeCoreBoot_RemovesEditorOnlyAttributeSources() {
        string source = """
using System;

namespace helengine {
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class EditorPropertyDisplayNameAttribute : Attribute {
        public string DisplayName { get; }

        public EditorPropertyDisplayNameAttribute(string displayName) {
            DisplayName = displayName;
        }
    }

    public sealed class CameraSettings {
        [EditorPropertyDisplayName("Clear Color")]
        public string ClearColor { get; set; }
    }
}
""";

        string outputPath = RunConversionWithPreset(source, "native-core-boot");

        Assert.True(File.Exists(Path.Combine(outputPath, "CameraSettings.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "EditorPropertyDisplayNameAttribute.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "EditorPropertyDisplayNameAttribute.cpp")));
        string unitySource = File.ReadAllText(Path.Combine(outputPath, "helengine_core_unity.cpp"));
        Assert.DoesNotContain("EditorPropertyDisplayNameAttribute.cpp", unitySource);
    }

    /// <summary>
    /// Verifies generated source files include concrete headers for signature types whose implementations dereference those parameters in the source body.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenSourceUsesPointerLikeSignatureType_IncludesConcreteHeaderInSource() {
        string source = """
namespace helengine {
    public sealed class Child {
        public int Value { get; set; }
    }

    public sealed class Parent {
        public int ReadValue(Child child) {
            return child.Value;
        }
    }
}
""";

        string outputPath = RunConversionWithPreset(source, "native-core-boot");

        string parentSource = File.ReadAllText(Path.Combine(outputPath, "Parent.cpp"));
        Assert.Contains("#include \"Child.hpp\"", parentSource);
    }

    /// <summary>
    /// Verifies constructor initializer arguments are lowered through the C++ expression pipeline so enum member access uses scoped enum syntax.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenConstructorInitializerUsesEnumValue_EmitsScopedEnumAccess() {
        string source = """
namespace helengine {
    public enum LightType {
        Ambient,
        Spot
    }

    public class LightComponent {
        public LightComponent(LightType lightType) {
        }
    }

    public sealed class AmbientLightComponent : LightComponent {
        public AmbientLightComponent() : base(LightType.Ambient) {
        }
    }
}
""";

        string outputPath = RunConversionWithPreset(source, "native-core-boot");

        string ambientSource = File.ReadAllText(Path.Combine(outputPath, "AmbientLightComponent.cpp"));
        Assert.Contains("LightType::Ambient", ambientSource);
    }

    /// <summary>
    /// Verifies primitive finite-check static calls are lowered to the native number runtime instead of leaking CLR primitive type names into generated C++.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenSourceUsesPrimitiveFiniteChecks_LowersToNativeNumberRuntime() {
        string source = """
namespace helengine {
    public sealed class StepValidator {
        public bool IsFinite(double value) {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
""";

        string outputPath = RunConversionWithPreset(source, "native-core-boot");

        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "StepValidator.cpp"));
        string numberHeader = File.ReadAllText(Path.Combine(outputPath, "system", "number.hpp"));
        Assert.Contains("Number::IsNaN", sourceOutput);
        Assert.Contains("Number::IsInfinity", sourceOutput);
        Assert.DoesNotContain("Double::IsNaN", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Double::IsInfinity", sourceOutput, StringComparison.Ordinal);
        Assert.Contains("static bool IsNaN(double value)", numberHeader);
        Assert.Contains("static bool IsInfinity(double value)", numberHeader);
    }

    /// <summary>
    /// Runs the C++ converter against a temporary single-file project and returns the generated output path.
    /// </summary>
    /// <param name="source">C# source file content to convert.</param>
    /// <param name="featureProfile">Resolved feature configuration for the conversion.</param>
    /// <returns>The output directory that contains the generated C++ files.</returns>
    static string RunConversion(string source, CPPBuildFeatureProfile featureProfile, string presetId = "") {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-feature-pruning-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string sourcePath = Path.Combine(rootPath, "Fixture.cs");
        string outputPath = Path.Combine(rootPath, "out");

        Directory.CreateDirectory(rootPath);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(sourcePath, source);

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.PresetId = presetId ?? string.Empty;
        options.FeatureCatalog = CPPTestFeatureCatalogFactory.CreateHelengineCatalog();
        if (featureProfile != null && string.IsNullOrWhiteSpace(options.PresetId)) {
            options.BuildFeatureProfile = featureProfile;
        }

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);

        string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
        using JsonDocument _ = JsonDocument.Parse(File.ReadAllText(reportPath));
        return outputPath;
    }

    /// <summary>
    /// Runs the C++ converter against a temporary single-file project using a named preset.
    /// </summary>
    /// <param name="source">C# source file content to convert.</param>
    /// <param name="presetId">Stable preset id to resolve for the conversion.</param>
    /// <returns>The output directory that contains the generated C++ files.</returns>
    static string RunConversionWithPreset(string source, string presetId) {
        return RunConversion(source, new CPPBuildFeatureProfile(), presetId);
    }

    /// <summary>
    /// Creates a minimal SDK-style project file for temporary converter fixtures.
    /// </summary>
    /// <returns>Project file content suitable for Roslyn-based analysis.</returns>
    static string CreateProjectFile() {
        return """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """;
    }

    /// <summary>
    /// Creates a minimal SDK-style project file that references another temporary fixture project.
    /// </summary>
    /// <param name="projectReferencePath">Relative path from the root fixture project to the referenced project.</param>
    /// <returns>Project file content suitable for Roslyn-based analysis.</returns>
    static string CreateProjectFileWithReference(string projectReferencePath) {
        if (string.IsNullOrWhiteSpace(projectReferencePath)) {
            throw new ArgumentException("Project reference path must be provided.", nameof(projectReferencePath));
        }

        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{projectReferencePath}}" />
              </ItemGroup>
            </Project>
            """;
    }

    /// <summary>
    /// Resolves the source runtime template root used to seed temporary end-to-end runtime folders.
    /// </summary>
    /// <returns>The runtime template directory from the repository checkout.</returns>
    static string ResolveRuntimeTemplateRoot() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cs2.cpp", ".net.cpp"));
    }

    /// <summary>
    /// Copies one directory tree into another for runtime-template end-to-end setup.
    /// </summary>
    /// <param name="sourcePath">Directory to copy from.</param>
    /// <param name="targetPath">Directory to copy into.</param>
    static void CopyDirectoryRecursively(string sourcePath, string targetPath) {
        Directory.CreateDirectory(targetPath);

        foreach (string directoryPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories)) {
            string relativePath = Path.GetRelativePath(sourcePath, directoryPath);
            Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
        }

        foreach (string filePath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories)) {
            string relativePath = Path.GetRelativePath(sourcePath, filePath);
            string targetFilePath = Path.Combine(targetPath, relativePath);
            string targetDirectoryPath = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrWhiteSpace(targetDirectoryPath)) {
                Directory.CreateDirectory(targetDirectoryPath);
            }

            File.Copy(filePath, targetFilePath, true);
        }
    }

    /// <summary>
    /// Provides a row-vector float4x4 runtime seed that the end-to-end test can force through the GameCube adapter seam.
    /// </summary>
    static readonly string GameCubeFloat4x4RuntimeSeed = """
void float4x4::CreateFromQuaternion(::float4& quaternion, ::float4x4& result)
{
const float num9 = quaternion.X * quaternion.X;
const float num8 = quaternion.Y * quaternion.Y;
const float num7 = quaternion.Z * quaternion.Z;
const float num6 = quaternion.X * quaternion.Y;
const float num5 = quaternion.Z * quaternion.W;
const float num4 = quaternion.Z * quaternion.X;
const float num3 = quaternion.Y * quaternion.W;
const float num2 = quaternion.Y * quaternion.Z;
const float num = quaternion.X * quaternion.W;
result.M11 = 1.0f - (2.0f * (num8 + num7));
result.M12 = 2.0f * (num6 + num5);
result.M13 = 2.0f * (num4 - num3);
result.M14 = 0.0f;
result.M21 = 2.0f * (num6 - num5);
result.M22 = 1.0f - (2.0f * (num7 + num9));
result.M23 = 2.0f * (num2 + num);
result.M24 = 0.0f;
result.M31 = 2.0f * (num4 + num3);
result.M32 = 2.0f * (num2 - num);
result.M33 = 1.0f - (2.0f * (num8 + num9));
result.M34 = 0.0f;
result.M41 = 0.0f;
result.M42 = 0.0f;
result.M43 = 0.0f;
result.M44 = 1.0f;
}
void float4x4::CreateLookAt(::float3& cameraPosition, ::float3& cameraTarget, ::float3& cameraUpVector, ::float4x4& result)
{
::float3 vector = float3::Normalize(cameraPosition - cameraTarget);
::float3 vector2 = float3::Normalize(float3::Cross(cameraUpVector, vector));
::float3 vector3 = float3::Cross(vector, vector2);
result.M11 = vector2.X;
result.M12 = vector3.X;
result.M13 = vector.X;
result.M14 = 0.0f;
result.M21 = vector2.Y;
result.M22 = vector3.Y;
result.M23 = vector.Y;
result.M24 = 0.0f;
result.M31 = vector2.Z;
result.M32 = vector3.Z;
result.M33 = vector.Z;
result.M34 = 0.0f;
result.M41 = -float3::Dot(vector2, cameraPosition);
result.M42 = -float3::Dot(vector3, cameraPosition);
result.M43 = -float3::Dot(vector, cameraPosition);
result.M44 = 1.0f;
}
void float4x4::CreateOrthographicOffCenter(float left, float right, float bottom, float top, float zNearPlane, float zFarPlane, ::float4x4& result)
{
result.M11 = static_cast<float>((2.0 / (static_cast<double>(right) - static_cast<double>(left))));
result.M12 = 0.0f;
result.M13 = 0.0f;
result.M14 = 0.0f;
result.M21 = 0.0f;
result.M22 = static_cast<float>((2.0 / (static_cast<double>(top) - static_cast<double>(bottom))));
result.M23 = 0.0f;
result.M24 = 0.0f;
result.M31 = 0.0f;
result.M32 = 0.0f;
result.M33 = static_cast<float>((1.0 / (static_cast<double>(zNearPlane) - static_cast<double>(zFarPlane))));
result.M34 = 0.0f;
result.M41 = static_cast<float>(((static_cast<double>(left) + static_cast<double>(right)) / (static_cast<double>(left) - static_cast<double>(right))));
result.M42 = static_cast<float>(((static_cast<double>(top) + static_cast<double>(bottom)) / (static_cast<double>(bottom) - static_cast<double>(top))));
result.M43 = static_cast<float>((static_cast<double>(zNearPlane) / (static_cast<double>(zNearPlane) - static_cast<double>(zFarPlane))));
result.M44 = 1.0f;
}
void float4x4::CreatePerspectiveFieldOfView(float fieldOfView, float aspectRatio, float nearPlaneDistance, float farPlaneDistance, ::float4x4& result)
{
    if ((fieldOfView <= 0.0f) || (fieldOfView >= 3.141593f))
    {
throw new ArgumentException("fieldOfView <= 0 or >= PI");
    }
    if (nearPlaneDistance <= 0.0f)
    {
throw new ArgumentException("nearPlaneDistance <= 0");
    }
    if (farPlaneDistance <= 0.0f)
    {
throw new ArgumentException("farPlaneDistance <= 0");
    }
    if (nearPlaneDistance >= farPlaneDistance)
    {
throw new ArgumentException("nearPlaneDistance >= farPlaneDistance");
    }
float yScale = 1.0f / static_cast<float>(Math::Tan(static_cast<double>(fieldOfView) * 0.5f));
float xScale = yScale / aspectRatio;
float negFarRange = Number::IsPositiveInfinity(farPlaneDistance) ? -1.0f : farPlaneDistance / (nearPlaneDistance - farPlaneDistance);
result.M11 = xScale;
result.M12 = result.M13 = result.M14 = 0.0f;
result.M22 = yScale;
result.M21 = result.M23 = result.M24 = 0.0f;
result.M31 = result.M32 = 0.0f;
result.M33 = negFarRange;
result.M34 = -1.0f;
result.M41 = result.M42 = result.M44 = 0.0f;
result.M43 = nearPlaneDistance * negFarRange;
}
void float4x4::CreateTranslation(float x, float y, float z, ::float4x4& result)
{
result.M11 = 1;
result.M12 = 0;
result.M13 = 0;
result.M14 = 0;
result.M21 = 0;
result.M22 = 1;
result.M23 = 0;
result.M24 = 0;
result.M31 = 0;
result.M32 = 0;
result.M33 = 1;
result.M34 = 0;
result.M41 = x;
result.M42 = y;
result.M43 = z;
result.M44 = 1;
}
void float4x4::CreateTranslation(::float3& position, ::float4x4& result)
{
result.M11 = 1;
result.M12 = 0;
result.M13 = 0;
result.M14 = 0;
result.M21 = 0;
result.M22 = 1;
result.M23 = 0;
result.M24 = 0;
result.M31 = 0;
result.M32 = 0;
result.M33 = 1;
result.M34 = 0;
result.M41 = position.X;
result.M42 = position.Y;
result.M43 = position.Z;
result.M44 = 1;
}
void float4x4::Multiply(::float4x4& matrix1, ::float4x4& matrix2, ::float4x4& result)
{
float m11 = (((matrix1.M11 * matrix2.M11) + (matrix1.M12 * matrix2.M21)) + (matrix1.M13 * matrix2.M31)) + (matrix1.M14 * matrix2.M41);
float m12 = (((matrix1.M11 * matrix2.M12) + (matrix1.M12 * matrix2.M22)) + (matrix1.M13 * matrix2.M32)) + (matrix1.M14 * matrix2.M42);
float m13 = (((matrix1.M11 * matrix2.M13) + (matrix1.M12 * matrix2.M23)) + (matrix1.M13 * matrix2.M33)) + (matrix1.M14 * matrix2.M43);
float m14 = (((matrix1.M11 * matrix2.M14) + (matrix1.M12 * matrix2.M24)) + (matrix1.M13 * matrix2.M34)) + (matrix1.M14 * matrix2.M44);
float m21 = (((matrix1.M21 * matrix2.M11) + (matrix1.M22 * matrix2.M21)) + (matrix1.M23 * matrix2.M31)) + (matrix1.M24 * matrix2.M41);
float m22 = (((matrix1.M21 * matrix2.M12) + (matrix1.M22 * matrix2.M22)) + (matrix1.M23 * matrix2.M32)) + (matrix1.M24 * matrix2.M42);
float m23 = (((matrix1.M21 * matrix2.M13) + (matrix1.M22 * matrix2.M23)) + (matrix1.M23 * matrix2.M33)) + (matrix1.M24 * matrix2.M43);
float m24 = (((matrix1.M21 * matrix2.M14) + (matrix1.M22 * matrix2.M24)) + (matrix1.M23 * matrix2.M34)) + (matrix1.M24 * matrix2.M44);
float m31 = (((matrix1.M31 * matrix2.M11) + (matrix1.M32 * matrix2.M21)) + (matrix1.M33 * matrix2.M31)) + (matrix1.M34 * matrix2.M41);
float m32 = (((matrix1.M31 * matrix2.M12) + (matrix1.M32 * matrix2.M22)) + (matrix1.M33 * matrix2.M32)) + (matrix1.M34 * matrix2.M42);
float m33 = (((matrix1.M31 * matrix2.M13) + (matrix1.M32 * matrix2.M23)) + (matrix1.M33 * matrix2.M33)) + (matrix1.M34 * matrix2.M43);
float m34 = (((matrix1.M31 * matrix2.M14) + (matrix1.M32 * matrix2.M24)) + (matrix1.M33 * matrix2.M34)) + (matrix1.M34 * matrix2.M44);
float m41 = (((matrix1.M41 * matrix2.M11) + (matrix1.M42 * matrix2.M21)) + (matrix1.M43 * matrix2.M31)) + (matrix1.M44 * matrix2.M41);
float m42 = (((matrix1.M41 * matrix2.M12) + (matrix1.M42 * matrix2.M22)) + (matrix1.M43 * matrix2.M32)) + (matrix1.M44 * matrix2.M42);
float m43 = (((matrix1.M41 * matrix2.M13) + (matrix1.M42 * matrix2.M23)) + (matrix1.M43 * matrix2.M33)) + (matrix1.M44 * matrix2.M43);
float m44 = (((matrix1.M41 * matrix2.M14) + (matrix1.M42 * matrix2.M24)) + (matrix1.M43 * matrix2.M34)) + (matrix1.M44 * matrix2.M44);
result.M11 = m11;
result.M12 = m12;
result.M13 = m13;
result.M14 = m14;
result.M21 = m21;
result.M22 = m22;
result.M23 = m23;
result.M24 = m24;
result.M31 = m31;
result.M32 = m32;
result.M33 = m33;
result.M34 = m34;
result.M41 = m41;
result.M42 = m42;
result.M43 = m43;
result.M44 = m44;
}
""";
}
