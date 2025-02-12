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

        protected override string[] PreProcessorSymbols { get { return ["CPP"]; } }

        public CPPCodeConverter(ConversionRules rules)
            : base(rules) {
            this.rules = rules;

            var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

            workspace = MSBuildWorkspace.Create();

            tsProgram = new CPPProgram(rules);
            program = tsProgram;

            //tsProgram.AddDotNet(env);
            context = new ConversionContext(program);

            conversion = new CPPConversiorProcessor();

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

        private void writeConstructors(ConvertedClass cl, StreamWriter headerWriter, StreamWriter codeWriter) {

        }

        private void writeFunction(ConvertedClass cl, ConvertedFunction fn, StreamWriter headerWriter, StreamWriter codeWriter) {
            string staticKeyword = fn.IsStatic ? "static " : "";
            string returnKeyword = fn.ReturnType == null ? "void " : fn.ReturnType.ToCPPString(this.program) + " ";

            headerWriter.Write($"    {staticKeyword}{returnKeyword}{fn.Name}(");
            codeWriter.Write($"{returnKeyword}{cl.Name}::{fn.Name}(");

            for (int i = 0; i < fn.InParameters.Count; i++) {
                ConvertedVariable var = fn.InParameters[i];
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

        private bool writeVariable(ConvertedClass cl, ConvertedVariable var, StreamWriter headerWriter, StreamWriter codeWriter) {

            return false;
        }

        private void writeClass(ConvertedClass cl, StreamWriter headerWriter, StreamWriter codeWriter) {
            headerWriter.WriteLine("#pragma once");
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
                    ConvertedVariable var = cl.Variables[j];
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
                    ConvertedFunction fn = cl.Functions[i];

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
                ConvertedClass cl = program.Classes[i];
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
            ConversionPreProcessor.PreProcessExpression(model, member, context);
        }

        protected override void ProcessClass(ConvertedClass cl, ConvertedProgram program) {
            conversion.ProcessClass(cl, program);
        }
    }
}
