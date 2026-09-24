namespace cs2.cpp;

/// <summary>
/// Writes the isolated native-imports translation unit for a P/Invoke plan. The header declares the native mirror
/// structs and the namespaced forwarder functions generated code calls; it only includes portable standard headers and
/// the calling-convention runtime header, so it never drags platform headers such as <c>Windows.h</c> into generated
/// code. The source declares the raw <c>extern "C"</c> native prototypes and defines each forwarder as a direct call to
/// its symbol, and is compiled as its own object outside the unity build.
/// </summary>
public static class CPPNativeImportsWriter {
    /// <summary>
    /// Gets the output-relative folder that holds the native-imports header and source.
    /// </summary>
    public const string FolderName = "native_imports";

    /// <summary>
    /// Gets the file name of the generated native-imports header.
    /// </summary>
    public const string HeaderFileName = "native_imports.hpp";

    /// <summary>
    /// Gets the file name of the generated native-imports forwarder source.
    /// </summary>
    public const string SourceFileName = "native_imports.cpp";

    /// <summary>
    /// Gets the banner written as the first line of both generated files.
    /// </summary>
    const string Banner = "// Generated direct P/Invoke forwarders. Do not edit.";

    /// <summary>
    /// Gets the indentation used for members nested in a namespace or struct body.
    /// </summary>
    const string Indent = "    ";

    /// <summary>
    /// Writes the native-imports header, and the forwarder source when the plan has at least one import, into
    /// <c>&lt;outputFolder&gt;/native_imports/</c>. Writes nothing when the plan has neither imports nor mirror structs.
    /// </summary>
    /// <param name="outputFolder">Root output folder for the generated C++ project.</param>
    /// <param name="plan">P/Invoke plan whose mirrors and forwarders are emitted.</param>
    /// <returns>The paths of every file written, or an empty list when nothing was written.</returns>
    /// <exception cref="ArgumentException"><paramref name="outputFolder"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is null.</exception>
    public static IReadOnlyList<string> Write(string outputFolder, CPPPInvokePlan plan) {
        if (string.IsNullOrWhiteSpace(outputFolder)) {
            throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
        }
        if (plan == null) {
            throw new ArgumentNullException(nameof(plan));
        }

        List<string> writtenFiles = new List<string>();
        if (!plan.HasNativeImports && plan.MirrorStructs.Count == 0) {
            return writtenFiles;
        }

        string folder = Path.Combine(outputFolder, FolderName);
        Directory.CreateDirectory(folder);

        string headerPath = Path.Combine(folder, HeaderFileName);
        File.WriteAllText(headerPath, BuildHeaderText(plan));
        writtenFiles.Add(headerPath);

        if (plan.HasNativeImports) {
            string sourcePath = Path.Combine(folder, SourceFileName);
            File.WriteAllText(sourcePath, BuildSourceText(plan));
            writtenFiles.Add(sourcePath);
        }
        return writtenFiles;
    }

    /// <summary>
    /// Builds the header text: include guard, portable includes, mirror structs in the plan's nested-first order, the
    /// <c>he_pinvoke_bit_copy</c> helper, and one namespace block of forwarder declarations per native library.
    /// </summary>
    /// <param name="plan">P/Invoke plan to emit.</param>
    /// <returns>The complete header text.</returns>
    static string BuildHeaderText(CPPPInvokePlan plan) {
        List<string> lines = new List<string> {
            Banner,
            "#ifndef HE_PINVOKE_NATIVE_IMPORTS_HPP",
            "#define HE_PINVOKE_NATIVE_IMPORTS_HPP",
            string.Empty,
            "#include <cstdint>",
            "#include <cstring>",
            string.Empty,
            "#include \"../runtime/native_calling_convention.hpp\"",
            string.Empty
        };

        foreach (CPPPInvokeMirrorStruct mirror in plan.MirrorStructs) {
            AppendMirrorStruct(lines, mirror);
            lines.Add(string.Empty);
        }

        lines.Add("template <typename TTo, typename TFrom>");
        lines.Add("inline TTo he_pinvoke_bit_copy(const TFrom& value) {");
        lines.Add(Indent + "static_assert(sizeof(TTo) == sizeof(TFrom), \"he_pinvoke_bit_copy requires layout-compatible types.\");");
        lines.Add(Indent + "TTo result;");
        lines.Add(Indent + "std::memcpy(&result, &value, sizeof(TTo));");
        lines.Add(Indent + "return result;");
        lines.Add("}");
        lines.Add(string.Empty);

        foreach (IGrouping<string, CPPPInvokeImport> library in plan.Imports.GroupBy(import => import.LibraryNamespace, StringComparer.Ordinal)) {
            lines.Add("namespace he_pinvoke::" + library.Key + " {");
            foreach (CPPPInvokeImport import in library) {
                lines.Add(Indent + BuildForwarderSignature(import) + ";");
            }
            lines.Add("}");
            lines.Add(string.Empty);
        }

        lines.Add("#endif");
        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Builds the source text: the raw <c>extern "C"</c> prototype of every distinct native symbol, followed by one
    /// namespace block per native library whose forwarders call those symbols directly.
    /// </summary>
    /// <param name="plan">P/Invoke plan to emit; must contain at least one import.</param>
    /// <returns>The complete source text.</returns>
    static string BuildSourceText(CPPPInvokePlan plan) {
        List<string> lines = new List<string> {
            Banner,
            "#include \"" + HeaderFileName + "\"",
            string.Empty
        };

        foreach (string prototype in plan.Imports.Select(BuildExternPrototype).Distinct(StringComparer.Ordinal)) {
            lines.Add(prototype);
        }

        foreach (IGrouping<string, CPPPInvokeImport> library in plan.Imports.GroupBy(import => import.LibraryNamespace, StringComparer.Ordinal)) {
            lines.Add(string.Empty);
            lines.Add("namespace he_pinvoke::" + library.Key + " {");
            bool first = true;
            foreach (CPPPInvokeImport import in library) {
                if (!first) {
                    lines.Add(string.Empty);
                }
                first = false;
                lines.Add(Indent + BuildForwarderSignature(import) + " {");
                lines.Add(Indent + Indent + BuildForwarderCall(import));
                lines.Add(Indent + "}");
            }
            lines.Add("}");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Appends one mirror struct definition, wrapped in <c>#pragma pack</c> push/pop when the struct declares an
    /// explicit packing.
    /// </summary>
    /// <param name="lines">Header lines being built.</param>
    /// <param name="mirror">Mirror struct to emit.</param>
    static void AppendMirrorStruct(List<string> lines, CPPPInvokeMirrorStruct mirror) {
        if (mirror.Pack > 0) {
            lines.Add("#pragma pack(push, " + mirror.Pack + ")");
        }
        lines.Add("struct " + mirror.MirrorName + " {");
        foreach (CPPPInvokeMirrorField field in mirror.Fields) {
            lines.Add(Indent + CPPPInvokeDeclaratorFormatter.Format(field.MirrorTypeText, CPPIdentifierSanitizer.SanitizeIdentifier(field.Name)) + ";");
        }
        lines.Add("};");
        if (mirror.Pack > 0) {
            lines.Add("#pragma pack(pop)");
        }
    }

    /// <summary>
    /// Builds the forwarder's unqualified signature (return type, entry-point name, and named parameter list), shared by
    /// the header declaration and the source definition.
    /// </summary>
    /// <param name="import">Import whose forwarder signature is built.</param>
    /// <returns>The signature text without a trailing semicolon or body.</returns>
    static string BuildForwarderSignature(CPPPInvokeImport import) {
        return import.Signature.ReturnType.MirrorTypeText + " " + import.EntryPoint + "(" + BuildParameterList(import.Signature) + ")";
    }

    /// <summary>
    /// Builds the raw <c>extern "C"</c> prototype of the native symbol an import calls, including its calling-convention
    /// macro.
    /// </summary>
    /// <param name="import">Import whose native prototype is built.</param>
    /// <returns>The prototype line.</returns>
    static string BuildExternPrototype(CPPPInvokeImport import) {
        CPPPInvokeSignature signature = import.Signature;
        return "extern \"C\" " + signature.ReturnType.MirrorTypeText + " " + signature.CallingConventionMacro + " " + import.EntryPoint + "(" + BuildParameterList(signature) + ");";
    }

    /// <summary>
    /// Builds the forwarder body statement that calls the global native symbol with every parameter, returning its
    /// result unless the signature returns <c>void</c>.
    /// </summary>
    /// <param name="import">Import whose forwarder body is built.</param>
    /// <returns>The single body statement.</returns>
    static string BuildForwarderCall(CPPPInvokeImport import) {
        string arguments = string.Join(", ", import.Signature.Parameters.Select(parameter => CPPIdentifierSanitizer.SanitizeIdentifier(parameter.Name)));
        string call = "::" + import.EntryPoint + "(" + arguments + ");";
        return import.Signature.ReturnType.Kind == CPPPInvokeValueKind.Void ? call : "return " + call;
    }

    /// <summary>
    /// Builds a comma-separated named parameter list, using each parameter's ref-lowered type text and its sanitized
    /// managed name.
    /// </summary>
    /// <param name="signature">Signature whose parameters are listed.</param>
    /// <returns>The parameter list text without surrounding parentheses.</returns>
    static string BuildParameterList(CPPPInvokeSignature signature) {
        return string.Join(", ", signature.Parameters.Select(parameter => CPPPInvokeDeclaratorFormatter.Format(parameter.MirrorParameterText, CPPIdentifierSanitizer.SanitizeIdentifier(parameter.Name))));
    }
}
