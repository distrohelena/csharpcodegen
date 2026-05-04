using cs2.core.symbols;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace cs2.cpp.doxygen;

public static class DoxygenUtil {
    const string DoxygenPathEnvironmentVariable = "CS2_DOXYGEN_PATH";
    const string LegacyDoxygenPathEnvironmentVariable = "DOXYGEN_PATH";

    public static void GenerateDoxygenConfig(string projectDir, string outputDir) {
        var config = $@"INPUT = ""{projectDir}""
RECURSIVE = YES
EXTRACT_ALL = YES
GENERATE_XML = YES
GENERATE_HTML = NO
GENERATE_LATEX = NO
QUIET = YES
OUTPUT_DIRECTORY = ""{outputDir}""
XML_OUTPUT = xml
FILE_PATTERNS = *.cpp *.h *.hpp *.cxx
EXCLUDE_PATTERNS = *.*pp_* *.bak";

        File.WriteAllText(Path.Combine(outputDir, "doxygen.config"), config);
    }

    public static void RunDoxygen(string configPath) {
        string doxygenPath = ResolveDoxygenPath();
        using var process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = doxygenPath,
                Arguments = $"\"{configPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        if (!process.Start()) {
            throw new Exception($"Failed to start Doxygen at '{doxygenPath}'.");
        }

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(output)) {
            Console.WriteLine(output);
        }
        if (!string.IsNullOrWhiteSpace(error)) {
            Console.Error.WriteLine(error);
        }

        if (process.ExitCode != 0)
            throw new Exception($"Doxygen failed to run from '{doxygenPath}' with exit code {process.ExitCode}.");
    }

    static string ResolveDoxygenPath() {
        string? resolvedPath =
            ResolvePathFromEnvironment(DoxygenPathEnvironmentVariable)
            ?? ResolvePathFromEnvironment(LegacyDoxygenPathEnvironmentVariable)
            ?? ResolveBundledPath()
            ?? ResolvePathFromSystemPath()
            ?? ResolveCommonInstallationPath();

        if (!string.IsNullOrWhiteSpace(resolvedPath)) {
            return resolvedPath;
        }

        throw new FileNotFoundException(
            $"Could not locate the Doxygen executable. Set '{DoxygenPathEnvironmentVariable}' or '{LegacyDoxygenPathEnvironmentVariable}', place doxygen next to the codegen executable, or install doxygen on PATH.");
    }

    static string? ResolvePathFromEnvironment(string environmentVariableName) {
        string? candidatePath = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(candidatePath)) {
            return null;
        }

        candidatePath = candidatePath.Trim('"');
        return File.Exists(candidatePath) ? Path.GetFullPath(candidatePath) : null;
    }

    static string? ResolveBundledPath() {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidatePaths = OperatingSystem.IsWindows()
            ? [
                Path.Combine(baseDirectory, "doxygen.exe"),
                Path.Combine(baseDirectory, "tools", "doxygen.exe"),
                Path.Combine(baseDirectory, "doxygen", "bin", "doxygen.exe")
            ]
            : [
                Path.Combine(baseDirectory, "doxygen"),
                Path.Combine(baseDirectory, "tools", "doxygen"),
                Path.Combine(baseDirectory, "doxygen", "bin", "doxygen")
            ];

        for (int index = 0; index < candidatePaths.Length; index++) {
            if (File.Exists(candidatePaths[index])) {
                return Path.GetFullPath(candidatePaths[index]);
            }
        }

        return null;
    }

    static string? ResolvePathFromSystemPath() {
        string? pathEnvironment = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnvironment)) {
            return null;
        }

        string[] pathEntries = pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] executableNames = OperatingSystem.IsWindows()
            ? ["doxygen.exe", "doxygen.cmd", "doxygen.bat", "doxygen"]
            : ["doxygen"];

        for (int entryIndex = 0; entryIndex < pathEntries.Length; entryIndex++) {
            string pathEntry = pathEntries[entryIndex];
            for (int executableIndex = 0; executableIndex < executableNames.Length; executableIndex++) {
                string candidatePath = Path.Combine(pathEntry, executableNames[executableIndex]);
                if (File.Exists(candidatePath)) {
                    return Path.GetFullPath(candidatePath);
                }
            }
        }

        return null;
    }

    static string? ResolveCommonInstallationPath() {
        if (!OperatingSystem.IsWindows()) {
            return null;
        }

        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string[] roots = [
            Path.Combine(programFiles ?? string.Empty, "doxygen", "bin", "doxygen.exe"),
            Path.Combine(programFiles ?? string.Empty, "Doxygen", "bin", "doxygen.exe"),
            Path.Combine(programFilesX86 ?? string.Empty, "doxygen", "bin", "doxygen.exe"),
            Path.Combine(programFilesX86 ?? string.Empty, "Doxygen", "bin", "doxygen.exe")
        ];

        for (int index = 0; index < roots.Length; index++) {
            if (File.Exists(roots[index])) {
                return Path.GetFullPath(roots[index]);
            }
        }

        return null;
    }

    public static Dictionary<string, List<Symbol>> ParseEntireProject(string xmlDir, string projectRoot) {
        var symbolsByFile = new Dictionary<string, List<Symbol>>();
        var indexXml = Path.Combine(xmlDir, "index.xml");

        foreach (var compound in XDocument.Load(indexXml).Descendants("compound")) {
            var refid = compound.Attribute("refid")?.Value;
            if (!string.IsNullOrEmpty(refid)) {
                var compoundXml = Path.Combine(xmlDir, $"{refid}.xml");
                if (!File.Exists(compoundXml))
                    continue;

                var compoundDoc = XDocument.Load(compoundXml);
                var compoundDef = compoundDoc.Descendants("compounddef").FirstOrDefault();
                if (compoundDef == null)
                    continue;

                var locFile = compoundDef.Element("location")?.Attribute("file")?.Value;
                if (string.IsNullOrEmpty(locFile))
                    continue;

                var fullPath = Path.GetFullPath(Path.Combine(projectRoot, locFile));
                if (!symbolsByFile.ContainsKey(fullPath)) {
                    symbolsByFile[fullPath] = new List<Symbol>();
                }

                ProcessCompound(compoundDef, symbolsByFile[fullPath]);
            }
        }

        return symbolsByFile;
    }

    private static void ProcessCompound(XElement compoundDef, List<Symbol> symbols) {
        var kind = compoundDef.Attribute("kind")?.Value.ToLowerInvariant();

        switch (kind) {
            case "file":
                ProcessFile(compoundDef, symbols);
                break;
            case "class":
            case "struct":
                ProcessClass(compoundDef, symbols);
                break;
            case "function":
                ProcessGlobalFunction(compoundDef, symbols);
                break;
            case "variable":
                ProcessGlobalVariable(compoundDef, symbols);
                break;
            case "enum":
                ProcessEnum(compoundDef, symbols);
                break;
        }
    }

    private static void ProcessFile(XElement compoundDef, List<Symbol> symbols) {
        foreach (var memberDef in compoundDef.Descendants("memberdef")) {
            var memberKind = memberDef.Attribute("kind")?.Value.ToLowerInvariant();

            switch (memberKind) {
                case "enum":
                    ProcessEnum(compoundDef, symbols);
                    break;
            }
        }
    }

    private static void ProcessClass(XElement compoundDef, List<Symbol> symbols) {
        var classSymbol = new Symbol {
            Type = compoundDef.Attribute("kind")?.Value.ToLowerInvariant(),
            Name = compoundDef.Element("compoundname")?.Value,
            Members = new List<ClassMember>()
        };

        foreach (var memberDef in compoundDef.Descendants("memberdef")) {
            var memberKind = memberDef.Attribute("kind")?.Value.ToLowerInvariant();
            var memberName = memberDef.Element("name")?.Value;

            switch (memberKind) {
                case "function":
                    ProcessMethod(memberDef, classSymbol.Members);
                    break;
                case "variable":
                    ProcessProperty(memberDef, classSymbol.Members);
                    break;
                case "enum":
                    ProcessEnum(memberDef, symbols); // Handle enum inside a class or struct
                    break;
            }
        }

        symbols.Add(classSymbol);
    }

    private static void ProcessGlobalFunction(XElement memberDef, List<Symbol> symbols) {
        var globalFunc = new Symbol {
            Type = "function",
            Name = memberDef.Element("name")?.Value,
            Parameters = memberDef.Descendants("param")
                .Select(p => new Parameter {
                    Name = p.Element("declname")?.Value,
                    Type = p.Element("type")?.Value?.Trim()
            })
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .ToList(),
            ReturnType = memberDef.Element("type")?.Value?.Trim() ?? "void"
        };

        symbols.Add(globalFunc);
    }

    private static void ProcessGlobalVariable(XElement memberDef, List<Symbol> symbols) {
        var globalVar = new Symbol {
            Type = "variable",
            Name = memberDef.Element("name")?.Value,
            VariableType = memberDef.Element("type")?.Value?.Trim() ?? "var"
        };

        symbols.Add(globalVar);
    }

    private static void ProcessMethod(XElement memberDef, List<ClassMember> members) {
        var method = new ClassMember {
            Type = "method",
            Name = memberDef.Element("name")?.Value,
            Parameters = memberDef.Descendants("param")
                .Select(p => new Parameter {
                    Name = p.Element("declname")?.Value,
                    Type = p.Element("type")?.Value?.Trim()
            })
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .ToList(),
            ReturnType = memberDef.Element("type")?.Value?.Trim() ?? "void"
        };

        members.Add(method);
    }

    private static void ProcessProperty(XElement memberDef, List<ClassMember> members) {
        var property = new ClassMember {
            Type = "property",
            Name = memberDef.Element("name")?.Value,
            PropertyType = memberDef.Element("type")?.Value?.Trim() ?? "var"
        };

        members.Add(property);
    }

    private static void ProcessEnum(XElement sectionDef, List<Symbol> symbols) {
        foreach (var memberDef in sectionDef.Descendants("memberdef")) {
            var enumSymbol = new Symbol {
                Type = "enum",
                Name = memberDef.Element("name")?.Value
            };
            symbols.Add(enumSymbol);

            foreach (var enumValue in memberDef.Descendants("enumvalue")) {
                var enumValueName = enumValue.Element("name")?.Value;
                var enumValueValue = enumValue.Element("initializer")?.Value?.Trim() ?? string.Empty;

                if (!string.IsNullOrEmpty(enumValueName)) {
                    enumSymbol.Members.Add(new ClassMember {
                        Name = enumValueName,
                        Value = enumValueValue
                    });
                }
            }
        }
    }
}
