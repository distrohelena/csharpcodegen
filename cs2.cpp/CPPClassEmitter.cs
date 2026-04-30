using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Emits C++ header and source declarations for a converted class using the current backend rules.
    /// </summary>
    public class CPPClassEmitter {
        readonly CPPConversiorProcessor processor;
        readonly CPPProgram program;

        /// <summary>
        /// Initializes a class emitter bound to the current processor and program state.
        /// </summary>
        /// <param name="processor">Processor used to lower method and accessor bodies.</param>
        /// <param name="program">Program model that resolves known C++ runtime types.</param>
        public CPPClassEmitter(CPPConversiorProcessor processor, CPPProgram program) {
            this.processor = processor;
            this.program = program;
        }

        /// <summary>
        /// Emits the full header and source representation for a converted type.
        /// </summary>
        /// <param name="conversionClass">The class, interface, or enum to emit.</param>
        /// <param name="headerWriter">Writer that receives the header output.</param>
        /// <param name="sourceWriter">Writer that receives the source output.</param>
        public void Emit(ConversionClass conversionClass, TextWriter headerWriter, TextWriter sourceWriter) {
            if (conversionClass == null) {
                throw new ArgumentNullException(nameof(conversionClass));
            }

            if (headerWriter == null) {
                throw new ArgumentNullException(nameof(headerWriter));
            }

            if (sourceWriter == null) {
                throw new ArgumentNullException(nameof(sourceWriter));
            }

            WriteHeaderPreamble(conversionClass, headerWriter);
            WriteSourcePreamble(conversionClass, sourceWriter);

            if (conversionClass.DeclarationType == MemberDeclarationType.Enum) {
                WriteEnum(conversionClass, headerWriter);
                return;
            }

            WriteClass(conversionClass, headerWriter, sourceWriter);
        }

        /// <summary>
        /// Writes the header preamble and include directives required by a converted type.
        /// </summary>
        /// <param name="conversionClass">The type being emitted.</param>
        /// <param name="headerWriter">Writer that receives the header preamble.</param>
        void WriteHeaderPreamble(ConversionClass conversionClass, TextWriter headerWriter) {
            headerWriter.WriteLine("#pragma once");
            headerWriter.WriteLine("#include <cstdint>");
            headerWriter.WriteLine();

            bool wroteInclude = false;
            HashSet<string> referencedTypes = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> forwardDeclaredTypes = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> excludedTypeNames = GetExcludedTypeNames(conversionClass);

            foreach (string extension in conversionClass.Extensions.Distinct(StringComparer.Ordinal)) {
                if (!excludedTypeNames.Contains(extension)) {
                    referencedTypes.Add(extension);
                }
            }

            foreach (string referencedClass in conversionClass.ReferencedClasses.Distinct(StringComparer.Ordinal)) {
                if (!excludedTypeNames.Contains(referencedClass)) {
                    referencedTypes.Add(referencedClass);
                }
            }

            AddSignatureTypeReferences(conversionClass, referencedTypes);

            foreach (string referencedType in referencedTypes) {
                if (TryResolveForwardDeclaration(conversionClass, referencedType, out string forwardDeclaration)) {
                    forwardDeclaredTypes.Add(forwardDeclaration);
                }
            }

            foreach (string forwardDeclaration in forwardDeclaredTypes) {
                headerWriter.WriteLine(forwardDeclaration);
            }

            if (forwardDeclaredTypes.Count > 0) {
                headerWriter.WriteLine();
            }

            foreach (string referencedType in referencedTypes) {
                if (WriteInclude(headerWriter, conversionClass, referencedType)) {
                    wroteInclude = true;
                }
            }

            if (wroteInclude) {
                headerWriter.WriteLine();
            }
        }

        /// <summary>
        /// Collects referenced type names from fields, properties, method return types, and parameters so header dependencies remain concrete.
        /// </summary>
        /// <param name="conversionClass">The type whose declared member signatures should be scanned.</param>
        /// <param name="referencedTypes">The destination set that receives discovered type names.</param>
        void AddSignatureTypeReferences(ConversionClass conversionClass, ISet<string> referencedTypes) {
            foreach (ConversionVariable variable in conversionClass.Variables) {
                AddTypeReference(variable.VarType, referencedTypes, conversionClass.GenericArgs);
            }

            foreach (ConversionFunction function in conversionClass.Functions) {
                HashSet<string> excludedTypeNames = GetExcludedTypeNames(conversionClass, function);
                AddTypeReference(function.ReturnType, referencedTypes, excludedTypeNames);

                foreach (ConversionVariable parameter in function.InParameters) {
                    AddTypeReference(parameter.VarType, referencedTypes, excludedTypeNames);
                }
            }
        }

        /// <summary>
        /// Adds the referenced type and any nested generic argument types to the include set.
        /// </summary>
        /// <param name="variableType">The type metadata to inspect.</param>
        /// <param name="referencedTypes">The destination set that receives discovered type names.</param>
        /// <param name="excludedTypeNames">Type names that should remain compile-time only and must not become includes.</param>
        void AddTypeReference(VariableType variableType, ISet<string> referencedTypes, IEnumerable<string> excludedTypeNames) {
            if (variableType == null) {
                return;
            }

            HashSet<string> excludedTypeNameSet = excludedTypeNames == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(excludedTypeNames, StringComparer.Ordinal);

            if (variableType.IsNullable) {
                referencedTypes.Add("Nullable");
            }

            string referencedTypeName = variableType.GenericArgs.Count > 0
                ? variableType.ToString()
                : variableType.TypeName;

            if (!string.IsNullOrWhiteSpace(referencedTypeName) &&
                !excludedTypeNameSet.Contains(referencedTypeName) &&
                !excludedTypeNameSet.Contains(variableType.TypeName)) {
                referencedTypes.Add(referencedTypeName);
            }

            if (variableType.GenericArgs == null) {
                return;
            }

            foreach (VariableType genericArgument in variableType.GenericArgs) {
                AddTypeReference(genericArgument, referencedTypes, excludedTypeNameSet);
            }
        }

        /// <summary>
        /// Builds the set of compile-time generic symbols that must not generate runtime or header dependencies.
        /// </summary>
        /// <param name="conversionClass">The class that owns the emitted members.</param>
        /// <param name="function">The function whose generic scope should be excluded.</param>
        /// <returns>A case-sensitive set of generic symbol names that remain compile-time only.</returns>
        static HashSet<string> GetExcludedTypeNames(ConversionClass conversionClass, ConversionFunction function) {
            HashSet<string> excludedTypeNames = new HashSet<string>(StringComparer.Ordinal);

            if (conversionClass.GenericArgs != null) {
                foreach (string genericArgument in conversionClass.GenericArgs) {
                    excludedTypeNames.Add(genericArgument);
                }
            }

            if (function.GenericParameters != null) {
                foreach (string genericParameter in function.GenericParameters) {
                    excludedTypeNames.Add(genericParameter);
                }
            }

            return excludedTypeNames;
        }

        /// <summary>
        /// Builds the set of compile-time generic symbols declared anywhere on the class surface so they never become includes.
        /// </summary>
        /// <param name="conversionClass">The class whose generic symbols should stay compile-time only.</param>
        /// <returns>A case-sensitive set of generic symbol names that must not generate includes.</returns>
        static HashSet<string> GetExcludedTypeNames(ConversionClass conversionClass) {
            HashSet<string> excludedTypeNames = new HashSet<string>(StringComparer.Ordinal);

            if (conversionClass.GenericArgs != null) {
                foreach (string genericArgument in conversionClass.GenericArgs) {
                    excludedTypeNames.Add(genericArgument);
                }
            }

            foreach (ConversionFunction function in conversionClass.Functions) {
                if (function.GenericParameters == null) {
                    continue;
                }

                foreach (string genericParameter in function.GenericParameters) {
                    excludedTypeNames.Add(genericParameter);
                }
            }

            return excludedTypeNames;
        }

        /// <summary>
        /// Writes a single include directive when the referenced type resolves to a concrete generated header.
        /// </summary>
        /// <param name="headerWriter">Writer that receives the include directive.</param>
        /// <param name="conversionClass">Type currently being emitted.</param>
        /// <param name="referencedClass">Referenced type name to resolve.</param>
        /// <returns><c>true</c> when an include directive was emitted; otherwise <c>false</c>.</returns>
        bool WriteInclude(TextWriter headerWriter, ConversionClass conversionClass, string referencedClass) {
            string normalizedReferencedClass = NormalizeReferencedClassName(referencedClass);
            string currentEmittedTypeName = conversionClass.GetEmittedTypeName();

            if (TryResolveGeneratedClass(referencedClass, out ConversionClass generatedClass) &&
                string.Equals(generatedClass.GetEmittedTypeName(), currentEmittedTypeName, StringComparison.Ordinal)) {
                return false;
            }

            if (string.Equals(referencedClass, currentEmittedTypeName, StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, currentEmittedTypeName, StringComparison.Ordinal)) {
                return false;
            }

            if (conversionClass.GenericArgs != null &&
                (conversionClass.GenericArgs.Contains(referencedClass, StringComparer.Ordinal) ||
                 conversionClass.GenericArgs.Contains(normalizedReferencedClass, StringComparer.Ordinal))) {
                return false;
            }

            string includePath = ResolveIncludePath(referencedClass);
            if (string.IsNullOrWhiteSpace(includePath)) {
                return false;
            }

            headerWriter.WriteLine($"#include \"{includePath}.hpp\"");
            return true;
        }

        /// <summary>
        /// Resolves whether a referenced type should be forward declared to break generated header cycles.
        /// </summary>
        /// <param name="conversionClass">The type currently being emitted.</param>
        /// <param name="referencedClass">Referenced type name to inspect.</param>
        /// <param name="forwardDeclaration">Receives the forward declaration text when a generated type declaration should be emitted.</param>
        /// <returns><c>true</c> when a generated type forward declaration should be emitted; otherwise <c>false</c>.</returns>
        bool TryResolveForwardDeclaration(ConversionClass conversionClass, string referencedClass, out string forwardDeclaration) {
            forwardDeclaration = string.Empty;

            if (!TryResolveGeneratedClass(referencedClass, out ConversionClass generatedClass)) {
                return false;
            }

            string generatedTypeName = generatedClass.GetEmittedTypeName();
            if (string.Equals(generatedTypeName, conversionClass.GetEmittedTypeName(), StringComparison.Ordinal)) {
                return false;
            }

            if (generatedClass.DeclarationType == MemberDeclarationType.Enum) {
                return false;
            }

            if (generatedClass.GenericArgs != null && generatedClass.GenericArgs.Count > 0) {
                string templateParameters = string.Join(", ", generatedClass.GenericArgs.Select(static genericArgument => $"typename {genericArgument}"));
                forwardDeclaration = $"template <{templateParameters}> class {generatedTypeName};";
                return true;
            }

            forwardDeclaration = $"class {generatedTypeName};";
            return true;
        }

        /// <summary>
        /// Resolves a referenced type name back to a generated class model when the type belongs to the converted program.
        /// </summary>
        /// <param name="referencedClass">Referenced type name to inspect.</param>
        /// <param name="generatedClass">Receives the matching generated class when found.</param>
        /// <returns><c>true</c> when the reference resolves to a generated class; otherwise <c>false</c>.</returns>
        bool TryResolveGeneratedClass(string referencedClass, out ConversionClass generatedClass) {
            generatedClass = null;

            if (string.IsNullOrWhiteSpace(referencedClass)) {
                return false;
            }

            VariableType variableType = VariableUtil.GetVarType(referencedClass);
            generatedClass = program.FindGeneratedClass(variableType.TypeName, variableType.GenericArgs.Count);
            if (generatedClass != null) {
                return true;
            }

            string normalizedReferencedClass = NormalizeReferencedClassName(referencedClass);
            generatedClass = program.Classes.FirstOrDefault(candidate =>
                !candidate.IsNative &&
                string.Equals(candidate.GetEmittedTypeName(), referencedClass, StringComparison.Ordinal));

            if (generatedClass != null) {
                return true;
            }

            generatedClass = program.Classes.FirstOrDefault(candidate =>
                !candidate.IsNative &&
                string.Equals(candidate.GetEmittedTypeName(), normalizedReferencedClass, StringComparison.Ordinal));
            return generatedClass != null;
        }

        /// <summary>
        /// Writes the source preamble that binds a converted implementation file to its header.
        /// </summary>
        /// <param name="conversionClass">The type being emitted.</param>
        /// <param name="sourceWriter">Writer that receives the source preamble.</param>
        void WriteSourcePreamble(ConversionClass conversionClass, TextWriter sourceWriter) {
            sourceWriter.WriteLine($"#include \"{conversionClass.GetEmittedTypeName()}.hpp\"");
            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Resolves the include path for a referenced class using known runtime metadata when available.
        /// </summary>
        /// <param name="referencedClass">The referenced class name as discovered during conversion.</param>
        /// <returns>The include path without extension.</returns>
        string ResolveIncludePath(string referencedClass) {
            if (string.IsNullOrWhiteSpace(referencedClass) || referencedClass == "var" || referencedClass == "?") {
                return string.Empty;
            }

            string normalizedReferencedClass = NormalizeReferencedClassName(referencedClass);
            VariableType referencedType = VariableUtil.GetVarType(referencedClass);
            string referencedTypeName = referencedType.TypeName;

            if (string.Equals(referencedClass, "Array", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Array", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Array", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Array", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeArray");
                return "runtime/array";
            }

            if (referencedType.Type == VariableDataType.Tuple ||
                string.Equals(referencedTypeName, "ValueTuple", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeTuple");
                return "runtime/native_tuple";
            }

            if (string.Equals(referencedClass, "Span", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Span", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Span", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Span", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "ReadOnlySpan", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.ReadOnlySpan", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "ReadOnlySpan", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "ReadOnlySpan", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeSpan");
                return "runtime/native_span";
            }

            if (string.Equals(referencedClass, "Nullable", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Nullable", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Nullable", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Nullable", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeNullable");
                return "runtime/native_nullable";
            }

            if (string.Equals(referencedClass, "IDisposable", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.IDisposable", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "IDisposable", StringComparison.Ordinal)) {
                if (processor != null) {
                    CPPTypeData runtimeTypeData;
                    processor.ConvertToCPPType(VariableUtil.GetVarType(normalizedReferencedClass), out runtimeTypeData);
                }

                return "runtime/native_disposable";
            }

            if (string.Equals(referencedClass, "IEquatable", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.IEquatable", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "IEquatable", StringComparison.Ordinal)) {
                if (processor != null) {
                    CPPTypeData runtimeTypeData;
                    processor.ConvertToCPPType(VariableUtil.GetVarType(normalizedReferencedClass), out runtimeTypeData);
                }

                return "runtime/native_equatable";
            }

            if (string.Equals(referencedClass, "Type", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Type", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Type", StringComparison.Ordinal)) {
                if (processor != null) {
                    CPPTypeData runtimeTypeData;
                    processor.ConvertToCPPType(VariableUtil.GetVarType(normalizedReferencedClass), out runtimeTypeData);
                }

                return "runtime/native_type";
            }

            if (string.Equals(referencedClass, "DateTime", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.DateTime", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "DateTime", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "DateTime", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "TimeSpan", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.TimeSpan", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "TimeSpan", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "TimeSpan", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeDateTime");
                return "runtime/native_datetime";
            }

            if (string.Equals(referencedClass, "StringBuilder", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Text.StringBuilder", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "StringBuilder", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "StringBuilder", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("StringBuilder");
                return "system/text/string-builder";
            }

            if (string.Equals(referencedClass, "Regex", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "Match", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "MatchCollection", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "Group", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "GroupCollection", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "RegexOptions", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Text.RegularExpressions.Regex", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Text.RegularExpressions.Match", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Text.RegularExpressions.MatchCollection", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Text.RegularExpressions.Group", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Text.RegularExpressions.GroupCollection", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Text.RegularExpressions.RegexOptions", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Regex", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Match", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "MatchCollection", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Group", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "GroupCollection", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "RegexOptions", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Regex", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Match", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "MatchCollection", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Group", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "GroupCollection", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "RegexOptions", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Regex");
                return "system/text/regular_expressions/regex";
            }

            if (string.Equals(referencedClass, "StringReader", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.IO.StringReader", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "StringReader", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "StringReader", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("StringReader");
                return "system/io/string-reader";
            }

            if (string.Equals(referencedClass, "StreamReader", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.IO.StreamReader", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "StreamReader", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "StreamReader", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("StreamReader");
                return "system/io/stream-reader";
            }

            if (string.Equals(referencedClass, "Stack", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Collections.Generic.Stack", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Stack", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Stack", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeStack");
                return "runtime/native_stack";
            }

            if (string.Equals(referencedClass, "Event", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Event", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeEvent");
                return "runtime/native_event";
            }

            if (string.Equals(referencedClass, "Action", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Action", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Action", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Action", StringComparison.Ordinal)) {
                return "system/action";
            }

            if (string.Equals(referencedClass, "Func", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Func", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Func", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Func", StringComparison.Ordinal)) {
                return "system/func";
            }

            if (string.Equals(referencedClass, "IntPtr", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.IntPtr", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "IntPtr", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "UIntPtr", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.UIntPtr", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "UIntPtr", StringComparison.Ordinal)) {
                return string.Empty;
            }

            string includePath = TryResolveIncludePath(referencedClass);
            if (!string.IsNullOrWhiteSpace(includePath) &&
                !string.Equals(includePath, referencedClass, StringComparison.Ordinal)) {
                return includePath;
            }

            if (!string.Equals(normalizedReferencedClass, referencedClass, StringComparison.Ordinal)) {
                includePath = TryResolveIncludePath(normalizedReferencedClass);
                if (!string.IsNullOrWhiteSpace(includePath)) {
                    return includePath;
                }

                if (!string.IsNullOrWhiteSpace(includePath)) {
                    return includePath;
                }
            }

            if (string.Equals(normalizedReferencedClass, "Stream", StringComparison.Ordinal) &&
                !program.Classes.Any(candidate => !candidate.IsNative && candidate.Name == "Stream")) {
                processor?.RegisterRuntimeRequirement("Stream");
                return "system/io/stream";
            }

            if (string.Equals(normalizedReferencedClass, "StringReader", StringComparison.Ordinal) &&
                !program.Classes.Any(candidate => !candidate.IsNative && candidate.Name == "StringReader")) {
                processor?.RegisterRuntimeRequirement("StringReader");
                return "system/io/string-reader";
            }

            if (string.Equals(normalizedReferencedClass, "StreamReader", StringComparison.Ordinal) &&
                !program.Classes.Any(candidate => !candidate.IsNative && candidate.Name == "StreamReader")) {
                processor?.RegisterRuntimeRequirement("StreamReader");
                return "system/io/stream-reader";
            }

            if (string.Equals(normalizedReferencedClass, "MemoryStream", StringComparison.Ordinal) &&
                !program.Classes.Any(candidate => !candidate.IsNative && candidate.Name == "MemoryStream")) {
                processor?.RegisterRuntimeRequirement("MemoryStream");
                return "system/io/memory-stream";
            }

            if (string.Equals(normalizedReferencedClass, "FileStream", StringComparison.Ordinal) &&
                !program.Classes.Any(candidate => !candidate.IsNative && candidate.Name == "FileStream")) {
                processor?.RegisterRuntimeRequirement("FileStream");
                return "system/io/file-stream";
            }

            if (!string.IsNullOrWhiteSpace(includePath)) {
                return includePath;
            }

            return string.Empty;
        }

        /// <summary>
        /// Attempts to resolve an include path for a referenced type name without applying namespace fallback.
        /// </summary>
        /// <param name="referencedClass">Referenced type name to resolve.</param>
        /// <returns>The include path without extension when resolution succeeds; otherwise, an empty string.</returns>
        string TryResolveIncludePath(string referencedClass) {
            VariableType variableType = VariableUtil.GetVarType(referencedClass);

            if (referencedClass.Contains("[]", StringComparison.Ordinal) || variableType.Type == VariableDataType.Array) {
                return "runtime/array";
            }

            if (variableType.Type == VariableDataType.Tuple || string.Equals(variableType.TypeName, "ValueTuple", StringComparison.Ordinal)) {
                return "runtime/native_tuple";
            }

            if (variableType.Type == VariableDataType.String) {
                return "runtime/native_string";
            }

            if (variableType.Type == VariableDataType.List) {
                return "runtime/native_list";
            }

            if (variableType.Type == VariableDataType.Dictionary) {
                return "runtime/native_dictionary";
            }

            if (string.Equals(variableType.TypeName, "Stack", StringComparison.Ordinal)) {
                return "runtime/native_stack";
            }

            if (string.Equals(variableType.TypeName, "StringReader", StringComparison.Ordinal)) {
                return "system/io/string-reader";
            }

            if (string.Equals(variableType.TypeName, "Regex", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "Match", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "MatchCollection", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "Group", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "GroupCollection", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "RegexOptions", StringComparison.Ordinal)) {
                return "system/text/regular_expressions/regex";
            }

            ConversionClass generatedClass = null;
            if (TryResolveGeneratedClass(referencedClass, out generatedClass)) {
                return generatedClass.GetEmittedTypeName();
            }

            generatedClass = program.FindGeneratedClass(variableType.TypeName, variableType.GenericArgs.Count);
            if (generatedClass != null) {
                return generatedClass.GetEmittedTypeName();
            }

            CPPKnownClass knownSourceClass = program.Requirements.FirstOrDefault(requirement => requirement.Name == variableType.TypeName);
            if (knownSourceClass != null && !string.IsNullOrWhiteSpace(knownSourceClass.Path)) {
                return knownSourceClass.Path;
            }

            CPPTypeData typeData = new CPPTypeData();

            if (processor != null) {
                variableType = processor.ConvertToCPPType(variableType, out typeData);
            }

            if (typeData.IsNativeType) {
                return string.Empty;
            }

            CPPKnownClass knownClass = program.Requirements.FirstOrDefault(requirement => requirement.Name == variableType.TypeName);
            if (knownClass != null && !string.IsNullOrWhiteSpace(knownClass.Path)) {
                return knownClass.Path;
            }

            return variableType.TypeName;
        }

        /// <summary>
        /// Collapses a namespace-qualified type reference to the leaf type name used by generated headers.
        /// </summary>
        /// <param name="referencedClass">Referenced type name to normalize.</param>
        /// <returns>The leaf type name when the reference is namespace qualified; otherwise, the original value.</returns>
        static string NormalizeReferencedClassName(string referencedClass) {
            if (string.IsNullOrWhiteSpace(referencedClass) || !referencedClass.Contains('.', StringComparison.Ordinal)) {
                return referencedClass;
            }

            int separatorIndex = referencedClass.LastIndexOf('.');
            if (separatorIndex < 0 || separatorIndex == referencedClass.Length - 1) {
                return referencedClass;
            }

            return referencedClass[(separatorIndex + 1)..];
        }

        /// <summary>
        /// Writes an enum declaration into the header file.
        /// </summary>
        /// <param name="conversionClass">The enum type to emit.</param>
        /// <param name="headerWriter">Writer that receives the enum declaration.</param>
        void WriteEnum(ConversionClass conversionClass, TextWriter headerWriter) {
            headerWriter.WriteLine($"enum class {conversionClass.GetEmittedTypeName()}");
            headerWriter.WriteLine("{");

            List<string> members = GetEnumMembers(conversionClass);
            for (int index = 0; index < members.Count; index++) {
                string suffix = index == members.Count - 1 ? string.Empty : ",";
                headerWriter.WriteLine($"    {members[index]}{suffix}");
            }

            headerWriter.WriteLine("};");
        }

        /// <summary>
        /// Extracts the emitted member names for an enum declaration.
        /// </summary>
        /// <param name="conversionClass">The enum conversion model.</param>
        /// <returns>The ordered enum member names.</returns>
        List<string> GetEnumMembers(ConversionClass conversionClass) {
            List<string> members = new List<string>();

            foreach (ConversionVariable variable in conversionClass.Variables) {
                if (!string.IsNullOrWhiteSpace(variable.Name)) {
                    members.Add(variable.Name);
                }
            }

            if (members.Count > 0) {
                return members;
            }

            if (conversionClass.EnumMembers == null) {
                return members;
            }

            foreach (object enumMember in conversionClass.EnumMembers) {
                if (enumMember == null) {
                    continue;
                }

                string memberName = enumMember.ToString();
                if (!string.IsNullOrWhiteSpace(memberName)) {
                    members.Add(memberName);
                }
            }

            return members;
        }

        /// <summary>
        /// Writes a class-like declaration and its out-of-line method definitions.
        /// </summary>
        /// <param name="conversionClass">The type to emit.</param>
        /// <param name="headerWriter">Writer that receives the header declaration.</param>
        /// <param name="sourceWriter">Writer that receives the source definitions.</param>
        void WriteClass(ConversionClass conversionClass, TextWriter headerWriter, TextWriter sourceWriter) {
            WriteTemplateDeclaration(conversionClass, headerWriter);

            string emittedTypeName = conversionClass.GetEmittedTypeName();
            string inheritance = CPPUtils.GetInheritance(program, conversionClass);
            if (string.IsNullOrWhiteSpace(inheritance)) {
                headerWriter.WriteLine($"class {emittedTypeName}");
            } else {
                headerWriter.WriteLine($"class {emittedTypeName} : {inheritance}");
            }

            headerWriter.WriteLine("{");

            bool wroteAnySection = false;
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Public, headerWriter, sourceWriter);
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Protected, headerWriter, sourceWriter);
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Private, headerWriter, sourceWriter);

            if (!wroteAnySection) {
                headerWriter.WriteLine("public:");
            }

            headerWriter.WriteLine("};");
        }

        /// <summary>
        /// Writes a template declaration for generic class-like types so generic parameters remain compile-time only.
        /// </summary>
        /// <param name="conversionClass">The type being emitted.</param>
        /// <param name="headerWriter">Writer that receives the template declaration.</param>
        void WriteTemplateDeclaration(ConversionClass conversionClass, TextWriter headerWriter) {
            if (conversionClass.GenericArgs == null || conversionClass.GenericArgs.Count == 0) {
                return;
            }

            string templateArguments = string.Join(", ", conversionClass.GenericArgs.Select(static argument => $"typename {argument}"));
            headerWriter.WriteLine($"template <{templateArguments}>");
        }

        /// <summary>
        /// Emits one access section, including lowered properties, fields, and methods.
        /// </summary>
        /// <param name="conversionClass">The class that owns the emitted members.</param>
        /// <param name="accessType">The access group to emit.</param>
        /// <param name="headerWriter">Writer that receives declarations.</param>
        /// <param name="sourceWriter">Writer that receives definitions.</param>
        /// <returns><c>true</c> when at least one member was emitted for the section.</returns>
        bool WriteAccessSection(ConversionClass conversionClass, MemberAccessType accessType, TextWriter headerWriter, TextWriter sourceWriter) {
            List<Action> writers = new List<Action>();

            foreach (ConversionVariable variable in conversionClass.Variables.Where(variable => variable.AccessType == accessType)) {
                if (IsComputedProperty(variable)) {
                    writers.Add(() => WriteComputedProperty(conversionClass, variable, headerWriter, sourceWriter));
                    continue;
                }

                writers.Add(() => WriteField(variable, headerWriter));
            }

            foreach (ConversionFunction function in conversionClass.Functions.Where(function => function.AccessType == accessType)) {
                writers.Add(() => WriteFunction(conversionClass, function, headerWriter, sourceWriter));
            }

            if (writers.Count == 0) {
                return false;
            }

            headerWriter.WriteLine($"{accessType.ToString().ToLowerInvariant()}:");

            for (int index = 0; index < writers.Count; index++) {
                writers[index]();
                if (index != writers.Count - 1) {
                    headerWriter.WriteLine();
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether a variable represents a property that needs accessor methods in C++ output.
        /// </summary>
        /// <param name="variable">The variable to inspect.</param>
        /// <returns><c>true</c> when the variable should lower into getter and setter methods.</returns>
        bool IsComputedProperty(ConversionVariable variable) {
            if (!variable.IsGet && !variable.IsSet) {
                return false;
            }

            return variable.GetBlock != null ||
                   variable.SetBlock != null ||
                   variable.ArrowExpression != null;
        }

        /// <summary>
        /// Writes a field declaration for a direct field or auto-property lowering result.
        /// </summary>
        /// <param name="variable">The variable to emit.</param>
        /// <param name="headerWriter">Writer that receives the field declaration.</param>
        void WriteField(ConversionVariable variable, TextWriter headerWriter) {
            string staticKeyword = variable.IsStatic ? "static " : string.Empty;
            string typeName = ConvertType(variable.VarType);
            headerWriter.WriteLine($"    {staticKeyword}{typeName} {variable.Name};");
        }

        /// <summary>
        /// Lowers a computed property into explicit accessor methods in both the header and source files.
        /// </summary>
        /// <param name="conversionClass">The class that owns the property.</param>
        /// <param name="variable">The property model to lower.</param>
        /// <param name="headerWriter">Writer that receives accessor declarations.</param>
        /// <param name="sourceWriter">Writer that receives accessor definitions.</param>
        void WriteComputedProperty(ConversionClass conversionClass, ConversionVariable variable, TextWriter headerWriter, TextWriter sourceWriter) {
            if (variable.IsGet) {
                ConversionFunction getter = CreateGetter(variable);
                WriteFunction(conversionClass, getter, headerWriter, sourceWriter);
            }

            if (variable.IsGet && variable.IsSet) {
                headerWriter.WriteLine();
            }

            if (variable.IsSet) {
                ConversionFunction setter = CreateSetter(variable);
                WriteFunction(conversionClass, setter, headerWriter, sourceWriter);
            }
        }

        /// <summary>
        /// Creates a getter function model from a property definition.
        /// </summary>
        /// <param name="variable">The source property model.</param>
        /// <returns>A function model suitable for normal function emission.</returns>
        ConversionFunction CreateGetter(ConversionVariable variable) {
            return new ConversionFunction {
                Name = $"get_{variable.Name}",
                AccessType = variable.AccessType,
                ReturnType = new VariableType(variable.VarType),
                RawBlock = variable.GetBlock
            };
        }

        /// <summary>
        /// Creates a setter function model from a property definition.
        /// </summary>
        /// <param name="variable">The source property model.</param>
        /// <returns>A function model suitable for normal function emission.</returns>
        ConversionFunction CreateSetter(ConversionVariable variable) {
            ConversionFunction setter = new ConversionFunction {
                Name = $"set_{variable.Name}",
                AccessType = variable.AccessType,
                RawBlock = variable.SetBlock
            };

            setter.InParameters.Add(new ConversionVariable {
                Name = "value",
                VarType = new VariableType(variable.VarType)
            });

            return setter;
        }

        /// <summary>
        /// Writes a normal function declaration and definition pair.
        /// </summary>
        /// <param name="conversionClass">The class that owns the function.</param>
        /// <param name="function">The function to emit.</param>
        /// <param name="headerWriter">Writer that receives the declaration.</param>
        /// <param name="sourceWriter">Writer that receives the definition.</param>
        void WriteFunction(ConversionClass conversionClass, ConversionFunction function, TextWriter headerWriter, TextWriter sourceWriter) {
            WriteFunctionDeclaration(conversionClass, function, headerWriter);
            WriteFunctionDefinition(conversionClass, function, sourceWriter);
        }

        /// <summary>
        /// Writes a function declaration into the class header.
        /// </summary>
        /// <param name="conversionClass">The class that owns the function.</param>
        /// <param name="function">The function to declare.</param>
        /// <param name="headerWriter">Writer that receives the declaration.</param>
        void WriteFunctionDeclaration(ConversionClass conversionClass, ConversionFunction function, TextWriter headerWriter) {
            WriteFunctionTemplateDeclaration(function, headerWriter, "    ");
            headerWriter.Write("    ");

            if (function.IsStatic) {
                headerWriter.Write("static ");
            }

            if (!function.IsConstructor) {
                headerWriter.Write($"{GetReturnType(function)} ");
            }

            headerWriter.Write($"{GetFunctionName(conversionClass, function)}(");
            WriteParameters(function, headerWriter);
            headerWriter.WriteLine(");");
        }

        /// <summary>
        /// Writes a function definition into the C++ source file.
        /// </summary>
        /// <param name="conversionClass">The class that owns the function.</param>
        /// <param name="function">The function to define.</param>
        /// <param name="sourceWriter">Writer that receives the definition.</param>
        void WriteFunctionDefinition(ConversionClass conversionClass, ConversionFunction function, TextWriter sourceWriter) {
            WriteTemplateDeclaration(conversionClass, sourceWriter);
            WriteFunctionTemplateDeclaration(function, sourceWriter, string.Empty);

            if (!function.IsConstructor) {
                sourceWriter.Write($"{GetReturnType(function)} ");
            }

            sourceWriter.Write($"{GetQualifiedClassName(conversionClass)}::{GetFunctionName(conversionClass, function)}(");
            WriteParameters(function, sourceWriter);
            sourceWriter.WriteLine(")");
            sourceWriter.WriteLine("{");

            if (function.HasBody) {
                function.WriteLines(processor, program, conversionClass, sourceWriter);
            }

            sourceWriter.WriteLine("}");
            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Writes a template declaration for a generic method so method type parameters remain compile-time only.
        /// </summary>
        /// <param name="function">The function whose generic parameters should be declared.</param>
        /// <param name="writer">Writer that receives the template declaration.</param>
        /// <param name="indentation">Indentation applied before the template line.</param>
        void WriteFunctionTemplateDeclaration(ConversionFunction function, TextWriter writer, string indentation) {
            if (function.GenericParameters == null || function.GenericParameters.Count == 0) {
                return;
            }

            string templateArguments = string.Join(", ", function.GenericParameters.Select(static argument => $"typename {argument}"));
            writer.Write(indentation);
            writer.WriteLine($"template <{templateArguments}>");
        }

        /// <summary>
        /// Writes the function parameter list to the provided writer.
        /// </summary>
        /// <param name="function">The function whose parameters will be written.</param>
        /// <param name="writer">Writer that receives the parameter list.</param>
        void WriteParameters(ConversionFunction function, TextWriter writer) {
            for (int index = 0; index < function.InParameters.Count; index++) {
                ConversionVariable parameter = function.InParameters[index];
                writer.Write($"{ConvertType(parameter.VarType)} {parameter.Name}");

                if (index != function.InParameters.Count - 1) {
                    writer.Write(", ");
                }
            }
        }

        /// <summary>
        /// Resolves the emitted function name, substituting the owning class name for constructors.
        /// </summary>
        /// <param name="conversionClass">The class that owns the function.</param>
        /// <param name="function">The function being emitted.</param>
        /// <returns>The emitted function name.</returns>
        string GetFunctionName(ConversionClass conversionClass, ConversionFunction function) {
            if (function.IsConstructor) {
                return conversionClass.GetEmittedTypeName();
            }

            return function.Name;
        }

        /// <summary>
        /// Resolves the emitted class qualification token, including compile-time generic arguments when required.
        /// </summary>
        /// <param name="conversionClass">The class whose qualified emitted name is needed.</param>
        /// <returns>The emitted class qualification token.</returns>
        string GetQualifiedClassName(ConversionClass conversionClass) {
            string emittedTypeName = conversionClass.GetEmittedTypeName();
            if (conversionClass.GenericArgs == null || conversionClass.GenericArgs.Count == 0) {
                return emittedTypeName;
            }

            return $"{emittedTypeName}<{string.Join(", ", conversionClass.GenericArgs)}>";
        }

        /// <summary>
        /// Resolves the emitted return type for a function.
        /// </summary>
        /// <param name="function">The function being emitted.</param>
        /// <returns>The emitted C++ return type token.</returns>
        string GetReturnType(ConversionFunction function) {
            if (function.ReturnType == null) {
                return "void";
            }

            return ConvertType(function.ReturnType);
        }

        string ConvertType(VariableType variableType) {
            if (variableType.Type == VariableDataType.Unknown && !variableType.IsNullable) {
                return variableType.ToCPPString(program);
            }

            VariableType cppType = processor.ConvertToCPPType(variableType, out CPPTypeData typeData);
            string cppTypeName = cppType.ToCPPString(program);

            if (typeData.IsPointer) {
                return cppTypeName + "*";
            }

            return cppTypeName;
        }
    }
}
