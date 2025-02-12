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

            //writer.WriteLine();

            //writeOutput(writer);

            //writer.WriteLine();

            //writer.Flush();
            //stream.Dispose();
        }

        private void writeConstructors(ConvertedClass cl, StreamWriter writer) {
          
        }

        private bool writeVariable(ConvertedClass cl, ConvertedVariable var, StreamWriter writer) {

            return false;
        }

        private void writeClass(ConvertedClass cl, StreamWriter writer) {
            
        }

        private void writeOutput(StreamWriter writer) {
            SortProgram();

            for (int i = 0; i < program.Classes.Count; i++) {
                writeClass(program.Classes[i], writer);
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
