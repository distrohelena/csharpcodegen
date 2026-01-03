using cs2.core;
using cs2.go.util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace cs2.go {
    /// <summary>
    /// Emits Go structs, interfaces, enums, and delegate aliases from conversion metadata.
    /// </summary>
    public class GoClassEmitter {
        /// <summary>
        /// Holds the conversion processor used for syntax emission.
        /// </summary>
        readonly GoConversiorProcessor Conversion;

        /// <summary>
        /// Holds the conversion program metadata.
        /// </summary>
        readonly ConversionProgram Program;

        /// <summary>
        /// Holds the Go program metadata.
        /// </summary>
        readonly GoProgram GoProgram;

        /// <summary>
        /// Tracks Go import requirements.
        /// </summary>
        readonly GoImportTracker ImportTracker;

        /// <summary>
        /// Holds the conversion options for emission.
        /// </summary>
        readonly GoConversionOptions Options;

        /// <summary>
        /// Holds the variable sorter delegate, if provided.
        /// </summary>
        readonly Action<ConversionClass> SortVariablesAction;

        /// <summary>
        /// Initializes a new class emitter with the required dependencies.
        /// </summary>
        /// <param name="conversion">The conversion processor used to render syntax.</param>
        /// <param name="program">The conversion program.</param>
        /// <param name="goProgram">The Go program metadata.</param>
        /// <param name="importTracker">The import tracker.</param>
        /// <param name="options">The conversion options.</param>
        /// <param name="sortVariablesAction">Optional variable sorter.</param>
        public GoClassEmitter(
            GoConversiorProcessor conversion,
            ConversionProgram program,
            GoProgram goProgram,
            GoImportTracker importTracker,
            GoConversionOptions options,
            Action<ConversionClass> sortVariablesAction) {
            Conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
            Program = program ?? throw new ArgumentNullException(nameof(program));
            GoProgram = goProgram ?? throw new ArgumentNullException(nameof(goProgram));
            ImportTracker = importTracker ?? throw new ArgumentNullException(nameof(importTracker));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            SortVariablesAction = sortVariablesAction;
        }

        /// <summary>
        /// Emits the declaration for the provided conversion class.
        /// </summary>
        /// <param name="cl">The class to emit.</param>
        /// <param name="writer">The output writer.</param>
        public void EmitClass(ConversionClass cl, GoOutputWriter writer) {
            if (cl == null || writer == null) {
                return;
            }

            if (cl.IsNative) {
                return;
            }

            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                EmitInterface(cl, writer);
                return;
            }

            if (cl.DeclarationType == MemberDeclarationType.Delegate) {
                EmitDelegate(cl, writer);
                return;
            }

            if (cl.DeclarationType == MemberDeclarationType.Enum) {
                EmitEnum(cl, writer);
                return;
            }

            EmitStruct(cl, writer);
        }

        /// <summary>
        /// Emits a Go interface declaration.
        /// </summary>
        /// <param name="cl">The interface class.</param>
        /// <param name="writer">The output writer.</param>
        void EmitInterface(ConversionClass cl, GoOutputWriter writer) {
            string generic = BuildGenericParameters(cl.GenericArgs);
            writer.WriteLine($"type {cl.Name}{generic} interface {{");
            writer.Indent();

            for (int i = 0; i < cl.Functions.Count; i++) {
                ConversionFunction fn = cl.Functions[i];
                if (fn.IsConstructor) {
                    continue;
                }

                string signature = BuildFunctionSignature(cl, fn, false);
                writer.WriteIndentedLine(signature);
            }

            writer.Outdent();
            writer.WriteLine("}");
            writer.WriteLine();
        }

        /// <summary>
        /// Emits a Go struct declaration with instance fields.
        /// </summary>
        /// <param name="cl">The struct class.</param>
        /// <param name="writer">The output writer.</param>
        void EmitStruct(ConversionClass cl, GoOutputWriter writer) {
            string generic = BuildGenericParameters(cl.GenericArgs);
            writer.WriteLine($"type {cl.Name}{generic} struct {{");
            writer.Indent();

            if (SortVariablesAction != null) {
                SortVariablesAction(cl);
            }

            EmitEmbeddedBase(cl, writer);
            EmitFields(cl, writer);

            writer.Outdent();
            writer.WriteLine("}");
            writer.WriteLine();

            EmitStaticMembers(cl, writer);
            EmitConstructors(cl, writer);
            EmitMethods(cl, writer);
        }

        /// <summary>
        /// Emits embedded base structs for inheritance emulation.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="writer">The output writer.</param>
        void EmitEmbeddedBase(ConversionClass cl, GoOutputWriter writer) {
            GoUtils.GetInheritance(GoProgram, cl, out string baseType, out _);
            if (string.IsNullOrWhiteSpace(baseType)) {
                return;
            }

            writer.WriteIndentedLine(baseType);
        }

        /// <summary>
        /// Emits instance fields for the struct.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="writer">The output writer.</param>
        void EmitFields(ConversionClass cl, GoOutputWriter writer) {
            for (int i = 0; i < cl.Variables.Count; i++) {
                ConversionVariable var = cl.Variables[i];
                if (var.IsStatic) {
                    continue;
                }

                string type = var.VarType.ToGoString(GoProgram, ImportTracker);
                string name = GetVariableName(var);
                writer.WriteIndentedLine($"{name} {type}");
            }
        }

        /// <summary>
        /// Emits static members as package-level variables.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="writer">The output writer.</param>
        void EmitStaticMembers(ConversionClass cl, GoOutputWriter writer) {
            List<ConversionVariable> staticVars = cl.Variables.Where(v => v.IsStatic).ToList();
            if (staticVars.Count == 0) {
                return;
            }

            for (int i = 0; i < staticVars.Count; i++) {
                ConversionVariable var = staticVars[i];
                string type = var.VarType.ToGoString(GoProgram, ImportTracker);
                string name = BuildStaticMemberName(cl.Name, GetVariableName(var));
                writer.WriteLine($"var {name} {type}");
            }

            writer.WriteLine();
        }

        /// <summary>
        /// Emits constructor functions for the struct.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="writer">The output writer.</param>
        void EmitConstructors(ConversionClass cl, GoOutputWriter writer) {
            List<ConversionFunction> constructors = cl.Functions.Where(c => c.IsConstructor && !c.IsStatic).ToList();
            if (constructors.Count == 0) {
                return;
            }

            for (int i = 0; i < constructors.Count; i++) {
                ConversionFunction fn = constructors[i];
                string name = BuildConstructorName(cl.Name, constructors.Count, i);
                string signature = BuildFunctionSignature(cl, fn, true, name);

                writer.WriteLine($"func {signature} {{");
                writer.WriteLine($"\tself := &{cl.Name}{{}}");

                List<string> lines = fn.WriteLines(Conversion, GoProgram, cl);
                GoFunction.PrintLines(writer, lines);

                writer.WriteLine("\treturn self");
                writer.WriteLine("}");
                writer.WriteLine();
            }
        }

        /// <summary>
        /// Emits instance and static methods for the struct.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="writer">The output writer.</param>
        void EmitMethods(ConversionClass cl, GoOutputWriter writer) {
            List<ConversionFunction> functions = cl.Functions.Where(c => !c.IsConstructor).ToList();
            for (int i = 0; i < functions.Count; i++) {
                ConversionFunction fn = functions[i];
                if (fn.DeclarationType == MemberDeclarationType.Interface) {
                    continue;
                }

                string signature = BuildFunctionSignature(cl, fn, false);
                writer.WriteLine($"func {signature} {{");

                List<string> lines = fn.WriteLines(Conversion, GoProgram, cl);
                GoFunction.PrintLines(writer, lines);

                writer.WriteLine("}");
                writer.WriteLine();
            }
        }

        /// <summary>
        /// Emits delegate declarations as Go function types.
        /// </summary>
        /// <param name="cl">The delegate class.</param>
        /// <param name="writer">The output writer.</param>
        void EmitDelegate(ConversionClass cl, GoOutputWriter writer) {
            ConversionFunction del = cl.Functions[0];
            string signature = BuildDelegateSignature(cl, del);
            writer.WriteLine($"type {del.Remap} {signature}");
            writer.WriteLine();
        }

        /// <summary>
        /// Emits enum declarations using typed const blocks.
        /// </summary>
        /// <param name="cl">The enum class.</param>
        /// <param name="writer">The output writer.</param>
        void EmitEnum(ConversionClass cl, GoOutputWriter writer) {
            writer.WriteLine($"type {cl.Name} int");
            writer.WriteLine("const (");
            writer.Indent();

            if (cl.EnumMembers != null) {
                for (int j = 0; j < cl.EnumMembers.Count; j++) {
                    string member = cl.EnumMembers[j].ToString();
                    string name = member;
                    string value = string.Empty;

                    int equalsIndex = member.IndexOf('=');
                    if (equalsIndex > 0) {
                        name = member.Substring(0, equalsIndex).Trim();
                        value = member.Substring(equalsIndex + 1).Trim();
                    }

                    string fullName = $"{cl.Name}_{name}";
                    if (j == 0 && string.IsNullOrEmpty(value)) {
                        writer.WriteIndentedLine($"{fullName} {cl.Name} = iota");
                    } else if (!string.IsNullOrEmpty(value)) {
                        writer.WriteIndentedLine($"{fullName} {cl.Name} = {value}");
                    } else {
                        writer.WriteIndentedLine($"{fullName}");
                    }
                }
            }

            writer.Outdent();
            writer.WriteLine(")");
            writer.WriteLine();
        }

        /// <summary>
        /// Builds a function signature for Go output.
        /// </summary>
        /// <param name="cl">The owning class.</param>
        /// <param name="fn">The function to emit.</param>
        /// <param name="isConstructor">Whether the signature is for a constructor function.</param>
        /// <param name="overrideName">Optional override name for the function.</param>
        /// <returns>The Go signature string.</returns>
        string BuildFunctionSignature(ConversionClass cl, ConversionFunction fn, bool isConstructor, string overrideName = "") {
            string name = string.IsNullOrWhiteSpace(overrideName)
                ? GetFunctionName(fn)
                : overrideName;

            string receiver = string.Empty;
            if (!isConstructor && !fn.IsStatic && cl.DeclarationType != MemberDeclarationType.Interface) {
                receiver = $"(self *{cl.Name}) ";
            }

            if (fn.IsStatic && !isConstructor) {
                name = BuildStaticMemberName(cl.Name, name);
            }

            string parameters = BuildParameters(fn.InParameters);
            string returnType = BuildReturnType(fn.ReturnType, fn.IsAsync);
            string generic = BuildGenericParameters(fn.GenericParameters);

            if (!string.IsNullOrEmpty(generic)) {
                name = $"{name}{generic}";
            }

            if (string.IsNullOrEmpty(returnType)) {
                return $"{receiver}{name}({parameters})";
            }

            return $"{receiver}{name}({parameters}) {returnType}";
        }

        /// <summary>
        /// Builds a delegate signature for Go output.
        /// </summary>
        /// <param name="cl">The owning class.</param>
        /// <param name="fn">The delegate function metadata.</param>
        /// <returns>The delegate signature string.</returns>
        string BuildDelegateSignature(ConversionClass cl, ConversionFunction fn) {
            string parameters = BuildParameters(fn.InParameters);
            string returnType = BuildReturnType(fn.ReturnType, fn.IsAsync);
            if (string.IsNullOrEmpty(returnType)) {
                return $"func({parameters})";
            }

            return $"func({parameters}) {returnType}";
        }

        /// <summary>
        /// Builds a parameter list for Go output.
        /// </summary>
        /// <param name="parameters">The parameters to emit.</param>
        /// <returns>The parameter list string.</returns>
        string BuildParameters(List<ConversionVariable> parameters) {
            if (parameters == null || parameters.Count == 0) {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < parameters.Count; i++) {
                ConversionVariable param = parameters[i];
                string type = param.VarType.ToGoString(GoProgram, ImportTracker);
                if (param.Modifier.HasFlag(ParameterModifier.Out) || param.Modifier.HasFlag(ParameterModifier.Ref)) {
                    type = $"*{type}";
                }
                string name = string.IsNullOrWhiteSpace(param.Name) ? "param" + i : param.Name;
                parts.Add($"{name} {type}");
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Builds a return type string for Go output.
        /// </summary>
        /// <param name="returnType">The return type metadata.</param>
        /// <param name="isAsync">Whether the function is async.</param>
        /// <returns>The Go return type string.</returns>
        string BuildReturnType(VariableType returnType, bool isAsync) {
            if (returnType == null) {
                return string.Empty;
            }

            return returnType.ToGoString(GoProgram, ImportTracker);
        }

        /// <summary>
        /// Builds a generic parameter list for Go output.
        /// </summary>
        /// <param name="parameters">The generic parameter names.</param>
        /// <returns>The Go generic parameter list.</returns>
        string BuildGenericParameters(List<string> parameters) {
            if (parameters == null || parameters.Count == 0) {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < parameters.Count; i++) {
                string param = parameters[i];
                if (string.IsNullOrWhiteSpace(param)) {
                    continue;
                }

                parts.Add($"{param} any");
            }

            if (parts.Count == 0) {
                return string.Empty;
            }

            return $"[{string.Join(", ", parts)}]";
        }

        /// <summary>
        /// Builds a generic parameter list for Go output from class parameters.
        /// </summary>
        /// <param name="parameters">The generic parameter names.</param>
        /// <returns>The Go generic parameter list.</returns>
        string BuildGenericParameters(IReadOnlyList<string> parameters) {
            if (parameters == null || parameters.Count == 0) {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < parameters.Count; i++) {
                string param = parameters[i];
                if (string.IsNullOrWhiteSpace(param)) {
                    continue;
                }

                parts.Add($"{param} any");
            }

            if (parts.Count == 0) {
                return string.Empty;
            }

            return $"[{string.Join(", ", parts)}]";
        }

        /// <summary>
        /// Builds a constructor name for the given class.
        /// </summary>
        /// <param name="className">The class name.</param>
        /// <param name="totalConstructors">Total constructor overloads.</param>
        /// <param name="index">The constructor index.</param>
        /// <returns>The constructor function name.</returns>
        string BuildConstructorName(string className, int totalConstructors, int index) {
            if (totalConstructors <= 1) {
                return $"New{className}";
            }

            return $"New{className}{index + 1}";
        }

        /// <summary>
        /// Builds a static member name for Go output.
        /// </summary>
        /// <param name="className">The owning class name.</param>
        /// <param name="memberName">The member name.</param>
        /// <returns>The Go static member name.</returns>
        string BuildStaticMemberName(string className, string memberName) {
            return $"{className}_{memberName}";
        }

        /// <summary>
        /// Gets the emitted function name for the conversion function.
        /// </summary>
        /// <param name="fn">The conversion function.</param>
        /// <returns>The emitted function name.</returns>
        static string GetFunctionName(ConversionFunction fn) {
            if (!string.IsNullOrWhiteSpace(fn.Remap)) {
                return fn.Remap;
            }

            return fn.Name;
        }

        /// <summary>
        /// Gets the emitted variable name for the conversion variable.
        /// </summary>
        /// <param name="var">The conversion variable.</param>
        /// <returns>The emitted variable name.</returns>
        static string GetVariableName(ConversionVariable var) {
            if (!string.IsNullOrWhiteSpace(var.Remap)) {
                return var.Remap;
            }

            return var.Name;
        }
    }
}
