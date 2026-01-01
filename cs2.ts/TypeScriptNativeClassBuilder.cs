using cs2.core;
using cs2.core.symbols;
using System;
using System.Collections.Generic;

namespace cs2.ts {
    /// <summary>
    /// Builds native conversion classes from runtime symbol requirements.
    /// </summary>
    public class TypeScriptNativeClassBuilder {
        /// <summary>
        /// Adds native classes for the provided requirements to the program.
        /// </summary>
        /// <param name="program">The program receiving native classes.</param>
        /// <param name="requirements">The runtime requirements to materialize.</param>
        public void BuildNativeClasses(TypeScriptProgram program, IReadOnlyList<TypeScriptKnownClass> requirements) {
            if (program == null) {
                throw new ArgumentNullException(nameof(program));
            }
            if (requirements == null) {
                return;
            }

            for (int i = 0; i < requirements.Count; i++) {
                TypeScriptKnownClass requirement = requirements[i];

                ConversionClass cl = new ConversionClass();
                cl.IsNative = true;
                cl.Name = requirement.Name;
                program.RegisterClass(cl);

                for (int j = 0; j < requirement.Symbols.Count; j++) {
                    Symbol symbol = requirement.Symbols[j];

                    if (symbol.Members == null) {
                        continue;
                    }

                    if (symbol.Type == "interface") {
                        cl.DeclarationType = MemberDeclarationType.Interface;
                    } else if (symbol.Type == "enum") {
                        cl.DeclarationType = MemberDeclarationType.Enum;

                        for (int k = 0; k < symbol.Members.Count; k++) {
                            ClassMember member = symbol.Members[k];
                            ConversionVariable var = new ConversionVariable();
                            var.Name = member.Name;
                            var.VarType = VariableUtil.GetVarType(cl.Name);
                            cl.Variables.Add(var);
                        }

                        continue;
                    } else {
                        cl.DeclarationType = MemberDeclarationType.Class;
                    }

                    for (int k = 0; k < symbol.Members.Count; k++) {
                        ClassMember member = symbol.Members[k];
                        if (member.Type == "variable" || member.Type == "property" || member.Type == "getter" || member.Type == "setter") {
                            ConversionVariable var = new ConversionVariable();
                            var.Name = member.Name;
                            if (string.IsNullOrEmpty(member.PropertyType)) {
                                var.VarType = VariableUtil.GetVarType(member.ReturnType);
                            } else {
                                var.VarType = VariableUtil.GetVarType(member.PropertyType);
                            }
                            cl.Variables.Add(var);
                        } else if (member.Type == "method") {
                            ConversionFunction fn = new ConversionFunction();
                            fn.Name = member.Name;
                            fn.ReturnType = VariableUtil.GetVarType(member.ReturnType);
                            cl.Functions.Add(fn);

                            for (int l = 0; l < member.Parameters.Count; l++) {
                                var parameter = member.Parameters[l];
                                ConversionVariable var = new ConversionVariable();
                                var.Name = parameter.Name;
                                if (!string.IsNullOrEmpty(parameter.Type)) {
                                    var.VarType = VariableUtil.GetVarType(parameter.Type);
                                }
                                fn.InParameters.Add(var);
                            }
                        }
                    }
                }
            }
        }
    }
}
