using cs2.core;
using System.IO;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that class emission lowers member signatures through the C++ type-conversion path.
/// </summary>
public class CPPClassEmitterTypeConversionTests {
    /// <summary>
    /// Ensures primitive member declarations emit converted C++ type tokens instead of source C# aliases.
    /// </summary>
    [Fact]
    public void Emit_WithBooleanMembers_UsesConvertedCppTypes() {
        CPPClassEmitter emitter = new CPPClassEmitter(new CPPConversiorProcessor(null), new CPPProgram(new CPPConversionRules()));
        ConversionClass conversionClass = new ConversionClass {
            Name = "BooleanCarrier",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Enabled",
            AccessType = MemberAccessType.Public,
            VarType = new VariableType(VariableDataType.Boolean, "Boolean")
        });

        conversionClass.Functions.Add(new ConversionFunction {
            Name = "SetEnabled",
            AccessType = MemberAccessType.Public,
            ReturnType = new VariableType(VariableDataType.Boolean, "Boolean"),
            InParameters = {
                new ConversionVariable {
                    Name = "enabled",
                    VarType = new VariableType(VariableDataType.Boolean, "Boolean")
                }
            }
        });

        using StringWriter headerWriter = new StringWriter();
        using StringWriter sourceWriter = new StringWriter();

        emitter.Emit(conversionClass, headerWriter, sourceWriter);

        string header = headerWriter.ToString();
        string source = sourceWriter.ToString();

        Assert.Contains("bool Enabled;", header);
        Assert.Contains("bool SetEnabled(bool enabled);", header);
        Assert.Contains("bool BooleanCarrier::SetEnabled(bool enabled)", source);
        Assert.DoesNotContain("Boolean Enabled", header);
    }
}
