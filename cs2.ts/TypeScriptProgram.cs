using cs2.core;
using cs2.core.json;
using Nucleus;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace cs2.ts {
    public class TypeScriptProgram : ConvertedProgram {
        public TypeScriptProgram(ConversionRules rules)
            : base(rules) {
        }

        public void AddDotNet(TypeScriptEnvironment env) {
            this.buildTypeMap();

            this.buildNativeRemap();

            //this.buildDotNetData();

            // system
            Requirements.Add(new GenericKnownClass(17, 0, false, "Action", "./system/action"));
            Requirements.Add(new GenericKnownClass(17, 1, false, "Func", "./system/func"));
            Requirements.Add(new KnownClass("ConsoleColor", "./system/console-color"));
            Requirements.Add(new KnownClass("DateTime", "./system/date-time"));
            Requirements.Add(new KnownClass("TimeSpan", "./system/time-span"));
            Requirements.Add(new KnownClass("Exception", "./system/exception"));
            Requirements.Add(new KnownClass("NotSupportedException", "./system/not-supported.exception"));
            Requirements.Add(new KnownClass("ArgumentException", "./system/argument.exception"));
            Requirements.Add(new KnownClass("Random", "./system/random"));
            Requirements.Add(new KnownClass("Attribute", "./system/attribute"));
            Requirements.Add(new KnownClass("Version", "./system/version"));
            Requirements.Add(new KnownClass("Tuple", "./system/tuple"));
            Requirements.Add(new KnownClass("Console", "./system/console"));
            Requirements.Add(new KnownClass("IDisposable", "./system/disposable.interface"));
            Requirements.Add(new KnownClass("Guid", "./system/guid"));

            // system.collection.concurrent
            Requirements.Add(new KnownClass("ConcurrentDictionary", "./system/collections/concurrent/concurrent-dictionary"));

            // system.collection.generic
            Requirements.Add(new KnownClass("IDictionary", "./system/collections/generic/dictionary.interface"));
            Requirements.Add(new KnownClass("Dictionary", "./system/collections/generic/dictionary"));
            Requirements.Add(new KnownClass("List", "./system/collections/generic/list"));
            Requirements.Add(new KnownClass("KeyValuePair", "./system/collections/generic/key-value-pair"));
            Requirements.Add(new KnownClass("SortedList", "./system/collections/generic/sorted-list"));
            Requirements.Add(new KnownClass("Queue", "./system/collections/generic/queue"));

            // system.drawing
            Requirements.Add(new KnownClass("Point", "./system/drawing/point"));
            Requirements.Add(new KnownClass("Rectangle", "./system/drawing/rectangle"));
            Requirements.Add(new KnownClass("Size", "./system/drawing/size"));

            // system.diagnostics
            Requirements.Add(new KnownClass("Debug", "./system/diagnostics/debug"));
            Requirements.Add(new KnownClass("Stopwatch", "./system/diagnostics/stopwatch"));

            // system.io
            Requirements.Add(new KnownClass("SeekOrigin", "./system/io/seek-origin"));
            Requirements.Add(new KnownClass("Stream", "./system/io/stream"));
            Requirements.Add(new KnownClass("MemoryStream", "./system/io/memory-stream"));
            Requirements.Add(new KnownClass("StreamWriter", "./system/io/stream-writer"));
            Requirements.Add(new KnownClass("BinaryReader", "./system/io/binary-reader"));
            Requirements.Add(new KnownClass("BinaryWriter", "./system/io/binary-writer"));
            Requirements.Add(new KnownClass("DirectoryInfo", "./system/io/directory-info"));
            Requirements.Add(new KnownClass("FileInfo", "./system/io/file-info"));
            Requirements.Add(new KnownClass("SearchOption", "./system/io/search-option"));
            Requirements.Add(new KnownClass("StreamReader", "./system/io/stream-reader"));
            Requirements.Add(new KnownClass("TextWriter", "./system/io/text-writer"));
            Requirements.Add(new KnownClass("DriveInfo", "./system/io/drive-info"));

            // system.net.sockets
            Requirements.Add(new KnownClass("TcpClient", "./system/net/sockets/tcp-client"));
            Requirements.Add(new KnownClass("TcpListener", "./system/net/sockets/tcp-listener"));

            // system.reflection
            Requirements.Add(new KnownClass("Assembly", "./system/reflection/assembly"));
            Requirements.Add(new KnownClass("AssemblyName", "./system/reflection/assembly-name"));

            // system.security.cryptography
            Requirements.Add(new KnownClass("SHA256", "./system/security/cryptography/sha256"));
            Requirements.Add(new KnownClass("MD5", "./system/security/cryptography/md5"));

            // system.text
            Requirements.Add(new KnownClass("Encoding", "./system/text/encoding"));

            // system.threading
            Requirements.Add(new KnownClass("Thread", "./system/threading/thread"));
            Requirements.Add(new KnownClass("AutoResetEvent", "./system/threading/auto-reset-event"));
            Requirements.Add(new KnownClass("SynchronizationContext", "./system/threading/synchronization-context", "", true));
            Requirements.Add(new KnownClass("SendOrPostCallback", "./system/threading/send-or-post-callback"));

            // system.threading.tasks
            Requirements.Add(new KnownClass("Task", "./system/threading/tasks/task", "", true));

            // WebSocketSharp
            Requirements.Add(new KnownClass("WebSocketWS", "./websocketsharp/websocket"));
            Requirements.Add(new KnownClass("WebSocketState", "./websocketsharp/websocket-state"));

            switch (env) {
                case TypeScriptEnvironment.Web:
                    addWeb();
                    break;
                case TypeScriptEnvironment.NodeJS:
                    addNode();
                    break;
            }

            for (int i = 0; i < Requirements.Count; i++) {
                KnownClass requirement = Requirements[i];

                ConvertedClass cl = new ConvertedClass();
                cl.IsNative = true;
                cl.Name = requirement.Name;
                Classes.Add(cl);

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
                            ConvertedVariable var = new ConvertedVariable();
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
                            ConvertedVariable var = new ConvertedVariable();
                            var.Name = member.Name;
                            if (string.IsNullOrEmpty(member.PropertyType)) {
                                var.VarType = VariableUtil.GetVarType(member.ReturnType);
                            } else {
                                var.VarType = VariableUtil.GetVarType(member.PropertyType);
                            }
                            cl.Variables.Add(var);
                        } else if (member.Type == "method") {
                            ConvertedFunction fn = new ConvertedFunction();
                            fn.Name = member.Name;
                            fn.ReturnType = VariableUtil.GetVarType(member.ReturnType);
                            cl.Functions.Add(fn);

                            for (int l = 0; l < member.Parameters.Count; l++) {
                                var parameter = member.Parameters[l];
                                ConvertedVariable var = new ConvertedVariable();
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

        private void addNode() {
            Requirements.Add(new KnownClass("NodeDirectory", "./system/io/node-directory", "Directory"));
        }

        private void addWeb() {
            Requirements.Add(new KnownClass("WebDirectory", "./system/io/web-directory", "Directory"));
        }

        private ConvertedClass makeClass(string name) {
            ConvertedClass cl = new ConvertedClass();
            cl.Name = name;
            cl.IsNative = true;

            makeTypeScriptFunction("ToString", "toString", cl);

            return cl;
        }

        private void makeTypeScriptFunction(string name, string remap, ConvertedClass cl, string type = "") {
            ConvertedFunction fnToString = new ConvertedFunction();
            fnToString.Name = name;
            fnToString.Remap = remap;
            if (!string.IsNullOrEmpty(type)) {
                fnToString.ReturnType = VariableUtil.GetVarType(type);
            }
            cl.Functions.Add(fnToString);
        }

        private void makeTypeScriptVariable(string name, string remap, ConvertedClass cl, string type) {
            ConvertedVariable fnToString = new ConvertedVariable();
            fnToString.Name = name;
            fnToString.Remap = remap;
            fnToString.VarType = VariableUtil.GetVarType(type);
            cl.Variables.Add(fnToString);
        }

        private void buildNativeRemap() {
            ConvertedClass clArray = makeClass("Array");
            Classes.Add(clArray);
            makeTypeScriptVariable("Length", "length", clArray, "int");

            ConvertedClass clnumber = makeClass("number");
            Classes.Add(clnumber);

            ConvertedClass clNumber = makeClass("Number");
            makeTypeScriptFunction("MaxValue", "MAX_VALUE", clNumber, "int");
            Classes.Add(clNumber);

            ConvertedClass clString = makeClass("string");
            makeTypeScriptFunction("Length", "length", clString, "int");
            makeTypeScriptFunction("Replace", "replace", clString, "string");
            makeTypeScriptFunction("Remove", "slice", clString, "string");
            Classes.Add(clString);

            ConvertedClass clBool = makeClass("boolean");
            Classes.Add(clBool);

            ConvertedClass clMath = makeClass("Math");
            makeTypeScriptFunction("Round", "round", clMath, "int");
            Classes.Add(clMath);

            ConvertedClass clUint8Array = makeClass("Uint8Array");
            makeTypeScriptVariable("Length", "length", clUint8Array, "int");
            Classes.Add(clUint8Array);
        }


        private void buildDotNetData() {
            string startFolder = AssemblyUtil.GetStartFolder();
            string dotNetFolder = Path.Combine(startFolder, ".net");
            string extractorPath = Path.Combine(dotNetFolder, "extractor.js");

            List<FileInfo> files = new List<FileInfo>();
            DirectoryUtil.RecursiveList(new DirectoryInfo(Path.Combine(dotNetFolder, "system")), files);
            DirectoryUtil.RecursiveList(new DirectoryInfo(Path.Combine(dotNetFolder, "websocketsharp")), files);

            string shell;
            string shellArguments;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                shell = "cmd.exe";
                shellArguments = "/c npm install --verbose"; // Windows cmd uses /c to run the command
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                shell = "/bin/bash"; // Use bash for macOS/Linux
                shellArguments = "-c \"npm install --verbose\""; // Use -c to run the command
            } else {
                throw new PlatformNotSupportedException("Unsupported operating system.");
            }

            var npm = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = shell,
                    Arguments = shellArguments,
                    WorkingDirectory = dotNetFolder, // Set the working directory to your project folder
                    RedirectStandardOutput = true, // Redirect the output
                    RedirectStandardError = true,  // Redirect errors
                    UseShellExecute = false,       // Do not use the shell to execute
                    CreateNoWindow = true          // Run without creating a window
                }
            };

            Console.WriteLine($"-- npm install");

            npm.Start();
            npm.WaitForExit();
            string npmOutput = npm.StandardOutput.ReadToEnd();
            Console.WriteLine($"---- result: {npmOutput}");

            for (int i = 0; i < files.Count; i++) {
                FileInfo f = files[i];

                var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = "node",
                        Arguments = $"\"{extractorPath}\" \"{f.FullName}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                Console.WriteLine($"-- Processing file: {f.Name}");

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                Console.WriteLine($"---- Result: {output}");
                process.WaitForExit();
            }
        }

        private void buildTypeMap() {
            TypeMap.Add("object", "any");

            TypeMap.Add("Byte", "number");
            TypeMap.Add("byte", "number");
            TypeMap.Add("sbyte", "number");
            TypeMap.Add("int", "number");
            TypeMap.Add("Int16", "number");
            TypeMap.Add("Int32", "number");
            TypeMap.Add("Int64", "number");
            TypeMap.Add("uint", "number");
            TypeMap.Add("UInt16", "number");
            TypeMap.Add("UInt32", "number");
            TypeMap.Add("UInt64", "number");
            TypeMap.Add("long", "number");
            TypeMap.Add("ulong", "number");
            TypeMap.Add("float", "number");
            TypeMap.Add("double", "number");
            TypeMap.Add("decimal", "number");
            TypeMap.Add("Single", "number");

            TypeMap.Add("bool", "boolean");
            TypeMap.Add("Boolean", "boolean");

            TypeMap.Add("char", "string");
            TypeMap.Add("String", "string");

            TypeMap.Add("Array", "Array<any>");
        }
    }
}
