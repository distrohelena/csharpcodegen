using cs2.core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Nucleus;
using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;

namespace cs2.ts {
    public class TypeScriptCodeConverter : CodeConverter {
        string assemblyName;
        string version;
        string targetFramework;

        TypeScriptConversiorProcessor conversion;
        TypeScriptProgram tsProgram;

        protected override string[] PreProcessorSymbols { get { return ["TYPESCRIPT"]; } }

        public TypeScriptCodeConverter(ConversionRules rules, TypeScriptEnvironment env)
            : base(rules) {
            this.rules = rules;

            var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

            workspace = MSBuildWorkspace.Create();

            tsProgram = new TypeScriptProgram(rules);
            program = tsProgram;

            tsProgram.AddDotNet(env);
            context = new ConversionContext(program);

            conversion = new TypeScriptConversiorProcessor();

            assemblyName = "";
            version = "";
            targetFramework = "";
        }

        public void WriteFile(string outputFolder, string fileName) {
            var replacements = new Dictionary<string, string>() {
                { "ASSEMBLY_NAME", assemblyName },
                { "ASSEMBLY_VERSION", version },
                { "ASSEMBLY_DESCRIPTION", targetFramework }
            };

            string rootPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, ".net.ts");
            DirectoryUtil.RecursiveCopy(new DirectoryInfo(rootPath), new DirectoryInfo(outputFolder),
                (file, reader, writer) => {
                    string source = reader.ReadToEnd();
                    source = Utils.ReplacePlaceholders(source, replacements);

                    writer.Write(source);
                },
                (file) => {
                    return !file.FullName.EndsWith(".json") && !file.FullName.EndsWith("extractor.js");
                });

            string outputFile = Path.Combine(outputFolder, fileName);
            string outputDir = Path.GetDirectoryName(outputFile)!;
            Directory.CreateDirectory(outputDir);

            if (File.Exists(outputFile)) {
                File.Delete(outputFile);
            }

            Stream stream = File.OpenWrite(outputFile);
            StreamWriter writer = new StreamWriter(stream);

            foreach (var pair in tsProgram.Requirements) {
                if (pair is TypeScriptGenericKnownClass gen) {
                    string imports = "";

                    for (int i = gen.Start; i < gen.TotalImports; i++) {
                        if (i == gen.TotalImports - 1) {
                            imports += gen.Name + i;
                        } else if (i == gen.Start) {
                            imports += gen.Name + ", ";
                        } else {
                            imports += gen.Name + i + ", ";
                        }
                    }

                    writer.WriteLine($"import type {{ {imports} }} from \"{pair.Path}\";");
                } else {
                    if (string.IsNullOrEmpty(pair.Replacement)) {
                        writer.WriteLine($"import {{ {pair.Name} }} from \"{pair.Path}\";");
                    } else {
                        writer.WriteLine($"import {{ {pair.Name} as {pair.Replacement} }} from \"{pair.Path}\";");
                    }
                }
            }

            writer.WriteLine();

            writeOutput(writer);

            writer.WriteLine();

            writer.Flush();
            stream.Dispose();
        }

        private void writeStaticConstructor(ConversionClass cl, StreamWriter writer) {
            var constructors = cl.Functions.Where(c => c.IsConstructor && c.IsStatic).ToList();
            if (constructors.Count == 0) {
                return;
            }

            ConversionFunction fn = constructors[0];
            writer.WriteLine("    static {");

            fn.WriteLines(conversion, program, cl, writer);

            writer.WriteLine("    }");
            writer.WriteLine($"");
        }

        private void writeConstructors(ConversionClass cl, StreamWriter writer) {
            var constructors = cl.Functions.Where(c => c.IsConstructor && !c.IsStatic).ToList();
            var classOverrides = cl.Extensions.Count(over => {
                var extCl = program.Classes.FirstOrDefault(c => c.Name == over);
                if (extCl != null) {
                    return extCl.DeclarationType != MemberDeclarationType.Interface;
                }

                return false;
            });

            if (constructors.Count == 1) {
                ConversionFunction fn = constructors[0];
                writer.Write($"    constructor(");

                for (int k = 0; k < fn.InParameters.Count; k++) {
                    var param = fn.InParameters[k];
                    string type = param.VarType.ToTypeScriptString(tsProgram);
                    string? def = param.DefaultValue;
                    if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                        def = "";
                    }

                    writer.Write($"{param.Name}: {type}{def}");

                    if (k != fn.InParameters.Count - 1) {
                        writer.Write($", ");
                    }
                }

                writer.WriteLine($") {{");

                if (classOverrides > 0) {
                    writer.WriteLine($"        super();");
                }

                fn.WriteLines(conversion, program, cl, writer);

                writer.WriteLine($"    }}");
                writer.WriteLine($"");
            } else {
                string generic = cl.GetGenericArguments();

                for (int i = 0; i < constructors.Count; i++) {
                    ConversionFunction fn = constructors[i];
                    writer.Write($"    static {fn.Name}{generic}(");

                    for (int k = 0; k < fn.InParameters.Count; k++) {
                        var param = fn.InParameters[k];
                        string type = param.VarType.ToTypeScriptString(tsProgram);
                        string? def = param.DefaultValue;
                        if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                            def = "";
                        }

                        writer.Write($"{param.Name}: {type}{def}");

                        if (k != fn.InParameters.Count - 1) {
                            writer.Write($", ");
                        }
                    }

                    if (cl.Name == "ClientSettings" && fn.Name == "New2") {
                        //Debugger.Break();
                    }

                    writer.WriteLine($"): {cl.Name}{generic} {{");

                    writer.WriteLine($"const __obj = new {cl.Name}();");

                    List<string> lines = new List<string>();
                    LayerContext context = new TypeScriptLayerContext((TypeScriptProgram)program);

                    int start = context.DepthClass;
                    int startFn = context.DepthFunction;

                    context.AddClass(cl);
                    context.AddFunction(new FunctionStack(fn));

                    if (fn.ArrowExpression != null) {
                        conversion.ProcessArrowExpressionClause(cl.Semantic, context, fn.ArrowExpression, lines);
                    } else if (fn.RawBlock != null) {
                        conversion.ProcessBlock(cl.Semantic, context, fn.RawBlock, lines);
                    }

                    context.PopClass(start);
                    context.PopFunction(startFn);


                    writer.Write("        ");
                    for (int k = 0; k < lines.Count; k++) {
                        string str = lines[k];
                        str = str.Replace("this.", "__obj.");

                        writer.Write(str);
                        if (str.IndexOf("\n") != -1 && k != lines.Count - 1) {
                            writer.Write("        ");
                        }
                    }

                    writer.WriteLine($"return __obj;");
                    writer.WriteLine($"    }}");
                    writer.WriteLine($"");
                }
            }
        }

        private bool writeVariable(ConversionClass cl, ConversionVariable var, StreamWriter writer) {
            string access = var.AccessType.ToString().ToLowerInvariant();
            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                access = "";
            }

            string type = var.VarType.ToTypeScriptString(tsProgram);

            string accessType = "";
            // TODO: implement none
            if (var.DeclarationType != MemberDeclarationType.Class) {
                accessType += " ";
                accessType += var.DeclarationType.ToString().ToLowerInvariant();
            }

            string isStatic = "";
            if (var.IsStatic) {
                isStatic = " static";
            }

            string assignment = "";
            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                if (var.IsGet && var.IsSet) {
                    writer.WriteLine($"    {access} get {var.Name}(): {type};");
                    writer.WriteLine($"    {access} set {var.Name}(val: {type});");
                } else {
                    if (var.IsGet) {
                        writer.WriteLine($"    {access}{accessType} get {var.Name}(): {type};");
                    }
                    if (var.IsSet) {
                        writer.WriteLine($"    {access}{accessType} set {var.Name}(value: {type});");
                    }
                    if (!var.IsGet && !var.IsSet) {
                        writer.WriteLine($"    {access} {var.Name}: {type};");
                    }
                }
            } else {
                if (!string.IsNullOrEmpty(var.Assignment)) {
                    string ass = var.Assignment;
                    ass = ass.Replace("=>", "").Trim();
                    assignment = $" = {ass}";
                }

                // check for property
                if (var.DeclarationType == MemberDeclarationType.Abstract) {
                    if (var.IsGet && var.IsSet) {
                        writer.WriteLine($"    {access}{isStatic} abstract get {var.Name}(): {type};");
                        writer.WriteLine($"    {access}{isStatic} abstract set {var.Name}(value: {type});");
                        return true;
                    } else if (var.IsGet) {
                        writer.WriteLine($"    {access}{isStatic} abstract get {var.Name}(): {type};");
                        return true;
                    }
                }
                if (var.IsGet && var.IsSet) {
                    writer.WriteLine($"    private{isStatic} _{var.Name}: {type}{assignment};");
                    writer.WriteLine($"    {access}{isStatic} get {var.Name}(): {type} {{");
                    writer.WriteLine($"        return this._{var.Name};");
                    writer.WriteLine($"    }}");
                    writer.WriteLine($"    {access}{isStatic} set {var.Name}(value: {type}) {{");
                    writer.WriteLine($"        this._{var.Name} = value;");
                    writer.WriteLine($"    }}");
                    return true;
                } else if (var.IsGet) {
                    writer.WriteLine($"    private _{var.Name}: {type}{assignment};");
                    writer.WriteLine($"    {access}{isStatic} get {var.Name}(): {type} {{");
                    writer.WriteLine($"        return this._{var.Name};");
                    writer.WriteLine($"    }}");
                    writer.WriteLine($"    private{isStatic} set {var.Name}(value: {type}) {{");
                    writer.WriteLine($"        this._{var.Name} = value;");
                    writer.WriteLine($"    }}");
                    return true;
                } else if (var.ArrowExpression != null) {
                    writer.WriteLine($"    {access}{isStatic} get {var.Name}(): {type} {{");

                    writer.Write("    ");
                    writer.Write("    ");
                    writer.Write("return ");

                    List<string> lines = new List<string>();
                    TypeScriptLayerContext context = new TypeScriptLayerContext(tsProgram);

                    int start = context.DepthClass;
                    context.AddClass(cl);
                    conversion.ProcessExpression(cl.Semantic, context, var.ArrowExpression, lines);
                    context.PopClass(start);

                    for (int k = 0; k < lines.Count; k++) {
                        string str = lines[k];
                        writer.Write(str);
                    }
                    writer.Write(";\n");

                    writer.WriteLine($"    }}");
                    return true;
                } else if (var.GetBlock != null || var.SetBlock != null) {
                    if (var.GetBlock != null) {
                        ConversionFunction fn = new ConversionFunction();
                        fn.Name = $"get_{var.Name}";

                        fn.RawBlock = var.GetBlock;

                        writer.WriteLine($"    {access}{isStatic} get {var.Name}(): {type} {{");
                        fn.WriteLines(conversion, program, cl, writer);
                        writer.WriteLine();
                        writer.WriteLine($"    }}");
                    }

                    if (var.SetBlock != null) {
                        ConversionFunction fn = new ConversionFunction();
                        ConversionVariable value = new ConversionVariable();
                        value.VarType = var.VarType;
                        value.Name = "value";
                        fn.InParameters.Add(value);
                        fn.Name = $"set_{var.Name}";

                        fn.RawBlock = var.SetBlock;

                        writer.WriteLine($"    {access}{isStatic} set {var.Name}(value: {type}) {{");
                        fn.WriteLines(conversion, program, cl, writer);
                        writer.WriteLine();
                        writer.WriteLine($"    }}");
                    }
                } else {
                    writer.WriteLine($"    {access}{accessType}{isStatic} {var.Name}: {type}{assignment};");
                }
            }

            return false;
        }

        private void preprocessClass(ConversionClass cl) {
            if (cl.IsNative) {
                return;
            }

            var functions = cl.Functions.Where(c => !c.IsConstructor).ToList();
            for (int j = 0; j < functions.Count; j++) {
                ConversionFunction fn = functions[j];

                if (fn.Name == "Dispose") {
                    fn.Remap = "dispose";
                }

                if (cl.Name == "ApplicationManager" && fn.Name == "BootApplication") {
                    //Debugger.Break();
                }

                fn.WriteLines(conversion, program, cl);
            }
        }

        private void writeClass(ConversionClass cl, StreamWriter writer) {
            if (cl.IsNative) {
                return;
            }

            var (implements, extends) = TypeScriptUtils.GetInheritance(program, cl);

            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                writer.WriteLine($"export interface {cl.Name}{extends} {{");
            } else if (cl.DeclarationType == MemberDeclarationType.Abstract) {
                writer.WriteLine($"export abstract class {cl.Name}{extends} {{");
            } else if (cl.DeclarationType == MemberDeclarationType.Delegate) {
                ConversionFunction del = cl.Functions[0];

                string generic = del.GetGenericArguments();

                writer.Write($"export type {del.Remap}{generic} = (");

                for (int k = 0; k < del.InParameters.Count; k++) {
                    var param = del.InParameters[k];
                    string type = param.VarType.ToTypeScriptString(tsProgram);
                    string? def = param.DefaultValue;
                    if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                        def = "";
                    }

                    writer.Write($"{param.Name}: {type}{def}");

                    if (k != del.InParameters.Count - 1) {
                        writer.Write($", ");
                    }
                }

                writer.Write(") => ");

                if (del.ReturnType == null) {
                    writer.Write("void");
                } else {
                    writer.Write(del.ReturnType.ToTypeScriptString(tsProgram));
                }

                writer.WriteLine(";");
                writer.WriteLine();

                return;
            } else if (cl.DeclarationType == MemberDeclarationType.Enum) {
                writer.WriteLine($"export enum {cl.Name} {{");
                if (cl.EnumMembers != null) {
                    for (int j = 0; j < cl.EnumMembers.Count; j++) {
                        if (j == cl.EnumMembers.Count - 1) {
                            writer.WriteLine($"    {cl.EnumMembers[j]}");
                        } else {
                            writer.WriteLine($"    {cl.EnumMembers[j]},");
                        }
                    }
                }
                writer.WriteLine($"}}");
                writer.WriteLine();

                return;
            } else {
                if (cl.GenericArgs?.Count > 0) {
                    string generics = "";
                    for (int j = 0; j < cl.GenericArgs.Count; j++) {
                        generics += cl.GenericArgs[j];
                        if (j != cl.GenericArgs.Count - 1) {
                            generics += ", ";
                        }
                    }

                    writer.WriteLine($"export class {cl.Name}<{generics}>{extends}{implements} {{");
                } else {
                    writer.WriteLine($"export class {cl.Name}{extends}{implements} {{");
                }
            }

            SortVariables(cl);

            for (int j = 0; j < cl.Variables.Count; j++) {
                ConversionVariable var = cl.Variables[j];
                if (writeVariable(cl, var, writer)) {
                    if (j != cl.Variables.Count - 1) {
                        writer.WriteLine();
                    }
                }
            }

            if (cl.Variables.Count > 0) {
                writer.WriteLine();
            }

            writeStaticConstructor(cl, writer);

            writeConstructors(cl, writer);

            var functions = cl.Functions.Where(c => !c.IsConstructor).ToList();

            for (int j = 0; j < functions.Count; j++) {
                ConversionFunction fn = functions[j];
                string name = fn.Name;

                string access = fn.AccessType.ToString().ToLowerInvariant();
                if (cl.DeclarationType == MemberDeclarationType.Class) {
                    access += " ";
                } else {
                    access = "";
                }

                if (j != 0) {
                    writer.WriteLine();
                }

                if (cl.Name == "WebSocketClientMessageHandler" && name == "CreateWs") {
                    Debugger.Break();
                }

                List<string> lines = fn.WriteLines(conversion, program, cl);

                string generic = fn.GetGenericArguments();
                string clType = fn.GetClassType();
                string async = fn.GetAsync();

                if (cl.DeclarationType == MemberDeclarationType.Interface) {
                    async = "";
                }

                if (fn.IsStatic) {
                    writer.Write($"    {access}static {async}{fn.Remap}{generic}(");
                } else {
                    writer.Write($"    {access}{clType}{async}{fn.Remap}{generic}(");
                }

                for (int k = 0; k < fn.InParameters.Count; k++) {
                    var param = fn.InParameters[k];
                    string type = param.VarType.ToTypeScriptString(tsProgram);
                    string? def = param.DefaultValue;
                    if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                        def = "";
                    }

                    if (param.Modifier == core.ParameterModifier.Out) {
                        writer.Write($"{param.Name}: {{ value: {type}{def} }}");
                    } else {
                        writer.Write($"{param.Name}: {type}{def}");
                    }

                    if (k != fn.InParameters.Count - 1) {
                        writer.Write($", ");
                    }
                }

                if (cl.DeclarationType == MemberDeclarationType.Interface || !fn.HasBody) {
                    writer.WriteLine(");");
                } else {
                    string returnParameter = fn.ReturnType?.ToTypeScriptString(tsProgram);
                    if (fn.IsAsync && !string.IsNullOrWhiteSpace(returnParameter)) {
                        returnParameter = $"Promise<{returnParameter}>";
                    }

                    if (string.IsNullOrEmpty(returnParameter)) {
                        writer.WriteLine($") {{");
                    } else {
                        writer.WriteLine($"): {returnParameter} {{");
                    }

                    TypeScriptFunction.PrintLines(writer, lines);
                    writer.WriteLine("    }");
                }
            }

            writer.WriteLine($"}}");
            writer.WriteLine();
        }

        private void writeOutput(StreamWriter writer) {
            SortProgram();

            // pre-process
            int steps = 3;
            for (int i = 0; i < steps; i++) {
                for (int j = 0; j < program.Classes.Count; j++) {
                    preprocessClass(program.Classes[j]);
                }
            }

            // check for async functions in inheritance hierarchy
            for (int j = 0; j < program.Classes.Count; j++) {
                ConversionClass cl = program.Classes[j];
                for (int i = 0; i < cl.Functions.Count; i++) {
                    ConversionFunction fn = cl.Functions[i];

                    if (fn.IsAsync) {
                        // check parent and child classes for the same function name
                        // and make them async too
                        string functionName = fn.Name;

                        // Check all classes that might have this function (parent or child)
                        for (int k = 0; k < program.Classes.Count; k++) {
                            ConversionClass otherClass = program.Classes[k];

                            // Skip the current class
                            if (otherClass == cl) continue;

                            // Check if this class extends the current class or vice versa
                            bool isRelated = cl.Extensions.Contains(otherClass.Name) ||
                                           otherClass.Extensions.Contains(cl.Name);

                            if (isRelated) {
                                // Find the same function in the related class
                                ConversionFunction relatedFn = otherClass.Functions.FirstOrDefault(f => f.Name == functionName);
                                if (relatedFn != null) {
                                    // Make the related function async too
                                    relatedFn.IsAsync = true;
                                }
                            }
                        }
                    }
                }
            }

            // Also check for base class async functions that need to propagate to derived classes
            for (int j = 0; j < program.Classes.Count; j++) {
                ConversionClass cl = program.Classes[j];
                for (int i = 0; i < cl.Functions.Count; i++) {
                    ConversionFunction fn = cl.Functions[i];

                    if (fn.IsAsync) {
                        string functionName = fn.Name;

                        // Find all classes that extend this class (derived classes)
                        for (int k = 0; k < program.Classes.Count; k++) {
                            ConversionClass derivedClass = program.Classes[k];

                            // Skip the current class
                            if (derivedClass == cl) continue;

                            // Check if derived class extends this class
                            if (derivedClass.Extensions.Contains(cl.Name)) {
                                // Find the same function in the derived class
                                ConversionFunction derivedFn = derivedClass.Functions.FirstOrDefault(f => f.Name == functionName);
                                if (derivedFn != null) {
                                    // Make the derived function async too
                                    derivedFn.IsAsync = true;
                                }
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < program.Classes.Count; i++) {
                writeClass(program.Classes[i], writer);
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
