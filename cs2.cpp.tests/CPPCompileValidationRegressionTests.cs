using System.Text.Json;
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
            Assert.Contains("#include \"IContentProcessor_1.hpp\"", contentManagerHeader);
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
        /// Ensures static generated property chains keep the owning generated type as a header dependency and emit static access.
        /// </summary>
        [Fact]
        public void WriteOutput_WithStaticGeneratedPropertyChain_EmitsOwnerIncludeAndStaticAccess() {
            string source = """
                public class ThemeColors {
                    public string TextOnAccent;
                }

                public static class ThemeManager {
                    public static ThemeColors Colors;
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
            Assert.Contains("ThemeManager::Colors->TextOnAccent", sourceOutput);
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

            Assert.Contains("Distance.HasValue", sourceOutput);
            Assert.Contains("Distance.Value > 0", sourceOutput);
            Assert.DoesNotContain("Distance->HasValue", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Distance->Value", sourceOutput, StringComparison.Ordinal);
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

            Assert.Contains("LeftDistance = left ? Nullable<float>(value) : Nullable<float>(nullptr);", sourceOutput);
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

            Assert.Contains("anchorData->LeftDistance.HasValue", sourceOutput);
            Assert.Contains("anchorData->LeftDistance.Value > 0", sourceOutput);
            Assert.DoesNotContain("anchorData->LeftDistance->HasValue", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("anchorData->LeftDistance->Value", sourceOutput, StringComparison.Ordinal);
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

            Assert.Contains("LeftDistance = left ? Nullable<float>(anchorParent->Position->X) : Nullable<float>(nullptr);", sourceOutput);
            Assert.Contains("this->anchorData->LeftDistance.HasValue", sourceOutput);
            Assert.Contains("this->anchorData->LeftDistance.Value > 0", sourceOutput);
            Assert.DoesNotContain("LeftDistance = left ? anchorParent->Position->X : nullptr;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->anchorData->LeftDistance->HasValue", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("this->anchorData->LeftDistance->Value", sourceOutput, StringComparison.Ordinal);
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

            Assert.True(sourceOutput.Contains("LeftDistance = left ? Nullable<float>(Parent->Position->X) : Nullable<float>(nullptr);", StringComparison.Ordinal), sourceOutput);
            Assert.True(sourceOutput.Contains("RightDistance = right ? Nullable<float>(windowSize->X - Parent->Position->X) : Nullable<float>(nullptr);", StringComparison.Ordinal), sourceOutput);
            Assert.True(sourceOutput.Contains("anchorData->LeftDistance.HasValue", StringComparison.Ordinal), sourceOutput);
            Assert.True(sourceOutput.Contains("anchorData->LeftDistance.Value > 0", StringComparison.Ordinal), sourceOutput);
            Assert.DoesNotContain("LeftDistance = left ? Parent->Position->X : nullptr;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("RightDistance = right ? windowSize->X - Parent->Position->X : nullptr;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("anchorData->LeftDistance->HasValue", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("anchorData->LeftDistance->Value", sourceOutput, StringComparison.Ordinal);
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

            Assert.Contains("const std::string info = \"Anchored to: \";", sourceOutput);
            Assert.Contains("return info + String::Join(\", \", values);", sourceOutput);
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
        /// Ensures interface auto-properties remain publicly accessible in generated output.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInterfaceAutoProperties_EmitsPublicMembers() {
            string source = """
                public interface ICamera {
                    ushort LayerMask { get; set; }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "ICamera.hpp"));

            Assert.Contains("public:", header);
            Assert.Contains("uint16_t LayerMask;", header);
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

            Assert.Contains("value->Name", sourceOutput);
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

            Assert.Contains("this->RegisterProcessor(TextContentProcessorId, new ::TextContentProcessor(), new List<std::string>({ WildcardExtension }))", sourceOutput);
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

            Assert.Contains("RegisterProcessor(new ::TextContentProcessor())", sourceOutput);
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

            Assert.Contains("class TextContentProcessor : public IContentProcessor_1<TextContent>", headerOutput);
            Assert.Contains("#include \"IContentProcessor_1.hpp\"", headerOutput);
            Assert.Contains("#include \"TextContent.hpp\"", headerOutput);
            Assert.DoesNotContain("class TextContentProcessor : public IContentProcessor\n", headerOutput, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generated value types can inherit the native generic equatable runtime contract without a header-shape mismatch.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericEquatableImplementation_UsesGenericRuntimeContract() {
            string source = """
                public struct float3 : System.IEquatable<float3> {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "float3.hpp"));
            string runtimeHeaderOutput = File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_equatable.hpp"));

            Assert.Contains("class float3 : public IEquatable<float3>", headerOutput);
            Assert.Contains("template <typename T>", runtimeHeaderOutput);
            Assert.Contains("class IEquatable", runtimeHeaderOutput);
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

            Assert.Contains("TextContentProcessor()", sourceOutput);
            Assert.Contains("RawByteContentProcessor()", sourceOutput);
            Assert.DoesNotContain("[WildcardExtension]", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("class TextContentProcessor : public IContentProcessor_1<TextContent>", textProcessorHeader);
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

            Assert.Contains("items->Count()", sourceOutput);
            Assert.DoesNotContain("items->Count >", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("items->Count == ", sourceOutput, StringComparison.Ordinal);
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

            Assert.Contains("StringComparer::OrdinalIgnoreCase", sourceOutput);
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
            Assert.Contains("Math::Max(static_cast<double>(this->Font->LineHeight), 1.0)", sourceOutput);
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

            Assert.Contains("for (int32_t i = 0; i < items->Count(); i++)", sourceOutput);
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

            Assert.Contains("throw new ArgumentOutOfRangeException(\"value\", \"Value must be non-negative.\")", sourceOutput);
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

            Assert.Contains("throw new ArgumentException(\"Items must be provided.\", \"items\")", sourceOutput);
            Assert.Contains("ArgumentException(const std::string& message, const std::string& parameterName)", runtimeHeader);
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
            Assert.Contains("new Array<std::string>(sourceExtensions->Count())", sourceOutput);
            Assert.Contains("(*normalized)[0] = (*sourceExtensions)[0];", sourceOutput);
            Assert.Contains("static Array<T>* Empty()", runtimeHeader);
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

            Assert.Contains("new List<::ComboBoxItemVisual*>(items->Count())", sourceOutput);
            Assert.Contains("for (int32_t i = 0; i < items->Count(); i++)", sourceOutput);
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
        /// Runs the C++ converter against a temporary single-file project and returns the generated output bundle.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(string source) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-compile-validation-tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(rootPath, "Fixture.csproj");
            string sourcePath = Path.Combine(rootPath, "Fixture.cs");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(rootPath);
            File.WriteAllText(projectPath, CreateProjectFile());
            File.WriteAllText(sourcePath, source);

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
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
