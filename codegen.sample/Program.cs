using cs2.core;
using cs2.cpp;
using cs2.ts;
using Microsoft.Build.Locator;
using System.Runtime.InteropServices;

namespace codegen.sample {
    internal class Program {
        static void Main(string[] args) {
            Console.WriteLine("---- C# to TypeScript Runner ----");

            // Initialize MSBuild to locate the proper build tools
            MSBuildLocator.RegisterDefaults();

            string basePath;
            string rootPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                basePath = Environment.CurrentDirectory.Replace("codegen.sample\\bin\\Debug\\net9.0", "");
                rootPath = basePath.Replace("\\backend\\", "");
            } else {
                basePath = Environment.CurrentDirectory.Replace("codegen.sample/bin/Debug/net9.0", "");
                rootPath = basePath.Replace("/backend/", "");
            }

            Console.WriteLine($"Base Path: {basePath}");
            Console.WriteLine($"Root Folder: {rootPath}");

            string sourceTestProj = Path.Combine(basePath, "codegen.testproj");
            string outputTsFolder = Path.Combine(Environment.CurrentDirectory, "output.ts");
            string outputCppFolder = Path.Combine(Environment.CurrentDirectory, "output.cpp");

            Console.WriteLine($"Folder: {sourceTestProj}");
            Console.WriteLine($"Output folder: {outputTsFolder}");

            CPPConversionRules rules = new CPPConversionRules();

            rules.IgnoredNamespaces.Add("WebSocketSharp");
            rules.IgnoredNamespaces.Add("SocketSharp");
            rules.IgnoredNamespaces.Add("Nucleus.Web");

            rules.IgnoredClasses.Add("AsyncUtil");
            rules.IgnoredClasses.Add("CmdUtil");
            rules.IgnoredClasses.Add("FileSystemRouteHandler");
            rules.IgnoredClasses.Add("UserManager");
            rules.IgnoredClasses.Add("AssemblyAttributes");

            TypeScriptCodeConverter converter = new TypeScriptCodeConverter(rules, TypeScriptEnvironment.Web);
            converter.AddCsproj(Path.Combine(sourceTestProj, "codegen.testproj.csproj"));
            converter.WriteFile(outputTsFolder, "codegen.testproj.ts");

            //CPPCodeConverter cppConverter = new CPPCodeConverter(rules);
            //cppConverter.AddCsproj(Path.Combine(sourceTestProj, "codegen.testproj.csproj"));
            ////cppConverter.AddCsproj("C:\\dev\\helengine\\engine\\helengine.core\\helengine.core.csproj");
            //cppConverter.WriteOutput(outputCppFolder);
        }
    }
}
