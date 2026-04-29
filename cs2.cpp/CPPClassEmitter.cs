using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Emits C++ header and source declarations for a converted class using the current backend rules.
    /// </summary>
    public class CPPClassEmitter {
        readonly CPPConversiorProcessor processor;
        readonly CPPProgram program;

        /// <summary>
        /// Initializes a class emitter bound to the current processor and program state.
        /// </summary>
        /// <param name="processor">Processor used to lower method and accessor bodies.</param>
        /// <param name="program">Program model that resolves known C++ runtime types.</param>
        public CPPClassEmitter(CPPConversiorProcessor processor, CPPProgram program) {
            this.processor = processor;
            this.program = program;
        }

        /// <summary>
        /// Emits the full header and source representation for a converted type.
        /// </summary>
        /// <param name="conversionClass">The class, interface, or enum to emit.</param>
        /// <param name="headerWriter">Writer that receives the header output.</param>
        /// <param name="sourceWriter">Writer that receives the source output.</param>
        public void Emit(ConversionClass conversionClass, TextWriter headerWriter, TextWriter sourceWriter) {
            if (conversionClass == null) {
                throw new ArgumentNullException(nameof(conversionClass));
            }

            if (headerWriter == null) {
                throw new ArgumentNullException(nameof(headerWriter));
            }

            if (sourceWriter == null) {
                throw new ArgumentNullException(nameof(sourceWriter));
            }

            WriteHeaderPreamble(conversionClass, headerWriter);
            WriteSourcePreamble(conversionClass, sourceWriter);

            if (conversionClass.DeclarationType == MemberDeclarationType.Enum) {
                WriteEnum(conversionClass, headerWriter);
                return;
            }

            WriteClass(conversionClass, headerWriter, sourceWriter);
        }

        /// <summary>
        /// Writes the header preamble and include directives required by a converted type.
        /// </summary>
        /// <param name="conversionClass">The type being emitted.</param>
        /// <param name="headerWriter">Writer that receives the header preamble.</param>
        void WriteHeaderPreamble(ConversionClass conversionClass, TextWriter headerWriter) {
            headerWriter.WriteLine("#pragma once");

            bool wroteInclude = false;
            foreach (string referencedClass in conversionClass.ReferencedClasses.Distinct(StringComparer.Ordinal)) {
                if (string.Equals(referencedClass, conversionClass.Name, StringComparison.Ordinal)) {
                    continue;
                }

                string includePath = ResolveIncludePath(referencedClass);
                if (string.IsNullOrWhiteSpace(includePath)) {
                    continue;
                }

                headerWriter.WriteLine($"#include \"{includePath}.hpp\"");
                wroteInclude = true;
            }

            if (wroteInclude) {
                headerWriter.WriteLine();
            }
        }

        /// <summary>
        /// Writes the source preamble that binds a converted implementation file to its header.
        /// </summary>
        /// <param name="conversionClass">The type being emitted.</param>
        /// <param name="sourceWriter">Writer that receives the source preamble.</param>
        void WriteSourcePreamble(ConversionClass conversionClass, TextWriter sourceWriter) {
            sourceWriter.WriteLine($"#include \"{conversionClass.Name}.hpp\"");
            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Resolves the include path for a referenced class using known runtime metadata when available.
        /// </summary>
        /// <param name="referencedClass">The referenced class name as discovered during conversion.</param>
        /// <returns>The include path without extension.</returns>
        string ResolveIncludePath(string referencedClass) {
            VariableType variableType = VariableUtil.GetVarType(referencedClass);
            CPPTypeData typeData = new CPPTypeData();

            if (processor != null) {
                variableType = processor.ConvertToCPPType(variableType, out typeData);
            }

            if (typeData.IsNativeType) {
                return string.Empty;
            }

            CPPKnownClass knownClass = program.Requirements.FirstOrDefault(requirement => requirement.Name == variableType.TypeName);
            if (knownClass != null && !string.IsNullOrWhiteSpace(knownClass.Path)) {
                return knownClass.Path;
            }

            return variableType.TypeName;
        }

        /// <summary>
        /// Writes an enum declaration into the header file.
        /// </summary>
        /// <param name="conversionClass">The enum type to emit.</param>
        /// <param name="headerWriter">Writer that receives the enum declaration.</param>
        void WriteEnum(ConversionClass conversionClass, TextWriter headerWriter) {
            headerWriter.WriteLine($"enum class {conversionClass.Name}");
            headerWriter.WriteLine("{");

            List<string> members = GetEnumMembers(conversionClass);
            for (int index = 0; index < members.Count; index++) {
                string suffix = index == members.Count - 1 ? string.Empty : ",";
                headerWriter.WriteLine($"    {members[index]}{suffix}");
            }

            headerWriter.WriteLine("};");
        }

        /// <summary>
        /// Extracts the emitted member names for an enum declaration.
        /// </summary>
        /// <param name="conversionClass">The enum conversion model.</param>
        /// <returns>The ordered enum member names.</returns>
        List<string> GetEnumMembers(ConversionClass conversionClass) {
            List<string> members = new List<string>();

            foreach (ConversionVariable variable in conversionClass.Variables) {
                if (!string.IsNullOrWhiteSpace(variable.Name)) {
                    members.Add(variable.Name);
                }
            }

            if (members.Count > 0) {
                return members;
            }

            if (conversionClass.EnumMembers == null) {
                return members;
            }

            foreach (object enumMember in conversionClass.EnumMembers) {
                if (enumMember == null) {
                    continue;
                }

                string memberName = enumMember.ToString();
                if (!string.IsNullOrWhiteSpace(memberName)) {
                    members.Add(memberName);
                }
            }

            return members;
        }

        /// <summary>
        /// Writes a class-like declaration and its out-of-line method definitions.
        /// </summary>
        /// <param name="conversionClass">The type to emit.</param>
        /// <param name="headerWriter">Writer that receives the header declaration.</param>
        /// <param name="sourceWriter">Writer that receives the source definitions.</param>
        void WriteClass(ConversionClass conversionClass, TextWriter headerWriter, TextWriter sourceWriter) {
            string inheritance = CPPUtils.GetInheritance(program, conversionClass);
            if (string.IsNullOrWhiteSpace(inheritance)) {
                headerWriter.WriteLine($"class {conversionClass.Name}");
            } else {
                headerWriter.WriteLine($"class {conversionClass.Name} : {inheritance}");
            }

            headerWriter.WriteLine("{");

            bool wroteAnySection = false;
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Public, headerWriter, sourceWriter);
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Protected, headerWriter, sourceWriter);
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Private, headerWriter, sourceWriter);

            if (!wroteAnySection) {
                headerWriter.WriteLine("public:");
            }

            headerWriter.WriteLine("};");
        }

        /// <summary>
        /// Emits one access section, including lowered properties, fields, and methods.
        /// </summary>
        /// <param name="conversionClass">The class that owns the emitted members.</param>
        /// <param name="accessType">The access group to emit.</param>
        /// <param name="headerWriter">Writer that receives declarations.</param>
        /// <param name="sourceWriter">Writer that receives definitions.</param>
        /// <returns><c>true</c> when at least one member was emitted for the section.</returns>
        bool WriteAccessSection(ConversionClass conversionClass, MemberAccessType accessType, TextWriter headerWriter, TextWriter sourceWriter) {
            List<Action> writers = new List<Action>();

            foreach (ConversionVariable variable in conversionClass.Variables.Where(variable => variable.AccessType == accessType)) {
                if (IsComputedProperty(variable)) {
                    writers.Add(() => WriteComputedProperty(conversionClass, variable, headerWriter, sourceWriter));
                    continue;
                }

                writers.Add(() => WriteField(variable, headerWriter));
            }

            foreach (ConversionFunction function in conversionClass.Functions.Where(function => function.AccessType == accessType)) {
                writers.Add(() => WriteFunction(conversionClass, function, headerWriter, sourceWriter));
            }

            if (writers.Count == 0) {
                return false;
            }

            headerWriter.WriteLine($"{accessType.ToString().ToLowerInvariant()}:");

            for (int index = 0; index < writers.Count; index++) {
                writers[index]();
                if (index != writers.Count - 1) {
                    headerWriter.WriteLine();
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether a variable represents a property that needs accessor methods in C++ output.
        /// </summary>
        /// <param name="variable">The variable to inspect.</param>
        /// <returns><c>true</c> when the variable should lower into getter and setter methods.</returns>
        bool IsComputedProperty(ConversionVariable variable) {
            if (!variable.IsGet && !variable.IsSet) {
                return false;
            }

            return variable.GetBlock != null ||
                   variable.SetBlock != null ||
                   variable.ArrowExpression != null;
        }

        /// <summary>
        /// Writes a field declaration for a direct field or auto-property lowering result.
        /// </summary>
        /// <param name="variable">The variable to emit.</param>
        /// <param name="headerWriter">Writer that receives the field declaration.</param>
        void WriteField(ConversionVariable variable, TextWriter headerWriter) {
            string staticKeyword = variable.IsStatic ? "static " : string.Empty;
            string typeName = variable.VarType.ToCPPString(program);
            headerWriter.WriteLine($"    {staticKeyword}{typeName} {variable.Name};");
        }

        /// <summary>
        /// Lowers a computed property into explicit accessor methods in both the header and source files.
        /// </summary>
        /// <param name="conversionClass">The class that owns the property.</param>
        /// <param name="variable">The property model to lower.</param>
        /// <param name="headerWriter">Writer that receives accessor declarations.</param>
        /// <param name="sourceWriter">Writer that receives accessor definitions.</param>
        void WriteComputedProperty(ConversionClass conversionClass, ConversionVariable variable, TextWriter headerWriter, TextWriter sourceWriter) {
            if (variable.IsGet) {
                ConversionFunction getter = CreateGetter(variable);
                WriteFunction(conversionClass, getter, headerWriter, sourceWriter);
            }

            if (variable.IsGet && variable.IsSet) {
                headerWriter.WriteLine();
            }

            if (variable.IsSet) {
                ConversionFunction setter = CreateSetter(variable);
                WriteFunction(conversionClass, setter, headerWriter, sourceWriter);
            }
        }

        /// <summary>
        /// Creates a getter function model from a property definition.
        /// </summary>
        /// <param name="variable">The source property model.</param>
        /// <returns>A function model suitable for normal function emission.</returns>
        ConversionFunction CreateGetter(ConversionVariable variable) {
            return new ConversionFunction {
                Name = $"get_{variable.Name}",
                AccessType = variable.AccessType,
                ReturnType = new VariableType(variable.VarType),
                RawBlock = variable.GetBlock
            };
        }

        /// <summary>
        /// Creates a setter function model from a property definition.
        /// </summary>
        /// <param name="variable">The source property model.</param>
        /// <returns>A function model suitable for normal function emission.</returns>
        ConversionFunction CreateSetter(ConversionVariable variable) {
            ConversionFunction setter = new ConversionFunction {
                Name = $"set_{variable.Name}",
                AccessType = variable.AccessType,
                RawBlock = variable.SetBlock
            };

            setter.InParameters.Add(new ConversionVariable {
                Name = "value",
                VarType = new VariableType(variable.VarType)
            });

            return setter;
        }

        /// <summary>
        /// Writes a normal function declaration and definition pair.
        /// </summary>
        /// <param name="conversionClass">The class that owns the function.</param>
        /// <param name="function">The function to emit.</param>
        /// <param name="headerWriter">Writer that receives the declaration.</param>
        /// <param name="sourceWriter">Writer that receives the definition.</param>
        void WriteFunction(ConversionClass conversionClass, ConversionFunction function, TextWriter headerWriter, TextWriter sourceWriter) {
            WriteFunctionDeclaration(conversionClass, function, headerWriter);
            WriteFunctionDefinition(conversionClass, function, sourceWriter);
        }

        /// <summary>
        /// Writes a function declaration into the class header.
        /// </summary>
        /// <param name="conversionClass">The class that owns the function.</param>
        /// <param name="function">The function to declare.</param>
        /// <param name="headerWriter">Writer that receives the declaration.</param>
        void WriteFunctionDeclaration(ConversionClass conversionClass, ConversionFunction function, TextWriter headerWriter) {
            headerWriter.Write("    ");

            if (function.IsStatic) {
                headerWriter.Write("static ");
            }

            if (!function.IsConstructor) {
                headerWriter.Write($"{GetReturnType(function)} ");
            }

            headerWriter.Write($"{GetFunctionName(conversionClass, function)}(");
            WriteParameters(function, headerWriter);
            headerWriter.WriteLine(");");
        }

        /// <summary>
        /// Writes a function definition into the C++ source file.
        /// </summary>
        /// <param name="conversionClass">The class that owns the function.</param>
        /// <param name="function">The function to define.</param>
        /// <param name="sourceWriter">Writer that receives the definition.</param>
        void WriteFunctionDefinition(ConversionClass conversionClass, ConversionFunction function, TextWriter sourceWriter) {
            if (!function.IsConstructor) {
                sourceWriter.Write($"{GetReturnType(function)} ");
            }

            sourceWriter.Write($"{conversionClass.Name}::{GetFunctionName(conversionClass, function)}(");
            WriteParameters(function, sourceWriter);
            sourceWriter.WriteLine(")");
            sourceWriter.WriteLine("{");

            if (function.HasBody) {
                function.WriteLines(processor, program, conversionClass, sourceWriter);
            }

            sourceWriter.WriteLine("}");
            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Writes the function parameter list to the provided writer.
        /// </summary>
        /// <param name="function">The function whose parameters will be written.</param>
        /// <param name="writer">Writer that receives the parameter list.</param>
        void WriteParameters(ConversionFunction function, TextWriter writer) {
            for (int index = 0; index < function.InParameters.Count; index++) {
                ConversionVariable parameter = function.InParameters[index];
                writer.Write($"{parameter.VarType.ToCPPString(program)} {parameter.Name}");

                if (index != function.InParameters.Count - 1) {
                    writer.Write(", ");
                }
            }
        }

        /// <summary>
        /// Resolves the emitted function name, substituting the owning class name for constructors.
        /// </summary>
        /// <param name="conversionClass">The class that owns the function.</param>
        /// <param name="function">The function being emitted.</param>
        /// <returns>The emitted function name.</returns>
        string GetFunctionName(ConversionClass conversionClass, ConversionFunction function) {
            if (function.IsConstructor) {
                return conversionClass.Name;
            }

            return function.Name;
        }

        /// <summary>
        /// Resolves the emitted return type for a function.
        /// </summary>
        /// <param name="function">The function being emitted.</param>
        /// <returns>The emitted C++ return type token.</returns>
        string GetReturnType(ConversionFunction function) {
            if (function.ReturnType == null) {
                return "void";
            }

            return function.ReturnType.ToCPPString(program);
        }
    }
}
