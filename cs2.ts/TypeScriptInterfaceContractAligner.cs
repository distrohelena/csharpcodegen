using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.ts {
    /// <summary>
    /// Aligns renamed TypeScript overloads with the method names exposed by implemented interfaces.
    /// </summary>
    public static class TypeScriptInterfaceContractAligner {
        /// <summary>
        /// Gives each implicit interface implementation the same emitted name as its interface member,
        /// swapping a sibling overload when declaration order originally assigned that name elsewhere.
        /// </summary>
        /// <param name="conversionClass">Converted class whose overload names may need alignment.</param>
        /// <param name="program">Program containing converted interface declarations.</param>
        public static void Align(ConversionClass conversionClass, ConversionProgram program) {
            if (conversionClass?.TypeSymbol == null || program == null) {
                return;
            }

            foreach (INamedTypeSymbol interfaceType in conversionClass.TypeSymbol.AllInterfaces) {
                foreach (IMethodSymbol interfaceMethod in interfaceType.GetMembers().OfType<IMethodSymbol>()) {
                    if (interfaceMethod.MethodKind != MethodKind.Ordinary || interfaceMethod.IsStatic) {
                        continue;
                    }

                    IMethodSymbol implementation = conversionClass.TypeSymbol.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol;
                    if (implementation == null || !SymbolEqualityComparer.Default.Equals(implementation.ContainingType, conversionClass.TypeSymbol)) {
                        continue;
                    }

                    string implementationKey = BuildSourceMethodKey(implementation);
                    ConversionFunction implementationFunction = conversionClass.Functions.FirstOrDefault(function =>
                        string.Equals(function.SourceMethodKey, implementationKey, StringComparison.Ordinal));
                    if (implementationFunction == null) {
                        continue;
                    }

                    string desiredName = GetInterfaceMemberName(interfaceType, interfaceMethod, program);
                    if (string.Equals(implementationFunction.Remap, desiredName, StringComparison.Ordinal)) {
                        continue;
                    }

                    ConversionFunction currentHolder = conversionClass.Functions.FirstOrDefault(function =>
                        string.Equals(function.Name, implementationFunction.Name, StringComparison.Ordinal) &&
                        string.Equals(function.Remap, desiredName, StringComparison.Ordinal));
                    if (currentHolder != null) {
                        currentHolder.Remap = implementationFunction.Remap;
                    }
                    implementationFunction.Remap = desiredName;
                }
            }
        }

        /// <summary>
        /// Resolves the emitted interface member name from the converted declaration or its source overload order.
        /// </summary>
        /// <param name="interfaceType">Interface that declares the method.</param>
        /// <param name="interfaceMethod">Interface method being implemented.</param>
        /// <param name="program">Program containing converted declarations.</param>
        /// <returns>The TypeScript name exposed by the interface.</returns>
        static string GetInterfaceMemberName(INamedTypeSymbol interfaceType, IMethodSymbol interfaceMethod, ConversionProgram program) {
            string interfaceKey = BuildSourceMethodKey(interfaceMethod);
            ConversionClass convertedInterface = program.Classes.FirstOrDefault(candidate =>
                candidate.TypeSymbol != null && SymbolEqualityComparer.Default.Equals(candidate.TypeSymbol, interfaceType));
            ConversionFunction convertedMethod = convertedInterface?.Functions.FirstOrDefault(function =>
                string.Equals(function.SourceMethodKey, interfaceKey, StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(convertedMethod?.Remap)) {
                return convertedMethod.Remap;
            }

            List<IMethodSymbol> overloads = interfaceType.GetMembers(interfaceMethod.Name)
                .OfType<IMethodSymbol>()
                .Where(method => method.MethodKind == MethodKind.Ordinary && !method.IsStatic)
                .ToList();
            int overloadIndex = overloads.FindIndex(method =>
                SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, interfaceMethod.OriginalDefinition));
            return overloadIndex <= 0 ? interfaceMethod.Name : interfaceMethod.Name + (overloadIndex + 1);
        }

        /// <summary>
        /// Builds the stable method identity used by conversion functions.
        /// </summary>
        /// <param name="methodSymbol">Roslyn method symbol to identify.</param>
        /// <returns>The stable method identity.</returns>
        static string BuildSourceMethodKey(IMethodSymbol methodSymbol) {
            var displayFormat = new SymbolDisplayFormat(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.None,
                memberOptions: SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters,
                parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
            return methodSymbol.OriginalDefinition.ToDisplayString(displayFormat);
        }
    }
}
