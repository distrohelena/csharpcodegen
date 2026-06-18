using System.Text.Json;
using System.Text.RegularExpressions;
using cs2.core;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Covers compile-validation regressions that surfaced from native compilation of generated helengine.core output.
    /// </summary>
    public class CPPCompileValidationRegressionTests {
        /// <summary>
        /// Ensures character literals preserve C# escape text so generated C++ uses valid char literals.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCharacterLiterals_EmitsEscapedCppCharLiterals() {
            string source = """
                public class CharacterGate {
                    public char Map(bool shift) {
                        return shift ? '"' : '\'';
                    }

                    public bool IsEmpty(char value) {
                        return value == '\0';
                    }

                    public char Space() {
                        return ' ';
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            AssertNoDiagnostic(output.Report, "CharacterLiteralExpression");
            Assert.Contains("return shift ? '\"' : '\\'';", output.GeneratedText);
            Assert.Contains("value == '\\0'", output.GeneratedText);
            Assert.Contains("return ' ';", output.GeneratedText);
            Assert.DoesNotContain("return shift ? \"\"\" : \"'\";", output.GeneratedText);
        }

        /// <summary>
        /// Ensures the copied console runtime template uses the correct include path for the file helper.
        /// </summary>
        [Fact]
        public void WriteOutput_CopiesConsoleRuntimeTemplateWithIoInclude() {
            string source = """
                public class EmptyGate {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string consolePath = Path.Combine(output.OutputPath, "system", "console.cpp");

            Assert.True(File.Exists(consolePath));
            Assert.Contains("#include \"io/file.hpp\"", File.ReadAllText(consolePath));
        }

        /// <summary>
        /// Ensures generated member access and primitive headers use native C++ syntax required by compile validation.
        /// </summary>
        [Fact]
        public void WriteOutput_NormalizesThisBaseNullAndFixedWidthIntegerHeaders() {
            string source = """
                public class Node {
                }

                public class BaseGate {
                    protected Node Current;

                    public virtual void Tick(int amount) {
                    }
                }

                public class DerivedGate : BaseGate {
                    Node Value;

                    public override void Tick(int amount) {
                        this.Value = this.Current;

                        if (this.Value == null) {
                            this.Value = null;
                        }

                        base.Tick(amount);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("#include <cstdint>", output.GeneratedText);
            Assert.Contains("this->Value = this->Current;", output.GeneratedText);
            Assert.Contains("if (this->Value == nullptr)", output.GeneratedText);
            Assert.Contains("this->Value = nullptr;", output.GeneratedText);
            Assert.Contains("BaseGate::Tick(amount);", output.GeneratedText);
            Assert.DoesNotContain("this.Value", output.GeneratedText);
            Assert.DoesNotContain("super", output.GeneratedText);
        }

        /// <summary>
        /// Ensures inherited classes and method-body type references emit the includes and base declaration required for native compile visibility.
        /// </summary>
        [Fact]
        public void WriteOutput_EmitsBaseInheritanceAndBodyTypeIncludes() {
            string source = """
                public class HelperNode {
                }

                public class CoreGate {
                    public static HelperNode Shared;
                }

                public class BaseGate {
                    public virtual void Tick() {
                    }
                }

                public class DerivedGate : BaseGate {
                    public override void Tick() {
                        base.Tick();
                        var local = new HelperNode();
                        var shared = CoreGate.Shared;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string derivedHeader = File.ReadAllText(Path.Combine(output.OutputPath, "DerivedGate.hpp"));
            string derivedSource = File.ReadAllText(Path.Combine(output.OutputPath, "DerivedGate.cpp"));

            Assert.Contains("#include \"BaseGate.hpp\"", derivedHeader);
            Assert.Contains("#include \"HelperNode.hpp\"", derivedHeader);
            Assert.Contains("#include \"CoreGate.hpp\"", derivedHeader);
            Assert.Contains("class DerivedGate : public BaseGate", derivedHeader);
            Assert.Contains("BaseGate::Tick()", derivedSource);
            Assert.Contains("HelperNode()", derivedSource);
            Assert.Contains("CoreGate::Shared", derivedSource);
        }

        /// <summary>
        /// Ensures concrete generated classes remain polymorphic so runtime downcasts can use RTTI safely.
        /// </summary>
        [Fact]
        public void WriteOutput_WithConcreteClass_EmitsVirtualDestructor() {
            string source = """
                public class Asset {
                }

                public class TextureAsset : Asset {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string assetHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Asset.hpp"));
            string textureHeader = File.ReadAllText(Path.Combine(output.OutputPath, "TextureAsset.hpp"));

            Assert.Contains("virtual ~Asset() = default;", assetHeader);
            Assert.Contains("virtual ~TextureAsset() = default;", textureHeader);
        }

        /// <summary>
        /// Ensures converted value types stay nonpolymorphic so unmanaged layout-sensitive structs do not gain vtables.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStruct_DoesNotEmitVirtualDestructor() {
            string source = """
                public struct Metanode {
                    public int Parent;
                    public int IndexInParent;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Metanode.hpp"));

            Assert.DoesNotContain("virtual ~Metanode() = default;", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures fallback inheritance rendering skips interface bases for value types when symbol-backed inheritance data is unavailable.
        /// </summary>
        [Fact]
        public void GetInheritance_WithValueTypeFallbackExtensions_ReturnsEmptyClause() {
            ConversionProgram program = new ConversionProgram(new ConversionRules());
            ConversionClass conversionClass = new ConversionClass {
                Name = "float3",
                IsValueType = true
            };
            conversionClass.Extensions.Add("IEquatable");
            conversionClass.Extensions.Add("IEqualityComparerRef");

            string inheritance = CPPUtils.GetInheritance(program, conversionClass);

            Assert.Equal(string.Empty, inheritance);
        }

        /// <summary>
        /// Ensures explicit-layout value types without overlapping offsets emit packed direct fields plus inserted padding that preserve declared field offsets.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitLayoutStruct_EmitsPackedOffsetFields() {
            string source = """
                using System.Runtime.InteropServices;

                public struct Float3 {
                    public float X;
                    public float Y;
                    public float Z;
                }

                [StructLayout(LayoutKind.Explicit)]
                public struct NodeChild {
                    [FieldOffset(0)]
                    public Float3 Min;

                    [FieldOffset(12)]
                    public int Index;

                    [FieldOffset(16)]
                    public Float3 Max;

                    [FieldOffset(28)]
                    public int LeafCount;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "NodeChild.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "NodeChild.cpp"));

            Assert.Contains("#pragma pack(push, 1)", headerOutput);
            Assert.DoesNotContain("union {", headerOutput, StringComparison.Ordinal);
            Assert.Contains(" Min;", headerOutput);
            Assert.Contains("int32_t Index;", headerOutput);
            Assert.Contains(" Max;", headerOutput);
            Assert.Contains("int32_t LeafCount;", headerOutput);
            Assert.Contains("#pragma pack(pop)", headerOutput);
            Assert.DoesNotContain("NodeChild::NodeChild() :", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("= ;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures explicit-layout overlapping fields emit separate overlay structs so unioned offsets remain representable.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitLayoutOverlap_EmitsOverlayStructsForSharedOffsets() {
            string source = """
                using System.Runtime.InteropServices;

                [StructLayout(LayoutKind.Explicit)]
                public struct Metanode {
                    [FieldOffset(0)]
                    public int Parent;

                    [FieldOffset(4)]
                    public int IndexInParent;

                    [FieldOffset(8)]
                    public int RefineFlag;

                    [FieldOffset(8)]
                    public float LocalCostChange;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Metanode.hpp"));

            Assert.Contains("uint8_t __pad_0[4];", headerOutput);
            Assert.Equal(2, Regex.Matches(headerOutput, "uint8_t __pad_[0-9]+\\[8\\];").Count);
            Assert.Contains("int32_t RefineFlag;", headerOutput);
            Assert.Contains("float LocalCostChange;", headerOutput);
        }

        /// <summary>
        /// Ensures explicit-layout structs without overlapping offsets emit padded direct fields instead of anonymous union overlays so stricter C++ compilers can accept non-trivial members.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitLayoutStructWithoutOverlap_EmitsPaddedDirectFields() {
            string source = """
                using System.Runtime.InteropServices;

                public struct Float3 {
                    public float X;
                    public float Y;
                    public float Z;
                }

                [StructLayout(LayoutKind.Explicit, Size = 32)]
                public struct BodyVelocity {
                    [FieldOffset(0)]
                    public Float3 Linear;

                    [FieldOffset(16)]
                    public Float3 Angular;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "BodyVelocity.hpp"));

            Assert.Contains("#pragma pack(push, 1)", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("union {", headerOutput, StringComparison.Ordinal);
            Assert.Contains("::Float3 Linear;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("uint8_t __pad_0[4];", headerOutput, StringComparison.Ordinal);
            Assert.Contains("::Float3 Angular;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("uint8_t __tail_padding[4];", headerOutput, StringComparison.Ordinal);
            Assert.Contains("#pragma pack(pop)", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures explicit-layout structs with monotonic generated generic value-type fields emit padded direct fields instead of invalid overlay unions.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitLayoutGeneratedGenericValueTypeFields_EmitsPaddedDirectFields() {
            string source = """
                using System.Runtime.InteropServices;

                public unsafe struct Buffer<T> where T : unmanaged {
                    public T* Memory;
                    public int Length;
                    public int Id;
                }

                [StructLayout(LayoutKind.Explicit)]
                public unsafe struct PackedContext {
                    [FieldOffset(0)]
                    public Buffer<int> A;

                    [FieldOffset(16)]
                    public Buffer<int> B;

                    [FieldOffset(32)]
                    public float Dt;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PackedContext.hpp"));

            Assert.DoesNotContain("union {", headerOutput, StringComparison.Ordinal);
            Assert.Contains("::Buffer_1<int32_t> A;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("::Buffer_1<int32_t> B;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("float Dt;", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures explicit generic IEnumerable.GetEnumerator implementations do not emit a second native overload that differs only by return type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitGenericGetEnumeratorImplementation_SkipsDuplicateNativeOverload() {
            string source = """
                using System.Collections;
                using System.Collections.Generic;

                public struct ItemEnumerator : IEnumerator<int> {
                    public int Current => 0;

                    object IEnumerator.Current => Current;

                    public bool MoveNext() {
                        return false;
                    }

                    public void Reset() {
                    }

                    public void Dispose() {
                    }
                }

                public struct ItemEnumerable : IEnumerable<int> {
                    public ItemEnumerator GetEnumerator() {
                        return new ItemEnumerator();
                    }

                    IEnumerator IEnumerable.GetEnumerator() {
                        return GetEnumerator();
                    }

                    IEnumerator<int> IEnumerable<int>.GetEnumerator() {
                        return GetEnumerator();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ItemEnumerable.hpp"));

            Assert.Contains("::ItemEnumerator GetEnumerator();", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("IEnumerator<int32_t>* GetEnumerator();", headerOutput, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(headerOutput, "GetEnumerator\\(").Cast<Match>());
        }

        /// <summary>
        /// Ensures indexer calls cast numeric arguments to the resolved native parameter type so signed and unsigned overloads stay unambiguous on stricter C++ toolchains.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSignedAndUnsignedIndexerOverloads_CastsElementAccessArgumentsToResolvedParameterTypes() {
            string source = """
                public class DualIndex {
                    public int this[int index] {
                        get {
                            return index;
                        }
                    }

                    public int this[uint index] {
                        get {
                            return (int)index;
                        }
                    }

                    public int ReadConstant() {
                        return this[0];
                    }

                    public int ReadUnsigned(uint index) {
                        return this[index];
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "DualIndex.cpp"));

            Assert.Contains("this->get_Item(static_cast<int32_t>(0))", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("this->get_Item(static_cast<uint32_t>(index))", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures integral overload disambiguation does not cast ref or out arguments into rvalues.
        /// </summary>
        [Fact]
        public void WriteOutput_WithIntegralOutArgument_DoesNotCastRefOrOutTargets() {
            string source = """
                public class OutHelper {
                    public static void ReadValue(int source, out int value) {
                        value = source;
                    }

                    public int Read() {
                        int result;
                        ReadValue(1, out result);
                        return result;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "OutHelper.cpp"));

            Assert.DoesNotContain("static_cast<int32_t>(result)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures sequential structs with fixed size and pack metadata emit packed declarations plus tail padding so nested runtime layouts remain stable.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSequentialStructLayoutSizeAndPack_EmitsPackedTailPadding() {
            string source = """
                using System.Runtime.InteropServices;

                [StructLayout(LayoutKind.Sequential, Size = 32, Pack = 1)]
                public struct PackedPose {
                    public int A;
                    public int B;
                    public int C;
                    public int D;
                    public int E;
                    public int F;
                    public int G;
                }

                [StructLayout(LayoutKind.Sequential, Size = 64, Pack = 1)]
                public struct PackedMotionState {
                    public PackedPose Pose;
                    public int LinearX;
                    public int LinearY;
                    public int LinearZ;
                    public int AngularX;
                    public int AngularY;
                    public int AngularZ;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string poseHeaderOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PackedPose.hpp"));
            string motionHeaderOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PackedMotionState.hpp"));

            Assert.Contains("#include <array>", poseHeaderOutput);
            Assert.Contains("#pragma pack(push, 1)", poseHeaderOutput);
            Assert.Contains("std::array<uint8_t, 4> __tail_padding{};", poseHeaderOutput);
            Assert.Contains("#pragma pack(pop)", poseHeaderOutput);
            Assert.Contains("#include <array>", motionHeaderOutput);
            Assert.Contains("#pragma pack(push, 1)", motionHeaderOutput);
            Assert.Contains("std::array<uint8_t, 8> __tail_padding{};", motionHeaderOutput);
            Assert.Contains("#pragma pack(pop)", motionHeaderOutput);
        }

        /// <summary>
        /// Ensures sequential-layout sizing respects configured type remaps so packed outer structs preserve their declared native size.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSequentialStructLayoutOverRemappedValueTypes_EmitsPackedTailPadding() {
            string source = """
                using System.Numerics;
                using System.Runtime.InteropServices;

                namespace helengine {
                    public struct float3 {
                        public float X;
                        public float Y;
                        public float Z;
                    }

                    public struct float4 {
                        public float X;
                        public float Y;
                        public float Z;
                        public float W;
                    }
                }

                [StructLayout(LayoutKind.Sequential, Size = 32, Pack = 1)]
                public struct PackedPose {
                    public Vector4 Orientation;
                    public Vector3 Position;
                }
                """;

            ConversionOutput output = RunConversionWithTypeRemaps(
                source,
                new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["System.Numerics.Vector3"] = "helengine.float3",
                    ["System.Numerics.Vector4"] = "helengine.float4"
                });
            string poseHeaderOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PackedPose.hpp"));

            Assert.Contains("#pragma pack(push, 1)", poseHeaderOutput);
            Assert.Contains("::float4 Orientation;", poseHeaderOutput);
            Assert.Contains("::float3 Position;", poseHeaderOutput);
            Assert.Contains("std::array<uint8_t, 4> __tail_padding{};", poseHeaderOutput);
            Assert.Contains("#pragma pack(pop)", poseHeaderOutput);
        }

        /// <summary>
        /// Ensures configured type remaps rewrite emitted C++ across fields, method signatures, local declarations, and nested generic arguments.
        /// </summary>
        [Fact]
        public void WriteOutput_WithConfiguredTypeRemaps_RewritesAllEmittedTypeReferences() {
            string source = """
                using System.Collections.Generic;
                using System.Numerics;

                namespace Example {
                    public struct Float2 {
                        public float X;
                        public float Y;
                    }

                    public struct Float3 {
                        public float X;
                        public float Y;
                        public float Z;
                    }

                    public struct Float4 {
                        public float X;
                        public float Y;
                        public float Z;
                        public float W;
                    }

                    public sealed class RemapFixture {
                        public Vector4 Orientation;
                        public List<Vector2> History;

                        public Vector3 Project(Vector4 input) {
                            Vector3 local = default;
                            List<Vector2> scratch = new List<Vector2>();
                            scratch.Add(default);
                            return local;
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversionWithTypeRemaps(
                source,
                new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["System.Numerics.Vector2"] = "Example.Float2",
                    ["System.Numerics.Vector3"] = "Example.Float3",
                    ["System.Numerics.Vector4"] = "Example.Float4"
                });
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RemapFixture.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RemapFixture.cpp"));
            string combinedOutput = headerOutput + sourceOutput;

            Assert.Contains("::Float4 Orientation;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("List<::Float2>* History;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("::Float3 Project(::Float4 input);", headerOutput, StringComparison.Ordinal);
            Assert.Contains("::Float3 local", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("new List<::Float2>()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Vector2", combinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Vector3", combinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Vector4", combinedOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures value-type instance fields preserve declaration order instead of being alphabetized during native emission.
        /// </summary>
        [Fact]
        public void WriteOutput_WithValueTypeFieldsOutOfAlphabeticalOrder_PreservesDeclarationOrder() {
            string source = """
                public struct OrderedValue {
                    public int Z;
                    public int X;
                    public int Y;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "OrderedValue.hpp"));

            int zIndex = headerOutput.IndexOf("int32_t Z;", StringComparison.Ordinal);
            int xIndex = headerOutput.IndexOf("int32_t X;", StringComparison.Ordinal);
            int yIndex = headerOutput.IndexOf("int32_t Y;", StringComparison.Ordinal);

            Assert.True(zIndex >= 0, "Expected Z field emission.");
            Assert.True(xIndex >= 0, "Expected X field emission.");
            Assert.True(yIndex >= 0, "Expected Y field emission.");
            Assert.True(zIndex < xIndex && xIndex < yIndex, "Expected emitted field order Z, X, Y.");
        }

        /// <summary>
        /// Ensures abstract and virtual members stay polymorphic in generated C++ so native hosts can provide backend subclasses.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAbstractAndOverrideMethods_EmitsVirtualAndOverrideDeclarations() {
            string source = """
                public abstract class Keyboard {
                    public abstract int GetState();

                    public virtual void SetActive(bool isActive) {
                    }
                }

                public class KeyboardWindows : Keyboard {
                    public override int GetState() {
                        return 1;
                    }

                    public override void SetActive(bool isActive) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string baseHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Keyboard.hpp"));
            string derivedHeader = File.ReadAllText(Path.Combine(output.OutputPath, "KeyboardWindows.hpp"));

            Assert.Contains("virtual int32_t GetState() = 0;", baseHeader);
            Assert.Contains("virtual void SetActive(bool isActive);", baseHeader);
            Assert.Contains("int32_t GetState();", derivedHeader);
            Assert.Contains("void SetActive(bool isActive);", derivedHeader);
        }

        /// <summary>
        /// Ensures cyclic generated pointer references emit forward declarations so headers stay compilable without depending on include order.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCyclicGeneratedPointerReferences_EmitsForwardDeclarations() {
            string source = """
                public class Component {
                    public Entity Parent;
                }

                public class Entity {
                    public Component Component;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string entityHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Entity.hpp"));
            string componentHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Component.hpp"));

            Assert.Contains("class Component;", entityHeader);
            Assert.Contains("class Entity;", componentHeader);
            Assert.Contains("Component* Component", entityHeader);
            Assert.Contains("Entity* Parent", componentHeader);
        }

        /// <summary>
        /// Ensures pointer-only generated interface signatures use forward declarations instead of pulling full generated headers into cyclic interface graphs.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerOnlyInterfaceSignature_DoesNotIncludeConcreteGeneratedHeader() {
            string source = """
                public class Entity {
                }

                public interface IDrawable2D {
                    Entity Parent { get; }
                    byte RenderOrder2D { get; set; }
                    void Draw();
                }

                public interface ITextDrawable2D : IDrawable2D {
                    string Text { get; set; }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string drawableHeader = File.ReadAllText(Path.Combine(output.OutputPath, "IDrawable2D.hpp"));

            Assert.Contains("class Entity;", drawableHeader);
            Assert.DoesNotContain("#include \"Entity.hpp\"", drawableHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated string property getters return constant references so hot render paths do not allocate temporary native strings on every access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringProperties_EmitsConstReferenceGetters() {
            string source = """
                public class RuntimeData {
                    public string Id { get; private set; }

                    public void SetId(string id) {
                        Id = id;
                    }
                }

                public class TextComponent {
                    string TextValue;

                    public string Text {
                        get { return TextValue; }
                        set { TextValue = value; }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string runtimeDataHeader = File.ReadAllText(Path.Combine(output.OutputPath, "RuntimeData.hpp"));
            string runtimeDataSource = File.ReadAllText(Path.Combine(output.OutputPath, "RuntimeData.cpp"));
            string textComponentHeader = File.ReadAllText(Path.Combine(output.OutputPath, "TextComponent.hpp"));
            string textComponentSource = File.ReadAllText(Path.Combine(output.OutputPath, "TextComponent.cpp"));

            Assert.Contains("const std::string& get_Id();", runtimeDataHeader);
            Assert.Contains("const std::string& RuntimeData::get_Id()", runtimeDataSource);
            Assert.Contains("return this->Id;", runtimeDataSource);
            Assert.Contains("const std::string& get_Text();", textComponentHeader);
            Assert.Contains("const std::string& TextComponent::get_Text()", textComponentSource);
            Assert.Contains("return this->TextValue;", textComponentSource);
        }

        /// <summary>
        /// Ensures assigning null to string-backed fields lowers to empty native strings instead of invalid std::string null-pointer assignments.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringFieldNullAssignments_EmitsEmptyNativeStrings() {
            string source = """
                public class MaterialLayout {
                    string ShaderAssetIdValue;
                    string VertexProgramValue;
                    string PixelProgramValue;
                    string VariantValue;

                    public void Dispose() {
                        ShaderAssetIdValue = null;
                        VertexProgramValue = null;
                        PixelProgramValue = null;
                        VariantValue = null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "MaterialLayout.cpp"));

            Assert.Contains("this->ShaderAssetIdValue = std::string();", sourceOutput);
            Assert.Contains("this->VertexProgramValue = std::string();", sourceOutput);
            Assert.Contains("this->PixelProgramValue = std::string();", sourceOutput);
            Assert.Contains("this->VariantValue = std::string();", sourceOutput);
            Assert.DoesNotContain("this->ShaderAssetIdValue = nullptr;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->VertexProgramValue = nullptr;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->PixelProgramValue = nullptr;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->VariantValue = nullptr;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated references to generic converted types emit template-aware forward declarations instead of non-template class declarations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericGeneratedReference_EmitsTemplateForwardDeclaration() {
            string source = """
                public class Stream {
                }

                public interface IContentProcessor<T> {
                    T Read(Stream stream);
                }

                public class ContentManager {
                    public IContentProcessor<Stream> Processor;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string contentManagerHeader = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.hpp"));

            Assert.Contains("template <typename T>", contentManagerHeader);
            Assert.Contains("class IContentProcessor_1;", contentManagerHeader);
            Assert.DoesNotContain("#include \"IContentProcessor_1.hpp\"", contentManagerHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated enum references do not emit invalid class forward declarations that conflict with enum class output.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEnumReference_DoesNotEmitClassForwardDeclaration() {
            string source = """
                public enum ButtonState {
                    Released,
                    Pressed
                }

                public class InputState {
                    public ButtonState Button;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string inputStateHeader = File.ReadAllText(Path.Combine(output.OutputPath, "InputState.hpp"));

            Assert.DoesNotContain("class ButtonState;", inputStateHeader, StringComparison.Ordinal);
            Assert.Contains("#include \"ButtonState.hpp\"", inputStateHeader);
        }

        /// <summary>
        /// Ensures generated type references remain qualified in class scope when a member shares the same identifier as its type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMemberNamesMatchingGeneratedTypes_QualifiesTypeReferences() {
            string source = """
                using System.Collections.Generic;

                public class FontInfo {
                }

                public class ContentManager {
                }

                public class Core {
                    public Dictionary<string, ContentManager> Managers;

                    public FontInfo FontInfo { get; private set; }

                    public ContentManager ContentManager { get; private set; }

                    public ContentManager GetContentManager() {
                        return ContentManager;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string coreHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Core.hpp"));
            string coreSource = File.ReadAllText(Path.Combine(output.OutputPath, "Core.cpp"));

            Assert.Contains("::FontInfo* FontInfo;", coreHeader);
            Assert.Contains("::ContentManager* ContentManager;", coreHeader);
            Assert.Contains("Dictionary<std::string, ::ContentManager*>* Managers;", coreHeader);
            Assert.Contains("::ContentManager* GetContentManager();", coreHeader);
            Assert.Contains("::ContentManager* Core::GetContentManager()", coreSource);
            Assert.DoesNotContain("\n    FontInfo* FontInfo;", coreHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("\n    ContentManager* ContentManager;", coreHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures instance method groups lower to native member-function pointers when used for event subscriptions.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInstanceMethodGroupEventSubscription_EmitsMemberFunctionPointer() {
            string source = """
                using System;

                public class Widget {
                    public event Action Changed;

                    public void Handle() {
                    }

                    public void Wire() {
                        Changed += Handle;
                        Changed -= Handle;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("Event Changed;", headerOutput);
            Assert.DoesNotContain("Event* Changed;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("Changed += &Widget::Handle;", sourceOutput);
            Assert.Contains("Changed -= &Widget::Handle;", sourceOutput);
            Assert.DoesNotContain("Changed += this->Handle;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Changed -= this->Handle;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures instance method groups assigned into Action fields lower through a bound native delegate wrapper instead of a raw member-function pointer.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInstanceMethodGroupDelegateAssignment_BindsCurrentInstanceInActionWrapper() {
            string source = """
                using System;

                public class Widget {
                    Action<int> handler;

                    public void Handle(int value) {
                    }

                    public void Wire() {
                        handler = Handle;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("Action<int32_t>* handler;", headerOutput);
            Assert.Contains("this->handler = new Action<int32_t>(std::bind_front(&Widget::Handle, this));", sourceOutput);
            Assert.DoesNotContain("this->handler = &Widget::Handle;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nongeneric Action callbacks emit valid native delegate types and guarded invocation instead of leaking null-conditional Invoke syntax.
        /// </summary>
        [Fact]
        public void WriteOutput_WithParameterlessActionInvocation_UsesActionTemplateAndGuardedCall() {
            string source = """
                using System;

                public class Widget {
                    Action onClickAction;

                    public Widget(Action onClickAction = null) {
                        this.onClickAction = onClickAction;
                    }

                    public void Activate() {
                        onClickAction?.Invoke();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("Action<>* onClickAction", headerOutput);
            Assert.Contains("Widget(Action<>* onClickAction)", headerOutput);
            Assert.Contains("if (this->onClickAction != nullptr)", sourceOutput);
            Assert.Contains("(*this->onClickAction)();", sourceOutput);
            Assert.DoesNotContain("Action* onClickAction", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("?.Invoke()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures event conditional invocation lowers to the native event placeholder instead of delegate-pointer null checks.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEventConditionalInvoke_UsesNativeEventInvoke() {
            string source = """
                using System;

                public struct int2 {
                    public int X;
                    public int Y;
                }

                public enum PointerInteraction {
                    None
                }

                public class Widget {
                    public event Action<int2, int2, PointerInteraction> CursorEvent;

                    public void OnCursor(int2 relPos, int2 delta, PointerInteraction state) {
                        CursorEvent?.Invoke(relPos, delta, state);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("::Event CursorEvent;", headerOutput);
            Assert.Contains("this->CursorEvent.Invoke(relPos, delta, state);", sourceOutput);
            Assert.DoesNotContain("this->CursorEvent != nullptr", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("(*this->CursorEvent)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated enum parameters and switches use value semantics instead of pointer semantics.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGeneratedEnumParametersAndSwitch_UsesValueSemantics() {
            string source = """
                public enum Keys {
                    Enter,
                    Space
                }

                public enum PointerInteraction {
                    None,
                    Hover,
                    Press
                }

                public class Widget {
                    public bool CanActivateWithKey(Keys key) {
                        return key == Keys.Enter || key == Keys.Space;
                    }

                    public void OnCursorEvent(PointerInteraction state) {
                        switch (state) {
                            case PointerInteraction.Hover:
                                break;
                            case PointerInteraction.Press:
                                break;
                            case PointerInteraction.None:
                                break;
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("bool CanActivateWithKey(::Keys key);", headerOutput);
            Assert.Contains("void OnCursorEvent(::PointerInteraction state);", headerOutput);
            Assert.DoesNotContain("Keys* key", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("PointerInteraction* state", headerOutput, StringComparison.Ordinal);
            Assert.Contains("return key == Keys::Enter || key == Keys::Space;", sourceOutput);
            Assert.Contains("switch (state)", sourceOutput);
        }

        /// <summary>
        /// Ensures static computed property chains keep the owning generated type as a header dependency and emit static getter access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStaticGeneratedPropertyChain_EmitsOwnerIncludeAndStaticGetterAccess() {
            string source = """
                public class ThemeColors {
                    public string TextOnAccent;
                }

                public static class ThemeManager {
                    static ThemeColors colors;

                    public static ThemeColors Colors {
                        get {
                            return colors;
                        }
                    }
                }

                public class Widget {
                    string textColor;

                    public void Init() {
                        textColor = ThemeManager.Colors.TextOnAccent;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("#include \"ThemeManager.hpp\"", sourceOutput);
            Assert.Contains("ThemeManager::get_Colors()->TextOnAccent", sourceOutput);
        }

        /// <summary>
        /// Ensures expression-bodied instance properties lower to getter calls instead of uninitialized backing fields.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExpressionBodiedPropertyReceiver_UsesGetterCall() {
            string source = """
                public class Stream {
                    public int ReadByte() {
                        return 1;
                    }
                }

                public class Reader {
                    protected Stream BaseStream;

                    protected Stream Stream => BaseStream;

                    public int Read() {
                        return Stream.ReadByte();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Reader.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Reader.cpp"));

            Assert.Contains("::Stream* get_Stream();", headerOutput);
            Assert.DoesNotContain("\n    ::Stream* Stream;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("::Stream* Reader::get_Stream()", sourceOutput);
            Assert.Contains("return this->get_Stream()->ReadByte();", sourceOutput);
            Assert.DoesNotContain("return this->Stream->ReadByte();", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures getter-bodied instance properties also lower to getter calls instead of native storage fields.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGetterBodiedPropertyReceiver_UsesGetterCall() {
            string source = """
                public class RenderQueue {
                    public int Count() {
                        return 1;
                    }
                }

                public class Camera {
                    RenderQueue renderQueue;

                    public RenderQueue RenderQueue {
                        get {
                            return renderQueue;
                        }
                    }

                    public int Read() {
                        return RenderQueue.Count();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Camera.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Camera.cpp"));

            Assert.Contains("::RenderQueue* get_RenderQueue();", headerOutput);
            Assert.DoesNotContain("\n    ::RenderQueue* RenderQueue;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("::RenderQueue* Camera::get_RenderQueue()", sourceOutput);
            Assert.Contains("return this->get_RenderQueue()->Count();", sourceOutput);
            Assert.DoesNotContain("return this->RenderQueue->Count();", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures accessor-bodied properties preserve their getter implementation instead of falling back to unsupported native stubs.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAccessorExpressionBodiedProperty_EmitsGetterBody() {
            string source = """
                public class Registration {
                    string processorIdValue;

                    public Registration(string processorId) {
                        processorIdValue = processorId;
                    }

                    public string ProcessorId {
                        get => processorIdValue;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Registration.cpp"));

            Assert.Contains("const std::string& Registration::get_ProcessorId()", sourceOutput);
            Assert.Contains("return this->processorIdValue;", sourceOutput);
            Assert.DoesNotContain("Method has no generated body.", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures inherited computed properties still lower to getter calls instead of colliding with generated type names.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInheritedPropertyReceiver_UsesGetterCall() {
            string source = """
                public class Stream {
                    public void Write(byte[] buffer) {
                    }
                }

                public class WriterBase {
                    protected Stream BaseStream;

                    protected Stream Stream => BaseStream;
                }

                public class Writer : WriterBase {
                    public void WriteBuffer(byte[] buffer) {
                        Stream.Write(buffer);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Writer.cpp"));

            Assert.Contains("this->get_Stream()->Write(buffer);", sourceOutput);
            Assert.DoesNotContain("Stream::Write(buffer);", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures computed property member access lowers through generated getter and setter calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithComputedPropertyMemberAccess_UsesGetterAndSetterCalls() {
            string source = """
                public struct float3 {
                    public float X;
                    public float Y;
                    public float Z;

                    public float3(float x, float y, float z) {
                        X = x;
                        Y = y;
                        Z = z;
                    }
                }

                public class Entity {
                    float3 position;
                    bool enabled;

                    public bool Enabled {
                        get {
                            return enabled;
                        }
                        set {
                            enabled = value;
                        }
                    }

                    public float3 Position {
                        get {
                            return position;
                        }
                        set {
                            position = value;
                        }
                    }
                }

                public class Widget {
                    Entity target;

                    public void Sync(float3 value) {
                        if (target.Enabled) {
                            target.Position = value;
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("if (this->target->get_Enabled())", sourceOutput);
            Assert.Contains("this->target->set_Position(value);", sourceOutput);
            Assert.DoesNotContain("this->target->Enabled", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->target->Position =", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures inherited value-type property bridges preserve direct-value setter signatures.
        /// </summary>
        [Fact]
        public void EmitInheritedPropertyBridge_WithValueType_UsesDirectValueSetterSignature() {
            ConversionOutput output = RunConversion("""
                public struct float3 {
                    public float X;
                }

                public class EntityBase {
                    float3 orientation;

                    public float3 LocalOrientation {
                        get {
                            return orientation;
                        }
                        set {
                            orientation = value;
                        }
                    }
                }

                public class Entity : EntityBase {
                }
                """);

            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Entity.hpp"));

            Assert.Contains("::float3 get_LocalOrientation();", headerOutput);
            Assert.Contains("void set_LocalOrientation(::float3 value);", headerOutput);
            Assert.DoesNotContain("set_LocalOrientation(::float3* value)", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures value-type properties, static value members, and mixed receiver shapes emit the correct native access operators.
        /// </summary>
        [Fact]
        public void WriteOutput_WithValueTypePropertyAndStaticAccessChain_UsesConsistentNativeShapes() {
            string source = """
                public struct float3 {
                    public float X;
                    public float Y;
                    public float Z;

                    public bool Equals(float3 other) {
                        return true;
                    }

                    public static float3 Zero {
                        get {
                            return new float3();
                        }
                    }

                    public static float3 Normalize(float3 value) {
                        return value;
                    }
                }

                public struct float4 {
                    public static float4 CreateFromAxisAngle(float3 axis, float angle) {
                        return new float4();
                    }
                }

                public class Core {
                    static Core instance;

                    public static Core Instance {
                        get {
                            return instance;
                        }
                    }

                    public float DeltaTime {
                        get {
                            return 1.0f;
                        }
                    }
                }

                public class Entity {
                    float4 localOrientation;

                    public float4 LocalOrientation {
                        get {
                            return localOrientation;
                        }
                        set {
                            localOrientation = value;
                        }
                    }
                }

                public class AxisRotationComponent {
                    public float3 Axis { get; set; }
                    public Entity Parent { get; set; }
                    public float AngularSpeedRadiansPerSecond { get; set; }

                    public void Update() {
                        if (Axis.Equals(float3.Zero)) {
                            return;
                        }

                        float3 normalizedAxis = float3.Normalize(Axis);
                        float deltaAngleRadians = AngularSpeedRadiansPerSecond * Core.Instance.DeltaTime;
                        float4 deltaRotation = float4.CreateFromAxisAngle(normalizedAxis, deltaAngleRadians);
                        Parent.LocalOrientation = deltaRotation;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "AxisRotationComponent.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "AxisRotationComponent.cpp"));

            Assert.Contains("::float3 Axis;", headerOutput);
            Assert.Contains("void set_Axis(::float3 value);", headerOutput);
            Assert.Contains("if (this->Axis.Equals(float3::get_Zero()))", sourceOutput);
            Assert.Contains("::float3 normalizedAxis = float3::Normalize(this->Axis);", sourceOutput);
            Assert.Contains("const float deltaAngleRadians = this->AngularSpeedRadiansPerSecond * Core::get_Instance()->get_DeltaTime();", sourceOutput);
            Assert.Contains("::float4 deltaRotation = float4::CreateFromAxisAngle(normalizedAxis, deltaAngleRadians);", sourceOutput);
            Assert.Contains("this->Parent->set_LocalOrientation(deltaRotation);", sourceOutput);
            Assert.DoesNotContain("::float3->", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Core->Instance", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Parent->LocalOrientation", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures value-type constructor temporaries lower as direct values instead of heap allocations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithValueTypeConstructorTemporary_UsesDirectValueConstruction() {
            string source = """
                public struct float3 {
                    public float X;
                    public float Y;
                    public float Z;

                    public float3(float x, float y, float z) {
                        X = x;
                        Y = y;
                        Z = z;
                    }

                    public static float3 Normalize(float3 value) {
                        return value;
                    }
                }

                public class AxisTestCameraForwardSpinComponent {
                    public float CameraForwardAxisX { get; set; }
                    public float CameraForwardAxisY { get; set; }
                    public float CameraForwardAxisZ { get; set; }

                    public void Update() {
                        float3 axis = float3.Normalize(new float3(CameraForwardAxisX, CameraForwardAxisY, CameraForwardAxisZ));
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "AxisTestCameraForwardSpinComponent.cpp"));

            Assert.Contains("::float3 axis = float3::Normalize(::float3(this->CameraForwardAxisX, this->CameraForwardAxisY, this->CameraForwardAxisZ));", sourceOutput);
            Assert.DoesNotContain("Normalize(new float3(", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Normalize(::float3* ", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures managed event null-guards and invocations lower through the native event placeholder instead of pointer-style calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedEventInvocation_UsesNativeEventValueSemantics() {
            string source = """
                using System;

                public class Widget {
                    public event Action Hovered;

                    public void RaiseHovered() {
                        if (Hovered != null) {
                            Hovered();
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("Event Hovered;", headerOutput);
            Assert.Contains("Hovered.Invoke();", sourceOutput);
            Assert.DoesNotContain("Hovered != nullptr", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("(*Hovered)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures System.Diagnostics.Debug resolves to the native diagnostics runtime header instead of a synthetic generated include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemDiagnosticsDebug_UsesRuntimeDebugHeader() {
            string source = """
                public static class Widget {
                    public static void Log(string text) {
                        System.Diagnostics.Debug.WriteLine(text);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string generatedText = output.GeneratedText;

            Assert.Contains("#include \"system/diagnostics/debug.hpp\"", generatedText);
            Assert.DoesNotContain("#include \"Debug.hpp\"", generatedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures System.Diagnostics.Stopwatch resolves to the native stopwatch runtime header instead of a synthetic generated include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemDiagnosticsStopwatch_UsesRuntimeStopwatchHeader() {
            string source = """
                public static class Widget {
                    public static double Measure() {
                        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                        stopwatch.Stop();
                        return stopwatch.Elapsed.TotalMilliseconds;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string generatedText = output.GeneratedText;

            Assert.Contains("#include \"system/diagnostics/stopwatch.hpp\"", generatedText);
            Assert.DoesNotContain("#include \"Stopwatch.hpp\"", generatedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nullable member access lowers through direct value access instead of pointer access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNullableValueMemberAccess_UsesDirectAccess() {
            string source = """
                public class Widget {
                    public float? Distance;

                    public bool HasDistance() {
                        return Distance.HasValue && Distance.Value > 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("Distance.get_HasValue()", sourceOutput);
            Assert.Contains("Distance.get_Value() > 0", sourceOutput);
            Assert.DoesNotContain("Distance->get_HasValue()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Distance->get_Value()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures primitive values retrieved from nullable wrappers lower ToString calls through native formatting instead of pointer-style member invocation.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNullablePrimitiveValueToString_UsesNativeFormatting() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    class AnchorData {
                        public float? LeftDistance { get; set; }
                    }

                    AnchorData anchorData;

                    public string Describe() {
                        var values = new List<string>();
                        if (anchorData.LeftDistance.HasValue) {
                            values.Add(anchorData.LeftDistance.Value.ToString());
                        }

                        return string.Join(", ", values);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("std::to_string(", sourceOutput);
            Assert.DoesNotContain("anchorData->LeftDistance.Value->ToString()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nullable primitive values inside string concatenation still lower through native formatting instead of pointer-style member invocation.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNullablePrimitiveValueToStringInsideConcatenation_UsesNativeFormatting() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    class AnchorData {
                        public float? LeftDistance { get; set; }
                    }

                    AnchorData anchorData;

                    public string Describe() {
                        var values = new List<string>();
                        if (anchorData.LeftDistance.HasValue) {
                            values.Add("Left (" + anchorData.LeftDistance.Value.ToString() + "px)");
                        }

                        return string.Join(", ", values);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("std::to_string(", sourceOutput);
            Assert.DoesNotContain("anchorData.LeftDistance.Value->ToString()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("anchorData->LeftDistance.Value->ToString()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nullable primitive values inside interpolated strings lower through native formatting instead of pointer-style ToString calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNullablePrimitiveValueInsideInterpolatedString_UsesNativeFormatting() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    class AnchorData {
                        public float? LeftDistance { get; set; }
                    }

                    AnchorData anchorData;

                    public string Describe() {
                        var values = new List<string>();
                        if (anchorData.LeftDistance.HasValue) {
                            values.Add($"Left ({anchorData.LeftDistance.Value:F1}px)");
                        }

                        return string.Join(", ", values);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("std::to_string(", sourceOutput);
            Assert.DoesNotContain("anchorData.LeftDistance.Value->ToString()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("anchorData->LeftDistance.Value->ToString()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nullable-target ternaries wrap scalar and null branches in the native nullable runtime type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNullableConditional_EmitsNativeNullableBranches() {
            string source = """
                public class Widget {
                    public float? Pick(bool useValue, float value) {
                        return useValue ? value : null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("return useValue ? Nullable<float>(value) : Nullable<float>(nullptr);", sourceOutput);
        }

        /// <summary>
        /// Ensures nullable properties assigned through object-initializer ternaries wrap scalar and null branches in the native nullable runtime type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNullableObjectInitializerConditional_EmitsNativeNullableBranches() {
            string source = """
                public class AnchorData {
                    public float? LeftDistance { get; set; }
                }

                public class Widget {
                    AnchorData anchorData;

                    public void SetAnchor(bool left, float value) {
                        anchorData = new AnchorData {
                            LeftDistance = left ? value : null
                        };
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("__object_00000000->set_LeftDistance(left ? Nullable<float>(value) : Nullable<float>(nullptr));", sourceOutput);
            Assert.DoesNotContain("LeftDistance = left ? value : nullptr;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nullable member access on nested objects keeps direct nullable access instead of pointer access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedNullableValueMemberAccess_UsesDirectAccess() {
            string source = """
                public class AnchorData {
                    public float? LeftDistance { get; set; }
                }

                public class Widget {
                    AnchorData anchorData;

                    public bool HasLeftDistance() {
                        return anchorData.LeftDistance.HasValue && anchorData.LeftDistance.Value > 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("this->anchorData->get_LeftDistance().get_HasValue()", sourceOutput);
            Assert.Contains("this->anchorData->get_LeftDistance().get_Value() > 0", sourceOutput);
            Assert.DoesNotContain("anchorData->LeftDistance->get_HasValue()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("anchorData->LeftDistance->get_Value()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures chained nullable access through an explicit this-reference and nullable object-initializer assignments
        /// keep native nullable value semantics instead of leaking pointer access or raw nullptr ternaries.
        /// </summary>
        [Fact]
        public void WriteOutput_WithThisQualifiedNullableMembersAndObjectInitializer_UsesNativeNullableValueSemantics() {
            string source = """
                public class float3 {
                    public float X;
                }

                public class ParentNode {
                    public float3 Position;
                }

                public class AnchorData {
                    public float? LeftDistance { get; set; }
                }

                public class Widget {
                    ParentNode anchorParent;
                    AnchorData anchorData;

                    public void Enable(bool left) {
                        anchorData = new AnchorData {
                            LeftDistance = left ? anchorParent.Position.X : null
                        };
                    }

                    public bool HasLeftDistance() {
                        return this.anchorData.LeftDistance.HasValue && this.anchorData.LeftDistance.Value > 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("__object_00000000->set_LeftDistance(left ? Nullable<float>(anchorParent->Position->X) : Nullable<float>(nullptr));", sourceOutput);
            Assert.Contains("this->anchorData->get_LeftDistance().get_HasValue()", sourceOutput);
            Assert.Contains("this->anchorData->get_LeftDistance().get_Value() > 0", sourceOutput);
            Assert.DoesNotContain("LeftDistance = left ? anchorParent->Position->X : nullptr;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->anchorData->LeftDistance->get_HasValue()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->anchorData->LeftDistance->get_Value()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nested helper classes with nullable members preserve nullable value semantics across object initializers
        /// and chained member access, matching the real AnchorComponent shape.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedAnchorDataNullableMembers_UsesNativeNullableValueSemantics() {
            string source = """
                using System.Collections.Generic;

                public class float3 {
                    public float X;
                    public float Y;
                }

                public class ParentNode {
                    public float3 Position;
                }

                public class int2 {
                    public int X;
                    public int Y;
                }

                public class RenderManager {
                    public int2 MainWindowSize;
                }

                public class CoreLike {
                    public static CoreLike Instance;
                    public RenderManager RenderManager3D;
                }

                public class Widget {
                    ParentNode Parent;
                    AnchorData anchorData;

                    public void Enable(bool left, bool right, bool top, bool bottom) {
                        var windowSize = CoreLike.Instance.RenderManager3D.MainWindowSize;
                        anchorData = new AnchorData {
                            LeftDistance = left ? Parent.Position.X : null,
                            RightDistance = right ? windowSize.X - Parent.Position.X : null,
                            TopDistance = top ? Parent.Position.Y : null,
                            BottomDistance = bottom ? windowSize.Y - Parent.Position.Y : null
                        };
                    }

                    public bool HasLeftDistance() {
                        var anchors = new List<string>();
                        return anchorData.LeftDistance.HasValue && anchorData.LeftDistance.Value > 0;
                    }

                    private class AnchorData {
                        public Nullable<float> LeftDistance { get; set; }
                        public Nullable<float> RightDistance { get; set; }
                        public Nullable<float> TopDistance { get; set; }
                        public Nullable<float> BottomDistance { get; set; }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.True(sourceOutput.Contains("__object_00000000->set_LeftDistance(left ? Nullable<float>(Parent->Position->X) : Nullable<float>(nullptr));", StringComparison.Ordinal), sourceOutput);
            Assert.True(sourceOutput.Contains("__object_00000000->set_RightDistance(right ? Nullable<float>(windowSize->X - Parent->Position->X) : Nullable<float>(nullptr));", StringComparison.Ordinal), sourceOutput);
            Assert.True(sourceOutput.Contains("this->anchorData->get_LeftDistance().get_HasValue()", StringComparison.Ordinal), sourceOutput);
            Assert.True(sourceOutput.Contains("this->anchorData->get_LeftDistance().get_Value() > 0", StringComparison.Ordinal), sourceOutput);
            Assert.DoesNotContain("LeftDistance = left ? Parent->Position->X : nullptr;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("RightDistance = right ? windowSize->X - Parent->Position->X : nullptr;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("anchorData->LeftDistance->get_HasValue()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("anchorData->LeftDistance->get_Value()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures string inference and string.Join lower through the native string runtime helpers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringJoinUsage_UsesNativeStringRuntimeHelpers() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    public string Describe() {
                        var info = "Anchored to: ";
                        var values = new List<string>();
                        return info + string.Join(", ", values);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("std::string info = \"Anchored to: \";", sourceOutput);
            Assert.Contains("return String::Concat(info, String::JoinArray(\", \", values->ToArray()));", sourceOutput);
            Assert.DoesNotContain("const std::string info[]", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Boolean::Join", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures runtime class types can still be qualified when a member name collides with the runtime type identifier.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRuntimeTypeMemberNameCollision_QualifiesRuntimeTypeReferences() {
            string source = """
                using System.IO;

                public class StreamGate {
                    public Stream Stream;

                    public void Set(Stream stream) {
                        Stream = stream;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "StreamGate.hpp"));

            Assert.Contains("::Stream* Stream;", headerOutput);
            Assert.Contains("void Set(::Stream* stream);", headerOutput);
            Assert.DoesNotContain("\n    Stream* Stream;", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures derived constructors emit explicit base-constructor initializer chaining.
        /// </summary>
        [Fact]
        public void WriteOutput_WithBaseConstructorInitializer_EmitsCppInitializerList() {
            string source = """
                public class BaseGate {
                    protected BaseGate(int value) {
                    }
                }

                public class DerivedGate : BaseGate {
                    public DerivedGate(int value) : base(value) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "DerivedGate.cpp"));

            Assert.Contains("DerivedGate::DerivedGate(int32_t value)", sourceOutput);
            Assert.Contains(": BaseGate(value)", sourceOutput);
        }

        /// <summary>
        /// Ensures nameof arguments lower to stable string literals inside exception construction.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNameofInExceptionCtor_EmitsStringLiteral() {
            string source = """
                using System;

                public class GuardGate {
                    public void Validate(object value) {
                        if (value == null) {
                            throw new ArgumentNullException(nameof(value));
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "GuardGate.cpp"));

            Assert.Contains("throw new ArgumentNullException(\"value\");", sourceOutput);
            Assert.DoesNotContain("nameof(value)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nameof arguments lower to stable string literals inside generic class methods too.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNameofInGenericExceptionCtor_EmitsStringLiteral() {
            string source = """
                using System;

                public class GuardGate<T> {
                    public T Read(object stream) {
                        if (stream == null) {
                            throw new ArgumentNullException(nameof(stream));
                        }

                        return default;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "GuardGate_1.cpp"));

            Assert.Contains("throw new ArgumentNullException(\"stream\");", sourceOutput);
            Assert.DoesNotContain("nameof(stream)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures static self-calls inside a static class do not emit instance access and preserve normal overloaded C++ calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStaticSelfCallsAndOverloads_DoesNotEmitThisOrNumericRemapCalls() {
            string source = """
                public static class AssetSerializer {
                    public static int Deserialize(string stream) {
                        return 1;
                    }

                    public static int Deserialize(string stream, int header) {
                        return Deserialize(stream);
                    }

                    public static int DeserializeFromBytes(string data) {
                        return Deserialize(data, 1);
                    }

                    public static int SerializeToBytes(string data) {
                        return DeserializeFromBytes(data);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "AssetSerializer.cpp"));

            Assert.DoesNotContain("this->", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Deserialize2", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("return Deserialize(stream);", sourceOutput);
            Assert.Contains("return Deserialize(data, 1);", sourceOutput);
            Assert.Contains("return DeserializeFromBytes(data);", sourceOutput);
        }

        /// <summary>
        /// Ensures managed MemoryStream constructions lower to the runtime MemoryStream type instead of leaking source namespaces.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMemoryStreamConstruction_UsesRuntimeMemoryStreamType() {
            string source = """
                using System.IO;

                public static class AssetSerializer {
                    public static Stream FromBytes(byte[] data) {
                        return new MemoryStream(data, false);
                    }

                    public static Stream Empty() {
                        return new MemoryStream();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "AssetSerializer.cpp"));

            Assert.Contains("new MemoryStream(data, false)", sourceOutput);
            Assert.Contains("new MemoryStream()", sourceOutput);
            Assert.DoesNotContain("new System.IO.MemoryStream", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures using declarations that construct MemoryStream still lower to the runtime MemoryStream type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUsingDeclarationMemoryStreamConstruction_UsesRuntimeMemoryStreamType() {
            string source = """
                using System.IO;

                public static class AssetSerializer {
                    public static Stream FromBytes(byte[] data) {
                        using var stream = new MemoryStream(data, false);
                        return stream;
                    }

                    public static byte[] EmptyBytes() {
                        using var stream = new MemoryStream();
                        return stream.ToArray();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "AssetSerializer.cpp"));

            Assert.Contains("MemoryStream *stream = new MemoryStream(data, false);", sourceOutput);
            Assert.Contains("MemoryStream *stream = new MemoryStream();", sourceOutput);
            Assert.DoesNotContain("new System.IO.MemoryStream", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures fully-qualified MemoryStream constructions still lower through the runtime type mapping.
        /// </summary>
        [Fact]
        public void WriteOutput_WithQualifiedMemoryStreamConstruction_UsesRuntimeMemoryStreamType() {
            string source = """
                using System.IO;

                public class StreamGate {
                    public Stream Open(byte[] data) {
                        return new System.IO.MemoryStream(data, false);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "StreamGate.cpp"));

            Assert.Contains("return new MemoryStream(data, false);", sourceOutput);
            Assert.DoesNotContain("System.IO.MemoryStream", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated generic element types drop source namespaces when rendered inside native collection constructions.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGeneratedGenericPointerConstruction_NormalizesQualifiedLeafTypeNames() {
            string source = """
                using System.Collections.Generic;

                namespace helengine {
                    public class ComboBoxItemVisual {
                    }

                    public class ComboBoxGate {
                        public List<ComboBoxItemVisual> Build(int count) {
                            return new List<ComboBoxItemVisual>(count);
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ComboBoxGate.cpp"));

            Assert.Contains("return new List<::ComboBoxItemVisual*>(count);", sourceOutput);
            Assert.DoesNotContain("helengine.ComboBoxItemVisual", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures interface auto-properties emit accessor declarations instead of invalid storage members.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInterfaceAutoProperties_EmitsAccessors() {
            string source = """
                public interface ICamera {
                    ushort LayerMask { get; set; }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "ICamera.hpp"));

            Assert.Contains("public:", header);
            Assert.Contains("virtual uint16_t get_LayerMask() = 0;", header);
            Assert.Contains("virtual void set_LayerMask(uint16_t value) = 0;", header);
            Assert.DoesNotContain("uint16_t LayerMask;", header, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures class auto-properties still emit backing storage while also generating accessors that satisfy implemented interfaces.
        /// </summary>
        [Fact]
        public void WriteOutput_WithClassAutoPropertyImplementingInterface_EmitsStorageAndAccessors() {
            string source = """
                public interface ICamera {
                    ushort LayerMask { get; set; }
                }

                public class Camera : ICamera {
                    public ushort LayerMask { get; set; }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "Camera.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Camera.cpp"));

            Assert.Contains("uint16_t LayerMask;", header);
            Assert.Contains("uint16_t get_LayerMask();", header);
            Assert.Contains("void set_LayerMask(uint16_t value);", header);
            Assert.Contains("uint16_t Camera::get_LayerMask()", sourceOutput);
            Assert.Contains("return this->LayerMask;", sourceOutput);
            Assert.Contains("void Camera::set_LayerMask(uint16_t value)", sourceOutput);
            Assert.Contains("this->LayerMask = value;", sourceOutput);
        }

        /// <summary>
        /// Ensures abstract class properties stay virtual accessors so base-class methods dispatch to derived overrides instead of reading synthetic storage.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAbstractPropertyReadFromBaseMethod_EmitsPureVirtualGetterWithoutBackingField() {
            string source = """
                public abstract class TypeProcessor {
                    protected abstract int InternalBodiesPerConstraint { get; }

                    protected int bodiesPerConstraint;

                    public void Initialize() {
                        bodiesPerConstraint = InternalBodiesPerConstraint;
                    }
                }

                public class TwoBodyTypeProcessor : TypeProcessor {
                    protected override int InternalBodiesPerConstraint => 2;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string baseHeader = File.ReadAllText(Path.Combine(output.OutputPath, "TypeProcessor.hpp"));
            string baseSource = File.ReadAllText(Path.Combine(output.OutputPath, "TypeProcessor.cpp"));
            string derivedSource = File.ReadAllText(Path.Combine(output.OutputPath, "TwoBodyTypeProcessor.cpp"));

            Assert.Contains("virtual int32_t get_InternalBodiesPerConstraint() = 0;", baseHeader);
            Assert.DoesNotContain("int32_t InternalBodiesPerConstraint;", baseHeader, StringComparison.Ordinal);
            Assert.Contains("this->bodiesPerConstraint = this->get_InternalBodiesPerConstraint();", baseSource);
            Assert.Contains("return 2;", derivedSource);
            Assert.DoesNotContain("TypeProcessor::get_InternalBodiesPerConstraint()", baseSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures concrete classes emit interface-property bridges when the implementation lives on another base class.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInterfacePropertyImplementedByDifferentBase_EmitsBridgeAccessor() {
            string source = """
                public class Entity {
                }

                public interface IDrawable {
                    Entity Parent { get; }
                }

                public class Component {
                    public Entity Parent { get; set; }
                }

                public class Sprite : Component, IDrawable {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "Sprite.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Sprite.cpp"));

            Assert.Contains("::Entity* get_Parent();", header);
            Assert.Contains("::Entity* Sprite::get_Parent()", sourceOutput);
            Assert.Contains("return this->Component::get_Parent();", sourceOutput);
        }

        /// <summary>
        /// Ensures abstract intermediate classes do not emit inherited base-property bridge accessors that forward back into unresolved abstract base getters.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAbstractIntermediateClass_DoesNotEmitInheritedAbstractPropertyBridge() {
            string source = """
                public abstract class BaseGate {
                    public abstract int Value { get; }
                }

                public abstract class IntermediateGate : BaseGate {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "IntermediateGate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "IntermediateGate.cpp"));

            Assert.DoesNotContain("get_Value()", header, StringComparison.Ordinal);
            Assert.DoesNotContain("get_Value()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("BaseGate::get_Value()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures interface-property bridges also emit for transitive interface hierarchies.
        /// </summary>
        [Fact]
        public void WriteOutput_WithTransitiveInterfacePropertyImplementation_EmitsBridgeAccessor() {
            string source = """
                public class Entity {
                }

                public interface IDrawable {
                    Entity Parent { get; }
                }

                public interface IRoundedRectDrawable : IDrawable {
                }

                public class Component {
                    public Entity Parent { get; set; }
                }

                public class RoundedRectComponent : Component, IRoundedRectDrawable {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "RoundedRectComponent.hpp"));

            Assert.Contains("::Entity* get_Parent();", header);
        }

        /// <summary>
        /// Ensures reads through interface-typed receivers lower to accessor calls instead of invalid field access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInterfaceTypedPropertyReceiver_UsesGetterCall() {
            string source = """
                public struct float4 {
                    public float X;
                }

                public interface ICamera {
                    float4 Viewport { get; }
                }

                public class Widget {
                    public float Read(ICamera camera) {
                        return camera.Viewport.X;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("return camera->get_Viewport().X;", sourceOutput);
            Assert.DoesNotContain("camera->Viewport", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures interface properties on indexed receivers lower through getter calls for both inferred locals and direct element access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithIndexedInterfacePropertyReceiver_UsesGetterCalls() {
            string source = """
                using System.Collections.Generic;

                public struct float4 {
                    public float X;
                }

                public interface ICamera {
                    float4 Viewport { get; }
                    byte CameraDrawOrder { get; }
                }

                public class Widget {
                    public float Read(List<ICamera> cameras, int index) {
                        var camera = cameras[index];
                        var viewport = camera.Viewport;
                        if (cameras[index].CameraDrawOrder > 0) {
                            return viewport.X + cameras[index].Viewport.X;
                        }

                        return viewport.X;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("::ICamera *camera = (*cameras)[index];", sourceOutput);
            Assert.Contains("::float4 viewport = camera->get_Viewport();", sourceOutput);
            Assert.Contains("(*cameras)[index]->get_CameraDrawOrder()", sourceOutput);
            Assert.Contains("(*cameras)[index]->get_Viewport().X", sourceOutput);
            Assert.DoesNotContain("camera->Viewport", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("->CameraDrawOrder", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures string-to-null comparisons lower through the native string helper instead of invalid pointer-style comparisons.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringNullComparison_UsesNativeStringHelper() {
            string source = """
                using System.Collections.Generic;

                public class StringGate {
                    public bool HasNull(List<string> items, int index) {
                        return items[index] == null;
                    }

                    public bool HasValue(List<string> items, int index) {
                        return items[index] != null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "StringGate.cpp"));

            Assert.Contains("return String::IsNullOrEmpty((*items)[index]);", sourceOutput);
            Assert.Contains("return !String::IsNullOrEmpty((*items)[index]);", sourceOutput);
            Assert.DoesNotContain("== nullptr", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("!= nullptr", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures Type.Name remains string-valued inside interpolated strings instead of lowering through an invalid ToString member chain.
        /// </summary>
        [Fact]
        public void WriteOutput_WithTypeNameInterpolation_UsesNativeTypeNameStringValue() {
            string source = """
                using System;

                public class TypeGate {
                    public string Describe(Type value) {
                        return $"Type '{value.Name}'";
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "TypeGate.cpp"));

            Assert.Contains("value->get_Name()", sourceOutput);
            Assert.DoesNotContain("value->Name->ToString()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures target-typed collection expressions passed to IReadOnlyList parameters lower to a native list allocation instead of raw bracket syntax.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCollectionExpressionArgumentForList_UsesNativeListConstruction() {
            string source = """
                using System.Collections.Generic;

                public class ContentManager {
                    const string WildcardExtension = "*";

                    public void RegisterBuiltInProcessors() {
                        RegisterProcessor([WildcardExtension]);
                    }

                    void RegisterProcessor(IReadOnlyList<string> extensions) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.cpp"));

            Assert.Contains("this->RegisterProcessor(new List<std::string>({ WildcardExtension }))", sourceOutput);
            Assert.DoesNotContain("[WildcardExtension]", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures collection expressions passed to generic method parameters still lower using the resolved parameter target type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCollectionExpressionArgumentForGenericMethodList_UsesNativeListConstruction() {
            string source = """
                using System.Collections.Generic;

                public interface IContentProcessor<T> {
                }

                public class TextContent {
                }

                public class TextContentProcessor : IContentProcessor<TextContent> {
                }

                public class ContentManager {
                    const string WildcardExtension = "*";

                    public void RegisterBuiltInProcessors() {
                        RegisterProcessor(TextContentProcessorId, new TextContentProcessor(), [WildcardExtension]);
                    }

                    const string TextContentProcessorId = "text";

                    void RegisterProcessor<T>(string processorId, IContentProcessor<T> processor, IReadOnlyList<string> extensions) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.cpp"));

            Assert.Contains("RegisterProcessor<TextContent*>(TextContentProcessorId, new ::TextContentProcessor(), new List<std::string>({ WildcardExtension }))", sourceOutput);
            Assert.DoesNotContain("[WildcardExtension]", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures concrete object creation stays on the explicitly constructed generated class even when the invocation expects a generic interface type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithConcreteGeneratedObjectCreationPassedToGenericInterfaceCall_UsesConcreteClassName() {
            string source = """
                public interface IContentProcessor<T> {
                }

                public class TextContent {
                }

                public class TextContentProcessor : IContentProcessor<TextContent> {
                }

                public class ContentManager {
                    void RegisterProcessor<T>(IContentProcessor<T> processor) {
                    }

                    public void RegisterBuiltInProcessors() {
                        RegisterProcessor(new TextContentProcessor());
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.cpp"));

            Assert.Contains("RegisterProcessor<TextContent*>(new ::TextContentProcessor())", sourceOutput);
            Assert.DoesNotContain("new IContentProcessor_1<TextContent>()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures concrete classes implementing generic interfaces inherit the generated generic interface specialization in emitted headers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericInterfaceImplementation_EmitsGenericInheritanceClause() {
            string source = """
                public interface IContentProcessor {
                }

                public interface IContentProcessor<T> : IContentProcessor {
                }

                public class TextContent {
                }

                public class TextContentProcessor : IContentProcessor<TextContent> {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "TextContentProcessor.hpp"));

            Assert.Contains("class TextContentProcessor : public IContentProcessor_1<::TextContent*>", headerOutput);
            Assert.Contains("#include \"IContentProcessor_1.hpp\"", headerOutput);
            Assert.Contains("#include \"TextContent.hpp\"", headerOutput);
            Assert.DoesNotContain("class TextContentProcessor : public IContentProcessor\n", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated value types do not inherit native interface runtime contracts that would add polymorphic layout.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericEquatableStruct_DoesNotInheritRuntimeContract() {
            string source = """
                public struct float3 : System.IEquatable<float3> {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "float3.hpp"));

            Assert.DoesNotContain("IEquatable<::float3>", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated struct-like value types keep direct member access and stack-style construction instead of leaking pointer semantics.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGeneratedStructValueMembers_UsesDirectAccessAndStackConstruction() {
            string source = """
                public struct float3 {
                    public float X;
                    public float Y;
                    public float Z;

                    public float3(float x, float y, float z) {
                        X = x;
                        Y = y;
                        Z = z;
                    }
                }

                public struct int2 {
                    public int X;
                    public int Y;

                    public int2(int x, int y) {
                        X = x;
                        Y = y;
                    }
                }

                public struct byte4 {
                    public byte X;
                    public byte Y;
                    public byte Z;
                    public byte W;

                    public byte4(byte x, byte y, byte z, byte w) {
                        X = x;
                        Y = y;
                        Z = z;
                        W = w;
                    }
                }

                public class ThemeColors {
                    public byte4 TextOnAccent;
                }

                public static class ThemeManager {
                    public static ThemeColors Colors;
                }

                public class RenderManager {
                    public int2 MainWindowSize;
                }

                public class CoreLike {
                    public static CoreLike Instance;
                    public RenderManager RenderManager3D;
                }

                public class Entity {
                    public float3 Position;
                }

                public class Widget {
                    Entity Parent;
                    byte4 ButtonTextColor;

                    public void Init() {
                        var windowSize = CoreLike.Instance.RenderManager3D.MainWindowSize;
                        var pos = Parent.Position;
                        pos.X = windowSize.X;
                        ButtonTextColor = ThemeManager.Colors.TextOnAccent;
                        Parent.Position = new float3(pos.X, pos.Y, 0.1f);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("::int2 windowSize = CoreLike::Instance->RenderManager3D->MainWindowSize;", sourceOutput);
            Assert.Contains("::float3 pos = this->Parent->Position;", sourceOutput);
            Assert.Contains("pos.X = windowSize.X;", sourceOutput);
            Assert.Contains("this->ButtonTextColor = ThemeManager::Colors->TextOnAccent;", sourceOutput);
            Assert.Contains("this->Parent->Position = ::float3(pos.X, pos.Y, 0.1f);", sourceOutput);
            Assert.DoesNotContain("::int2 *windowSize", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("windowSize->X", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("pos->X", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->Parent->Position = new ::float3", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated C# structs expose the implicit parameterless constructor needed for default value initialization in consuming types.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGeneratedStructWithoutParameterlessConstructor_EmitsImplicitDefaultConstructor() {
            string source = """
                public struct int2 {
                    public int X;
                    public int Y;

                    public int2(int x, int y) {
                        X = x;
                        Y = y;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "int2.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "int2.cpp"));

            Assert.Contains("int2();", headerOutput);
            Assert.Contains("int2::int2() : X(0), Y(0)", sourceOutput);
        }

        /// <summary>
        /// Ensures System.Runtime.CompilerServices.Unsafe intrinsics lower to native helper calls instead of unresolved managed helper includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCompilerServicesUnsafeIntrinsics_UsesNativeUnsafeHelpers() {
            string source = """
                using System.Runtime.CompilerServices;

                public struct float2 {
                    public float X;
                    public float Y;
                }

                public struct float4 {
                    public float X;
                    public float Y;
                    public float Z;
                    public float W;
                }

                public class UnsafeGate {
                    public float Read(float4 value) {
                        return Unsafe.As<float4, float2>(ref value).X;
                    }

                    public int Size() {
                        return Unsafe.SizeOf<float4>();
                    }

                    public void Reset(byte[] bytes, int index, int count) {
                        Unsafe.SkipInit(out float4 scratch);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "UnsafeGate.cpp"));
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "UnsafeGate.hpp"));
            string unsafeShimOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Unsafe.hpp"));

            Assert.Contains("#include \"runtime/native_unsafe.hpp\"", sourceOutput);
            Assert.Contains("#include \"Unsafe.hpp\"", sourceOutput);
            Assert.Contains("#include \"Unsafe.hpp\"", headerOutput);
            Assert.Contains("#include \"runtime/native_unsafe.hpp\"", unsafeShimOutput);
            Assert.Contains("return he_cpp_unsafe_as<::float2>(&(value)).X;", sourceOutput);
            Assert.Contains("return he_cpp_unsafe_size_of<::float4>();", sourceOutput);
            Assert.Contains("(void)0;", sourceOutput);
        }

        /// <summary>
        /// Ensures ref locals initialized from ref-return invocations lower to native references instead of empty placeholder initializers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefReturnLocalDeclaration_EmitsNativeReferenceBinding() {
            string source = """
                public struct Slot {
                    public int Value;
                }

                public struct Container {
                    public Slot Slot;
                }

                public static class Access {
                    public static ref Slot GetSlot(ref Container container) {
                        return ref container.Slot;
                    }
                }

                public class RefReturnFixture {
                    public int Read(ref Container container) {
                        ref var slot = ref Access.GetSlot(ref container);
                        slot.Value = 7;
                        return slot.Value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RefReturnFixture.cpp"));

            Assert.DoesNotContain("object *slot = ;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("auto& slot = Access::GetSlot__ref0(container);", sourceOutput);
            Assert.Contains("slot.Value = 7;", sourceOutput);
            Assert.Contains("return slot.Value;", sourceOutput);
        }

        /// <summary>
        /// Ensures ref-return methods emit native reference signatures instead of degrading to object pointers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefReturnMethod_EmitsReferenceSignature() {
            string source = """
                public struct Slot {
                    public int Value;
                }

                public struct Container {
                    public Slot Slot;
                }

                public static class Access {
                    public static ref Slot GetSlot(ref Container container) {
                        return ref container.Slot;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Access.hpp"));

            Assert.Contains("static ::Slot& GetSlot(::Container& container);", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("object*", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures ref-return properties emit native reference getter signatures instead of degrading to value copies or object placeholders.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefReturnProperty_EmitsReferenceGetter() {
            string source = """
                public struct Slot {
                    public int Value;
                }

                public struct Container {
                    public Slot Slot;
                }

                public class Holder {
                    Container backing;

                    public ref Slot Current {
                        get {
                            return ref backing.Slot;
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Holder.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Holder.cpp"));

            Assert.Contains("::Slot& get_Current();", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("object*", headerOutput, StringComparison.Ordinal);
            Assert.Contains("return this->backing.Slot;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("return ;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures constructor overloads that differ only by ref modifiers keep distinct native signatures.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefConstructorOverload_EmitsDistinctReferenceSignature() {
            string source = """
                using System.Numerics;

                public struct WideValue {
                    public Vector<float> X;

                    public WideValue(ref Vector<float> value) {
                        X = value;
                    }

                    public WideValue(Vector<float> value) {
                        X = value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "WideValue.hpp"));

            Assert.Contains("WideValue(Vector_1<float>& value);", headerOutput, StringComparison.Ordinal);
            Assert.Contains("WideValue(Vector_1<float> value);", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures partial class declarations merge into one emitted native type instead of overwriting earlier declarations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPartialClassDeclarations_MergesMembersIntoSingleType() {
            string source = """
                public partial class PartialFixture {
                    public int Value;
                }

                public partial class PartialFixture {
                    public int Read() {
                        return Value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PartialFixture.hpp"));

            Assert.Contains("int32_t Value;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("int32_t Read();", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures partial class members from different source files retain their own semantic models during emission.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCrossFilePartialClassDeclarations_UsesMemberSemanticModels() {
            ConversionOutput output = RunConversion(new Dictionary<string, string>(StringComparer.Ordinal) {
                ["Part1.cs"] = """
                    public partial class PartialFixture {
                        public int Read() {
                            return int.Abs(Value);
                        }
                    }
                    """,
                ["Part2.cs"] = """
                    public partial class PartialFixture {
                        public int Value;
                    }
                    """
            });
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PartialFixture.cpp"));

            Assert.Contains("return int32_t::Abs(this->Value);", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated C++ type names include namespace-derived prefixes when multiple source namespaces declare the same type name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNamespaceTypeNameCollision_QualifiesEmittedTypeNames() {
            string source = """
                namespace Alpha {
                    public class RefinementContext {
                    }
                }

                namespace Beta {
                    public class RefinementContext {
                    }

                    public class Holder {
                        public Alpha.RefinementContext First;
                        public Beta.RefinementContext Second;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string alphaHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Alpha_RefinementContext.hpp"));
            string betaHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Beta_RefinementContext.hpp"));
            string holderHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Holder.hpp"));

            Assert.Contains("class Alpha_RefinementContext", alphaHeader);
            Assert.Contains("class Beta_RefinementContext", betaHeader);
            Assert.Contains("::Alpha_RefinementContext* First;", holderHeader);
            Assert.Contains("::Beta_RefinementContext* Second;", holderHeader);
        }

        /// <summary>
        /// Ensures explicit generic interface implementations do not emit duplicate class members when the interface model omits the generic contract.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitGenericInterfaceImplementation_DoesNotEmitDuplicateMethod() {
            string source = """
                public interface IBufferPool {
                    int GetCapacityForCount<T>(int count);
                }

                public class BufferPool : IBufferPool {
                    public static int GetCapacityForCount<T>(int count) {
                        return count;
                    }

                    int IBufferPool.GetCapacityForCount<T>(int count) {
                        return GetCapacityForCount<T>(count);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "BufferPool.hpp"));
            int emittedMethodCount = headerOutput.Split("GetCapacityForCount", StringSplitOptions.None).Length - 1;

            Assert.Equal(1, emittedMethodCount);
        }

        /// <summary>
        /// Ensures nongeneric IEnumerable explicit enumerator shims do not emit an illegal overload that differs only by return type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitNonGenericEnumerableShim_DoesNotEmitConflictingGetEnumeratorOverload() {
            string source = """
                using System.Collections;
                using System.Collections.Generic;

                public class Numbers : IEnumerable<int> {
                    public IEnumerator<int> GetEnumerator() {
                        return null;
                    }

                    IEnumerator IEnumerable.GetEnumerator() {
                        return GetEnumerator();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Numbers.hpp"));

            Assert.DoesNotContain("IEnumerator* GetEnumerator();", headerOutput, StringComparison.Ordinal);
            Assert.Contains("IEnumerator<int32_t>* GetEnumerator();", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures default literals adopt value-type zero initialization instead of degrading to nullptr.
        /// </summary>
        [Fact]
        public void WriteOutput_WithValueTypeDefaultLiteral_EmitsValueInitialization() {
            string source = """
                public struct Handle {
                    public int Value;
                }

                public class Holder {
                    public Handle Read() {
                        return default;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Holder.cpp"));

            Assert.Contains("return ::Handle();", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("return nullptr;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures default literals used against fields whose names shadow their value types qualify the constructed native type name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithShadowedValueTypeDefaultLiteral_QualifiesConstructedTypeName() {
            string source = """
                public struct Handle {
                    public int Value;
                }

                public class Holder {
                    public Handle Handle;

                    public void Reset() {
                        Handle = default;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Holder.cpp"));

            Assert.Contains("this->Handle = ::Handle();", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->Handle = Handle();", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures pointer locals initialized from stackalloc emit a concrete native backing array and pointer binding.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerStackAllocDeclaration_EmitsBackingArrayAndPointerBinding() {
            string source = """
                public unsafe class BufferOwner {
                    public void Fill(int count) {
                        int* handles = stackalloc int[4];
                        handles[0] = count;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "BufferOwner.cpp"));

            Assert.Contains("int32_t handles_stackalloc[4];", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("int32_t *handles = handles_stackalloc;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures inferred pointer locals initialized from stackalloc emit the same native backing array and pointer binding as explicit pointer declarations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInferredPointerStackAllocDeclaration_EmitsBackingArrayAndPointerBinding() {
            string source = """
                public unsafe class BufferOwner {
                    public void Fill(int count) {
                        var handles = stackalloc int[count];
                        handles[0] = count;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "BufferOwner.cpp"));

            Assert.Contains("Array<int32_t> handles_stackalloc(count);", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("int32_t *handles = handles_stackalloc.Data;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("int32_t handles_stackalloc[count];", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures casts between raw pointers and native pointer-sized integers use reinterpret_cast and preserve pointer targets.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerSizedIntegerCasts_UsesReinterpretCast() {
            string source = """
                public unsafe class PointerBridge {
                    public static nuint ToValue(int* pointer) {
                        return (nuint)pointer;
                    }

                    public static int* ToPointer(nuint value) {
                        return (int*)value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PointerBridge.cpp"));

            Assert.Contains("return reinterpret_cast<uintptr_t>(pointer);", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("return reinterpret_cast<int32_t*>(value);", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures raw pointer-to-pointer casts preserve reinterpret semantics instead of emitting invalid static_cast conversions between unrelated native pointer types.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerToPointerCast_UsesReinterpretCast() {
            string source = """
                public unsafe class PointerBridge {
                    public static float* ToFloat(int* pointer) {
                        return (float*)pointer;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PointerBridge.cpp"));

            Assert.Contains("return reinterpret_cast<float*>(pointer);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("static_cast<float*>(pointer)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures casts from ordinary native integer expressions back to raw pointers use reinterpret_cast instead of invalid static_cast pointer conversions.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUnsignedIntegerExpressionToPointerCast_UsesReinterpretCast() {
            string source = """
                public unsafe class PointerBridge {
                    public static byte* Align(byte* pointer) {
                        return (byte*)(((ulong)pointer + 31ul) & (~31ul));
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PointerBridge.cpp"));

            Assert.Contains("return reinterpret_cast<uint8_t*>(", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("static_cast<uint8_t*>", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures method groups passed into System.Threading.Thread constructors lower through portable delegate wrappers and runtime thread headers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithThreadMethodGroupConstruction_UsesRuntimeThreadWrapper() {
            string source = """
                using System.Threading;

                public class Dispatcher {
                    Thread thread;

                    public void StartWorker() {
                        thread = new Thread(WorkerLoop);
                        thread.IsBackground = true;
                        thread.Start(null);
                    }

                    void WorkerLoop(object state) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Dispatcher.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Dispatcher.cpp"));

            Assert.Contains("#include \"system/threading/thread.hpp\"", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"Thread.hpp\"", headerOutput, StringComparison.Ordinal);
            Assert.Contains("new Thread(new Action<void*>(std::bind_front(&Dispatcher::WorkerLoop, this)))", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("new Thread(&Dispatcher::WorkerLoop)", sourceOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "threading", "thread.hpp")));
        }

        /// <summary>
        /// Ensures index-from-end element access lowers to one explicit length-based index expression instead of emitting the C# '^' token into native output.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSpanIndexFromEndAccess_UsesLengthBasedIndexer() {
            string source = """
                public class BufferOwner {
                    public int ReadLast(Span<int> values) {
                        return values[^1];
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "BufferOwner.cpp"));

            Assert.Contains("values.get_Item(values.get_Length() - 1)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("^1", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures primitive generic-math Max and Min calls lower through the shared Math runtime helpers instead of nonexistent static members on fixed-width C++ aliases.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPrimitiveMaxMinCalls_UsesMathRuntimeHelpers() {
            string source = """
                public class ClampHelper {
                    public int Clamp(int value, int minimum, int maximum) {
                        return int.Min(maximum, int.Max(minimum, value));
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ClampHelper.cpp"));

            Assert.Contains("Math::Max(minimum, value)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("Math::Min(maximum, Math::Max(minimum, value))", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("int32_t::Max", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("int32_t::Min", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nested object initializers resolve their concrete generated type by symbol rather than colliding with unrelated leaf-name matches.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedObjectInitializerTypeNameCollision_UsesCorrectGeneratedValueType() {
            string source = """
                namespace TaskScheduling {
                    public class Worker {
                    }
                }

                public class Dispatcher {
                    public struct Worker {
                        public int Value;
                    }

                    public Worker Create() {
                        return new Worker { Value = 1 };
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Dispatcher.cpp"));

            Assert.DoesNotContain("TaskScheduling_Worker()", sourceOutput, StringComparison.Ordinal);
            Assert.Contains(".Value = 1;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("->Value = 1;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nested generated helper types retain access to private members on their containing type after being emitted as separate top-level C++ classes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedTypePrivateMemberAccess_EmitsFriendDeclaration() {
            string source = """
                public class Outer {
                    static int Hidden(int value) {
                        return value;
                    }

                    public class Worker {
                        public int Read() {
                            return Hidden(1);
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Outer.hpp"));
            Assert.Contains("friend class Worker;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("return Outer::Hidden(1);", output.GeneratedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures assignments targeting ref-return properties write through the generated getter result instead of emitting a nonexistent field or setter access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefReturnPropertyAssignment_AssignsThroughGetterResult() {
            string source = """
                public struct Slot {
                    public int Value;
                }

                public class Holder {
                    Slot current;

                    public ref Slot Current => ref current;

                    public void Reset() {
                        Current = new Slot();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Holder.cpp"));

            Assert.Contains("this->get_Current() = ::Slot();", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->Current = ::Slot();", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures expression-bodied methods that return newly constructed managed objects emit a proper return statement instead of a dangling assignment token.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExpressionBodiedFactoryMethod_EmitsReturnNewExpression() {
            string source = """
                public class Worker {
                }

                public static class WorkerFactory {
                    public static Worker Create() => new Worker();
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "WorkerFactory.cpp"));

            Assert.Contains("return new ::Worker();", sourceOutput);
            Assert.DoesNotContain("= new ::Worker()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures void expression-bodied methods emit direct statements instead of invalid initializer-style tokens.
        /// </summary>
        [Fact]
        public void WriteOutput_WithVoidExpressionBodiedMethod_EmitsStatementBody() {
            string source = """
                public class Worker {
                    public int Count { get; private set; }

                    public void Touch() => Count = Count + 1;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Worker.cpp"));

            Assert.Contains("this->set_Count(this->Count + 1);", sourceOutput);
            Assert.DoesNotContain("{\n = ", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures ReadOnlySpan overloads remain distinct in generated C++ and the copied runtime span header exposes the read-only companion type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithReadOnlySpanOverloads_PreservesDistinctNativeSpanTypes() {
            string source = """
                using System;

                public class SpanBridge<T> {
                    public void Copy(Span<T> source) {
                    }

                    public void Copy(ReadOnlySpan<T> source) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "SpanBridge_1.hpp"));
            string nativeSpanRuntime = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_span.hpp"));

            Assert.Contains("void Copy(Span<T> source);", headerOutput);
            Assert.Contains("void Copy(ReadOnlySpan<T> source);", headerOutput);
            Assert.Contains("class ReadOnlySpan", nativeSpanRuntime);
            Assert.Contains("void CopyTo(Span<T> target) const", nativeSpanRuntime);
        }

        /// <summary>
        /// Ensures runtime span types stay globally qualified when one generated member named Span would otherwise shadow the template type token inside one class.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMemberNamedSpan_QualifiesRuntimeSpanTypes() {
            string source = """
                using System;

                public struct Buffer<T> {
                }

                public class SpanOwner<T> {
                    public Buffer<T> Span;

                    public void Copy(Span<T> source) {
                    }

                    public void Copy(ReadOnlySpan<T> source) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "SpanOwner_1.hpp"));

            Assert.Contains("void Copy(::Span<T> source);", headerOutput);
            Assert.Contains("void Copy(::ReadOnlySpan<T> source);", headerOutput);
        }

        /// <summary>
        /// Ensures generic static member access keeps its template arguments so EqualityComparer-based helpers bind to the native runtime template.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericStaticRuntimeMemberAccess_PreservesGenericOwnerArguments() {
            string source = """
                using System.Collections.Generic;

                public class EqualityGate<T> {
                    public bool Match(T a, T b) {
                        return EqualityComparer<T>.Default.Equals(a, b);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "EqualityGate_1.cpp"));

            Assert.Contains("#include \"system/collections/generic/equality_comparer.hpp\"", output.GeneratedText);
            Assert.Contains("EqualityComparer<T>::get_Default()", sourceOutput);
            Assert.DoesNotContain("EqualityComparer::get_Default()", sourceOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "collections", "generic", "equality_comparer.hpp")));
        }

        /// <summary>
        /// Ensures ref locals do not leak pseudo-type include paths into generated source files.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefLocals_DoesNotEmitRefPseudoIncludes() {
            string source = """
                public class RefLocalFixture {
                    int[] values = new int[] { 1, 2 };

                    public int Read() {
                        ref int typed = ref values[0];
                        ref var inferred = ref values[1];
                        typed = typed + inferred;
                        return values[0];
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.DoesNotContain("#include \"ref int.hpp\"", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"ref var.hpp\"", output.GeneratedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures the real content-manager built-in processor registration shape lowers concrete processors and implicit-array extensions correctly.
        /// </summary>
        [Fact]
        public void WriteOutput_WithBuiltInProcessorRegistrationShape_UsesConcreteProcessorAndNativeListArgument() {
            string source = """
                using System.Collections.Generic;

                public interface IContentProcessor {
                }

                public interface IContentProcessor<T> : IContentProcessor {
                }

                public class TextContent {
                }

                public class RawByteContent {
                }

                public class TextContentProcessor : IContentProcessor<TextContent> {
                }

                public class RawByteContentProcessor : IContentProcessor<RawByteContent> {
                }

                public class ContentManager {
                    const string TextContentProcessorId = "core.text-content";
                    const string RawByteContentProcessorId = "core.raw-byte-content";
                    const string WildcardExtension = "*";

                    void RegisterProcessor(ContentProcessorRegistration registration) {
                    }

                    void RegisterProcessor<T>(string processorId, IContentProcessor<T> processor, IReadOnlyList<string> extensions = null) {
                    }

                    public void RegisterBuiltInProcessors() {
                        RegisterProcessor(TextContentProcessorId, new TextContentProcessor(), new[] { WildcardExtension });
                        RegisterProcessor(RawByteContentProcessorId, new RawByteContentProcessor(), new[] { WildcardExtension });
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.cpp"));
            string textProcessorHeader = File.ReadAllText(Path.Combine(output.OutputPath, "TextContentProcessor.hpp"));

            Assert.Contains("RegisterProcessor<TextContent*>(TextContentProcessorId, new ::TextContentProcessor(), new List<std::string>({ WildcardExtension }))", sourceOutput);
            Assert.Contains("RegisterProcessor<RawByteContent*>(RawByteContentProcessorId, new ::RawByteContentProcessor(), new List<std::string>({ WildcardExtension }))", sourceOutput);
            Assert.DoesNotContain("[WildcardExtension]", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("class TextContentProcessor : public IContentProcessor_1<::TextContent*>", textProcessorHeader);
        }

        /// <summary>
        /// Ensures source-only generated object creation adds the generated include needed by the emitted C++ body.
        /// </summary>
        [Fact]
        public void WriteOutput_WithBodyOnlyGeneratedObjectCreation_EmitsSourceInclude() {
            string source = """
                public class HelperNode {
                }

                public class GraphHost {
                    public void Build() {
                        HelperNode node = new HelperNode();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "GraphHost.cpp"));

            Assert.Contains("#include \"HelperNode.hpp\"", sourceOutput);
            Assert.Contains("HelperNode()", sourceOutput);
        }

        /// <summary>
        /// Ensures generated types nested inside expression-side generic construction are globally qualified when a member name would otherwise collide.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGeneratedGenericConstructionInBody_QualifiesNestedGeneratedTypes() {
            string source = """
                using System.Collections.Generic;

                public class ContentManager {
                }

                public class Core {
                    public ContentManager ContentManager { get; set; }

                    public void Build() {
                        Dictionary<string, ContentManager> map = new Dictionary<string, ContentManager>();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Core.cpp"));

            Assert.Contains("new Dictionary<std::string, ::ContentManager*>", sourceOutput);
        }

        /// <summary>
        /// Ensures named tuple debug-info aggregation lowers through native ValueTuple construction, tuple item storage members, and a valid catch-all handler.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDebugInfoNamedTupleAggregation_UsesNativeTupleAndCatchAllLowering() {
            string source = """
                using System.Collections.Generic;

                public interface IDebugInfoProvider {
                    string Category { get; }
                    void AppendInfo(List<(string Key, string Value)> items);
                }

                public static class DebugInfoRegistry {
                    static readonly List<IDebugInfoProvider> providers = new List<IDebugInfoProvider>();

                    public static List<(string Category, string Key, string Value)> Snapshot() {
                        var result = new List<(string, string, string)>();
                        for (int i = 0; i < providers.Count; i++) {
                            var p = providers[i];
                            var items = new List<(string Key, string Value)>();
                            try {
                                p.AppendInfo(items);
                            } catch {
                            }

                            for (int j = 0; j < items.Count; j++) {
                                var it = items[j];
                                result.Add((p.Category, it.Key, it.Value));
                            }
                        }

                        return result;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "DebugInfoRegistry.cpp"));

            Assert.Contains("new List<ValueTuple<std::string, std::string, std::string>*>();", sourceOutput);
            Assert.Contains("new List<ValueTuple<std::string, std::string>*>();", sourceOutput);
            Assert.Contains("catch (...)", sourceOutput);
            Assert.Contains("::IDebugInfoProvider *p = (*providers)[i];", sourceOutput);
            Assert.Contains("ValueTuple<std::string, std::string> *it = (*items)[j];", sourceOutput);
            Assert.Contains("result->Add(", sourceOutput);
            Assert.Contains("ValueTuple<std::string, std::string, std::string>", sourceOutput);
            Assert.Contains("new ValueTuple<std::string, std::string, std::string>(p->get_Category(), it->Item1, it->Item2)", sourceOutput);
        }

        /// <summary>
        /// Ensures declaration-pattern guards over generic reference constraints lower to typed native cast checks.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericDeclarationPattern_EmitsTypedNativeCastGuard() {
            string source = """
                public class Asset {
                }

                public class Stream {
                }

                public static class AssetSerializer {
                    public static Asset Deserialize(Stream stream) {
                        return null;
                    }
                }

                public class AssetContentProcessor<TAsset> where TAsset : Asset {
                    public TAsset Read(Stream stream) {
                        Asset asset = AssetSerializer.Deserialize(stream);
                        if (asset is TAsset typedAsset) {
                            return typedAsset;
                        }

                        return null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "AssetContentProcessor_1.cpp"));

            Assert.Contains("TAsset typedAsset = he_cpp_try_cast<TAsset>(asset);", sourceOutput);
            Assert.Contains("if (typedAsset != nullptr)", sourceOutput);
            Assert.DoesNotContain("if ()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures serializer-style optional arguments and enum locals lower without pointer leakage.
        /// </summary>
        [Fact]
        public void WriteOutput_WithOptionalArgumentsAndEnumLocals_UsesValueSemanticsAndDefaultArguments() {
            string source = """
                public enum EngineBinaryEndianness {
                    LittleEndian,
                    BigEndian
                }

                public enum EditorAssetBinaryValueKind {
                    TextureAsset = 1
                }

                public class Stream {
                }

                public class EngineBinaryReader {
                    public static EngineBinaryReader Create(Stream stream, EngineBinaryEndianness endianness, bool leaveOpen = true) {
                        return null;
                    }
                }

                public class EngineBinaryHeader {
                    public EngineBinaryHeader(EngineBinaryEndianness endianness, byte version, ushort formatId, ushort recordKind, ushort valueKind) {
                    }
                }

                public static class Serializer {
                    public static void Deserialize(Stream stream, EngineBinaryEndianness endianness) {
                        EngineBinaryReader reader = EngineBinaryReader.Create(stream, endianness);
                        EditorAssetBinaryValueKind valueKind = EditorAssetBinaryValueKind.TextureAsset;
                        EngineBinaryHeader header = new EngineBinaryHeader(endianness, 2, 1, 7, (ushort)valueKind);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Serializer.cpp"));

            Assert.Contains("EngineBinaryReader::Create(stream, endianness, true)", sourceOutput);
            Assert.Contains("::EditorAssetBinaryValueKind valueKind = EditorAssetBinaryValueKind::TextureAsset;", sourceOutput);
            Assert.DoesNotContain("::EditorAssetBinaryValueKind *valueKind", sourceOutput);
            Assert.Contains("new ::EngineBinaryHeader(endianness, 2, 1, 7, static_cast<uint16_t>(valueKind))", sourceOutput);
        }

        /// <summary>
        /// Ensures serializer-style type checks and callback method groups lower through native casts and delegate wrappers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSerializerTypeChecksAndArrayCallbacks_UsesNativeCppShapes() {
            string source = """
                using System;

                public class Asset {
                }

                public class TextureAsset : Asset {
                }

                public class ModelAsset : Asset {
                }

                public class Reader {
                    public T[] ReadArray<T>(Func<Reader, T> readElement) {
                        return null;
                    }
                }

                public class Writer {
                    public void WriteArray<T>(T[] values, Action<Writer, T> writeElement) {
                    }
                }

                public static class Serializer {
                    public static int GetValueKind(Asset asset) {
                        if (asset is TextureAsset) {
                            return 1;
                        } else if (asset is ModelAsset) {
                            return 2;
                        }

                        return 0;
                    }

                    public static void WriteAsset(Writer writer, Asset asset) {
                        if (asset is TextureAsset textureAsset) {
                            WriteTexture(writer, textureAsset);
                            return;
                        } else if (asset is ModelAsset modelAsset) {
                            WriteModel(writer, modelAsset);
                            return;
                        }
                    }

                    public static int[] ReadInts(Reader reader) {
                        return reader.ReadArray(ReadInt);
                    }

                    public static void WriteInts(Writer writer, int[] values) {
                        writer.WriteArray(values, WriteInt);
                    }

                    static int ReadInt(Reader reader) {
                        return 0;
                    }

                    static void WriteTexture(Writer writer, TextureAsset asset) {
                    }

                    static void WriteModel(Writer writer, ModelAsset asset) {
                    }

                    static void WriteInt(Writer writer, int value) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string readerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Reader.cpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Serializer.cpp"));
            string writerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Writer.cpp"));

            Assert.Contains("he_cpp_try_cast<TextureAsset>(asset) != nullptr", sourceOutput);
            Assert.Contains("else", sourceOutput);
            Assert.Contains("ModelAsset* modelAsset = he_cpp_try_cast<ModelAsset>(asset);", sourceOutput);
            Assert.Contains("reader->ReadArray<int32_t>(new Func<Reader*, int32_t>(&Serializer::ReadInt))", sourceOutput);
            Assert.Contains("writer->WriteArray<int32_t>(values, new Action<Writer*, int32_t>(&Serializer::WriteInt))", sourceOutput);
            Assert.Contains("Array<T>* Reader::ReadArray(Func<::Reader*, T>* readElement)", readerOutput);
            Assert.Contains("void Writer::WriteArray(Array<T>* values, Action<::Writer*, T>* writeElement)", writerOutput);
            Assert.DoesNotContain("instanceof", sourceOutput);
            Assert.DoesNotContain("else     ModelAsset*", sourceOutput);
        }

        /// <summary>
        /// Ensures nested else-if conditions that declare inline out variables are wrapped into a scoped nested if so generated C++ stays syntactically valid.
        /// </summary>
        [Fact]
        public void WriteOutput_WithElseIfInlineOutDeclarations_WrapsNestedIfInElseScope() {
            string source = """
                public static class ContactResolver {
                    public static bool TryResolve(out float penetration, out int axisIndex) {
                        penetration = 1f;
                        axisIndex = 2;
                        return true;
                    }
                }

                public class PhysicsGate {
                    public void Tick(bool firstCondition) {
                        if (firstCondition) {
                            return;
                        } else if (ContactResolver.TryResolve(out float penetration, out int axisIndex)) {
                            Consume(penetration, axisIndex);
                        }
                    }

                    void Consume(float penetration, int axisIndex) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PhysicsGate.cpp"));

            Assert.Contains("else {\n", sourceOutput);
            Assert.Contains("float penetration;", sourceOutput);
            Assert.Contains("int32_t axisIndex;", sourceOutput);
            Assert.Contains("if (ContactResolver::TryResolve(penetration, axisIndex))", sourceOutput);
            Assert.DoesNotContain("else float penetration;", sourceOutput);
        }

        /// <summary>
        /// Ensures object.GetType().Name inside diagnostics lowers through the native type token helper instead of an unsupported instance GetType API.
        /// </summary>
        [Fact]
        public void WriteOutput_WithObjectGetTypeNameDiagnostic_UsesNativeTypeToken() {
            string source = """
                using System;

                public class Asset {
                }

                public static class Serializer {
                    public static string Describe(Asset asset) {
                        return $"Asset type '{asset.GetType().Name}'";
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Serializer.cpp"));

            Assert.Contains("he_cpp_type_of<Asset>(\"Asset\")->Name", sourceOutput);
            Assert.DoesNotContain("asset->GetType()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated peer types referenced only through typeof still register their emitted headers for native compilation.
        /// </summary>
        [Fact]
        public void WriteOutput_WithTypeOfGeneratedPeerType_RegistersGeneratedHeaderDependency() {
            string source = """
                public class PeerType {
                }

                public static class TypeGate {
                    public static bool IsPeer<TValue>() {
                        return typeof(TValue) == typeof(PeerType);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "TypeGate.cpp"));

            Assert.Contains("#include \"PeerType.hpp\"", sourceOutput);
            Assert.Contains("he_cpp_type_of<PeerType>(\"PeerType\")", sourceOutput);
        }

        /// <summary>
        /// Ensures binary serializer helpers lower through runtime constructor defaults and static helper surfaces that native compilation supports.
        /// </summary>
        [Fact]
        public void WriteOutput_WithBinarySerializerHelpers_UsesStaticRuntimeBridgesAndMutableLocals() {
            string source = """
                using System;
                using System.Text;

                public class Stream {
                    public virtual int ReadByte() {
                        return 0;
                    }

                    public virtual void WriteByte(byte value) {
                    }

                    public virtual int Read(Span<byte> buffer) {
                        return 0;
                    }
                }

                public class BinaryReaderLE {
                    public BinaryReaderLE(Stream stream, bool leaveOpen = true) {
                    }
                }

                public class BinaryWriterLE {
                    public BinaryWriterLE(Stream stream, bool leaveOpen = true) {
                    }
                }

                public static class Serializer {
                    public static BinaryReaderLE CreateReader(Stream stream) {
                        return new BinaryReaderLE(stream);
                    }

                    public static BinaryWriterLE CreateWriter(Stream stream) {
                        return new BinaryWriterLE(stream);
                    }

                    public static float ReadSingle(int value) {
                        return BitConverter.Int32BitsToSingle(value);
                    }

                    public static int WriteSingle(float value) {
                        return BitConverter.SingleToInt32Bits(value);
                    }

                    public static long WriteDouble(double value) {
                        return BitConverter.DoubleToInt64Bits(value);
                    }

                    public static double ReadDouble(byte[] bytes) {
                        return BinaryPrimitives.ReadDoubleLittleEndian(bytes);
                    }

                    public static void WriteDoubleBytes(byte[] bytes, double value) {
                        BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
                    }

                    public static string ReadString(byte[] bytes) {
                        return Encoding.UTF8.GetString(bytes);
                    }

                    public static byte[] WriteString(string value) {
                        return Encoding.UTF8.GetBytes(value);
                    }

                    public static void Fill(Stream stream, Span<byte> buffer) {
                        int totalBytesRead = 0;
                        while (totalBytesRead < buffer.Length) {
                            int bytesRead = stream.Read(buffer.Slice(totalBytesRead));
                            totalBytesRead += bytesRead;
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Serializer.cpp"));

            Assert.Contains("new ::BinaryReaderLE(stream, true)", sourceOutput);
            Assert.Contains("new ::BinaryWriterLE(stream, true)", sourceOutput);
            Assert.Contains("BitConverter::Int32BitsToSingle(value)", sourceOutput);
            Assert.Contains("BitConverter::SingleToInt32Bits(value)", sourceOutput);
            Assert.Contains("BitConverter::DoubleToInt64Bits(value)", sourceOutput);
            Assert.Contains("BinaryPrimitives::ReadDoubleLittleEndian(bytes->data())", sourceOutput);
            Assert.Contains("BinaryPrimitives::WriteDoubleBigEndian(bytes->data(), value)", sourceOutput);
            Assert.Contains("Encoding::GetString(Encoding::UTF8, bytes)", sourceOutput);
            Assert.Contains("Encoding::GetBytes(Encoding::UTF8, value)", sourceOutput);
            Assert.Contains("int32_t totalBytesRead = 0;", sourceOutput);
            Assert.DoesNotContain("const int32_t totalBytesRead = 0;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("BitConverter->", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Encoding::UTF8::", sourceOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "bit_converter.hpp")));
        }

        /// <summary>
        /// Ensures constructor arguments with side effects are hoisted into temporaries so emitted C++ preserves C# left-to-right evaluation order.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSideEffectingConstructorArguments_HoistsArgumentsIntoTemporaries() {
            string source = """
                public struct Vec4 {
                    public float X;
                    public float Y;
                    public float Z;
                    public float W;

                    public Vec4(float x, float y, float z, float w) {
                        X = x;
                        Y = y;
                        Z = z;
                        W = w;
                    }
                }

                public class Reader {
                    public float ReadSingle() {
                        return 0;
                    }
                }

                public class SceneLoader {
                    public Vec4 Load(Reader reader) {
                        return new Vec4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "SceneLoader.cpp"));

            Assert.Contains("([&]() {", sourceOutput);
            Assert.Contains("auto __ctor_arg_", sourceOutput);
            Assert.Contains("return ::Vec4(", sourceOutput);
            Assert.DoesNotContain("::Vec4(reader->ReadSingle(), reader->ReadSingle(), reader->ReadSingle(), reader->ReadSingle())", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures list removal uses the runtime list helper and qualified child/component callbacks do not rebind member names back onto the current instance.
        /// </summary>
        [Fact]
        public void WriteOutput_WithQualifiedChildCallbacksAndListRemoval_UsesReceiverMemberAccess() {
            string source = """
                using System.Collections.Generic;

                public class Component {
                    public virtual void ParentEnabledChange(bool value) {
                    }
                }

                public class Entity {
                    public List<Component> Components;
                    public List<Entity> Children;

                    public void RemoveChild(Entity entity) {
                        Children.Remove(entity);
                    }

                    public void RemoveComponent(Component component) {
                        Components.Remove(component);
                    }

                    protected virtual void ParentEnabledChange(bool newEnabled) {
                        if (Components != null) {
                            for (int i = 0; i < Components.Count; i++) {
                                Components[i].ParentEnabledChange(newEnabled);
                            }
                        }

                        if (Children != null) {
                            for (int i = 0; i < Children.Count; i++) {
                                Children[i].ParentEnabledChange(Children[i].IsHierarchyEnabled);
                            }
                        }
                    }

                    public bool IsHierarchyEnabled => true;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Entity.cpp"));

            Assert.Contains("this->Children->Remove(entity)", sourceOutput);
            Assert.Contains("this->Components->Remove(component)", sourceOutput);
            Assert.Contains("(*this->Components)[i]->ParentEnabledChange(newEnabled);", sourceOutput);
            Assert.Contains("(*this->Children)[i]->ParentEnabledChange((*this->Children)[i]->get_IsHierarchyEnabled());", sourceOutput);
            Assert.DoesNotContain("->this->ParentEnabledChange", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("->this->IsHierarchyEnabled", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures string indexers stay character-typed and dictionary out-var declarations use the resolved symbol type instead of leaking the C# var token.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringIndexAndOutVarDictionaryLookup_UsesCharAndResolvedOutType() {
            string source = """
                using System.Collections.Generic;

                public class Glyph {
                    public float AdvanceWidth;
                }

                public class FontAsset {
                    public Dictionary<char, Glyph> Characters;

                    public float Measure(string text) {
                        float width = 0f;
                        for (int i = 0; i < text.Length; i++) {
                            char c = text[i];
                            if (c == ' ') {
                                continue;
                            }

                            if (Characters.TryGetValue(c, out var ch)) {
                                width += ch.AdvanceWidth;
                            }
                        }

                        return width;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "FontAsset.cpp"));

            Assert.Contains("const char c = text[i];", sourceOutput);
            Assert.Contains("if (c == ' ')", sourceOutput);
            Assert.Contains("::Glyph* ch;", sourceOutput);
            Assert.Contains("this->Characters->TryGetValue(c, ch)", sourceOutput);
            Assert.DoesNotContain("const std::string c = text[i];", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("var* ch", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures local string variables continue to use native bracket indexing instead of synthetic get_Item calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithLocalStringIndexer_UsesNativeBracketAccess() {
            string source = """
                public class PathGate {
                    public bool IsRooted(string path) {
                        string normalized = path.Trim();
                        return normalized[0] == '/' || normalized[1] == ':';
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PathGate.cpp"));

            Assert.Contains("normalized[0] == '/'", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("normalized[1] == ':'", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("normalized.get_Item(0)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("normalized.get_Item(1)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures interpolated reads of unqualified instance properties lower through generated getter calls even when the property name collides with framework type names.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInterpolatedUnqualifiedInstanceProperty_UsesGetterCall() {
            string source = """
                public class Counter {
                    int current;

                    public int Index {
                        get {
                            return current;
                        }
                    }

                    public override string ToString() {
                        return $"<{Index}>";
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Counter.cpp"));

            Assert.Contains("std::to_string(this->get_Index())", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("std::to_string(Index)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures each interpolated segment restores class context so later unqualified properties still lower through generated getters.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMultipleInterpolatedUnqualifiedProperties_UsesGetterCallsForEverySegment() {
            string source = """
                public struct ContinuationIndex {
                    public uint Packed;

                    public int Index {
                        get {
                            return (int)(Packed & 0x3FFFFFFF);
                        }
                    }

                    public int Type {
                        get {
                            return (int)((Packed >> 30) & 1);
                        }
                    }

                    public override string ToString() {
                        return $"<{Type}, {Index}>";
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ContinuationIndex.cpp"));

            Assert.Contains("std::to_string(this->get_Type())", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("std::to_string(this->get_Index())", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("std::to_string(Index)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nested struct receivers keep direct member access and primitive float limits lower to native literals instead of the fake Number type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStructGlyphMetricsAndFloatLimits_UsesDirectValueMemberAccessAndNativeFloatLimits() {
            string source = """
                using System;
                using System.Collections.Generic;

                public struct float4 {
                    public float Z;
                    public float W;
                }

                public struct FontChar {
                    public float AdvanceWidth;
                    public float4 SourceRect;
                    public float OffsetY;
                }

                public struct FontTightMetrics {
                    public FontTightMetrics(float width, float minTop, float maxBottom) {
                    }
                }

                public class FontInfo {
                    public float SpaceWidth;
                }

                public class FontAsset {
                    public Dictionary<char, FontChar> Characters;
                    public FontInfo FontInfo;
                    public float LineHeight;
                    public int AtlasWidth;
                    public int AtlasHeight;

                    public float Measure(string text) {
                        float width = 0f;
                        for (int i = 0; i < text.Length; i++) {
                            char c = text[i];
                            if (c == ' ') {
                                width += FontInfo.SpaceWidth;
                                continue;
                            }

                            if (Characters.TryGetValue(c, out var ch)) {
                                float advance = ch.AdvanceWidth > 0 ? ch.AdvanceWidth : (ch.SourceRect.Z * AtlasWidth);
                                width += advance;
                            }
                        }

                        return width;
                    }

                    public FontTightMetrics MeasureTight(string text) {
                        float minTop = float.MaxValue;
                        float maxBottom = float.MinValue;

                        for (int i = 0; i < text.Length; i++) {
                            char c = text[i];
                            if (!Characters.TryGetValue(c, out var ch)) {
                                continue;
                            }

                            float glyphBottom = ch.OffsetY + (ch.SourceRect.W * AtlasHeight);
                            if (glyphBottom > maxBottom) {
                                maxBottom = glyphBottom;
                            }
                        }

                        if (minTop == float.MaxValue) {
                            minTop = 0f;
                        }

                        return new FontTightMetrics(0f, minTop, maxBottom);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "FontAsset.cpp"));

            Assert.Contains("ch.SourceRect.Z * AtlasWidth", sourceOutput);
            Assert.Contains("ch.SourceRect.W * this->AtlasHeight", sourceOutput);
            Assert.Contains("float minTop = 3.4028234663852886e38f;", sourceOutput);
            Assert.Contains("float maxBottom = -3.4028234663852886e38f;", sourceOutput);
            Assert.Contains("if (minTop == 3.4028234663852886e38f)", sourceOutput);
            Assert.DoesNotContain("SourceRect->Z", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("SourceRect->W", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Number.MaxValue", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Number.MinValue", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures dictionary TryGetValue out-variable declarations stay in the surrounding statement scope instead of being trapped inside an invocation lambda.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDictionaryTryGetValueOutVarCondition_HoistsDeclarationOutsideConditionLambda() {
            string source = """
                using System.Collections.Generic;

                public struct Glyph {
                    public float AdvanceWidth;
                }

                public class FontAsset {
                    public Dictionary<char, Glyph> Characters;

                    public float Measure(string text) {
                        float width = 0f;
                        for (int i = 0; i < text.Length; i++) {
                            char c = text[i];
                            if (Characters.TryGetValue(c, out var ch)) {
                                width += ch.AdvanceWidth;
                            }
                        }

                        return width;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "FontAsset.cpp"));

            Assert.Contains("::Glyph ch;", sourceOutput);
            Assert.Contains("if (this->Characters->TryGetValue(c, ch))", sourceOutput);
            Assert.Contains("width += ch.AdvanceWidth;", sourceOutput);
            Assert.DoesNotContain("([&]() {\n::Glyph ch;\nreturn this->Characters->TryGetValue(c, ch);\n})()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures out-variable declarations from invocation statements remain available to subsequent statements in the same scope.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInvocationStatementOutVars_HoistsDeclarationsOutsideStatement() {
            string source = """
                public interface ICamera {
                }

                public interface IHit {
                }

                public class PointerInteractionSystem {
                    IHit Hovering;

                    void ResolveTop(out IHit hit, out ICamera hitCamera) {
                        hit = null;
                        hitCamera = null;
                    }

                    public bool Update() {
                        ResolveTop(out var hit, out var hitCamera);
                        bool hoveringChanged = hit != Hovering;
                        return hoveringChanged && hitCamera != null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "PointerInteractionSystem.cpp"));

            Assert.Contains("::IHit* hit;", sourceOutput);
            Assert.Contains("::ICamera* hitCamera;", sourceOutput);
            Assert.Contains("this->ResolveTop(hit, hitCamera);", sourceOutput);
            Assert.Contains("const bool hoveringChanged = hit != this->Hovering;", sourceOutput);
            Assert.DoesNotContain("([&]() {\n::IHit* hit;\n::ICamera* hitCamera;\nthis->ResolveTop(hit, hitCamera);\n})();", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures inferred var declarations preserve invocation before-lines so inline out variables are declared before the initializer call.
        /// </summary>
        [Fact]
        public void WriteOutput_WithVarInitializerInlineOutDeclarations_DeclaresTemporariesBeforeInvocation() {
            string source = """
                public class ActiveSet {
                    public bool RemoveAt(int activeBodyIndex, out int handle, out int movedBodyIndex, out int movedBodyHandle) {
                        handle = activeBodyIndex;
                        movedBodyIndex = activeBodyIndex + 1;
                        movedBodyHandle = activeBodyIndex + 2;
                        return true;
                    }
                }

                public class Bodies {
                    public bool Remove(ActiveSet set, int activeBodyIndex) {
                        var bodyMoved = set.RemoveAt(activeBodyIndex, out var handle, out var movedBodyIndex, out var movedBodyHandle);
                        return bodyMoved && handle < movedBodyHandle && movedBodyIndex > activeBodyIndex;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Bodies.cpp"));

            Assert.Contains("int32_t handle;", sourceOutput);
            Assert.Contains("int32_t movedBodyIndex;", sourceOutput);
            Assert.Contains("int32_t movedBodyHandle;", sourceOutput);
            Assert.Contains("const bool bodyMoved = set->RemoveAt(activeBodyIndex, handle, movedBodyIndex, movedBodyHandle);", sourceOutput);
            Assert.True(sourceOutput.IndexOf("int32_t handle;", StringComparison.Ordinal) < sourceOutput.IndexOf("const bool bodyMoved = set->RemoveAt(activeBodyIndex, handle, movedBodyIndex, movedBodyHandle);", StringComparison.Ordinal));
        }

        /// <summary>
        /// Ensures standalone lexical blocks remain explicit C++ scopes so repeated local names in later sibling blocks do not collide.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStandaloneLexicalBlocks_PreservesScopedBraces() {
            string source = """
                public static class GatherScatter {
                    public static int Run(bool useFirst) {
                        {
                            int lane = 1;
                            if (useFirst) {
                                return lane;
                            }
                        }

                        {
                            int lane = 2;
                            return lane;
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "GatherScatter.cpp"));

            Assert.Contains("{\nconst int32_t lane = 1;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("}\n{\nconst int32_t lane = 2;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures HLSL parser helper patterns lower through native list adapters, runtime string helpers, numeric helpers, and string-switch if-chains.
        /// </summary>
        [Fact]
        public void WriteOutput_WithHlslParserHelpers_UsesListAdaptersStringHelpersAndStringSwitchLowering() {
            string source = """
                using System;
                using System.Collections.Generic;

                public class Define {
                }

                public class Binding {
                }

                public class Member {
                    public int Offset;
                    public int Size;

                    public Member(string name, string type, int offset, int size) {
                        Offset = offset;
                        Size = size;
                    }
                }

                public static class Parser {
                    public static Binding[] Parse(string source) {
                        return Parse(source, Array.Empty<Define>());
                    }

                    public static Binding[] Parse(string source, IReadOnlyList<Define> defines) {
                        List<Binding> bindings = new List<Binding>();
                        Member[] members = ParseMembers(source);
                        int size = ComputeSize(members);
                        return bindings.ToArray();
                    }

                    static Member[] ParseMembers(string body) {
                        List<Member> members = new List<Member>();
                        return members.ToArray();
                    }

                    static int ComputeSize(IReadOnlyList<Member> members) {
                        if (members.Count == 0) {
                            return 0;
                        }

                        Member lastMember = members[members.Count - 1];
                        return lastMember.Offset + lastMember.Size;
                    }

                    static string ExtractBaseType(string type) {
                        int numericIndex = type.Length;
                        for (int characterIndex = 0; characterIndex < type.Length; characterIndex++) {
                            if (char.IsDigit(type[characterIndex])) {
                                numericIndex = characterIndex;
                                break;
                            }
                        }

                        return type.Substring(0, numericIndex).Trim();
                    }

                    static int ParseInt(string text) {
                        if (int.TryParse(text, out int value)) {
                            return value;
                        }

                        throw new InvalidOperationException();
                    }

                    static int ResolveScalarTypeSize(string type) {
                        string baseType = ExtractBaseType(type);
                        switch (baseType) {
                            case "bool":
                            case "int":
                                return 4;
                            default:
                                return 0;
                        }
                    }

                    public static bool IsFar(float value) {
                        return float.IsPositiveInfinity(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Parser.cpp"));

            Assert.Contains("return Parse(source, new List<Define*>(Array<Define*>::Empty()))", sourceOutput);
            Assert.Contains("return members->ToArray()", sourceOutput);
            Assert.Contains("const int32_t size = ComputeSize(new List<Member*>(members));", sourceOutput);
            Assert.Contains("String::IsDigit(type[characterIndex])", sourceOutput);
            Assert.Contains("String::Substring(type, 0, numericIndex)", sourceOutput);
            Assert.Contains("return String::Trim(String::Substring(type, 0, numericIndex));", sourceOutput);
            Assert.Contains("Number::TryParse(text, value)", sourceOutput);
            Assert.Contains("return Number::IsPositiveInfinity(value);", sourceOutput);
            Assert.Contains("if (String::Equals(__switchValue", sourceOutput);
            Assert.DoesNotContain("switch (baseType)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(".Trim()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(".Substring(", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Number.TryParse", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures arrays of generated reference types preserve pointer element semantics through native array helpers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithReferenceTypeArrayReadWriteHelpers_UsesPointerElementArrays() {
            string source = """
                using System;

                public class Reader {
                    public T[] ReadArray<T>(Func<Reader, T> readElement) {
                        return null;
                    }
                }

                public class Writer {
                    public void WriteArray<T>(T[] values, Action<Writer, T> writeElement) {
                    }
                }

                public class Node {
                }

                public class GraphAsset {
                    public Node[] Items;
                }

                public static class Serializer {
                    public static Node ReadNode(Reader reader) {
                        return new Node();
                    }

                    public static void WriteNode(Writer writer, Node value) {
                    }

                    public static GraphAsset ReadGraph(Reader reader) {
                        return new GraphAsset {
                            Items = reader.ReadArray(ReadNode) ?? Array.Empty<Node>()
                        };
                    }

                    public static void WriteGraph(Writer writer, GraphAsset asset) {
                        writer.WriteArray(asset.Items, WriteNode);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string graphHeader = File.ReadAllText(Path.Combine(output.OutputPath, "GraphAsset.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Serializer.cpp"));

            Assert.Contains("Array<::Node*>* Items;", graphHeader);
            Assert.Contains("reader->ReadArray<Node*>(new Func<Reader*, Node*>(&Serializer::ReadNode))", sourceOutput);
            Assert.Contains("Array<Node*>::Empty()", sourceOutput);
            Assert.Contains("writer->WriteArray<Node*>(asset->Items, new Action<Writer*, Node*>(&Serializer::WriteNode))", sourceOutput);
        }

        /// <summary>
        /// Ensures collection-expression arguments targeting list-family interfaces lower through the native list runtime instead of synthetic arrays.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCollectionExpressionArgumentForReadOnlyList_UsesNativeListConstruction() {
            string source = """
                using System.Collections.Generic;

                public class TextContent {
                }

                public interface IContentProcessor<T> {
                }

                public class TextContentProcessor : IContentProcessor<TextContent> {
                }

                public class Widget {
                    public void RegisterProcessor<T>(
                        string processorId,
                        IContentProcessor<T> processor,
                        IReadOnlyList<string> extensions = null) {
                    }

                    public void RegisterBuiltInProcessors() {
                        RegisterProcessor("core.text-content", new TextContentProcessor(), ["*"]);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("new List<std::string>({ \"*\" })", sourceOutput);
            Assert.DoesNotContain("new Array<std::string>", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures null-coalescing array expressions lower to valid native C++ instead of leaking the C# coalesce operator.
        /// </summary>
        [Fact]
        public void WriteOutput_WithArrayNullCoalescing_UsesNativeNullGuardExpression() {
            string source = """
                using System;

                public struct Float3 {
                }

                public class Reader {
                    public T[] ReadArray<T>(Func<Reader, T> readElement) {
                        return null;
                    }
                }

                public class ModelAsset {
                    public Float3[] Positions;
                }

                public static class Serializer {
                    public static Float3 ReadFloat3(Reader reader) {
                        return default;
                    }

                    public static ModelAsset ReadModelAsset(Reader reader) {
                        return new ModelAsset {
                            Positions = reader.ReadArray(ReadFloat3) ?? Array.Empty<Float3>()
                        };
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Serializer.cpp"));

            Assert.DoesNotContain("??", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("Array<Float3>::Empty()", sourceOutput);
            Assert.Contains("__coalesce", sourceOutput);
        }

        /// <summary>
        /// Ensures nameof lowers inside generic processor methods that include interface implementation and type constraints.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericProcessorGuard_EmitsStringLiteralNameof() {
            string source = """
                using System;
                using System.IO;

                public interface IContentProcessor {
                    object ReadObject(Stream stream);
                }

                public interface IContentProcessor<T> : IContentProcessor {
                    T Read(Stream stream);
                }

                public class Asset {
                }

                public class AssetContentProcessor<TAsset> : IContentProcessor<TAsset> where TAsset : Asset {
                    public TAsset Read(Stream stream) {
                        if (stream == null) {
                            throw new ArgumentNullException(nameof(stream));
                        }

                        return default;
                    }

                    object IContentProcessor.ReadObject(Stream stream) {
                        return Read(stream);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "AssetContentProcessor_1.cpp"));

            Assert.Contains("throw new ArgumentNullException(\"stream\");", sourceOutput);
            Assert.DoesNotContain("nameof(stream)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"TAsset.hpp\"", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nameof lowers inside null-coalescing throw expressions used by generic processor constructors.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericProcessorCoalesceThrow_EmitsStringLiteralNameof() {
            string source = """
                using System;
                using System.IO;

                public class BinaryContentProcessor<T> {
                    readonly Func<Stream, T> Reader;

                    public BinaryContentProcessor(Func<Stream, T> reader) {
                        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
                    }

                    public T Read(Stream stream) {
                        if (stream == null) {
                            throw new ArgumentNullException(nameof(stream));
                        }

                        return Reader(stream);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "BinaryContentProcessor_1.cpp"));

            Assert.Contains("throw new ArgumentNullException(\"reader\")", sourceOutput);
            Assert.Contains("throw new ArgumentNullException(\"stream\");", sourceOutput);
            Assert.DoesNotContain("nameof(reader)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("nameof(stream)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures string coalesce expressions lower without pointer-style temporaries or raw question-mark operators.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringCoalesce_EmitsDirectStringValue() {
            string source = """
                public class Widget {
                    public string Normalize(string message) {
                        string text = message ?? string.Empty;
                        return text;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("const std::string text = message;", sourceOutput);
            Assert.DoesNotContain("object*", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("??", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures managed collection Count properties lower to callable native Count() helpers instead of member-field syntax.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedCollectionCountProperty_UsesNativeCountCall() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    readonly List<string> items = new List<string>();

                    public bool HasItems() {
                        return items.Count > 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("items->get_Count()", sourceOutput);
            Assert.DoesNotContain("items->Count >", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("items->Count == ", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures managed list Capacity property access and assignment lower to native helper calls instead of unsupported pseudo-fields.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedListCapacityProperty_UsesNativeCapacityHelpers() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    readonly List<string> items = new List<string>(4);

                    public string Grow(int desired) {
                        if (items.Capacity < desired) {
                            items.Capacity = desired;
                        }

                        return $"Capacity: {items.Capacity}";
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("items->get_Capacity()", sourceOutput);
            Assert.Contains("items->SetCapacity(desired)", sourceOutput);
            Assert.Contains("std::to_string(this->items->get_Capacity())", sourceOutput);
            Assert.DoesNotContain("items->Capacity = ", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures multi-local declarations and collection-expression array locals lower into valid native declarations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMultiLocalDeclarationAndArrayCollectionLocal_UsesSplitDeclarationsAndNativeArrayPointers() {
            string source = """
                public class Widget {
                    public int Sum(int width, int height) {
                        float cx = width * 0.5f, cy = height * 0.5f;
                        int[] values = [1, 2, 3];
                        return (int)(cx + cy + values.Length);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("const float cx = width * 0.5f;", sourceOutput);
            Assert.Contains("const float cy = height * 0.5f;", sourceOutput);
            Assert.Contains("Array<int32_t> *values = new Array<int32_t>({ 1, 2, 3 })", sourceOutput);
            Assert.DoesNotContain("cx, =", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("int32_t values[] = new Array", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures managed list Clear calls remain explicit method calls and compile against the native list runtime.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedListClearCall_UsesNativeClearCall() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    readonly List<string> items = new List<string>();

                    public void Reset() {
                        items.Clear();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("items->Clear()", sourceOutput);
        }

        /// <summary>
        /// Ensures non-escaping managed local allocations emit a scope-exit delete guard in generated C++.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNonEscapingManagedLocalAllocation_EmitsScopeDeleteGuard() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    public int Count() {
                        List<int> values = new List<int>();
                        values.Add(1);
                        values.Add(2);
                        return values.Count;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("List<int32_t> *values = new List<int32_t>();", sourceOutput);
            Assert.Contains("he_cpp_make_scope_exit", sourceOutput);
            Assert.Contains("delete values;", sourceOutput);
        }

        /// <summary>
        /// Ensures local managed arrays allocated inside a method are deleted on scope exit, including early return paths.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNonEscapingManagedArrayLocalAllocation_EmitsScopeDeleteGuard() {
            string source = """
                public class Widget {
                    public int Sum(bool exitEarly) {
                        int[] values = new int[4];
                        values[0] = 7;
                        if (exitEarly) {
                            return values[0];
                        }

                        return values[0] + values.Length;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("Array<int32_t> *values = new Array<int32_t>(4);", sourceOutput);
            Assert.Contains("he_cpp_make_scope_exit", sourceOutput);
            Assert.Contains("delete values;", sourceOutput);
        }

        /// <summary>
        /// Ensures managed locals that escape through a return value do not emit a scope-exit delete guard.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEscapingManagedLocalAllocation_DoesNotEmitScopeDeleteGuard() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    public List<int> Build() {
                        List<int> values = new List<int>();
                        return values;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("List<int32_t> *values = new List<int32_t>();", sourceOutput);
            Assert.DoesNotContain("delete values;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures local managed arrays that escape through a return value are not deleted before the caller receives them.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEscapingManagedArrayLocalAllocation_DoesNotEmitScopeDeleteGuard() {
            string source = """
                public class Widget {
                    public int[] Build() {
                        int[] values = new int[4];
                        return values;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("Array<int32_t> *values = new Array<int32_t>(4);", sourceOutput);
            Assert.DoesNotContain("delete values;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures managed locals passed into constructors are treated as escaping and do not emit a scope-exit delete guard.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedLocalPassedToConstructor_DoesNotEmitScopeDeleteGuard() {
            string source = """
                using System.Collections.Generic;

                public class Asset {
                    public Dictionary<char, int> Values;

                    public Asset(Dictionary<char, int> values) {
                        Values = values;
                    }
                }

                public class Widget {
                    public Asset Build() {
                        Dictionary<char, int> values = new Dictionary<char, int>();
                        return new Asset(values);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("Dictionary<char, int32_t> *values = new Dictionary<char, int32_t>();", sourceOutput);
            Assert.DoesNotContain("delete values;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("new ::Asset(values)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures managed builder locals passed to ordinary helper methods remain scoped to the caller and are deleted on scope exit.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedLocalPassedToHelperMethod_EmitsScopeDeleteGuard() {
            string source = """
                using System.Collections.Generic;

                public sealed class NativeNoEscapeAttribute : System.Attribute {
                }

                public class Widget {
                    public int Count() {
                        List<int> values = new List<int>();
                        Dictionary<string, int> indexes = new Dictionary<string, int>();
                        AddValues(values, indexes);
                        return values.Count;
                    }

                    static void AddValues([NativeNoEscape] List<int> values, [NativeNoEscape] Dictionary<string, int> indexes) {
                        values.Add(1);
                        indexes["one"] = 1;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("List<int32_t> *values = new List<int32_t>();", sourceOutput);
            Assert.Contains("Dictionary<std::string, int32_t> *indexes = new Dictionary<std::string, int32_t>();", sourceOutput);
            Assert.Contains("delete values;", sourceOutput);
            Assert.Contains("delete indexes;", sourceOutput);
            Assert.Contains("AddValues(values, indexes);", sourceOutput);
        }

        /// <summary>
        /// Ensures array-backed read-only list getters are treated as owned native return values when stored in caller locals.
        /// </summary>
        [Fact]
        public void WriteOutput_WithArrayBackedReadOnlyListGetterLocal_EmitsScopeDeleteGuard() {
            string source = """
                using System.Collections.Generic;

                public class Source {
                    readonly int[] items;

                    public Source(int[] items) {
                        this.items = items;
                    }

                    public IReadOnlyList<int> Items {
                        get {
                            return items;
                        }
                    }
                }

                public class Widget {
                    public int Count(Source source) {
                        IReadOnlyList<int> items = source.Items;
                        return items.Count;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));
            string providerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Source.cpp"));

            Assert.Contains("return new List<int32_t>(this->items);", providerOutput);
            Assert.Contains("List<int32_t> *items = source->get_Items();", sourceOutput);
            Assert.Contains("he_cpp_make_scope_exit", sourceOutput);
            Assert.Contains("delete items;", sourceOutput);
        }

        /// <summary>
        /// Ensures managed locals passed to unannotated helper methods remain conservatively treated as escaping.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedLocalPassedToUnannotatedHelperMethod_DoesNotEmitScopeDeleteGuard() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    public void Run() {
                        List<int> values = new List<int>();
                        Store(values);
                    }

                    static void Store(List<int> values) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("List<int32_t> *values = new List<int32_t>();", sourceOutput);
            Assert.DoesNotContain("delete values;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures managed locals with explicit NativeOwnership cleanup do not receive a second generated scope delete guard.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitNativeOwnershipDelete_DoesNotEmitScopeDeleteGuard() {
            string source = """
                using System;
                using System.Collections.Generic;

                public static class NativeOwnership {
                    public static void Delete<T>(T value) {
                    }
                }

                public class Widget {
                    public void Run() {
                        List<int> values = new List<int>();
                        try {
                            values.Add(1);
                        } finally {
                            NativeOwnership.Delete(values);
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("List<int32_t> *values = new List<int32_t>();", sourceOutput);
            Assert.Contains("delete values;", sourceOutput);
            Assert.DoesNotContain("__localDeleteGuard", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures method-group delegates passed to EngineBinaryReader.ReadArray lower through a scoped temporary that is deleted after the call.
        /// </summary>
        [Fact]
        public void WriteOutput_WithReadArrayMethodGroupArgument_EmitsScopedDelegateTemporary() {
            string source = """
                public class EngineBinaryReader {
                    public T[] ReadArray<T>(System.Func<EngineBinaryReader, T> readElement) {
                        return null;
                    }
                }

                public class Widget {
                    public int[] Read(EngineBinaryReader reader) {
                        return reader.ReadArray(ReadValue);
                    }

                    static int ReadValue(EngineBinaryReader reader) {
                        return 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("= new Func<EngineBinaryReader*, int32_t>(&Widget::ReadValue);", sourceOutput);
            Assert.Contains("he_cpp_make_scope_exit", sourceOutput);
            Assert.Contains("delete __delegateArg", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("ReadArray(new Func<EngineBinaryReader*, int32_t>(&Widget::ReadValue))", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures method-group delegates passed to EngineBinaryWriter.WriteArray lower through a scoped temporary that is deleted after the call.
        /// </summary>
        [Fact]
        public void WriteOutput_WithWriteArrayMethodGroupArgument_EmitsScopedDelegateTemporary() {
            string source = """
                public class EngineBinaryWriter {
                    public void WriteArray<T>(T[] values, System.Action<EngineBinaryWriter, T> writeElement) {
                    }
                }

                public class Widget {
                    public void Write(EngineBinaryWriter writer, int[] values) {
                        writer.WriteArray(values, WriteValue);
                    }

                    static void WriteValue(EngineBinaryWriter writer, int value) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("= new Action<EngineBinaryWriter*, int32_t>(&Widget::WriteValue);", sourceOutput);
            Assert.Contains("he_cpp_make_scope_exit", sourceOutput);
            Assert.Contains("delete __delegateArg", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("WriteArray(values, new Action<EngineBinaryWriter*, int32_t>(&Widget::WriteValue))", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures scoped delegate temporaries used by ReadArray survive member-assignment lowering and are declared before the generated assignment statement.
        /// </summary>
        [Fact]
        public void WriteOutput_WithReadArrayAssignment_EmitsDelegateTemporaryBeforeAssignment() {
            string source = """
                public class EngineBinaryReader {
                    public T[] ReadArray<T>(System.Func<EngineBinaryReader, T> readElement) {
                        return null;
                    }
                }

                public class Asset {
                    public int[] Values;
                }

                public class Widget {
                    public Asset Read(EngineBinaryReader reader) {
                        Asset asset = new Asset();
                        asset.Values = reader.ReadArray(ReadValue);
                        return asset;
                    }

                    static int ReadValue(EngineBinaryReader reader) {
                        return 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("auto __delegateArg", sourceOutput);
            Assert.Contains("delete __delegateArg", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("ReadArray<int32_t>(__delegateArg", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures scoped delegate temporaries used by ReadArray survive object-initializer member assignments.
        /// </summary>
        [Fact]
        public void WriteOutput_WithReadArrayObjectInitializer_EmitsDelegateTemporaryBeforeInitializerAssignment() {
            string source = """
                public class EngineBinaryReader {
                    public T[] ReadArray<T>(System.Func<EngineBinaryReader, T> readElement) {
                        return null;
                    }
                }

                public class Asset {
                    public int[] Values;
                }

                public class Widget {
                    public Asset Read(EngineBinaryReader reader) {
                        return new Asset {
                            Values = reader.ReadArray(ReadValue)
                        };
                    }

                    static int ReadValue(EngineBinaryReader reader) {
                        return 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("auto __delegateArg", sourceOutput);
            Assert.Contains("delete __delegateArg", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("ReadArray<int32_t>(__delegateArg", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures scoped delegate temporaries used by ReadArray remain outside native coalesce temporaries so null-coalescing assignments stay syntactically valid.
        /// </summary>
        [Fact]
        public void WriteOutput_WithReadArrayMethodGroupCoalesce_EmitsDelegateTemporaryBeforeCoalesceAssignment() {
            string source = """
                using System;

                public abstract class EngineBinaryReader {
                    public abstract T[] ReadArray<T>(Func<EngineBinaryReader, T> readElement);
                }

                public sealed class BinaryReaderLE : EngineBinaryReader {
                    public override T[] ReadArray<T>(Func<EngineBinaryReader, T> readElement) {
                        return null;
                    }
                }

                public sealed class BinaryReaderBE : EngineBinaryReader {
                    public override T[] ReadArray<T>(Func<EngineBinaryReader, T> readElement) {
                        return null;
                    }
                }

                public static class ReaderFactory {
                    public static EngineBinaryReader Create(bool useBigEndian) {
                        if (useBigEndian) {
                            return new BinaryReaderBE();
                        }

                        return new BinaryReaderLE();
                    }
                }

                public class Node {
                }

                public class Asset {
                    public Node[] Values;
                }

                public class Widget {
                    public Asset Read(bool useBigEndian) {
                        EngineBinaryReader reader = ReaderFactory.Create(useBigEndian);
                        return new Asset {
                            Values = reader.ReadArray(ReadValue) ?? Array.Empty<Node>()
                        };
                    }

                    static Node ReadValue(EngineBinaryReader reader) {
                        return new Node();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("auto __delegateArg", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("ReadArray<Node*>(__delegateArg", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("Array<::Node*>* __coalesce", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("dynamic_cast<::BinaryReaderLE*>(reader)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("dynamic_cast<::BinaryReaderBE*>(reader)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("([&]() -> Array<::Node*>*", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("= auto __delegateArg", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures repeated out-var names in disjoint branch scopes do not lower to duplicate native declarations in the same outer function scope.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRepeatedOutVarAcrossBranches_UsesDistinctScopedNames() {
            string source = """
                public enum Mode {
                    None
                }

                public static class Reader {
                    public static Mode Read(int value, out int mask) {
                        mask = value;
                        return Mode.None;
                    }
                }

                public class Widget {
                    public int Run(bool allow, bool always, bool never) {
                        if (allow) {
                            if (always) {
                                return 1;
                            } else if (never) {
                                return 2;
                            } else {
                                Mode mode = Reader.Read(1, out var mask);
                                return mask;
                            }
                        } else {
                            if (always) {
                                return 3;
                            } else if (never) {
                                return 4;
                            } else {
                                Mode mode = Reader.Read(2, out var mask);
                                return mask;
                            }
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("Reader::Read__out1(1, ", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("Reader::Read__out1(2, ", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("int32_t mask;\r\nint32_t mask;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures repeated out-var names across compile-time generic branch specializations do not collapse into duplicate native declarations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRepeatedOutVarAcrossGenericTypeBranches_UsesRemappedNames() {
            string source = """
                public enum Mode {
                    None
                }

                public interface IBatchPoseIntegrationAllowed {
                }

                public interface IBatchIntegrationMode {
                }

                public struct AllowPoseIntegration : IBatchPoseIntegrationAllowed {
                }

                public struct DisallowPoseIntegration : IBatchPoseIntegrationAllowed {
                }

                public struct BatchShouldAlwaysIntegrate : IBatchIntegrationMode {
                }

                public struct BatchShouldNeverIntegrate : IBatchIntegrationMode {
                }

                public struct BatchShouldConditionallyIntegrate : IBatchIntegrationMode {
                }

                public static class Reader {
                    public static Mode Read(int value, int flags, out int integrationMask) {
                        integrationMask = value + flags;
                        return Mode.None;
                    }
                }

                public class Widget {
                    public void Run<TAllowPoseIntegration, TBatchIntegrationMode>(int flags) {
                        if (typeof(TAllowPoseIntegration) == typeof(AllowPoseIntegration)) {
                            if (typeof(TBatchIntegrationMode) == typeof(BatchShouldAlwaysIntegrate)) {
                                return;
                            } else if (typeof(TBatchIntegrationMode) == typeof(BatchShouldNeverIntegrate)) {
                                return;
                            } else {
                                Mode mode = Reader.Read(1, flags, out var integrationMask);
                            }
                        } else {
                            if (typeof(TBatchIntegrationMode) == typeof(BatchShouldAlwaysIntegrate)) {
                                return;
                            } else if (typeof(TBatchIntegrationMode) == typeof(BatchShouldNeverIntegrate)) {
                                return;
                            } else {
                                Mode mode = Reader.Read(2, flags, out var integrationMask);
                            }
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("Reader::Read__out2(1, flags, ", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("Reader::Read__out2(2, flags, ", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("int32_t integrationMask;\r\nint32_t integrationMask;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures pointer-backed using declarations dispose and delete the generated resource on scope exit.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerUsingDeclaration_EmitsDisposeAndDeleteCleanup() {
            string source = """
                using System;

                public class Reader : IDisposable {
                    public void Dispose() {
                    }

                    public int Read() {
                        return 1;
                    }
                }

                public class Widget {
                    public int Load() {
                        using Reader reader = new Reader();
                        return reader.Read();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("reader->Dispose();", sourceOutput);
            Assert.Contains("delete reader;", sourceOutput);
        }

        /// <summary>
        /// Ensures pointer-backed using statements dispose and delete the generated resource at the end of the using block.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerUsingStatement_EmitsDisposeAndDeleteCleanup() {
            string source = """
                using System;

                public class Reader : IDisposable {
                    public void Dispose() {
                    }

                    public int Read() {
                        return 1;
                    }
                }

                public class Widget {
                    public int Load() {
                        using (Reader reader = new Reader()) {
                            return reader.Read();
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("reader->Dispose();", sourceOutput);
            Assert.Contains("delete reader;", sourceOutput);
        }

        /// <summary>
        /// Ensures nongeneric explicit interface implementations remain emitted when they satisfy native abstract interface contracts.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitDisposableImplementation_EmitsDisposeOverride() {
            string source = """
                using System;

                public class Reader : IDisposable {
                    void IDisposable.Dispose() {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Reader.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Reader.cpp"));

            Assert.Contains("class Reader : public IDisposable", headerOutput);
            Assert.Contains("void Dispose();", headerOutput);
            Assert.Contains("void Reader::Dispose()", sourceOutput);
        }

        /// <summary>
        /// Ensures inline list temporaries passed to HlslShaderBindingParser.ParseBindings lower through a scoped temporary that is deleted after the call.
        /// </summary>
        [Fact]
        public void WriteOutput_WithParseBindingsInlineListArgument_EmitsScopedTemporary() {
            string source = """
                using System.Collections.Generic;

                public class ShaderDefine {
                }

                public class ShaderBinding {
                }

                public class ShaderBindingPolicy {
                }

                public class HlslShaderBindingParser {
                    public static ShaderBinding[] ParseBindings(string source, ShaderBindingPolicy bindingPolicy) {
                        return ParseBindings(source, bindingPolicy, new List<ShaderDefine>());
                    }

                    public static ShaderBinding[] ParseBindings(string source, ShaderBindingPolicy bindingPolicy, List<ShaderDefine> defines) {
                        return null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "HlslShaderBindingParser.cpp"));

            Assert.Contains("__scopedArg", sourceOutput);
            Assert.Contains("delete __scopedArg", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("ParseBindings(source, bindingPolicy, new List<ShaderDefine*>())", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures inline list temporaries passed to HlslShaderBindingParser.ComputeConstantBufferSize lower through a scoped temporary that is deleted after the call.
        /// </summary>
        [Fact]
        public void WriteOutput_WithComputeConstantBufferSizeInlineListArgument_EmitsScopedTemporary() {
            string source = """
                using System.Collections.Generic;

                public class ShaderConstantMember {
                }

                public class HlslShaderBindingParser {
                    static int ComputeConstantBufferSize(List<ShaderConstantMember> members) {
                        return members.Count;
                    }

                    static int Measure(ShaderConstantMember[] members) {
                        return ComputeConstantBufferSize(new List<ShaderConstantMember>(members));
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "HlslShaderBindingParser.cpp"));

            Assert.Contains("__scopedArg", sourceOutput);
            Assert.Contains("delete __scopedArg", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("ComputeConstantBufferSize(new List<ShaderConstantMember*>", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures inline separator arrays passed to string.Split lower through a scoped temporary that is deleted after the call.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringSplitInlineSeparatorArray_EmitsScopedTemporary() {
            string source = """
                public class Widget {
                    public string[] Split(string value) {
                        return value.Split(new[] { ' ', '\t' }, 2, System.StringSplitOptions.RemoveEmptyEntries);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("__scopedArg", sourceOutput);
            Assert.Contains("delete __scopedArg", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("String::Split(value, new Array<char>({ ' ', '\\t' })", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures scope-deleted managed locals that are reassigned to a fresh heap allocation delete the previous value before overwriting the pointer.
        /// </summary>
        [Fact]
        public void WriteOutput_WithScopeDeletedManagedLocalReassignment_DeletesPreviousValueBeforeOverwrite() {
            string source = """
                public class StringBuilder {
                    public StringBuilder() {
                    }

                    public StringBuilder(string value) {
                    }
                }

                public class Widget {
                    public void Replace() {
                        StringBuilder wrappedText = new StringBuilder();
                        wrappedText = new StringBuilder("next");
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("auto __reassignValue", sourceOutput);
            Assert.Contains("delete wrappedText;", sourceOutput);
            Assert.Contains("wrappedText = __reassignValue", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures string static helpers lower through the native string runtime surface.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringStaticHelpers_UsesNativeStringRuntime() {
            string source = """
                public class Widget {
                    public string Reset() {
                        return string.Empty;
                    }

                    public bool HasText(string value) {
                        return !string.IsNullOrWhiteSpace(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("runtime/native_string.hpp", headerOutput);
            Assert.Contains("String::Empty", sourceOutput);
            Assert.Contains("String::IsNullOrWhiteSpace(value)", sourceOutput);
        }

        /// <summary>
        /// Ensures string null checks lower through a runtime helper that exists on the emitted native string surface.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringNullCheck_EmitsRuntimeIsNullOrEmptyHelper() {
            string source = """
                public class Widget {
                    public bool IsMissing(string value) {
                        return value == null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_string.hpp"));

            Assert.Contains("String::IsNullOrEmpty(value)", sourceOutput);
            Assert.Contains("static bool IsNullOrEmpty", runtimeHeader);
        }

        /// <summary>
        /// Ensures methods that return managed strings coalesce explicit null returns to the native managed empty-string singleton instead of emitting an invalid <c>std::string</c> null return.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringNullReturn_UsesManagedEmptyString() {
            string source = """
                public class Reader {
                    public string ReadString() {
                        return null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Reader.cpp"));

            Assert.Contains("return String::Empty;", sourceOutput);
            Assert.DoesNotContain("return nullptr;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated body-only generic type arguments register header dependencies before emission so pointer-only generated references can rely on forward declarations without post-generation repair.
        /// </summary>
        [Fact]
        public void WriteOutput_WithBodyOnlyGenericTypeArgument_EmitsForwardDeclarationInHeader() {
            string source = """
                public class MenuSelectedDescriptionComponent {
                }

                public class MenuItemComponent {
                }

                public class MenuComponent {
                    T Find<T>() {
                        return default;
                    }

                    public bool HasSelection() {
                        return Find<MenuSelectedDescriptionComponent>() != null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "MenuComponent.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "MenuComponent.cpp"));

            Assert.DoesNotContain("class MenuSelectedDescriptionComponent;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("Find<MenuSelectedDescriptionComponent*>()", sourceOutput);
        }

        /// <summary>
        /// Ensures StringComparer static members lower through a native comparer runtime token instead of synthetic type leakage.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringComparerStaticMember_UsesNativeStringComparerRuntime() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    readonly Dictionary<string, string> values;

                    public Widget() {
                        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("StringComparer::get_OrdinalIgnoreCase()", sourceOutput);
            Assert.DoesNotContain("#include \"StringComparer.hpp\"", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures C# double-suffixed literals normalize to valid native numeric literals inside nested Math expressions.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDoubleSuffixLiterals_EmitsNativeNumericLiterals() {
            string source = """
                using System;

                public class FontAsset {
                    public int LineHeight { get; set; }
                }

                public class Widget {
                    FontAsset Font;

                    public float Radius(int x, int y) {
                        return (float)(Math.Min(x, y) * 0.15d);
                    }

                    public int Height() {
                        return Math.Max(1, (int)Math.Ceiling(Math.Max((double)Font.LineHeight, 1d)));
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("0.15", sourceOutput);
            Assert.Contains("Math::Max(static_cast<double>(this->Font->get_LineHeight()), 1.0)", sourceOutput);
            Assert.DoesNotContain("0.15d", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("1d", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures pointer-backed managed collection indexing lowers through native collection indexers instead of raw pointer arithmetic.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerBackedListIndexing_UsesDereferencedIndexerAccess() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    public List<string> Copy(IReadOnlyList<string> items) {
                        List<string> values = new List<string>(items.Count);
                        for (int i = 0; i < items.Count; i++) {
                            values.Add(items[i]);
                        }

                        return values;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("for (int32_t i = 0; i < items->get_Count(); i++)", sourceOutput);
            Assert.Contains("values->Add((*items)[i])", sourceOutput);
            Assert.DoesNotContain("for (const int32_t i = 0;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("values->Add(items[i])", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generic list construction preserves generated pointer element types when creating runtime lists.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGeneratedTypeListConstruction_UsesPointerGenericArguments() {
            string source = """
                using System.Collections.Generic;

                public class Item {
                }

                public class Widget {
                    readonly List<Item> items;

                    public Widget(int count) {
                        items = new List<Item>(count);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("List<::Item*>* items;", headerOutput);
            Assert.Contains("new List<::Item*>(count)", sourceOutput);
            Assert.DoesNotContain("new List<Item>(count)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures ArgumentOutOfRangeException runtime support includes the managed parameter-name and message overload shape.
        /// </summary>
        [Fact]
        public void WriteOutput_WithArgumentOutOfRangeExceptionParamAndMessage_UsesRuntimeOverload() {
            string source = """
                using System;

                public class Widget {
                    public void Validate(int value) {
                        if (value < 0) {
                            throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative.");
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_exceptions.hpp"));

            Assert.Contains("return new ArgumentOutOfRangeException(__ctor_arg_00000000, __ctor_arg_00000001);", sourceOutput);
            Assert.Contains("ArgumentOutOfRangeException(const std::string& parameterName, const std::string& message)", runtimeHeader);
        }

        /// <summary>
        /// Ensures ArgumentException exposes the managed message and parameter-name overload required by generated throws.
        /// </summary>
        [Fact]
        public void WriteOutput_WithArgumentExceptionMessageAndParameter_UsesRuntimeOverload() {
            string source = """
                using System;

                public class Widget {
                    public void Validate(string items) {
                        if (items == null) {
                            throw new ArgumentException("Items must be provided.", nameof(items));
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_exceptions.hpp"));

            Assert.Contains("return new ArgumentException(__ctor_arg_00000000, __ctor_arg_00000001);", sourceOutput);
            Assert.Contains("ArgumentException(const std::string& message, const std::string& parameterName)", runtimeHeader);
        }

        /// <summary>
        /// Ensures ArgumentNullException exposes the managed parameter-name and message overload required by generated throws.
        /// </summary>
        [Fact]
        public void WriteOutput_WithArgumentNullExceptionParameterAndMessage_UsesRuntimeOverload() {
            string source = """
                using System;

                public class Widget {
                    public void Validate(string value) {
                        if (value == null) {
                            throw new ArgumentNullException(nameof(value), "Value is required.");
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_exceptions.hpp"));

            Assert.Contains("return new ArgumentNullException(__ctor_arg_00000000, __ctor_arg_00000001);", sourceOutput);
            Assert.Contains("ArgumentNullException(const std::string& parameterName, const std::string& message)", runtimeHeader);
        }

        /// <summary>
        /// Ensures native ownership helper calls lower directly into explicit delete and dispose operations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNativeOwnershipHelpers_EmitsStructuredDeleteAndDisposeContracts() {
            string source = """
                using System;

                public static class NativeOwnership {
                    public static void Delete<T>(T value) where T : class {
                    }

                    public static void DisposeAndDelete<T>(T value) where T : class, IDisposable {
                        value?.Dispose();
                    }

                    public static void Release<T>(ref T value) where T : class {
                        value = null;
                    }

                    public static void DisposeAndRelease<T>(ref T value) where T : class, IDisposable {
                        if (value != null) {
                            value.Dispose();
                        }

                        value = null;
                    }
                }

                public class Child : IDisposable {
                    public void Dispose() {
                    }
                }

                public class Widget {
                    public Child Owned { get; set; }
                    public Child Source;

                    public void Clear(Child child) {
                        NativeOwnership.Delete(child);
                        NativeOwnership.DisposeAndDelete(child);
                        NativeOwnership.Release(ref Source);
                        NativeOwnership.DisposeAndRelease(ref Owned);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("delete child;", sourceOutput);
            Assert.Contains("if (child != nullptr)", sourceOutput);
            Assert.Contains("child->Dispose();", sourceOutput);
            Assert.Contains("delete this->Source;", sourceOutput);
            Assert.Contains("this->Source = nullptr;", sourceOutput);
            Assert.Contains("if (this->Owned != nullptr)", sourceOutput);
            Assert.Contains("this->Owned->Dispose();", sourceOutput);
            Assert.Contains("delete this->Owned;", sourceOutput);
            Assert.Contains("this->set_Owned(nullptr);", sourceOutput);
        }

        /// <summary>
        /// Ensures the generated NativeOwnership helper implementation preserves the same delete and dispose semantics as direct call-site lowering.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNativeOwnershipHelpers_EmitsStructuredDeleteAndDisposeContractsInNativeOwnershipSource() {
            string source = """
                using System;

                public static class NativeOwnership {
                    public static void Delete<T>(T value) where T : class {
                    }

                    public static void DisposeAndDelete<T>(T value) where T : class, IDisposable {
                        value?.Dispose();
                    }

                    public static void Release<T>(ref T value) where T : class {
                        value = null;
                    }

                    public static void DisposeAndRelease<T>(ref T value) where T : class, IDisposable {
                        if (value != null) {
                            value.Dispose();
                        }

                        value = null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string nativeOwnershipSource = File.ReadAllText(Path.Combine(output.OutputPath, "NativeOwnership.cpp"));

            Assert.Contains("void NativeOwnership::Delete(T value)", nativeOwnershipSource);
            Assert.Contains("delete value;", nativeOwnershipSource);
            Assert.Contains("void NativeOwnership::DisposeAndDelete(T value)", nativeOwnershipSource);
            Assert.Contains("value->Dispose();", nativeOwnershipSource);
            Assert.Contains("void NativeOwnership::Release(T& value)", nativeOwnershipSource);
            Assert.Contains("value = nullptr;", nativeOwnershipSource);
        }

        /// <summary>
        /// Ensures string-typed null and default assignments lower to empty native strings instead of pointer literals so generated unload and disposal paths remain valid for std::string-backed members.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringNullAssignments_EmitsEmptyNativeStringsInsteadOfNullptr() {
            string source = """
                using System;

                public sealed class Widget {
                    public string Name { get; set; }

                    public void Clear() {
                        string local = null;
                        Name = null;
                        local = default;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("std::string local = std::string();", sourceOutput);
            Assert.Contains("this->set_Name(std::string());", sourceOutput);
            Assert.Contains("local = std::string();", sourceOutput);
            Assert.DoesNotContain("this->set_Name(nullptr);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("std::string local = nullptr;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("local = nullptr;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures NativeOwnership lowering still emits direct delete operations when a local type name collides with a member name in class scope.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNativeOwnershipOnLocalTypeMatchingMemberName_EmitsDirectDeleteWithoutHelperCall() {
            string source = """
                namespace helengine {
                    public class FontInfo {
                    }

                    public static class NativeOwnership {
                        public static void Delete<T>(T value) where T : class {
                        }
                    }

                    public class FontAsset {
                        public FontInfo FontInfo { get; set; }

                        public void Dispose() {
                            FontInfo fontInfo = FontInfo;
                            FontInfo = null;
                            NativeOwnership.Delete(fontInfo);
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "FontAsset.cpp"));

            Assert.Contains("FontInfo *fontInfo", sourceOutput);
            Assert.Contains("delete fontInfo;", sourceOutput);
            Assert.DoesNotContain("NativeOwnership::Delete", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures typed out-variable declarations in compound conditions are hoisted into the surrounding scope before the invocation.
        /// </summary>
        [Fact]
        public void WriteOutput_WithTypedOutDeclarationInCompoundCondition_HoistsDeclarationBeforeCondition() {
            string source = """
                public class RuntimeSceneCatalogEntry {
                    public string CookedRelativePath { get; set; }
                }

                public class RuntimeSceneCatalog {
                    public bool TryGetEntry(string sceneId, out RuntimeSceneCatalogEntry entry) {
                        entry = null;
                        return false;
                    }
                }

                public class SceneManager {
                    RuntimeSceneCatalog SceneCatalog;

                    public string Resolve(string sceneId) {
                        if (SceneCatalog != null && SceneCatalog.TryGetEntry(sceneId, out RuntimeSceneCatalogEntry entry)) {
                            return entry.CookedRelativePath;
                        }

                        return string.Empty;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "SceneManager.cpp"));

            Assert.Contains("::RuntimeSceneCatalogEntry* entry;", sourceOutput);
            Assert.Contains("if (this->SceneCatalog != nullptr && this->SceneCatalog->TryGetEntry(sceneId, entry))", sourceOutput);
            Assert.DoesNotContain("out RuntimeSceneCatalogEntry entry", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures attributed partial-method hooks lower into native free-function calls and source includes without emitting a dead generated member definition.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNativeFreeFunctionPartialMethod_UsesFreeFunctionCallAndSkipsStubEmission() {
            string source = """
                using System;

                [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
                internal sealed class NativeFreeFunctionAttribute : Attribute {
                    public NativeFreeFunctionAttribute(string functionName, string includePath) {
                    }
                }

                public sealed class RuntimeComponentRegistry {
                    public static RuntimeComponentRegistry CreateDefault() {
                        RuntimeComponentRegistry registry = new RuntimeComponentRegistry();
                        RegisterGeneratedRuntimeComponentDeserializers(registry);
                        return registry;
                    }

                    [NativeFreeFunction("RegisterGeneratedRuntimeComponentDeserializers", "GeneratedRuntimeComponentDeserializerRegistration.hpp")]
                    static void RegisterGeneratedRuntimeComponentDeserializers(RuntimeComponentRegistry registry) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RuntimeComponentRegistry.cpp"));
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RuntimeComponentRegistry.hpp"));

            Assert.Contains("#include \"GeneratedRuntimeComponentDeserializerRegistration.hpp\"", sourceOutput);
            Assert.Contains("RegisterGeneratedRuntimeComponentDeserializers(registry);", sourceOutput);
            Assert.DoesNotContain("RuntimeComponentRegistry::RegisterGeneratedRuntimeComponentDeserializers", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("RegisterGeneratedRuntimeComponentDeserializers(", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures native free-function registration hooks emit the companion generated registration support files expected by the generated runtime component registry.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNativeFreeFunctionPartialMethod_EmitsGeneratedRegistrationSupportFiles() {
            string source = """
                using System;

                [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
                internal sealed class NativeFreeFunctionAttribute : Attribute {
                    public NativeFreeFunctionAttribute(string functionName, string includePath) {
                    }
                }

                public sealed class RuntimeComponentRegistry {
                    public static RuntimeComponentRegistry CreateDefault() {
                        RuntimeComponentRegistry registry = new RuntimeComponentRegistry();
                        RegisterGeneratedRuntimeComponentDeserializers(registry);
                        return registry;
                    }

                    [NativeFreeFunction("RegisterGeneratedRuntimeComponentDeserializers", "GeneratedRuntimeComponentDeserializerRegistration.hpp")]
                    static void RegisterGeneratedRuntimeComponentDeserializers(RuntimeComponentRegistry registry) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerPath = Path.Combine(output.OutputPath, "GeneratedRuntimeComponentDeserializerRegistration.hpp");
            string sourcePath = Path.Combine(output.OutputPath, "GeneratedRuntimeComponentDeserializerRegistration.cpp");

            Assert.True(File.Exists(headerPath));
            Assert.True(File.Exists(sourcePath));
            Assert.Contains("void RegisterGeneratedRuntimeComponentDeserializers(::RuntimeComponentRegistry* registry);", File.ReadAllText(headerPath));
            Assert.Contains("void RegisterGeneratedRuntimeComponentDeserializers(::RuntimeComponentRegistry* registry)", File.ReadAllText(sourcePath));
        }

        /// <summary>
        /// Ensures managed array helper and allocation patterns lower through the native Array runtime surface.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedArrayHelpers_UsesNativeArrayRuntimeShapes() {
            string source = """
                using System;
                using System.Collections.Generic;

                public class ContentProcessorRegistration {
                    readonly string[] ExtensionsValue;

                    public ContentProcessorRegistration(IReadOnlyList<string> extensions) {
                        ExtensionsValue = extensions == null ? Array.Empty<string>() : NormalizeExtensions(extensions);
                    }

                    string[] NormalizeExtensions(IReadOnlyList<string> sourceExtensions) {
                        string[] normalized = new string[sourceExtensions.Count];
                        normalized[0] = sourceExtensions[0];
                        return normalized;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ContentProcessorRegistration.cpp"));
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "array.hpp"));

            Assert.Contains("Array<std::string>::Empty()", sourceOutput);
            Assert.Contains("new Array<std::string>(sourceExtensions->get_Count())", sourceOutput);
            Assert.Contains("(*normalized)[0] = (*sourceExtensions)[0];", sourceOutput);
            Assert.Contains("static Array<T>* Empty()", runtimeHeader);
        }

        /// <summary>
        /// Ensures static managed Array resize and ranged copy helpers lower through the native generic Array runtime owner.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedArrayResizeAndRangedCopy_UsesNativeGenericArrayStatics() {
            string source = """
                using System;

                public class ResizeGate {
                    int[] values;

                    public void EnsureCapacity(int count) {
                        Array.Resize(ref values, count);
                    }

                    public void ShiftRight(int index, int count) {
                        Array.Copy(values, index, values, index + 1, count);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "ResizeGate.cpp"));

            Assert.Contains("Array<int32_t>::Resize(this->values, count);", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("Array<int32_t>::Copy(this->values, index, this->values, index + 1, count);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Array::Resize", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Array::Copy", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures chained member access on a ref-return invocation preserves inline out-variable declarations before the call site.
        /// </summary>
        [Fact]
        public void WriteOutput_WithChainedRefReturnInvocationAndOutVar_DeclaresOutVariableBeforeReceiverCall() {
            string source = """
                public struct Slot {
                    public void Initialize() {
                    }
                }

                public struct Cache<T> {
                    public T Value;

                    public ref T Allocate(out int index) {
                        index = 1;
                        return ref Value;
                    }
                }

                public class Holder {
                    Cache<Slot> cache;

                    public void Run() {
                        cache.Allocate(out var index).Initialize();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Holder.cpp"));

            Assert.Contains("int32_t index;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("this->cache.Allocate__out0(index).Initialize();", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nested generated generic type references preserve emitted type names and captured outer generic parameters in type positions.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedGeneratedGenericTypeReference_UsesEmittedNestedTypeNamesAndCapturedOuterArguments() {
            string source = """
                public class Outer<T> {
                    public struct Item {
                        public int Value;
                    }

                    public struct Cache<TValue> {
                        public TValue Stored;
                    }

                    Cache<Item> cache;

                    public Cache<Item> Read() {
                        return cache;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Outer_1.hpp"));

            Assert.Contains("Cache_2<", headerOutput, StringComparison.Ordinal);
            Assert.Contains("Item_1<T>", headerOutput, StringComparison.Ordinal);
            Assert.Contains("Read();", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Cache<Item>", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures local declarations qualify generated types when a member with the same identifier exists in class scope.
        /// </summary>
        [Fact]
        public void WriteOutput_WithLocalGeneratedTypeMatchingMemberName_QualifiesLocalTypeReference() {
            string source = """
                using System.Collections.Generic;

                public class ContentManager {
                }

                public class Core {
                    public ContentManager ContentManager { get; set; }

                    Dictionary<string, ContentManager> managers;

                    public ContentManager Get(string root) {
                        if (managers.TryGetValue(root, out ContentManager contentManager)) {
                            return contentManager;
                        }

                        contentManager = new ContentManager();
                        return contentManager;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Core.cpp"));

            Assert.Contains("::ContentManager* contentManager;", sourceOutput);
            Assert.Contains("contentManager = new ::ContentManager()", sourceOutput);
        }

        /// <summary>
        /// Ensures string instance helpers and comparisons lower through the native string runtime instead of synthetic member calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStringInstanceHelpers_UsesNativeStringHelperCalls() {
            string source = """
                using System;

                public class Widget {
                    public string Normalize(string extension) {
                        if (string.Equals(extension, "*", StringComparison.Ordinal)) {
                            return extension;
                        }

                        if (!extension.StartsWith(".")) {
                            extension = "." + extension;
                        }

                        if (extension.Length > 3 && extension.EndsWith(".TXT", StringComparison.OrdinalIgnoreCase)) {
                            return extension.ToLowerInvariant();
                        }

                        return extension;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("String::Equals(extension, \"*\", StringComparison::Ordinal)", sourceOutput);
            Assert.Contains("String::StartsWith(extension, \".\")", sourceOutput);
            Assert.Contains("static_cast<int32_t>(extension.size()) > 3", sourceOutput);
            Assert.Contains("String::EndsWith(extension, \".TXT\", StringComparison::OrdinalIgnoreCase)", sourceOutput);
            Assert.Contains("return String::ToLowerInvariant(extension);", sourceOutput);
            Assert.DoesNotContain("extension.StartsWith", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("extension.EndsWith", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("extension.ToLowerInvariant", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("String::Length", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures constant string expressions used for <c>.Length</c> lower to integer literals instead of invalid C-string member access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithConstantStringLengthMemberAccess_EmitsIntegerLiteralLength() {
            string source = """
                public class Widget {
                    public string Extract(string text) {
                        return text.Substring("defined(".Length, text.Length - "defined(".Length - 1);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("String::Substring(text, 8, static_cast<int32_t>(text.size()) - 8 - 1)", sourceOutput);
            Assert.DoesNotContain("\"defined(\".size()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures numeric <c>var</c> locals infer float types when Roslyn recovery is weak across arithmetic and conditional expressions.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNumericVarInferenceFallback_EmitsFloatLocals() {
            string source = """
                using System;

                public class Widget {
                    public float Build(float fieldOfView, float aspectRatio, float nearPlaneDistance, float farPlaneDistance) {
                        var yScale = 1.0f / (float)Math.Tan((double)fieldOfView * 0.5f);
                        var xScale = yScale / aspectRatio;
                        var negFarRange = float.IsPositiveInfinity(farPlaneDistance) ? -1.0f : farPlaneDistance / (nearPlaneDistance - farPlaneDistance);
                        return xScale + yScale + negFarRange;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("float yScale = 1.0f / static_cast<float>(Math::Tan(static_cast<double>(fieldOfView) * 0.5f));", sourceOutput);
            Assert.Contains("float xScale = yScale / aspectRatio;", sourceOutput);
            Assert.Contains("float negFarRange = Number::IsPositiveInfinity(farPlaneDistance) ? -1.0f : farPlaneDistance / (nearPlaneDistance - farPlaneDistance);", sourceOutput);
            Assert.DoesNotContain("int32_t yScale", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("object *negFarRange", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures primitive instance <c>GetHashCode</c> calls lower through the numeric runtime instead of invalid native member calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPrimitiveGetHashCode_UsesNumberRuntimeHashing() {
            string source = """
                public struct Widget {
                    public float X;
                    public float Y;

                    public override int GetHashCode() {
                        int hash = X.GetHashCode();
                        hash = (hash * 397) ^ Y.GetHashCode();
                        return hash;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("int32_t hash = Number::GetHashCode(this->X);", sourceOutput);
            Assert.Contains("hash = (hash * 397) ^ Number::GetHashCode(this->Y);", sourceOutput);
            Assert.DoesNotContain(".GetHashCode()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures pointer-sized primitive <c>GetHashCode</c> calls lower through the numeric runtime instead of invalid native member calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerSizedPrimitiveGetHashCode_UsesNumberRuntimeHashing() {
            string source = """
                public struct Widget {
                    public nint Value;

                    public override int GetHashCode() {
                        return Value.GetHashCode();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("return Number::GetHashCode(this->Value);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(".GetHashCode()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures dependent generic <c>GetHashCode</c> calls lower through the portable runtime helper instead of invalid native member syntax for primitive instantiations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericGetHashCode_UsesPortableHashHelper() {
            string source = """
                public class Widget<T> {
                    public int Hash(T value) {
                        return value.GetHashCode();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget_1.cpp"));

            Assert.Contains("return he_cpp_get_hash_code(value);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("value.GetHashCode()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures pointer-to-int casts lower through a pointer-sized reinterpret cast instead of an invalid direct static_cast.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerToIntCast_UsesPointerSizedIntermediateCast() {
            string source = """
                public unsafe struct Widget {
                    public int* Pointer;

                    public int Read() {
                        return (int)Pointer;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("static_cast<int32_t>(reinterpret_cast<intptr_t>(this->Pointer))", sourceOutput);
            Assert.DoesNotContain("static_cast<int32_t>(this->Pointer)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures numeric casts from Math helpers do not route through intptr reinterpret casts.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNumericRoundCast_DoesNotUsePointerSizedReinterpretCast() {
            string source = """
                using System;

                public sealed class LayoutMath {
                    public int Snap(double value) {
                        return (int)Math.Round(value);
                    }

                    public int Measure(double value) {
                        return (int)Math.Ceiling(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "LayoutMath.cpp"));

            Assert.Contains("static_cast<int32_t>(Math::Round(value))", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("static_cast<int32_t>(Math::Ceiling(value))", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("reinterpret_cast<intptr_t>(Math::Round(value))", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("reinterpret_cast<intptr_t>(Math::Ceiling(value))", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures numeric casts from Math helpers still avoid intptr reinterpret casts when native runtime metadata is loaded.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNumericRoundCastAndNativeRuntimeMetadata_DoesNotUsePointerSizedReinterpretCast() {
            string source = """
                using System;

                public sealed class LayoutMath {
                    public int Snap(double value) {
                        return (int)Math.Round(value);
                    }

                    public int Measure(double value) {
                        return (int)Math.Ceiling(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, loadNativeRuntimeMetadata: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "LayoutMath.cpp"));

            Assert.Contains("static_cast<int32_t>(Math::Round(value))", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("static_cast<int32_t>(Math::Ceiling(value))", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("reinterpret_cast<intptr_t>(Math::Round(value))", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("reinterpret_cast<intptr_t>(Math::Ceiling(value))", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures function-pointer wrapper invocations use the wrapper call surface instead of delegate-style dereferencing.
        /// </summary>
        [Fact]
        public void WriteOutput_WithFunctionPointerInvocation_UsesWrapperCallSurface() {
            string source = """
                public unsafe struct Task {
                    public delegate*<int, void> Function;

                    public void Invoke(int value) {
                        Function(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Task.cpp"));

            Assert.Contains("this->Function(value);", sourceOutput);
            Assert.DoesNotContain("(*this->Function)(value)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures expression-bodied members returning implicit object creation expressions emit the constructed value.
        /// </summary>
        [Fact]
        public void WriteOutput_WithImplicitObjectCreationExpressionBody_EmitsReturnValue() {
            string source = """
                public enum Mode {
                    Off,
                    On
                }

                public struct Settings {
                    public Mode Mode;
                }

                public static class Factory {
                    public static Settings Create() => new() { Mode = Mode.On };
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Factory.cpp"));

            Assert.Contains("auto __object_", sourceOutput);
            Assert.Contains("= ::Settings();", sourceOutput);
            Assert.DoesNotContain("return ;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures implicit object creation targeting one qualified generic type can still render detached generic type syntax safely.
        /// </summary>
        [Fact]
        public void WriteOutput_WithImplicitQualifiedGenericObjectCreation_DoesNotCrashOnDetachedGenericTypeSyntax() {
            string source = """
                using System.Collections.Generic;

                public static class Factory {
                    public static global::System.Collections.Generic.List<int> Create(bool create) {
                        global::System.Collections.Generic.List<int> existing = new global::System.Collections.Generic.List<int>();
                        return create ? new() : existing;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Factory.cpp"));

            Assert.Contains("return create ? ", sourceOutput);
            Assert.Contains("System::Collections::Generic::List", sourceOutput);
        }

        /// <summary>
        /// Ensures implicit object creation with constructor arguments can lower detached argument expressions without asking Roslyn for symbols from a foreign tree.
        /// </summary>
        [Fact]
        public void WriteOutput_WithImplicitQualifiedGenericObjectCreationArguments_DoesNotCrashOnDetachedArgumentSyntax() {
            string source = """
                using System.Collections.Generic;

                public static class Factory {
                    public static global::System.Collections.Generic.List<int> Create(bool create, int capacity) {
                        global::System.Collections.Generic.List<int> existing = new global::System.Collections.Generic.List<int>();
                        return create ? new(capacity) : existing;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Factory.cpp"));

            Assert.Contains("capacity", sourceOutput);
            Assert.Contains("return create ? ", sourceOutput);
        }

        /// <summary>
        /// Ensures implicit object creation can lower detached unmanaged function pointer arguments without requesting Roslyn symbol info from a foreign tree.
        /// </summary>
        [Fact]
        public void WriteOutput_WithImplicitObjectCreationFunctionPointerArgument_DoesNotCrashOnDetachedAddressOfSyntax() {
            string source = """
                public unsafe struct CallbackHolder {
                    public delegate*<int, void> Callback;

                    public CallbackHolder(delegate*<int, void> callback) {
                        Callback = callback;
                    }
                }

                public static unsafe class Factory {
                    static void Handle(int value) {
                    }

                    public static CallbackHolder Create(bool create) {
                        CallbackHolder existing = new CallbackHolder(&Handle);
                        return create ? new(&Handle) : existing;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Factory.cpp"));

            Assert.Contains("&Handle", sourceOutput);
            Assert.Contains("return create ? ", sourceOutput);
        }

        /// <summary>
        /// Ensures unmanaged function-pointer address-of expressions emit one typed method pointer without taking the address of the cast result.
        /// </summary>
        [Fact]
        public void WriteOutput_WithFunctionPointerAddressOfMethod_EmitsSingleTypedMethodPointer() {
            string source = """
                public unsafe struct CallbackHolder {
                    public delegate*<int, void> Callback;

                    public CallbackHolder(delegate*<int, void> callback) {
                        Callback = callback;
                    }
                }

                public static unsafe class Factory {
                    static void Handle(int value) {
                    }

                    public static CallbackHolder Create() {
                        return new CallbackHolder(&Handle);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Factory.cpp"));

            Assert.Contains("static_cast<void (*)(int32_t)>(&Factory::Handle)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("&static_cast<void (*)(int32_t)>(&Factory::Handle)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures unmanaged function-pointer address-of expressions preserve closed generic method arguments so native overload resolution targets one concrete template instantiation.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericFunctionPointerAddressOfMethod_EmitsClosedGenericMethodTarget() {
            string source = """
                public unsafe struct CallbackHolder {
                    public delegate*<int, void> Callback;

                    public CallbackHolder(delegate*<int, void> callback) {
                        Callback = callback;
                    }
                }

                public static unsafe class Factory {
                    static void Handle<T>(int value) {
                    }

                    public static CallbackHolder Create() {
                        return new CallbackHolder(&Handle<int>);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Factory.cpp"));

            Assert.Contains("static_cast<void (*)(int32_t)>(&Factory::Handle<int32_t>)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("static_cast<void (*)(int32_t)>(&Factory::Handle)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures member access to one field is not redirected to an explicit-interface property with the same simple name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithFieldShadowedByExplicitInterfaceProperty_PrefersFieldMemberAccess() {
            string source = """
                public interface ICounter<T> {
                    int Count { get; }
                }

                public struct Counter : ICounter<Counter> {
                    public int Count;

                    int ICounter<Counter>.Count => Count;

                    public static void Reset(ref Counter counter) {
                        counter.Count = 0;
                    }

                    public static void Increment(ref Counter counter) {
                        counter.Count++;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Counter.cpp"));

            Assert.Contains("counter.Count = 0;", sourceOutput);
            Assert.Contains("counter.Count++;", sourceOutput);
            Assert.DoesNotContain("counter.get_Count()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures dictionary key iteration and TryGetValue out parameters lower to native C++ iteration and reference calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDictionaryKeysForeachAndTryGetValue_UsesNativeDictionaryShapes() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    bool TryResolve(
                        IReadOnlyDictionary<string, string> registrationsByExtension,
                        out string matchedExtension) {
                        matchedExtension = string.Empty;
                        foreach (string extension in registrationsByExtension.Keys) {
                            if (registrationsByExtension.TryGetValue(extension, out string registration)) {
                                matchedExtension = registration;
                            }
                        }

                        return matchedExtension != string.Empty;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("for (const auto& extension : registrationsByExtension->Keys())", sourceOutput);
            Assert.Contains("std::string registration;", sourceOutput);
            Assert.Contains("registrationsByExtension->TryGetValue(extension, registration)", sourceOutput);
            Assert.DoesNotContain("for (let ", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(" out_", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures locals later supplied to out parameters stay mutable so native declarations do not gain invalid const qualifiers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDeclaredOutLocal_DoesNotEmitConstDeclaration() {
            string source = """
                using System.Collections.Generic;

                public class Widget {
                    bool TryResolve(
                        IReadOnlyDictionary<string, string> registrationsByExtension,
                        string fileName) {
                        string extension;
                        if (!registrationsByExtension.TryGetValue(fileName, out extension)) {
                            return false;
                        }

                        return extension != string.Empty;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("std::string extension;", sourceOutput);
            Assert.DoesNotContain("const std::string extension;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("registrationsByExtension->TryGetValue(fileName, extension)", sourceOutput);
        }

        /// <summary>
        /// Ensures list construction, indexing, and for-loop locals emit valid C++ shapes for pointer-backed managed collections.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerBackedListLoop_UsesPointerSafeConstructionIndexingAndMutableCounter() {
            string source = """
                using System.Collections.Generic;

                public class ComboBoxItemVisual {
                }

                public class Widget {
                    List<ComboBoxItemVisual> itemVisuals;

                    public void Reset(List<ComboBoxItemVisual> items) {
                        itemVisuals = new List<ComboBoxItemVisual>(items.Count);

                        for (int i = 0; i < items.Count; i++) {
                            itemVisuals.Add(items[i]);
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.Contains("new List<::ComboBoxItemVisual*>(items->get_Count())", sourceOutput);
            Assert.Contains("for (int32_t i = 0; i < items->get_Count(); i++)", sourceOutput);
            Assert.Contains("this->itemVisuals->Add((*items)[i]);", sourceOutput);
            Assert.DoesNotContain("for (const int32_t i = 0;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("itemVisuals->Add(items[i])", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures ArgumentOutOfRangeException exposes the managed parameter-name and message overload required by generated throws.
        /// </summary>
        [Fact]
        public void WriteOutput_WithArgumentOutOfRangeExceptionParameterAndMessage_UsesRuntimeOverload() {
            string source = """
                public class Widget {
                    public void Validate(int size) {
                        if (size < 0) {
                            throw new System.ArgumentOutOfRangeException("size", "ComboBox size must be positive.");
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));
            string runtimeHeader = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_exceptions.hpp"));

            Assert.Contains("throw new ArgumentOutOfRangeException(\"size\", \"ComboBox size must be positive.\")", sourceOutput);
            Assert.Contains("ArgumentOutOfRangeException(const std::string& parameterName, const std::string& message)", runtimeHeader);
        }

        /// <summary>
        /// Ensures InputManager-style patterns lower through scope-exit finally handling, ref/out parameter emission, member-access var inference, and reference equality.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInputManagerPatterns_UsesScopeExitRefOutAndValueMemberInference() {
            string source = """
                using System;
                using System.Collections.Generic;

                public struct float4 {
                    public float X;
                    public float Y;
                    public float Z;
                    public float W;
                }

                public class Camera {
                    public float4 Viewport;
                }

                public class Entity {
                    public Entity Parent;
                }

                public class Interactable {
                    public Entity Parent;
                }

                public class Drawable {
                    public Entity Parent;
                    public byte RenderOrder2D;
                }

                public class InputManager {
                    Camera FindCamera(List<Camera> cameras, int x, int y) {
                        for (int i = cameras.Count - 1; i >= 0; i--) {
                            var cam = cameras[i];
                            var vp = cam.Viewport;
                            if (x >= vp.X && x < vp.X + vp.Z && y >= vp.Y && y < vp.Y + vp.W) {
                                return cam;
                            }
                        }

                        return null;
                    }

                    byte GetTopDrawableRenderOrder(List<Drawable> drawables, Interactable interactable, ushort cameraLayerMask, out int highestDrawableIndex) {
                        highestDrawableIndex = -1;
                        return 0;
                    }

                    bool IsSameEntityOrDescendant(Entity candidate, Entity root) {
                        Entity current = candidate;
                        while (current != null) {
                            if (ReferenceEquals(current, root)) {
                                return true;
                            }

                            current = current.Parent;
                        }

                        return false;
                    }

                    public byte Update(List<Drawable> drawables, Interactable interactable, List<Camera> cameras) {
                        try {
                            Camera topCamera = FindCamera(cameras, 10, 20);
                            byte candidateRenderOrder = GetTopDrawableRenderOrder(drawables, interactable, 0, out int candidateDrawableIndex);
                            return candidateRenderOrder;
                        } finally {
                            FindCamera(cameras, 0, 0);
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "InputManager.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "InputManager.cpp"));

            Assert.Contains("vp = cam->Viewport;", sourceOutput);
            Assert.Contains("int32_t& highestDrawableIndex", headerOutput);
            Assert.Contains("int32_t candidateDrawableIndex;", sourceOutput);
            Assert.Contains("current == root", sourceOutput);
            Assert.Contains("he_cpp_make_scope_exit", sourceOutput);
            Assert.DoesNotContain("finally {", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("object *vp", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("ReferenceEquals(", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures KeyboardState-style value-type helpers keep instance field access, preserve hexadecimal bitmasks, and lower boxed value checks to compilable C++.
        /// </summary>
        [Fact]
        public void WriteOutput_WithKeyboardStateValuePatterns_UsesThisPrefixesHexMasksAndBoxedValueCasts() {
            string source = """
                public enum Keys {
                    A,
                    B
                }

                public struct KeyboardState {
                    uint _keys0, _keys1;

                    public static bool operator ==(KeyboardState a, KeyboardState b) {
                        return a._keys0 == b._keys0 && a._keys1 == b._keys1;
                    }

                    public static bool operator !=(KeyboardState a, KeyboardState b) {
                        return !(a == b);
                    }

                    public override bool Equals(object obj) {
                        return obj is KeyboardState && this == (KeyboardState)obj;
                    }

                    public override int GetHashCode() {
                        return (int)(_keys0 ^ _keys1);
                    }

                    public void Clear(int key) {
                        uint mask = (uint)1 << (((int)key) & 0x1f);
                        _keys1 &= ~mask;
                    }

                    public int Count(Keys[] keys) {
                        int count = 0;
                        foreach (Keys key in keys) {
                            count++;
                        }

                        return count;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "KeyboardState.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "KeyboardState.cpp"));

            Assert.Contains("bool operator==(", headerOutput);
            Assert.Contains("bool operator!=(", headerOutput);
            Assert.Contains("uint32_t _keys0;", headerOutput);
            Assert.Contains("uint32_t _keys1;", headerOutput);
            Assert.Contains("return obj != nullptr && (*this) == (*static_cast<KeyboardState*>(obj));", sourceOutput);
            Assert.Contains("return static_cast<int32_t>((this->_keys0 ^ this->_keys1));", sourceOutput);
            Assert.Contains("((static_cast<int32_t>(key)) & 0x1f)", sourceOutput);
            Assert.Contains("this->_keys1 &= ~mask;", sourceOutput);
            Assert.Contains("for (const auto& key : keys)", sourceOutput);
            Assert.DoesNotContain("0x1.0f", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("return  &&", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures value-type operator overloads keep canonical C++ operator names even when the parameter-lowering path would otherwise add ref/out suffixes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithLargeValueTypeOperators_DoesNotMangleOperatorNames() {
            string source = """
                public enum ButtonState {
                    Released,
                    Pressed
                }

                public struct MouseState {
                    int _x;
                    int _y;
                    int _scrollWheelValue;
                    int _horizontalScrollWheelValue;
                    byte _buttons;

                    public static bool operator ==(MouseState left, MouseState right) {
                        return left._x == right._x &&
                               left._y == right._y &&
                               left._buttons == right._buttons &&
                               left._scrollWheelValue == right._scrollWheelValue &&
                               left._horizontalScrollWheelValue == right._horizontalScrollWheelValue;
                    }

                    public static bool operator !=(MouseState left, MouseState right) {
                        return !(left == right);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "MouseState.hpp"));

            Assert.Contains("friend bool operator==(", headerOutput);
            Assert.Contains("friend bool operator!=(", headerOutput);
            Assert.DoesNotContain("operator==__", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("operator!=__", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generic object creation keeps the concrete generated type instead of collapsing to the interface target type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericProcessorObjectCreation_UsesConcreteGeneratedType() {
            string source = """
                public class Stream {
                }

                public class Asset {
                }

                public class ModelAsset : Asset {
                }

                public interface IContentProcessor<T> {
                    T Read(Stream stream);
                }

                public class AssetContentProcessor<TAsset> : IContentProcessor<TAsset> where TAsset : Asset {
                    public TAsset Read(Stream stream) {
                        return default;
                    }
                }

                public static class Gate {
                    public static IContentProcessor<ModelAsset> Build() {
                        return new AssetContentProcessor<ModelAsset>();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("new ::AssetContentProcessor_1<::ModelAsset*>()", output.GeneratedText);
            Assert.DoesNotContain("new ::IContentProcessor_1<::ModelAsset*>()", output.GeneratedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures pointer coalesce lowering keeps concrete array types instead of degrading to object.
        /// </summary>
        [Fact]
        public void WriteOutput_WithByteArrayCoalesce_UsesNativeArrayPointerType() {
            string source = """
                using System;

                public class SceneRecord {
                    public byte[] Payload { get; set; } = Array.Empty<byte>();
                }

                public class Gate {
                    public byte[] Read(SceneRecord record) {
                        return record.Payload ?? Array.Empty<byte>();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("Array<uint8_t>* __coalesce_", output.GeneratedText);
            Assert.DoesNotContain("object* __coalesce_", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain(" ?? ", output.GeneratedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures object initializers route assignments through generated setters even for auto-properties backed by value types.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAutoPropertyObjectInitializer_UsesSetterCalls() {
            string source = """
                public struct Bounds {
                    public float X;
                }

                public class Camera {
                    public byte LayerMask { get; set; }

                    public Bounds Viewport { get; set; }
                }

                public class Gate {
                    public Camera Create(Bounds viewport) {
                        return new Camera {
                            LayerMask = 1,
                            Viewport = viewport
                        };
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("set_LayerMask(1);", output.GeneratedText);
            Assert.Contains("set_Viewport(viewport);", output.GeneratedText);
        }

        /// <summary>
        /// Ensures array-backed IReadOnlyList returns are wrapped as native lists so generated signatures remain compilable.
        /// </summary>
        [Fact]
        public void WriteOutput_WithArrayBackedReadOnlyListReturn_WrapsNativeList() {
            string source = """
                using System.Collections.Generic;

                public class Gate {
                    readonly string[] values = [];

                    public IReadOnlyList<string> Values {
                        get {
                            return values;
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("return new List<std::string>(", output.GeneratedText);
            Assert.Contains("values", output.GeneratedText);
        }

        /// <summary>
        /// Ensures array-backed assignments into IReadOnlyList properties wrap the array as a native list so generated constructors remain compilable.
        /// </summary>
        [Fact]
        public void WriteOutput_WithArrayBackedReadOnlyListPropertyAssignment_WrapsNativeList() {
            string source = """
                using System.Collections.Generic;

                public class Binding {
                }

                public class Gate {
                    public Gate(IReadOnlyList<Binding> bindings) {
                        Binding[] copiedBindings = new Binding[bindings.Count];
                        Bindings = copiedBindings;
                    }

                    public IReadOnlyList<Binding> Bindings { get; }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Gate.cpp"));

            Assert.Contains("this->Bindings = new List<Binding*>(copiedBindings);", sourceOutput);
        }

        /// <summary>
        /// Ensures value-type compound assignments expand to explicit binary assignments when only operator overloads are available.
        /// </summary>
        [Fact]
        public void WriteOutput_WithValueTypeCompoundAssignment_ExpandsBinaryAssignment() {
            string source = """
                public struct Rotation {
                    public static Rotation operator *(Rotation left, Rotation right) {
                        return left;
                    }

                    public static Rotation operator +(Rotation left, Rotation right) {
                        return left;
                    }
                }

                public class Gate {
                    public Rotation Parent;

                    public Rotation Mix(Rotation value) {
                        Rotation current = value;
                        current *= Parent;
                        current += Parent;
                        return current;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("current = current * this->Parent;", output.GeneratedText);
            Assert.Contains("current = current + this->Parent;", output.GeneratedText);
            Assert.DoesNotContain("current *= this->Parent;", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("current += this->Parent;", output.GeneratedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures Path separator characters convert to native one-character strings instead of pointer-style ToString calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPathSeparatorToString_EmitsNativeCharString() {
            string source = """
                using System.IO;

                public class Gate {
                    public string Read() {
                        return Path.DirectorySeparatorChar.ToString();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("std::string(1, Path::DirectorySeparatorChar)", output.GeneratedText);
            Assert.DoesNotContain("Path::DirectorySeparatorChar->ToString()", output.GeneratedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures declaration-pattern checks inside boolean expressions declare a typed temporary before the condition and reuse it in the trailing expression.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDeclarationPatternInLogicalAnd_DeclaresPatternTemporaryBeforeCondition() {
            string source = """
                public struct Handle {
                    public int Value;

                    public bool Equals(Handle other) {
                        return Value == other.Value;
                    }

                    public override bool Equals(object other) {
                        return other is Handle typed && Equals(typed);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Handle.cpp"));

            Assert.Contains("Handle* __pattern_typed = static_cast<Handle*>(other);", sourceOutput);
            Assert.Contains("return __pattern_typed != nullptr && this->Equals((*__pattern_typed));", sourceOutput);
            Assert.DoesNotContain("return  &&", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures member access uses the instance property getter when a property shares its identifier with a generated value type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPropertyReceiverMatchingTypeName_UsesInstancePropertyGetter() {
            string source = """
                public struct BodyHandle {
                    public int Value;
                }

                public struct CollidableReference {
                    BodyHandle handle;

                    public BodyHandle BodyHandle {
                        get {
                            return handle;
                        }
                    }

                    public int Read() {
                        return BodyHandle.Value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "CollidableReference.cpp"));

            Assert.Contains("return this->get_BodyHandle().Value;", sourceOutput);
            Assert.DoesNotContain("return BodyHandle.Value;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures conditional expressions still use the instance property getter when one branch reads a property whose identifier matches a generated type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithConditionalPropertyReceiverMatchingTypeName_UsesInstancePropertyGetter() {
            string source = """
                public enum CollidableMobility {
                    Dynamic,
                    Kinematic,
                    Static
                }

                public struct BodyHandle {
                    public int Value;
                }

                public struct StaticHandle {
                    public int Value;
                }

                public struct CollidableReference {
                    BodyHandle bodyHandle;
                    StaticHandle staticHandle;

                    public CollidableMobility Mobility {
                        get {
                            return CollidableMobility.Dynamic;
                        }
                    }

                    public BodyHandle BodyHandle {
                        get {
                            return bodyHandle;
                        }
                    }

                    public StaticHandle StaticHandle {
                        get {
                            return staticHandle;
                        }
                    }

                    public int Read() {
                        int handle = (Mobility == CollidableMobility.Static) ? StaticHandle.Value : BodyHandle.Value;
                        return handle;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "CollidableReference.cpp"));

            Assert.Contains("this->get_StaticHandle().Value : this->get_BodyHandle().Value", sourceOutput);
            Assert.DoesNotContain(": BodyHandle.Value", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures explicit generic invocation type arguments register generated dependencies so the caller includes the generated type header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitGenericInvocationTypeArgument_EmitsGeneratedTypeInclude() {
            string source = """
                public struct CollisionPair {
                    public int Value;
                }

                public class CollisionBatcher {
                    public TPair AllocatePair<TPair>() {
                        return default;
                    }

                    public CollisionPair Make() {
                        return AllocatePair<CollisionPair>();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "CollisionBatcher.cpp"));

            Assert.Contains("#include \"CollisionPair.hpp\"", sourceOutput);
            Assert.Contains("AllocatePair<CollisionPair>()", sourceOutput);
        }

        /// <summary>
        /// Ensures explicit generic invocation type arguments register generated dependencies even when the type argument is only used inside the method body.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitGenericInvocationBodyOnlyTypeArgument_EmitsGeneratedTypeInclude() {
            string source = """
                public struct CollisionPair {
                    public int Value;
                }

                public class CollisionBatcher {
                    public TPair AllocatePair<TPair>() {
                        return default;
                    }

                    public int Make() {
                        AllocatePair<CollisionPair>();
                        return 0;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "CollisionBatcher.cpp"));

            Assert.Contains("#include \"CollisionPair.hpp\"", sourceOutput);
            Assert.Contains("AllocatePair<CollisionPair>()", sourceOutput);
        }

        /// <summary>
        /// Ensures generic and nongeneric zero-argument overloads do not collapse into one emitted native declaration.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericAndNonGenericZeroArgumentOverloads_EmitsBothMethods() {
            string source = """
                public struct Pair {
                    public int Value;
                }

                public struct UntypedList {
                    public int AllocateUnsafely() {
                        return 1;
                    }

                    public T AllocateUnsafely<T>() {
                        return default;
                    }
                }

                public class Fixture {
                    UntypedList list;

                    public Pair Read() {
                        return list.AllocateUnsafely<Pair>();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "UntypedList.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("int32_t AllocateUnsafely();", headerOutput);
            Assert.Contains("template <typename T>", headerOutput);
            Assert.Contains("T AllocateUnsafely();", headerOutput);
            Assert.Contains("#include \"Pair.hpp\"", sourceOutput);
            Assert.Contains("this->list.AllocateUnsafely<Pair>()", sourceOutput);
        }

        /// <summary>
        /// Ensures tuple generic arguments keep the native ValueTuple pointer contract when used inside generated generic containers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericFieldUsingTupleType_EmitsPointerTupleArgument() {
            string source = """
                public class Box<T> {
                }

                public class Fixture {
                    public Box<(int start, int count)> Regions;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.hpp"));

            Assert.Contains("Box_1<ValueTuple<int32_t, int32_t>*>* Regions;", headerOutput);
        }

        /// <summary>
        /// Ensures tuple locals continue to use pointer-style member access against the native ValueTuple runtime.
        /// </summary>
        [Fact]
        public void WriteOutput_WithTupleLocalMemberReads_UsesPointerTupleAccess() {
            string source = """
                public class Fixture {
                    public int Read() {
                        var pair = (start: 1, count: 2);
                        return pair.start + pair.count;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("pair->Item1 + pair->Item2", sourceOutput);
        }

        /// <summary>
        /// Ensures inferred generic method type arguments for Buffer-based tuple storage stay aligned with the generated Buffer tuple element type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithBufferTupleTakeAndReturn_EmitsValueTupleMethodArgumentsWithoutPointer() {
            string source = """
                public struct Buffer<T> {
                }

                public class BufferPool {
                    public void Take<T>(int count, out Buffer<T> buffer) {
                        buffer = default;
                    }

                    public void Return<T>(ref Buffer<T> buffer) {
                    }
                }

                public class Fixture {
                    Buffer<(int start, int count)> pairRegions;

                    public void Init(BufferPool pool) {
                        pool.Take(1, out pairRegions);
                        pool.Return(ref pairRegions);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("Take__out1<ValueTuple<int32_t, int32_t>>(1, this->pairRegions)", sourceOutput);
            Assert.Contains("Return__ref0<ValueTuple<int32_t, int32_t>>(this->pairRegions)", sourceOutput);
            Assert.DoesNotContain("ValueTuple<int32_t, int32_t>*", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures tuple literals assigned into Buffer tuple slots emit direct value construction instead of heap allocation.
        /// </summary>
        [Fact]
        public void WriteOutput_WithBufferTupleElementAssignment_EmitsDirectValueTupleConstruction() {
            string source = """
                public struct Buffer<T> {
                    T item;

                    public ref T this[int index] {
                        get => ref item;
                    }
                }

                public class Fixture {
                    Buffer<(int start, int count)> pairRegions;
                    int pairCount;
                    int subpairCount;

                    public void Create(int childrenInPair) {
                        pairRegions[pairCount++] = (subpairCount, childrenInPair);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("this->pairRegions.get_Item(this->pairCount++) = ValueTuple<int32_t, int32_t>(this->subpairCount, childrenInPair);", sourceOutput);
            Assert.DoesNotContain("= new ValueTuple<int32_t, int32_t>(", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures tuple members read from ref locals over Buffer tuple elements use direct member access instead of pointer access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefTupleLocalFromBuffer_UsesDirectTupleMemberAccess() {
            string source = """
                public struct Buffer<T> {
                    T item;

                    public ref T Get(int index) {
                        return ref item;
                    }
                }

                public class Fixture {
                    Buffer<(int start, int count)> pairRegions;

                    public int Read() {
                        ref var region = ref pairRegions.Get(0);
                        return region.count;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("return region.Item2;", sourceOutput);
            Assert.DoesNotContain("return region->Item2;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures inherited instance members in generic base classes are qualified with this-> so dependent-base lookup works in emitted C++ templates.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInheritedGenericBaseFieldAccess_QualifiesThisPointer() {
            string source = """
                public class Base<T> {
                    protected Buffer<T> shapes;
                }

                public struct Buffer<T> {
                    T item;

                    public ref T this[int index] {
                        get => ref item;
                    }
                }

                public struct Payload {
                    public int Value;
                }

                public class Derived<T> : Base<T> {
                    public int Read() {
                        return shapes[0].Value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Derived_1.cpp"));

            Assert.Contains("this->shapes.get_Item(0)", sourceOutput);
            Assert.DoesNotContain("shapes.get_Item(0)", sourceOutput.Replace("this->shapes.get_Item(0)", string.Empty), StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures primitive CompareTo on native scalar receivers lowers to a direct native comparison expression.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPrimitiveCompareTo_EmitsNativeComparisonExpression() {
            string source = """
                public struct Pair {
                    public int Value;
                }

                public class Fixture {
                    public int Compare(Pair a, Pair b) {
                        return a.Value.CompareTo(b.Value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("((a.Value) < (b.Value) ? -1 : ((a.Value) > (b.Value) ? 1 : 0))", sourceOutput);
            Assert.DoesNotContain(".CompareTo(", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures dependent generic static method calls emit the C++ template disambiguator before the method name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDependentGenericStaticMethodCall_EmitsTemplateDisambiguator() {
            string source = """
                public class StaticHost<TState> {
                    public static void Call<TValue>(TValue value) {
                    }
                }

                public class Fixture<TState> {
                    public void Invoke<TValue>(TValue value) {
                        StaticHost<TState>.Call<TValue>(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("StaticHost_1<TState>::template Call<TValue>(value)", sourceOutput);
        }

        /// <summary>
        /// Ensures dependent generic static calls on nested generic owners emit the C++ template disambiguator before the method name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDependentNestedGenericStaticMethodCall_EmitsTemplateDisambiguator() {
            string source = """
                public partial class Outer<TState> {
                    public struct Cache {
                        public static void Call<TValue>(int typeIndex, TValue value) {
                        }
                    }
                }

                public class Fixture<TState> {
                    public void Invoke<TValue>(int typeIndex, TValue value) {
                        Outer<TState>.Cache.Call<TValue>(typeIndex, value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("Cache_1<TState>::template Call<TValue>(typeIndex, value)", sourceOutput);
        }

        /// <summary>
        /// Ensures non-runtime generic types named Buffer keep their generated static-owner surface instead of being remapped to System.Buffer.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGeneratedGenericBufferStaticCall_UsesGeneratedOwnerInsteadOfRuntimeBuffer() {
            string source = """
                public struct Buffer<T> {
                    public static T Pick(T value) {
                        return value;
                    }
                }

                public class Gate {
                    public int Read(int value) {
                        return Buffer<int>.Pick(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Gate.cpp"));

            Assert.Contains("#include \"Buffer_1.hpp\"", sourceOutput);
            Assert.Contains("Buffer_1<int32_t>::Pick(value)", sourceOutput);
            Assert.DoesNotContain("Buffer::Pick(value)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"system/buffer.hpp\"", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures ref-return static calls on generated generic Buffer owners preserve the emitted generic owner name instead of collapsing to System.Buffer.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGeneratedGenericBufferRefReturnStaticCall_UsesGeneratedOwnerInsteadOfRuntimeBuffer() {
            string source = """
                public struct Buffer<T> {
                    public T Value;

                    public static ref T Get(ref Buffer<byte> values, int index) {
                        return ref values.Value;
                    }
                }

                public struct Batch {
                    public Buffer<byte> Values;
                }

                public static class Reader {
                    public static ref Item Read(ref Batch batch, int index) {
                        return ref Buffer<Item>.Get(ref batch.Values, index);
                    }
                }

                public struct Item {
                    public int Value;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Reader.cpp"));

            Assert.Contains("Buffer_1<Item>::Get__ref0(batch.Values, index)", sourceOutput);
            Assert.DoesNotContain("Buffer::Get__ref0(batch.Values, index)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"system/buffer.hpp\"", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generic declaration-pattern casts do not add an extra pointer layer when the generic argument already lowers as a pointer type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericReferencePatternVariable_DoesNotEmitDoublePointer() {
            string source = """
                public class Asset {
                }

                public class Stream {
                }

                public static class AssetSerializer {
                    public static Asset Deserialize(Stream stream) {
                        return null;
                    }
                }

                public interface IContentProcessor<T> {
                    T Read(Stream stream);
                }

                public class AssetContentProcessor<TAsset> : IContentProcessor<TAsset> where TAsset : Asset {
                    public TAsset Read(Stream stream) {
                        Asset asset = AssetSerializer.Deserialize(stream);
                        if (asset is TAsset typedAsset) {
                            return typedAsset;
                        }

                        return default;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("TAsset typedAsset = he_cpp_try_cast<TAsset>(asset);", output.GeneratedText);
            Assert.DoesNotContain("TAsset* typedAsset = he_cpp_try_cast<TAsset>(asset);", output.GeneratedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures C# <c>as</c> expressions lower to native cast helpers instead of leaking raw C# syntax into generated C++.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAsExpressionInLocalAssignment_UsesNativeTryCast() {
            string source = """
                using System.Collections.Generic;

                public class Component {
                }

                public class DirectionalLightComponent : Component {
                }

                public class Entity {
                    public List<Component> Components { get; set; }
                }

                public class DemoDiscLightToggleComponent {
                    public DirectionalLightComponent Capture(Entity entity, int componentIndex) {
                        return entity.Components[componentIndex] as DirectionalLightComponent;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string generatedSource = File.ReadAllText(Path.Combine(output.OutputPath, "DemoDiscLightToggleComponent.cpp"));

            Assert.Contains("return he_cpp_try_cast<DirectionalLightComponent>((*entity->get_Components())[componentIndex]);", generatedSource);
            Assert.DoesNotContain(" as DirectionalLightComponent", generatedSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures ref-parameter declarations to generated types rely on forward declarations in headers so cyclic value-type signatures do not force recursive includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefParameterToGeneratedType_UsesForwardDeclarationOnlyInHeader() {
            string source = """
                public struct Payload {
                    public int Value;
                }

                public static class Gate {
                    public static void Touch(ref Payload payload) {
                        payload.Value++;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "Gate.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Gate.cpp"));

            Assert.Contains("class Payload;", header);
            Assert.DoesNotContain("#include \"Payload.hpp\"", header, StringComparison.Ordinal);
            Assert.Contains("#include \"Payload.hpp\"", sourceOutput);
            Assert.Contains("::Payload& payload", output.GeneratedText);
        }

        /// <summary>
        /// Ensures user-defined ref-return indexers lower to generated get_Item accessors instead of assuming a native operator[] exists on every converted type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefReturnIndexer_UsesGeneratedGetItemAccessor() {
            string source = """
                public struct Buffer {
                    int[] values;

                    public Buffer(int[] values) {
                        this.values = values;
                    }

                    public ref int this[int index] {
                        get {
                            return ref values[index];
                        }
                    }
                }

                public static class Gate {
                    public static int Read(Buffer buffer, int index) {
                        return buffer[index];
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "Buffer.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Gate.cpp"));

            Assert.Contains("int32_t& get_Item(int32_t index);", header);
            Assert.Contains("return buffer.get_Item(index);", sourceOutput);
            Assert.DoesNotContain("buffer[index]", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures pointer-backed fields keep native pointer member access instead of degrading to value-member syntax when invoking methods.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerFieldMethodAccess_UsesPointerMemberAccess() {
            string source = """
                public unsafe struct Sink {
                    public void Touch() {
                    }
                }

                public unsafe class Owner {
                    Sink* sink;

                    public void Run() {
                        sink->Touch();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Owner.cpp"));

            Assert.Contains("this->sink->Touch();", sourceOutput);
            Assert.DoesNotContain("this->sink.Touch();", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nested generated types qualify outer static members so out-of-class native method definitions keep access to the containing type contract.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedTypeAccessingOuterStaticMember_QualifiesContainingType() {
            string source = """
                public partial class Bodies {
                    public const int BodyReferenceMask = 7;

                    public struct Enumerator {
                        public int Read(int encodedBodyIndex) {
                            return encodedBodyIndex & BodyReferenceMask;
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Enumerator.cpp"));

            Assert.Contains("Bodies::BodyReferenceMask", sourceOutput);
            Assert.DoesNotContain("encodedBodyIndex & BodyReferenceMask;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures default expressions for value types lower to value initialization instead of an invalid null token.
        /// </summary>
        [Fact]
        public void WriteOutput_WithValueTypeDefaultExpression_EmitsValueInitialization() {
            string source = """
                public struct Token {
                    public int Value;
                }

                public class Factory {
                    public Token Make() {
                        Token token = default(Token);
                        return token;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Factory.cpp"));

            Assert.Contains("Token()", sourceOutput);
            Assert.DoesNotContain("= null;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures pointer-backed native list fields still lower indexer access through the dereferenced value receiver instead of applying pointer member access twice.
        /// </summary>
        [Fact]
        public void WriteOutput_WithListFieldIndexerAccess_UsesDereferencedDirectIndexerCall() {
            string source = """
                using System.Collections.Generic;

                public class Inventory {
                    List<int> items;

                    public int ReadLast() {
                        return items[items.Count - 1];
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Inventory.cpp"));

            Assert.Contains("(*this->items).get_Item", sourceOutput);
            Assert.DoesNotContain("(*this->items)->get_Item", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures ref locals bound from element-access indexers keep the underlying value-type tracking so subsequent member access stays direct.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefElementAccessLocal_UsesDirectMemberAccess() {
            string source = """
                public struct Location {
                    public int Index;
                }

                public struct Buffer {
                    Location[] items;

                    public Buffer(Location[] items) {
                        this.items = items;
                    }

                    public ref Location this[int index] {
                        get {
                            return ref items[index];
                        }
                    }
                }

                public class Fixture {
                    public int Read(Buffer buffer, int index) {
                        ref var location = ref buffer[index];
                        return location.Index;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("auto& location = buffer.get_Item(index);", sourceOutput);
            Assert.Contains("return location.Index;", sourceOutput);
            Assert.DoesNotContain("return location->Index;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures by-ref overloads receive distinct emitted names so lvalue calls do not become ambiguous in native overload resolution.
        /// </summary>
        [Fact]
        public void WriteOutput_WithValueAndRefOverloads_EmitsDistinctNativeMethodNames() {
            string source = """
                public class OverloadFixture {
                    public bool Contains(int value) {
                        return true;
                    }

                    public bool Contains(ref int value) {
                        return false;
                    }

                    public bool Read(ref int value) {
                        return Contains(value) || Contains(ref value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "OverloadFixture.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "OverloadFixture.cpp"));

            Assert.Contains("bool Contains(int32_t value);", headerOutput);
            Assert.Contains("bool Contains__ref0(int32_t& value);", headerOutput);
            Assert.Contains("Contains(value)", sourceOutput);
            Assert.Contains("Contains__ref0", sourceOutput);
        }

        /// <summary>
        /// Ensures qualified static overload calls preserve the exact emitted by-out suffix selected by Roslyn invocation binding.
        /// </summary>
        [Fact]
        public void WriteOutput_WithQualifiedStaticOutOverload_EmitsBoundNativeMethodName() {
            string source = """
                public static class Resolver {
                    public static int Pick(int a, int b, int c, int d, int e) {
                        return e;
                    }

                    public static int Pick(int a, int b, int c, int d, out int e) {
                        e = d;
                        return e;
                    }
                }

                public class Fixture {
                    public int Run() {
                        return Resolver.Pick(1, 2, 3, 4, out int value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("Resolver::Pick__out4(1, 2, 3, 4, value)", sourceOutput);
            Assert.DoesNotContain("Resolver::Pick(1, 2, 3, 4, value)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures qualified static overload calls on value types preserve the emitted by-out suffix selected by Roslyn invocation binding.
        /// </summary>
        [Fact]
        public void WriteOutput_WithQualifiedStructStaticOutOverload_EmitsBoundNativeMethodName() {
            string source = """
                public struct Resolver {
                    public static int Pick(int a, int b, int c, int d, int e) {
                        return e;
                    }

                    public static int Pick(int a, int b, int c, int d, out int e) {
                        e = d;
                        return e;
                    }
                }

                public class Fixture {
                    public int Run() {
                        return Resolver.Pick(1, 2, 3, 4, out int value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("Resolver::Pick__out4(1, 2, 3, 4, value)", sourceOutput);
            Assert.DoesNotContain("Resolver::Pick(1, 2, 3, 4, value)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures qualified static overload calls on value types preserve mixed ref and out suffixes selected by Roslyn invocation binding.
        /// </summary>
        [Fact]
        public void WriteOutput_WithQualifiedStructStaticRefOutOverload_EmitsBoundNativeMethodName() {
            string source = """
                public struct Resolver {
                    public static void Rotate(int axis, int angle, out int result) {
                        result = axis + angle;
                    }

                    public static void Rotate(ref int axis, int angle, out int result) {
                        result = axis - angle;
                    }
                }

                public class Fixture {
                    public int Run(int axis, int angle) {
                        Resolver.Rotate(axis, angle, out int result);
                        return result;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("Resolver::Rotate__out2(axis, angle, result)", sourceOutput);
            Assert.DoesNotContain("Resolver::Rotate(axis, angle, result)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures qualified static overload calls on value types preserve mixed ref and out suffixes when native runtime metadata is loaded.
        /// </summary>
        [Fact]
        public void WriteOutput_WithQualifiedStructStaticRefOutOverloadAndNativeRuntimeMetadata_EmitsBoundNativeMethodName() {
            string source = """
                public struct Resolver {
                    public static void Rotate(int axis, int angle, out int result) {
                        result = axis + angle;
                    }

                    public static void Rotate(ref int axis, int angle, out int result) {
                        result = axis - angle;
                    }
                }

                public class Fixture {
                    public int Run(int axis, int angle) {
                        Resolver.Rotate(axis, angle, out int result);
                        return result;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, loadNativeRuntimeMetadata: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("Resolver::Rotate__out2(axis, angle, result)", sourceOutput);
            Assert.DoesNotContain("Resolver::Rotate(axis, angle, result)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures instance member overload calls preserve emitted by-ref and by-out suffixes instead of falling back to the unsuffixed method name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMemberRefOutOverload_EmitsBoundNativeMethodName() {
            string source = """
                public class Table {
                    public bool TryGetValue(int key, out int value) {
                        value = key;
                        return true;
                    }

                    public bool TryGetValue(ref int key, out int value) {
                        value = key + 1;
                        return false;
                    }
                }

                public class Fixture {
                    Table table = new Table();

                    public bool Run(ref int key) {
                        return table.TryGetValue(ref key, out int value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("this->table->TryGetValue__ref0_out1(key, value)", sourceOutput);
            Assert.DoesNotContain("this->table->TryGetValue(key, value)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated generic container overload calls preserve emitted by-out suffixes without affecting framework dictionary calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGeneratedGenericMemberOutOverload_EmitsBoundNativeMethodName() {
            string source = """
                public class Table<TKey, TValue> {
                    public bool TryGetValue(TKey key, out TValue value) {
                        value = default(TValue);
                        return true;
                    }

                    public bool TryGetValue(ref TKey key, out TValue value) {
                        value = default(TValue);
                        return false;
                    }
                }

                public class Fixture {
                    Table<int, int> table = new Table<int, int>();

                    public bool Run(int key) {
                        return table.TryGetValue(key, out int value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("this->table->TryGetValue__out1(key, value)", sourceOutput);
            Assert.DoesNotContain("this->table->TryGetValue(key, value)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures static generic generated helper calls preserve ref-based emitted names and source includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStaticGenericRefHelperInvocation_EmitsIncludedSuffixedTarget() {
            string source = """
                using static Gatherer;

                public static class Gatherer {
                    public static ref T GetOffsetInstance<T>(ref T value, int index) {
                        return ref value;
                    }

                    public static ref T GetFirst<T>(ref T value) {
                        return ref value;
                    }
                }

                public struct Payload {
                    public int Value;
                }

                public class Fixture {
                    public int Run() {
                        Payload payload = default(Payload);
                        ref var target = ref GetOffsetInstance<Payload>(ref payload, 0);
                        GetFirst<int>(ref target.Value) = 3;
                        return target.Value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("#include \"Gatherer.hpp\"", sourceOutput);
            Assert.Contains("Gatherer::GetOffsetInstance__ref0<Payload>(payload, 0)", sourceOutput);
            Assert.Contains("Gatherer::GetFirst__ref0<int32_t>(target.Value) = 3;", sourceOutput);
        }

        /// <summary>
        /// Ensures out discards lower to generated temporary locals instead of an undeclared underscore identifier.
        /// </summary>
        [Fact]
        public void WriteOutput_WithOutDiscardInvocation_EmitsGeneratedDiscardTemporary() {
            string source = """
                public static class Helper {
                    public static void Read(out int left, out int right) {
                        left = 1;
                        right = 2;
                    }
                }

                public class Fixture {
                    public int Run() {
                        Helper.Read(out _, out int value);
                        return value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("int32_t __discard_", sourceOutput);
            Assert.Contains("Helper::Read__out0_out1(__discard_", sourceOutput);
            Assert.DoesNotContain("out _", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("(_,", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures rebindable ref locals lower to pointer-backed aliases so later <c>ref</c> reassignments stay valid in C++.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRebindableRefLocal_EmitsPointerBackedAlias() {
            string source = """
                public struct Pair {
                    public int Left;
                    public int Right;
                }

                public class Fixture {
                    public int Run() {
                        Pair pair = default(Pair);
                        pair.Left = 1;
                        pair.Right = 2;
                        ref var target = ref pair.Left;
                        target = ref pair.Right;
                        return target;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("auto* target = &pair.Left;", sourceOutput);
            Assert.Contains("target = &pair.Right;", sourceOutput);
            Assert.Contains("return (*target);", sourceOutput);
            Assert.DoesNotContain("target = ;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generic member invocations on dependent receivers emit the required <c>template</c> disambiguator.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDependentReceiverGenericInvocation_EmitsTemplateDisambiguator() {
            string source = """
                public interface IExtractor {
                    void Capture<T>(ref T value);
                }

                public class Fixture<TExtractor> where TExtractor : IExtractor {
                    public void Run(ref TExtractor extractor, ref int value) {
                        extractor.Capture<int>(ref value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("extractor.template Capture__ref0<int32_t>(value);", sourceOutput);
        }

        /// <summary>
        /// Ensures generic member invocations on chained receivers still emit the required <c>template</c> disambiguator when an earlier member in the chain depends on template parameters.
        /// </summary>
        [Fact]
        public void WriteOutput_WithChainedDependentReceiverGenericInvocation_EmitsTemplateDisambiguator() {
            string source = """
                public unsafe struct Buffer<T> where T : unmanaged {
                    public T* Memory;
                    public int Length;

                    public Buffer<TTo> As<TTo>() where TTo : unmanaged {
                        return default;
                    }
                }

                public struct TypeBatch<TMarker> where TMarker : unmanaged {
                    public Buffer<byte> BodyReferences;
                }

                public class Fixture<TMarker> where TMarker : unmanaged {
                    TypeBatch<TMarker> GetBatch() {
                        return default;
                    }

                    public void Run() {
                        var batch = GetBatch();
                        var ints = batch.BodyReferences.As<int>();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("batch.BodyReferences.template As<int32_t>()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures <c>ref var</c> locals use their resolved native type instead of <c>auto&amp;</c> so subsequent generic member calls on concrete value-type members stay non-dependent in C++.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefVarLocalOnConcreteType_EmitsTypedReferenceLocal() {
            string source = """
                public unsafe struct Buffer<T> where T : unmanaged {
                    public T* Memory;
                    public int Length;

                    public Buffer<TTo> As<TTo>() where TTo : unmanaged {
                        return default;
                    }
                }

                public struct TypeBatch {
                    public Buffer<byte> BodyReferences;
                }

                public class Fixture<TMarker> where TMarker : unmanaged {
                    TypeBatch batch;

                    ref TypeBatch GetBatch() {
                        return ref batch;
                    }

                    public void Run() {
                        ref var localBatch = ref GetBatch();
                        var ints = localBatch.BodyReferences.As<int>();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("::TypeBatch& localBatch = this->GetBatch();", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("localBatch.BodyReferences.As<int32_t>()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("auto& localBatch =", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generic base-member invocations qualify through the instantiated generated base type instead of an unbound simple base name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericBaseTemplateInvocation_UsesInstantiatedBaseTypeQualifier() {
            string source = """
                public class Base<TState> {
                    protected void Copy<TValue>(TValue value) {
                    }
                }

                public class Fixture<TState> : Base<TState> {
                    public void Run(int value) {
                        base.Copy<int>(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("Base_1<TState>::template Copy<int32_t>(value);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Base::template Copy<int32_t>(value);", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures dependent receivers do not emit the C++ <c>template</c> disambiguator for nongeneric member calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDependentReceiverNonGenericInvocation_DoesNotEmitTemplateDisambiguator() {
            string source = """
                public interface IComparerRef<T> {
                    int Compare(ref T left, ref T right);
                }

                public class Fixture<TComparer> where TComparer : IComparerRef<int> {
                    public int Run(ref TComparer comparer, ref int left, ref int right) {
                        return comparer.Compare(ref left, ref right);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.DoesNotContain(".template Compare__ref0_ref1", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("->template Compare__ref0_ref1", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nested generated base interfaces inherit the implicit outer generic arguments required by the emitted type name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedGenericBaseInterface_EmitsOuterTypeArgumentsInInheritanceClause() {
            string source = """
                public class Outer<TA, TB> {
                    public interface IWorker {
                        void Run();
                    }

                    public class Worker : IWorker {
                        public void Run() {
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Worker_2.hpp"));

            Assert.Contains("class Worker_2 : public ::IWorker_2<TA, TB>", headerOutput);
        }

        /// <summary>
        /// Ensures constructor base initializers use the emitted generic base type name rather than the unspecialized source type name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericBaseConstructorInitializer_UsesSpecializedBaseTypeName() {
            string source = """
                public class Base<TA, TB> {
                    public Base(int value) {
                    }
                }

                public class Derived : Base<int, string> {
                    public Derived() : base(1) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Derived.cpp"));

            Assert.Contains("Derived::Derived() : ::Base_2<int32_t, std::string>(1)", sourceOutput);
            Assert.DoesNotContain(": Base(1)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures numeric literals with digit separators normalize to portable C++ tokens instead of invalid literal suffixes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDigitSeparatedNumericLiterals_RemovesUnderscores() {
            string source = """
                public class Fixture {
                    public ulong ReadMask() {
                        return 0xFFFF_FFFF_FFFF_FFFF;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("0xFFFFFFFFFFFFFFFF", sourceOutput);
            Assert.DoesNotContain("_FFFF_", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures user-defined conversion operators become generated helper calls when native invocation arguments require the conversion.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUserDefinedConversionInvocationArgument_EmitsGeneratedConversionHelperCall() {
            string source = """
                using System;

                public struct QuickLike {
                    public int[] Buffer;
                    public int Count;

                    public static implicit operator ReadOnlySpan<int>(QuickLike value) {
                        return new ReadOnlySpan<int>(value.Buffer, 0, value.Count);
                    }
                }

                public static class Consumer {
                    public static int Take(ReadOnlySpan<int> values) {
                        return values.Length;
                    }
                }

                public class Fixture {
                    public int Run(QuickLike value) {
                        return Consumer.Take(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string fixtureSourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));
            string quickLikeSourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "QuickLike.cpp"));

            Assert.Contains("Consumer::Take(QuickLike::op_Implicit_to_ReadOnlySpan_1(value))", fixtureSourceOutput);
            Assert.Contains("ReadOnlySpan<int32_t>", quickLikeSourceOutput);
            Assert.Contains("QuickLike::op_Implicit_to_ReadOnlySpan_1(::QuickLike value)", quickLikeSourceOutput);
        }

        /// <summary>
        /// Ensures primitive single-precision helper calls lower to the MathF runtime surface instead of invalid primitive static member access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPrimitiveSingleMinMagnitude_UsesMathFRuntimeHelper() {
            string source = """
                public class Fixture {
                    public float ClampTowardZero(float value, float limit) {
                        return float.MinMagnitude(value, limit);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("MathF::MinMagnitude(value, limit)", sourceOutput);
            Assert.DoesNotContain("float::MinMagnitude", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures explicit generic type arguments used only inside method bodies still register source includes for generated target types.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitGenericBodyTypeArgument_EmitsGeneratedSourceInclude() {
            string source = """
                public class Constraint {
                }

                public class Host {
                    public void Register<T>() {
                    }
                }

                public class Fixture {
                    public void Run(Host host) {
                        host.Register<Constraint>();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("#include \"Constraint.hpp\"", sourceOutput);
            Assert.Contains("host->Register<Constraint>()", sourceOutput);
        }

        /// <summary>
        /// Ensures explicit generic type arguments imported through one using directive still register generated source includes for body-only method calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithImportedExplicitGenericBodyTypeArgument_EmitsGeneratedSourceInclude() {
            string source = """
                namespace BepuPhysics.Constraints {
                    public struct Constraint {
                    }
                }

                namespace BepuPhysics {
                    using BepuPhysics.Constraints;

                    public class Host {
                        public void Register<T>() {
                        }
                    }

                    public class Fixture {
                        public void Run(Host host) {
                            host.Register<Constraint>();
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("#include \"Constraint.hpp\"", sourceOutput);
            Assert.Contains("host->Register<Constraint>()", sourceOutput);
        }

        /// <summary>
        /// Ensures static member calls from a function body emit one source include for the owning generated type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStaticBodyMemberAccess_EmitsOwningTypeSourceInclude() {
            string source = """
                namespace Example {
                    public class SceneMapComponent {
                        public static string ResolveSceneId(string sceneId) {
                            return sceneId + "_resolved";
                        }
                    }

                    public class Consumer {
                        public string Resolve(string sceneId) {
                            return SceneMapComponent.ResolveSceneId(sceneId);
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Consumer.cpp"));

            Assert.Contains("#include \"SceneMapComponent.hpp\"", sourceOutput);
            Assert.Contains("SceneMapComponent::ResolveSceneId(sceneId)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures body-only instance method calls do not get mistaken for generated type dependencies that emit nonexistent source includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInstanceMethodCall_DoesNotEmitMethodNameSourceInclude() {
            string source = """
                public class Widget {
                    bool disposed;

                    public void Touch() {
                        ThrowIfDisposed();
                    }

                    void ThrowIfDisposed() {
                        if (disposed) {
                            throw new System.InvalidOperationException();
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Widget.cpp"));

            Assert.DoesNotContain("#include \"ThrowIfDisposed.hpp\"", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("this->ThrowIfDisposed();", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generic base-type arguments that are themselves generated generic types emit the correct instantiated include path.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericInheritanceTypeArgument_EmitsGeneratedGenericInclude() {
            string source = """
                public class Functions<TLeft, TRight> {
                }

                public class Base<TValue> {
                }

                public class Payload {
                }

                public class Derived : Base<Functions<Payload, Payload>> {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Derived.hpp"));

            Assert.Contains("template <typename TLeft, typename TRight> class Functions_2;", headerOutput, StringComparison.Ordinal);
            Assert.Contains("::Functions_2<::Payload*, ::Payload*>", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures inherited property bridges scope through the instantiated generic base type instead of an unbound template name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInheritedGenericBasePropertyBridge_UsesInstantiatedBaseQualifier() {
            string source = """
                public class Base<TValue> {
                    public virtual bool Enabled => true;
                }

                public class Mid<TValue> : Base<TValue> {
                }

                public class Payload {
                }

                public class Derived : Mid<Payload> {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Derived.cpp"));

            Assert.DoesNotContain("this->Base_1::get_Enabled()", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("Base_1<::Payload*>::get_Enabled()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures case-only type-name differences emit the canonical qualified lowercase generated type directly while keeping Windows-safe file stems.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAutoPropertyOnCaseCollidedLowercaseValueType_UsesCanonicalQualifiedEmittedTypeName() {
            string source = """
                namespace helengine {
                    public struct int2 {
                        public int X;
                        public int Y;

                        public int2(int x, int y) {
                            X = x;
                            Y = y;
                        }
                    }

                    public class RoundedRectComponent {
                        public int2 Size { get; set; }
                    }

                    public class DebugOverlayComponent {
                        public int2 Padding { get; set; } = new int2(8, 6);

                        public void Configure(RoundedRectComponent roundedRectComponent) {
                            roundedRectComponent.Size = new int2(200, 80);
                        }
                    }
                }

                namespace BepuUtilities {
                    public struct Int2 {
                        public int X;
                        public int Y;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "DebugOverlayComponent.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "DebugOverlayComponent.cpp"));
            string bepuHeaderOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Int2.hpp"));

            Assert.Contains("#include \"helengine_int2.hpp\"", headerOutput);
            Assert.Contains("::helengine_int2 Padding;", headerOutput);
            Assert.Contains("void set_Padding(::helengine_int2 value);", headerOutput);
            Assert.Contains("DebugOverlayComponent() : Padding(::helengine_int2(static_cast<int32_t>(8), static_cast<int32_t>(6)))", sourceOutput);
            Assert.Contains("roundedRectComponent->set_Size(::helengine_int2(static_cast<int32_t>(200), static_cast<int32_t>(80)));", sourceOutput);
            Assert.Contains("class Int2", bepuHeaderOutput);
            Assert.DoesNotContain("::int2 Padding;", headerOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("set_Padding(::int2 value)", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures lowercase generated types emit canonical qualified artifacts directly without compatibility headers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithLowercaseValueType_UsesCanonicalQualifiedArtifactsWithoutCompatibilityHeaders() {
            string source = """
                namespace helengine {
                    public struct int2 {
                        public int X;
                        public int Y;

                        public int2(int x, int y) {
                            X = x;
                            Y = y;
                        }
                    }

                    public class OverlayBox {
                        public int2 Padding { get; set; } = new int2(8, 6);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string overlayHeader = File.ReadAllText(Path.Combine(output.OutputPath, "OverlayBox.hpp"));

            Assert.True(File.Exists(Path.Combine(output.OutputPath, "helengine_int2.hpp")));
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "helengine_int2.cpp")));
            Assert.False(File.Exists(Path.Combine(output.OutputPath, "int2.hpp")));
            Assert.False(File.Exists(Path.Combine(output.OutputPath, "helengine_helengine_int2.hpp")));
            Assert.Contains("#include \"helengine_int2.hpp\"", overlayHeader, StringComparison.Ordinal);
            Assert.Contains("::helengine_int2 Padding;", overlayHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("::int2 Padding;", overlayHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures tuple deconstruction declarations emit concrete locals before assigning tuple members from the returned value.
        /// </summary>
        [Fact]
        public void WriteOutput_WithTupleDeconstructionDeclaration_EmitsLocalTupleElementAssignments() {
            string source = """
                public class Fixture {
                    public (int, int) Prepare() {
                        return (3, 5);
                    }

                    public int Run() {
                        var (left, right) = Prepare();
                        return left + right;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("const auto __deconstruct_", sourceOutput);
            Assert.Contains("int32_t left = __deconstruct_", sourceOutput);
            Assert.Contains("->Item1", sourceOutput);
            Assert.Contains("int32_t right = __deconstruct_", sourceOutput);
            Assert.Contains("->Item2", sourceOutput);
            Assert.DoesNotContain("(left, right) =", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures overloaded local method invocations keep the resolved non-generic overload instead of falling back to a same-name ref-suffixed helper.
        /// </summary>
        [Fact]
        public void WriteOutput_WithOverloadedInvocation_PrefersResolvedNonGenericOverload() {
            string source = """
                public class Fixture {
                    public void Work(int value, int other) {
                    }

                    public void Work<T>(int value, int other, ref T state) {
                    }

                    public void Run() {
                        Work(1, 2);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("this->Work(1, 2);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Work__ref2(1, 2)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures overloaded instance method groups assigned to delegates emit one typed method-pointer cast so std::bind_front selects the intended overload.
        /// </summary>
        [Fact]
        public void WriteOutput_WithOverloadedDelegateMethodGroup_EmitsTypedMethodPointerCast() {
            string source = """
                using System;

                public class Fixture {
                    Action<int> handler;

                    public void Bind() {
                        handler = Work;
                    }

                    void Work(int value) {
                    }

                    void Work(int value, string text) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("static_cast<void (Fixture::*)(int32_t)>(&Fixture::Work)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("std::bind_front(", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures optional value-type parameters with a default literal emit one value construction rather than nullptr.
        /// </summary>
        [Fact]
        public void WriteOutput_WithOptionalValueTypeDefault_EmitsValueConstruction() {
            string source = """
                public struct Handle {
                }

                public class Fixture {
                    public void Consume(Handle handle = default) {
                    }

                    public void Run() {
                        Consume();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("this->Consume(Handle());", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Consume(nullptr)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures omitted optional unmanaged pointer parameters stay nullptr instead of lowering to one pointer pseudo-construction.
        /// </summary>
        [Fact]
        public void WriteOutput_WithOptionalPointerDefault_EmitsNullptr() {
            string source = """
                public unsafe class Fixture {
                    public void Consume(void* context = null) {
                    }

                    public void Run() {
                        Consume();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("this->Consume(nullptr);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("void*()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures extension-method invocations lower to one static helper call with the receiver as the first argument instead of an instance call with duplicated optional parameters.
        /// </summary>
        [Fact]
        public void WriteOutput_WithReducedExtensionInvocation_EmitsStaticHelperCall() {
            string source = """
                public static class Helpers {
                    public static void Validate(this int value, int laneCount = -1) {
                    }

                    public static void Run(int value, int laneCount) {
                        value.Validate(laneCount);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Helpers.cpp"));

            Assert.Contains("Helpers::Validate(value, laneCount);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("value.Validate(laneCount, -1)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures explicit-type tuple deconstruction declarations emit one temporary tuple followed by typed local assignments instead of one invalid ValueTuple assignment target.
        /// </summary>
        [Fact]
        public void WriteOutput_WithTypedTupleDeconstructionDeclaration_EmitsTypedLocalAssignments() {
            string source = """
                public class Fixture {
                    (int, int) Prepare() {
                        return (3, 4);
                    }

                    public int Run() {
                        (int left, int right) = Prepare();
                        return left + right;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("const auto __deconstruct_", sourceOutput);
            Assert.Contains("int32_t left = __deconstruct_", sourceOutput);
            Assert.Contains("int32_t right = __deconstruct_", sourceOutput);
            Assert.DoesNotContain("new ValueTuple<int32_t, int32_t>(left, right) =", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures null-conditional instance method statements lower to one explicit null guard instead of preserving raw conditional-access syntax.
        /// </summary>
        [Fact]
        public void WriteOutput_WithConditionalAccessMethodInvocation_EmitsNullGuardedCall() {
            string source = """
                public class Child {
                    public void Ping() {
                    }
                }

                public class Fixture {
                    public void Run(Child value) {
                        value?.Ping();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("if (value != nullptr)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("value->Ping();", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("?.Ping()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures null-conditional abstract generic calls use the generated runtime dispatch chain inside the null guard instead of direct unresolved base-template calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithConditionalAccessAbstractGenericInvocationAndMultipleImplementations_UsesRuntimeDispatchChain() {
            string source = """
                public abstract class WorkerBase {
                    public abstract void Execute<T>(ref int value);
                }

                public sealed class WorkerA : WorkerBase {
                    public override void Execute<T>(ref int value) {
                        value++;
                    }
                }

                public sealed class WorkerB : WorkerBase {
                    public override void Execute<T>(ref int value) {
                        value += 2;
                    }
                }

                public static class WorkerFactory {
                    public static WorkerBase Create(bool useB) {
                        return useB ? new WorkerB() : new WorkerA();
                    }
                }

                public class Fixture {
                    public void Run(WorkerBase value, ref int count) {
                        value?.Execute<int>(ref count);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("if (value != nullptr)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("dynamic_cast<::WorkerA*>(value)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("dynamic_cast<::WorkerB*>(value)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("heCppDispatchImpl->Execute__ref0<int32_t>(count)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("value->Execute__ref0<int32_t>(count)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures reference-type ToString calls without one generated override lower through the runtime string helper instead of assuming a missing member function.
        /// </summary>
        [Fact]
        public void WriteOutput_WithReferenceTypeToStringWithoutOverride_UsesRuntimeStringHelper() {
            string source = """
                public class Accessor {
                }

                public class Fixture {
                    Accessor value;

                    public string Run() {
                        return "existing: " + value.ToString();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("String::ToJoinString(this->value)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->value->ToString()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures reference-type interpolations without one generated ToString override lower through the runtime string helper instead of assuming a missing member function.
        /// </summary>
        [Fact]
        public void WriteOutput_WithReferenceTypeInterpolation_UsesRuntimeStringHelper() {
            string source = """
                public class Accessor {
                }

                public class Fixture {
                    Accessor value;

                    public string Run() {
                        return $"existing: {value}";
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("String::ToJoinString(this->value)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->value->ToString()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures ref locals initialized from ref conditional expressions preserve both branches instead of dropping the referenced targets.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefConditionalExpression_EmitsReferencedBranches() {
            string source = """
                public class Fixture {
                    int left;
                    int right;

                    public ref int Pick(bool useLeft) {
                        ref var selected = ref useLeft ? ref left : ref right;
                        return ref selected;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("auto& selected = useLeft ? this->left : this->right;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(" ?  : ", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures object initializer field assignments keep the authored member name when it collides with the containing generic type name.
        /// </summary>
        [Fact]
        public void WriteOutput_WithObjectInitializerMemberNameCollision_UsesDirectMemberName() {
            string source = """
                public struct Filter<T> {
                    public Fixture<T> Fixture;
                    public int Value;
                }

                public class Fixture<T> {
                    public Filter<T> Create() {
                        return new Filter<T> { Fixture = this, Value = 4 };
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("__object_", sourceOutput, StringComparison.Ordinal);
            Assert.Contains(".Fixture = this;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(".&Fixture_1<T>::Fixture", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures member access on parenthesized value-type expressions preserves direct access instead of degrading to pointer-style access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithParenthesizedValueTypeMemberAccess_UsesDotAccess() {
            string source = """
                public struct Vec {
                    public float X;

                    public float Length() {
                        return X;
                    }

                    public static Vec operator -(Vec left, Vec right) {
                        return left;
                    }
                }

                public class Fixture {
                    public float Run(Vec left, Vec right) {
                        return (left - right).Length();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("(left - right).Length()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("(left - right)->Length()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures member access on parenthesized value-type expressions formed from dereferenced pointers still uses direct access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDereferencedPointerValueTypeMemberAccess_UsesDotAccess() {
            string source = """
                public unsafe struct Vec {
                    public float X;

                    public float Length() {
                        return X;
                    }

                    public static Vec operator -(Vec left, Vec right) {
                        return left;
                    }
                }

                public unsafe class Fixture {
                    public float Run(Vec* left, Vec* right) {
                        return (*left - *right).Length();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("(*left - *right).Length()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("(*left - *right)->Length()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures primitive positive-infinity static members lower through the portable number runtime instead of unresolved pseudo-members.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPrimitivePositiveInfinityMemberAccess_UsesNumberRuntimeInfinity() {
            string source = """
                public class Fixture {
                    public float Run() {
                        return float.PositiveInfinity;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("Number::PositiveInfinity<float>()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("float::PositiveInfinity", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures System.Math inverse cosine calls lower to the portable math runtime surface.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMathAcosCall_UsesRuntimeMathSurface() {
            string source = """
                public class Fixture {
                    public double Run(double value) {
                        return System.Math.Acos(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("Math::Acos(value)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures NativeMemory static allocation helpers lower to the portable runtime surface instead of unresolved managed symbols.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNativeMemoryStaticCall_UsesRuntimeNativeMemorySurface() {
            string source = """
                public unsafe class Fixture {
                    public void* Run() {
                        return System.Runtime.InteropServices.NativeMemory.AlignedAlloc((nuint)16, (nuint)8);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("NativeMemory::AlignedAlloc", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures the shared NativeMemory runtime asset does not require std::aligned_alloc on non-MSVC toolchains.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNativeMemoryStaticCall_EmitsPortableAlignedAllocationRuntime() {
            string source = """
                public unsafe class Fixture {
                    public void* Run() {
                        return System.Runtime.InteropServices.NativeMemory.AlignedAlloc((nuint)16, (nuint)8);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string runtimeHeaderOutput = File.ReadAllText(Path.Combine(output.OutputPath, "system", "runtime", "interopservices", "native_memory.hpp"));

            Assert.DoesNotContain("std::aligned_alloc", runtimeHeaderOutput, StringComparison.Ordinal);
            Assert.Contains("std::malloc", runtimeHeaderOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures parenthesized lambda expressions lower to native lambdas instead of preserving raw C# arrow syntax.
        /// </summary>
        [Fact]
        public void WriteOutput_WithParenthesizedLambdaExpression_EmitsNativeLambda() {
            string source = """
                public delegate int Mapper(int index);

                public class Fixture {
                    public Mapper Create() {
                        return (int value) => value + 1;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("[&](int32_t value)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("=>", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures simple lambda expressions lower to native lambdas instead of preserving raw C# arrow syntax.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSimpleLambdaExpression_EmitsNativeLambda() {
            string source = """
                public delegate int Mapper(int index);

                public class Fixture {
                    public Mapper Create() {
                        return value => value + 1;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("[&](int32_t value)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("=>", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures simple lambdas used as constructor arguments preserve the closing invocation syntax around the emitted native lambda.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSimpleLambdaConstructorArgument_ClosesInvocationAfterNativeLambda() {
            string source = """
                public delegate int Mapper(int index);

                public class Holder {
                    public Holder(Mapper map) {
                    }
                }

                public class Fixture {
                    public Holder Create() {
                        return new Holder(value => value + 1);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("new ::Holder([&](int32_t value)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("});", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures direct delegate wrapper construction from a simple lambda closes the generated invocation after the native lambda body.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSimpleLambdaDelegateConstruction_ClosesInvocationAfterNativeLambda() {
            string source = """
                public delegate int Mapper(int index);

                public class Fixture {
                    public Mapper Create() {
                        return new Mapper(value => value + 1);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("new ::Mapper([&](int32_t value)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("});", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures direct delegate wrapper construction from a captured lambda still closes the generated invocation after the native lambda body.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCapturedLambdaDelegateConstruction_ClosesInvocationAfterNativeLambda() {
            string source = """
                public delegate int Mapper(int index);

                public class Fixture {
                    public Mapper Create(int[] copy) {
                        return new Mapper(substepIndex => copy[substepIndex]);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("new ::Mapper([&](int32_t substepIndex)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("});", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures assigning a directly-constructed captured delegate preserves the closing invocation after the native lambda body.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCapturedLambdaDelegateAssignment_ClosesInvocationAfterNativeLambda() {
            string source = """
                public delegate int Mapper(int index);

                public struct Fixture {
                    public Mapper Scheduler;

                    public Fixture(int[] copy) {
                        Scheduler = new Mapper(substepIndex => copy[substepIndex]);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("this->Scheduler = new ::Mapper([&](int32_t substepIndex)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("});", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures mixed numeric clamp calls preserve the dominant floating-point overload instead of emitting ambiguous integer literals.
        /// </summary>
        [Fact]
        public void WriteOutput_WithFloatClampBounds_EmitsFloatingPointArguments() {
            string source = """
                public static class MathHelper {
                    public static float Clamp(float value, float minValue, float maxValue) {
                        return value;
                    }
                }

                public class Fixture {
                    public float Run(float value) {
                        return MathHelper.Clamp(value, -1, 1);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("MathHelper::Clamp(value, -1.0f, 1.0f)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures ref-reinterpreted value receivers preserve instance method calls instead of inventing missing ref-suffixed methods.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefUnsafeValueMethodCall_UsesExistingInstanceMethod() {
            string source = """
                public struct Vec {
                    public float X;

                    public float Length() {
                        return X;
                    }
                }

                public static class UnsafeShim {
                    public static ref TTo As<TFrom, TTo>(ref TFrom value) {
                        throw new System.NotImplementedException();
                    }
                }

                public static class Helper {
                    public static float Read(ref Vec value) {
                        return UnsafeShim.As<Vec, Vec>(ref value).Length();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Helper.cpp"));

            Assert.Contains(".Length()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Length__ref0()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures the real System.Runtime.CompilerServices.Unsafe intrinsic path preserves instance method calls on the reinterpreted ref receiver.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRuntimeUnsafeValueMethodCall_UsesExistingInstanceMethod() {
            string source = """
                namespace System.Runtime.CompilerServices {
                    public static class Unsafe {
                        public static ref TTo As<TFrom, TTo>(ref TFrom value) {
                            throw new global::System.NotImplementedException();
                        }
                    }
                }

                public struct Vec {
                    public float X;

                    public float Length() {
                        return X;
                    }
                }

                public static class Helper {
                    public static float Read(ref Vec value) {
                        return System.Runtime.CompilerServices.Unsafe.As<Vec, Vec>(ref value).Length();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Helper.cpp"));

            Assert.Contains(".Length()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Length__ref0()", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures BEPU-style captured delegate construction in a value-type constructor closes after the native lambda body.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSolveDescriptionStyleCapturedDelegateConstruction_ClosesInvocationAfterNativeLambda() {
            string source = """
                public delegate int SubstepVelocityIterationScheduler(int substepIndex);

                public struct SolveDescription {
                    public SubstepVelocityIterationScheduler VelocityIterationScheduler;

                    public SolveDescription(System.ReadOnlySpan<int> substepVelocityIterations) {
                        int[] copy = substepVelocityIterations.ToArray();
                        VelocityIterationScheduler = new SubstepVelocityIterationScheduler(substepIndex => copy[substepIndex]);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "SolveDescription.cpp"));

            Assert.Contains("this->VelocityIterationScheduler = new ::SubstepVelocityIterationScheduler([&](int32_t substepIndex)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("});", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures BEPU-style delegate construction remains closed when the delegate type and consumer live in separate source files.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCrossFileSolveDescriptionDelegateConstruction_ClosesInvocationAfterNativeLambda() {
            IReadOnlyDictionary<string, string> sources = new Dictionary<string, string> {
                ["SubstepVelocityIterationScheduler.cs"] = """
                    namespace BepuPhysics {
                        public delegate int SubstepVelocityIterationScheduler(int substepIndex);
                    }
                    """,
                ["Buffer.cs"] = """
                    namespace BepuUtilities.Memory {
                        public struct Buffer<T> {
                            public T[] Values;

                            public static implicit operator System.ReadOnlySpan<T>(Buffer<T> buffer) {
                                return buffer.Values;
                            }
                        }
                    }
                    """,
                ["SolveDescription.cs"] = """
                    using BepuUtilities.Memory;
                    using System;

                    namespace BepuPhysics {
                        public struct SolveDescription {
                            public int VelocityIterationCount;
                            public int SubstepCount;
                            public int FallbackBatchThreshold;
                            public SubstepVelocityIterationScheduler VelocityIterationScheduler;

                            public const int DefaultFallbackBatchThreshold = 64;

                            public SolveDescription(ReadOnlySpan<int> substepVelocityIterations, int fallbackVelocityIterationCount = 1, int fallbackBatchThreshold = DefaultFallbackBatchThreshold) {
                                SubstepCount = substepVelocityIterations.Length;
                                VelocityIterationCount = fallbackVelocityIterationCount;
                                FallbackBatchThreshold = fallbackBatchThreshold;
                                var copy = substepVelocityIterations.ToArray();
                                VelocityIterationScheduler = new SubstepVelocityIterationScheduler(substepIndex => copy[substepIndex]);
                            }

                            public static implicit operator SolveDescription(Buffer<int> substepVelocityIterations) {
                                return new SolveDescription(substepVelocityIterations);
                            }
                        }
                    }
                    """
            };

            ConversionOutput output = RunConversion(sources);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "SolveDescription.cpp"));

            Assert.Contains("this->VelocityIterationScheduler = new ::SubstepVelocityIterationScheduler([&](int32_t substepIndex)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("});", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures lambda assignments to delegate-typed targets do not leave partial delegate-construction wrappers behind.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDelegateLambdaAssignment_EmitsClosedDelegateWrapper() {
            string source = """
                public delegate int SubstepVelocityIterationScheduler(int substepIndex);

                public struct SolveDescription {
                    public SubstepVelocityIterationScheduler VelocityIterationScheduler;

                    public SolveDescription(System.ReadOnlySpan<int> substepVelocityIterations) {
                        int[] copy = substepVelocityIterations.ToArray();
                        VelocityIterationScheduler = substepIndex => copy[substepIndex];
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "SolveDescription.cpp"));

            Assert.Contains("this->VelocityIterationScheduler = new ::SubstepVelocityIterationScheduler([&](int32_t substepIndex)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("});", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures multiple for-loop incrementors stay comma separated so native update clauses remain valid.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMultipleForIncrementors_EmitsCommaSeparatedUpdates() {
            string source = """
                public static class LoopGate {
                    public static void Step(int l, int p, int q, int r) {
                        int j = r;
                        int i = l;
                        for (int k = l; k < p; k++, j--) {
                        }

                        for (int k = r - 1; k > q; k--, i++) {
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "LoopGate.cpp"));

            Assert.Contains("for (int32_t k = l; k < p; k++, j--)", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("for (int32_t k = r - 1; k > q; k--, i++)", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("k++j--", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("k--i++", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures switch expressions lower to a native conditional expression instead of dropping the initializer body.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSwitchExpressionInitializer_EmitsConditionalExpression() {
            string source = """
                public unsafe struct SwitchGate {
                    public static int* Pick(int selector, int* first, int* second, int* third) {
                        int* result = selector switch {
                            0 => first,
                            1 => second,
                            _ => third
                        };
                        return result;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "SwitchGate.cpp"));

            Assert.Contains("int32_t *result =", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("int32_t *result = ;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("? ", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures span locals backed by stackalloc initializers emit a concrete backing array and span view.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSpanStackAllocInitializer_EmitsBackingArrayAndSpanView() {
            string source = """
                public struct BodyHandle {
                    public int Value;
                }

                public static class StackAllocGate {
                    public static System.Span<BodyHandle> Create(BodyHandle first, BodyHandle second) {
                        System.Span<BodyHandle> bodyHandles = stackalloc BodyHandle[] { first, second };
                        return bodyHandles;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "StackAllocGate.cpp"));

            Assert.Contains("bodyHandles_stackalloc[2]", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("bodyHandles_stackalloc[0] = first;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("bodyHandles_stackalloc[1] = second;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("bodyHandles_stackalloc, 2)", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures runtime span constructions in generic implicit operators stay globally qualified so native parsing does not misread the template-id.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericSpanImplicitOperators_QualifiesRuntimeSpanConstruction() {
            string source = """
                public struct QuickList<T> {
                    public T[] Values;

                    public static implicit operator System.ReadOnlySpan<T>(QuickList<T> list) {
                        return new System.ReadOnlySpan<T>(list.Values);
                    }

                    public static implicit operator System.Span<T>(QuickList<T> list) {
                        return new System.Span<T>(list.Values);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "QuickList_1.cpp"));

            Assert.Contains("return ::ReadOnlySpan<T>(", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("return ::Span<T>(", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures System.Array.Clear lowers to the runtime array helper instead of a missing generated static member.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemArrayClear_UsesRuntimeArrayHelper() {
            string source = """
                public class Fixture {
                    public void Run(int[] values) {
                        System.Array.Clear(values, 0, values.Length);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("Array<int32_t>::Clear(values, 0, values->get_Length())", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures while-loop out-var conditions emit the required local declaration before the loop header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithWhileOutVarCondition_DeclaresVariableBeforeLoop() {
            string source = """
                public class StackLike {
                    int Value;
                    bool HasValue;

                    public StackLike(int value) {
                        Value = value;
                        HasValue = true;
                    }

                    public bool TryPop(out int next) {
                        next = Value;
                        bool result = HasValue;
                        HasValue = false;
                        return result;
                    }
                }

                public class Fixture {
                    public int Run(StackLike stack) {
                        int sum = 0;
                        while (stack.TryPop(out var next)) {
                            sum += next;
                        }

                        return sum;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("int32_t next;", sourceOutput);
            Assert.Contains("while (stack->TryPop__out0(next))", sourceOutput);
        }

        /// <summary>
        /// Ensures copied runtime templates expose the console and regex accessors required by generated tooling code.
        /// </summary>
        [Fact]
        public void WriteOutput_CopiesRuntimeTemplatesWithConsoleAndRegexCollectionHelpers() {
            string source = """
                public class Fixture {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string consoleHeaderPath = Path.Combine(output.OutputPath, "system", "console.hpp");
            string regexHeaderPath = Path.Combine(output.OutputPath, "system", "text", "regular_expressions", "regex.hpp");
            string consoleHeader = File.ReadAllText(consoleHeaderPath);
            string regexHeader = File.ReadAllText(regexHeaderPath);

            Assert.Contains("static bool Write(const std::string& text);", consoleHeader);
            Assert.Contains("static bool WriteLine();", consoleHeader);
            Assert.Contains("static std::string ReadLine();", consoleHeader);
            Assert.Contains("GroupAccessor get_Item(const std::string& name) const", regexHeader);
            Assert.Contains("Match get_Item(int32_t index) const", regexHeader);
        }

        /// <summary>
        /// Ensures invocation overload resolution prefers exact argument types instead of reusing the first same-arity candidate.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSameArityOverloads_UsesExactGeneratedSuffix() {
            string source = """
                public struct Handle {
                    public int Value;

                    public Handle(int value) {
                        Value = value;
                    }
                }

                public struct Collector {
                    public int Tag;
                }

                public class Fixture {
                    public void Enumerate(ref int value, ref Collector collector) {
                        collector.Tag = 1;
                    }

                    public void Enumerate(Handle value, ref Collector collector) {
                        collector.Tag = 2;
                    }

                    public int Run(Handle value) {
                        var collector = new Collector();
                        Enumerate(value, ref collector);
                        return collector.Tag;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("this->Enumerate__ref1(value, collector);", sourceOutput);
            Assert.DoesNotContain("this->Enumerate__ref0_ref1(value, collector);", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures C# const fields emit inline native constants so integral constants stay usable in headers without forcing constexpr strings.
        /// </summary>
        [Fact]
        public void WriteOutput_WithConstField_EmitsInlineNativeConstantField() {
            string source = """
                public class Fixture {
                    public const int MaximumBodiesPerConstraint = 4;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.hpp"));

            Assert.Contains("inline static const int32_t MaximumBodiesPerConstraint = 4;", headerOutput);
            Assert.DoesNotContain("static int32_t MaximumBodiesPerConstraint;", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generic overload invocations emit the exact ref-suffix chosen by Roslyn instead of reusing the first same-arity candidate.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericSameArityOverloads_UsesExactGeneratedSuffix() {
            string source = """
                public interface IForEach<T> {
                    void LoopBody(T value);
                }

                public struct Handle {
                    public int Value;
                }

                public struct Batch {
                }

                public class Enumerator : IForEach<int> {
                    public void LoopBody(int value) {
                    }
                }

                public class Fixture {
                    public void Enumerate<TEnumerator>(Handle handle, ref TEnumerator enumerator) where TEnumerator : IForEach<int> {
                    }

                    public void Enumerate<TEnumerator>(ref Batch batch, int indexInBatch, ref TEnumerator enumerator) where TEnumerator : IForEach<int> {
                    }

                    public void Run(Handle handle, Enumerator enumerator) {
                        Enumerate<Enumerator>(handle, ref enumerator);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("Enumerate__ref1<", sourceOutput);
            Assert.Contains("(handle, enumerator);", sourceOutput);
            Assert.DoesNotContain("Enumerate__ref0_ref2<", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures inferred generic overload invocations that mix member-access and ref arguments keep the correct emitted ref suffix on both overload shapes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInferredGenericMixedArityOverloads_UsesExactGeneratedSuffixForEachCallShape() {
            string source = """
                public interface IForEach<T> {
                    void LoopBody(T value);
                }

                public struct ConstraintHandle {
                    public int Value;
                }

                public struct TypeBatch {
                }

                public struct ConstraintReference {
                    public ConstraintHandle ConnectingConstraintHandle;
                }

                public class Enumerator : IForEach<int> {
                    public void LoopBody(int value) {
                    }
                }

                public class Fixture {
                    public void EnumerateConnectedDynamicBodies<TEnumerator>(ref TypeBatch typeBatch, int indexInTypeBatch, ref TEnumerator enumerator) where TEnumerator : IForEach<int> {
                    }

                    public void EnumerateConnectedDynamicBodies<TEnumerator>(ConstraintHandle constraintHandle, ref TEnumerator enumerator) where TEnumerator : IForEach<int> {
                    }

                    public void EnumerateConnectedRawBodyReferences<TEnumerator>(ConstraintHandle constraintHandle, ref TEnumerator enumerator) where TEnumerator : IForEach<int> {
                    }

                    public void EnumerateConnectedRawBodyReferences<TEnumerator>(ref TypeBatch typeBatch, int indexInTypeBatch, ref TEnumerator enumerator) where TEnumerator : IForEach<int> {
                    }

                    public void Run(ConstraintReference constraint, ref TypeBatch typeBatch, int indexInTypeBatch, Enumerator enumerator) {
                        EnumerateConnectedDynamicBodies(constraint.ConnectingConstraintHandle, ref enumerator);
                        EnumerateConnectedRawBodyReferences(ref typeBatch, indexInTypeBatch, ref enumerator);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("EnumerateConnectedDynamicBodies__ref1<Enumerator*>(constraint.ConnectingConstraintHandle, enumerator);", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("EnumerateConnectedRawBodyReferences__ref0_ref2<Enumerator*>(typeBatch, indexInTypeBatch, enumerator);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("EnumerateConnectedDynamicBodies__ref0_ref2<Enumerator*>", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("EnumerateConnectedRawBodyReferences__ref1<Enumerator*>(typeBatch, indexInTypeBatch, enumerator);", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures local identifiers that collide with C++ keywords are remapped to safe emitted names.
        /// </summary>
        [Fact]
        public void WriteOutput_WithKeywordNamedLocal_EmitsSafeIdentifier() {
            string source = """
                public static class Fixture {
                    public static int Run(int value) {
                        var unsigned = value + 1;
                        return unsigned;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("int32_t unsigned_ = value + 1;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("return unsigned_;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("int32_t unsigned = value + 1;", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nested structs inside static classes preserve every declared field in generated output.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedStructFields_PreservesTrailingFieldDeclarations() {
            string source = """
                public struct FieldA {
                }

                public struct FieldB {
                }

                public struct FieldC {
                }

                public static class Fixture {
                    public struct Payload {
                        public FieldA LinearA;
                        public FieldB AngularA;
                        public FieldC AngularB;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "Payload.hpp"));

            Assert.Contains("::FieldA LinearA;", header, StringComparison.Ordinal);
            Assert.Contains("::FieldB AngularA;", header, StringComparison.Ordinal);
            Assert.Contains("::FieldC AngularB;", header, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures constructor calls select the by-value overload unambiguously when a matching ref overload also exists.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefAndValueConstructors_WrapsByValueArgumentToAvoidNativeAmbiguity() {
            string source = """
                public struct Lane {
                    public int Value;
                }

                public struct Wide {
                    public Lane X;

                    public Wide(ref Lane lane) {
                        X = lane;
                    }

                    public Wide(Lane lane) {
                        X = lane;
                    }
                }

                public static class Fixture {
                    public static Wide Run(Lane lane) {
                        return new Wide(lane);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("return ::Wide(::Lane(lane));", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("return ::Wide(lane);", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures constructor overload resolution also keeps by-value creation unambiguous when the argument is sourced from a field access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRefAndValueConstructorsFromFieldAccess_WrapsByValueArgumentToAvoidNativeAmbiguity() {
            string source = """
                public struct Lane {
                    public int Value;
                }

                public struct Wide {
                    public Lane X;

                    public Wide(ref Lane lane) {
                        X = lane;
                    }

                    public Wide(Lane lane) {
                        X = lane;
                    }
                }

                public class Fixture {
                    public Lane Radius;

                    public Wide Run() {
                        return new Wide(Radius);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("return ::Wide(::Lane(this->Radius));", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("return ::Wide(this->Radius);", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures a selected ref constructor overload still lowers unambiguously when an equivalent by-value overload exists.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSelectedRefConstructorAndCompetingValueOverload_CastsArgumentToReference() {
            string source = """
                public struct Lane {
                    public int Value;
                }

                public struct Wide {
                    public Lane X;

                    public Wide(ref Lane lane) {
                        X = lane;
                    }

                    public Wide(Lane lane) {
                        X = lane;
                    }
                }

                public class Fixture {
                    public Lane Radius;

                    public Wide Run() {
                        return new Wide(ref Radius);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("return ::Wide(static_cast<::Lane&>(this->Radius));", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("return ::Wide(this->Radius);", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures unqualified member-template calls inside generic classes use the dependent <c>this->template</c> form required by C++.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericClassMemberTemplateCall_EmitsThisTemplateQualifier() {
            string source = """
                public class Fixture<T> {
                    public void Copy<TItem>(TItem value) {
                    }

                    public void Run(T value) {
                        Copy<T>(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("this->template Copy<T>(value);", sourceOutput);
        }

        /// <summary>
        /// Ensures nested types with the same leaf name under different outer types emit distinct generated names and files.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedTypeLeafNameCollision_UsesContainingTypeQualifiedGeneratedNames() {
            ConversionOutput output = RunConversion(new Dictionary<string, string>(StringComparer.Ordinal) {
                ["TangentFriction.cs"] = """
                    namespace BepuPhysics.Constraints.Contact {
                        public class TangentFriction {
                            public struct Jacobians {
                                public int AngularA;
                                public int LinearA;
                            }
                        }
                    }
                    """,
                ["TangentFrictionOneBody.cs"] = """
                    namespace BepuPhysics.Constraints.Contact {
                        public class TangentFrictionOneBody {
                            public struct Jacobians {
                                public int AngularA;
                                public int LinearA;
                                public int AngularB;
                            }
                        }
                    }
                    """,
                ["Fixture.cs"] = """
                    namespace BepuPhysics.Constraints.Contact {
                        public class Fixture {
                            public TangentFriction.Jacobians Pair;
                            public TangentFrictionOneBody.Jacobians Single;
                        }
                    }
                    """
            });

            string tangentHeaderPath = Path.Combine(output.OutputPath, "BepuPhysics_Constraints_Contact_TangentFriction_Jacobians.hpp");
            string oneBodyHeaderPath = Path.Combine(output.OutputPath, "BepuPhysics_Constraints_Contact_TangentFrictionOneBody_Jacobians.hpp");

            Assert.True(File.Exists(tangentHeaderPath));
            Assert.True(File.Exists(oneBodyHeaderPath));

            string tangentHeader = File.ReadAllText(tangentHeaderPath);
            string oneBodyHeader = File.ReadAllText(oneBodyHeaderPath);
            string fixtureHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.hpp"));

            Assert.Contains("class BepuPhysics_Constraints_Contact_TangentFriction_Jacobians", tangentHeader);
            Assert.Contains("class BepuPhysics_Constraints_Contact_TangentFrictionOneBody_Jacobians", oneBodyHeader);
            Assert.DoesNotContain("AngularB", tangentHeader, StringComparison.Ordinal);
            Assert.Contains("AngularB", oneBodyHeader);
            Assert.Contains("::BepuPhysics_Constraints_Contact_TangentFriction_Jacobians Pair;", fixtureHeader);
            Assert.Contains("::BepuPhysics_Constraints_Contact_TangentFrictionOneBody_Jacobians Single;", fixtureHeader);
        }

        /// <summary>
        /// Ensures nested type references written as bare identifiers inside their containing type still resolve through the full nested Roslyn identity.
        /// </summary>
        [Fact]
        public void WriteOutput_WithBareNestedTypeIdentifierInContainingType_UsesContainingTypeQualifiedGeneratedName() {
            ConversionOutput output = RunConversion(new Dictionary<string, string>(StringComparer.Ordinal) {
                ["TangentFriction.cs"] = """
                    namespace BepuPhysics.Constraints.Contact {
                        public class TangentFriction {
                            public struct Jacobians {
                                public int AngularA;
                                public int LinearA;
                                public int AngularB;
                            }

                            public static void Apply(Jacobians jacobians) {
                                jacobians.AngularB = 1;
                            }
                        }
                    }
                    """,
                ["TangentFrictionOneBody.cs"] = """
                    namespace BepuPhysics.Constraints.Contact {
                        public class TangentFrictionOneBody {
                            public struct Jacobians {
                                public int AngularA;
                                public int LinearA;
                            }
                        }
                    }
                    """
            });

            string tangentSource = File.ReadAllText(Path.Combine(output.OutputPath, "TangentFriction.cpp"));

            Assert.Contains("::BepuPhysics_Constraints_Contact_TangentFriction_Jacobians jacobians", tangentSource);
            Assert.Contains("jacobians.AngularB = 1;", tangentSource);
            Assert.DoesNotContain("::BepuPhysics_Constraints_Contact_Jacobians jacobians", tangentSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures unconstrained generic null comparisons lower through the portable runtime helper instead of emitting raw nullptr comparisons that fail for value-type instantiations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUnconstrainedGenericNullComparison_UsesPortableNullHelper() {
            string source = """
                public class Fixture<TComparer> {
                    public bool HasComparer(TComparer comparer) {
                        return comparer != null;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture_1.cpp"));

            Assert.Contains("return !he_cpp_is_null(comparer);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("comparer != nullptr", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures explicit interface property implementations remain publicly callable in generated C++ so interface-constrained generic code can access them.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitInterfacePropertyImplementation_EmitsPublicGetter() {
            ConversionOutput output = RunConversion(new Dictionary<string, string>(StringComparer.Ordinal) {
                ["ICounted.cs"] = """
                    public interface ICounted<TManifold> where TManifold : struct, ICounted<TManifold> {
                        int Count { get; }
                    }
                    """,
                ["Manifold.cs"] = """
                    public struct Manifold : ICounted<Manifold> {
                        public int Count;

                        int ICounted<Manifold>.Count => Count;
                    }
                    """,
                ["Fixture.cs"] = """
                    public class Fixture {
                        public bool Any<TManifold>(TManifold manifold) where TManifold : struct, ICounted<TManifold> {
                            return manifold.Count > 0;
                        }
                    }
                    """
            });

            string manifoldHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Manifold.hpp"));
            string fixtureSource = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.cpp"));

            Assert.Contains("int32_t get_Count();", manifoldHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("private:\r\n    int32_t get_Count();", manifoldHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("private:\n    int32_t get_Count();", manifoldHeader, StringComparison.Ordinal);
            Assert.Contains("return manifold.get_Count() > 0;", fixtureSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures rotation-style value-type flows emit final native syntax without downstream text normalization.
        /// </summary>
        [Fact]
        public void WriteOutput_WithRotationValueTypes_EmitsFinalValidNativeSyntax() {
            string source = """
                public struct float3 {
                    public float X;
                    public float Y;
                    public float Z;

                    public static float3 Normalize(float3 value) {
                        return value;
                    }
                }

                public struct float4 {
                    public void Normalize() {
                    }

                    public static void CreateFromYawPitchRoll(float yaw, float pitch, float roll, out float4 result) {
                        result = new float4();
                    }

                    public static void CreateFromAxisAngle(float3 axis, float angle, out float4 result) {
                        result = new float4();
                    }

                    public static void Concatenate(ref float4 left, ref float4 right, out float4 result) {
                        result = left;
                    }
                }

                public sealed class RotationFixture {
                    public void Update(float yawRadians, float pitchRadians, float3 axis, float angle, float4 currentOrientation, float4 deltaRotation) {
                        float4 orientation;
                        float4.CreateFromYawPitchRoll(yawRadians, pitchRadians, 0.0f, out orientation);
                        orientation.Normalize();

                        float4 axisAngleRotation;
                        float4.CreateFromAxisAngle(axis, angle, out axisAngleRotation);

                        float4 mergedOrientation;
                        float4.Concatenate(ref currentOrientation, ref deltaRotation, out mergedOrientation);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RotationFixture.cpp"));

            Assert.Contains("float4 orientation;", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("float4::CreateFromYawPitchRoll__out3(yawRadians, pitchRadians, 0.0f, orientation);", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("orientation.Normalize();", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("float4::CreateFromAxisAngle__ref0_out2(axis, angle, axisAngleRotation);", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("float4::Concatenate__ref0_ref1_out2(currentOrientation, deltaRotation, mergedOrientation);", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("float4 *orientation;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("float4->CreateFromYawPitchRoll", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("orientation->Normalize()", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("float4::CreateFromAxisAngle(", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("float4::Concatenate(", sourceOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns the generated output bundle.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(string source, bool allowUnsafe = false, bool loadNativeRuntimeMetadata = false) {
            return RunConversion(new Dictionary<string, string>(StringComparer.Ordinal) {
                ["Fixture.cs"] = source
            }, allowUnsafe, loadNativeRuntimeMetadata);
        }

        /// <summary>
        /// Runs the C++ converter against one temporary single-file project using explicit type remaps and returns the generated output bundle.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <param name="typeRemaps">Configured source-to-target type remaps used by the conversion run.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversionWithTypeRemaps(string source, IReadOnlyDictionary<string, string> typeRemaps) {
            return RunConversionWithTypeRemaps(
                new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["Fixture.cs"] = source
                },
                typeRemaps);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary multi-file project and returns the generated output bundle.
        /// </summary>
        /// <param name="sources">Source file content keyed by relative file name.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(IReadOnlyDictionary<string, string> sources, bool allowUnsafe = false, bool loadNativeRuntimeMetadata = false) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-compile-validation-tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(rootPath, "Fixture.csproj");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(rootPath);
            File.WriteAllText(projectPath, CreateProjectFile(allowUnsafe));
            foreach (KeyValuePair<string, string> source in sources) {
                File.WriteAllText(Path.Combine(rootPath, source.Key), source.Value);
            }

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = loadNativeRuntimeMetadata;
            options.WriteConversionReport = true;

            CPPConversionRules rules = new CPPConversionRules();
            CPPCodeConverter converter = new CPPCodeConverter(rules, options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
            JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            string generatedText = ReadGeneratedOutput(outputPath);
            return new ConversionOutput(outputPath, generatedText, report);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary project using explicit type remaps and returns the generated output bundle.
        /// </summary>
        /// <param name="sources">Source file content keyed by relative file name.</param>
        /// <param name="typeRemaps">Configured source-to-target type remaps used by the conversion run.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversionWithTypeRemaps(IReadOnlyDictionary<string, string> sources, IReadOnlyDictionary<string, string> typeRemaps) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-compile-validation-tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(rootPath, "Fixture.csproj");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(rootPath);
            File.WriteAllText(projectPath, CreateProjectFile(false));
            foreach (KeyValuePair<string, string> source in sources) {
                File.WriteAllText(Path.Combine(rootPath, source.Key), source.Value);
            }

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.TypeRemaps = typeRemaps;
            options.LoadNativeRuntimeMetadata = false;
            options.WriteConversionReport = true;

            CPPConversionRules rules = new CPPConversionRules();
            CPPCodeConverter converter = new CPPCodeConverter(rules, options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
            JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            string generatedText = ReadGeneratedOutput(outputPath);
            return new ConversionOutput(outputPath, generatedText, report);
        }

        /// <summary>
        /// Creates a minimal SDK-style project file for temporary converter fixtures.
        /// </summary>
        /// <returns>Project file content suitable for Roslyn-based analysis.</returns>
        static string CreateProjectFile(bool allowUnsafe) {
            string allowUnsafeElement = allowUnsafe ? "    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n" : string.Empty;
            return
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                "  <PropertyGroup>\n" +
                "    <TargetFramework>net9.0</TargetFramework>\n" +
                "    <LangVersion>preview</LangVersion>\n" +
                "    <ImplicitUsings>enable</ImplicitUsings>\n" +
                "    <Nullable>disable</Nullable>\n" +
                allowUnsafeElement +
                "  </PropertyGroup>\n" +
                "</Project>\n";
        }

        /// <summary>
        /// Reads all generated headers and sources from a converter output directory into a single string for assertions.
        /// </summary>
        /// <param name="outputPath">Converter output directory to inspect.</param>
        /// <returns>Concatenated generated text content.</returns>
        static string ReadGeneratedOutput(string outputPath) {
            string[] files = Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return string.Join("\n", files.Select(File.ReadAllText));
        }

        /// <summary>
        /// Asserts that the conversion report contains no diagnostic entries for the supplied syntax kind.
        /// </summary>
        /// <param name="report">Parsed conversion report to inspect.</param>
        /// <param name="syntaxKind">Roslyn syntax kind that should be absent from the report.</param>
        static void AssertNoDiagnostic(JsonDocument report, string syntaxKind) {
            foreach (JsonElement diagnostic in report.RootElement.GetProperty("diagnostics").EnumerateArray()) {
                string actualSyntaxKind = diagnostic.GetProperty("syntaxKind").GetString() ?? string.Empty;
                Assert.NotEqual(syntaxKind, actualSyntaxKind);
            }
        }

        /// <summary>
        /// Represents the generated output artifacts captured for a compile-validation regression fixture.
        /// </summary>
        /// <param name="OutputPath">Generated output directory.</param>
        /// <param name="GeneratedText">Concatenated generated textual output.</param>
        /// <param name="Report">Parsed conversion report.</param>
        record ConversionOutput(string OutputPath, string GeneratedText, JsonDocument Report);
    }
}
