using cs2.core;
using cs2.core.symbols;
using Nucleus;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace cs2.ts {
    public class TypeScriptProgram : ConversionProgram {
        public List<TypeScriptKnownClass> Requirements { get; private set; }

        public TypeScriptProgram(ConversionRules rules)
            : base(rules) {
            Requirements = new List<TypeScriptKnownClass>();
        }

        public void AddDotNet(TypeScriptEnvironment env) {
            this.buildTypeMap();

            this.buildNativeRemap();

            this.buildDotNetData();

            // system
            Requirements.Add(new TypeScriptKnownClass("NotSupportedException", "./system/not-supported.exception"));
            Requirements.Add(new TypeScriptKnownClass("NotImplementedException", "./system/not-implemented.exception"));
            Requirements.Add(new TypeScriptGenericKnownClass(17, 0, false, "Action", "./system/action"));
            Requirements.Add(new TypeScriptGenericKnownClass(17, 1, false, "Func", "./system/func"));
            Requirements.Add(new TypeScriptKnownClass("ConsoleColor", "./system/console-color"));
            Requirements.Add(new TypeScriptKnownClass("DateTime", "./system/date-time"));
            Requirements.Add(new TypeScriptKnownClass("TimeSpan", "./system/time-span"));
            Requirements.Add(new TypeScriptKnownClass("Exception", "./system/exception"));
            Requirements.Add(new TypeScriptKnownClass("ArgumentException", "./system/argument.exception"));
            Requirements.Add(new TypeScriptKnownClass("Random", "./system/random"));
            Requirements.Add(new TypeScriptKnownClass("Attribute", "./system/attribute"));
            Requirements.Add(new TypeScriptKnownClass("Version", "./system/version"));
            Requirements.Add(new TypeScriptKnownClass("Tuple", "./system/tuple"));
            Requirements.Add(new TypeScriptKnownClass("Console", "./system/console"));
            Requirements.Add(new TypeScriptKnownClass("IDisposable", "./system/disposable.interface"));
            Requirements.Add(new TypeScriptKnownClass("Guid", "./system/guid"));
            Requirements.Add(new TypeScriptKnownClass("ArrayUtil", "./system/array-util"));

            // system.collection.concurrent
            Requirements.Add(new TypeScriptKnownClass("ConcurrentDictionary", "./system/collections/concurrent/concurrent-dictionary"));

            // system.collection.generic
            Requirements.Add(new TypeScriptKnownClass("IDictionary", "./system/collections/generic/dictionary.interface"));
            Requirements.Add(new TypeScriptKnownClass("Dictionary", "./system/collections/generic/dictionary"));
            Requirements.Add(new TypeScriptKnownClass("List", "./system/collections/generic/list"));
            Requirements.Add(new TypeScriptKnownClass("KeyValuePair", "./system/collections/generic/key-value-pair"));
            Requirements.Add(new TypeScriptKnownClass("SortedList", "./system/collections/generic/sorted-list"));
            Requirements.Add(new TypeScriptKnownClass("Queue", "./system/collections/generic/queue"));

            // system.drawing
            Requirements.Add(new TypeScriptKnownClass("Point", "./system/drawing/point"));
            Requirements.Add(new TypeScriptKnownClass("Rectangle", "./system/drawing/rectangle"));
            Requirements.Add(new TypeScriptKnownClass("Size", "./system/drawing/size"));

            // system.diagnostics
            Requirements.Add(new TypeScriptKnownClass("Debug", "./system/diagnostics/debug"));
            Requirements.Add(new TypeScriptKnownClass("Stopwatch", "./system/diagnostics/stopwatch"));

            // system.io
            Requirements.Add(new TypeScriptKnownClass("SeekOrigin", "./system/io/seek-origin"));
            Requirements.Add(new TypeScriptKnownClass("Stream", "./system/io/stream"));
            Requirements.Add(new TypeScriptKnownClass("MemoryStream", "./system/io/memory-stream"));
            Requirements.Add(new TypeScriptKnownClass("StreamWriter", "./system/io/stream-writer"));
            Requirements.Add(new TypeScriptKnownClass("BinaryReader", "./system/io/binary-reader"));
            Requirements.Add(new TypeScriptKnownClass("BinaryWriter", "./system/io/binary-writer"));
            Requirements.Add(new TypeScriptKnownClass("DirectoryInfo", "./system/io/directory-info"));
            Requirements.Add(new TypeScriptKnownClass("FileInfo", "./system/io/file-info"));
            Requirements.Add(new TypeScriptKnownClass("FileMode", "./system/io/file-mode"));
            Requirements.Add(new TypeScriptKnownClass("FileStream", "./system/io/file-stream"));
            Requirements.Add(new TypeScriptKnownClass("File", "./system/io/file"));
            Requirements.Add(new TypeScriptKnownClass("SearchOption", "./system/io/search-option"));
            Requirements.Add(new TypeScriptKnownClass("StreamReader", "./system/io/stream-reader"));
            Requirements.Add(new TypeScriptKnownClass("TextWriter", "./system/io/text-writer"));
            Requirements.Add(new TypeScriptKnownClass("DriveInfo", "./system/io/drive-info"));

            // system.net.sockets
            Requirements.Add(new TypeScriptKnownClass("TcpClient", "./system/net/sockets/tcp-client"));
            Requirements.Add(new TypeScriptKnownClass("TcpListener", "./system/net/sockets/tcp-listener"));

            // system.reflection
            Requirements.Add(new TypeScriptKnownClass("Assembly", "./system/reflection/assembly"));
            Requirements.Add(new TypeScriptKnownClass("AssemblyName", "./system/reflection/assembly-name"));

            // system.security.cryptography
            Requirements.Add(new TypeScriptKnownClass("SHA256", "./system/security/cryptography/sha256"));
            Requirements.Add(new TypeScriptKnownClass("MD5", "./system/security/cryptography/md5"));

            // system.text
            Requirements.Add(new TypeScriptKnownClass("Encoding", "./system/text/encoding"));

            // system.threading
            Requirements.Add(new TypeScriptKnownClass("Thread", "./system/threading/thread"));
            Requirements.Add(new TypeScriptKnownClass("AutoResetEvent", "./system/threading/auto-reset-event"));
            Requirements.Add(new TypeScriptKnownClass("SynchronizationContext", "./system/threading/synchronization-context", "", true));
            Requirements.Add(new TypeScriptKnownClass("SendOrPostCallback", "./system/threading/send-or-post-callback"));

            // system.threading.tasks
            Requirements.Add(new TypeScriptKnownClass("Task", "./system/threading/tasks/task", "", true));

            // WebSocketSharp
            Requirements.Add(new TypeScriptKnownClass("WebSocketWS", "./websocketsharp/websocket"));
            Requirements.Add(new TypeScriptKnownClass("WebSocketState", "./websocketsharp/websocket-state"));

            // Blake2B
            Requirements.Add(new TypeScriptKnownClass("Blake2b", "./blake2fast/blake2b"));

            switch (env) {
                case TypeScriptEnvironment.Web:
                    addWeb();
                    break;
                case TypeScriptEnvironment.NodeJS:
                    addNode();
                    break;
            }

            for (int i = 0; i < Requirements.Count; i++) {
                TypeScriptKnownClass requirement = Requirements[i];

                ConversionClass cl = new ConversionClass();
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

        private void addNode() {
            Requirements.Add(new TypeScriptKnownClass("NodeDirectory", "./system/io/node-directory", "Directory"));
        }

        private void addWeb() {
            Requirements.Add(new TypeScriptKnownClass("WebDirectory", "./system/io/web-directory", "Directory"));
        }

        private ConversionClass makeClass(string name) {
            ConversionClass cl = new ConversionClass();
            cl.Name = name;
            cl.IsNative = true;

            makeTypeScriptFunction("ToString", "toString", cl);

            return cl;
        }

        private void makeTypeScriptFunction(string name, string remap, ConversionClass cl, string type = "", string remapCl = "") {
            ConversionFunction fnToString = new ConversionFunction();
            fnToString.Name = name;
            fnToString.Remap = remap;
            fnToString.RemapClass = remapCl;
            if (!string.IsNullOrEmpty(type)) {
                fnToString.ReturnType = VariableUtil.GetVarType(type);
            }
            cl.Functions.Add(fnToString);
        }

        private void makeTypeScriptVariable(string name, string remap, ConversionClass cl, string type) {
            ConversionVariable fnToString = new ConversionVariable();
            fnToString.Name = name;
            fnToString.Remap = remap;
            fnToString.VarType = VariableUtil.GetVarType(type);
            cl.Variables.Add(fnToString);
        }

        private void buildNativeRemap() {
            ConversionClass clArray = makeClass("Array");
            Classes.Add(clArray);
            makeTypeScriptVariable("Length", "length", clArray, "int");
            makeTypeScriptFunction("Copy", "copy", clArray, "", "ArrayUtil");

            ConversionClass clnumber = makeClass("number");
            Classes.Add(clnumber);

            ConversionClass clNumber = makeClass("Number");
            makeTypeScriptFunction("MaxValue", "MAX_VALUE", clNumber, "int");
            Classes.Add(clNumber);

            ConversionClass clString = makeClass("string");
            makeTypeScriptFunction("Length", "length", clString, "int");
            makeTypeScriptFunction("Replace", "replace", clString, "string");
            makeTypeScriptFunction("Remove", "slice", clString, "string");
            Classes.Add(clString);

            ConversionClass clBool = makeClass("boolean");
            Classes.Add(clBool);

            ConversionClass clMath = makeClass("Math");
            makeTypeScriptFunction("Round", "round", clMath, "int");
            Classes.Add(clMath);

            ConversionClass clUint8Array = makeClass("Uint8Array");
            makeTypeScriptVariable("Length", "length", clUint8Array, "int");
            Classes.Add(clUint8Array);
        }

        private void buildDotNetData() {
            string startFolder = AssemblyUtil.GetStartFolder();
            string dotNetFolder = Path.Combine(startFolder, ".net.ts");
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

            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "node",
                    Arguments = $"\"{extractorPath}\" \"{dotNetFolder}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            Console.WriteLine($"-- Processing folder: {dotNetFolder}");

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string outputError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Console.WriteLine($"---- Result: {output}");
            Console.WriteLine($"---- Result Error: {outputError}");
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
