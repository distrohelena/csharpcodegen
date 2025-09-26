using cs2.core;
using cs2.core.symbols;
using Nucleus;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;

namespace cs2.ts {
    /// <summary>
    /// Holds TypeScript target configuration, known-class metadata and type mappings.
    /// Responsible for wiring the TS runtime symbols and native remaps used by the converter.
    /// </summary>
    public class TypeScriptProgram : ConversionProgram {
        public List<TypeScriptKnownClass> Requirements { get; private set; }
        private void AddRequirement(TypeScriptKnownClass requirement) {
            if (!Requirements.Any(r => r.Name == requirement.Name && r.Path == requirement.Path)) {
                Requirements.Add(requirement);
            }
        }



        public TypeScriptProgram(ConversionRules rules)
            : base(rules) {
            Requirements = new List<TypeScriptKnownClass>();
        }

        /// <summary>
        /// Configures the TypeScript program with .NET-like runtime symbols and mappings for the given environment.
        /// Loads symbol JSON from .net.ts, sets up native remaps, and populates <see cref="Classes"/>.
        /// </summary>
        public void AddDotNet(TypeScriptEnvironment env) {
            this.buildTypeMap();

            this.buildNativeRemap();

            this.buildDotNetData();

            // system
            AddRequirement(new TypeScriptKnownClass("InvalidOperationException", "./system/invalid-operation.exception"));
            AddRequirement(new TypeScriptKnownClass("NotSupportedException", "./system/not-supported.exception"));
            AddRequirement(new TypeScriptKnownClass("NotImplementedException", "./system/not-implemented.exception"));
            AddRequirement(new TypeScriptKnownClass("ArgumentException", "./system/argument.exception"));
            AddRequirement(new TypeScriptKnownClass("ArgumentNullException", "./system/argument-null.exception"));
            AddRequirement(new TypeScriptGenericKnownClass(17, 0, false, "Action", "./system/action"));
            AddRequirement(new TypeScriptGenericKnownClass(17, 1, false, "Func", "./system/func"));
            AddRequirement(new TypeScriptKnownClass("ConsoleColor", "./system/console-color"));
            AddRequirement(new TypeScriptKnownClass("DateTime", "./system/date-time"));
            AddRequirement(new TypeScriptKnownClass("TimeSpan", "./system/time-span"));
            AddRequirement(new TypeScriptKnownClass("Exception", "./system/exception"));
            AddRequirement(new TypeScriptKnownClass("Random", "./system/random"));
            AddRequirement(new TypeScriptKnownClass("Attribute", "./system/attribute"));
            AddRequirement(new TypeScriptKnownClass("Version", "./system/version"));
            AddRequirement(new TypeScriptKnownClass("Tuple", "./system/tuple"));
            AddRequirement(new TypeScriptKnownClass("Console", "./system/console"));
            AddRequirement(new TypeScriptKnownClass("IDisposable", "./system/disposable.interface"));
            AddRequirement(new TypeScriptKnownClass("Guid", "./system/guid"));
            AddRequirement(new TypeScriptKnownClass("NativeArrayUtil", "./system/util/nat-array-util"));
            AddRequirement(new TypeScriptKnownClass("NativeStringUtil", "./system/util/nat-string-util"));
            AddRequirement(new TypeScriptKnownClass("Convert", "./system/convert"));
            AddRequirement(new TypeScriptKnownClass("Environment", "./system/environment"));
            AddRequirement(new TypeScriptKnownClass("AppDomain", "./system/app-domain"));

            // system.collection.concurrent
            AddRequirement(new TypeScriptKnownClass("ConcurrentDictionary", "./system/collections/concurrent/concurrent-dictionary"));

            // system.collection.generic
            AddRequirement(new TypeScriptKnownClass("IDictionary", "./system/collections/generic/dictionary.interface"));
            AddRequirement(new TypeScriptKnownClass("Dictionary", "./system/collections/generic/dictionary"));
            AddRequirement(new TypeScriptKnownClass("ICollection", "./system/collections/generic/icollection"));
            AddRequirement(new TypeScriptKnownClass("Dictionary", "./system/collections/generic/dictionary"));
            AddRequirement(new TypeScriptKnownClass("List", "./system/collections/generic/list"));
            AddRequirement(new TypeScriptKnownClass("KeyValuePair", "./system/collections/generic/key-value-pair"));
            AddRequirement(new TypeScriptKnownClass("SortedList", "./system/collections/generic/sorted-list"));
            AddRequirement(new TypeScriptKnownClass("Queue", "./system/collections/generic/queue"));

            // system.drawing
            AddRequirement(new TypeScriptKnownClass("Point", "./system/drawing/point"));
            AddRequirement(new TypeScriptKnownClass("Rectangle", "./system/drawing/rectangle"));
            AddRequirement(new TypeScriptKnownClass("Size", "./system/drawing/size"));

            // system.diagnostics
            AddRequirement(new TypeScriptKnownClass("Debug", "./system/diagnostics/debug"));
            AddRequirement(new TypeScriptKnownClass("Stopwatch", "./system/diagnostics/stopwatch"));

            // system.io
            AddRequirement(new TypeScriptKnownClass("SeekOrigin", "./system/io/seek-origin"));
            AddRequirement(new TypeScriptKnownClass("Stream", "./system/io/stream"));
            AddRequirement(new TypeScriptKnownClass("MemoryStream", "./system/io/memory-stream"));
            AddRequirement(new TypeScriptKnownClass("StreamWriter", "./system/io/stream-writer"));
            AddRequirement(new TypeScriptKnownClass("BinaryReader", "./system/io/binary-reader"));
            AddRequirement(new TypeScriptKnownClass("BinaryWriter", "./system/io/binary-writer"));
            AddRequirement(new TypeScriptKnownClass("DirectoryInfo", "./system/io/directory-info"));
            AddRequirement(new TypeScriptKnownClass("FileInfo", "./system/io/file-info"));
            AddRequirement(new TypeScriptKnownClass("FileMode", "./system/io/file-mode"));
            AddRequirement(new TypeScriptKnownClass("FileAccess", "./system/io/file-access"));
            AddRequirement(new TypeScriptKnownClass("FileShare", "./system/io/file-share"));
            AddRequirement(new TypeScriptKnownClass("FileStream", "./system/io/file-stream"));            
            AddRequirement(new TypeScriptKnownClass("Path", "./system/io/path"));
            AddRequirement(new TypeScriptKnownClass("SearchOption", "./system/io/search-option"));
            AddRequirement(new TypeScriptKnownClass("StreamReader", "./system/io/stream-reader"));
            AddRequirement(new TypeScriptKnownClass("TextWriter", "./system/io/text-writer"));
            AddRequirement(new TypeScriptKnownClass("DriveInfo", "./system/io/drive-info"));

            // system.net.sockets
            AddRequirement(new TypeScriptKnownClass("TcpClient", "./system/net/sockets/tcp-client"));
            AddRequirement(new TypeScriptKnownClass("TcpListener", "./system/net/sockets/tcp-listener"));

            // system.reflection
            AddRequirement(new TypeScriptKnownClass("Assembly", "./system/reflection/assembly"));
            AddRequirement(new TypeScriptKnownClass("AssemblyName", "./system/reflection/assembly-name"));

            // system.security.cryptography
            AddRequirement(new TypeScriptKnownClass("SHA256", "./system/security/cryptography/sha256"));
            AddRequirement(new TypeScriptKnownClass("MD5", "./system/security/cryptography/md5"));
            AddRequirement(new TypeScriptKnownClass("AesGcm", "./system/security/cryptography/aes-gcm"));
            AddRequirement(new TypeScriptKnownClass("HashAlgorithm", "./system/security/cryptography/hash-algorithm"));
            AddRequirement(new TypeScriptKnownClass("HMACSHA256", "./system/security/cryptography/hmac-sha256"));
            AddRequirement(new TypeScriptKnownClass("Rfc2898DeriveBytes", "./system/security/cryptography/rfc-2898-derive-bytes"));
            AddRequirement(new TypeScriptKnownClass("HashAlgorithmName", "./system/security/cryptography/hash-algorithm-name"));
            AddRequirement(new TypeScriptKnownClass("RandomNumberGenerator", "./system/security/cryptography/random-number-generator"));
            AddRequirement(new TypeScriptKnownClass("WebCryptoUtil", "./system/security/cryptography/web-crypto"));
            AddRequirement(new TypeScriptKnownClass("ECParameters", "./system/security/cryptography/ec-parameters"));
            AddRequirement(new TypeScriptKnownClass("ECDiffieHellman", "./system/security/cryptography/ec-diffie-helman"));
            AddRequirement(new TypeScriptKnownClass("ECDiffieHellmanPublicKey", "./system/security/cryptography/ec-diffie-helman-public-key"));
            AddRequirement(new TypeScriptKnownClass("ECCurve", "./system/security/cryptography/ec-curve"));
            AddRequirement(new TypeScriptKnownClass("ECCurveType", "./system/security/cryptography/ec-curve-type"));
            AddRequirement(new TypeScriptKnownClass("ECPoint", "./system/security/cryptography/ec-point"));
            AddRequirement(new TypeScriptKnownClass("ECDsa", "./system/security/cryptography/ecdsa"));
            AddRequirement(new TypeScriptKnownClass("CryptographicException", "./system/security/cryptography/cryptographic-exception"));

            // system.text
            AddRequirement(new TypeScriptKnownClass("Encoding", "./system/text/encoding"));

            // system.threading
            AddRequirement(new TypeScriptKnownClass("Thread", "./system/threading/thread"));
            AddRequirement(new TypeScriptKnownClass("AutoResetEvent", "./system/threading/auto-reset-event"));
            AddRequirement(new TypeScriptKnownClass("SynchronizationContext", "./system/threading/synchronization-context", "", true));
            AddRequirement(new TypeScriptKnownClass("SendOrPostCallback", "./system/threading/send-or-post-callback"));

            // system.threading.tasks
            AddRequirement(new TypeScriptKnownClass("Task", "./system/threading/tasks/task", "", true));

            // WebSocketSharp
            AddRequirement(new TypeScriptKnownClass("WebSocketWS", "./websocketsharp/websocket"));
            AddRequirement(new TypeScriptKnownClass("WebSocketState", "./websocketsharp/websocket-state"));
            AddRequirement(new TypeScriptKnownClass("ErrorEventArgs", "./websocketsharp/error-event-args"));
            AddRequirement(new TypeScriptKnownClass("MessageEventArgs", "./websocketsharp/message-event-args"));

            // Blake2B
            AddRequirement(new TypeScriptKnownClass("Blake2b", "./blake2fast/blake2b"));

            // Newtonsoft.Json
            AddRequirement(new TypeScriptKnownClass("JsonConvert", "./newtonsoft.json/jsonconvert"));

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

        /// <summary>
        /// NodeJS-specific environment shims.
        /// </summary>
        private void addNode() {
            AddRequirement(new TypeScriptKnownClass("NodeDirectory", "./system/io/node-directory", "Directory"));
            AddRequirement(new TypeScriptKnownClass("File", "./system/io/node-file"));
            AddRequirement(new TypeScriptKnownClass("FileStream", "./system/io/node-file-stream"));
        }

        /// <summary>
        /// Web-specific environment shims.
        /// </summary>
        private void addWeb() {
            AddRequirement(new TypeScriptKnownClass("WebDirectory", "./system/io/web-directory", "Directory"));
            AddRequirement(new TypeScriptKnownClass("File", "./system/io/file"));
            AddRequirement(new TypeScriptKnownClass("FileStream", "./system/io/file-stream"));
        }

        /// <summary>
        /// Creates a native (non-generated) class placeholder with common members.
        /// </summary>
        private ConversionClass makeClass(string name) {
            ConversionClass cl = new ConversionClass();
            cl.Name = name;
            cl.IsNative = true;

            makeTypeScriptFunction("ToString", "toString", cl);

            return cl;
        }

        /// <summary>
        /// Adds a remapped function to a native class placeholder.
        /// </summary>
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

        /// <summary>
        /// Adds a remapped variable to a native class placeholder.
        /// </summary>
        private void makeTypeScriptVariable(string name, string remap, ConversionClass cl, string type) {
            ConversionVariable fnToString = new ConversionVariable();
            fnToString.Name = name;
            fnToString.Remap = remap;
            fnToString.VarType = VariableUtil.GetVarType(type);
            cl.Variables.Add(fnToString);
        }

        /// <summary>
        /// Registers native TS prototypes/methods that emulate .NET members (e.g., Array, string, Math).
        /// </summary>
        private void buildNativeRemap() {
            ConversionClass clArray = makeClass("Array");
            Classes.Add(clArray);
            makeTypeScriptVariable("Length", "length", clArray, "int");
            makeTypeScriptFunction("Copy", "copy", clArray, "", "NativeArrayUtil");
            makeTypeScriptFunction("SequenceEqual", "sequenceEqual", clArray, "", "NativeArrayUtil");

            ConversionClass clnumber = makeClass("number");
            Classes.Add(clnumber);

            ConversionClass clNumber = makeClass("Number");
            makeTypeScriptFunction("MaxValue", "MAX_VALUE", clNumber, "int");
            Classes.Add(clNumber);

            ConversionClass clUint = makeClass("uint");
            makeTypeScriptFunction("Parse", "parse", clUint, "uint");
            Classes.Add(clUint);

            ConversionClass clString = makeClass("string");
            makeTypeScriptFunction("Length", "length", clString, "int");
            makeTypeScriptFunction("IndexOf", "indexOf", clString, "string");
            makeTypeScriptFunction("Replace", "replace", clString, "string");
            makeTypeScriptFunction("Remove", "slice", clString, "string");
            makeTypeScriptFunction("StartsWith", "startsWith", clString, "string");
            makeTypeScriptFunction("Split", "split", clString, "string");
            makeTypeScriptFunction("Substring", "substring", clString, "string");
            makeTypeScriptFunction("IsNullOrEmpty", "isNullOrEmpty", clString, "string");
            Classes.Add(clString);

            ConversionClass clStringUpper = makeClass("String");
            makeTypeScriptFunction("IsNullOrEmpty", "isNullOrEmpty", clStringUpper, "string", "NativeStringUtil");
            Classes.Add(clStringUpper);

            ConversionClass clBool = makeClass("boolean");
            Classes.Add(clBool);

            ConversionClass clMath = makeClass("Math");
            makeTypeScriptFunction("Round", "round", clMath, "int");
            Classes.Add(clMath);

            ConversionClass clUint8Array = makeClass("Uint8Array");
            makeTypeScriptVariable("Length", "length", clUint8Array, "int");
            Classes.Add(clUint8Array);
        }

        /// <summary>
        /// Ensures .net.ts dependencies are installed, then runs the symbol extractor to generate JSON
        /// that describes available TS classes/interfaces/enums. This JSON is consumed to populate Requirements.
        /// </summary>
        private void buildDotNetData() {
            string startFolder = AssemblyUtil.GetStartFolder();
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            string dotNetFolder = Path.Combine(startFolder, ".net.ts");
            if (!Directory.Exists(dotNetFolder)) {
                dotNetFolder = Path.Combine(assemblyFolder, ".net.ts");
            }
            string extractorPath = Path.Combine(dotNetFolder, "extractor.js");

            if (!Directory.Exists(dotNetFolder)) {
                throw new DirectoryNotFoundException($"Expected TypeScript runtime at '{dotNetFolder}'.");
            }

            string typesDir = Path.Combine(dotNetFolder, "node_modules", "typescript");
            if (!Directory.Exists(typesDir)) {
                string packageJson = Path.Combine(dotNetFolder, "package.json");
                if (!File.Exists(packageJson)) {
                    throw new FileNotFoundException($"Expected package.json in '{dotNetFolder}'.");
                }

                Console.WriteLine("-- npm install");
                ProcessStartInfo npmInfo;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    npmInfo = new ProcessStartInfo("cmd.exe", "/c npm install");
                } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    npmInfo = new ProcessStartInfo("npm", "install");
                } else {
                    throw new PlatformNotSupportedException("Unsupported operating system.");
                }

                npmInfo.WorkingDirectory = dotNetFolder;
                npmInfo.UseShellExecute = false;
                npmInfo.CreateNoWindow = true;
                npmInfo.RedirectStandardOutput = true;
                npmInfo.RedirectStandardError = true;

                using (var npmProcess = Process.Start(npmInfo)) {
                    if (npmProcess == null) {
                        throw new InvalidOperationException("npm install failed to start (npm not found?).");
                    }

                    npmProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine(e.Data); };
                    npmProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine(e.Data); };
                    npmProcess.BeginOutputReadLine();
                    npmProcess.BeginErrorReadLine();

                    if (!npmProcess.WaitForExit((int)TimeSpan.FromMinutes(3).TotalMilliseconds)) {
                        try { npmProcess.Kill(); } catch { }
                        throw new TimeoutException("npm install timed out after 3 minutes.");
                    }

                    if (npmProcess.ExitCode != 0) {
                        throw new InvalidOperationException($"npm install exited with code {npmProcess.ExitCode}.");
                    }
                }
            }

            if (!File.Exists(extractorPath)) {
                throw new FileNotFoundException($"extractor.js not found in '{dotNetFolder}'.");
            }

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

            if (!string.IsNullOrWhiteSpace(output)) {
                Console.WriteLine(output);
            }
            if (!string.IsNullOrWhiteSpace(outputError)) {
                Console.WriteLine(outputError);
            }

            if (process.ExitCode != 0) {
                throw new InvalidOperationException($"Metadata extractor exited with code {process.ExitCode}.");
            }
        }
        /// <summary>
        /// Maps common .NET primitive types to TypeScript equivalents.
        /// </summary>
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



