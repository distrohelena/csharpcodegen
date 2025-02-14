using cs2.core;
using cs2.core.symbols;
using cs2.cpp.doxygen;
using Nucleus;
using System.Diagnostics;

namespace cs2.cpp;

public class CPPProgram : ConversionProgram {
    public List<CPPKnownClass> Requirements { get; private set; }

    public CPPProgram(ConversionRules rules)
        : base(rules) {
        Requirements = new List<CPPKnownClass>();
    }

    public void AddDotNet() {
        //this.buildTypeMap();

        //this.buildNativeRemap();

        // system
        Requirements.Add(new CPPKnownClass("Action", "./system/action"));
        Requirements.Add(new CPPKnownClass("Console", "./system/console"));

        // system.io
        Requirements.Add(new CPPKnownClass("BinaryWriter", "./system/io/binary-writer"));
        Requirements.Add(new CPPKnownClass("BinaryReader", "./system/io/binary-reader"));
        Requirements.Add(new CPPKnownClass("File", "./system/io/file"));
        Requirements.Add(new CPPKnownClass("FileMode", "./system/io/file-mode"));
        Requirements.Add(new CPPKnownClass("FileStream", "./system/io/file-stream"));
        Requirements.Add(new CPPKnownClass("MemoryStream", "./system/io/memory-stream"));
        Requirements.Add(new CPPKnownClass("SeekOrigin", "./system/io/seek-origin"));
        Requirements.Add(new CPPKnownClass("Stream", "./system/io/stream"));

        this.buildDotNetData();

        for (int i = 0; i < Requirements.Count; i++) {
            CPPKnownClass requirement = Requirements[i];

            ConversionClass cl = new ConversionClass();
            cl.IsNative = true;
            cl.Name = requirement.Name;
            Classes.Add(cl);

            Symbol symbol = requirement.Symbol;

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

    private void buildDotNetData() {
        var tempDir = Path.Combine(Path.GetTempPath(), "cs2.cpp");
        Directory.CreateDirectory(tempDir);

        string startFolder = AssemblyUtil.GetStartFolder();
        string dotNetFolder = Path.Combine(startFolder, ".net.cpp");

        DoxygenUtil.GenerateDoxygenConfig(dotNetFolder, tempDir);
        DoxygenUtil.RunDoxygen(Path.Combine(tempDir, "doxygen.config"));

        // Get XML output directory
        var xmlOutputDir = Path.Combine(tempDir, "xml");

        // Call the ParseEntireProject method
        Dictionary<string, List<Symbol>> symbolsByFile = DoxygenUtil.ParseEntireProject(xmlOutputDir, dotNetFolder);

        foreach (var symbolPair in symbolsByFile) {
            if (!symbolPair.Key.EndsWith(".hpp")) {
                continue;
            }

            for (int i = 0; i < symbolPair.Value.Count; i++) {
                Symbol symbol = symbolPair.Value[i];
                CPPKnownClass known = Requirements.FirstOrDefault(c => c.Name == symbol.Name);
                known.Symbol = symbol;
            }

        }
    }
}

