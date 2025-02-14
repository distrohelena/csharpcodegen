using cs2.core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Nucleus;
using System.Reflection;

namespace cs2.cpp {
    public class CPPCodeConverter : CodeConverter {
        string assemblyName;
        string version;
        string targetFramework;

        CPPConversiorProcessor conversion;
        CPPProgram tsProgram;
        public CPPConversionRules CPPRules { get; private set; }

        protected override string[] PreProcessorSymbols { get { return ["CPP"]; } }

        public CPPCodeConverter(CPPConversionRules rules)
            : base(rules) {
            this.CPPRules = rules;

            var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

            workspace = MSBuildWorkspace.Create();

            tsProgram = new CPPProgram(rules);
            program = tsProgram;

            context = new ConversionContext(program);

            conversion = new CPPConversiorProcessor(this);

            tsProgram.AddDotNet();

            assemblyName = "";
            version = "";
            targetFramework = "";
        }

        public void WriteOutput(string outputFolder) {
            var replacements = new Dictionary<string, string>() {
                { "ASSEMBLY_NAME", assemblyName },
                { "ASSEMBLY_VERSION", version },
                { "ASSEMBLY_DESCRIPTION", targetFramework }
            };

            string rootPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, ".net.cpp");
            DirectoryUtil.RecursiveCopy(new DirectoryInfo(rootPath), new DirectoryInfo(outputFolder),
                (file, reader, writer) => {
                    string source = reader.ReadToEnd();
                    source = Utils.ReplacePlaceholders(source, replacements);

                    writer.Write(source);
                },
                (file) => {
                    return !file.FullName.EndsWith(".json");
                });


            if (Directory.Exists(outputFolder)) {
                //Directory.Delete(outputFolder, true);
            }

            Directory.CreateDirectory(outputFolder);

            writeClasses(outputFolder);

            //string outputFile = Path.Combine(outputFolder, fileName);
            //string outputDir = Path.GetDirectoryName(outputFile)!;
            //Directory.CreateDirectory(outputDir);

            //Stream stream = File.OpenWrite(outputFile);
            //StreamWriter writer = new StreamWriter(stream);

            //foreach (var pair in program.Requirements) {
            //    if (pair is GenericKnownClass gen) {
            //        string imports = "";

            //        for (int i = gen.Start; i < gen.TotalImports; i++) {
            //            if (i == gen.TotalImports - 1) {
            //                imports += gen.Name + i;
            //            } else if (i == gen.Start) {
            //                imports += gen.Name + ", ";
            //            } else {
            //                imports += gen.Name + i + ", ";
            //            }
            //        }

            //        writer.WriteLine($"import type {{ {imports} }} from \"{pair.Path}\";");
            //    } else {
            //        if (string.IsNullOrEmpty(pair.Replacement)) {
            //            writer.WriteLine($"import {{ {pair.Name} }} from \"{pair.Path}\";");
            //        } else {
            //            writer.WriteLine($"import {{ {pair.Name} as {pair.Replacement} }} from \"{pair.Path}\";");
            //        }
            //    }
            //}

            //writeOutput(writer);
        }

        private void writeConstructors(ConversionClass cl, StreamWriter headerWriter, StreamWriter codeWriter) {

        }

        private void writeFunction(ConversionClass cl, ConversionFunction fn, StreamWriter headerWriter, StreamWriter codeWriter) {
            string staticKeyword = fn.IsStatic ? "static " : "";
            string returnKeyword = fn.ReturnType == null ? "void " : fn.ReturnType.ToCPPString(this.program) + " ";

            headerWriter.Write($"    {staticKeyword}{returnKeyword}{fn.Name}(");
            codeWriter.Write($"{returnKeyword}{cl.Name}::{fn.Name}(");

            for (int i = 0; i < fn.InParameters.Count; i++) {
                ConversionVariable var = fn.InParameters[i];
                string type = var.VarType.ToCPPString(tsProgram);
                headerWriter.Write($"{type} {var.Name}");
                codeWriter.Write($"{type} {var.Name}");

                if (i != fn.InParameters.Count - 1) {
                    headerWriter.Write(", ");
                    codeWriter.Write(", ");
                }
            }

            headerWriter.WriteLine(");");

            codeWriter.WriteLine(")");
            codeWriter.WriteLine("{");

            // 
            fn.WriteLines(conversion, program, cl, codeWriter);

            codeWriter.WriteLine("}");
        }

        private bool writeVariable(ConversionClass cl, ConversionVariable var, StreamWriter headerWriter, StreamWriter codeWriter) {

            return false;
        }

        private void writeClass(ConversionClass cl, StreamWriter headerWriter, StreamWriter codeWriter) {
            headerWriter.WriteLine("#pragma once");

            // include headers
            for (int i = 0; i < cl.ReferencedClasses.Count; i++) {
                string refClass = cl.ReferencedClasses[i];
                VariableType varType = VariableUtil.GetVarType(refClass);

                CPPTypeData typeData;
                VariableType type = conversion.ConvertToCPPType(varType, out typeData);

                if (typeData.IsNativeType) {
                    continue;
                }

                CPPKnownClass known = tsProgram.Requirements.FirstOrDefault(c => c.Name == type.TypeName);

                headerWriter.WriteLine($"#include \"{known.Path}.hpp\"");
            }

            if (cl.ReferencedClasses.Count > 0) {
                headerWriter.WriteLine();
            }


            codeWriter.WriteLine($"#include \"{cl.Name}.h\"");
            codeWriter.WriteLine();

            var extends = CPPUtils.GetInheritance(program, cl);

            if (cl.DeclarationType == MemberDeclarationType.Interface) {
            } else if (cl.DeclarationType == MemberDeclarationType.Abstract) {
            } else if (cl.DeclarationType == MemberDeclarationType.Delegate) {
            } else if (cl.DeclarationType == MemberDeclarationType.Enum) {
            } else {
                // class
                if (string.IsNullOrEmpty(extends)) {
                    headerWriter.WriteLine($"class {cl.Name}");
                    headerWriter.WriteLine("{");
                } else {
                    throw new NotImplementedException();
                }

                SortVariables(cl);

                for (int j = 0; j < cl.Variables.Count; j++) {
                    ConversionVariable var = cl.Variables[j];
                    if (writeVariable(cl, var, headerWriter, codeWriter)) {
                        if (j != cl.Variables.Count - 1) {
                            headerWriter.WriteLine();
                        }
                    }
                }

                if (cl.Variables.Count > 0) {
                    headerWriter.WriteLine();
                }

                SortFunctions(cl);

                MemberAccessType? lastAccessType = null;
                for (int i = 0; i < cl.Functions.Count; i++) {
                    ConversionFunction fn = cl.Functions[i];

                    if (lastAccessType == null ||
                        lastAccessType.Value != fn.AccessType) {
                        lastAccessType = fn.AccessType;

                        headerWriter.WriteLine($"{fn.AccessType.ToString().ToLowerInvariant()}:");
                    }

                    writeFunction(cl, fn, headerWriter, codeWriter);
                }

                headerWriter.WriteLine("};");
            }

        }

        private void writeClasses(string folder) {
            SortProgram();

            for (int i = 0; i < program.Classes.Count; i++) {
                ConversionClass cl = program.Classes[i];
                if (cl.IsNative) {
                    continue;
                }

                string filePath = Path.Combine(folder, cl.Name);

                using (StreamWriter writerHeader = new StreamWriter(filePath + ".h")) {
                    using (StreamWriter writerCode = new StreamWriter(filePath + ".cpp")) {
                        writeClass(cl, writerHeader, writerCode);

                        writerCode.Flush();
                        writerHeader.Flush();
                    }
                }
            }
        }

        protected override void PreProcessExpression(SemanticModel model, MemberDeclarationSyntax member, ConversionContext context) {
            ConversionPreProcessor.PreProcessExpression(model, context, member);
        }

        protected override void ProcessClass(ConversionClass cl, ConversionProgram program) {
            conversion.ProcessClass(cl, program);
        }
    }
}
