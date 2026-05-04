using System.IO;
using cs2.core;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies focused class and enum emission behavior for the extracted C++ class emitter.
/// </summary>
public class CPPClassEmitterTests {
    /// <summary>
    /// Ensures a basic class emits one header declaration and one source definition with matching native signatures.
    /// </summary>
    [Fact]
    public void EmitClass_WritesHeaderAndSourceForPublicMethod() {
        CPPClassEmitter emitter = CreateEmitter();
        ConversionClass conversionClass = new ConversionClass {
            Name = "Player",
            DeclarationType = MemberDeclarationType.Class
        };

        ConversionFunction function = new ConversionFunction {
            Name = "Tick",
            AccessType = MemberAccessType.Public
        };
        function.InParameters.Add(new ConversionVariable {
            Name = "delta",
            VarType = new VariableType(typeName: "int")
        });
        conversionClass.Functions.Add(function);

        (string header, string source) = Emit(emitter, conversionClass);

        Assert.Contains("class Player", header);
        Assert.Contains("public:", header);
        Assert.Contains("void Tick(int32_t delta);", header);
        Assert.Contains("#include \"Player.hpp\"", source);
        Assert.Contains("void Player::Tick(int32_t delta)", source);
    }

    /// <summary>
    /// Ensures a trivial auto-property lowers into storage plus generated accessors.
    /// </summary>
    [Fact]
    public void EmitAutoProperty_LowersToStorageBackedProperty() {
        CPPClassEmitter emitter = CreateEmitter();
        ConversionClass conversionClass = new ConversionClass {
            Name = "Stats",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Health",
            AccessType = MemberAccessType.Public,
            IsGet = true,
            IsSet = true,
            VarType = new VariableType(typeName: "int")
        });

        (string header, string source) = Emit(emitter, conversionClass);

        Assert.Contains("int32_t Health;", header);
        Assert.Contains("int32_t get_Health();", header);
        Assert.Contains("void set_Health(int32_t value);", header);
        Assert.Contains("int32_t Stats::get_Health()", source);
        Assert.Contains("return this->Health;", source);
        Assert.Contains("void Stats::set_Health(int32_t value)", source);
        Assert.Contains("this->Health = value;", source);
    }

    /// <summary>
    /// Ensures a property with explicit accessor bodies lowers to getter and setter methods instead of a field.
    /// </summary>
    [Fact]
    public void EmitComputedProperty_LowersToAccessorMethods() {
        CPPClassEmitter emitter = CreateEmitter();
        ConversionClass conversionClass = new ConversionClass {
            Name = "Profile",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "DisplayName",
            AccessType = MemberAccessType.Public,
            IsGet = true,
            IsSet = true,
            GetBlock = SyntaxFactory.Block(),
            SetBlock = SyntaxFactory.Block(),
            VarType = new VariableType(typeName: "string")
        });

        (string header, string source) = Emit(emitter, conversionClass);

        Assert.Contains("std::string get_DisplayName();", header);
        Assert.Contains("void set_DisplayName(std::string value);", header);
        Assert.Contains("std::string Profile::get_DisplayName()", source);
        Assert.Contains("void Profile::set_DisplayName(std::string value)", source);
    }

    /// <summary>
    /// Ensures enum emission stays header-only and produces stable member ordering.
    /// </summary>
    [Fact]
    public void EmitEnum_WritesMembersIntoHeader() {
        CPPClassEmitter emitter = CreateEmitter();
        ConversionClass conversionClass = new ConversionClass {
            Name = "RunState",
            DeclarationType = MemberDeclarationType.Enum
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Idle",
            VarType = new VariableType(typeName: "RunState")
        });
        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Running",
            VarType = new VariableType(typeName: "RunState")
        });

        (string header, string source) = Emit(emitter, conversionClass);

        Assert.Contains("enum class RunState", header);
        Assert.Contains("Idle", header);
        Assert.Contains("Running", header);
        Assert.Contains("#include \"RunState.hpp\"", source);
    }

    /// <summary>
    /// Ensures emitted constructors value-initialize instance fields so generated classes preserve C# default field semantics.
    /// </summary>
    [Fact]
    public void EmitConstructor_InitializesInstanceFields() {
        CPPClassEmitter emitter = CreateEmitter();
        ConversionClass conversionClass = new ConversionClass {
            Name = "InputManager",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Highlighted",
            AccessType = MemberAccessType.Private,
            VarType = new VariableType(typeName: "IInteractable2D")
        });
        conversionClass.Variables.Add(new ConversionVariable {
            Name = "hasCapturedInput",
            AccessType = MemberAccessType.Private,
            VarType = new VariableType(typeName: "bool")
        });

        conversionClass.Functions.Add(new ConversionFunction {
            Name = "InputManager",
            IsConstructor = true,
            AccessType = MemberAccessType.Public
        });

        (_, string source) = Emit(emitter, conversionClass);

        Assert.Contains("InputManager::InputManager() : Highlighted(), hasCapturedInput(false)", source);
    }

    /// <summary>
    /// Ensures classes without an explicit constructor still emit a safe parameterless constructor when instance fields need initialization.
    /// </summary>
    [Fact]
    public void EmitClassWithoutConstructors_SynthesizesDefaultFieldInitializingConstructor() {
        CPPClassEmitter emitter = CreateEmitter();
        ConversionClass conversionClass = new ConversionClass {
            Name = "InputManager",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Highlighted",
            AccessType = MemberAccessType.Private,
            VarType = new VariableType(typeName: "IInteractable2D")
        });
        conversionClass.Variables.Add(new ConversionVariable {
            Name = "hasCapturedInput",
            AccessType = MemberAccessType.Private,
            VarType = new VariableType(typeName: "bool")
        });

        (string header, string source) = Emit(emitter, conversionClass);

        Assert.Contains("InputManager();", header);
        Assert.Contains("InputManager::InputManager() : Highlighted(), hasCapturedInput(false)", source);
    }

    /// <summary>
    /// Ensures auto-property initializers are preserved in generated constructors so managed default values survive export.
    /// </summary>
    [Fact]
    public void EmitAutoPropertyInitializer_InitializesBackingFieldInConstructor() {
        CPPClassEmitter emitter = CreateEmitter();
        ConversionClass conversionClass = new ConversionClass {
            Name = "CoreInitializationOptions",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "ContentRootPath",
            AccessType = MemberAccessType.Public,
            IsGet = true,
            IsSet = true,
            VarType = new VariableType(typeName: "string"),
            Assignment = "AppContext.BaseDirectory"
        });
        conversionClass.Variables.Add(new ConversionVariable {
            Name = "UpdateOrderLayers",
            AccessType = MemberAccessType.Public,
            IsGet = true,
            IsSet = true,
            VarType = new VariableType(typeName: "byte"),
            Assignment = "4"
        });

        (_, string source) = Emit(emitter, conversionClass);

        Assert.Contains("CoreInitializationOptions::CoreInitializationOptions()", source);
        Assert.Contains("ContentRootPath(", source);
        Assert.Contains("UpdateOrderLayers(4)", source);
    }

    /// <summary>
    /// Creates the emitter under test with the current C++ processor and runtime program model.
    /// </summary>
    /// <returns>The class emitter to exercise.</returns>
    private static CPPClassEmitter CreateEmitter() {
        CPPConversiorProcessor processor = CppProcessorTestHarness.CreateProcessor();
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        return new CPPClassEmitter(processor, program);
    }

    /// <summary>
    /// Emits one class into in-memory header and source writers.
    /// </summary>
    /// <param name="emitter">Emitter under test.</param>
    /// <param name="conversionClass">The class metadata to emit.</param>
    /// <returns>The generated header and source text.</returns>
    private static (string Header, string Source) Emit(CPPClassEmitter emitter, ConversionClass conversionClass) {
        using StringWriter headerWriter = new StringWriter();
        using StringWriter sourceWriter = new StringWriter();

        emitter.Emit(conversionClass, headerWriter, sourceWriter);

        return (headerWriter.ToString(), sourceWriter.ToString());
    }
}
