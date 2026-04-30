using cs2.core;
using Microsoft.CodeAnalysis;
using System.Text.RegularExpressions;

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

            if (conversionClass.DeclarationType == MemberDeclarationType.Enum) {
                WriteSourcePreamble(conversionClass, sourceWriter);
                WriteEnum(conversionClass, headerWriter);
                return;
            }

            StringWriter deferredSourceWriter = new StringWriter();
            WriteClass(conversionClass, headerWriter, deferredSourceWriter);
            WriteSourcePreamble(conversionClass, sourceWriter);
            sourceWriter.Write(deferredSourceWriter.ToString());
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

            AddSemanticInheritanceTypeReferences(conversionClass, referencedTypes, excludedTypeNames);

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
                return;
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

            HashSet<string> emittedIncludePaths = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> excludedTypeNames = GetExcludedTypeNames(conversionClass);
            foreach (string referencedClass in conversionClass.ReferencedClasses.Distinct(StringComparer.Ordinal)) {
                string normalizedReferencedClass = NormalizeReferencedClassName(referencedClass);
                if (excludedTypeNames.Contains(referencedClass) || excludedTypeNames.Contains(normalizedReferencedClass)) {
                    continue;
                }

                string includePath = ResolveIncludePath(referencedClass);
                if (string.IsNullOrWhiteSpace(includePath) || !emittedIncludePaths.Add(includePath)) {
                    continue;
                }

                sourceWriter.WriteLine($"#include \"{includePath}.hpp\"");
            }

            foreach (CPPRuntimeRequirementDefinition requirement in processor.RuntimeRequirementRegistrar.RegisteredRequirements.OrderBy(requirement => requirement.IncludePath, StringComparer.Ordinal)) {
                if (string.IsNullOrWhiteSpace(requirement.IncludePath) || !emittedIncludePaths.Add(requirement.IncludePath)) {
                    continue;
                }

                sourceWriter.WriteLine($"#include \"{requirement.IncludePath}\"");
            }

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

            if (referencedType.IsNullable) {
                processor?.RegisterRuntimeRequirement("NativeNullable");
                return "runtime/native_nullable";
            }

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

            if (string.Equals(referencedClass, "Enum", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Enum", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Enum", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeEnum");
                return "runtime/native_enum";
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

            if (string.Equals(referencedClass, "StringComparer", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.StringComparer", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "StringComparer", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "StringComparer", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("StringComparer");
                return "system/string_comparer";
            }

            if (string.Equals(referencedClass, "StringComparison", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.StringComparison", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "StringComparison", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "StringComparison", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeString");
                return "runtime/native_string";
            }

            if (string.Equals(referencedClass, "Math", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Math", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Math", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Math", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "MidpointRounding", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.MidpointRounding", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "MidpointRounding", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "MidpointRounding", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Math");
                return "system/math";
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

            if (string.Equals(referencedClass, "Debug", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Diagnostics.Debug", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Debug", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Debug", StringComparison.Ordinal)) {
                return "system/diagnostics/debug";
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

            if (string.Equals(normalizedReferencedClass, "File", StringComparison.Ordinal) &&
                !program.Classes.Any(candidate => !candidate.IsNative && candidate.Name == "File")) {
                processor?.RegisterRuntimeRequirement("File");
                return "system/io/file";
            }

            if (string.Equals(normalizedReferencedClass, "Path", StringComparison.Ordinal) &&
                !program.Classes.Any(candidate => !candidate.IsNative && candidate.Name == "Path")) {
                processor?.RegisterRuntimeRequirement("Path");
                return "system/io/path";
            }

            if (IsNativeExceptionTypeName(normalizedReferencedClass)) {
                processor?.RegisterRuntimeRequirement("NativeExceptions");
                return "runtime/native_exceptions";
            }

            string includePath = TryResolveIncludePath(referencedClass);
            if (!string.IsNullOrWhiteSpace(includePath) &&
                !string.Equals(includePath, referencedClass, StringComparison.Ordinal) &&
                !string.Equals(includePath, normalizedReferencedClass, StringComparison.Ordinal)) {
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

            if (string.Equals(variableType.TypeName, "Encoding", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Encoding");
                return "system/text/encoding";
            }

            if (string.Equals(variableType.TypeName, "MathF", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Math");
                return "system/math";
            }

            if (string.Equals(variableType.TypeName, "BinaryPrimitives", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("BinaryPrimitives");
                return "system/binary_primitives";
            }

            if (IsNativeExceptionTypeName(variableType.TypeName)) {
                processor?.RegisterRuntimeRequirement("NativeExceptions");
                return "runtime/native_exceptions";
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
            string inheritance = GetInheritanceClause(conversionClass);
            if (string.IsNullOrWhiteSpace(inheritance)) {
                headerWriter.WriteLine($"class {emittedTypeName}");
            } else {
                headerWriter.WriteLine($"class {emittedTypeName} : {inheritance}");
            }

            headerWriter.WriteLine("{");

            if (conversionClass.DeclarationType == MemberDeclarationType.Interface) {
                WriteInterfaceSection(conversionClass, headerWriter, sourceWriter);
                headerWriter.WriteLine("};");
                return;
            }

            bool wroteAnySection = false;
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Public, headerWriter, sourceWriter);
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Protected, headerWriter, sourceWriter);
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Private, headerWriter, sourceWriter);

            if (!wroteAnySection) {
                headerWriter.WriteLine("public:");
            }

            headerWriter.WriteLine("};");
        }

        string GetInheritanceClause(ConversionClass conversionClass) {
            if (conversionClass.TypeSymbol is INamedTypeSymbol typeSymbol) {
                List<string> inheritanceParts = new List<string>();

                if (typeSymbol.TypeKind != TypeKind.Interface &&
                    typeSymbol.BaseType != null &&
                    typeSymbol.BaseType.SpecialType != SpecialType.System_Object &&
                    typeSymbol.BaseType.SpecialType != SpecialType.System_ValueType) {
                    inheritanceParts.Add($"public {RenderInheritanceType(typeSymbol.BaseType)}");
                }

                foreach (INamedTypeSymbol interfaceSymbol in typeSymbol.Interfaces) {
                    inheritanceParts.Add($"public {RenderInheritanceType(interfaceSymbol)}");
                }

                if (inheritanceParts.Count > 0) {
                    return string.Join(", ", inheritanceParts.Distinct(StringComparer.Ordinal));
                }
            }

            return CPPUtils.GetInheritance(program, conversionClass);
        }

        void AddSemanticInheritanceTypeReferences(
            ConversionClass conversionClass,
            HashSet<string> referencedTypes,
            HashSet<string> excludedTypeNames) {
            if (conversionClass.TypeSymbol is not INamedTypeSymbol typeSymbol) {
                return;
            }

            if (typeSymbol.TypeKind != TypeKind.Interface &&
                typeSymbol.BaseType != null &&
                typeSymbol.BaseType.SpecialType != SpecialType.System_Object &&
                typeSymbol.BaseType.SpecialType != SpecialType.System_ValueType) {
                AddInheritanceTypeReference(typeSymbol.BaseType, referencedTypes, excludedTypeNames);
            }

            foreach (INamedTypeSymbol interfaceSymbol in typeSymbol.Interfaces) {
                AddInheritanceTypeReference(interfaceSymbol, referencedTypes, excludedTypeNames);
            }
        }

        void AddInheritanceTypeReference(
            INamedTypeSymbol typeSymbol,
            HashSet<string> referencedTypes,
            HashSet<string> excludedTypeNames) {
            string typeName = program.FindGeneratedClass(typeSymbol.Name, typeSymbol.TypeArguments.Length)?.GetEmittedTypeName()
                ?? NormalizeReferencedClassName(typeSymbol.Name);
            if (!excludedTypeNames.Contains(typeName)) {
                referencedTypes.Add(typeName);
            }

            foreach (ITypeSymbol typeArgument in typeSymbol.TypeArguments) {
                VariableType argumentType = VariableUtil.GetVarType(typeArgument);
                string argumentTypeName = NormalizeReferencedClassName(argumentType.TypeName);
                if (!string.IsNullOrWhiteSpace(argumentTypeName) && !excludedTypeNames.Contains(argumentTypeName)) {
                    referencedTypes.Add(argumentTypeName);
                }
            }
        }

        string RenderInheritanceType(INamedTypeSymbol typeSymbol) {
            string emittedTypeName = program.FindGeneratedClass(typeSymbol.Name, typeSymbol.TypeArguments.Length)?.GetEmittedTypeName()
                ?? NormalizeReferencedClassName(typeSymbol.Name);

            if (typeSymbol.TypeArguments.Length == 0) {
                return emittedTypeName;
            }

            List<string> renderedArguments = new List<string>();
            foreach (ITypeSymbol typeArgument in typeSymbol.TypeArguments) {
                renderedArguments.Add(ConvertType(VariableUtil.GetVarType(typeArgument)));
            }

            return $"{emittedTypeName}<{string.Join(", ", renderedArguments)}>";
        }

        /// <summary>
        /// Emits interface members as a single public section because the current backend lowers interface properties as directly accessible members.
        /// </summary>
        /// <param name="conversionClass">The interface declaration being emitted.</param>
        /// <param name="headerWriter">Writer that receives the header declaration.</param>
        /// <param name="sourceWriter">Writer that receives source definitions for computed members.</param>
        void WriteInterfaceSection(ConversionClass conversionClass, TextWriter headerWriter, TextWriter sourceWriter) {
            headerWriter.WriteLine("public:");

            List<Action> writers = new List<Action>();

            foreach (ConversionVariable variable in conversionClass.Variables) {
                if (IsComputedProperty(variable)) {
                    writers.Add(() => WriteComputedProperty(conversionClass, variable, headerWriter, sourceWriter));
                    continue;
                }

                writers.Add(() => WriteField(conversionClass, variable, headerWriter));
            }

            foreach (ConversionFunction function in conversionClass.Functions) {
                writers.Add(() => WriteFunction(conversionClass, function, headerWriter, sourceWriter));
            }

            for (int index = 0; index < writers.Count; index++) {
                writers[index]();
                if (index != writers.Count - 1) {
                    headerWriter.WriteLine();
                }
            }
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

            if (accessType == MemberAccessType.Public &&
                ShouldEmitImplicitValueTypeDefaultConstructor(conversionClass)) {
                writers.Add(() => WriteImplicitValueTypeDefaultConstructor(conversionClass, headerWriter, sourceWriter));
            }

            foreach (ConversionVariable variable in conversionClass.Variables.Where(variable => variable.AccessType == accessType)) {
                if (IsComputedProperty(variable)) {
                    writers.Add(() => WriteComputedProperty(conversionClass, variable, headerWriter, sourceWriter));
                    continue;
                }

                writers.Add(() => WriteField(conversionClass, variable, headerWriter));
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
        /// Determines whether the emitter should synthesize the implicit parameterless constructor that C# value types always expose.
        /// </summary>
        /// <param name="conversionClass">The converted type to inspect.</param>
        /// <returns><c>true</c> when the value type needs an emitted parameterless constructor; otherwise <c>false</c>.</returns>
        bool ShouldEmitImplicitValueTypeDefaultConstructor(ConversionClass conversionClass) {
            if (conversionClass?.TypeSymbol?.IsValueType != true ||
                conversionClass.DeclarationType == MemberDeclarationType.Enum) {
                return false;
            }

            return !conversionClass.Functions.Any(function => function.IsConstructor && function.InParameters.Count == 0);
        }

        /// <summary>
        /// Emits the implicit parameterless constructor required for C# value-type default initialization.
        /// </summary>
        /// <param name="conversionClass">The value type that needs the constructor.</param>
        /// <param name="headerWriter">Writer that receives the declaration.</param>
        /// <param name="sourceWriter">Writer that receives the definition.</param>
        void WriteImplicitValueTypeDefaultConstructor(ConversionClass conversionClass, TextWriter headerWriter, TextWriter sourceWriter) {
            string emittedTypeName = conversionClass.GetEmittedTypeName();
            headerWriter.WriteLine($"    {emittedTypeName}();");

            WriteTemplateDeclaration(conversionClass, sourceWriter);
            sourceWriter.Write($"{GetQualifiedClassName(conversionClass)}::{emittedTypeName}()");

            List<string> initializers = conversionClass.Variables
                .Where(variable => !variable.IsStatic && !IsComputedProperty(variable))
                .Select(variable => $"{variable.Name}()")
                .ToList();
            if (initializers.Count > 0) {
                sourceWriter.Write(" : ");
                sourceWriter.Write(string.Join(", ", initializers));
            }

            sourceWriter.WriteLine();
            sourceWriter.WriteLine("{");
            sourceWriter.WriteLine("}");
            sourceWriter.WriteLine();
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
        void WriteField(ConversionClass conversionClass, ConversionVariable variable, TextWriter headerWriter) {
            string staticKeyword = variable.IsStatic ? "static " : string.Empty;
            string typeName = ConvertType(variable.VarType, conversionClass);
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
                headerWriter.Write($"{GetReturnType(conversionClass, function)} ");
            }

            headerWriter.Write($"{GetFunctionName(conversionClass, function)}(");
            WriteParameters(conversionClass, function, headerWriter);
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
                sourceWriter.Write($"{GetReturnType(conversionClass, function)} ");
            }

            sourceWriter.Write($"{GetQualifiedClassName(conversionClass)}::{GetFunctionName(conversionClass, function)}(");
            WriteParameters(conversionClass, function, sourceWriter);
            sourceWriter.Write(")");
            WriteConstructorInitializer(conversionClass, function, sourceWriter);
            sourceWriter.WriteLine();
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
        void WriteParameters(ConversionClass conversionClass, ConversionFunction function, TextWriter writer) {
            for (int index = 0; index < function.InParameters.Count; index++) {
                ConversionVariable parameter = function.InParameters[index];
                string parameterType = ConvertType(parameter.VarType, conversionClass, function);
                if ((parameter.Modifier & (ParameterModifier.Out | ParameterModifier.Ref)) != 0) {
                    parameterType += "&";
                }

                writer.Write($"{parameterType} {parameter.Name}");

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

        void WriteConstructorInitializer(ConversionClass conversionClass, ConversionFunction function, TextWriter sourceWriter) {
            if (!function.IsConstructor || function.ConstructorInitializer == null) {
                return;
            }

            string initializerTarget;
            if (string.Equals(function.ConstructorInitializer.ThisOrBaseKeyword.Text, "base", StringComparison.Ordinal)) {
                string baseTypeName = conversionClass.Extensions?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(baseTypeName)) {
                    return;
                }

                ConversionClass baseClass = program.FindGeneratedClass(baseTypeName, 0);
                if (baseClass != null) {
                    initializerTarget = baseClass.GetEmittedTypeName();
                } else {
                    initializerTarget = NormalizeReferencedClassName(baseTypeName);
                }
            } else {
                initializerTarget = conversionClass.GetEmittedTypeName();
            }

            sourceWriter.Write(" : ");
            sourceWriter.Write(initializerTarget);
            sourceWriter.Write("(");

            if (function.ConstructorInitializer.ArgumentList != null) {
                for (int index = 0; index < function.ConstructorInitializer.ArgumentList.Arguments.Count; index++) {
                    sourceWriter.Write(function.ConstructorInitializer.ArgumentList.Arguments[index].Expression.ToString());
                    if (index < function.ConstructorInitializer.ArgumentList.Arguments.Count - 1) {
                        sourceWriter.Write(", ");
                    }
                }
            }

            sourceWriter.Write(")");
        }

        /// <summary>
        /// Resolves the emitted return type for a function.
        /// </summary>
        /// <param name="function">The function being emitted.</param>
        /// <returns>The emitted C++ return type token.</returns>
        string GetReturnType(ConversionClass conversionClass, ConversionFunction function) {
            if (function.ReturnType == null) {
                return "void";
            }

            return ConvertType(function.ReturnType, conversionClass, function);
        }

        string ConvertType(VariableType variableType, ConversionClass conversionClass = null, ConversionFunction function = null) {
            if (variableType.Type == VariableDataType.Unknown && !variableType.IsNullable) {
                return GetScopedTypeName(variableType, conversionClass, function);
            }

            VariableType cppType = processor.ConvertToCPPType(variableType, out CPPTypeData typeData);
            string cppTypeName = GetScopedTypeName(cppType, conversionClass, function);
            if (IsGeneratedEnumType(variableType)) {
                return cppTypeName;
            }

            if (typeData.IsPointer) {
                return cppTypeName + "*";
            }

            return cppTypeName;
        }

        /// <summary>
        /// Determines whether the referenced type resolves to a generated enum declaration that must retain value semantics.
        /// </summary>
        /// <param name="variableType">The type being emitted.</param>
        /// <returns><c>true</c> when the type resolves to a generated enum; otherwise, <c>false</c>.</returns>
        bool IsGeneratedEnumType(VariableType variableType) {
            if (variableType == null) {
                return false;
            }

            ConversionClass generatedClass = program.FindGeneratedClass(variableType.TypeName, variableType.GenericArgs.Count);
            if (generatedClass == null && !string.IsNullOrWhiteSpace(variableType.TypeName)) {
                string normalizedTypeName = NormalizeReferencedClassName(variableType.TypeName);
                generatedClass = program.FindGeneratedClass(normalizedTypeName, variableType.GenericArgs.Count);
            }

            return generatedClass?.DeclarationType == MemberDeclarationType.Enum;
        }

        string GetScopedTypeName(VariableType variableType, ConversionClass conversionClass, ConversionFunction function) {
            string renderedTypeName = variableType.ToCPPString(program);
            if (IsParameterlessActionType(variableType, renderedTypeName)) {
                return "Action<>";
            }

            if (variableType.GenericArgs == null || variableType.GenericArgs.Count == 0) {
                return QualifyTypeName(renderedTypeName, variableType, conversionClass, function);
            }

            int genericSeparatorIndex = renderedTypeName.IndexOf('<');
            string topLevelTypeName = genericSeparatorIndex >= 0
                ? renderedTypeName[..genericSeparatorIndex]
                : renderedTypeName;

            string qualifiedTopLevelTypeName = QualifyTypeName(topLevelTypeName, variableType, conversionClass, function);
            string genericArguments = string.Join(", ", variableType.GenericArgs.Select(argument => GetScopedTypeName(argument, conversionClass, function)));
            return QualifyRenderedTypeName($"{qualifiedTopLevelTypeName}<{genericArguments}>", conversionClass, function);
        }

        string QualifyRenderedTypeName(string renderedTypeName, ConversionClass conversionClass, ConversionFunction function) {
            if (string.IsNullOrWhiteSpace(renderedTypeName)) {
                return renderedTypeName;
            }

            IEnumerable<string> excludedTypeNames = Enumerable.Empty<string>();
            if (conversionClass?.GenericArgs != null) {
                excludedTypeNames = excludedTypeNames.Concat(conversionClass.GenericArgs);
            }

            if (function?.GenericParameters != null) {
                excludedTypeNames = excludedTypeNames.Concat(function.GenericParameters);
            }

            string qualifiedTypeName = renderedTypeName;
            foreach (string generatedTypeName in program.Classes
                .Where(candidate => !candidate.IsNative)
                .Select(candidate => candidate.GetEmittedTypeName())
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(static candidate => candidate.Length)) {
                if (excludedTypeNames.Any(candidate => string.Equals(candidate, generatedTypeName, StringComparison.Ordinal))) {
                    continue;
                }

                qualifiedTypeName = Regex.Replace(
                    qualifiedTypeName,
                    $@"(?<![:\w]){Regex.Escape(generatedTypeName)}(?!\w)",
                    $"::{generatedTypeName}");
            }

            return qualifiedTypeName;
        }

        string QualifyTypeName(string typeName, VariableType variableType, ConversionClass conversionClass, ConversionFunction function) {
            if (string.IsNullOrWhiteSpace(typeName) ||
                string.Equals(typeName, "void", StringComparison.Ordinal) ||
                string.Equals(typeName, "bool", StringComparison.Ordinal) ||
                string.Equals(typeName, "char", StringComparison.Ordinal) ||
                string.Equals(typeName, "float", StringComparison.Ordinal) ||
                string.Equals(typeName, "double", StringComparison.Ordinal) ||
                string.Equals(typeName, "int8_t", StringComparison.Ordinal) ||
                string.Equals(typeName, "uint8_t", StringComparison.Ordinal) ||
                string.Equals(typeName, "int16_t", StringComparison.Ordinal) ||
                string.Equals(typeName, "uint16_t", StringComparison.Ordinal) ||
                string.Equals(typeName, "int32_t", StringComparison.Ordinal) ||
                string.Equals(typeName, "uint32_t", StringComparison.Ordinal) ||
                string.Equals(typeName, "int64_t", StringComparison.Ordinal) ||
                string.Equals(typeName, "uint64_t", StringComparison.Ordinal) ||
                typeName.Contains("::", StringComparison.Ordinal) ||
                IsGenericTypeParameter(typeName, conversionClass, function)) {
                return typeName;
            }

            if (!IsGeneratedTypeName(typeName, variableType) &&
                !IsScopedRuntimeTypeName(typeName)) {
                return typeName;
            }

            return $"::{typeName}";
        }

        bool IsGenericTypeParameter(string typeName, ConversionClass conversionClass, ConversionFunction function) {
            if (conversionClass?.GenericArgs != null && conversionClass.GenericArgs.Any(argument => string.Equals(argument, typeName, StringComparison.Ordinal))) {
                return true;
            }

            if (function?.GenericParameters != null && function.GenericParameters.Any(argument => string.Equals(argument, typeName, StringComparison.Ordinal))) {
                return true;
            }

            return false;
        }

        bool IsGeneratedTypeName(string typeName, VariableType variableType) {
            int genericArity = variableType.GenericArgs?.Count ?? 0;
            if (program.FindGeneratedClass(typeName, genericArity) != null) {
                return true;
            }

            return program.Classes.Any(candidate =>
                !candidate.IsNative &&
                string.Equals(candidate.GetEmittedTypeName(), typeName, StringComparison.Ordinal));
        }

        static bool IsParameterlessActionType(VariableType variableType, string renderedTypeName) {
            if (variableType == null) {
                return false;
            }

            if (variableType.GenericArgs != null && variableType.GenericArgs.Count > 0) {
                return false;
            }

            return string.Equals(renderedTypeName, "Action", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "Action", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Action", StringComparison.Ordinal);
        }

        static bool IsScopedRuntimeTypeName(string typeName) {
            return string.Equals(typeName, "Stream", StringComparison.Ordinal) ||
                string.Equals(typeName, "FileStream", StringComparison.Ordinal) ||
                string.Equals(typeName, "MemoryStream", StringComparison.Ordinal) ||
                string.Equals(typeName, "Event", StringComparison.Ordinal) ||
                string.Equals(typeName, "MathF", StringComparison.Ordinal) ||
                IsNativeExceptionTypeName(typeName);
        }

        static bool IsNativeExceptionTypeName(string typeName) {
            return string.Equals(typeName, "Exception", StringComparison.Ordinal) ||
                string.Equals(typeName, "ArgumentException", StringComparison.Ordinal) ||
                string.Equals(typeName, "ArgumentNullException", StringComparison.Ordinal) ||
                string.Equals(typeName, "ArgumentOutOfRangeException", StringComparison.Ordinal) ||
                string.Equals(typeName, "InvalidOperationException", StringComparison.Ordinal) ||
                string.Equals(typeName, "EndOfStreamException", StringComparison.Ordinal) ||
                string.Equals(typeName, "NotSupportedException", StringComparison.Ordinal);
        }
    }
}
