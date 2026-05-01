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
            Assert.Contains("return info + String::JoinArray(\", \", values->ToArray());", sourceOutput);
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

            Assert.Contains("class float3 : public IEquatable<::float3>", headerOutput);
            Assert.Contains("template <typename T>", runtimeHeaderOutput);
            Assert.Contains("class IEquatable", runtimeHeaderOutput);
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
            Assert.Contains("int2::int2() : X(), Y()", sourceOutput);
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

            Assert.Contains("TAsset* typedAsset = he_cpp_try_cast<TAsset>(asset);", sourceOutput);
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
            Assert.Contains("reader->ReadArray(new Func<Reader*, int32_t>(&Serializer::ReadInt))", sourceOutput);
            Assert.Contains("writer->WriteArray<int32_t>(values, new Action<Writer*, int32_t>(&Serializer::WriteInt))", sourceOutput);
            Assert.Contains("(*readElement)(this)", readerOutput);
            Assert.Contains("(*writeElement)(this, (*values)[i])", writerOutput);
            Assert.DoesNotContain("instanceof", sourceOutput);
            Assert.DoesNotContain("else     ModelAsset*", sourceOutput);
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
            Assert.Contains("Encoding::GetString(Encoding::UTF8, bytes)", sourceOutput);
            Assert.Contains("Encoding::GetBytes(Encoding::UTF8, value)", sourceOutput);
            Assert.Contains("int32_t totalBytesRead = 0;", sourceOutput);
            Assert.DoesNotContain("const int32_t totalBytesRead = 0;", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("BitConverter->", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Encoding::UTF8::", sourceOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "bit_converter.hpp")));
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
            Assert.Contains("(*this->Children)[i]->ParentEnabledChange((*this->Children)[i]->IsHierarchyEnabled);", sourceOutput);
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

            Assert.Contains("items->Count()", sourceOutput);
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

            Assert.Contains("items->Capacity()", sourceOutput);
            Assert.Contains("items->SetCapacity(desired)", sourceOutput);
            Assert.Contains("std::to_string(this->items->Capacity())", sourceOutput);
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
            Assert.Contains("for (const auto& key : *keys)", sourceOutput);
            Assert.DoesNotContain("0x1.0f", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("return  &&", sourceOutput, StringComparison.Ordinal);
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
