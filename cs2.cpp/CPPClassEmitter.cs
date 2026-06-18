using cs2.core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;
using System.Text.RegularExpressions;

namespace cs2.cpp {
    /// <summary>
    /// Emits C++ header and source declarations for a converted class using the current backend rules.
    /// </summary>
    public class CPPClassEmitter {
        readonly CPPConversiorProcessor processor;
        readonly CPPProgram program;
        readonly CPPGeneratedFunctionBodyOverrideCatalog functionBodyOverrideCatalog;

        /// <summary>
        /// Initializes a class emitter bound to the current processor and program state.
        /// </summary>
        /// <param name="processor">Processor used to lower method and accessor bodies.</param>
        /// <param name="program">Program model that resolves known C++ runtime types.</param>
        public CPPClassEmitter(CPPConversiorProcessor processor, CPPProgram program) {
            this.processor = processor;
            this.program = program;
            functionBodyOverrideCatalog = new CPPGeneratedFunctionBodyOverrideCatalog();
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
            CPPTypeRuntimeRequirementScope typeScope = processor?.BeginTypeRuntimeRequirementScope() ?? new CPPTypeRuntimeRequirementScope();

            try {
                if (conversionClass.DeclarationType == MemberDeclarationType.Enum) {
                    WriteSourcePreamble(conversionClass, sourceWriter, typeScope);
                    WriteEnum(conversionClass, headerWriter);
                    return;
                }

                if (conversionClass.DeclarationType == MemberDeclarationType.Delegate) {
                    processor?.RegisterRuntimeRequirement("Delegate");
                    WriteSourcePreamble(conversionClass, sourceWriter, typeScope);
                    WriteDelegate(conversionClass, headerWriter);
                    return;
                }

                StringWriter deferredSourceWriter = new StringWriter();
                WriteClass(conversionClass, headerWriter, deferredSourceWriter);
                WriteSourcePreamble(conversionClass, sourceWriter, typeScope);
                sourceWriter.Write(deferredSourceWriter.ToString());
            } finally {
                processor?.EndTypeRuntimeRequirementScope(typeScope);
            }
        }

        /// <summary>
        /// Writes the header preamble and include directives required by a converted type.
        /// </summary>
        /// <param name="conversionClass">The type being emitted.</param>
        /// <param name="headerWriter">Writer that receives the header preamble.</param>
        void WriteHeaderPreamble(ConversionClass conversionClass, TextWriter headerWriter) {
            headerWriter.WriteLine("#pragma once");
            headerWriter.WriteLine("#ifdef DrawText");
            headerWriter.WriteLine("#undef DrawText");
            headerWriter.WriteLine("#endif");
            if (ShouldEmitSequentialLayoutTailPadding(conversionClass)) {
                headerWriter.WriteLine("#include <array>");
            }
            headerWriter.WriteLine("#include <cstdint>");
            headerWriter.WriteLine();

            bool wroteInclude = false;
            HashSet<string> referencedTypes = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> includeRequiredTypes = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> forwardDeclaredTypes = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> excludedTypeNames = GetExcludedTypeNames(conversionClass);

            foreach (string extension in conversionClass.Extensions.Distinct(StringComparer.Ordinal)) {
                string normalizedExtension = NormalizeReferencedClassName(NormalizeIncludeCandidateTypeName(extension));
                if (!excludedTypeNames.Contains(extension) &&
                    !excludedTypeNames.Contains(normalizedExtension)) {
                    referencedTypes.Add(extension);
                    includeRequiredTypes.Add(extension);
                }
            }

            AddSemanticInheritanceTypeReferences(conversionClass, referencedTypes, includeRequiredTypes, excludedTypeNames);

            AddSignatureTypeReferences(conversionClass, referencedTypes, includeRequiredTypes);

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
                string normalizedReferencedType = NormalizeReferencedClassName(referencedType);
                if (!includeRequiredTypes.Contains(referencedType) &&
                    !includeRequiredTypes.Contains(normalizedReferencedType) &&
                    TryResolveGeneratedClass(referencedType, out _)) {
                    continue;
                }

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
        /// <param name="includeRequiredTypes">The destination set that receives types whose signatures require a concrete header include.</param>
        void AddSignatureTypeReferences(ConversionClass conversionClass, ISet<string> referencedTypes, ISet<string> includeRequiredTypes) {
            foreach (ConversionVariable variable in conversionClass.Variables) {
                AddTypeReference(variable.VarType, referencedTypes, includeRequiredTypes, conversionClass.GenericArgs);
            }

            foreach (ConversionFunction function in conversionClass.Functions) {
                HashSet<string> excludedTypeNames = GetExcludedTypeNames(conversionClass, function);
                AddTypeReference(
                    function.ReturnType,
                    referencedTypes,
                    includeRequiredTypes,
                    excludedTypeNames,
                    allowReferenceForwardDeclaration: function.ReturnsReference || function.ReturnsConstReference || CanUseGeneratedEnumeratorForwardDeclaration(function.ReturnType));

                foreach (ConversionVariable parameter in function.InParameters) {
                    AddTypeReference(
                        parameter.VarType,
                        referencedTypes,
                        includeRequiredTypes,
                        excludedTypeNames,
                        allowReferenceForwardDeclaration: UsesReferenceSignature(parameter) || CanUseGeneratedEnumeratorForwardDeclaration(parameter.VarType));
                }
            }
        }

        /// <summary>
        /// Resolves whether one generated enumerator type can stay forward-declared in headers because it only appears in a function signature.
        /// </summary>
        /// <param name="variableType">Signature type being inspected.</param>
        /// <returns><c>true</c> when a generated enumerator forward declaration is sufficient; otherwise <c>false</c>.</returns>
        static bool CanUseGeneratedEnumeratorForwardDeclaration(VariableType variableType) {
            if (variableType == null) {
                return false;
            }

            string qualifiedTypeName = GetReferencedTypeName(variableType);
            if (string.IsNullOrWhiteSpace(qualifiedTypeName)) {
                return false;
            }

            return qualifiedTypeName.Contains("Enumerator", StringComparison.Ordinal);
        }

        /// <summary>
        /// Adds the referenced type and any nested generic argument types to the include set.
        /// </summary>
        /// <param name="variableType">The type metadata to inspect.</param>
        /// <param name="referencedTypes">The destination set that receives discovered type names.</param>
        /// <param name="includeRequiredTypes">The destination set that receives type names that require concrete header includes.</param>
        /// <param name="excludedTypeNames">Type names that should remain compile-time only and must not become includes.</param>
        void AddTypeReference(
            VariableType variableType,
            ISet<string> referencedTypes,
            ISet<string> includeRequiredTypes,
            IEnumerable<string> excludedTypeNames,
            bool allowReferenceForwardDeclaration = false) {
            if (variableType == null) {
                return;
            }

            if (variableType.IsGenericParameter) {
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
                : GetReferencedTypeName(variableType);

            if (!string.IsNullOrWhiteSpace(referencedTypeName) &&
                !excludedTypeNameSet.Contains(referencedTypeName) &&
                !excludedTypeNameSet.Contains(variableType.TypeName)) {
                referencedTypes.Add(referencedTypeName);
                if (!CanUseForwardDeclarationOnly(variableType, allowReferenceForwardDeclaration)) {
                    includeRequiredTypes.Add(referencedTypeName);
                }
            }

            if (variableType.GenericArgs == null) {
                return;
            }

            foreach (VariableType genericArgument in variableType.GenericArgs) {
                AddTypeReference(genericArgument, referencedTypes, includeRequiredTypes, excludedTypeNameSet);
            }
        }

        /// <summary>
        /// Chooses the most specific available source type name for dependency tracking, preferring qualified Roslyn metadata when present.
        /// </summary>
        /// <param name="variableType">Type metadata being recorded for include resolution.</param>
        /// <returns>Qualified type name when available; otherwise the leaf type name.</returns>
        static string GetReferencedTypeName(VariableType variableType) {
            if (variableType == null) {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(variableType.QualifiedTypeName)
                ? variableType.QualifiedTypeName
                : variableType.TypeName;
        }

        /// <summary>
        /// Determines whether a type used in a declaration can rely on a generated forward declaration without a concrete header include.
        /// </summary>
        /// <param name="variableType">Declaration type being evaluated.</param>
        /// <returns><c>true</c> when a forward declaration is sufficient; otherwise <c>false</c>.</returns>
        bool CanUseForwardDeclarationOnly(VariableType variableType, bool allowReferenceForwardDeclaration = false) {
            if (variableType == null || processor == null) {
                return false;
            }

            if (IsGeneratedDelegateType(variableType)) {
                return false;
            }

            processor.ConvertToCPPType(variableType, out CPPTypeData typeData);
            if (typeData.IsPointer && !typeData.IsArray && !typeData.IsNativeType) {
                return true;
            }

            return allowReferenceForwardDeclaration && !typeData.IsArray && !typeData.IsNativeType;
        }

        /// <summary>
        /// Determines whether one parameter lowers to a C++ reference signature that can rely on a forward declaration in the owning header.
        /// </summary>
        /// <param name="parameter">Parameter metadata to inspect.</param>
        /// <returns><c>true</c> when the emitted signature uses a reference; otherwise, <c>false</c>.</returns>
        static bool UsesReferenceSignature(ConversionVariable parameter) {
            if (parameter == null) {
                return false;
            }

            return parameter.Modifier.HasFlag(ParameterModifier.In) ||
                parameter.Modifier.HasFlag(ParameterModifier.Out) ||
                parameter.Modifier.HasFlag(ParameterModifier.Ref);
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
            string normalizedIncludeCandidate = NormalizeIncludeCandidateTypeName(referencedClass);
            if (string.IsNullOrWhiteSpace(normalizedIncludeCandidate)) {
                return false;
            }

            string normalizedReferencedClass = NormalizeReferencedClassName(referencedClass);
            string currentEmittedTypeName = conversionClass.GetEmittedTypeName();

            if (TryResolveGeneratedClass(normalizedIncludeCandidate, out ConversionClass generatedClass) &&
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

            string includePath = ResolveIncludePath(normalizedIncludeCandidate);
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

            if (generatedClass.DeclarationType == MemberDeclarationType.Delegate) {
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

            string normalizedIncludeCandidate = NormalizeIncludeCandidateTypeName(referencedClass);
            if (string.IsNullOrWhiteSpace(normalizedIncludeCandidate)) {
                return false;
            }

            VariableType variableType = VariableUtil.GetVarType(normalizedIncludeCandidate);
            variableType.QualifiedTypeName = normalizedIncludeCandidate;
            generatedClass = program.FindGeneratedClass(variableType);
            if (generatedClass != null) {
                return true;
            }

            string normalizedReferencedClass = NormalizeReferencedClassName(normalizedIncludeCandidate);
            generatedClass = program.Classes.FirstOrDefault(candidate =>
                !candidate.IsNative &&
                string.Equals(candidate.GetEmittedTypeName(), normalizedIncludeCandidate, StringComparison.Ordinal));

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
        void WriteSourcePreamble(ConversionClass conversionClass, TextWriter sourceWriter, CPPTypeRuntimeRequirementScope typeScope) {
            sourceWriter.WriteLine("#ifdef DrawText");
            sourceWriter.WriteLine("#undef DrawText");
            sourceWriter.WriteLine("#endif");
            sourceWriter.WriteLine($"#include \"{conversionClass.GetEmittedFileStem(program)}.hpp\"");

            HashSet<string> emittedIncludePaths = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> excludedTypeNames = GetExcludedTypeNames(conversionClass);
            HashSet<string> sourceIncludeTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (string referencedClass in conversionClass.ReferencedClasses.Distinct(StringComparer.Ordinal)) {
                sourceIncludeTypes.Add(referencedClass);
            }

            AddSourceSignatureTypeReferences(conversionClass, sourceIncludeTypes);
            AddReferencedGeneratedClassSignatureTypeReferences(conversionClass, sourceIncludeTypes);
            AddFunctionBodyTypeReferences(conversionClass, sourceIncludeTypes);

            foreach (string referencedClass in sourceIncludeTypes) {
                string normalizedReferencedClass = NormalizeReferencedClassName(referencedClass);
                string normalizedIncludeCandidate = NormalizeReferencedClassName(NormalizeIncludeCandidateTypeName(referencedClass));
                if (excludedTypeNames.Contains(referencedClass) ||
                    excludedTypeNames.Contains(normalizedReferencedClass) ||
                    excludedTypeNames.Contains(normalizedIncludeCandidate)) {
                    continue;
                }

                string includePath = ResolveIncludePath(referencedClass);
                if (string.IsNullOrWhiteSpace(includePath) || !emittedIncludePaths.Add(includePath)) {
                    continue;
                }

                sourceWriter.WriteLine($"#include \"{includePath}.hpp\"");
            }

            foreach (string sourceIncludePath in conversionClass.SourceIncludes.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)) {
                if (string.IsNullOrWhiteSpace(sourceIncludePath) || !emittedIncludePaths.Add(sourceIncludePath)) {
                    continue;
                }

                sourceWriter.WriteLine($"#include \"{sourceIncludePath}\"");
            }

            foreach (string requirementName in typeScope.GetRegisteredRequirements().OrderBy(name => name, StringComparer.Ordinal)) {
                if (processor == null ||
                    !processor.TryGetRuntimeRequirementDefinition(requirementName, out CPPRuntimeRequirementDefinition requirement)) {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(requirement.IncludePath) || !emittedIncludePaths.Add(requirement.IncludePath)) {
                    continue;
                }

                sourceWriter.WriteLine($"#include \"{requirement.IncludePath}\"");
            }

            if (conversionClass.Functions.Any(function => !function.HasBody) &&
                emittedIncludePaths.Add("runtime/native_exceptions.hpp")) {
                sourceWriter.WriteLine("#include \"runtime/native_exceptions.hpp\"");
            }

            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Collects member-signature types that source files must include concretely because implementation bodies commonly dereference those parameters and return types.
        /// </summary>
        /// <param name="conversionClass">The type whose member signatures should be scanned for source includes.</param>
        /// <param name="sourceIncludeTypes">Destination set that receives discovered concrete type names.</param>
        void AddSourceSignatureTypeReferences(ConversionClass conversionClass, ISet<string> sourceIncludeTypes) {
            foreach (ConversionVariable variable in conversionClass.Variables) {
                AddSourceTypeReference(variable.VarType, sourceIncludeTypes, conversionClass.GenericArgs);
            }

            foreach (ConversionFunction function in conversionClass.Functions) {
                HashSet<string> excludedTypeNames = GetExcludedTypeNames(conversionClass, function);
                AddSourceTypeReference(function.ReturnType, sourceIncludeTypes, excludedTypeNames);

                foreach (ConversionVariable parameter in function.InParameters) {
                    AddSourceTypeReference(parameter.VarType, sourceIncludeTypes, excludedTypeNames);
                }
            }
        }

        /// <summary>
        /// Adds concrete signature types exposed by directly referenced generated classes so chained property-return types do not remain incomplete in implementation files.
        /// </summary>
        /// <param name="conversionClass">The class whose direct generated dependencies should be scanned.</param>
        /// <param name="sourceIncludeTypes">Destination set that receives discovered concrete type names.</param>
        void AddReferencedGeneratedClassSignatureTypeReferences(ConversionClass conversionClass, ISet<string> sourceIncludeTypes) {
            if (conversionClass == null) {
                throw new ArgumentNullException(nameof(conversionClass));
            }
            if (sourceIncludeTypes == null) {
                throw new ArgumentNullException(nameof(sourceIncludeTypes));
            }

            foreach (string referencedClass in conversionClass.ReferencedClasses.Distinct(StringComparer.Ordinal)) {
                if (!TryResolveGeneratedClass(referencedClass, out ConversionClass generatedClass)) {
                    continue;
                }

                AddSourceSignatureTypeReferences(generatedClass, sourceIncludeTypes);
            }
        }

        /// <summary>
        /// Collects concrete type references that appear only inside function bodies so body-only generic arguments still emit the source includes they require.
        /// </summary>
        /// <param name="conversionClass">The type whose function bodies should be scanned.</param>
        /// <param name="sourceIncludeTypes">Destination set that receives discovered concrete type names.</param>
        void AddFunctionBodyTypeReferences(ConversionClass conversionClass, ISet<string> sourceIncludeTypes) {
            if (conversionClass == null) {
                throw new ArgumentNullException(nameof(conversionClass));
            }

            if (sourceIncludeTypes == null) {
                throw new ArgumentNullException(nameof(sourceIncludeTypes));
            }

            foreach (ConversionFunction function in conversionClass.Functions) {
                SemanticModel semantic = function.Semantic ?? conversionClass.Semantic;
                if (semantic == null) {
                    continue;
                }

                HashSet<string> excludedTypeNames = GetExcludedTypeNames(conversionClass, function);
                foreach (TypeSyntax bodyTypeSyntax in EnumerateFunctionBodyTypeSyntax(function)) {
                    VariableType bodyType = VariableUtil.GetVarType(bodyTypeSyntax, semantic);
                    AddSourceTypeReference(bodyType, sourceIncludeTypes, excludedTypeNames);
                }

                foreach (ITypeSymbol referencedTypeSymbol in EnumerateFunctionBodyReferencedTypeSymbols(function, semantic)) {
                    AddSourceTypeReference(VariableUtil.GetVarType(referencedTypeSymbol), sourceIncludeTypes, excludedTypeNames);
                    if (TryResolveDirectExternalGeneratedIncludePath(referencedTypeSymbol, out string directIncludePath) &&
                        !conversionClass.SourceIncludes.Contains(directIncludePath, StringComparer.Ordinal)) {
                        conversionClass.SourceIncludes.Add(directIncludePath);
                    }
                }
            }
        }

        /// <summary>
        /// Enumerates type syntax nodes that appear within one function body or expression body.
        /// </summary>
        /// <param name="function">Function whose body syntax should be scanned.</param>
        /// <returns>Sequence of type syntax nodes referenced within the function body.</returns>
        static IEnumerable<TypeSyntax> EnumerateFunctionBodyTypeSyntax(ConversionFunction function) {
            if (function == null) {
                yield break;
            }

            if (function.RawBlock != null) {
                foreach (TypeSyntax typeSyntax in function.RawBlock.DescendantNodes().OfType<TypeSyntax>()) {
                    yield return typeSyntax;
                }
            }

            if (function.ArrowExpression != null) {
                foreach (TypeSyntax typeSyntax in function.ArrowExpression.DescendantNodes().OfType<TypeSyntax>()) {
                    yield return typeSyntax;
                }
            }

            if (function.ConstructorInitializer != null) {
                foreach (TypeSyntax typeSyntax in function.ConstructorInitializer.DescendantNodes().OfType<TypeSyntax>()) {
                    yield return typeSyntax;
                }
            }
        }

        /// <summary>
        /// Enumerates containing type symbols referenced by one function body through member access so source files include owning generated types even when no explicit type syntax appears.
        /// </summary>
        /// <param name="function">Function whose body syntax should be scanned.</param>
        /// <param name="semantic">Semantic model used to resolve referenced symbols.</param>
        /// <returns>Sequence of containing type symbols required by the function body.</returns>
        static IEnumerable<ITypeSymbol> EnumerateFunctionBodyReferencedTypeSymbols(ConversionFunction function, SemanticModel semantic) {
            if (function == null || semantic == null) {
                yield break;
            }

            foreach (SyntaxNode rootNode in EnumerateFunctionBodyRoots(function)) {
                foreach (ExpressionSyntax expressionSyntax in rootNode.DescendantNodesAndSelf().OfType<ExpressionSyntax>()) {
                    SymbolInfo symbolInfo = semantic.GetSymbolInfo(expressionSyntax);
                    ISymbol referencedSymbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                    ITypeSymbol containingTypeSymbol = GetReferencedContainingTypeSymbol(referencedSymbol);
                    if (containingTypeSymbol != null) {
                        yield return containingTypeSymbol;
                    }
                }
            }
        }

        /// <summary>
        /// Enumerates one function's syntax roots that may contain body-only references.
        /// </summary>
        /// <param name="function">Function whose syntax roots should be returned.</param>
        /// <returns>Syntax roots that belong to the function body, expression body, or constructor initializer.</returns>
        static IEnumerable<SyntaxNode> EnumerateFunctionBodyRoots(ConversionFunction function) {
            if (function == null) {
                yield break;
            }

            if (function.RawBlock != null) {
                yield return function.RawBlock;
            }

            if (function.ArrowExpression != null) {
                yield return function.ArrowExpression;
            }

            if (function.ConstructorInitializer != null) {
                yield return function.ConstructorInitializer;
            }
        }

        /// <summary>
        /// Resolves the containing type that owns one referenced member symbol.
        /// </summary>
        /// <param name="referencedSymbol">Referenced symbol discovered in one function body.</param>
        /// <returns>The owning containing type when the symbol requires a generated include; otherwise <c>null</c>.</returns>
        static ITypeSymbol GetReferencedContainingTypeSymbol(ISymbol referencedSymbol) {
            return referencedSymbol switch {
                IMethodSymbol methodSymbol => methodSymbol.ContainingType,
                IPropertySymbol propertySymbol => propertySymbol.ContainingType,
                IFieldSymbol fieldSymbol => fieldSymbol.ContainingType,
                IEventSymbol eventSymbol => eventSymbol.ContainingType,
                _ => null
            };
        }

        /// <summary>
        /// Resolves one direct generated-header include path for a referenced external named type when the current conversion program does not own that generated class.
        /// </summary>
        /// <param name="referencedTypeSymbol">Referenced containing type symbol discovered in one function body.</param>
        /// <param name="includePath">Receives the direct generated-header include path when one can be inferred safely.</param>
        /// <returns>True when a direct generated-header include path was inferred; otherwise false.</returns>
        bool TryResolveDirectExternalGeneratedIncludePath(ITypeSymbol referencedTypeSymbol, out string includePath) {
            includePath = string.Empty;
            if (referencedTypeSymbol is not INamedTypeSymbol namedTypeSymbol) {
                return false;
            }

            if (namedTypeSymbol.SpecialType != SpecialType.None ||
                namedTypeSymbol.TypeKind == TypeKind.TypeParameter ||
                namedTypeSymbol.ContainingNamespace == null) {
                return false;
            }

            string namespaceName = namedTypeSymbol.ContainingNamespace.ToDisplayString();
            if (namespaceName.StartsWith("System", StringComparison.Ordinal) ||
                namespaceName.StartsWith("Microsoft", StringComparison.Ordinal)) {
                return false;
            }

            if (program.FindGeneratedClass(namedTypeSymbol) != null) {
                return false;
            }

            VariableType referencedType = VariableUtil.GetVarType(namedTypeSymbol);
            string resolvedIncludePath = TryResolveIncludePath(referencedType.QualifiedTypeName);
            if (!string.IsNullOrWhiteSpace(resolvedIncludePath)) {
                includePath = resolvedIncludePath + ".hpp";
                return true;
            }

            string leafTypeName = namedTypeSymbol.Name;
            if (string.IsNullOrWhiteSpace(leafTypeName)) {
                return false;
            }

            string emittedFileStem = namedTypeSymbol.Arity > 0
                ? $"{leafTypeName}_{namedTypeSymbol.Arity}"
                : leafTypeName;
            includePath = emittedFileStem + ".hpp";
            return true;
        }

        /// <summary>
        /// Adds one type and any nested generic argument types to the concrete source-include set.
        /// </summary>
        /// <param name="variableType">Type metadata to inspect.</param>
        /// <param name="sourceIncludeTypes">Destination set that receives concrete source include types.</param>
        /// <param name="excludedTypeNames">Type names that must remain compile-time only.</param>
        void AddSourceTypeReference(VariableType variableType, ISet<string> sourceIncludeTypes, IEnumerable<string> excludedTypeNames) {
            if (variableType == null) {
                return;
            }

            if (variableType.IsGenericParameter) {
                return;
            }

            HashSet<string> excludedTypeNameSet = excludedTypeNames == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(excludedTypeNames, StringComparer.Ordinal);

            if (variableType.IsNullable) {
                sourceIncludeTypes.Add("Nullable");
                return;
            }

            string referencedTypeName = variableType.GenericArgs.Count > 0
                ? variableType.ToString()
                : GetReferencedTypeName(variableType);

            if (!string.IsNullOrWhiteSpace(referencedTypeName) &&
                !excludedTypeNameSet.Contains(referencedTypeName) &&
                !excludedTypeNameSet.Contains(variableType.TypeName)) {
                sourceIncludeTypes.Add(referencedTypeName);
            }

            if (variableType.GenericArgs == null) {
                return;
            }

            foreach (VariableType genericArgument in variableType.GenericArgs) {
                AddSourceTypeReference(genericArgument, sourceIncludeTypes, excludedTypeNameSet);
            }
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

            if (string.Equals(referencedClass, "IEnumerator", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Collections.Generic.IEnumerator", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "IEnumerator", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "IEnumerator", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeIEnumerator");
                return "system/collections/generic/ienumerator";
            }

            if (string.Equals(referencedClass, "IEnumerable", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Collections.Generic.IEnumerable", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "IEnumerable", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "IEnumerable", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeIEnumerable");
                return "system/collections/generic/ienumerable";
            }

            if (string.Equals(referencedClass, "SpinLock", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Threading.SpinLock", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "SpinLock", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "SpinLock", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("SpinLock");
                return "system/threading/spin_lock";
            }

            if (string.Equals(referencedClass, "AutoResetEvent", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Threading.AutoResetEvent", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "AutoResetEvent", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "AutoResetEvent", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("AutoResetEvent");
                return "system/threading/auto_reset_event";
            }

            if (string.Equals(referencedClass, "Thread", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Threading.Thread", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Thread", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Thread", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Thread");
                return "system/threading/thread";
            }

            if (string.Equals(referencedClass, "NotImplementedException", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.NotImplementedException", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "NotImplementedException", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "NotImplementedException", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NotImplementedException");
                return "system/not_implemented_exception";
            }

            if (string.Equals(referencedClass, "Random", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Random", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Random", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Random", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Random");
                return "system/random";
            }

            if (string.Equals(referencedClass, "MulticastDelegate", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.MulticastDelegate", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "MulticastDelegate", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "MulticastDelegate", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("MulticastDelegate");
                return "system/multicast_delegate";
            }

            if (string.Equals(referencedClass, "ICloneable", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.ICloneable", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "ICloneable", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "ICloneable", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("ICloneable");
                return "system/icloneable";
            }

            if (string.Equals(referencedClass, "ISerializable", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Runtime.Serialization.ISerializable", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "ISerializable", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "ISerializable", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("ISerializable");
                return "system/runtime/serialization/iserializable";
            }

            if (string.Equals(referencedClass, "KeyValuePair", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Collections.Generic.KeyValuePair", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "KeyValuePair", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "KeyValuePair", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeKeyValuePair");
                return "system/collections/generic/key_value_pair";
            }

            if (string.Equals(referencedClass, "MemoryMarshal", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Runtime.InteropServices.MemoryMarshal", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "MemoryMarshal", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "MemoryMarshal", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("MemoryMarshal");
                return "system/runtime/interopservices/memory_marshal";
            }

            if (string.Equals(referencedClass, "Vector", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Numerics.Vector", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Vector", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Vector", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeVector");
                return "system/numerics/vector";
            }

            if (string.Equals(referencedClass, "Vector128", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Runtime.Intrinsics.Vector128", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Vector128", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Vector128", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeVector128");
                return "system/runtime/intrinsics/vector128";
            }

            if (string.Equals(referencedClass, "Vector256", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "Vector256Runtime", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Runtime.Intrinsics.Vector256", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Vector256", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Vector256Runtime", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Vector256", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeVector256");
                return "system/runtime/intrinsics/vector256";
            }

            if (string.Equals(referencedClass, "Vector512", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "Vector512Runtime", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Runtime.Intrinsics.Vector512", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Vector512", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Vector512Runtime", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Vector512", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeVector512");
                return "system/runtime/intrinsics/vector512";
            }

            if (string.Equals(referencedClass, "Sse", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Runtime.Intrinsics.X86.Sse", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Sse", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Sse", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Sse");
                return "system/runtime/intrinsics/x86/sse";
            }

            if (string.Equals(referencedClass, "Avx", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Runtime.Intrinsics.X86.Avx", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Avx", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Avx", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Avx");
                return "system/runtime/intrinsics/x86/avx";
            }

            if (string.Equals(referencedClass, "Avx2", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Runtime.Intrinsics.X86.Avx2", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Avx2", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Avx2", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Avx2");
                return "system/runtime/intrinsics/x86/avx2";
            }

            if (string.Equals(referencedClass, "Sse41", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Runtime.Intrinsics.X86.Sse41", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Sse41", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Sse41", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Sse41");
                return "system/runtime/intrinsics/x86/sse41";
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
                    processor.RegisterRuntimeRequirement("NativeType");
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

            if (string.Equals(referencedClass, "EqualityComparer", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Collections.Generic.EqualityComparer", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "EqualityComparer", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "EqualityComparer", StringComparison.Ordinal)) {
                return "system/collections/generic/equality_comparer";
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

            if (string.Equals(referencedClass, "BitOperations", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Numerics.BitOperations", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "BitOperations", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "BitOperations", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("BitOperations");
                return "system/bit_operations";
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

            if (string.Equals(referencedClass, "Stopwatch", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.Diagnostics.Stopwatch", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "Stopwatch", StringComparison.Ordinal) ||
                string.Equals(referencedTypeName, "Stopwatch", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Stopwatch");
                return "system/diagnostics/stopwatch";
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

            if (string.Equals(referencedClass, "nint", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "nint", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "IntPtr", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "System.IntPtr", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "IntPtr", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "nuint", StringComparison.Ordinal) ||
                string.Equals(normalizedReferencedClass, "nuint", StringComparison.Ordinal) ||
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

            if (string.Equals(referencedClass, "System.Buffer", StringComparison.Ordinal) ||
                string.Equals(referencedClass, "global::System.Buffer", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Buffer");
                return string.Empty;
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
            string normalizedIncludeCandidate = NormalizeIncludeCandidateTypeName(referencedClass);
            if (string.IsNullOrWhiteSpace(normalizedIncludeCandidate)) {
                return string.Empty;
            }

            VariableType variableType = VariableUtil.GetVarType(normalizedIncludeCandidate);

            if (normalizedIncludeCandidate.Contains("[]", StringComparison.Ordinal) || variableType.Type == VariableDataType.Array) {
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

            if (string.Equals(variableType.TypeName, "FunctionPointer", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeFunctionPointer");
                return "runtime/function_pointer";
            }

            if (string.Equals(variableType.TypeName, "Stack", StringComparison.Ordinal)) {
                return "runtime/native_stack";
            }

            if (string.Equals(variableType.TypeName, "Interlocked", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Threading.Interlocked", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Interlocked");
                return "system/threading/interlocked";
            }

            if (string.Equals(variableType.TypeName, "Volatile", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Threading.Volatile", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Volatile");
                return "system/threading/volatile";
            }

            if (string.Equals(variableType.TypeName, "Vector256", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "Vector256Runtime", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Runtime.Intrinsics.Vector256", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeVector256");
                return "system/runtime/intrinsics/vector256";
            }

            if (string.Equals(variableType.TypeName, "Vector512", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "Vector512Runtime", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Runtime.Intrinsics.Vector512", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NativeVector512");
                return "system/runtime/intrinsics/vector512";
            }

            if (string.Equals(variableType.TypeName, "Sse", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Runtime.Intrinsics.X86.Sse", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Sse");
                return "system/runtime/intrinsics/x86/sse";
            }

            if (string.Equals(variableType.TypeName, "Avx", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Runtime.Intrinsics.X86.Avx", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Avx");
                return "system/runtime/intrinsics/x86/avx";
            }

            if (string.Equals(variableType.TypeName, "Avx2", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Runtime.Intrinsics.X86.Avx2", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Avx2");
                return "system/runtime/intrinsics/x86/avx2";
            }

            if (string.Equals(variableType.TypeName, "Thread", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Threading.Thread", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("Thread");
                return "system/threading/thread";
            }

            if (string.Equals(variableType.TypeName, "NotImplementedException", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.NotImplementedException", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("NotImplementedException");
                return "system/not_implemented_exception";
            }

            if (string.Equals(variableType.TypeName, "SpinLock", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Threading.SpinLock", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("SpinLock");
                return "system/threading/spin_lock";
            }

            if (string.Equals(variableType.TypeName, "SpinWait", StringComparison.Ordinal) ||
                string.Equals(variableType.TypeName, "System.Threading.SpinWait", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("SpinWait");
                return "system/threading/spin_wait";
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

            if (string.Equals(variableType.TypeName, "BitOperations", StringComparison.Ordinal)) {
                processor?.RegisterRuntimeRequirement("BitOperations");
                return "system/bit_operations";
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

            if (variableType.TryResolveConfiguredTypeRemapIncludePath(program, out string remappedIncludePath)) {
                return remappedIncludePath;
            }

            ConversionClass generatedClass = null;
            if (TryResolveGeneratedClass(normalizedIncludeCandidate, out generatedClass)) {
                return generatedClass.GetEmittedFileStem(program);
            }

            generatedClass = program.FindGeneratedClass(variableType);
            if (generatedClass != null) {
                return generatedClass.GetEmittedFileStem(program);
            }

            if (variableType.TryBuildQualifiedLowercaseFileStem(out string qualifiedIncludePath)) {
                return qualifiedIncludePath;
            }

            CPPKnownClass knownSourceClass = program.Requirements.FirstOrDefault(requirement => requirement.Name == variableType.TypeName);
            if (knownSourceClass != null && !string.IsNullOrWhiteSpace(knownSourceClass.Path)) {
                return knownSourceClass.Path;
            }

            CPPTypeData typeData = new CPPTypeData();

            if (processor != null) {
                variableType = processor.ConvertToCPPType(variableType, out typeData);
            }

            if (TryResolveGeneratedClass(variableType.TypeName, out generatedClass)) {
                return generatedClass.GetEmittedFileStem(program);
            }

            generatedClass = program.FindGeneratedClass(variableType);
            if (generatedClass != null) {
                return generatedClass.GetEmittedFileStem(program);
            }

            if (typeData.IsNativeType) {
                return string.Empty;
            }

            CPPKnownClass knownClass = program.Requirements.FirstOrDefault(requirement => requirement.Name == variableType.TypeName);
            if (knownClass != null && !string.IsNullOrWhiteSpace(knownClass.Path)) {
                return knownClass.Path;
            }

            return string.Empty;
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
        /// Removes ref-like modifiers and pointer suffixes from include candidates so pseudo-types do not become invalid generated header names.
        /// </summary>
        /// <param name="referencedClass">Raw referenced type text captured from preprocessing.</param>
        /// <returns>Normalized include candidate, or an empty string when the candidate should not produce a header include.</returns>
        static string NormalizeIncludeCandidateTypeName(string referencedClass) {
            if (string.IsNullOrWhiteSpace(referencedClass)) {
                return string.Empty;
            }

            string normalized = referencedClass.Trim();
            bool removedPrefix;
            do {
                removedPrefix = false;
                removedPrefix |= TryTrimPrefix(ref normalized, "scoped ");
                removedPrefix |= TryTrimPrefix(ref normalized, "readonly ");
                removedPrefix |= TryTrimPrefix(ref normalized, "ref ");
                removedPrefix |= TryTrimPrefix(ref normalized, "out ");
                removedPrefix |= TryTrimPrefix(ref normalized, "in ");
                removedPrefix |= TryTrimPrefix(ref normalized, "params ");
            } while (removedPrefix);

            normalized = normalized.TrimEnd('*', '&').Trim();
            if (string.IsNullOrWhiteSpace(normalized) ||
                string.Equals(normalized, "var", StringComparison.Ordinal)) {
                return string.Empty;
            }

            return normalized;
        }

        /// <summary>
        /// Removes one known prefix from a referenced type candidate when present.
        /// </summary>
        /// <param name="value">Candidate text to normalize.</param>
        /// <param name="prefix">Prefix to remove.</param>
        /// <returns>True when the prefix was removed; otherwise false.</returns>
        static bool TryTrimPrefix(ref string value, string prefix) {
            if (!value.StartsWith(prefix, StringComparison.Ordinal)) {
                return false;
            }

            value = value[prefix.Length..].TrimStart();
            return true;
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
            string emittedTypeName = conversionClass.GetEmittedTypeName();
            string inheritance = GetInheritanceClause(conversionClass);
            int layoutPack = GetPackedStructLayoutPack(conversionClass);
            if (layoutPack > 0) {
                headerWriter.WriteLine($"#pragma pack(push, {layoutPack})");
            }

            WriteTemplateDeclaration(conversionClass, headerWriter);

            if (string.IsNullOrWhiteSpace(inheritance)) {
                headerWriter.WriteLine($"class {emittedTypeName}");
            } else {
                headerWriter.WriteLine($"class {emittedTypeName} : {inheritance}");
            }

            headerWriter.WriteLine("{");
            WriteNestedTypeFriendDeclarations(conversionClass, headerWriter);

            if (conversionClass.DeclarationType == MemberDeclarationType.Interface) {
                WriteInterfaceSection(conversionClass, headerWriter, sourceWriter);
                headerWriter.WriteLine("};");
                if (conversionClass.HasExplicitLayout) {
                    headerWriter.WriteLine("#pragma pack(pop)");
                }
                WriteFreeOperatorFunctions(conversionClass, headerWriter, sourceWriter);
                return;
            }

            bool wroteAnySection = false;
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Public, headerWriter, sourceWriter);
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Protected, headerWriter, sourceWriter);
            wroteAnySection |= WriteAccessSection(conversionClass, MemberAccessType.Private, headerWriter, sourceWriter);

            if (!wroteAnySection) {
                headerWriter.WriteLine("public:");
            }

            if (ShouldEmitSequentialLayoutTailPadding(conversionClass)) {
                WriteSequentialLayoutTailPadding(conversionClass, headerWriter);
            }

            headerWriter.WriteLine("};");
            if (layoutPack > 0) {
                headerWriter.WriteLine("#pragma pack(pop)");
            }
            WriteFreeOperatorFunctions(conversionClass, headerWriter, sourceWriter);
        }

        void WriteDelegate(ConversionClass conversionClass, TextWriter headerWriter) {
            if (conversionClass == null) {
                throw new ArgumentNullException(nameof(conversionClass));
            }

            ConversionFunction invokeFunction = conversionClass.Functions.FirstOrDefault();
            if (invokeFunction == null) {
                throw new InvalidOperationException($"Delegate '{conversionClass.Name}' is missing its invoke signature.");
            }

            headerWriter.WriteLine("#include \"system/delegate.hpp\"");
            headerWriter.WriteLine();
            WriteTemplateDeclaration(conversionClass, headerWriter);
            headerWriter.WriteLine($"using {conversionClass.GetEmittedTypeName()} = {BuildDelegateAliasType(conversionClass, invokeFunction)};");
        }

        string BuildDelegateAliasType(ConversionClass conversionClass, ConversionFunction invokeFunction) {
            List<string> signatureParts = new List<string> {
                GetReturnType(conversionClass, invokeFunction)
            };

            foreach (ConversionVariable parameter in invokeFunction.InParameters) {
                string parameterType = ConvertType(parameter.VarType, conversionClass, invokeFunction);
                if ((parameter.Modifier & (ParameterModifier.Out | ParameterModifier.Ref)) != 0) {
                    parameterType += "&";
                }

                signatureParts.Add(parameterType);
            }

            return $"Delegate<{string.Join(", ", signatureParts)}>";
        }

        void WriteNestedTypeFriendDeclarations(ConversionClass conversionClass, TextWriter headerWriter) {
            if (conversionClass?.TypeSymbol == null) {
                return;
            }

            string emittedTypePrefix = conversionClass.GetEmittedTypeName() + "_";
            foreach (ConversionClass nestedType in program.Classes.Where(candidate =>
                         candidate != null &&
                         candidate.DeclarationType != MemberDeclarationType.Delegate &&
                         candidate.DeclarationType != MemberDeclarationType.Enum &&
                         ((candidate.TypeSymbol?.ContainingType != null &&
                           SymbolEqualityComparer.Default.Equals(candidate.TypeSymbol.ContainingType, conversionClass.TypeSymbol)) ||
                          candidate.GetEmittedTypeName().StartsWith(emittedTypePrefix, StringComparison.Ordinal)))) {
                if (nestedType.GenericArgs != null && nestedType.GenericArgs.Count > 0) {
                    string templateArguments = string.Join(", ", nestedType.GenericArgs.Select((argument, index) => $"typename TFriendArg{index}"));
                    headerWriter.WriteLine($"template <{templateArguments}>");
                }

                headerWriter.WriteLine($"friend class {nestedType.GetEmittedTypeName()};");
            }
        }

        string GetInheritanceClause(ConversionClass conversionClass) {
            if (conversionClass.TypeSymbol is INamedTypeSymbol typeSymbol) {
                List<string> inheritanceParts = new List<string>();
                bool isValueType = typeSymbol.IsValueType;

                if (!isValueType &&
                    typeSymbol.TypeKind != TypeKind.Interface &&
                    typeSymbol.BaseType != null &&
                    typeSymbol.BaseType.SpecialType != SpecialType.System_Object &&
                    typeSymbol.BaseType.SpecialType != SpecialType.System_ValueType) {
                    inheritanceParts.Add($"public {RenderInheritanceType(typeSymbol.BaseType)}");
                }

                if (!isValueType) {
                    foreach (INamedTypeSymbol interfaceSymbol in typeSymbol.Interfaces) {
                        inheritanceParts.Add($"public {RenderInheritanceType(interfaceSymbol)}");
                    }
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
            HashSet<string> includeRequiredTypes,
            HashSet<string> excludedTypeNames) {
            if (conversionClass.TypeSymbol is not INamedTypeSymbol typeSymbol) {
                return;
            }

            if (!typeSymbol.IsValueType &&
                typeSymbol.TypeKind != TypeKind.Interface &&
                typeSymbol.BaseType != null &&
                typeSymbol.BaseType.SpecialType != SpecialType.System_Object &&
                typeSymbol.BaseType.SpecialType != SpecialType.System_ValueType) {
                AddInheritanceTypeReference(typeSymbol.BaseType, referencedTypes, includeRequiredTypes, excludedTypeNames);
            }

            if (!typeSymbol.IsValueType) {
                foreach (INamedTypeSymbol interfaceSymbol in typeSymbol.AllInterfaces) {
                    AddInheritanceTypeReference(interfaceSymbol, referencedTypes, includeRequiredTypes, excludedTypeNames);
                }
            }
        }

        void AddInheritanceTypeReference(
            INamedTypeSymbol typeSymbol,
            HashSet<string> referencedTypes,
            HashSet<string> includeRequiredTypes,
            HashSet<string> excludedTypeNames) {
            string typeName = program.FindGeneratedClass(typeSymbol)?.GetEmittedTypeName()
                ?? NormalizeReferencedClassName(typeSymbol.Name);
            if (!excludedTypeNames.Contains(typeName)) {
                referencedTypes.Add(typeName);
                includeRequiredTypes.Add(typeName);
            }

            foreach (ITypeSymbol typeArgument in typeSymbol.TypeArguments) {
                VariableType argumentType = VariableUtil.GetVarType(typeArgument);
                AddTypeReference(argumentType, referencedTypes, includeRequiredTypes, excludedTypeNames);
            }
        }

        string RenderInheritanceType(INamedTypeSymbol typeSymbol) {
            ConversionClass generatedClass = program.FindGeneratedClass(typeSymbol);
            string emittedTypeName = generatedClass?.GetEmittedTypeName()
                ?? NormalizeReferencedClassName(typeSymbol.Name);

            List<ITypeSymbol> effectiveTypeArguments = new List<ITypeSymbol>();
            if (generatedClass?.GenericArgs != null &&
                generatedClass.GenericArgs.Count > typeSymbol.TypeArguments.Length &&
                typeSymbol.ContainingType != null) {
                AppendContainingTypeArguments(typeSymbol.ContainingType, effectiveTypeArguments);
            }

            effectiveTypeArguments.AddRange(typeSymbol.TypeArguments);
            if (effectiveTypeArguments.Count == 0) {
                return $"::{emittedTypeName}";
            }

            List<string> renderedArguments = new List<string>();
            foreach (ITypeSymbol effectiveTypeArgument in effectiveTypeArguments) {
                VariableType sourceType = VariableUtil.GetVarType(effectiveTypeArgument);
                renderedArguments.Add(ConvertType(sourceType));
            }

            return $"::{emittedTypeName}<{string.Join(", ", renderedArguments)}>";
        }

        static void AppendContainingTypeArguments(INamedTypeSymbol containingTypeSymbol, List<ITypeSymbol> effectiveTypeArguments) {
            if (containingTypeSymbol == null || effectiveTypeArguments == null) {
                return;
            }

            if (containingTypeSymbol.ContainingType != null) {
                AppendContainingTypeArguments(containingTypeSymbol.ContainingType, effectiveTypeArguments);
            }

            foreach (ITypeSymbol typeArgument in containingTypeSymbol.TypeArguments) {
                effectiveTypeArguments.Add(typeArgument);
            }
        }

        /// <summary>
        /// Emits interface members as a single public section with accessor-based properties and pure virtual functions.
        /// </summary>
        /// <param name="conversionClass">The interface declaration being emitted.</param>
        /// <param name="headerWriter">Writer that receives the header declaration.</param>
        /// <param name="sourceWriter">Writer that receives source definitions for computed members.</param>
        void WriteInterfaceSection(ConversionClass conversionClass, TextWriter headerWriter, TextWriter sourceWriter) {
            headerWriter.WriteLine("public:");

            List<Action> writers = new List<Action>();
            HashSet<string> emittedFunctionKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (ConversionVariable variable in conversionClass.Variables) {
                if (variable.IsGet || variable.IsSet) {
                    writers.Add(() => WriteComputedProperty(conversionClass, variable, headerWriter, sourceWriter));
                    continue;
                }

                writers.Add(() => WriteField(conversionClass, variable, headerWriter, sourceWriter));
            }

            foreach (ConversionFunction function in conversionClass.Functions) {
                if (IsFreeOperatorFunction(function)) {
                    writers.Add(() => WriteFriendOperatorDeclaration(conversionClass, function, headerWriter));
                    continue;
                }

                if (function.GenericParameters != null && function.GenericParameters.Count > 0) {
                    continue;
                }

                if (!emittedFunctionKeys.Add(BuildFunctionEmissionKey(conversionClass, function))) {
                    continue;
                }

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
            HashSet<string> emittedFunctionKeys = new HashSet<string>(StringComparer.Ordinal);

            if (accessType == MemberAccessType.Public &&
                conversionClass.DeclarationType != MemberDeclarationType.Interface &&
                ShouldEmitVirtualDestructor(conversionClass)) {
                writers.Add(() => headerWriter.WriteLine($"    virtual ~{conversionClass.GetEmittedTypeName()}() = default;"));
            }

            if (accessType == MemberAccessType.Public &&
                ShouldEmitImplicitDefaultConstructor(conversionClass)) {
                writers.Add(() => WriteImplicitDefaultConstructor(conversionClass, headerWriter, sourceWriter));
            }

            if (ShouldWriteExplicitLayoutFieldsInSection(conversionClass, accessType)) {
                writers.Add(() => WriteExplicitLayoutFields(conversionClass, headerWriter, sourceWriter));
            }

            foreach (ConversionVariable variable in conversionClass.Variables.Where(variable => variable.AccessType == accessType)) {
                if (IsComputedProperty(variable)) {
                    writers.Add(() => WriteComputedProperty(conversionClass, variable, headerWriter, sourceWriter));
                    continue;
                }

                if (variable.IsGet || variable.IsSet) {
                    writers.Add(() => WriteStorageBackedProperty(conversionClass, variable, headerWriter, sourceWriter));
                    continue;
                }

                if (conversionClass.HasExplicitLayout && IsExplicitLayoutInstanceField(variable)) {
                    continue;
                }

                writers.Add(() => WriteField(conversionClass, variable, headerWriter, sourceWriter));
            }

            foreach (ConversionFunction function in conversionClass.Functions.Where(function => GetEffectiveFunctionAccessType(function) == accessType)) {
                if (IsFreeOperatorFunction(function)) {
                    writers.Add(() => WriteFriendOperatorDeclaration(conversionClass, function, headerWriter));
                    continue;
                }

                if (!emittedFunctionKeys.Add(BuildFunctionEmissionKey(conversionClass, function))) {
                    continue;
                }

                writers.Add(() => WriteFunction(conversionClass, function, headerWriter, sourceWriter));
            }

            if (accessType == MemberAccessType.Public) {
                HashSet<string> plannedAccessorNames = BuildPlannedPropertyAccessorNames(conversionClass);
                AddInterfacePropertyBridgeWriters(conversionClass, plannedAccessorNames, writers, headerWriter, sourceWriter);
                AddBasePropertyBridgeWriters(conversionClass, plannedAccessorNames, writers, headerWriter, sourceWriter);
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
        /// Determines whether the converted type should expose a virtual destructor in native output.
        /// </summary>
        /// <param name="conversionClass">Type metadata being emitted.</param>
        /// <returns><c>true</c> for reference types that participate in polymorphic ownership; otherwise <c>false</c>.</returns>
        static bool ShouldEmitVirtualDestructor(ConversionClass conversionClass) {
            return conversionClass?.TypeSymbol == null ||
                conversionClass.TypeSymbol.TypeKind == TypeKind.Class;
        }

        /// <summary>
        /// Promotes certain non-public generic overrides to public in native output when runtime dispatch rewrites need to invoke them directly on concrete generated implementations.
        /// </summary>
        /// <param name="function">Function being assigned to one native access section.</param>
        /// <returns>The effective access bucket to use for native emission.</returns>
        static MemberAccessType GetEffectiveFunctionAccessType(ConversionFunction function) {
            if (function == null) {
                return MemberAccessType.Private;
            }

            if (function.AccessType != MemberAccessType.Public &&
                function.GenericParameters != null &&
                function.GenericParameters.Count > 0 &&
                (function.IsOverride || function.DeclarationType == MemberDeclarationType.Abstract)) {
                return MemberAccessType.Public;
            }

            return function.AccessType;
        }

        /// <summary>
        /// Adds bridge accessors for interface properties implemented through another base so the generated C++ type is no longer abstract.
        /// </summary>
        /// <param name="conversionClass">Concrete generated class being emitted.</param>
        /// <param name="writers">Writer list that receives any needed bridge emitters.</param>
        /// <param name="headerWriter">Writer that receives declarations.</param>
        /// <param name="sourceWriter">Writer that receives definitions.</param>
        void AddInterfacePropertyBridgeWriters(ConversionClass conversionClass, HashSet<string> plannedAccessorNames, List<Action> writers, TextWriter headerWriter, TextWriter sourceWriter) {
            if (conversionClass.DeclarationType == MemberDeclarationType.Interface ||
                conversionClass.TypeSymbol is not INamedTypeSymbol typeSymbol) {
                return;
            }

            foreach (INamedTypeSymbol interfaceSymbol in typeSymbol.Interfaces) {
                foreach (ISymbol member in interfaceSymbol.GetMembers()) {
                    if (member is not IPropertySymbol interfacePropertySymbol) {
                        continue;
                    }

                    if (FindInterfacePropertyImplementation(typeSymbol, interfacePropertySymbol) is not IPropertySymbol implementationPropertySymbol ||
                        SymbolEqualityComparer.Default.Equals(implementationPropertySymbol.ContainingType, typeSymbol)) {
                        continue;
                    }

                    if (interfacePropertySymbol.GetMethod != null &&
                        plannedAccessorNames.Add($"get_{interfacePropertySymbol.Name}")) {
                        writers.Add(() => WriteInterfacePropertyBridgeGetter(conversionClass, interfacePropertySymbol, implementationPropertySymbol, headerWriter, sourceWriter));
                    }

                    if (interfacePropertySymbol.SetMethod != null &&
                        plannedAccessorNames.Add($"set_{interfacePropertySymbol.Name}")) {
                        writers.Add(() => WriteInterfacePropertyBridgeSetter(conversionClass, interfacePropertySymbol, implementationPropertySymbol, headerWriter, sourceWriter));
                    }
                }
            }
        }

        /// <summary>
        /// Adds bridge accessors for inherited base-class properties so multiple-inheritance interface contracts can resolve to a concrete final overrider.
        /// </summary>
        /// <param name="conversionClass">Concrete generated class being emitted.</param>
        /// <param name="writers">Writer list that receives any needed bridge emitters.</param>
        /// <param name="headerWriter">Writer that receives declarations.</param>
        /// <param name="sourceWriter">Writer that receives definitions.</param>
        void AddBasePropertyBridgeWriters(ConversionClass conversionClass, HashSet<string> plannedAccessorNames, List<Action> writers, TextWriter headerWriter, TextWriter sourceWriter) {
            if (conversionClass.TypeSymbol?.IsAbstract == true) {
                return;
            }

            for (INamedTypeSymbol baseTypeSymbol = conversionClass.TypeSymbol?.BaseType; baseTypeSymbol != null; baseTypeSymbol = baseTypeSymbol.BaseType) {
                ConversionClass baseConversionClass = program.FindGeneratedClass(baseTypeSymbol);
                if (baseConversionClass == null) {
                    continue;
                }

                foreach (ConversionVariable baseVariable in baseConversionClass.Variables.Where(variable => variable.AccessType == MemberAccessType.Public && (variable.IsGet || variable.IsSet))) {
                    if (baseVariable.IsGet &&
                        plannedAccessorNames.Add($"get_{baseVariable.Name}")) {
                        writers.Add(() => WriteBasePropertyBridgeGetter(conversionClass, baseConversionClass, baseVariable, headerWriter, sourceWriter));
                    }

                    if (baseVariable.IsSet &&
                        plannedAccessorNames.Add($"set_{baseVariable.Name}")) {
                        writers.Add(() => WriteBasePropertyBridgeSetter(conversionClass, baseConversionClass, baseVariable, headerWriter, sourceWriter));
                    }
                }
            }
        }

        /// <summary>
        /// Builds the set of property accessor names that the current class already emits directly.
        /// </summary>
        /// <param name="conversionClass">Class being inspected before bridge emission.</param>
        /// <returns>A mutable set seeded with the accessors already owned by the class.</returns>
        static HashSet<string> BuildPlannedPropertyAccessorNames(ConversionClass conversionClass) {
            HashSet<string> plannedAccessorNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (ConversionFunction function in conversionClass.Functions) {
                plannedAccessorNames.Add(function.Name);
            }

            foreach (ConversionVariable variable in conversionClass.Variables) {
                if (variable.IsGet) {
                    plannedAccessorNames.Add($"get_{variable.Name}");
                }

                if (variable.IsSet) {
                    plannedAccessorNames.Add($"set_{variable.Name}");
                }
            }

            return plannedAccessorNames;
        }

        /// <summary>
        /// Resolves the property symbol that actually satisfies an interface property, falling back to base-type name matching when Roslyn does not surface the inherited implementation directly.
        /// </summary>
        /// <param name="typeSymbol">Concrete type that implements the interface.</param>
        /// <param name="interfacePropertySymbol">Interface property contract.</param>
        /// <returns>The implementing property symbol when one can be resolved; otherwise <c>null</c>.</returns>
        static IPropertySymbol FindInterfacePropertyImplementation(INamedTypeSymbol typeSymbol, IPropertySymbol interfacePropertySymbol) {
            if (typeSymbol.FindImplementationForInterfaceMember(interfacePropertySymbol) is IPropertySymbol directImplementationPropertySymbol) {
                return directImplementationPropertySymbol;
            }

            for (INamedTypeSymbol currentTypeSymbol = typeSymbol.BaseType; currentTypeSymbol != null; currentTypeSymbol = currentTypeSymbol.BaseType) {
                IPropertySymbol inheritedPropertySymbol = currentTypeSymbol.GetMembers(interfacePropertySymbol.Name)
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault();
                if (inheritedPropertySymbol != null) {
                    return inheritedPropertySymbol;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether the current class already emits the requested property accessor directly.
        /// </summary>
        /// <param name="conversionClass">The class being inspected.</param>
        /// <param name="propertyName">Source property name.</param>
        /// <param name="isGetter"><c>true</c> to check for a getter; otherwise checks for a setter.</param>
        /// <returns><c>true</c> when the accessor is already emitted on the class itself.</returns>
        /// <summary>
        /// Emits a bridge getter that forwards an interface contract to the actual implementation on another base.
        /// </summary>
        /// <param name="conversionClass">Concrete generated class receiving the bridge.</param>
        /// <param name="interfacePropertySymbol">Interface property contract.</param>
        /// <param name="implementationPropertySymbol">Resolved implementation symbol supplied by another base.</param>
        /// <param name="headerWriter">Writer that receives declarations.</param>
        /// <param name="sourceWriter">Writer that receives definitions.</param>
        void WriteInterfacePropertyBridgeGetter(ConversionClass conversionClass, IPropertySymbol interfacePropertySymbol, IPropertySymbol implementationPropertySymbol, TextWriter headerWriter, TextWriter sourceWriter) {
            string typeName = GetPropertyGetterReturnType(
                conversionClass,
                VariableUtil.GetVarType(interfacePropertySymbol.Type),
                interfacePropertySymbol.RefKind == RefKind.Ref,
                interfacePropertySymbol.RefKind == RefKind.RefReadOnly);
            string accessorName = $"get_{interfacePropertySymbol.Name}";
            string qualifier = GetInterfaceBridgeQualifier(conversionClass, implementationPropertySymbol.ContainingType);

            headerWriter.WriteLine($"    {typeName} {accessorName}();");

            WriteTemplateDeclaration(conversionClass, sourceWriter);
            sourceWriter.WriteLine($"{typeName} {GetQualifiedClassName(conversionClass)}::{accessorName}()");
            sourceWriter.WriteLine("{");
            sourceWriter.WriteLine($"return {qualifier}get_{implementationPropertySymbol.Name}();");
            sourceWriter.WriteLine("}");
            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Emits a bridge setter that forwards an interface contract to the actual implementation on another base.
        /// </summary>
        /// <param name="conversionClass">Concrete generated class receiving the bridge.</param>
        /// <param name="interfacePropertySymbol">Interface property contract.</param>
        /// <param name="implementationPropertySymbol">Resolved implementation symbol supplied by another base.</param>
        /// <param name="headerWriter">Writer that receives declarations.</param>
        /// <param name="sourceWriter">Writer that receives definitions.</param>
        void WriteInterfacePropertyBridgeSetter(ConversionClass conversionClass, IPropertySymbol interfacePropertySymbol, IPropertySymbol implementationPropertySymbol, TextWriter headerWriter, TextWriter sourceWriter) {
            string typeName = ConvertType(VariableUtil.GetVarType(interfacePropertySymbol.Type), conversionClass);
            string accessorName = $"set_{interfacePropertySymbol.Name}";
            string qualifier = GetInterfaceBridgeQualifier(conversionClass, implementationPropertySymbol.ContainingType);

            headerWriter.WriteLine($"    void {accessorName}({typeName} value);");

            WriteTemplateDeclaration(conversionClass, sourceWriter);
            sourceWriter.WriteLine($"void {GetQualifiedClassName(conversionClass)}::{accessorName}({typeName} value)");
            sourceWriter.WriteLine("{");
            sourceWriter.WriteLine($"{qualifier}set_{implementationPropertySymbol.Name}(value);");
            sourceWriter.WriteLine("}");
            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Emits a bridge getter that forwards to an inherited base-class accessor with the same property contract.
        /// </summary>
        /// <param name="conversionClass">Concrete generated class receiving the bridge.</param>
        /// <param name="baseConversionClass">Base class that already owns the accessor implementation.</param>
        /// <param name="baseVariable">Base property model being forwarded.</param>
        /// <param name="headerWriter">Writer that receives declarations.</param>
        /// <param name="sourceWriter">Writer that receives definitions.</param>
        void WriteBasePropertyBridgeGetter(ConversionClass conversionClass, ConversionClass baseConversionClass, ConversionVariable baseVariable, TextWriter headerWriter, TextWriter sourceWriter) {
            string typeName = GetPropertyGetterReturnType(conversionClass, baseVariable);
            string accessorName = $"get_{baseVariable.Name}";
            string qualifier = GetBasePropertyBridgeQualifier(conversionClass, baseConversionClass);

            headerWriter.WriteLine($"    {typeName} {accessorName}();");

            WriteTemplateDeclaration(conversionClass, sourceWriter);
            sourceWriter.WriteLine($"{typeName} {GetQualifiedClassName(conversionClass)}::{accessorName}()");
            sourceWriter.WriteLine("{");
            sourceWriter.WriteLine($"return {qualifier}{accessorName}();");
            sourceWriter.WriteLine("}");
            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Emits a bridge setter that forwards to an inherited base-class accessor with the same property contract.
        /// </summary>
        /// <param name="conversionClass">Concrete generated class receiving the bridge.</param>
        /// <param name="baseConversionClass">Base class that already owns the accessor implementation.</param>
        /// <param name="baseVariable">Base property model being forwarded.</param>
        /// <param name="headerWriter">Writer that receives declarations.</param>
        /// <param name="sourceWriter">Writer that receives definitions.</param>
        void WriteBasePropertyBridgeSetter(ConversionClass conversionClass, ConversionClass baseConversionClass, ConversionVariable baseVariable, TextWriter headerWriter, TextWriter sourceWriter) {
            string typeName = ConvertType(baseVariable.VarType, conversionClass);
            string accessorName = $"set_{baseVariable.Name}";
            string qualifier = GetBasePropertyBridgeQualifier(conversionClass, baseConversionClass);

            headerWriter.WriteLine($"    void {accessorName}({typeName} value);");

            WriteTemplateDeclaration(conversionClass, sourceWriter);
            sourceWriter.WriteLine($"void {GetQualifiedClassName(conversionClass)}::{accessorName}({typeName} value)");
            sourceWriter.WriteLine("{");
            sourceWriter.WriteLine($"{qualifier}{accessorName}(value);");
            sourceWriter.WriteLine("}");
            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Builds the qualified base-type prefix used by inherited property bridges, preserving concrete generic arguments from the current derived type.
        /// </summary>
        /// <param name="conversionClass">Concrete generated class receiving the bridge.</param>
        /// <param name="baseConversionClass">Base class that already owns the accessor implementation.</param>
        /// <returns>C++ base-type qualifier ending in <c>::</c>.</returns>
        string GetBasePropertyBridgeQualifier(ConversionClass conversionClass, ConversionClass baseConversionClass) {
            if (conversionClass?.TypeSymbol == null || baseConversionClass?.TypeSymbol == null) {
                return $"{baseConversionClass?.GetEmittedTypeName()}::";
            }

            INamedTypeSymbol matchingBaseTypeSymbol = FindMatchingBaseTypeSymbol(conversionClass.TypeSymbol, baseConversionClass.TypeSymbol);
            if (matchingBaseTypeSymbol == null) {
                return $"{baseConversionClass.GetEmittedTypeName()}::";
            }

            string renderedBaseType = RenderInheritanceType(matchingBaseTypeSymbol).TrimStart(':');
            return $"{renderedBaseType}::";
        }

        /// <summary>
        /// Resolves the concrete base-type symbol on one derived type that matches the requested generated base class definition.
        /// </summary>
        /// <param name="derivedTypeSymbol">Derived type being emitted.</param>
        /// <param name="targetBaseTypeSymbol">Base type definition that owns the inherited member implementation.</param>
        /// <returns>The concrete instantiated base type when found; otherwise <c>null</c>.</returns>
        static INamedTypeSymbol FindMatchingBaseTypeSymbol(INamedTypeSymbol derivedTypeSymbol, INamedTypeSymbol targetBaseTypeSymbol) {
            if (derivedTypeSymbol == null || targetBaseTypeSymbol == null) {
                return null;
            }

            for (INamedTypeSymbol currentBaseTypeSymbol = derivedTypeSymbol.BaseType; currentBaseTypeSymbol != null; currentBaseTypeSymbol = currentBaseTypeSymbol.BaseType) {
                if (SymbolEqualityComparer.Default.Equals(currentBaseTypeSymbol.OriginalDefinition, targetBaseTypeSymbol)) {
                    return currentBaseTypeSymbol;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds the qualified call prefix used by an interface bridge to forward into another base implementation.
        /// </summary>
        /// <param name="conversionClass">Concrete class that owns the bridge.</param>
        /// <param name="containingTypeSymbol">Type that already implements the interface member.</param>
        /// <returns>C++ call prefix ending in <c>::</c> or <c>-></c>.</returns>
        string GetInterfaceBridgeQualifier(ConversionClass conversionClass, INamedTypeSymbol containingTypeSymbol) {
            if (containingTypeSymbol == null ||
                SymbolEqualityComparer.Default.Equals(conversionClass.TypeSymbol, containingTypeSymbol)) {
                return "this->";
            }

            string containingTypeName = program.FindGeneratedClass(containingTypeSymbol)?.GetEmittedTypeName()
                ?? containingTypeSymbol.Name;
            return $"this->{containingTypeName}::";
        }

        /// <summary>
        /// Determines whether the emitter should synthesize the implicit parameterless constructor that C# value types always expose.
        /// </summary>
        /// <param name="conversionClass">The converted type to inspect.</param>
        /// <returns><c>true</c> when the value type needs an emitted parameterless constructor; otherwise <c>false</c>.</returns>
        bool ShouldEmitImplicitDefaultConstructor(ConversionClass conversionClass) {
            if (conversionClass == null ||
                conversionClass.DeclarationType == MemberDeclarationType.Enum ||
                conversionClass.DeclarationType == MemberDeclarationType.Interface) {
                return false;
            }

            if (conversionClass.TypeSymbol?.IsValueType == true) {
                return !conversionClass.Functions.Any(function => function.IsConstructor && function.InParameters.Count == 0);
            }

            return !conversionClass.Functions.Any(function => function.IsConstructor) &&
                   GetInstanceFieldInitializers(conversionClass).Count > 0;
        }

        /// <summary>
        /// Emits the implicit parameterless constructor required for C# value-type default initialization.
        /// </summary>
        /// <param name="conversionClass">The value type that needs the constructor.</param>
        /// <param name="headerWriter">Writer that receives the declaration.</param>
        /// <param name="sourceWriter">Writer that receives the definition.</param>
        void WriteImplicitDefaultConstructor(ConversionClass conversionClass, TextWriter headerWriter, TextWriter sourceWriter) {
            string emittedTypeName = conversionClass.GetEmittedTypeName();
            headerWriter.WriteLine($"    {emittedTypeName}();");

            WriteTemplateDeclaration(conversionClass, sourceWriter);
            sourceWriter.Write($"{GetQualifiedClassName(conversionClass)}::{emittedTypeName}()");

            List<string> initializers = ShouldEmitExplicitLayoutFieldAssignments(conversionClass)
                ? new List<string>()
                : GetInstanceFieldInitializers(conversionClass);
            if (initializers.Count > 0) {
                sourceWriter.Write(" : ");
                sourceWriter.Write(string.Join(", ", initializers));
            }

            sourceWriter.WriteLine();
            sourceWriter.WriteLine("{");
            if (ShouldEmitExplicitLayoutFieldAssignments(conversionClass)) {
                WriteExplicitLayoutFieldAssignments(conversionClass, sourceWriter);
            }
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

            return variable.DeclarationType == MemberDeclarationType.Abstract ||
                   variable.GetBlock != null ||
                   variable.SetBlock != null ||
                   variable.ArrowExpression != null;
        }

        List<string> GetInstanceFieldInitializers(ConversionClass conversionClass) {
            return conversionClass.Variables
                .Where(variable => !variable.IsStatic && !IsComputedProperty(variable))
                .Select(variable => $"{variable.Name}({GetFieldInitializerExpression(conversionClass, variable)})")
                .ToList();
        }

        static bool ConstructorDelegatesToThis(ConversionFunction function) {
            return function?.ConstructorInitializer != null &&
                string.Equals(function.ConstructorInitializer.ThisOrBaseKeyword.Text, "this", StringComparison.Ordinal);
        }

        bool ShouldEmitExplicitLayoutFieldAssignments(ConversionClass conversionClass) {
            return conversionClass?.HasExplicitLayout == true;
        }

        void WriteExplicitLayoutFieldAssignments(ConversionClass conversionClass, TextWriter sourceWriter) {
            foreach (ConversionVariable variable in conversionClass.Variables.Where(variable => !variable.IsStatic && !IsComputedProperty(variable))) {
                sourceWriter.WriteLine($"this->{variable.Name} = {GetFieldAssignmentExpression(conversionClass, variable)};");
            }
        }

        string GetFieldInitializerExpression(ConversionClass conversionClass, ConversionVariable variable) {
            if (TryLowerInlineFieldInitializer(conversionClass, variable, out string loweredInitializer)) {
                return loweredInitializer;
            }

            return string.Empty;
        }

        string GetFieldAssignmentExpression(ConversionClass conversionClass, ConversionVariable variable) {
            string initializerExpression = GetFieldInitializerExpression(conversionClass, variable);
            if (!string.IsNullOrWhiteSpace(initializerExpression)) {
                return initializerExpression;
            }

            if (variable?.VarType?.IsPointer == true) {
                return "nullptr";
            }

            return $"{ConvertType(variable.VarType, conversionClass)}()";
        }

        bool TryLowerInlineFieldInitializer(ConversionClass conversionClass, ConversionVariable variable, out string loweredInitializer) {
            loweredInitializer = string.Empty;
            SemanticModel variableSemantic = variable.Semantic ?? conversionClass.Semantic;

            if (variable.AssignmentExpression != null) {
                List<string> initializerLines = new List<string>();
                LayerContext context = new CPPLayerContext(program);
                int start = context.DepthClass;
                context.AddClass(conversionClass);
                ExpressionResult result = processor.ProcessExpression(variableSemantic, context, variable.AssignmentExpression, initializerLines);
                context.PopClass(start);

                if ((result.BeforeLines == null || result.BeforeLines.Count == 0) &&
                    (result.AfterLines == null || result.AfterLines.Count == 0) &&
                    initializerLines.Count > 0) {
                    loweredInitializer = string.Concat(initializerLines);
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(variable.Assignment) ||
                string.Equals(variable.Assignment, "null", StringComparison.Ordinal) ||
                string.Equals(variable.VarType?.TypeName, "Event", StringComparison.Ordinal)) {
                return false;
            }

            loweredInitializer = variable.Assignment;
            return true;
        }

        /// <summary>
        /// Writes a field declaration for a direct field or auto-property lowering result.
        /// </summary>
        /// <param name="variable">The variable to emit.</param>
        /// <param name="headerWriter">Writer that receives the field declaration.</param>
        void WriteField(ConversionClass conversionClass, ConversionVariable variable, TextWriter headerWriter, TextWriter sourceWriter) {
            if (variable.IsConst) {
                WriteConstField(conversionClass, variable, headerWriter, sourceWriter);
                return;
            }

            headerWriter.WriteLine(GetFieldDeclaration(conversionClass, variable, "    "));

            if (variable.IsStatic) {
                WriteStaticFieldDefinition(conversionClass, variable, sourceWriter);
            }
        }

        bool ShouldWriteExplicitLayoutFieldsInSection(ConversionClass conversionClass, MemberAccessType accessType) {
            if (!conversionClass.HasExplicitLayout) {
                return false;
            }

            return accessType == GetExplicitLayoutFieldAccessType(conversionClass);
        }

        static bool IsExplicitLayoutInstanceField(ConversionVariable variable) {
            return variable != null &&
                !variable.IsStatic &&
                !variable.IsConst &&
                !variable.IsGet &&
                !variable.IsSet;
        }

        MemberAccessType GetExplicitLayoutFieldAccessType(ConversionClass conversionClass) {
            ConversionVariable firstField = conversionClass.Variables.FirstOrDefault(IsExplicitLayoutInstanceField);
            return firstField?.AccessType ?? MemberAccessType.Public;
        }

        string GetFieldDeclaration(ConversionClass conversionClass, ConversionVariable variable, string indent) {
            string staticKeyword = variable.IsStatic ? "static " : string.Empty;
            string typeName = ConvertType(variable.VarType, conversionClass);
            return $"{indent}{staticKeyword}{typeName} {variable.Name};";
        }

        void WriteExplicitLayoutFields(ConversionClass conversionClass, TextWriter headerWriter, TextWriter sourceWriter) {
            List<ConversionVariable> fields = conversionClass.Variables
                .Where(IsExplicitLayoutInstanceField)
                .OrderBy(variable => variable.HasExplicitLayoutOffset ? variable.ExplicitLayoutOffset : int.MaxValue)
                .ThenBy(variable => variable.Name, StringComparer.Ordinal)
                .ToList();
            if (fields.Count == 0) {
                return;
            }

            if (TryWriteNonOverlappingExplicitLayoutFields(conversionClass, fields, headerWriter)) {
                return;
            }

            headerWriter.WriteLine("    union {");
            int paddingIndex = 0;
            foreach (ConversionVariable field in fields) {
                headerWriter.WriteLine("        struct {");
                if (field.HasExplicitLayoutOffset && field.ExplicitLayoutOffset > 0) {
                    headerWriter.WriteLine($"            uint8_t __pad_{paddingIndex}[{field.ExplicitLayoutOffset}];");
                    paddingIndex++;
                }

                headerWriter.WriteLine(GetFieldDeclaration(conversionClass, field, "            "));
                headerWriter.WriteLine("        };");
            }

            headerWriter.WriteLine("    };");
        }

        bool TryWriteNonOverlappingExplicitLayoutFields(ConversionClass conversionClass, List<ConversionVariable> fields, TextWriter headerWriter) {
            if (!TryGetExplicitLayoutFieldSpans(fields, out List<ExplicitLayoutFieldSpan> spans)) {
                return false;
            }

            int paddingIndex = 0;
            int currentOffset = 0;
            foreach (ExplicitLayoutFieldSpan span in spans) {
                if (span.Offset > currentOffset) {
                    headerWriter.WriteLine($"    uint8_t __pad_{paddingIndex}[{span.Offset - currentOffset}];");
                    paddingIndex++;
                }

                headerWriter.WriteLine(GetFieldDeclaration(conversionClass, span.Field, "    "));
                currentOffset = span.Offset + span.Size;
            }

            if (TryGetExplicitLayoutSize(conversionClass, out int explicitLayoutSize) && explicitLayoutSize > currentOffset) {
                headerWriter.WriteLine($"    uint8_t __tail_padding[{explicitLayoutSize - currentOffset}];");
            }

            return true;
        }

        bool TryGetExplicitLayoutFieldSpans(List<ConversionVariable> fields, out List<ExplicitLayoutFieldSpan> spans) {
            spans = new List<ExplicitLayoutFieldSpan>(fields.Count);
            foreach (ConversionVariable field in fields) {
                int offset = field.HasExplicitLayoutOffset ? field.ExplicitLayoutOffset : 0;
                if (!TryGetNativeValueSize(field.VarType, out int size)) {
                    spans.Clear();
                    return false;
                }

                spans.Add(new ExplicitLayoutFieldSpan(field, offset, size));
            }

            ExplicitLayoutFieldSpan previousSpan = null;
            foreach (ExplicitLayoutFieldSpan span in spans) {
                if (previousSpan != null && span.Offset < previousSpan.Offset + previousSpan.Size) {
                    spans.Clear();
                    return false;
                }

                previousSpan = span;
            }

            return true;
        }

        sealed class ExplicitLayoutFieldSpan
        {
            public ExplicitLayoutFieldSpan(ConversionVariable field, int offset, int size) {
                Field = field;
                Offset = offset;
                Size = size;
            }

            public ConversionVariable Field { get; }

            public int Offset { get; }

            public int Size { get; }
        }

        int GetPackedStructLayoutPack(ConversionClass conversionClass) {
            if (conversionClass == null) {
                return 0;
            }

            if (conversionClass.HasExplicitLayout) {
                return 1;
            }

            if (conversionClass.HasSequentialStructLayout && conversionClass.SequentialStructLayoutPack > 0) {
                return conversionClass.SequentialStructLayoutPack;
            }

            return 0;
        }

        bool ShouldEmitSequentialLayoutTailPadding(ConversionClass conversionClass) {
            if (conversionClass == null ||
                conversionClass.HasExplicitLayout ||
                !conversionClass.HasSequentialStructLayout ||
                conversionClass.SequentialStructLayoutSize <= 0) {
                return false;
            }

            return TryGetSequentialLayoutFieldSize(conversionClass, out int fieldSize) &&
                fieldSize < conversionClass.SequentialStructLayoutSize;
        }

        void WriteSequentialLayoutTailPadding(ConversionClass conversionClass, TextWriter headerWriter) {
            if (!TryGetSequentialLayoutFieldSize(conversionClass, out int fieldSize)) {
                return;
            }

            int paddingSize = conversionClass.SequentialStructLayoutSize - fieldSize;
            if (paddingSize <= 0) {
                return;
            }

            headerWriter.WriteLine($"    std::array<uint8_t, {paddingSize}> __tail_padding{{}};");
        }

        bool TryGetSequentialLayoutFieldSize(ConversionClass conversionClass, out int size) {
            size = 0;
            if (conversionClass == null) {
                return false;
            }

            foreach (ConversionVariable variable in conversionClass.Variables) {
                if (variable == null ||
                    variable.IsStatic ||
                    variable.IsConst ||
                    variable.IsGet ||
                    variable.IsSet) {
                    continue;
                }

                if (!TryGetNativeValueSize(variable.VarType, out int variableSize)) {
                    return false;
                }

                size += variableSize;
            }

            return true;
        }

        bool TryGetNativeValueSize(VariableType variableType, out int size) {
            size = 0;
            if (variableType == null || variableType.IsReference || variableType.IsConstReference) {
                return false;
            }

            if (variableType.IsPointer || IsNativePointerSizedValueType(variableType)) {
                size = GetPlatformPointerSizeInBytes();
                return size > 0;
            }

            string qualifiedTypeName = variableType.QualifiedTypeName ?? string.Empty;
            string typeName = variableType.TypeName ?? string.Empty;
            if (TryGetKnownPrimitiveSize(qualifiedTypeName, out size) || TryGetKnownPrimitiveSize(typeName, out size)) {
                return true;
            }

            if (TryResolveConfiguredLayoutTypeRemap(variableType, out VariableType remappedVariableType)) {
                return TryGetNativeValueSize(remappedVariableType, out size);
            }

            if (program.FindGeneratedClass(variableType) is ConversionClass generatedClass) {
                return TryGetGeneratedClassValueSize(generatedClass, out size);
            }

            string normalizedTypeName = NormalizeReferencedClassName(typeName);
            if (program.FindGeneratedClass(normalizedTypeName, variableType.GenericArgs.Count) is ConversionClass normalizedClass) {
                return TryGetGeneratedClassValueSize(normalizedClass, out size);
            }

            return false;
        }

        bool IsNativePointerSizedValueType(VariableType variableType) {
            if (variableType == null) {
                return false;
            }

            string qualifiedTypeName = variableType.QualifiedTypeName ?? string.Empty;
            string typeName = variableType.TypeName ?? string.Empty;
            return string.Equals(qualifiedTypeName, "System.IntPtr", StringComparison.Ordinal) ||
                string.Equals(qualifiedTypeName, "System.UIntPtr", StringComparison.Ordinal) ||
                string.Equals(typeName, "IntPtr", StringComparison.Ordinal) ||
                string.Equals(typeName, "UIntPtr", StringComparison.Ordinal) ||
                string.Equals(typeName, "nint", StringComparison.Ordinal) ||
                string.Equals(typeName, "nuint", StringComparison.Ordinal);
        }

        int GetPlatformPointerSizeInBytes() {
            if (program?.Options?.PlatformProfile?.PointerSizeInBytes > 0) {
                return program.Options.PlatformProfile.PointerSizeInBytes;
            }

            return IntPtr.Size;
        }

        bool TryResolveConfiguredLayoutTypeRemap(VariableType variableType, out VariableType remappedVariableType) {
            remappedVariableType = null;
            if (variableType == null || program?.TypeMap == null || program.TypeMap.Count == 0) {
                return false;
            }

            string remappedTypeName;
            if (!string.IsNullOrWhiteSpace(variableType.QualifiedTypeName) &&
                program.TypeMap.TryGetValue(variableType.QualifiedTypeName, out remappedTypeName) &&
                !string.Equals(remappedTypeName, variableType.QualifiedTypeName, StringComparison.Ordinal)) {
                remappedVariableType = VariableUtil.GetVarType(remappedTypeName);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(variableType.TypeName) &&
                program.TypeMap.TryGetValue(variableType.TypeName, out remappedTypeName) &&
                !string.Equals(remappedTypeName, variableType.TypeName, StringComparison.Ordinal)) {
                remappedVariableType = VariableUtil.GetVarType(remappedTypeName);
                return true;
            }

            return false;
        }

        bool TryGetGeneratedClassValueSize(ConversionClass conversionClass, out int size) {
            size = 0;
            if (conversionClass == null) {
                return false;
            }

            if (conversionClass.HasSequentialStructLayout && conversionClass.SequentialStructLayoutSize > 0) {
                size = conversionClass.SequentialStructLayoutSize;
                return true;
            }

            if (conversionClass.HasExplicitLayout) {
                return TryGetExplicitLayoutSize(conversionClass, out size);
            }

            if (!conversionClass.IsValueType) {
                return false;
            }

            return TryGetSequentialLayoutFieldSize(conversionClass, out size);
        }

        bool TryGetExplicitLayoutSize(ConversionClass conversionClass, out int size) {
            size = 0;
            if (conversionClass == null) {
                return false;
            }

            if (conversionClass.SequentialStructLayoutSize > 0) {
                size = conversionClass.SequentialStructLayoutSize;
                return true;
            }

            foreach (ConversionVariable variable in conversionClass.Variables.Where(IsExplicitLayoutInstanceField)) {
                if (!TryGetNativeValueSize(variable.VarType, out int variableSize)) {
                    return false;
                }

                int candidateSize = variable.HasExplicitLayoutOffset
                    ? variable.ExplicitLayoutOffset + variableSize
                    : variableSize;
                if (candidateSize > size) {
                    size = candidateSize;
                }
            }

            return size > 0;
        }

        static bool TryGetKnownPrimitiveSize(string typeName, out int size) {
            size = 0;
            if (string.IsNullOrWhiteSpace(typeName)) {
                return false;
            }

            switch (typeName) {
                case "bool":
                case "System.Boolean":
                case "byte":
                case "System.Byte":
                case "sbyte":
                case "System.SByte":
                    size = 1;
                    return true;
                case "short":
                case "System.Int16":
                case "ushort":
                case "System.UInt16":
                case "char":
                case "System.Char":
                    size = 2;
                    return true;
                case "int":
                case "System.Int32":
                case "uint":
                case "System.UInt32":
                case "float":
                case "System.Single":
                    size = 4;
                    return true;
                case "long":
                case "System.Int64":
                case "ulong":
                case "System.UInt64":
                case "double":
                case "System.Double":
                    size = 8;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Writes one C# const field either as an inline literal constant or as a declaration plus source definition for dependent expressions.
        /// </summary>
        /// <param name="conversionClass">Owning class that declares the field.</param>
        /// <param name="variable">Constant field to emit.</param>
        /// <param name="headerWriter">Writer that receives the header declaration.</param>
        /// <param name="sourceWriter">Writer that receives any out-of-class definition required for dependent constants.</param>
        void WriteConstField(ConversionClass conversionClass, ConversionVariable variable, TextWriter headerWriter, TextWriter sourceWriter) {
            string typeName = ConvertType(variable.VarType, conversionClass);
            if (ShouldInlineConstField(variable)) {
                headerWriter.Write($"    inline static const {typeName} {variable.Name}");
                if (TryLowerInlineFieldInitializer(conversionClass, variable, out string loweredInitializer)) {
                    headerWriter.Write($" = {loweredInitializer}");
                } else if (!string.IsNullOrWhiteSpace(variable.Assignment)) {
                    headerWriter.Write($" = {variable.Assignment}");
                }

                headerWriter.WriteLine(";");
                return;
            }

            headerWriter.WriteLine($"    static const {typeName} {variable.Name};");
            WriteStaticFieldDefinition(conversionClass, variable, sourceWriter);
        }

        /// <summary>
        /// Determines whether one C# const field can stay inline in the header without depending on sibling declarations.
        /// </summary>
        /// <param name="variable">Constant field being evaluated.</param>
        /// <returns><c>true</c> when the initializer is one standalone literal; otherwise <c>false</c>.</returns>
        static bool ShouldInlineConstField(ConversionVariable variable) {
            return variable?.AssignmentExpression is LiteralExpressionSyntax;
        }

        /// <summary>
        /// Writes one out-of-class definition for a static field so generated translation units satisfy native linkage.
        /// </summary>
        /// <param name="conversionClass">Owning class that declares the static field.</param>
        /// <param name="variable">Static variable to define.</param>
        /// <param name="sourceWriter">Writer that receives the source definition.</param>
        void WriteStaticFieldDefinition(ConversionClass conversionClass, ConversionVariable variable, TextWriter sourceWriter) {
            WriteTemplateDeclaration(conversionClass, sourceWriter);

            string typeName = ConvertType(variable.VarType, conversionClass);
            string constQualifier = variable.IsConst ? "const " : string.Empty;
            sourceWriter.Write($"{constQualifier}{typeName} {GetQualifiedClassName(conversionClass)}::{variable.Name}");

            if (TryWriteStaticFieldInitializer(conversionClass, variable, sourceWriter)) {
                sourceWriter.WriteLine(";");
            } else {
                sourceWriter.WriteLine(";");
            }

            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Attempts to emit a native initializer for a static field definition using the lowered assignment expression when available.
        /// </summary>
        /// <param name="conversionClass">Owning class that provides semantic lowering context.</param>
        /// <param name="variable">Static field whose initializer should be written.</param>
        /// <param name="sourceWriter">Writer that receives the initializer text.</param>
        /// <returns><c>true</c> when an initializer was emitted; otherwise <c>false</c>.</returns>
        bool TryWriteStaticFieldInitializer(ConversionClass conversionClass, ConversionVariable variable, TextWriter sourceWriter) {
            if (variable.AssignmentExpression != null) {
                SemanticModel variableSemantic = variable.Semantic ?? conversionClass.Semantic;
                List<string> initializerLines = new List<string>();
                LayerContext context = new CPPLayerContext(program);
                int start = context.DepthClass;
                context.AddClass(conversionClass);
                context.AddFunction(new FunctionStack(new ConversionFunction {
                    IsStatic = true
                }));
                ExpressionResult result = processor.ProcessExpression(variableSemantic, context, variable.AssignmentExpression, initializerLines);
                context.PopClass(start);

                if ((result.BeforeLines == null || result.BeforeLines.Count == 0) &&
                    (result.AfterLines == null || result.AfterLines.Count == 0) &&
                    initializerLines.Count > 0) {
                    sourceWriter.Write(" = ");
                    sourceWriter.Write(string.Concat(initializerLines));
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(variable.Assignment) ||
                string.Equals(variable.VarType?.TypeName, "Event", StringComparison.Ordinal)) {
                return false;
            }

            sourceWriter.Write(" = ");
            sourceWriter.Write(variable.Assignment);
            return true;
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
                if (variable.ArrowExpression != null && variable.GetBlock == null) {
                    WriteExpressionBodiedGetter(conversionClass, variable, headerWriter, sourceWriter);
                } else {
                    ConversionFunction getter = CreateGetter(variable);
                    WriteFunction(conversionClass, getter, headerWriter, sourceWriter);
                }
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
        /// Emits a getter backed by the original Roslyn expression so semantic binding stays attached to the source syntax tree.
        /// </summary>
        /// <param name="conversionClass">The class that owns the getter.</param>
        /// <param name="variable">The source property definition.</param>
        /// <param name="headerWriter">Writer that receives the getter declaration.</param>
        /// <param name="sourceWriter">Writer that receives the getter definition.</param>
        void WriteExpressionBodiedGetter(ConversionClass conversionClass, ConversionVariable variable, TextWriter headerWriter, TextWriter sourceWriter) {
            string staticKeyword = variable.IsStatic ? "static " : string.Empty;
            string returnTypeName = GetPropertyGetterReturnType(conversionClass, variable);
            SemanticModel variableSemantic = variable.Semantic ?? conversionClass.Semantic;
            headerWriter.WriteLine($"    {staticKeyword}{returnTypeName} get_{variable.Name}();");

            WriteTemplateDeclaration(conversionClass, sourceWriter);
            sourceWriter.WriteLine($"{returnTypeName} {GetQualifiedClassName(conversionClass)}::get_{variable.Name}()");
            sourceWriter.WriteLine("{");

            List<string> expressionLines = new List<string>();
            LayerContext context = new CPPLayerContext(program);
            int start = context.DepthClass;
            context.AddClass(conversionClass);
            if (variable.IsStatic) {
                context.AddFunction(new FunctionStack(new ConversionFunction {
                    IsStatic = true
                }));
            }

            ExpressionResult expressionResult = processor.ProcessExpression(variableSemantic, context, variable.ArrowExpression, expressionLines);
            context.PopClass(start);

            if (!expressionResult.Processed) {
                sourceWriter.WriteLine("throw new NotSupportedException(\"Property getter could not be lowered.\");");
            } else {
                sourceWriter.WriteLine($"return {string.Concat(expressionLines)};");
            }

            sourceWriter.WriteLine("}");
            sourceWriter.WriteLine();
        }

        /// <summary>
        /// Emits an auto-property as native storage plus generated getter and setter methods so class implementations satisfy interface-style contracts.
        /// </summary>
        /// <param name="conversionClass">The class that owns the property.</param>
        /// <param name="variable">The property model to emit.</param>
        /// <param name="headerWriter">Writer that receives the field and accessor declarations.</param>
        /// <param name="sourceWriter">Writer that receives the accessor definitions.</param>
        void WriteStorageBackedProperty(ConversionClass conversionClass, ConversionVariable variable, TextWriter headerWriter, TextWriter sourceWriter) {
            WriteField(conversionClass, variable, headerWriter, sourceWriter);

            if (variable.IsGet || variable.IsSet) {
                headerWriter.WriteLine();
            }

            string staticKeyword = variable.IsStatic ? "static " : string.Empty;
            string typeName = ConvertType(variable.VarType, conversionClass);
            string getterReturnTypeName = GetPropertyGetterReturnType(conversionClass, variable);

            if (variable.IsGet) {
                headerWriter.WriteLine($"    {staticKeyword}{getterReturnTypeName} get_{variable.Name}();");
                WriteTemplateDeclaration(conversionClass, sourceWriter);
                sourceWriter.WriteLine($"{getterReturnTypeName} {GetQualifiedClassName(conversionClass)}::get_{variable.Name}()");
                sourceWriter.WriteLine("{");
                sourceWriter.WriteLine(variable.IsStatic
                    ? $"return {GetQualifiedClassName(conversionClass)}::{variable.Name};"
                    : $"return this->{variable.Name};");
                sourceWriter.WriteLine("}");
                sourceWriter.WriteLine();
            }

            if (variable.IsSet) {
                headerWriter.WriteLine($"    {staticKeyword}void set_{variable.Name}({typeName} value);");
                WriteTemplateDeclaration(conversionClass, sourceWriter);
                sourceWriter.WriteLine($"void {GetQualifiedClassName(conversionClass)}::set_{variable.Name}({typeName} value)");
                sourceWriter.WriteLine("{");
                sourceWriter.WriteLine(variable.IsStatic
                    ? $"{GetQualifiedClassName(conversionClass)}::{variable.Name} = value;"
                    : $"this->{variable.Name} = value;");
                sourceWriter.WriteLine("}");
                sourceWriter.WriteLine();
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
                DeclarationType = variable.DeclarationType,
                IsOverride = variable.IsOverride,
                IsStatic = variable.IsStatic,
                ReturnsConstReference = variable.ReturnsConstReference || ShouldEmitConstReferencePropertyGetter(variable),
                ReturnsReference = variable.ReturnsReference,
                Semantic = variable.Semantic,
                ReturnType = new VariableType(variable.VarType),
                RawBlock = variable.GetBlock
            };
        }

        /// <summary>
        /// Resolves the native return type for one generated property getter, preserving constant-reference semantics for strings.
        /// </summary>
        /// <param name="conversionClass">Owning class for the property getter.</param>
        /// <param name="variable">Property being emitted.</param>
        /// <returns>The native return type token.</returns>
        string GetPropertyGetterReturnType(ConversionClass conversionClass, ConversionVariable variable) {
            string typeName = ConvertType(variable.VarType, conversionClass);
            if (variable.ReturnsReference) {
                return $"{typeName}&";
            }

            if (variable.ReturnsConstReference) {
                return $"const {typeName}&";
            }

            if (ShouldEmitConstReferencePropertyGetter(variable)) {
                return $"const {typeName}&";
            }

            return typeName;
        }

        /// <summary>
        /// Resolves the native return type for one generated property getter from raw type metadata, preserving constant-reference semantics for native strings.
        /// </summary>
        /// <param name="conversionClass">Owning class for the property getter.</param>
        /// <param name="variableType">Type metadata for the property being emitted.</param>
        /// <returns>The native return type token.</returns>
        string GetPropertyGetterReturnType(ConversionClass conversionClass, VariableType variableType) {
            return GetPropertyGetterReturnType(conversionClass, variableType, false, false);
        }

        /// <summary>
        /// Resolves the native return type for one generated property getter from raw type metadata and ref-kind information.
        /// </summary>
        /// <param name="conversionClass">Owning class for the property getter.</param>
        /// <param name="variableType">Type metadata for the property being emitted.</param>
        /// <param name="returnsReference"><c>true</c> when the getter returns one mutable native reference.</param>
        /// <param name="returnsConstReference"><c>true</c> when the getter returns one constant native reference.</param>
        /// <returns>The native return type token.</returns>
        string GetPropertyGetterReturnType(ConversionClass conversionClass, VariableType variableType, bool returnsReference, bool returnsConstReference) {
            string typeName = ConvertType(variableType, conversionClass);
            if (returnsReference) {
                return $"{typeName}&";
            }

            if (returnsConstReference) {
                return $"const {typeName}&";
            }

            if (ShouldEmitConstReferencePropertyGetter(variableType)) {
                return $"const {typeName}&";
            }

            return typeName;
        }

        /// <summary>
        /// Returns whether one generated property getter should expose a constant-reference native signature instead of copying the string value on every access.
        /// </summary>
        /// <param name="variable">Property being emitted.</param>
        /// <returns><c>true</c> when the getter should return a constant string reference; otherwise, <c>false</c>.</returns>
        bool ShouldEmitConstReferencePropertyGetter(ConversionVariable variable) {
            if (variable == null || !variable.IsGet) {
                return false;
            }

            return ShouldEmitConstReferencePropertyGetter(variable.VarType);
        }

        /// <summary>
        /// Returns whether one property type should expose a constant-reference native getter instead of copying the string value on every access.
        /// </summary>
        /// <param name="variableType">Property type being emitted.</param>
        /// <returns><c>true</c> when the getter should return a constant string reference; otherwise, <c>false</c>.</returns>
        bool ShouldEmitConstReferencePropertyGetter(VariableType variableType) {
            if (variableType == null) {
                return false;
            }

            return string.Equals(variableType.ToCPPString(program), "std::string", StringComparison.Ordinal);
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
                DeclarationType = variable.DeclarationType,
                IsOverride = variable.IsOverride,
                IsStatic = variable.IsStatic,
                Semantic = variable.Semantic,
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
            if (IsNativeFreeFunctionStub(function)) {
                return;
            }

            WriteFunctionDeclaration(conversionClass, function, headerWriter);
            WriteFunctionDefinition(conversionClass, function, sourceWriter);
        }

        void WriteFreeOperatorFunctions(ConversionClass conversionClass, TextWriter headerWriter, TextWriter sourceWriter) {
            List<ConversionFunction> freeOperatorFunctions = conversionClass.Functions
                .Where(IsFreeOperatorFunction)
                .ToList();
            if (freeOperatorFunctions.Count == 0) {
                return;
            }

            foreach (ConversionFunction function in freeOperatorFunctions) {
                WriteFreeFunctionDefinition(conversionClass, function, sourceWriter);
            }
        }

        /// <summary>
        /// Writes a function declaration into the class header.
        /// </summary>
        /// <param name="conversionClass">The class that owns the function.</param>
        /// <param name="function">The function to declare.</param>
        /// <param name="headerWriter">Writer that receives the declaration.</param>
        void WriteFunctionDeclaration(ConversionClass conversionClass, ConversionFunction function, TextWriter headerWriter) {
            if (IsNativeFreeFunctionStub(function)) {
                return;
            }

            bool emitPureVirtualDeclaration = ShouldEmitPureVirtualDeclaration(conversionClass, function);

            WriteFunctionTemplateDeclaration(function, headerWriter, "    ");
            headerWriter.Write("    ");

            if (ShouldEmitVirtualKeyword(conversionClass, function)) {
                headerWriter.Write("virtual ");
            }

            if (function.IsStatic) {
                headerWriter.Write("static ");
            }

            if (!function.IsConstructor) {
                headerWriter.Write($"{GetReturnType(conversionClass, function)} ");
            }

            headerWriter.Write($"{GetFunctionName(conversionClass, function)}(");
            WriteParameters(conversionClass, function, headerWriter);
            if (emitPureVirtualDeclaration) {
                headerWriter.WriteLine(") = 0;");
                return;
            }

            headerWriter.WriteLine(");");
        }

        /// <summary>
        /// Writes a function definition into the C++ source file.
        /// </summary>
        /// <param name="conversionClass">The class that owns the function.</param>
        /// <param name="function">The function to define.</param>
        /// <param name="sourceWriter">Writer that receives the definition.</param>
        void WriteFunctionDefinition(ConversionClass conversionClass, ConversionFunction function, TextWriter sourceWriter) {
            if (IsNativeFreeFunctionStub(function)) {
                return;
            }

            if (ShouldSkipFunctionDefinition(conversionClass, function)) {
                return;
            }

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

            if (function.IsConstructor &&
                ShouldEmitExplicitLayoutFieldAssignments(conversionClass) &&
                !ConstructorDelegatesToThis(function)) {
                WriteExplicitLayoutFieldAssignments(conversionClass, sourceWriter);
            }

            if (functionBodyOverrideCatalog.TryWriteOverride(processor?.Options, function, sourceWriter)) {
            } else if (function.HasBody) {
                function.WriteLines(processor, program, conversionClass, sourceWriter);
            } else {
                sourceWriter.WriteLine("throw new NotSupportedException(\"Method has no generated body.\");");
            }

            sourceWriter.WriteLine("}");
            sourceWriter.WriteLine();
        }

        static bool IsNativeFreeFunctionStub(ConversionFunction function) {
            if (function == null) {
                return false;
            }

            return !string.IsNullOrWhiteSpace(function.NativeFreeFunctionName);
        }

        static bool ShouldEmitPureVirtualDeclaration(ConversionClass conversionClass, ConversionFunction function) {
            if (HasMethodLevelGenericParameters(function)) {
                return false;
            }

            if (function.IsStatic) {
                return false;
            }

            if (conversionClass.DeclarationType == MemberDeclarationType.Interface) {
                return true;
            }

            return function.DeclarationType == MemberDeclarationType.Abstract;
        }

        static bool ShouldEmitVirtualKeyword(ConversionClass conversionClass, ConversionFunction function) {
            if (HasMethodLevelGenericParameters(function)) {
                return false;
            }

            if (function.IsStatic) {
                return false;
            }

            return ShouldEmitPureVirtualDeclaration(conversionClass, function) ||
                function.DeclarationType == MemberDeclarationType.Virtual;
        }

        static bool ShouldSkipFunctionDefinition(ConversionClass conversionClass, ConversionFunction function) {
            if (ShouldEmitPureVirtualDeclaration(conversionClass, function)) {
                return true;
            }

            if (!HasMethodLevelGenericParameters(function)) {
                return false;
            }

            if (conversionClass.DeclarationType == MemberDeclarationType.Interface) {
                return true;
            }

            return false;
        }

        static bool HasMethodLevelGenericParameters(ConversionFunction function) {
            return function.GenericParameters != null && function.GenericParameters.Count > 0;
        }

        void WriteFreeFunctionDefinition(ConversionClass conversionClass, ConversionFunction function, TextWriter sourceWriter) {
            WriteTemplateDeclaration(conversionClass, sourceWriter);
            WriteFunctionTemplateDeclaration(function, sourceWriter, string.Empty);
            sourceWriter.Write($"{GetReturnType(conversionClass, function)} {GetFunctionName(conversionClass, function)}(");
            WriteParameters(conversionClass, function, sourceWriter);
            sourceWriter.WriteLine(")");
            sourceWriter.WriteLine("{");

            if (function.HasBody) {
                function.WriteLines(processor, program, conversionClass, sourceWriter);
            } else {
                sourceWriter.WriteLine("throw new NotSupportedException(\"Method has no generated body.\");");
            }

            sourceWriter.WriteLine("}");
            sourceWriter.WriteLine();
        }

        void WriteFriendOperatorDeclaration(ConversionClass conversionClass, ConversionFunction function, TextWriter headerWriter) {
            WriteFunctionTemplateDeclaration(function, headerWriter, "    ");
            headerWriter.Write("    friend ");
            headerWriter.Write($"{GetReturnType(conversionClass, function)} {GetFunctionName(conversionClass, function)}(");
            WriteParameters(conversionClass, function, headerWriter);
            headerWriter.WriteLine(");");
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

            if (IsFreeOperatorFunction(function)) {
                return function.Name;
            }

            return function.Name + GetRefModifierSuffix(function);
        }

        string BuildFunctionEmissionKey(ConversionClass conversionClass, ConversionFunction function) {
            StringBuilder keyBuilder = new StringBuilder();
            keyBuilder.Append(GetFunctionName(conversionClass, function));
            keyBuilder.Append('|');
            if (function.GenericParameters != null && function.GenericParameters.Count > 0) {
                keyBuilder.Append('<');
                for (int index = 0; index < function.GenericParameters.Count; index++) {
                    if (index > 0) {
                        keyBuilder.Append(',');
                    }

                    keyBuilder.Append(function.GenericParameters[index]);
                }
                keyBuilder.Append('>');
            }
            keyBuilder.Append('|');
            for (int index = 0; index < function.InParameters.Count; index++) {
                ConversionVariable parameter = function.InParameters[index];
                string parameterType = ConvertType(parameter.VarType, conversionClass, function);
                if ((parameter.Modifier & (ParameterModifier.Out | ParameterModifier.Ref)) != 0) {
                    parameterType += "&";
                }

                keyBuilder.Append(parameterType);
                keyBuilder.Append('|');
            }

            return keyBuilder.ToString();
        }

        static string GetRefModifierSuffix(ConversionFunction function) {
            if (function?.InParameters == null || function.InParameters.Count == 0) {
                return string.Empty;
            }

            List<string> suffixParts = new List<string>();
            for (int index = 0; index < function.InParameters.Count; index++) {
                ConversionVariable parameter = function.InParameters[index];
                if ((parameter.Modifier & (ParameterModifier.Ref | ParameterModifier.Out)) == 0) {
                    continue;
                }

                if ((parameter.Modifier & ParameterModifier.Ref) != 0) {
                    suffixParts.Add($"ref{index}");
                } else {
                    suffixParts.Add($"out{index}");
                }
            }

            return suffixParts.Count == 0
                ? string.Empty
                : "__" + string.Join("_", suffixParts);
        }

        static bool IsFreeOperatorFunction(ConversionFunction function) {
            return function != null &&
                function.IsStatic &&
                !function.IsConstructor &&
                function.Name.StartsWith("operator", StringComparison.Ordinal);
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
            if (!function.IsConstructor) {
                return;
            }

            List<string> initializerSegments = new List<string>();

            bool delegatesToThis = false;
            if (function.ConstructorInitializer != null) {
                string initializerTarget;
                delegatesToThis = string.Equals(function.ConstructorInitializer.ThisOrBaseKeyword.Text, "this", StringComparison.Ordinal);
                if (string.Equals(function.ConstructorInitializer.ThisOrBaseKeyword.Text, "base", StringComparison.Ordinal)) {
                    if (conversionClass.TypeSymbol?.BaseType != null &&
                        conversionClass.TypeSymbol.BaseType.SpecialType != SpecialType.System_Object &&
                        conversionClass.TypeSymbol.BaseType.SpecialType != SpecialType.System_ValueType) {
                        initializerTarget = RenderInheritanceType(conversionClass.TypeSymbol.BaseType);
                    } else {
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
                    }
                } else {
                    initializerTarget = conversionClass.GetEmittedTypeName();
                }

                StringWriter initializerWriter = new StringWriter();
                initializerWriter.Write(initializerTarget);
                initializerWriter.Write("(");

                if (function.ConstructorInitializer.ArgumentList != null) {
                    for (int index = 0; index < function.ConstructorInitializer.ArgumentList.Arguments.Count; index++) {
                        initializerWriter.Write(RenderConstructorInitializerArgument(conversionClass, function, function.ConstructorInitializer.ArgumentList.Arguments[index].Expression));
                        if (index < function.ConstructorInitializer.ArgumentList.Arguments.Count - 1) {
                            initializerWriter.Write(", ");
                        }
                    }
                }

                initializerWriter.Write(")");
                initializerSegments.Add(initializerWriter.ToString());
            }

            if (!delegatesToThis && !ShouldEmitExplicitLayoutFieldAssignments(conversionClass)) {
                initializerSegments.AddRange(GetInstanceFieldInitializers(conversionClass));
            }

            if (initializerSegments.Count == 0) {
                return;
            }

            sourceWriter.Write(" : ");
            sourceWriter.Write(string.Join(", ", initializerSegments));
        }

        /// <summary>
        /// Converts one constructor initializer argument through the normal C++ expression lowering pipeline so enum members and other platform-sensitive syntax are emitted correctly.
        /// </summary>
        /// <param name="conversionClass">The class that owns the constructor.</param>
        /// <param name="function">The constructor function being emitted.</param>
        /// <param name="expression">The Roslyn syntax expression that provides the initializer argument.</param>
        /// <returns>The lowered C++ expression text.</returns>
        string RenderConstructorInitializerArgument(ConversionClass conversionClass, ConversionFunction function, ExpressionSyntax expression) {
            if (conversionClass == null) {
                throw new ArgumentNullException(nameof(conversionClass));
            }
            if (function == null) {
                throw new ArgumentNullException(nameof(function));
            }
            if (expression == null) {
                throw new ArgumentNullException(nameof(expression));
            }

            LayerContext context = new CPPLayerContext(program);
            context.AddClass(conversionClass);
            context.AddFunction(new FunctionStack(function));

            List<string> tokens = new List<string>();
            SemanticModel functionSemantic = function.Semantic ?? conversionClass.Semantic;
            processor.ProcessExpression(functionSemantic, context, expression, tokens);
            return string.Concat(tokens);
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

            string typeName = ConvertType(function.ReturnType, conversionClass, function);
            if (function.ReturnsReference) {
                return $"{typeName}&";
            }

            if (function.ReturnsConstReference) {
                return $"const {typeName}&";
            }

            return typeName;
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

            ConversionClass generatedClass = program.FindGeneratedClass(variableType);
            if (generatedClass == null && !string.IsNullOrWhiteSpace(variableType.TypeName)) {
                string normalizedTypeName = NormalizeReferencedClassName(variableType.TypeName);
                generatedClass = program.FindGeneratedClass(normalizedTypeName, variableType.GenericArgs.Count);
            }

            return generatedClass?.DeclarationType == MemberDeclarationType.Enum;
        }

        bool IsGeneratedDelegateType(VariableType variableType) {
            if (variableType == null) {
                return false;
            }

            ConversionClass generatedClass = program.FindGeneratedClass(variableType);
            if (generatedClass == null && !string.IsNullOrWhiteSpace(variableType.TypeName)) {
                string normalizedTypeName = NormalizeReferencedClassName(variableType.TypeName);
                generatedClass = program.FindGeneratedClass(normalizedTypeName, variableType.GenericArgs.Count);
            }

            return generatedClass?.DeclarationType == MemberDeclarationType.Delegate;
        }

        string GetScopedTypeName(VariableType variableType, ConversionClass conversionClass, ConversionFunction function) {
            string renderedTypeName = variableType.ToCPPString(program);
            if (IsParameterlessActionType(variableType, renderedTypeName)) {
                return "Action<>";
            }

            IReadOnlyList<VariableType> effectiveGenericArguments = GetScopedGenericArguments(variableType);
            if (effectiveGenericArguments.Count == 0) {
                return QualifyTypeName(renderedTypeName, variableType, conversionClass, function);
            }

            int genericSeparatorIndex = renderedTypeName.IndexOf('<');
            string topLevelTypeName = genericSeparatorIndex >= 0
                ? renderedTypeName[..genericSeparatorIndex]
                : renderedTypeName;

            string qualifiedTopLevelTypeName = QualifyTypeName(topLevelTypeName, variableType, conversionClass, function);
            string genericArguments = string.Join(", ", effectiveGenericArguments.Select(argument => GetScopedTypeName(argument, conversionClass, function)));
            return QualifyRenderedTypeName($"{qualifiedTopLevelTypeName}<{genericArguments}>", conversionClass, function);
        }

        IReadOnlyList<VariableType> GetScopedGenericArguments(VariableType variableType) {
            if (variableType == null) {
                return Array.Empty<VariableType>();
            }

            ConversionClass generatedClass = program.FindGeneratedClass(variableType);
            if (generatedClass?.GenericArgs == null || generatedClass.GenericArgs.Count == 0) {
                return variableType.GenericArgs != null
                    ? variableType.GenericArgs
                    : Array.Empty<VariableType>();
            }

            if (variableType.GenericArgs != null && variableType.GenericArgs.Count > 0) {
                int implicitArgumentCount = generatedClass.GenericArgs.Count - variableType.GenericArgs.Count;
                if (implicitArgumentCount <= 0) {
                    return variableType.GenericArgs;
                }

                List<VariableType> effectiveGenericArguments = generatedClass.GenericArgs
                    .Take(implicitArgumentCount)
                    .Select(genericArgument => new VariableType(VariableDataType.Unknown, genericArgument) {
                        QualifiedTypeName = genericArgument,
                        IsGenericParameter = true
                    })
                    .ToList();
                effectiveGenericArguments.AddRange(variableType.GenericArgs);
                return effectiveGenericArguments;
            }

            if (generatedClass.TypeSymbol?.ContainingType == null) {
                return Array.Empty<VariableType>();
            }

            return generatedClass.GenericArgs
                .Select(genericArgument => new VariableType(VariableDataType.Unknown, genericArgument) {
                    QualifiedTypeName = genericArgument,
                    IsGenericParameter = true
                })
                .ToList();
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
                string.Equals(typeName, "Span", StringComparison.Ordinal) ||
                string.Equals(typeName, "ReadOnlySpan", StringComparison.Ordinal) ||
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
                string.Equals(typeName, "FileNotFoundException", StringComparison.Ordinal) ||
                string.Equals(typeName, "DirectoryNotFoundException", StringComparison.Ordinal) ||
                string.Equals(typeName, "NotSupportedException", StringComparison.Ordinal);
        }
    }
}
