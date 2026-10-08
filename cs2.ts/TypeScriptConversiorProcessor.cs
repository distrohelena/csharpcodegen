using cs2.core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nucleus;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using cs2.ts.util;
using StringUtil = cs2.core.StringUtil;

namespace cs2.ts {
    /// <summary>
    /// Processes Roslyn syntax nodes into TypeScript code lines following project rules and mappings.
    /// </summary>
    public class TypeScriptConversiorProcessor : ConversionProcessor {
        /// <summary>Owns deterministic, collision-safe names for this processor conversion.</summary>
        readonly TypeScriptTemporaryNameAllocator TemporaryNames = new();

        /// <summary>Supplies the explicit receiver consumed by the first member binding in a conditional-access chain.</summary>
        readonly System.Threading.AsyncLocal<Stack<string>> ConditionalAccessReceivers = new();

        /// <summary>
        /// Determines whether the assignment occurs within an object initializer.
        /// </summary>
        /// <param name="assignment">The assignment expression to inspect.</param>
        /// <returns>True when the assignment belongs to an object initializer.</returns>
        static bool IsInsideObjectInitializer(AssignmentExpressionSyntax assignment) {
            // Walk up until we find the nearest InitializerExpression
            InitializerExpressionSyntax initializer = null;
            var parent = assignment.Parent;
            if (parent != null) {
                initializer = parent.AncestorsAndSelf()
                    .OfType<InitializerExpressionSyntax>()
                    .FirstOrDefault();
            }

            if (initializer == null) {
                return false;
            }

            // Then check if that initializer belongs to an object creation — explicit or
            // target-typed (`new() { ... }`), which otherwise emitted raw `=` member assignments
            // inside the Object.assign literal instead of `:` properties.
            return initializer.Parent is ObjectCreationExpressionSyntax
                || initializer.Parent is ImplicitObjectCreationExpressionSyntax;
        }

        /// <summary>
        /// Handles assignment expressions, including event-like callbacks (+=/-=) and object initializers.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="assignment">The assignment expression being processed.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessAssignmentExpressionSyntax(SemanticModel semantic, LayerContext context, AssignmentExpressionSyntax assignment, List<string> lines) {
            if (assignment.Left is ElementAccessExpressionSyntax elementAccess &&
                IsDictionaryLike(semantic.GetTypeInfo(elementAccess.Expression).Type) &&
                assignment.OperatorToken.ValueText == "=") {
                int dictStartDepth = context.Class.Count;
                List<string> targetLines = new List<string>();
                ProcessExpression(semantic, context, elementAccess.Expression, targetLines);
                context.PopClass(dictStartDepth);

                lines.AddRange(targetLines);
                lines.Add(".set(");

                var elementArguments = elementAccess.ArgumentList.Arguments;
                List<string> keyLines = new List<string>();
                for (int i = 0; i < elementArguments.Count; i++) {
                    var argument = elementArguments[i];
                    dictStartDepth = context.Class.Count;
                    ProcessExpression(semantic, context, argument.Expression, keyLines);
                    context.PopClass(dictStartDepth);
                    if (i != elementArguments.Count - 1) {
                        keyLines.Add(", ");
                    }
                }

                lines.AddRange(keyLines);
                if (keyLines.Count > 0) {
                    lines.Add(", ");
                }

                dictStartDepth = context.Class.Count;
                List<string> valueLines = new List<string>();
                ExpressionResult valueResult = ProcessExpression(semantic, context, assignment.Right, valueLines);
                context.PopClass(dictStartDepth);

                FunctionStack functionStack = context.GetCurrentFunction();
                if (functionStack != null &&
                    valueResult.Type != null &&
                    valueResult.Type.TypeName.StartsWith("Promise<")) {
                    if (!functionStack.Function.IsAsync) {
                        functionStack.Function.IsAsync = true;
                    }
                }

                lines.AddRange(valueLines);
                lines.Add(")");

                return;
            }

            int assignmentStart = lines.Count;
            int startDepth = context.Class.Count;
            ExpressionResult assignResult = ProcessExpression(semantic, context, assignment.Left, lines);
            context.PopClass(startDepth);

            string operatorVal = assignment.OperatorToken.ToString();

            IEventSymbol eventSymbol = GetEventSymbol(semantic, assignment.Left);
            bool isEventAssignment = (eventSymbol != null ||
                (assignResult.Type != null && string.Equals(assignResult.Type.TypeName, "Event", StringComparison.Ordinal))) &&
                (operatorVal == "+=" || operatorVal == "-=");
            if (isEventAssignment) {
                lines.Add(operatorVal == "+=" ? ".Add(" : ".Remove(");

                startDepth = context.Class.Count;
                List<string> eventLines = new List<string>();
                ProcessExpression(semantic, context, assignment.Right, eventLines);
                context.PopClass(startDepth);
                AppendMethodGroupBind(semantic, context, assignment.Right, eventLines);

                lines.AddRange(eventLines);
                lines.Add(")");

                return;
            }

            bool isDelegateAssignment = (assignResult.Type != null && assignResult.Type.Type == VariableDataType.Callback) ||
                semantic.GetTypeInfo(assignment.Left).Type?.TypeKind == TypeKind.Delegate;
            bool isInsideObject = IsInsideObjectInitializer(assignment);
            if (isDelegateAssignment && (operatorVal == "+=" || operatorVal == "-=")) {
                string target = string.Concat(lines.Skip(assignmentStart));
                lines.RemoveRange(assignmentStart, lines.Count - assignmentStart);
                context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeDelegateUtil"));
                lines.Add(target);
                lines.Add(" = NativeDelegateUtil.");
                lines.Add(operatorVal == "+=" ? "combine(" : "remove(");
                lines.Add(target);
                lines.Add(", ");
            } else {
                lines.Add(isInsideObject ? " : " : $" {operatorVal} ");
            }

            startDepth = context.Class.Count;

            List<string> initLines = new List<string>();
            ExpressionResult result = ProcessExpression(semantic, context, assignment.Right, initLines);
            context.PopClass(startDepth);

            FunctionStack fn = context.GetCurrentFunction();
            if (result.Type != null &&
                result.Type.TypeName.StartsWith("Promise<")) {
                //lines.Add("await ");

                if (!fn.Function.IsAsync) {
                    fn.Function.IsAsync = true;
                }
            }


            bool hasPrerequisites = (result.BeforeLines?.Count ?? 0) > 0 || (result.AfterLines?.Count ?? 0) > 0;
            if (hasPrerequisites && isInsideObject) {
                string temporary = TemporaryNames.Allocate(semantic, "__initializerValue");
                lines.Add("(() => { ");
                if (result.BeforeLines != null) {
                    lines.AddRange(result.BeforeLines);
                }
                lines.Add("const ");
                lines.Add(temporary);
                lines.Add(" = ");
                lines.AddRange(initLines);
                lines.Add("; ");
                if (result.AfterLines != null) {
                    lines.AddRange(result.AfterLines);
                }
                lines.Add("return ");
                lines.Add(temporary);
                lines.Add("; })()");
                return;
            }
            if (hasPrerequisites && assignment.Parent is ExpressionStatementSyntax) {
                if (result.BeforeLines != null) {
                    lines.InsertRange(assignmentStart, result.BeforeLines);
                }
                lines.AddRange(initLines);
                lines.Add(";\n");
                if (result.AfterLines != null) {
                    lines.AddRange(result.AfterLines);
                }
                return;
            }
            lines.AddRange(initLines);
            if (isDelegateAssignment && (operatorVal == "+=" || operatorVal == "-=")) {
                lines.Add(")");
            }
        }

        /// <summary>
        /// Processes expressions, handling TypeScript-specific syntax extensions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="expression">The expression being processed.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="refTypes">Optional reference type tracking list.</param>
        /// <returns>The expression result describing the expression.</returns>
        public override ExpressionResult ProcessExpression(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax expression,
            List<string> lines,
            List<ExpressionResult> refTypes = null) {
            if (expression is AssignmentExpressionSyntax discard &&
                discard.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                semantic.GetOperation(discard.Left) is Microsoft.CodeAnalysis.Operations.IDiscardOperation) {
                // Keep evaluation and the assignment expression's value, but never create an underscore binding.
                lines.Add("(");
                ExpressionResult value = ProcessExpression(semantic, context, discard.Right, lines, refTypes);
                if (!value.Processed) {
                    throw new NotSupportedException($"Discarded expression is unsupported: {discard.Right}");
                }
                lines.Add(")");
                return value;
            } else if (expression is AwaitExpressionSyntax awaitedExpression) {
                lines.Add("await ");
                ExpressionResult awaited = ProcessExpression(semantic, context, awaitedExpression.Expression, lines, refTypes);
                // Await consumes the value, but declarations and out-variable assignments still belong to the enclosing statement.
                awaited.Type = VariableUtil.GetVarType(semantic.GetTypeInfo(awaitedExpression).Type);
                context.GetCurrentFunction().Function.IsAsync = true;
                return awaited;
            } else if (expression is SwitchExpressionSyntax switchExpression) {
                return ProcessSwitchExpression(semantic, context, switchExpression, lines);
            } else if (expression is IsPatternExpressionSyntax patternExpression) {
                return ProcessIsPatternExpression(semantic, context, patternExpression, lines);
            } else if (expression is CollectionExpressionSyntax collectionExpression) {
                return ProcessCollectionExpression(semantic, context, collectionExpression, lines);
            } else if (expression is AnonymousObjectCreationExpressionSyntax anonymousCreation) {
                return ProcessAnonymousObjectCreation(semantic, context, anonymousCreation, lines);
            } else if (expression is ImplicitObjectCreationExpressionSyntax implicitCreation) {
                return ProcessImplicitObjectCreationExpressionSyntax(semantic, context, implicitCreation, lines);
            } else if (expression is CheckedExpressionSyntax checkedExpression) {
                lines.Add("(");
                ExpressionResult result = ProcessExpression(semantic, context, checkedExpression.Expression, lines, refTypes);
                lines.Add(")");
                return result;
            }

            return base.ProcessExpression(semantic, context, expression, lines, refTypes);
        }

        /// <summary>Emits anonymous records with their exact declared or inferred property names and source evaluation order.</summary>
        /// <param name="semantic">Semantic model used to identify each anonymous property.</param>
        /// <param name="context">Scope used to resolve member values and temporary names.</param>
        /// <param name="creation">Anonymous object whose ordered values must reach serialization intact.</param>
        /// <param name="lines">Destination for the structural TypeScript object expression.</param>
        /// <returns>The object result with prerequisite declarations preserved for its caller.</returns>
        ExpressionResult ProcessAnonymousObjectCreation(SemanticModel semantic, LayerContext context, AnonymousObjectCreationExpressionSyntax creation, List<string> lines) {
            List<string> beforeLines = new List<string>();
            lines.Add("({ ");
            for (int index = 0; index < creation.Initializers.Count; index++) {
                AnonymousObjectMemberDeclaratorSyntax initializer = creation.Initializers[index];
                IPropertySymbol property = semantic.GetDeclaredSymbol(initializer) as IPropertySymbol;
                if (property == null) {
                    throw new NotSupportedException($"Anonymous property could not be resolved: {initializer}");
                }
                if (index > 0) { lines.Add(", "); }
                // Computed keys also preserve a literal __proto__ property without changing the object's prototype.
                lines.Add("[" + QuoteString(property.Name) + "]: ");
                List<string> valueLines = new List<string>();
                int depth = context.DepthClass;
                ExpressionResult value = ProcessExpression(semantic, context, initializer.Expression, valueLines);
                context.PopClass(depth);
                if (!value.Processed) {
                    throw new NotSupportedException($"Anonymous property value is unsupported: {initializer.Expression}");
                }
                if (value.BeforeLines != null) { beforeLines.AddRange(value.BeforeLines); }
                if (value.AfterLines != null && value.AfterLines.Count > 0) {
                    List<string> assignments = new List<string>();
                    SplitAfterLines(value.AfterLines, beforeLines, assignments);
                    string temporary = TemporaryNames.Allocate(semantic, "__anonymousValue");
                    bool awaits = initializer.Expression.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>().Any();
                    lines.Add(awaits ? "(await (async () => { const " : "(() => { const ");
                    lines.Add(temporary + " = ");
                    lines.AddRange(valueLines);
                    lines.Add("; ");
                    lines.AddRange(assignments);
                    lines.Add("return " + temporary + (awaits ? "; })())" : "; })()"));
                } else {
                    lines.AddRange(valueLines);
                }
            }
            lines.Add(" })");
            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("object")) { BeforeLines = beforeLines };
        }

        /// <summary>
        /// Processes implicit object creation expressions (target-typed new).
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="objectCreation">The implicit object creation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the creation.</returns>
        ExpressionResult ProcessImplicitObjectCreationExpressionSyntax(
            SemanticModel semantic,
            LayerContext context,
            ImplicitObjectCreationExpressionSyntax objectCreation,
            List<string> lines) {
            if (objectCreation.Initializer is InitializerExpressionSyntax initializer) {
                if (TryProcessImplicitDictionaryCreation(semantic, context, objectCreation, initializer, lines, out var dictResult)) {
                    return dictResult;
                }

                if (IsObjectInitializer(initializer)) {
                    List<string> creationLines = new List<string>();
                    ExpressionResult creationResult = BuildImplicitObjectCreationExpression(semantic, context, objectCreation, creationLines);
                    List<string> initLines = new List<string>();
                    ProcessExpression(semantic, context, initializer, initLines);

                    lines.Add("Object.assign(");
                    lines.AddRange(creationLines);
                    lines.Add(", ");
                    lines.AddRange(initLines);
                    lines.Add(")");
                    return creationResult;
                }

                if (TryProcessImplicitCollectionInitializer(semantic, context, objectCreation, initializer, lines, out var collectionResult)) {
                    return collectionResult;
                }

                return ProcessExpression(semantic, context, initializer, lines);
            }

            return BuildImplicitObjectCreationExpression(semantic, context, objectCreation, lines);
        }

        /// <summary>
        /// Resolves the ConversionClass for a given VariableType within the TypeScript program.
        /// </summary>
        /// <param name="program">The TypeScript program that owns the classes.</param>
        /// <param name="varType">The variable type to resolve.</param>
        /// <returns>The resolved conversion class.</returns>
        public static ConversionClass GetClass(TypeScriptProgram program, VariableType varType) {
            string name = varType.GetTypeScriptType(program);
            ConversionClass found = program.GetClassByName(name);

            if (found == null) {
                name = varType.GetTypeScriptTypeNoGeneric(program);
                found = program.GetClassByName(name);
            }

            return found;
        }

        /// <summary>
        /// Checks whether a symbol represents a dictionary-like type.
        /// </summary>
        /// <param name="type">The type symbol to inspect.</param>
        /// <returns>True when the type behaves like a dictionary.</returns>
        static bool IsDictionaryLike(ITypeSymbol type) {
            if (type == null) {
                return false;
            }

            if (type.Name.Contains("Dictionary") || type.Name == "IDictionary" || type.Name == "IReadOnlyDictionary") {
                return true;
            }

            if (type.AllInterfaces.Any(i => i.Name.Contains("Dictionary") || i.Name == "IDictionary" || i.Name == "IReadOnlyDictionary")) {
                return true;
            }

            var baseType = type.BaseType;
            while (baseType != null) {
                if (baseType.Name.Contains("Dictionary") || baseType.Name == "IDictionary" || baseType.Name == "IReadOnlyDictionary") {
                    return true;
                }
                baseType = baseType.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Emits identifier references, supporting dynamic resolution of overloaded members based on argument types.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="identifier">The identifier being processed.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="refTypes">Resolved argument types for overload matching.</param>
        /// <returns>The expression result describing the identifier.</returns>
        protected override ExpressionResult ProcessIdentifierNameSyntax(SemanticModel semantic, LayerContext context, IdentifierNameSyntax identifier, List<string> lines, List<ExpressionResult> refTypes) {
            string name = identifier.ToString();
            bool isInvocation = identifier.Parent is InvocationExpressionSyntax ||
                (identifier.Parent is MemberAccessExpressionSyntax memberAccessSyntax &&
                memberAccessSyntax.Parent is InvocationExpressionSyntax);
            bool isMethod = isInvocation;

            SymbolInfo symbolInfo = semantic.GetSymbolInfo(identifier);
            ISymbol nsSymbol = symbolInfo.Symbol;
            if (nsSymbol is IPropertySymbol arrayLengthProperty &&
                arrayLengthProperty.ContainingType?.SpecialType == SpecialType.System_Array &&
                (arrayLengthProperty.Name == "Length" || arrayLengthProperty.Name == "LongLength")) {
                lines.Add("length");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(arrayLengthProperty.Type));
            }
            IMethodSymbol invokedMethod = nsSymbol as IMethodSymbol;
            if (invokedMethod == null && symbolInfo.CandidateSymbols.Length > 0) {
                invokedMethod = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            }
            if (nsSymbol is INamespaceSymbol namespaceSymbol) {
                if (namespaceSymbol.IsNamespace) {
                    return new ExpressionResult(false);
                }
            } else if (invokedMethod != null) {
                isMethod = true;
            }
            TypeInfo identifierType = semantic.GetTypeInfo(identifier);
            AssignmentExpressionSyntax assignment = identifier.Parent as AssignmentExpressionSyntax;
            bool isObjectInitializerTarget = assignment != null &&
                assignment.Left == identifier &&
                IsInsideObjectInitializer(assignment);
            if (isObjectInitializerTarget) {
                // Initializer keys belong to the created object's type, never to the enclosing method's fields or parameters.
                ConversionClass owner = ((TypeScriptProgram)context.Program).GetClassByName(nsSymbol?.ContainingType?.Name);
                ConversionVariable member = owner?.Variables.FirstOrDefault(variable => variable.Name == name);
                if (member == null) {
                    member = owner?.Variables.FirstOrDefault(variable => variable.Name == StringUtil.ToCamelCase(name));
                }
                lines.Add(!string.IsNullOrEmpty(member?.Remap) ? member.Remap : member?.Name ?? identifier.Identifier.ValueText);
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(identifierType.Type));
            }
            bool isMethodGroup = invokedMethod != null &&
                !isInvocation &&
                IsDelegateType(identifierType.ConvertedType ?? identifierType.Type);
            if (!isMethodGroup &&
                !isInvocation &&
                assignment != null &&
                (assignment.OperatorToken.ValueText == "+=" || assignment.OperatorToken.ValueText == "-=")) {
                ITypeSymbol targetType = null;
                IEventSymbol eventSymbol = GetEventSymbol(semantic, assignment.Left);
                if (eventSymbol != null) {
                    targetType = eventSymbol.Type;
                } else {
                    TypeInfo leftTypeInfo = semantic.GetTypeInfo(assignment.Left);
                    targetType = leftTypeInfo.ConvertedType ?? leftTypeInfo.Type;
                }

                if (IsDelegateType(targetType)) {
                    isMethodGroup = true;
                }
            }

            int layer = context.GetClassLayer();

            // abstract class
            TypeScriptProgram tsProgram = (TypeScriptProgram)context.Program;
            ConversionClass staticClass = tsProgram.GetClassByName(name);

            ConversionClass currentClass = context.GetCurrentClass();
            FunctionStack currentFn = context.GetCurrentFunction();
            bool forcedStaticPrefix = false;
            if (identifier.Parent is InvocationExpressionSyntax &&
                invokedMethod != null &&
                invokedMethod.IsStatic &&
                currentClass != null &&
                invokedMethod.ContainingType != null &&
                invokedMethod.ContainingType.Name == currentClass.Name) {
                lines.Add(currentClass.Name);
                lines.Add(".");
                forcedStaticPrefix = true;
            }
            if (!forcedStaticPrefix &&
                identifier.Parent is InvocationExpressionSyntax &&
                currentFn != null &&
                currentFn.Function != null &&
                currentFn.Function.IsStatic &&
                currentFn.Function.Name == name) {
                ConversionClass ownerClass = currentClass;
                if (ownerClass == null || !ownerClass.Functions.Contains(currentFn.Function)) {
                    ownerClass = tsProgram.Classes.FirstOrDefault(c => c.Functions.Contains(currentFn.Function));
                }
                if (ownerClass != null) {
                    lines.Add(ownerClass.Name);
                    lines.Add(".");
                    forcedStaticPrefix = true;
                }
            }

            var classVars = new List<ConversionVariable>();
            for (int i = 0; i < context.Class.Count; i++) {
                ConversionClass stackClass = context.Class[i];
                if (stackClass == null) {
                    continue;
                }

                var variables = stackClass.Variables;
                if (variables == null) {
                    continue;
                }

                classVars.AddRange(variables.Where(var => var.Name == name));
            }

            // variable from the current class
            ConversionVariable classVar = null;
            if (currentClass != null) {
                classVar = currentClass.Variables.Find(c => c.Name == name);
            }
            if (classVar == null && currentClass != null) {
                // look at base classes
                for (int i = 0; i < currentClass.Extensions.Count; i++) {
                    string extension = currentClass.Extensions[i];
                    ConversionClass cl = tsProgram.GetClassByName(extension);
                    ConversionVariable foundVar = null;
                    if (cl != null) {
                        foundVar = cl.Variables.Find(c => c.Name == name);
                    }
                    if (foundVar != null) {
                        classVar = foundVar;
                    }
                }
            }

            if (classVar == null && staticClass == null) {
                string camelCame = StringUtil.ToCamelCase(name);
                if (currentClass != null) {
                    classVar = currentClass.Variables.Find(c => c.Name == camelCame);
                }
                if (classVar != null) {
                    name = camelCame;
                }
            }

            // function from the current class
            ConversionFunction classFn = null;
            bool matchedInvocationSignature = false;
            if (currentClass != null) {
                classFn = currentClass.Functions.Find(c => c.Name == name);
            }
            if (classFn == null && currentClass != null) {
                // look at base classes
                for (int i = 0; i < currentClass.Extensions.Count; i++) {
                    string extension = currentClass.Extensions[i];
                    ConversionClass cl = tsProgram.GetClassByName(extension);
                    ConversionFunction foundFn = null;
                    if (cl != null) {
                        foundFn = cl.Functions.Find(c => c.Name == name);
                    }
                    if (foundFn != null) {
                        classFn = foundFn;
                    }
                }
            }

            if (invokedMethod != null) {
                ConversionClass methodClass = currentClass;
                if (invokedMethod.ContainingType != null &&
                    (methodClass == null || methodClass.Name != invokedMethod.ContainingType.Name)) {
                    methodClass = tsProgram.GetClassByName(invokedMethod.ContainingType.Name);
                }

                if (methodClass != null &&
                    invokedMethod.Parameters != null &&
                    invokedMethod.Parameters.Length >= 0) {
                    var candidates = methodClass.Functions
                        .Where(c => c.Name == invokedMethod.Name &&
                            c.InParameters != null &&
                            c.InParameters.Count == invokedMethod.Parameters.Length)
                        .ToList();
                    if (candidates.Count > 0) {
                        List<VariableType> paramTypes = new List<VariableType>();
                        foreach (var parameter in invokedMethod.Parameters) {
                            paramTypes.Add(VariableUtil.GetVarType(parameter.Type));
                        }

                        ConversionFunction match = candidates.FirstOrDefault(c => MethodParameterTypesMatch(c.InParameters, paramTypes));
                        if (match == null && candidates.Count == 1) {
                            match = candidates[0];
                        }

                        if (match != null) {
                            classFn = match;
                            matchedInvocationSignature = true;
                            currentClass = methodClass;
                        }
                    }
                }
            }

            if (!isMethodGroup &&
                !isInvocation &&
                classFn != null &&
                assignment != null &&
                assignment.Right == identifier) {
                ITypeSymbol targetType = null;
                SymbolInfo leftSymbolInfo = semantic.GetSymbolInfo(assignment.Left);
                if (leftSymbolInfo.Symbol is IEventSymbol eventSymbol) {
                    targetType = eventSymbol.Type;
                } else {
                    TypeInfo leftTypeInfo = semantic.GetTypeInfo(assignment.Left);
                    targetType = leftTypeInfo.ConvertedType ?? leftTypeInfo.Type;
                }

                if (IsDelegateType(targetType)) {
                    isMethodGroup = true;
                }
            }

            // Roslyn already matched the source overload. Its trailing optional parameters are appended later,
            // so refTypes contains only supplied arguments and must not select a different runtime overload.
            bool paramsMatch = matchedInvocationSignature;
            if (!paramsMatch && classFn != null && classFn.InParameters != null && refTypes != null) {
                paramsMatch = classFn.InParameters.Count == refTypes.Count;
            }

            // here: dynamic system for typed functions. Like BinaryWriter writeByte, writeInt
            if (currentClass != null && classFn == null && isMethod) {
                // search for closest version
                string lowercase = name.ToLowerInvariant();
                string searchName = lowercase;
                int refCount = 0;
                if (refTypes != null) {
                    refCount = refTypes.Count;
                }
                for (int i = 0; i < refCount; i++) {
                    ExpressionResult result = refTypes[i];
                    if (result.Type == null) {
                        continue;
                    }

                    if (result.Type.Type == VariableDataType.Array) {
                        for (int j = 0; j < result.Type.GenericArgs.Count; j++) {
                            searchName += StringUtil.CapitalizerFirstLetter(result.Type.GenericArgs[j].TypeName);
                        }
                    }

                    string typeName = result.Type.TypeName;
                    if (string.IsNullOrEmpty(typeName)) {
                        continue;
                    }

                    searchName += StringUtil.CapitalizerFirstLetter(typeName);
                }

                ConversionFunction similarFn = currentClass.Functions.Find(c => c.Name == searchName);
                if (similarFn == null) {
                    // search for lower first letter
                    lowercase = name[0].ToString().ToLowerInvariant() + name.Remove(0, 1);

                    similarFn = currentClass.Functions.Find(c => c.Name == lowercase);
                    if (similarFn != null) {
                        classFn = similarFn;
                        name = similarFn.Name;
                    }
                } else {
                    classFn = similarFn;
                    name = similarFn.Name;
                }
            } else if (!paramsMatch && classFn != null && classFn.InParameters != null && refTypes != null) {
                ConversionFunction overload = currentClass.Functions.Find(c => {
                    if (c.Name.StartsWith(name)) {
                        // check in parameters
                        if (c.InParameters == null ||
                        c.InParameters.Count != refTypes.Count) {
                            return false;
                        }

                        for (int i = 0; i < c.InParameters.Count; i++) {
                            ConversionVariable inParam = c.InParameters[i];
                            ExpressionResult result = refTypes[i];
                            if (result.Type == null) {
                                continue;
                            }


                            if (inParam.VarType.ToString() != result.Type.ToString()) {
                                return false;
                            }
                        }

                        return true;

                    }
                    return false;
                });

                if (overload == null) {
                    var countMatches = currentClass.Functions
                        .Where(c => c.Name.StartsWith(name) &&
                            c.InParameters != null &&
                            c.InParameters.Count == refTypes.Count)
                        .ToList();
                    if (countMatches.Count == 1) {
                        overload = countMatches[0];
                    }
                }

                if (overload != null) {
                    classFn = overload;
                    name = overload.Name;
                }
            }

            if (!forcedStaticPrefix &&
                identifier.Parent is InvocationExpressionSyntax &&
                classFn != null &&
                classFn.IsStatic &&
                currentClass != null) {
                lines.Add(currentClass.Name);
                lines.Add(".");
                forcedStaticPrefix = true;
            }

            // current function
            // in-parameter for the current function
            ConversionVariable functionInVar = null;
            if (currentFn != null && currentFn.Function.InParameters != null) {
                functionInVar = currentFn.Function.InParameters.Find(c => c.Name == name);
            }

            // current stack
            ConversionVariable stackVar = null;
            if (currentFn != null && currentFn.Stack != null) {
                stackVar = currentFn.Stack.Find(c => c.Name == name);
            }

            var matchingVars = new List<ConversionVariable>();
            for (int i = 0; i < context.Function.Count; i++) {
                FunctionStack fn = context.Function[i];
                if (fn == null || fn.Stack == null) {
                    continue;
                }

                matchingVars.AddRange(fn.Stack.Where(var => var.Name == name));
            }

            if (name == "Dispose") {
                name = "dispose";
            }

            bool isOutParameter = functionInVar != null && functionInVar.Modifier.HasFlag(ParameterModifier.Out);
            string variableIdentifier = isOutParameter ? $"{name}.value" : name;

            if (currentClass == null) {
                lines.Add(variableIdentifier);
            } else {
                bool isMemberAccessName = identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name == identifier ||
                    identifier.Parent is MemberBindingExpressionSyntax;
                bool isUnqualifiedMemberReference = !forcedStaticPrefix &&
                    !isObjectInitializerTarget &&
                    !isMemberAccessName &&
                    functionInVar == null &&
                    matchingVars.Count == 0 &&
                    nsSymbol?.ContainingType != null &&
                    (nsSymbol is IFieldSymbol || nsSymbol is IPropertySymbol || nsSymbol is IEventSymbol ||
                     nsSymbol is IMethodSymbol memberMethod && memberMethod.MethodKind == MethodKind.Ordinary);
                if (isUnqualifiedMemberReference) {
                    if (nsSymbol.IsStatic) {
                        lines.Add(nsSymbol.ContainingType.Name + ".");
                    } else {
                        lines.Add("this.");
                    }
                    forcedStaticPrefix = true;
                }
                bool isUnqualifiedStaticMember = !forcedStaticPrefix &&
                    !isObjectInitializerTarget &&
                    !isMemberAccessName &&
                    functionInVar == null &&
                    matchingVars.Count == 0 &&
                    (nsSymbol is IFieldSymbol || nsSymbol is IPropertySymbol) &&
                    nsSymbol?.IsStatic == true &&
                    nsSymbol.ContainingType?.Name == currentClass.Name;
                if (isUnqualifiedStaticMember) {
                    lines.Add(nsSymbol.ContainingType.Name + ".");
                    forcedStaticPrefix = true;
                }

                if (layer == 1 && !forcedStaticPrefix && !isObjectInitializerTarget && !isMemberAccessName) {
                    bool isClassVar = (classVar != null &&
                        functionInVar == null &&
                        matchingVars.Count == 0) ||
                        (classFn != null &&
                        functionInVar == null &&
                        matchingVars.Count == 0);


                    if (isClassVar) {
                        // semantic
                        ISymbol symbol = semantic.GetSymbolInfo(identifier).Symbol;

                        if (lines.Count > 1) {
                            string b2 = lines[lines.Count - 2];
                            string b1 = lines[lines.Count - 1];

                            if (b2 == "this" && b1.IndexOf(";") == -1) {
                            } else {
                                if (symbol != null && symbol.IsStatic) {
                                    lines.Add($"{symbol.ContainingType.Name}.");
                                } else if (b1 != "new ") {
                                    lines.Add("this.");
                                }
                            }
                        } else {
                            if (symbol is INamedTypeSymbol namedTypeSymbol) {
                                if (!symbol.IsStatic &&
                                    !namedTypeSymbol.IsType) {
                                    lines.Add("this.");
                                }
                            } else {
                                if (symbol != null && symbol.IsStatic) {
                                    lines.Add($"{symbol.ContainingType.Name}.");
                                } else {
                                    lines.Add("this.");
                                }
                            }
                        }
                    }
                }

                if (classFn == null || string.IsNullOrEmpty(classFn.Remap)) {
                    ConversionVariable varOnClass = currentClass.Variables.FirstOrDefault(c => c.Name == name);
                    if (varOnClass != null && !string.IsNullOrEmpty(varOnClass.RemapClass) && lines.Count > 1) {
                        lines[lines.Count - 2] = varOnClass.RemapClass;
                    }
                    if (varOnClass == null || string.IsNullOrEmpty(varOnClass.Remap)) {
                        lines.Add(variableIdentifier);
                    } else {
                        lines.Add(varOnClass.Remap);
                    }
                } else {
                    if (!string.IsNullOrEmpty(classFn.RemapClass)) {
                        lines[lines.Count - 2] = classFn.RemapClass;
                    }

                    lines.Add(classFn.Remap);
                }
            }



            if (stackVar != null) {
                context.AddClass(GetClass((TypeScriptProgram)context.Program, stackVar.VarType));
                return new ExpressionResult(true, VariablePath.Unknown, stackVar.VarType);
            } else if (functionInVar != null) {
                context.AddClass(GetClass((TypeScriptProgram)context.Program, functionInVar.VarType));
                ExpressionResult res = new ExpressionResult(true, VariablePath.Unknown, functionInVar.VarType);
                res.Variable = functionInVar;
                return res;
            } else if (classVar != null) {
                context.AddClass(GetClass((TypeScriptProgram)context.Program, classVar.VarType));
                return new ExpressionResult(true, VariablePath.Unknown, classVar.VarType);
            } else if (staticClass != null) {
                context.AddClass(staticClass);
                ExpressionResult result = new ExpressionResult(true, VariablePath.Unknown, new VariableType(VariableDataType.Object, staticClass.Name));
                result.Class = staticClass;
                return result;
            } else if (classFn != null) {
                EnsureFunctionAsyncState(semantic, context, currentClass, classFn);

                if (!isMethodGroup) {
                    if (classFn.ReturnType != null) {
                        // invoked function
                        if (classFn.ReturnType.Type != VariableDataType.Void) {
                            context.AddClass(GetClass((TypeScriptProgram)context.Program, classFn.ReturnType));

                            if (classFn.IsAsync) {
                                VariableType cloned = new VariableType(classFn.ReturnType) {
                                    TypeName = classFn.ReturnType.ToTypeScriptAsyncReturnString((TypeScriptProgram)context.Program),
                                    GenericArgs = new List<VariableType>()
                                };
                                return new ExpressionResult(true, VariablePath.Unknown, cloned);
                            }
                            return new ExpressionResult(true, VariablePath.Unknown, classFn.ReturnType);
                        }
                    } else if (classFn.IsAsync) {
                        VariableType cloned = new VariableType(VariableDataType.Void);
                        cloned.TypeName = $"Promise<{cloned.TypeName}>";
                        return new ExpressionResult(true, VariablePath.Unknown, cloned);
                    }
                }
            } else if (currentClass != null && currentClass.DeclarationType == MemberDeclarationType.Enum) {

            } else {
                //Debugger.Break();
            }

            if (isMethodGroup && invokedMethod != null && !invokedMethod.IsStatic) {
                lines.Add(".bind(");
                lines.Add("this");
                lines.Add(")");
            }

            if (isMethodGroup) {
                VariableType delegateType = VariableUtil.GetVarType(identifierType.ConvertedType);
                context.AddClass(GetClass((TypeScriptProgram)context.Program, delegateType));
                return new ExpressionResult(true, VariablePath.Unknown, delegateType);
            }

            return new ExpressionResult(true);
        }

        /// <summary>
        /// Processes object creation expressions, including dictionary initializer shortcuts.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="objectCreation">The object creation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the creation.</returns>
        protected override ExpressionResult ProcessObjectCreationExpressionSyntax(SemanticModel semantic, LayerContext context, ObjectCreationExpressionSyntax objectCreation, List<string> lines) {
            if (objectCreation.Initializer is InitializerExpressionSyntax initializer) {
                if (TryProcessDictionaryCreation(semantic, context, objectCreation, initializer, lines, out var dictResult)) {
                    return dictResult;
                }

                if (IsObjectInitializer(initializer)) {
                    List<string> creationLines = new List<string>();
                    ExpressionResult creationResult = BuildObjectCreationExpression(semantic, context, objectCreation, creationLines);
                    List<string> initLines = new List<string>();
                    ProcessExpression(semantic, context, initializer, initLines);

                    lines.Add("Object.assign(");
                    lines.AddRange(creationLines);
                    lines.Add(", ");
                    lines.AddRange(initLines);
                    lines.Add(")");
                    return creationResult;
                }

                if (TryProcessCollectionInitializer(semantic, context, objectCreation, initializer, lines, out var collectionResult)) {
                    return collectionResult;
                }

                return ProcessExpression(semantic, context, objectCreation.Initializer, lines);
            }

            return BuildObjectCreationExpression(semantic, context, objectCreation, lines);
        }

        /// <summary>
        /// Determines whether an initializer represents an object initializer with assignments.
        /// </summary>
        /// <param name="initializer">The initializer expression to inspect.</param>
        /// <returns>True when the initializer includes assignment expressions.</returns>
        static bool IsObjectInitializer(InitializerExpressionSyntax initializer) {
            if (initializer == null) {
                return false;
            }

            return initializer.Expressions.Any(expression => expression is AssignmentExpressionSyntax);
        }

        static bool IsCollectionInitializer(InitializerExpressionSyntax initializer) {
            return initializer != null && initializer.IsKind(SyntaxKind.CollectionInitializerExpression);
        }

        bool TryProcessCollectionInitializer(
            SemanticModel semantic,
            LayerContext context,
            ObjectCreationExpressionSyntax objectCreation,
            InitializerExpressionSyntax initializer,
            List<string> lines,
            out ExpressionResult result) {
            result = default;

            if (!IsCollectionInitializer(initializer)) {
                return false;
            }

            List<string> creationLines = new List<string>();
            ExpressionResult creationResult = BuildObjectCreationExpression(semantic, context, objectCreation, creationLines);

            string collectionName = TemporaryNames.Allocate(semantic, "__collection_");

            lines.Add("(() => {");
            lines.Add("const ");
            lines.Add(collectionName);
            lines.Add(" = ");
            lines.AddRange(creationLines);
            lines.Add(";\n");

            foreach (var element in initializer.Expressions) {
                lines.Add(collectionName);
                lines.Add(".add(");

                if (element is InitializerExpressionSyntax complex) {
                    for (int i = 0; i < complex.Expressions.Count; i++) {
                        int startDepth = context.DepthClass;
                        ProcessExpression(semantic, context, complex.Expressions[i], lines);
                        context.PopClass(startDepth);

                        if (i < complex.Expressions.Count - 1) {
                            lines.Add(", ");
                        }
                    }
                } else {
                    int startDepth = context.DepthClass;
                    ProcessExpression(semantic, context, element, lines);
                    context.PopClass(startDepth);
                }

                lines.Add(");\n");
            }

            lines.Add("return ");
            lines.Add(collectionName);
            lines.Add("; })()");

            result = creationResult;
            return true;
        }

        /// <summary>
        /// Builds the TypeScript expression for object creation, including constructor overload resolution.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="objectCreation">The object creation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the creation.</returns>
        ExpressionResult BuildObjectCreationExpression(
            SemanticModel semantic,
            LayerContext context,
            ObjectCreationExpressionSyntax objectCreation,
            List<string> lines) {
            List<string> newLines = new List<string>();
            List<string> afterLines = new List<string>();

            if (objectCreation.Type is PredefinedTypeSyntax predefinedType &&
                predefinedType.Keyword.IsKind(SyntaxKind.ObjectKeyword)) {
                lines.Add("new Object()");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("object"));
            }

            int startDepth = context.DepthClass;
            ExpressionResult result = ProcessExpression(semantic, context, objectCreation.Type, afterLines);
            context.PopClass(startDepth);

            bool foundMultiple = false;
            List<ConversionFunction> constructors = null;
            IMethodSymbol externalConstructorSymbol = null;
            List<IMethodSymbol> externalConstructors = null;
            if (result.Class != null) {
                constructors = result.Class.Functions.Where(c => c.IsConstructor && !c.IsStatic).ToList();
                if (constructors.Count == 1) {
                    EnsureFunctionAsyncState(semantic, context, result.Class, constructors[0]);
                }
                if (constructors.Count > 1 || constructors.Any(c => c.IsAsync)) {
                    foundMultiple = true;
                }
            } else {
                externalConstructorSymbol = GetObjectCreationConstructorSymbol(semantic, objectCreation);
                if (externalConstructorSymbol != null) {
                    INamedTypeSymbol containingType = externalConstructorSymbol.ContainingType;
                    TypeScriptProgram tsProgram = (TypeScriptProgram)context.Program;
                    ConversionClass knownClass = null;
                    if (tsProgram != null && containingType != null) {
                        knownClass = tsProgram.GetClassByName(containingType.Name);
                    }
                    if (knownClass == null || !knownClass.IsNative) {
                        if (containingType != null) {
                            externalConstructors = containingType.Constructors.Where(c => !c.IsStatic).ToList();
                            if (externalConstructors.Count > 1) {
                                foundMultiple = true;
                            }
                        }
                    }
                }
            }

            if (foundMultiple) {
                afterLines.Add(".New");
            } else {
                newLines.Add("new ");
            }

            List<ExpressionResult> types = new List<ExpressionResult>();

            List<string> finalLines = new List<string>();
            if (objectCreation.ArgumentList == null) {
                finalLines.Add("()");
            } else {
                finalLines.Add("(");
                for (int i = 0; i < objectCreation.ArgumentList.Arguments.Count; i++) {
                    var arg = objectCreation.ArgumentList.Arguments[i];

                    int startArg = context.DepthClass;
                    types.Add(ProcessExpression(semantic, context, arg.Expression, finalLines));
                    if (types[i].Type == null) {
                        //Debugger.Break();
                    }
                    context.PopClass(startArg);

                    if (i != objectCreation.ArgumentList.Arguments.Count - 1) {
                        finalLines.Add(", ");
                    }
                }
                finalLines.Add(")");
            }

            if (foundMultiple) {
                if (result.Class != null) {
                    ConversionFunction fn = ResolveConstructorForObjectCreation(semantic, objectCreation, constructors, types);
                    if (fn == null) {
                        throw new Exception("Constructor not found");
                    }

                    EnsureFunctionAsyncState(semantic, context, result.Class, fn);

                    if (fn.IsAsync) {
                        FunctionStack functionStack = context.GetCurrentFunction();
                        if (functionStack != null) {
                            newLines.Add("await ");
                            functionStack.Function.IsAsync = true;
                        }
                    }

                    int index = constructors.IndexOf(fn);
                    afterLines.Add($"{index + 1}");
                } else {
                    if (externalConstructorSymbol == null || externalConstructors == null || externalConstructors.Count == 0) {
                        throw new Exception("Constructor not found");
                    }

                    int index = externalConstructors.IndexOf(externalConstructorSymbol);
                    if (index < 0) {
                        throw new Exception("Constructor not found");
                    }

                    afterLines.Add($"{index + 1}");
                }
            }

            lines.AddRange(newLines);
            if (foundMultiple) {
                // TypeScript forbids `Type<Args>.NewN` (property access on an instantiation
                // expression); the generic arguments move onto the generic factory method.
                lines.Add(TypeScriptUtils.MoveGenericArgumentsAfterFactoryName(string.Concat(afterLines)));
            } else {
                lines.AddRange(afterLines);
            }
            lines.AddRange(finalLines);
            return result;
        }

        /// <summary>
        /// Handles collection initializers for implicit object creation.
        /// </summary>
        bool TryProcessImplicitCollectionInitializer(
            SemanticModel semantic,
            LayerContext context,
            ImplicitObjectCreationExpressionSyntax objectCreation,
            InitializerExpressionSyntax initializer,
            List<string> lines,
            out ExpressionResult result) {
            result = default;

            if (!IsCollectionInitializer(initializer)) {
                return false;
            }

            List<string> creationLines = new List<string>();
            ExpressionResult creationResult = BuildImplicitObjectCreationExpression(semantic, context, objectCreation, creationLines);

            string collectionName = TemporaryNames.Allocate(semantic, "__collection_");

            lines.Add("(() => {");
            lines.Add("const ");
            lines.Add(collectionName);
            lines.Add(" = ");
            lines.AddRange(creationLines);
            lines.Add(";\n");

            foreach (var element in initializer.Expressions) {
                lines.Add(collectionName);
                lines.Add(".add(");

                if (element is InitializerExpressionSyntax complex) {
                    for (int i = 0; i < complex.Expressions.Count; i++) {
                        int startDepth = context.DepthClass;
                        ProcessExpression(semantic, context, complex.Expressions[i], lines);
                        context.PopClass(startDepth);

                        if (i < complex.Expressions.Count - 1) {
                            lines.Add(", ");
                        }
                    }
                } else {
                    int startDepth = context.DepthClass;
                    ProcessExpression(semantic, context, element, lines);
                    context.PopClass(startDepth);
                }

                lines.Add(");\n");
            }

            lines.Add("return ");
            lines.Add(collectionName);
            lines.Add("; })()");

            result = creationResult;
            return true;
        }

        /// <summary>
        /// Lowers the predicate Enumerable.Any overload for strings through a UTF-16-preserving runtime helper.
        /// </summary>
        /// <param name="semantic">Semantic model used to distinguish LINQ from user-defined Any methods.</param>
        /// <param name="context">Conversion context that records the NativeStringUtil runtime requirement.</param>
        /// <param name="invocationExpression">Reduced Enumerable.Any invocation over a string.</param>
        /// <param name="lines">Destination for the helper invocation.</param>
        /// <param name="result">Boolean result when the LINQ string operation is converted.</param>
        /// <returns>True only for Enumerable.Any(string, predicate) invoked as an extension method.</returns>
        bool TryProcessStringEnumerableAny(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocationExpression.ArgumentList.Arguments.Count != 1) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            bool isReducedEnumerableAny = method?.Name == "Any" && method.ReducedFrom != null &&
                method.ContainingType?.ToDisplayString() == "System.Linq.Enumerable";
            ITypeSymbol receiverType = semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (!isReducedEnumerableAny || receiverType?.SpecialType != SpecialType.System_String) {
                return false;
            }

            ArgumentSyntax predicate = invocationExpression.ArgumentList.Arguments[0];
            if (predicate.NameColon != null) {
                throw new NotSupportedException("Enumerable.Any string predicate requires positional arguments.");
            }

            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);

            List<string> predicateLines = new List<string>();
            ProcessExpression(semantic, context, predicate.Expression, predicateLines);
            context.PopClass(depth);

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeStringUtil"));
            lines.Add("NativeStringUtil.any(");
            lines.AddRange(receiverLines);
            lines.Add(", ");
            lines.AddRange(predicateLines);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }
        /// <summary>
        /// Lowers the predicate Enumerable.All overload for strings through a UTF-16-preserving runtime helper.
        /// </summary>
        bool TryProcessStringEnumerableAll(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocationExpression.ArgumentList.Arguments.Count != 1) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            bool isReducedEnumerableAll = method?.Name == "All" && method.ReducedFrom != null &&
                method.ContainingType?.ToDisplayString() == "System.Linq.Enumerable";
            ITypeSymbol receiverType = semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (!isReducedEnumerableAll || receiverType?.SpecialType != SpecialType.System_String) {
                return false;
            }

            ArgumentSyntax predicate = invocationExpression.ArgumentList.Arguments[0];
            if (predicate.NameColon != null) {
                throw new NotSupportedException("Enumerable.All string predicate requires positional arguments.");
            }

            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);

            List<string> predicateLines = new List<string>();
            ProcessExpression(semantic, context, predicate.Expression, predicateLines);
            context.PopClass(depth);

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeStringUtil"));
            lines.Add("NativeStringUtil.all(");
            lines.AddRange(receiverLines);
            lines.Add(", ");
            lines.AddRange(predicateLines);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }
        /// <summary>
        /// Lowers Enumerable.Select over a string to a UTF-16 char-array projection.
        /// </summary>
        /// <returns>True only for the positional selector overload invoked as an extension method over a string.</returns>
        bool TryProcessStringEnumerableSelect(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocationExpression.ArgumentList.Arguments.Count != 1) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            bool isReducedEnumerableSelect = method?.Name == "Select" && method.ReducedFrom != null &&
                method.ContainingType?.ToDisplayString() == "System.Linq.Enumerable";
            ITypeSymbol receiverType = semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (!isReducedEnumerableSelect || receiverType?.SpecialType != SpecialType.System_String) {
                return false;
            }

            ArgumentSyntax selector = invocationExpression.ArgumentList.Arguments[0];
            if (selector.NameColon != null) {
                throw new NotSupportedException("Enumerable.Select string selector requires positional arguments.");
            }

            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);

            List<string> selectorLines = new List<string>();
            ProcessExpression(semantic, context, selector.Expression, selectorLines);
            context.PopClass(depth);

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeStringUtil"));
            lines.Add("NativeStringUtil.select(");
            lines.AddRange(receiverLines);
            lines.Add(", ");
            lines.AddRange(selectorLines);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }
        /// <summary>
        /// Lowers String.Replace(oldValue, newValue), which replaces every literal occurrence in .NET.
        /// JavaScript String.replace without a global regex replaces only the first occurrence.
        /// </summary>
        bool TryProcessStringReplace(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name is not IdentifierNameSyntax memberName ||
                memberName.Identifier.Text != "Replace" ||
                invocationExpression.ArgumentList.Arguments.Count != 2 ||
                invocationExpression.ArgumentList.Arguments.Any(argument => argument.NameColon != null)) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            ITypeSymbol receiverType = semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (method?.ContainingType?.SpecialType != SpecialType.System_String ||
                receiverType?.SpecialType != SpecialType.System_String ||
                method.Parameters.Length != 2) {
                return false;
            }

            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);
            List<string> oldValueLines = new List<string>();
            ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0].Expression, oldValueLines);
            context.PopClass(depth);
            List<string> newValueLines = new List<string>();
            ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[1].Expression, newValueLines);
            context.PopClass(depth);

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeStringUtil"));
            lines.Add("NativeStringUtil.replace(");
            lines.AddRange(receiverLines);
            lines.Add(", ");
            lines.AddRange(oldValueLines);
            lines.Add(", ");
            lines.AddRange(newValueLines);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }
        /// <summary>
        /// Lowers String.Trim overloads with one or more character arguments through a runtime helper.
        /// JavaScript trim methods ignore supplied characters, while .NET treats them as a character set.
        /// </summary>
        bool TryProcessStringTrimCharacters(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name is not IdentifierNameSyntax memberName ||
                invocationExpression.ArgumentList.Arguments.Count == 0) {
                return false;
            }

            string mode = memberName.Identifier.Text switch {
                "Trim" => "both",
                "TrimStart" => "start",
                "TrimEnd" => "end",
                _ => null
            };
            if (mode == null) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            ITypeSymbol receiverType = semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (method?.ContainingType?.SpecialType != SpecialType.System_String ||
                receiverType?.SpecialType != SpecialType.System_String) {
                return false;
            }

            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);

            var argumentLines = new List<List<string>>();
            foreach (ArgumentSyntax argument in invocationExpression.ArgumentList.Arguments) {
                if (argument.NameColon != null) {
                    throw new NotSupportedException("String.Trim character arguments require positional arguments.");
                }
                depth = context.DepthClass;
                List<string> convertedArgument = new List<string>();
                ProcessExpression(semantic, context, argument.Expression, convertedArgument);
                context.PopClass(depth);
                argumentLines.Add(convertedArgument);
            }

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeStringUtil"));
            lines.Add("NativeStringUtil.trimCharacters(");
            lines.AddRange(receiverLines);
            lines.Add(", ");
            lines.Add(StringUtil.FormatDoubleQuotedLiteral(mode));
            foreach (List<string> argument in argumentLines) {
                lines.Add(", ");
                lines.AddRange(argument);
            }
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }
        /// <summary>
        /// Lowers String.Split(separator, StringSplitOptions) so browser output retains option semantics.
        /// </summary>
        /// <returns>True only for the two-argument String.Split overload with a StringSplitOptions value.</returns>
        bool TryProcessStringSplitWithOptions(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocationExpression.ArgumentList.Arguments.Count != 2) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            ITypeSymbol receiverType = semantic.GetTypeInfo(memberAccess.Expression).Type;
            ITypeSymbol optionType = semantic.GetTypeInfo(invocationExpression.ArgumentList.Arguments[1].Expression).Type;
            bool isStringSplit = method?.Name == "Split" &&
                method.ContainingType?.SpecialType == SpecialType.System_String &&
                receiverType?.SpecialType == SpecialType.System_String &&
                optionType?.ToDisplayString() == "System.StringSplitOptions";
            if (!isStringSplit) {
                return false;
            }

            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);
            List<string> separatorLines = new List<string>();
            ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0].Expression, separatorLines);
            context.PopClass(depth);
            List<string> optionLines = new List<string>();
            ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[1].Expression, optionLines);
            context.PopClass(depth);

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeStringUtil"));
            lines.Add("NativeStringUtil.split(");
            lines.AddRange(receiverLines);
            lines.Add(", ");
            lines.AddRange(separatorLines);
            lines.Add(", ");
            lines.AddRange(optionLines);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }
        /// <summary>
        /// Lowers Enumerable.SingleOrDefault for array-mapped reference collections, preserving duplicate-match failures.
        /// </summary>
        /// <param name="semantic">Semantic model used to identify the selected LINQ overload.</param>
        /// <param name="context">Conversion context that records NativeArrayUtil.</param>
        /// <param name="invocationExpression">Reduced SingleOrDefault invocation.</param>
        /// <param name="lines">Destination for the runtime helper call.</param>
        /// <param name="result">Reference result when the collection has zero or one matching item.</param>
        /// <returns>True for the supported predicate overload over an array-mapped reference collection.</returns>
        bool TryProcessEnumerableSingleOrDefault(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocationExpression.ArgumentList.Arguments.Count != 1) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            bool isReducedEnumerableSingleOrDefault = method?.Name == "SingleOrDefault" && method.ReducedFrom != null &&
                method.ContainingType?.ToDisplayString() == "System.Linq.Enumerable";
            ITypeSymbol returnedType = semantic.GetTypeInfo(invocationExpression).Type;
            if (!isReducedEnumerableSingleOrDefault || returnedType == null || !returnedType.IsReferenceType) {
                return false;
            }

            ArgumentSyntax predicate = invocationExpression.ArgumentList.Arguments[0];
            if (predicate.NameColon != null) {
                throw new NotSupportedException("Enumerable.SingleOrDefault predicate requires positional arguments.");
            }

            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);

            List<string> predicateLines = new List<string>();
            ProcessExpression(semantic, context, predicate.Expression, predicateLines);
            context.PopClass(depth);

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeArrayUtil"));
            lines.Add("NativeArrayUtil.singleOrDefault<");
            lines.Add(VariableUtil.GetVarType(returnedType).ToTypeScriptString((TypeScriptProgram)context.Program));
            lines.Add(">(");
            lines.AddRange(receiverLines);
            lines.Add(", ");
            lines.AddRange(predicateLines);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(returnedType));
            return true;
        }
        /// <summary>
        /// Lowers Enumerable.SequenceEqual with an explicit comparer for iterable collections.
        /// </summary>
        bool TryProcessEnumerableSequenceEqual(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocationExpression.ArgumentList.Arguments.Count != 2) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            bool isReducedEnumerableSequenceEqual = method?.Name == "SequenceEqual" && method.ReducedFrom != null &&
                method.ContainingType?.ToDisplayString() == "System.Linq.Enumerable";
            if (!isReducedEnumerableSequenceEqual) {
                return false;
            }

            ArgumentSyntax second = invocationExpression.ArgumentList.Arguments[0];
            ArgumentSyntax comparer = invocationExpression.ArgumentList.Arguments[1];
            if (second.NameColon != null || comparer.NameColon != null) {
                throw new NotSupportedException("Enumerable.SequenceEqual requires positional arguments.");
            }

            int depth = context.DepthClass;
            List<string> firstLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, firstLines);
            context.PopClass(depth);

            List<string> secondLines = new List<string>();
            ProcessExpression(semantic, context, second.Expression, secondLines);
            context.PopClass(depth);

            List<string> comparerLines = new List<string>();
            ProcessExpression(semantic, context, comparer.Expression, comparerLines);
            context.PopClass(depth);

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeArrayUtil"));
            lines.Add("NativeArrayUtil.sequenceEqual(");
            lines.AddRange(firstLines);
            lines.Add(", ");
            lines.AddRange(secondLines);
            lines.Add(", ");
            lines.AddRange(comparerLines);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }
        /// <summary>
        /// Attempts to emit a dictionary creation expression from an implicit initializer.
        /// </summary>
        bool TryProcessImplicitDictionaryCreation(
            SemanticModel semantic,
            LayerContext context,
            ImplicitObjectCreationExpressionSyntax objectCreation,
            InitializerExpressionSyntax initializer,
            List<string> lines,
            out ExpressionResult result) {
            result = default;

            if (objectCreation.ArgumentList != null && objectCreation.ArgumentList.Arguments.Count > 0) {
                return false;
            }

            TypeInfo typeInfo = semantic.GetTypeInfo(objectCreation);
            ITypeSymbol typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
            if (!IsDictionaryType(typeSymbol)) {
                return false;
            }

            TypeScriptProgram tsProgram = (TypeScriptProgram)context.Program;
            VariableType type = VariableUtil.GetVarType(typeSymbol);
            List<string> typeLines = new List<string> { type.GetTypeScriptType(tsProgram) };

            List<string> beforeLines = new List<string>();
            List<string> entryStrings = new List<string>();

            foreach (var element in initializer.Expressions) {
                if (element is not InitializerExpressionSyntax complex || complex.Expressions.Count < 2) {
                    return false;
                }

                string keyText = BuildExpressionString(semantic, context, complex.Expressions[0], beforeLines);
                string valueText = BuildExpressionString(semantic, context, complex.Expressions[1], beforeLines);
                entryStrings.Add($"[{keyText}, {valueText}]");
            }

            if (beforeLines.Count > 0) {
                lines.AddRange(beforeLines);
            }

            lines.Add("new ");
            lines.AddRange(typeLines);

            if (entryStrings.Count == 0) {
                lines.Add("()");
            } else {
                lines.Add("(undefined, [ ");
                for (int i = 0; i < entryStrings.Count; i++) {
                    if (i > 0) {
                        lines.Add(", ");
                    }
                    lines.Add(entryStrings[i]);
                }
                lines.Add(" ])");
            }

            ConversionClass resolvedClass = GetClass(tsProgram, type);
            result = new ExpressionResult(true, VariablePath.Unknown, type);
            result.Class = resolvedClass;
            return true;
        }

        /// <summary>
        /// Builds the TypeScript expression for implicit object creation.
        /// </summary>
        ExpressionResult BuildImplicitObjectCreationExpression(
            SemanticModel semantic,
            LayerContext context,
            ImplicitObjectCreationExpressionSyntax objectCreation,
            List<string> lines) {
            List<string> newLines = new List<string>();
            List<string> afterLines = new List<string>();

            TypeInfo typeInfo = semantic.GetTypeInfo(objectCreation);
            ITypeSymbol typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
            if (typeSymbol == null) {
                throw new InvalidOperationException("Unable to resolve implicit object creation type.");
            }

            TypeScriptProgram tsProgram = (TypeScriptProgram)context.Program;
            VariableType type = VariableUtil.GetVarType(typeSymbol);
            ConversionClass resolvedClass = GetClass(tsProgram, type);

            bool foundMultiple = false;
            List<ConversionFunction> constructors = null;
            IMethodSymbol externalConstructorSymbol = null;
            List<IMethodSymbol> externalConstructors = null;
            if (resolvedClass != null) {
                constructors = resolvedClass.Functions.Where(c => c.IsConstructor && !c.IsStatic).ToList();
                if (constructors.Count == 1) {
                    EnsureFunctionAsyncState(semantic, context, resolvedClass, constructors[0]);
                }
                if (constructors.Count > 1 || constructors.Any(c => c.IsAsync)) {
                    foundMultiple = true;
                }
            } else {
                externalConstructorSymbol = GetImplicitObjectCreationConstructorSymbol(semantic, objectCreation);
                if (externalConstructorSymbol != null) {
                    INamedTypeSymbol containingType = externalConstructorSymbol.ContainingType;
                    ConversionClass knownClass = null;
                    if (tsProgram != null && containingType != null) {
                        knownClass = tsProgram.GetClassByName(containingType.Name);
                    }
                    if (knownClass == null || !knownClass.IsNative) {
                        if (containingType != null) {
                            externalConstructors = containingType.Constructors.Where(c => !c.IsStatic).ToList();
                            if (externalConstructors.Count > 1) {
                                foundMultiple = true;
                            }
                        }
                    }
                }
            }

            if (foundMultiple) {
                afterLines.Add(".New");
            } else {
                newLines.Add("new ");
            }

            afterLines.Add(type.GetTypeScriptType(tsProgram));

            List<ExpressionResult> types = new List<ExpressionResult>();
            List<string> finalLines = new List<string>();
            if (objectCreation.ArgumentList == null) {
                finalLines.Add("()");
            } else {
                finalLines.Add("(");
                for (int i = 0; i < objectCreation.ArgumentList.Arguments.Count; i++) {
                    var arg = objectCreation.ArgumentList.Arguments[i];

                    int startArg = context.DepthClass;
                    types.Add(ProcessExpression(semantic, context, arg.Expression, finalLines));
                    if (types[i].Type == null) {
                        //Debugger.Break();
                    }
                    context.PopClass(startArg);

                    if (i != objectCreation.ArgumentList.Arguments.Count - 1) {
                        finalLines.Add(", ");
                    }
                }
                finalLines.Add(")");
            }

            if (foundMultiple) {
                if (resolvedClass != null) {
                    ConversionFunction fn = ResolveConstructorForImplicitObjectCreation(semantic, objectCreation, constructors, types);
                    if (fn == null) {
                        throw new Exception("Constructor not found");
                    }

                    EnsureFunctionAsyncState(semantic, context, resolvedClass, fn);

                    if (fn.IsAsync) {
                        FunctionStack functionStack = context.GetCurrentFunction();
                        if (functionStack != null) {
                            newLines.Add("await ");
                            functionStack.Function.IsAsync = true;
                        }
                    }

                    int index = constructors.IndexOf(fn);
                    afterLines.Add($"{index + 1}");
                } else {
                    if (externalConstructorSymbol == null || externalConstructors == null || externalConstructors.Count == 0) {
                        throw new Exception("Constructor not found");
                    }

                    int index = externalConstructors.IndexOf(externalConstructorSymbol);
                    if (index < 0) {
                        throw new Exception("Constructor not found");
                    }

                    afterLines.Add($"{index + 1}");
                }
            }

            ExpressionResult result = new ExpressionResult(true, VariablePath.Unknown, type);
            result.Class = resolvedClass;

            lines.AddRange(newLines);
            lines.AddRange(afterLines);
            lines.AddRange(finalLines);
            return result;
        }

        /// <summary>
        /// Resolves the constructor overload to use for an implicit object creation expression.
        /// </summary>
        ConversionFunction ResolveConstructorForImplicitObjectCreation(
            SemanticModel semantic,
            ImplicitObjectCreationExpressionSyntax objectCreation,
            List<ConversionFunction> constructors,
            List<ExpressionResult> argumentTypes) {
            if (constructors == null || constructors.Count == 0) {
                return null;
            }

            IMethodSymbol constructorSymbol = GetImplicitObjectCreationConstructorSymbol(semantic, objectCreation);
            if (constructorSymbol != null) {
                int symbolParamCount = constructorSymbol.Parameters.Length;
                for (int i = 0; i < constructors.Count; i++) {
                    ConversionFunction candidate = constructors[i];
                    if (candidate.InParameters.Count != symbolParamCount) {
                        continue;
                    }

                    bool matches = true;
                    for (int paramIndex = 0; paramIndex < symbolParamCount; paramIndex++) {
                        VariableType symbolType = VariableUtil.GetVarType(constructorSymbol.Parameters[paramIndex].Type);
                        VariableType expectedType = candidate.InParameters[paramIndex].VarType;
                        if (!AreConstructorTypesCompatible(expectedType, symbolType)) {
                            matches = false;
                            break;
                        }
                    }

                    if (matches) {
                        return candidate;
                    }
                }
            }

            if (argumentTypes != null && argumentTypes.Count > 0) {
                ConversionFunction argMatch = constructors.Find(candidate =>
                    ConstructorMatchesArgumentTypes(candidate, argumentTypes));
                if (argMatch != null) {
                    return argMatch;
                }
            }

            int argCount = argumentTypes?.Count ?? 0;
            List<ConversionFunction> countMatches = constructors.Where(c => c.InParameters.Count == argCount).ToList();
            if (countMatches.Count == 1) {
                return countMatches[0];
            }

            return null;
        }

        /// <summary>
        /// Gets the constructor symbol selected by Roslyn for a given implicit object creation expression.
        /// </summary>
        static IMethodSymbol GetImplicitObjectCreationConstructorSymbol(
            SemanticModel semantic,
            ImplicitObjectCreationExpressionSyntax objectCreation) {
            if (semantic == null || objectCreation == null) {
                return null;
            }

            SymbolInfo symbolInfo = semantic.GetSymbolInfo(objectCreation);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol &&
                methodSymbol.MethodKind == MethodKind.Constructor) {
                return methodSymbol;
            }

            if (symbolInfo.CandidateSymbols.Length > 0) {
                return symbolInfo.CandidateSymbols
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(candidate => candidate.MethodKind == MethodKind.Constructor);
            }

            return null;
        }

        /// <summary>
        /// Resolves the constructor overload to use for an object creation expression.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="objectCreation">The object creation expression being processed.</param>
        /// <param name="constructors">Available constructors for the target type.</param>
        /// <param name="argumentTypes">Resolved argument expression types.</param>
        /// <returns>The matching constructor, or null if no match is found.</returns>
        ConversionFunction ResolveConstructorForObjectCreation(
            SemanticModel semantic,
            ObjectCreationExpressionSyntax objectCreation,
            List<ConversionFunction> constructors,
            List<ExpressionResult> argumentTypes) {
            if (constructors == null || constructors.Count == 0) {
                return null;
            }

            IMethodSymbol constructorSymbol = GetObjectCreationConstructorSymbol(semantic, objectCreation);
            if (constructorSymbol != null) {
                int symbolParamCount = constructorSymbol.Parameters.Length;
                for (int i = 0; i < constructors.Count; i++) {
                    ConversionFunction candidate = constructors[i];
                    if (candidate.InParameters.Count != symbolParamCount) {
                        continue;
                    }

                    bool matches = true;
                    for (int paramIndex = 0; paramIndex < symbolParamCount; paramIndex++) {
                        VariableType symbolType = VariableUtil.GetVarType(constructorSymbol.Parameters[paramIndex].Type);
                        VariableType expectedType = candidate.InParameters[paramIndex].VarType;
                        if (!AreConstructorTypesCompatible(expectedType, symbolType)) {
                            matches = false;
                            break;
                        }
                    }

                    if (matches) {
                        return candidate;
                    }
                }
            }

            if (argumentTypes != null && argumentTypes.Count > 0) {
                ConversionFunction argMatch = constructors.Find(candidate =>
                    ConstructorMatchesArgumentTypes(candidate, argumentTypes));
                if (argMatch != null) {
                    return argMatch;
                }
            }

            int argCount = argumentTypes?.Count ?? 0;
            List<ConversionFunction> countMatches = constructors.Where(c => c.InParameters.Count == argCount).ToList();
            if (countMatches.Count == 1) {
                return countMatches[0];
            }

            return null;
        }

        /// <summary>
        /// Gets the constructor symbol selected by Roslyn for a given object creation expression.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="objectCreation">The object creation expression being resolved.</param>
        /// <returns>The constructor symbol, or null when it cannot be resolved.</returns>
        static IMethodSymbol GetObjectCreationConstructorSymbol(
            SemanticModel semantic,
            ObjectCreationExpressionSyntax objectCreation) {
            if (semantic == null || objectCreation == null) {
                return null;
            }

            SymbolInfo symbolInfo = semantic.GetSymbolInfo(objectCreation);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol &&
                methodSymbol.MethodKind == MethodKind.Constructor) {
                return methodSymbol;
            }

            if (symbolInfo.CandidateSymbols.Length > 0) {
                return symbolInfo.CandidateSymbols
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(candidate => candidate.MethodKind == MethodKind.Constructor);
            }

            return null;
        }

        /// <summary>
        /// Determines whether a constructor's parameter list matches the provided argument types.
        /// </summary>
        /// <param name="constructor">The constructor candidate.</param>
        /// <param name="argumentTypes">Resolved argument types from the invocation.</param>
        /// <returns>True when the argument types line up with the constructor.</returns>
        static bool ConstructorMatchesArgumentTypes(
            ConversionFunction constructor,
            List<ExpressionResult> argumentTypes) {
            if (constructor == null || argumentTypes == null) {
                return false;
            }

            if (constructor.InParameters.Count != argumentTypes.Count) {
                return false;
            }

            for (int i = 0; i < argumentTypes.Count; i++) {
                ExpressionResult argument = argumentTypes[i];
                if (argument.Type == null) {
                    return false;
                }

                VariableType expectedType = constructor.InParameters[i].VarType;
                if (!AreConstructorTypesCompatible(expectedType, argument.Type)) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether two variable types are compatible for constructor overload selection.
        /// </summary>
        /// <param name="expected">The constructor parameter type.</param>
        /// <param name="actual">The resolved argument type.</param>
        /// <returns>True when the types are considered compatible.</returns>
        static bool AreConstructorTypesCompatible(VariableType expected, VariableType actual) {
            if (expected == null || actual == null) {
                return false;
            }

            if (string.Equals(actual.TypeName, "null", StringComparison.Ordinal)) {
                return expected.Type != VariableDataType.Boolean &&
                    expected.Type != VariableDataType.Char &&
                    expected.Type != VariableDataType.Single &&
                    expected.Type != VariableDataType.Double &&
                    expected.Type != VariableDataType.Int8 &&
                    expected.Type != VariableDataType.UInt8 &&
                    expected.Type != VariableDataType.Int16 &&
                    expected.Type != VariableDataType.UInt16 &&
                    expected.Type != VariableDataType.Int32 &&
                    expected.Type != VariableDataType.UInt32 &&
                    expected.Type != VariableDataType.Int64 &&
                    expected.Type != VariableDataType.UInt64 &&
                    expected.Type != VariableDataType.Enum;
            }

            return string.Equals(expected.ToString(), actual.ToString(), StringComparison.Ordinal);
        }

        static bool MethodParameterTypesMatch(List<ConversionVariable> parameters, List<VariableType> types) {
            if (parameters == null || types == null) {
                return false;
            }

            if (parameters.Count != types.Count) {
                return false;
            }

            for (int i = 0; i < parameters.Count; i++) {
                VariableType expected = parameters[i].VarType;
                VariableType actual = types[i];
                if (expected == null || actual == null) {
                    return false;
                }
                if (!string.Equals(expected.ToString(), actual.ToString(), StringComparison.Ordinal)) {
                    return false;
                }
            }

            return true;
        }

        static ConversionFunction ResolveMethodSymbol(TypeScriptProgram program, IMethodSymbol methodSymbol) {
            if (program == null || methodSymbol == null || methodSymbol.ContainingType == null) {
                return null;
            }

            ConversionClass methodClass = program.GetClassByName(methodSymbol.ContainingType.Name);
            if (methodClass == null || methodClass.Functions == null) {
                return null;
            }

            var candidates = methodClass.Functions
                .Where(c => c.Name == methodSymbol.Name &&
                    c.InParameters != null &&
                    c.InParameters.Count == methodSymbol.Parameters.Length)
                .ToList();
            if (candidates.Count == 0) {
                return null;
            }

            List<VariableType> paramTypes = new List<VariableType>();
            foreach (var parameter in methodSymbol.Parameters) {
                paramTypes.Add(VariableUtil.GetVarType(parameter.Type));
            }

            ConversionFunction match = candidates.FirstOrDefault(c => MethodParameterTypesMatch(c.InParameters, paramTypes));
            if (match == null && candidates.Count == 1) {
                match = candidates[0];
            }

            return match;
        }

        static void ReplaceLastIdentifier(List<string> lines, string identifier, string replacement) {
            if (lines == null || string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(replacement)) {
                return;
            }

            for (int i = lines.Count - 1; i >= 0; i--) {
                if (lines[i] == identifier) {
                    lines[i] = replacement;
                    return;
                }
            }
        }

        static ConversionFunction ResolveInvocationFromLines(
            TypeScriptProgram program,
            List<string> lines,
            List<ExpressionResult> argumentTypes,
            int argumentCount,
            ConversionClass fallbackClass,
            out string methodName) {
            methodName = null;
            if (program == null || lines == null || lines.Count == 0) {
                return null;
            }

            int methodIndex = -1;
            for (int i = lines.Count - 1; i >= 0; i--) {
                if (IsIdentifierToken(lines[i])) {
                    methodIndex = i;
                    methodName = lines[i];
                    break;
                }
            }

            if (methodIndex == -1 || string.IsNullOrEmpty(methodName)) {
                return null;
            }

            string ownerName = null;
            if (methodIndex > 1 && lines[methodIndex - 1] == ".") {
                for (int i = methodIndex - 2; i >= 0; i--) {
                    if (IsIdentifierToken(lines[i])) {
                        ownerName = lines[i];
                        break;
                    }
                    if (lines[i] == ".") {
                        break;
                    }
                }
            }

            ConversionClass ownerClass = fallbackClass;
            if (!string.IsNullOrEmpty(ownerName) &&
                ownerName != "this" &&
                ownerName != "base") {
                ownerClass = program.GetClassByName(ownerName) ?? ownerClass;
            }

            if (ownerClass == null || ownerClass.Functions == null) {
                return null;
            }

            string resolvedName = methodName;
            int argCount = argumentCount;
            var candidates = ownerClass.Functions
                .Where(c => c.Name == resolvedName &&
                    c.InParameters != null &&
                    c.InParameters.Count == argCount)
                .ToList();
            if (candidates.Count == 0) {
                return null;
            }

            ConversionFunction match = null;
            if (argumentTypes != null && argumentTypes.Count == argCount) {
                match = candidates.FirstOrDefault(c => MethodParametersMatchExpressionResults(c.InParameters, argumentTypes));
            }
            if (match == null && candidates.Count == 1) {
                match = candidates[0];
            }

            return match;
        }

        static bool MethodParametersMatchExpressionResults(List<ConversionVariable> parameters, List<ExpressionResult> types) {
            if (parameters == null || types == null) {
                return false;
            }

            if (parameters.Count != types.Count) {
                return false;
            }

            for (int i = 0; i < parameters.Count; i++) {
                ExpressionResult arg = types[i];
                if (arg.Type == null) {
                    continue;
                }
                if (!string.Equals(parameters[i].VarType.ToString(), arg.Type.ToString(), StringComparison.Ordinal)) {
                    return false;
                }
            }

            return true;
        }

        static bool IsIdentifierToken(string token) {
            if (string.IsNullOrWhiteSpace(token)) {
                return false;
            }

            for (int i = 0; i < token.Length; i++) {
                char ch = token[i];
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '$')) {
                    return false;
                }
            }

            return true;
        }

        static int GetMethodOverloadIndex(IMethodSymbol methodSymbol) {
            if (methodSymbol == null || methodSymbol.ContainingType == null) {
                return 0;
            }

            var methods = methodSymbol.ContainingType
                .GetMembers(methodSymbol.Name)
                .OfType<IMethodSymbol>()
                .Where(m => !m.IsImplicitlyDeclared)
                .ToList();
            if (methods.Count <= 1) {
                return 0;
            }

            if (methods.Any(m => m.Locations.All(l => !l.IsInSource))) {
                return 0;
            }

            methods.Sort((a, b) => {
                var aLoc = a.Locations.FirstOrDefault(l => l.IsInSource);
                var bLoc = b.Locations.FirstOrDefault(l => l.IsInSource);
                if (aLoc == null || bLoc == null) {
                    return 0;
                }
                return aLoc.SourceSpan.Start.CompareTo(bLoc.SourceSpan.Start);
            });

            for (int i = 0; i < methods.Count; i++) {
                if (SymbolEqualityComparer.Default.Equals(methodSymbol, methods[i])) {
                    return i + 1;
                }
            }

            return 0;
        }

        /// <summary>
        /// Attempts to emit a dictionary creation expression from an initializer.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="objectCreation">The object creation expression.</param>
        /// <param name="initializer">The initializer expression for the dictionary.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="result">Outputs the expression result for the creation.</param>
        /// <returns>True when the dictionary creation was handled.</returns>
        bool TryProcessDictionaryCreation(
            SemanticModel semantic,
            LayerContext context,
            ObjectCreationExpressionSyntax objectCreation,
            InitializerExpressionSyntax initializer,
            List<string> lines,
            out ExpressionResult result) {

            result = default;

            // The runtime dictionary constructor takes up to two shape-detected arguments (a copy
            // source and/or a comparer); anything beyond that falls through to the ordinary path.
            if (objectCreation.ArgumentList != null && objectCreation.ArgumentList.Arguments.Count > 2) {
                return false;
            }

            var typeInfo = semantic.GetTypeInfo(objectCreation);
            if (!IsDictionaryType(typeInfo.Type)) {
                return false;
            }

            List<string> typeLines = new List<string>();
            int startDepth = context.DepthClass;
            result = ProcessExpression(semantic, context, objectCreation.Type, typeLines);
            context.PopClass(startDepth);

            List<string> beforeLines = new List<string>();
            List<string> entryStrings = new List<string>();
            List<string> entryKeys = new List<string>();
            List<string> entryValues = new List<string>();

            foreach (var element in initializer.Expressions) {
                if (element is InitializerExpressionSyntax complex && complex.Expressions.Count >= 2) {
                    // Collection-initializer entry: { key, value }.
                    string keyText = BuildExpressionString(semantic, context, complex.Expressions[0], beforeLines);
                    string valueText = BuildExpressionString(semantic, context, complex.Expressions[1], beforeLines);
                    entryStrings.Add($"[{keyText}, {valueText}]");
                    entryKeys.Add(keyText);
                    entryValues.Add(valueText);
                } else if (element is AssignmentExpressionSyntax assignment &&
                    assignment.Left is ImplicitElementAccessSyntax elementAccess &&
                    elementAccess.ArgumentList != null &&
                    elementAccess.ArgumentList.Arguments.Count == 1) {
                    // Index-initializer entry: ["key"] = value. Without this branch these fell into the
                    // object-initializer emission, which dropped the bracketed keys entirely.
                    string keyText = BuildExpressionString(semantic, context, elementAccess.ArgumentList.Arguments[0].Expression, beforeLines);
                    string valueText = BuildExpressionString(semantic, context, assignment.Right, beforeLines);
                    entryStrings.Add($"[{keyText}, {valueText}]");
                    entryKeys.Add(keyText);
                    entryValues.Add(valueText);
                } else {
                    return false;
                }
            }

            List<string> argumentTexts = new List<string>();
            if (objectCreation.ArgumentList != null) {
                foreach (var argument in objectCreation.ArgumentList.Arguments) {
                    argumentTexts.Add(BuildExpressionString(semantic, context, argument.Expression, beforeLines));
                }
            }

            if (beforeLines.Count > 0) {
                lines.AddRange(beforeLines);
            }

            if (entryStrings.Count > 0 && argumentTexts.Count > 0) {
                // Constructor arguments (a copy source, a comparer) AND initializer entries cannot
                // share the runtime constructor's two slots, so the entries apply through set calls.
                string dictionaryName = TemporaryNames.Allocate(semantic, "__dictionary_");
                lines.Add("(() => {\nconst ");
                lines.Add(dictionaryName);
                lines.Add(" = new ");
                lines.AddRange(typeLines);
                lines.Add($"({string.Join(", ", argumentTexts)});\n");
                for (int i = 0; i < entryKeys.Count; i++) {
                    lines.Add($"{dictionaryName}.set({entryKeys[i]}, {entryValues[i]});\n");
                }
                lines.Add($"return {dictionaryName}; }})()");
                return true;
            }

            lines.Add("new ");
            lines.AddRange(typeLines);

            if (entryStrings.Count == 0) {
                lines.Add($"({string.Join(", ", argumentTexts)})");
            } else {
                lines.Add("(undefined, [ ");
                for (int i = 0; i < entryStrings.Count; i++) {
                    if (i > 0) {
                        lines.Add(", ");
                    }
                    lines.Add(entryStrings[i]);
                }
                lines.Add(" ])");
            }

            return true;
        }

        /// <summary>
        /// Determines whether the type symbol represents a generic Dictionary type.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to inspect.</param>
        /// <returns>True when the symbol is a Dictionary type.</returns>
        static bool IsDictionaryType(ITypeSymbol typeSymbol) {
            if (typeSymbol is INamedTypeSymbol named) {
                var constructedFrom = named.ConstructedFrom;
                if (constructedFrom == null) {
                    constructedFrom = named;
                }
                if (constructedFrom.Name == "Dictionary") {
                    string ns = string.Empty;
                    var containingNamespace = constructedFrom.ContainingNamespace;
                    if (containingNamespace != null) {
                        ns = containingNamespace.ToDisplayString();
                    }
                    return ns == "System.Collections.Generic";
                }
            }
            return false;
        }

        /// <summary>
        /// Builds a string representation of an expression, collecting any prerequisite lines.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="expression">The expression to render.</param>
        /// <param name="beforeLines">Lines that must appear before the expression.</param>
        /// <returns>The expression string.</returns>
        string BuildExpressionString(SemanticModel semantic, LayerContext context, ExpressionSyntax expression, List<string> beforeLines) {
            List<string> parts = new List<string>();
            int startDepth = context.DepthClass;
            ExpressionResult res = ProcessExpression(semantic, context, expression, parts);
            context.PopClass(startDepth);

            if (res.BeforeLines != null && res.BeforeLines.Count > 0) {
                beforeLines.AddRange(res.BeforeLines);
            }

            if (res.AfterLines != null && res.AfterLines.Count > 0) {
                parts.AddRange(res.AfterLines);
            }

            return string.Concat(parts);
        }

        /// <summary>
        /// Ensures function async usage is analyzed before invocation.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="ownerClass">The class that owns the function.</param>
        /// <param name="functionFn">The function to analyze.</param>
        void EnsureFunctionAsyncState(
            SemanticModel semantic,
            LayerContext context,
            ConversionClass ownerClass,
            ConversionFunction functionFn) {
            if (functionFn == null || functionFn.IsAsync || functionFn.AsyncAnalyzed) {
                return;
            }

            functionFn.AsyncAnalyzed = true;

            if (functionFn.RawBlock == null && functionFn.ArrowExpression == null && functionFn.ConstructorInitializer == null) {
                return;
            }

            ConversionClass resolvedOwner = ownerClass;
            if (resolvedOwner == null || !resolvedOwner.Functions.Contains(functionFn)) {
                TypeScriptProgram program = (TypeScriptProgram)context.Program;
                resolvedOwner = program.Classes.FirstOrDefault(c => c.Functions.Contains(functionFn));
            }

            if (resolvedOwner == null) {
                return;
            }

            SemanticModel resolvedSemantic = resolvedOwner.Semantic ?? semantic;
            LayerContext tempContext = new TypeScriptLayerContext((TypeScriptProgram)context.Program);
            int start = tempContext.DepthClass;
            int startFn = tempContext.DepthFunction;

            tempContext.AddClass(resolvedOwner);
            tempContext.AddFunction(new FunctionStack(functionFn));

            if (functionFn.IsConstructor && functionFn.ConstructorInitializer?.ArgumentList != null) {
                var arguments = functionFn.ConstructorInitializer.ArgumentList.Arguments;
                for (int i = 0; i < arguments.Count; i++) {
                    int startArg = tempContext.DepthClass;
                    ProcessExpression(resolvedSemantic, tempContext, arguments[i].Expression, new List<string>());
                    tempContext.PopClass(startArg);
                }
            }

            if (functionFn.ArrowExpression != null) {
                ProcessArrowExpressionClause(resolvedSemantic, tempContext, functionFn.ArrowExpression, new List<string>());
            } else if (functionFn.RawBlock != null) {
                ProcessBlock(resolvedSemantic, tempContext, functionFn.RawBlock, new List<string>());
            }

            tempContext.PopClass(start);
            tempContext.PopFunction(startFn);
            functionFn.AsyncAnalyzed = true;
        }

        /// <summary>
        /// Processes member access expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="memberAccess">The member access expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="refTypes">Resolved argument types for overload matching.</param>
        /// <returns>The expression result describing the access.</returns>
        protected override ExpressionResult ProcessMemberAccessExpressionSyntax(SemanticModel semantic, LayerContext context, MemberAccessExpressionSyntax memberAccess, List<string> lines, List<ExpressionResult> refTypes) {
            if (TryProcessPrimitiveConstantMember(semantic, memberAccess, lines, out ExpressionResult primitiveConstant)) {
                return primitiveConstant;
            }

            List<string> leftLines = new List<string>();
            ExpressionResult leftResult = ProcessExpression(semantic, context, memberAccess.Expression, leftLines);
            if (leftResult.Processed) {
                if (leftResult.Type != null &&
                    leftResult.Type.IsNullable &&
                    memberAccess.Name is IdentifierNameSyntax nullableMember) {
                    string memberName = nullableMember.Identifier.Text;
                    if (memberName == "HasValue") {
                        lines.AddRange(leftLines);
                        lines.Add(" != null");
                        return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
                    }
                    if (memberName == "Value") {
                        lines.AddRange(leftLines);
                        VariableType valueType = new VariableType(leftResult.Type) { IsNullable = false };
                        return new ExpressionResult(true, leftResult.VarPath, valueType);
                    }
                }

                if (memberAccess.Name is IdentifierNameSyntax linqMember &&
                    linqMember.Identifier.Text == "ToList") {
                    IMethodSymbol linqSymbol = semantic.GetSymbolInfo(memberAccess.Name).Symbol as IMethodSymbol;
                    if (linqSymbol != null &&
                        linqSymbol.ContainingType?.Name == "Enumerable" &&
                        linqSymbol.ContainingNamespace?.ToDisplayString() == "System.Linq") {
                        lines.AddRange(leftLines);
                        lines.Add(".toList");
                        return new ExpressionResult(true, leftResult.VarPath, VariableUtil.GetVarType(linqSymbol.ReturnType));
                    }
                }

                if (leftResult.Type != null &&
                    leftResult.Type.TypeName == "KeyValuePair" &&
                    leftResult.Type.GenericArgs != null &&
                    leftResult.Type.GenericArgs.Count >= 2 &&
                    memberAccess.Name is IdentifierNameSyntax kvpMember) {
                    string memberName = kvpMember.Identifier.Text;
                    if (memberName == "Key" || memberName == "Value") {
                        VariableType memberType = memberName == "Key"
                            ? leftResult.Type.GenericArgs[0]
                            : leftResult.Type.GenericArgs[1];
                        if (memberType == null) {
                            memberType = new VariableType(VariableDataType.Object);
                        }

                        lines.AddRange(leftLines);
                        lines.Add(".");
                        lines.Add(memberName);

                        context.AddClass(GetClass((TypeScriptProgram)context.Program, memberType));
                        return new ExpressionResult(true, leftResult.VarPath, memberType);
                    }
                }

                SymbolInfo memberSymbolInfo = semantic.GetSymbolInfo(memberAccess);
                IMethodSymbol methodSymbol = memberSymbolInfo.Symbol as IMethodSymbol;
                if (methodSymbol == null && memberSymbolInfo.CandidateSymbols.Length > 0) {
                    methodSymbol = memberSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                }
                if (memberAccess.Parent is not InvocationExpressionSyntax &&
                    methodSymbol != null) {
                    TypeInfo memberTypeInfo = semantic.GetTypeInfo(memberAccess);
                    if (IsDelegateType(memberTypeInfo.ConvertedType)) {
                        lines.AddRange(leftLines);
                        lines.Add(".");

                        List<string> nameLines = new List<string>();
                        ProcessExpression(semantic, context, memberAccess.Name, nameLines, refTypes);
                        lines.AddRange(nameLines);

                        if (!methodSymbol.IsStatic) {
                            lines.Add(".bind(");
                            lines.AddRange(leftLines);
                            lines.Add(")");
                        }

                        VariableType delegateType = VariableUtil.GetVarType(memberTypeInfo.ConvertedType);
                        context.AddClass(GetClass((TypeScriptProgram)context.Program, delegateType));
                        return new ExpressionResult(true, leftResult.VarPath, delegateType);
                    }
                }

                ISymbol leftSymbol = semantic.GetSymbolInfo(memberAccess.Expression).Symbol;
                if (leftSymbol is IAliasSymbol aliasSymbol) {
                    leftSymbol = aliasSymbol.Target;
                }

                if (leftSymbol is not INamedTypeSymbol) {
                    ITypeSymbol leftTypeSymbol = semantic.GetTypeInfo(memberAccess.Expression).Type ??
                        semantic.GetTypeInfo(memberAccess.Expression).ConvertedType;
                    if (leftTypeSymbol != null && leftTypeSymbol.SpecialType == SpecialType.System_String) {
                        TypeScriptProgram tsProgram = (TypeScriptProgram)context.Program;
                        ConversionClass stringClass = tsProgram.GetClassByName("string");
                        ConversionClass currentClass = context.GetCurrentClass();
                        if (stringClass != null && currentClass != stringClass) {
                            context.AddClass(stringClass);
                        }
                    }
                }

                lines.AddRange(leftLines);
                lines.Add(".");
            }
            TypeScriptProgram program = (TypeScriptProgram)context.Program;
            ConversionClass targetClass = leftResult.Class;
            if (targetClass == null && leftResult.Type != null) {
                targetClass = GetClass(program, leftResult.Type);
            }

            if (targetClass != null) {
                int startDepth = context.DepthClass;
                context.AddClass(targetClass);
                ExpressionResult memberResult = ProcessExpression(semantic, context, memberAccess.Name, lines, refTypes);
                context.PopClass(startDepth);
                return memberResult;
            }

            return ProcessExpression(semantic, context, memberAccess.Name, lines, refTypes);
        }

        /// <summary>
        /// Determines whether a type symbol represents a delegate type.
        /// </summary>
        /// <param name="type">The type symbol to inspect.</param>
        /// <returns>True when the type is a delegate.</returns>
        static bool IsDelegateType(ITypeSymbol type) {
            if (type == null) {
                return false;
            }

            if (type.TypeKind == TypeKind.Delegate) {
                return true;
            }

            if (type is INamedTypeSymbol namedType && namedType.DelegateInvokeMethod != null) {
                return true;
            }

            return false;
        }

        /// <summary>Emits CLR primitive constants with their semantic JavaScript values.</summary>
        static bool TryProcessPrimitiveConstantMember(
            SemanticModel semantic,
            MemberAccessExpressionSyntax memberAccess,
            List<string> lines,
            out ExpressionResult result) {

            result = new ExpressionResult(false);
            if (semantic.GetSymbolInfo(memberAccess).Symbol is not IFieldSymbol field || !field.IsStatic) {
                return false;
            }

            string value = null;
            switch (field.ContainingType.SpecialType) {
                case SpecialType.System_Double:
                    if (field.Name == "Epsilon") value = "Number.MIN_VALUE";
                    else if (field.Name == "MinValue") value = "-Number.MAX_VALUE";
                    else if (field.Name == "MaxValue") value = "Number.MAX_VALUE";
                    break;
                case SpecialType.System_Single:
                    if (field.Name == "Epsilon") value = "1.401298464324817e-45";
                    else if (field.Name == "MinValue") value = "-3.4028234663852886e38";
                    else if (field.Name == "MaxValue") value = "3.4028234663852886e38";
                    break;
                case SpecialType.System_Int64:
                    if (field.Name == "MinValue") value = "-9223372036854775808";
                    else if (field.Name == "MaxValue") value = "9223372036854775807";
                    break;
                case SpecialType.System_Int32:
                    if (field.Name == "MinValue") value = "-2147483648";
                    else if (field.Name == "MaxValue") value = "2147483647";
                    break;
                case SpecialType.System_Int16:
                    if (field.Name == "MinValue") value = "-32768";
                    else if (field.Name == "MaxValue") value = "32767";
                    break;
                case SpecialType.System_SByte:
                    if (field.Name == "MinValue") value = "-128";
                    else if (field.Name == "MaxValue") value = "127";
                    break;
                case SpecialType.System_Byte:
                    if (field.Name == "MinValue") value = "0";
                    else if (field.Name == "MaxValue") value = "255";
                    break;
                case SpecialType.System_UInt16:
                    if (field.Name == "MinValue") value = "0";
                    else if (field.Name == "MaxValue") value = "65535";
                    break;
                case SpecialType.System_UInt32:
                    if (field.Name == "MinValue") value = "0";
                    else if (field.Name == "MaxValue") value = "4294967295";
                    break;
            }

            if (value == null) {
                return false;
            }
            lines.Add(value);
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(field.Type));
            return true;
        }

        /// <summary>Lowers numeric framework calls whose CLR casing or overload contract differs in JavaScript.</summary>
        bool TryProcessNumericFrameworkInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {

            result = new ExpressionResult(false);
            if (invocationExpression.ArgumentList.Arguments.Count != 1) {
                return false;
            }
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            MemberAccessExpressionSyntax numericMemberAccess = invocationExpression.Expression as MemberAccessExpressionSyntax;
            ITypeSymbol invocationReceiverType = numericMemberAccess != null
                ? semantic.GetTypeInfo(numericMemberAccess.Expression).Type
                : null;
            bool isSystemMathReceiver = invocationReceiverType?.ContainingNamespace?.ToDisplayString() == "System" &&
                (invocationReceiverType.Name == "Math" || invocationReceiverType.Name == "MathF");
            bool isFrameworkMathSyntax = numericMemberAccess != null &&
                (numericMemberAccess.Expression.ToString() == "Math" || numericMemberAccess.Expression.ToString() == "MathF" ||
                 numericMemberAccess.Expression.ToString() == "System.Math" || numericMemberAccess.Expression.ToString() == "System.MathF");
            bool isMathAbs = numericMemberAccess?.Name.Identifier.ValueText == "Abs" &&
                (isSystemMathReceiver || (method == null && isFrameworkMathSyntax));
            if (isMathAbs) {
                lines.Add("Math.abs(");
                ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0].Expression, lines);
                lines.Add(")");
                ITypeSymbol returnType = method?.ReturnType ?? semantic.GetTypeInfo(invocationExpression).Type;
                result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(returnType));
                return true;
            }

            if (method == null || method.Name != "Parse" || !TryGetNumericTryParseBounds(method.ContainingType.SpecialType, out string minimum, out string maximum)) {
                return false;
            }

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeNumberUtil"));
            lines.Add("NativeNumberUtil.parseInteger(");
            ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0].Expression, lines);
            lines.Add(", ");
            lines.Add(minimum);
            lines.Add(", ");
            lines.Add(maximum);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }

        /// <summary>
        /// Processes invocation expressions, including runtime-specific remaps.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="invocationExpression">The invocation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the invocation.</returns>
        protected override ExpressionResult ProcessInvocationExpressionSyntax(SemanticModel semantic, LayerContext context, InvocationExpressionSyntax invocationExpression, List<string> lines) {
            if (invocationExpression.Expression is IdentifierNameSyntax identifierName &&
                identifierName.Identifier.Text == "nameof") {
                lines.Add($"\"{GetNameofValue(semantic, invocationExpression)}\"");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
            }

            if (TryProcessNumericFrameworkInvocation(semantic, context, invocationExpression, lines, out ExpressionResult numericFrameworkResult)) {
                return numericFrameworkResult;
            }

            if (TryProcessThreadingPrimitiveInvocation(semantic, context, invocationExpression, lines, out ExpressionResult threadingPrimitiveResult)) {
                return threadingPrimitiveResult;
            }

            if (TryProcessFrameworkInvocationAdapter(semantic, context, invocationExpression, lines, out ExpressionResult frameworkAdapterResult)) {
                return frameworkAdapterResult;
            }

            if (TryProcessTaskAwaiter(semantic, context, invocationExpression, lines, out ExpressionResult awaitedTask)) {
                return awaitedTask;
            }

            if (TryProcessTaskConfigureAwait(semantic, context, invocationExpression, lines, out ExpressionResult configuredTask)) {
                return configuredTask;
            }

            if (TryProcessDictionaryDefaultLookup(semantic, context, invocationExpression, lines, out ExpressionResult dictionaryResult)) {
                return dictionaryResult;
            }

            if (TryProcessStringEnumerableAny(semantic, context, invocationExpression, lines, out ExpressionResult stringAnyResult)) {
                return stringAnyResult;
            }

            if (TryProcessStringEnumerableAll(semantic, context, invocationExpression, lines, out ExpressionResult stringAllResult)) {
                return stringAllResult;
            }

            if (TryProcessStringReplace(semantic, context, invocationExpression, lines, out ExpressionResult stringReplaceResult)) {
                return stringReplaceResult;
            }
            if (TryProcessStringTrimCharacters(semantic, context, invocationExpression, lines, out ExpressionResult stringTrimResult)) {
                return stringTrimResult;
            }
            if (TryProcessStringSplitWithOptions(semantic, context, invocationExpression, lines, out ExpressionResult stringSplitResult)) {
                return stringSplitResult;
            }

            if (TryProcessStringEnumerableSelect(semantic, context, invocationExpression, lines, out ExpressionResult stringSelectResult)) {
                return stringSelectResult;
            }

            if (TryProcessEnumerableGrouping(semantic, context, invocationExpression, lines, out ExpressionResult enumerableGroupingResult)) {
                return enumerableGroupingResult;
            }
            if (TryProcessEnumerableCoreOperator(semantic, context, invocationExpression, lines, out ExpressionResult enumerableCoreResult)) {
                return enumerableCoreResult;
            }
            if (TryProcessEnumerableSingle(semantic, context, invocationExpression, lines, out ExpressionResult singleResult)) {
                return singleResult;
            }

            if (TryProcessEnumerableSingleOrDefault(semantic, context, invocationExpression, lines, out ExpressionResult singleOrDefaultResult)) {
                return singleOrDefaultResult;
            }

            if (TryProcessEnumerableSequenceEqual(semantic, context, invocationExpression, lines, out ExpressionResult sequenceEqualResult)) {
                return sequenceEqualResult;
            }

            if (TryProcessArrayEmpty(semantic, invocationExpression, lines, out ExpressionResult emptyResult)) {
                return emptyResult;
            }

            if (TryProcessByteArrayToArray(semantic, context, invocationExpression, lines, out ExpressionResult byteArrayCopyResult)) {
                return byteArrayCopyResult;
            }

            if (TryProcessEnumToString(semantic, context, invocationExpression, lines, out ExpressionResult enumResult)) {
                return enumResult;
            }

            if (TryProcessPrimitiveToString(semantic, context, invocationExpression, lines, out ExpressionResult primitiveResult)) {
                return primitiveResult;
            }

            if (TryProcessNumericTryParse(semantic, context, invocationExpression, lines, out ExpressionResult numericTryParseResult)) {
                return numericTryParseResult;
            }

            if (TryProcessNumericCompareTo(semantic, context, invocationExpression, lines, out ExpressionResult numericCompareResult)) {
                return numericCompareResult;
            }

            if (TryProcessMathClamp(semantic, context, invocationExpression, lines, out ExpressionResult clampResult)) {
                return clampResult;
            }

            if (TryProcessArgumentExceptionGuard(semantic, context, invocationExpression, lines, out ExpressionResult argumentGuardResult)) {
                return argumentGuardResult;
            }

            if (TryProcessReferenceEquals(semantic, context, invocationExpression, lines, out ExpressionResult referenceEqualsResult)) {
                return referenceEqualsResult;
            }

            if (TryProcessObjectGetType(semantic, context, invocationExpression, lines, out ExpressionResult getTypeResult)) {
                return getTypeResult;
            }

            if (TryProcessStringCompare(semantic, context, invocationExpression, lines, out ExpressionResult compareResult)) {
                return compareResult;
            }

            if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "AsSpan") {
                int targetStart = context.DepthClass;
                List<string> targetLines = new List<string>();
                ExpressionResult targetResult = ProcessExpression(semantic, context, memberAccess.Expression, targetLines);
                context.PopClass(targetStart);

                lines.AddRange(targetLines);
                lines.Add(".subarray(");

                var arguments = invocationExpression.ArgumentList.Arguments;
                List<string> startLines = new List<string>();
                if (arguments.Count > 0) {
                    int argStart = context.DepthClass;
                    ProcessExpression(semantic, context, arguments[0].Expression, startLines);
                    context.PopClass(argStart);
                    lines.AddRange(startLines);
                } else {
                    lines.Add("0");
                }

                if (arguments.Count > 1) {
                    lines.Add(", ");
                    if (startLines.Count == 0) {
                        lines.Add("0");
                    } else {
                        lines.AddRange(startLines);
                    }
                    lines.Add(" + ");

                    int lengthStart = context.DepthClass;
                    List<string> lengthLines = new List<string>();
                    ProcessExpression(semantic, context, arguments[1].Expression, lengthLines);
                    context.PopClass(lengthStart);
                    lines.AddRange(lengthLines);
                }

                lines.Add(")");

                ExpressionResult spanResult = new ExpressionResult(true, targetResult.VarPath, targetResult.Type) { Class = targetResult.Class, Variable = targetResult.Variable };
                spanResult.BeforeLines = null;
                spanResult.AfterLines = null;
                return spanResult;
            }

            List<ConversionClass> conditionalClasses = null;
            if (invocationExpression.Expression is MemberBindingExpressionSyntax &&
                context.DepthClass > 1) {
                // Avoid leaking the conditional-access target type into argument resolution.
                conditionalClasses = context.SavePopClass(context.DepthClass - 1);
            }

            List<string> argLines = ["("];
            int count = 0;
            List<ExpressionResult> types = new List<ExpressionResult>();

            Dictionary<string, string> outs = new Dictionary<string, string>();
            HashSet<string> outDeclarations = new HashSet<string>();

            List<string> beforeLines = new List<string>();
            List<string> addLines = new List<string>();

            IMethodSymbol selectedInvocationMethod = GetInvocationMethodSymbol(semantic, invocationExpression);
            int nextParameterIndex = 0;
            int sourceArgumentIndex = 0;
            foreach (var arg in invocationExpression.ArgumentList.Arguments) {
                int parameterIndex = nextParameterIndex;
                if (arg.NameColon != null && selectedInvocationMethod != null) {
                    string parameterName = arg.NameColon.Name.Identifier.ValueText;
                    parameterIndex = selectedInvocationMethod.Parameters.IndexOf(
                        selectedInvocationMethod.Parameters.FirstOrDefault(parameter => parameter.Name == parameterName));
                    if (parameterIndex < nextParameterIndex) {
                        throw new NotSupportedException($"Named argument order cannot be represented safely: {arg}");
                    }
                }

                while (selectedInvocationMethod != null && nextParameterIndex < parameterIndex) {
                    string defaultValue = GetOptionalDefaultValue(selectedInvocationMethod.Parameters[nextParameterIndex]);
                    if (string.IsNullOrWhiteSpace(defaultValue)) {
                        throw new NotSupportedException($"Required parameter '{selectedInvocationMethod.Parameters[nextParameterIndex].Name}' was omitted.");
                    }
                    if (count > 0) {
                        argLines.Add(", ");
                    }
                    argLines.Add(defaultValue);
                    count++;
                    nextParameterIndex++;
                }

                if (count > 0) {
                    argLines.Add(", ");
                }
                string refKeyword = arg.RefKindKeyword.ToString();
                string strName = string.Empty;
                bool isOut = false;
                bool isOutDeclaration = false;
                if (refKeyword == "out") {
                    isOut = true;
                    isOutDeclaration = arg.Expression is DeclarationExpressionSyntax;
                    beforeLines.Add("let ");
                    strName = TemporaryNames.Allocate(semantic, "out_");
                    beforeLines.Add(strName);
                    beforeLines.Add(" = { value: undefined };\n");
                }

                int startArg = context.DepthClass;
                int argLinesIndex = argLines.Count;
                if (ShouldSpreadParamsArgument(semantic, invocationExpression, sourceArgumentIndex)) {
                    argLines.Add("...");
                }
                ExpressionResult res = ProcessExpression(semantic, context, arg.Expression, argLines);
                types.Add(res);
                context.PopClass(startArg);

                if (isOut) {
                    string outName = argLines[argLinesIndex];
                    if (!isOutDeclaration && res.Variable != null && res.Variable.Modifier.HasFlag(ParameterModifier.Out)) {
                        outName = res.Variable.Name;
                    }

                    bool isDiscardOut = string.Equals(outName, "_", StringComparison.Ordinal);
                    if (!isDiscardOut) {
                        if (isOutDeclaration) {
                            outs[outName] = strName;
                            outDeclarations.Add(outName);
                        } else if (res.Variable != null && res.Variable.Modifier.HasFlag(ParameterModifier.Out)) {
                            outs[outName + ".value"] = strName;
                        } else {
                            outs[outName] = strName;
                        }
                    }

                    argLines.RemoveAt(argLinesIndex);
                    argLines.Add(strName);
                }

                count++;
                nextParameterIndex = parameterIndex + 1;
                sourceArgumentIndex++;
            }

            AppendOptionalArguments(semantic, invocationExpression, argLines, ref count);
            argLines.Add(")");

            List<string> invoLines = new List<string>();
            if (conditionalClasses != null) {
                context.LoadClass(conditionalClasses);
            }
            ExpressionResult result = ProcessExpression(semantic, context, invocationExpression.Expression, invoLines, types);
            IEventSymbol invokedEvent = GetEventSymbol(semantic, invocationExpression.Expression);
            if (invokedEvent != null) {
                lines.AddRange(invoLines);
                lines.Add(".Invoke");
                lines.AddRange(argLines);

                VariableType returnType = GetDelegateReturnType(invokedEvent);
                ExpressionResult eventResult = new ExpressionResult(true, VariablePath.Unknown, returnType);
                eventResult.BeforeLines = beforeLines;
                eventResult.AfterLines = addLines;
                return eventResult;
            }

            IMethodSymbol invocationSymbol = GetInvocationMethodSymbol(semantic, invocationExpression);
            TypeScriptProgram program = (TypeScriptProgram)context.Program;
            ConversionFunction resolvedFunction = ResolveMethodSymbol(program, invocationSymbol);
            string invocationName = invocationSymbol?.Name;
            if (resolvedFunction == null) {
                resolvedFunction = ResolveInvocationFromLines(program, invoLines, types, count, context.GetCurrentClass(), out string lineName);
                if (string.IsNullOrEmpty(invocationName)) {
                    invocationName = lineName;
                }
            }
            string remappedName = null;
            if (resolvedFunction != null && !string.IsNullOrEmpty(resolvedFunction.Remap)) {
                remappedName = resolvedFunction.Remap;
            } else if (invocationSymbol != null) {
                int overloadIndex = GetMethodOverloadIndex(invocationSymbol);
                if (overloadIndex > 1) {
                    remappedName = $"{invocationSymbol.Name}{overloadIndex}";
                }
            }

            if (!string.IsNullOrEmpty(remappedName) &&
                !string.IsNullOrEmpty(invocationName) &&
                !string.Equals(remappedName, invocationName, StringComparison.Ordinal)) {
                ReplaceLastIdentifier(invoLines, invocationName, remappedName);
            }
            if (invocationSymbol != null && !invocationSymbol.IsStatic &&
                invocationExpression.Expression is MemberAccessExpressionSyntax stringEqualsAccess &&
                stringEqualsAccess.Name.Identifier.ValueText == "Equals" &&
                semantic.GetTypeInfo(stringEqualsAccess.Expression).Type?.SpecialType == SpecialType.System_String) {
                string memberSuffix = "." + invocationName;
                string receiver = string.Concat(invoLines);
                if (receiver.EndsWith(memberSuffix, StringComparison.Ordinal)) {
                    receiver = receiver.Substring(0, receiver.Length - memberSuffix.Length);
                    invoLines.Clear();
                    invoLines.Add("String.Equals(");
                    invoLines.Add(receiver);
                    if (invocationExpression.ArgumentList.Arguments.Count > 0) {
                        invoLines.Add(", ");
                        argLines.RemoveAt(0);
                    }
                }
            }
            TryInjectJsonSerializerReturnType(invocationSymbol, invoLines, argLines);
            bool wrapInt64 = ShouldWrapBinaryReaderInt64(invocationSymbol);
            bool forceAsync = HasTypeScriptAsyncAttribute(invocationSymbol) ||
                HasTypeScriptAsyncAttribute(invocationSymbol?.ContainingType) ||
                IsManualResetEventSlimWait(invocationSymbol);
            // A Task-valued C# call produces a task; only a source await should consume it.
            // Synchronous APIs remapped to asynchronous browser operations still need the implicit wait.
            bool shouldAwait = !IsSourceTaskType(invocationSymbol?.ReturnType) &&
                (forceAsync || (result.Type != null && result.Type.TypeName.StartsWith("Promise<")));
            if (wrapInt64) {
                lines.Add("Number(");
            }
            if (shouldAwait) {
                lines.Add("(await ");
                context.GetCurrentFunction().Function.IsAsync = true;
            }

            lines.AddRange(invoLines);

            lines.AddRange(argLines);
            if (shouldAwait) {
                lines.Add(")");
            }
            if (wrapInt64) {
                lines.Add(")");
                if (invocationSymbol?.ReturnType != null) {
                    result.Type = VariableUtil.GetVarType(invocationSymbol.ReturnType);
                }
            }

            if (invocationSymbol?.ReturnType != null &&
                (result.Type == null ||
                (result.Type.Type == VariableDataType.Object &&
                (string.IsNullOrWhiteSpace(result.Type.TypeName) ||
                string.Equals(result.Type.TypeName, "object", StringComparison.Ordinal) ||
                string.Equals(result.Type.TypeName, "any", StringComparison.Ordinal))))) {
                VariableType inferredReturn = VariableUtil.GetVarType(invocationSymbol.ReturnType);
                if (inferredReturn != null && inferredReturn.Type != VariableDataType.Void) {
                    result.Type = inferredReturn;
                }
            }

            foreach (var pair in outs) {
                if (outDeclarations.Contains(pair.Key)) {
                    addLines.Add($"let {pair.Key} = {pair.Value}.value;\n");
                } else {
                    addLines.Add($"{pair.Key} = {pair.Value}.value;\n");
                }
            }

            result.BeforeLines = beforeLines;
            result.AfterLines = addLines;
            return result;
        }

        /// <summary>
        /// Lowers atomic read/exchange operations to equivalent single-threaded JavaScript expressions.
        /// Exchange still returns the previous value and mutates the referenced storage location.
        /// </summary>
        /// <param name="semantic">Semantic model used to distinguish framework methods from user methods with the same names.</param>
        /// <param name="context">Current conversion scope.</param>
        /// <param name="invocation">Candidate framework invocation.</param>
        /// <param name="lines">Destination for the lowered expression.</param>
        /// <param name="result">Converted expression result when the invocation is supported.</param>
        /// <returns><c>true</c> when a supported threading primitive was emitted.</returns>
        bool TryProcessThreadingPrimitiveInvocation(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocation,
            List<string> lines,
            out ExpressionResult result) {

            result = default;
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocation);
            if (method == null || !method.IsStatic ||
                method.ContainingNamespace?.ToDisplayString() != "System.Threading") {
                return false;
            }

            SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
            VariableType returnType = VariableUtil.GetVarType(method.ReturnType);
            if (method.ContainingType?.Name == "Thread" && method.Name == "Sleep" && arguments.Count == 1 &&
                method.Parameters[0].Type.ToDisplayString() == "System.TimeSpan") {
                context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("Task"));
                lines.Add("await Task.Delay(");
                ProcessExpression(semantic, context, arguments[0].Expression, lines);
                lines.Add(")");
                context.GetCurrentFunction().Function.IsAsync = true;
                result = new ExpressionResult(true, VariablePath.Unknown, returnType);
                return true;
            }
            if (method.ContainingType?.Name == "Volatile" && method.Name == "Read" && arguments.Count == 1) {
                List<string> beforeLines = new List<string>();
                string target = BuildExpressionString(semantic, context, arguments[0].Expression, beforeLines);
                lines.Add(target);
                result = new ExpressionResult(true, VariablePath.Unknown, returnType) { BeforeLines = beforeLines };
                return true;
            }

            if (method.ContainingType?.Name != "Interlocked" || method.Name != "Exchange" || arguments.Count != 2) {
                return false;
            }

            if (!IsStableThreadingPrimitiveTarget(arguments[0].Expression)) {
                throw new NotSupportedException($"Interlocked.Exchange requires a stable generated storage target: {arguments[0].Expression}");
            }

            List<string> prerequisites = new List<string>();
            string exchangeTarget = BuildExpressionString(semantic, context, arguments[0].Expression, prerequisites);
            string exchangeValue = BuildExpressionString(semantic, context, arguments[1].Expression, prerequisites);
            string oldValue = TemporaryNames.Allocate(semantic, "__interlockedOld");
            lines.Add("(() => { const ");
            lines.Add(oldValue);
            lines.Add(" = ");
            lines.Add(exchangeTarget);
            lines.Add("; ");
            lines.Add(exchangeTarget);
            lines.Add(" = ");
            lines.Add(exchangeValue);
            lines.Add("; return ");
            lines.Add(oldValue);
            lines.Add("; })()");
            result = new ExpressionResult(true, VariablePath.Unknown, returnType) { BeforeLines = prerequisites };
            return true;
        }

        /// <summary>Bridges selected framework APIs to their browser runtime equivalents.</summary>
        bool TryProcessFrameworkInvocationAdapter(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocation,
            List<string> lines,
            out ExpressionResult result) {

            result = new ExpressionResult(false);
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocation);
            if (method == null || invocation.Expression is not MemberAccessExpressionSyntax memberAccess) {
                return false;
            }
            SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

            if (method.Name == "Write" && method.ContainingType?.ToDisplayString() == "System.IO.BinaryWriter" &&
                method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Int32 && arguments.Count == 1) {
                ProcessExpression(semantic, context, memberAccess.Expression, lines);
                lines.Add(".writeInt32(");
                ProcessExpression(semantic, context, arguments[0].Expression, lines);
                lines.Add(")");
                result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
                return true;
            }

            if (method.Name == "ContinueWith" &&
                method.ContainingType?.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks" &&
                arguments.Count == 2 && arguments[1].Expression.ToString().EndsWith("TaskScheduler.Default", StringComparison.Ordinal)) {
                ProcessExpression(semantic, context, memberAccess.Expression, lines);
                lines.Add(".then(");
                ProcessExpression(semantic, context, arguments[0].Expression, lines);
                lines.Add(")");
                result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
                return true;
            }

            if (method.Name == "IndexOf" && method.IsStatic && method.ContainingType?.SpecialType == SpecialType.System_Array &&
                arguments.Count == 2) {
                ProcessExpression(semantic, context, arguments[0].Expression, lines);
                lines.Add(".indexOf(");
                ProcessExpression(semantic, context, arguments[1].Expression, lines);
                lines.Add(")");
                result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
                return true;
            }

            return false;
        }

        /// <summary>Checks whether repeating a reference target preserves its JavaScript evaluation semantics.</summary>
        /// <param name="expression">Reference argument supplied to a threading primitive.</param>
        /// <returns><c>true</c> for local variables and stable field access chains.</returns>
        static bool IsStableThreadingPrimitiveTarget(ExpressionSyntax expression) {
            if (expression is IdentifierNameSyntax || expression is ThisExpressionSyntax || expression is BaseExpressionSyntax) {
                return true;
            }

            return expression is MemberAccessExpressionSyntax memberAccess &&
                IsStableThreadingPrimitiveTarget(memberAccess.Expression);
        }

        /// <summary>Identifies the blocking browser-incompatible wait overload used by mesh response coordination.</summary>
        /// <remarks>
        /// Only <c>System.Threading.ManualResetEventSlim.Wait(int)</c> is asynchronous in the browser runtime.
        /// Other framework overloads remain unconverted until their cancellation and TimeSpan semantics are implemented.
        /// </remarks>
        static bool IsManualResetEventSlimWait(IMethodSymbol method) {
            return method != null && !method.IsStatic && method.Name == "Wait" &&
                method.ContainingType?.Name == "ManualResetEventSlim" &&
                method.ContainingType.ContainingNamespace?.ToDisplayString() == "System.Threading" &&
                method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Int32;
        }
        /// <summary>Identifies the framework declarations that own Task and ValueTask operations, excluding user overrides.</summary>
        /// <param name="type">The declaring type of a resolved framework method.</param>
        /// <returns>Whether this is a framework Task or ValueTask declaration.</returns>
        static bool IsFrameworkTaskDeclaration(INamedTypeSymbol type) {
            return type != null && type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks" &&
                (type.Name == "Task" || type.Name == "ValueTask");
        }

        /// <summary>Maps framework awaiter acquisition to the task value and synchronous result consumption to browser await.</summary>
        /// <param name="semantic">Semantic model used to identify framework awaiter methods.</param>
        /// <param name="context">Conversion scope whose containing function becomes asynchronous when a result is consumed.</param>
        /// <param name="invocation">Candidate GetAwaiter or GetResult invocation.</param>
        /// <param name="lines">Destination for the promise expression or awaited result.</param>
        /// <param name="result">Converted result type and prerequisite expression work.</param>
        /// <returns>Whether a framework awaiter operation was emitted.</returns>
        bool TryProcessTaskAwaiter(SemanticModel semantic, LayerContext context, InvocationExpressionSyntax invocation, List<string> lines, out ExpressionResult result) {
            result = new ExpressionResult(false);
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocation);
            if (method == null || invocation.ArgumentList.Arguments.Count != 0 ||
                invocation.Expression is not MemberAccessExpressionSyntax access) {
                return false;
            }
            INamedTypeSymbol owner = method.ContainingType;
            bool compilerServices = owner.ContainingNamespace?.ToDisplayString() == "System.Runtime.CompilerServices";
            bool acquire = method.Name == "GetAwaiter" && (IsFrameworkTaskDeclaration(owner) ||
                (compilerServices && (owner.Name == "ConfiguredTaskAwaitable" || owner.Name == "ConfiguredValueTaskAwaitable")));
            bool consume = method.Name == "GetResult" && compilerServices &&
                (owner.Name == "TaskAwaiter" || owner.Name == "ValueTaskAwaiter" ||
                 owner.Name == "ConfiguredTaskAwaiter" || owner.Name == "ConfiguredValueTaskAwaiter");
            if (!acquire && !consume) { return false; }
            if (consume) {
                lines.Add("(await ");
                context.GetCurrentFunction().Function.IsAsync = true;
            }
            if (acquire) {
                lines.Add("((<__AwaiterTask>(__task: __AwaiterTask): __AwaiterTask => { if (__task == null) { throw new TypeError(\"Task cannot be null.\"); } return __task; }))(");
            }
            result = ProcessExpression(semantic, context, access.Expression, lines);
            if (!result.Processed) { throw new NotSupportedException($"Unsupported framework awaiter receiver: {access.Expression}"); }
            if (acquire) { lines.Add(")"); }
            if (consume) {
                lines.Add(")");
                result.Type = VariableUtil.GetVarType(method.ReturnType);
            }
            return true;
        }

        /// <summary>Preserves framework task values while evaluating the browser-irrelevant synchronization-context flag.</summary>
        /// <param name="semantic">Semantic model distinguishing framework methods from user methods with the same name.</param>
        /// <param name="context">Active conversion scope.</param>
        /// <param name="invocation">Candidate ConfigureAwait invocation.</param>
        /// <param name="lines">Destination for the task-preserving expression.</param>
        /// <param name="result">Task expression metadata and deferred expression work.</param>
        /// <returns>Whether the framework boolean ConfigureAwait overload was converted.</returns>
        bool TryProcessTaskConfigureAwait(SemanticModel semantic, LayerContext context, InvocationExpressionSyntax invocation, List<string> lines, out ExpressionResult result) {
            result = new ExpressionResult(false);
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocation);
            if (method == null || method.Name != "ConfigureAwait" || !IsFrameworkTaskDeclaration(method.ContainingType) ||
                invocation.Expression is not MemberAccessExpressionSyntax access) {
                return false;
            }
            if (method.Parameters.Length != 1 || method.Parameters[0].Type.SpecialType != SpecialType.System_Boolean) {
                throw new NotSupportedException("Browser ConfigureAwait currently requires the boolean synchronization-context overload.");
            }
            List<string> before = new List<string>();
            lines.Add("((<__ConfiguredTask>(__configuredTask: __ConfiguredTask, __captureContext: boolean): __ConfiguredTask => { if (__configuredTask == null) { throw new TypeError(\"Task cannot be null.\"); } return __configuredTask; }))(");
            ExpressionResult task = AppendConfiguredAwaitOperand(semantic, context, access.Expression, lines, before);
            lines.Add(", ");
            AppendConfiguredAwaitOperand(semantic, context, invocation.ArgumentList.Arguments[0].Expression, lines, before);
            lines.Add(")");
            result = new ExpressionResult(true, task.VarPath, task.Type) { BeforeLines = before };
            return true;
        }

        /// <summary>Emits one ConfigureAwait operand with its out-variable assignments completed before the next operand.</summary>
        /// <param name="semantic">Semantic model for expression conversion.</param>
        /// <param name="context">Active conversion scope.</param>
        /// <param name="operand">Source task receiver or context argument.</param>
        /// <param name="lines">Destination expression tokens.</param>
        /// <param name="declarations">Outer declarations needed by subsequent source statements.</param>
        /// <returns>The converted operand metadata.</returns>
        ExpressionResult AppendConfiguredAwaitOperand(SemanticModel semantic, LayerContext context, ExpressionSyntax operand, List<string> lines, List<string> declarations) {
            List<string> valueLines = new List<string>();
            int depth = context.DepthClass;
            ExpressionResult value = ProcessExpression(semantic, context, operand, valueLines);
            context.PopClass(depth);
            if (!value.Processed) {
                throw new NotSupportedException($"Unsupported ConfigureAwait operand: {operand}");
            }
            List<string> assignments = new List<string>();
            if (value.AfterLines != null) { SplitAfterLines(value.AfterLines, declarations, assignments); }
            if ((value.BeforeLines == null || value.BeforeLines.Count == 0) && assignments.Count == 0) {
                lines.AddRange(valueLines);
                return value;
            }
            bool awaits = valueLines.Any(token => token.TrimStart('(').StartsWith("await ", StringComparison.Ordinal));
            string temporary = TemporaryNames.Allocate(semantic, "__configuredOperand");
            // Box the return value of an async operand wrapper so Promise assimilation cannot consume the task itself.
            lines.Add(awaits ? "(await (async () => { " : "(() => { ");
            if (value.BeforeLines != null) { lines.AddRange(value.BeforeLines); }
            lines.Add("const " + temporary + " = ");
            lines.AddRange(valueLines);
            lines.Add("; ");
            lines.AddRange(assignments);
            lines.Add(awaits ? "return { value: " + temporary + " }; })()).value" : "return " + temporary + "; })()");
            return value;
        }

        /// <summary>Recognizes framework task values before TypeScript erasure so invocation emission preserves explicit C# awaiting.</summary>
        /// <param name="type">Resolved C# invocation return type, including derived Task types.</param>
        /// <returns>True for Task or ValueTask values whose completion must remain controlled by the source.</returns>
        static bool IsSourceTaskType(ITypeSymbol type) {
            for (ITypeSymbol current = type; current != null; current = current.BaseType) {
                if ((current.Name == "Task" || current.Name == "ValueTask") &&
                    current.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks") {
                    return true;
                }
            }
            return false;
        }

        static void TryInjectJsonSerializerReturnType(IMethodSymbol invocationSymbol, List<string> invocationLines, List<string> argLines) {
            if (invocationSymbol == null || argLines == null || argLines.Count < 2) {
                return;
            }

            if (!string.Equals(invocationSymbol.Name, "Deserialize", StringComparison.Ordinal)) {
                return;
            }

            if (!invocationSymbol.IsGenericMethod || invocationSymbol.TypeArguments.Length == 0) {
                return;
            }

            string containingType = invocationSymbol.ContainingType?.ToDisplayString();
            if (!string.Equals(containingType, "System.Text.Json.JsonSerializer", StringComparison.Ordinal)) {
                return;
            }

            if (invocationSymbol.TypeArguments[0] is ITypeParameterSymbol) {
                return;
            }

            string typeName = invocationSymbol.TypeArguments[0].ToDisplayString();
            if (string.IsNullOrWhiteSpace(typeName)) {
                return;
            }

            string returnTypeExpr = $"Type.GetType(\"{typeName}\")";
            int firstDelimiter = argLines.IndexOf(", ");
            if (firstDelimiter >= 0) {
                argLines.Insert(firstDelimiter + 1, returnTypeExpr);
                argLines.Insert(firstDelimiter + 2, ", ");
                StripGenericInvocation(invocationLines);
                return;
            }

            int closeIndex = argLines.Count - 1;
            if (closeIndex <= 0) {
                return;
            }
            argLines.Insert(closeIndex, returnTypeExpr);
            argLines.Insert(closeIndex, ", ");
            StripGenericInvocation(invocationLines);
        }

        static void StripGenericInvocation(List<string> invocationLines) {
            if (invocationLines == null || invocationLines.Count == 0) {
                return;
            }

            int start = invocationLines.LastIndexOf("<");
            if (start < 0) {
                return;
            }
            int end = invocationLines.IndexOf(">", start + 1);
            if (end < 0 || end <= start) {
                return;
            }
            invocationLines.RemoveRange(start, end - start + 1);
        }

        static bool ShouldWrapBinaryReaderInt64(IMethodSymbol methodSymbol) {
            if (methodSymbol == null) {
                return false;
            }

            if (!string.Equals(methodSymbol.Name, "ReadInt64", StringComparison.Ordinal)) {
                return false;
            }

            string containingType = methodSymbol.ContainingType?.ToDisplayString();
            return string.Equals(containingType, "System.IO.BinaryReader", StringComparison.Ordinal);
        }

        static void AppendOptionalArguments(
            SemanticModel semantic,
            InvocationExpressionSyntax invocationExpression,
            List<string> argLines,
            ref int count) {
            IMethodSymbol methodSymbol = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (methodSymbol == null || invocationExpression.ArgumentList == null) {
                return;
            }

            var parameters = methodSymbol.Parameters;
            // Split(char, StringSplitOptions.None) maps to JavaScript's unlimited split.
            // Its omitted options value is not the JavaScript result-count limit.
            if (methodSymbol.ContainingType.SpecialType == SpecialType.System_String &&
                methodSymbol.Name == "Split" && count == 1 && parameters.Length == 2 &&
                parameters[0].Type.SpecialType == SpecialType.System_Char &&
                parameters[1].Type.ToDisplayString() == "System.StringSplitOptions" &&
                parameters[1].HasExplicitDefaultValue && Equals(parameters[1].ExplicitDefaultValue, 0)) {
                return;
            }

            if (count >= parameters.Length) {
                return;
            }

            for (int i = count; i < parameters.Length; i++) {
                IParameterSymbol parameter = parameters[i];
                if (parameter.IsParams) {
                    return;
                }
                string defaultValue = GetOptionalDefaultValue(parameter);
                if (string.IsNullOrWhiteSpace(defaultValue)) {
                    break;
                }

                if (count > 0) {
                    argLines.Add(", ");
                }
                argLines.Add(defaultValue);
                count++;
            }
        }

        /// <summary>
        /// Determines whether one C# params-array argument must expand into TypeScript rest arguments.
        /// </summary>
        static bool ShouldSpreadParamsArgument(SemanticModel semantic, InvocationExpressionSyntax invocationExpression, int argumentIndex) {
            if (semantic == null || invocationExpression?.ArgumentList == null || argumentIndex < 0 ||
                argumentIndex >= invocationExpression.ArgumentList.Arguments.Count) {
                return false;
            }

            ArgumentSyntax argument = invocationExpression.ArgumentList.Arguments[argumentIndex];
            if (argument.NameColon != null) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (method == null || argumentIndex >= method.Parameters.Length || method.Parameters.Length != invocationExpression.ArgumentList.Arguments.Count) {
                return false;
            }

            // The TypeScript Task runtime deliberately accepts one iterable for WhenAll. Expanding the
            // source array would target a rest signature which the runtime neither declares nor needs.
            if (method.Name == "WhenAll" && method.ContainingType?.ToDisplayString() == "System.Threading.Tasks.Task") {
                return false;
            }

            IParameterSymbol parameter = method.Parameters[argumentIndex];
            if (!parameter.IsParams) {
                return false;
            }

            TypeInfo argumentType = semantic.GetTypeInfo(argument.Expression);
            ITypeSymbol actualType = argumentType.Type ?? argumentType.ConvertedType;
            return actualType != null && SymbolEqualityComparer.Default.Equals(actualType, parameter.Type);
        }
        static IMethodSymbol GetInvocationMethodSymbol(
            SemanticModel semantic,
            InvocationExpressionSyntax invocationExpression) {
            SymbolInfo symbolInfo = semantic.GetSymbolInfo(invocationExpression);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol) {
                return methodSymbol;
            }

            if (symbolInfo.CandidateSymbols.Length > 0) {
                return symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            }

            return null;
        }

        static string GetOptionalDefaultValue(IParameterSymbol parameter) {
            if (parameter == null) {
                return null;
            }

            if (parameter.IsParams) {
                return "[]";
            }

            if (!parameter.HasExplicitDefaultValue) {
                return null;
            }

            return FormatDefaultValue(parameter.ExplicitDefaultValue);
        }

        static string FormatDefaultValue(object value) {
            switch (value) {
                case null:
                    return "null";
                case bool b:
                    return b ? "true" : "false";
                case string s:
                    return QuoteString(s);
                case char ch:
                    return QuoteString(ch.ToString());
                case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                default:
                    return QuoteString(value.ToString() ?? string.Empty);
            }
        }

        static string QuoteString(string value) {
            StringBuilder builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char ch in value) {
                switch (ch) {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(ch);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        static IEventSymbol GetEventSymbol(SemanticModel semantic, ExpressionSyntax expression) {
            if (semantic == null || expression == null) {
                return null;
            }

            SymbolInfo symbolInfo = semantic.GetSymbolInfo(expression);
            if (symbolInfo.Symbol is IEventSymbol eventSymbol) {
                return eventSymbol;
            }
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol &&
                (methodSymbol.MethodKind == MethodKind.EventAdd || methodSymbol.MethodKind == MethodKind.EventRemove)) {
                return methodSymbol.AssociatedSymbol as IEventSymbol;
            }

            if (symbolInfo.CandidateSymbols.Length > 0) {
                IEventSymbol candidateEvent = symbolInfo.CandidateSymbols.OfType<IEventSymbol>().FirstOrDefault();
                if (candidateEvent != null) {
                    return candidateEvent;
                }

                IMethodSymbol candidateMethod = symbolInfo.CandidateSymbols
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.MethodKind == MethodKind.EventAdd || m.MethodKind == MethodKind.EventRemove);
                if (candidateMethod != null) {
                    return candidateMethod.AssociatedSymbol as IEventSymbol;
                }
            }

            if (expression is MemberAccessExpressionSyntax memberAccess) {
                SymbolInfo memberSymbolInfo = semantic.GetSymbolInfo(memberAccess.Name);
                if (memberSymbolInfo.Symbol is IEventSymbol memberEvent) {
                    return memberEvent;
                }
                if (memberSymbolInfo.Symbol is IMethodSymbol memberMethod &&
                    (memberMethod.MethodKind == MethodKind.EventAdd || memberMethod.MethodKind == MethodKind.EventRemove)) {
                    return memberMethod.AssociatedSymbol as IEventSymbol;
                }
            }

            return null;
        }

        static VariableType GetDelegateReturnType(IEventSymbol eventSymbol) {
            if (eventSymbol?.Type is INamedTypeSymbol namedType &&
                namedType.DelegateInvokeMethod != null) {
                return VariableUtil.GetVarType(namedType.DelegateInvokeMethod.ReturnType);
            }

            return new VariableType(VariableDataType.Void);
        }

        void AppendMethodGroupBind(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax expression,
            List<string> lines) {
            if (semantic == null || context == null || expression == null || lines == null) {
                return;
            }

            if (lines.Any(l => l.Contains(".bind("))) {
                return;
            }

            IMethodSymbol methodSymbol = GetMethodSymbol(semantic, expression);
            if (methodSymbol == null || methodSymbol.IsStatic) {
                return;
            }

            if (expression is MemberAccessExpressionSyntax memberAccess) {
                int startDepth = context.Class.Count;
                List<string> targetLines = new List<string>();
                ProcessExpression(semantic, context, memberAccess.Expression, targetLines);
                context.PopClass(startDepth);

                if (targetLines.Count == 0) {
                    return;
                }

                lines.Add(".bind(");
                lines.AddRange(targetLines);
                lines.Add(")");
                return;
            }

            if (expression is IdentifierNameSyntax) {
                lines.Add(".bind(");
                lines.Add("this");
                lines.Add(")");
            }
        }

        static IMethodSymbol GetMethodSymbol(SemanticModel semantic, ExpressionSyntax expression) {
            if (semantic == null || expression == null) {
                return null;
            }

            SymbolInfo symbolInfo = semantic.GetSymbolInfo(expression);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol) {
                return methodSymbol;
            }

            if (symbolInfo.CandidateSymbols.Length > 0) {
                return symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            }

            return null;
        }

        /// <summary>
        /// Determines whether a symbol has the TypeScript async attribute applied.
        /// </summary>
        /// <param name="symbol">Symbol to inspect.</param>
        /// <returns>True when the attribute is present.</returns>
        static bool HasTypeScriptAsyncAttribute(ISymbol symbol) {
            if (symbol == null) {
                return false;
            }

            foreach (AttributeData attribute in symbol.GetAttributes()) {
                INamedTypeSymbol attributeType = attribute.AttributeClass;
                if (attributeType == null) {
                    continue;
                }

                string name = attributeType.Name;
                if (string.Equals(name, "TypeScriptAsyncAttribute", StringComparison.Ordinal) ||
                    string.Equals(name, "TypeScriptAsync", StringComparison.Ordinal)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Handles System.Array.Empty&lt;T&gt; invocations with runtime-friendly TypeScript output.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="invocationExpression">The invocation expression to inspect.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="result">Outputs the expression result when the invocation is handled.</param>
        /// <returns>True when the invocation was handled.</returns>
        bool TryProcessArrayEmpty(
            SemanticModel semantic,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            SymbolInfo symbolInfo = semantic.GetSymbolInfo(invocationExpression);
            IMethodSymbol methodSymbol = symbolInfo.Symbol as IMethodSymbol;
            if (methodSymbol == null && symbolInfo.CandidateSymbols.Length > 0) {
                methodSymbol = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            }

            if (methodSymbol == null ||
                !string.Equals(methodSymbol.Name, "Empty", StringComparison.Ordinal) ||
                methodSymbol.ContainingType == null ||
                methodSymbol.ContainingType.Name != "Array" ||
                methodSymbol.ContainingType.ContainingNamespace == null ||
                methodSymbol.ContainingType.ContainingNamespace.ToDisplayString() != "System") {
                return false;
            }

            ITypeSymbol typeArgument = methodSymbol.TypeArguments.Length > 0 ? methodSymbol.TypeArguments[0] : null;
            if (typeArgument != null && typeArgument.SpecialType == SpecialType.System_Byte) {
                lines.Add("new Uint8Array(0)");
            } else {
                lines.Add("[]");
            }

            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(methodSymbol.ReturnType));
            return true;
        }

        /// <summary>
        /// Resolves the string literal to emit for a nameof invocation.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="invocationExpression">The nameof invocation expression.</param>
        /// <returns>The resolved name for the nameof argument.</returns>
        string GetNameofValue(SemanticModel semantic, InvocationExpressionSyntax invocationExpression) {
            if (invocationExpression.ArgumentList == null ||
                invocationExpression.ArgumentList.Arguments.Count == 0) {
                throw new InvalidOperationException("nameof requires a single argument.");
            }

            ExpressionSyntax expression = invocationExpression.ArgumentList.Arguments[0].Expression;
            ISymbol symbol = semantic.GetSymbolInfo(expression).Symbol;
            if (symbol != null && !string.IsNullOrWhiteSpace(symbol.Name)) {
                return symbol.Name;
            }

            if (expression is IdentifierNameSyntax identifier) {
                return identifier.Identifier.Text;
            }

            if (expression is MemberAccessExpressionSyntax memberAccess) {
                return memberAccess.Name.Identifier.Text;
            }

            if (expression is QualifiedNameSyntax qualified) {
                return qualified.Right.Identifier.Text;
            }

            if (expression is GenericNameSyntax genericName) {
                return genericName.Identifier.Text;
            }

            return expression.ToString();
        }

        /// <summary>
        /// Emits a reference to the current instance.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="thisExpression">The this expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessThisExpressionSyntax(SemanticModel semantic, LayerContext context, ThisExpressionSyntax thisExpression, List<string> lines) {
            lines.Add("this");
            context.AddClass(context.Class[0]);
        }

        /// <summary>
        /// Processes binary expressions, emitting operands and operators.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="binary">The binary expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the binary expression.</returns>
        protected override ExpressionResult ProcessBinaryExpressionSyntax(SemanticModel semantic, LayerContext context, BinaryExpressionSyntax binary, List<string> lines) {
            if (binary.IsKind(SyntaxKind.CoalesceExpression) && binary.Right is ThrowExpressionSyntax throwExpression) {
                List<string> throwLeft = new List<string>();
                int startThrowLeft = context.DepthClass;
                ExpressionResult throwResult = ProcessExpression(semantic, context, binary.Left, throwLeft);
                context.PopClass(startThrowLeft);

                List<string> thrown = new List<string>();
                int startThrowRight = context.DepthClass;
                ProcessExpression(semantic, context, throwExpression.Expression, thrown);
                context.PopClass(startThrowRight);

                lines.AddRange(throwLeft);
                lines.Add(" ?? ");
                lines.Add("(() => { throw ");
                lines.AddRange(thrown);
                lines.Add("; })()");
                return throwResult;
            }

            List<string> left = new List<string>();
            int startLeft = context.DepthClass;
            ExpressionResult leftResult = ProcessExpression(semantic, context, binary.Left, left);
            context.PopClass(startLeft);

            List<string> right = new List<string>();
            int startRight = context.DepthClass;
            ExpressionResult rightResult = ProcessExpression(semantic, context, binary.Right, right);
            context.PopClass(startRight);

            BinaryOpTypes op = ParseBinaryOperator(binary.Kind());
            bool convertLeftCharacterToCodeUnit = RequiresCharacterCodeUnit(binary, semantic, binary.Left);
            bool convertRightCharacterToCodeUnit = RequiresCharacterCodeUnit(binary, semantic, binary.Right);

            if (op == BinaryOpTypes.As && string.IsNullOrWhiteSpace(string.Concat(right)) && binary.Right is TypeSyntax asTypeSyntax) {
                // Composite type syntaxes (`as byte[]`) have no expression emission and left the cast
                // target empty; map the TYPE through the variable-type pipeline instead
                // (byte[] becomes Uint8Array).
                VariableType asType = VariableUtil.GetVarType(asTypeSyntax, semantic);
                right.Clear();
                right.Add(asType.ToTypeScriptString((TypeScriptProgram)context.Program));
            }

            bool hasLeftBefore = leftResult.BeforeLines != null && leftResult.BeforeLines.Count > 0;
            bool hasLeftAfter = leftResult.AfterLines != null && leftResult.AfterLines.Count > 0;
            bool hasRightBefore = rightResult.BeforeLines != null && rightResult.BeforeLines.Count > 0;
            bool hasRightAfter = rightResult.AfterLines != null && rightResult.AfterLines.Count > 0;

            if (!hasLeftBefore && !hasLeftAfter && !hasRightBefore && !hasRightAfter) {
                AppendCharacterCodeUnitOperand(lines, left, convertLeftCharacterToCodeUnit);
                lines.Add($" {op.ToStringOperator()} ");
                AppendCharacterCodeUnitOperand(lines, right, convertRightCharacterToCodeUnit);
                return leftResult;
            }

            List<string> preludeLines = new List<string>();
            string resultVar = TemporaryNames.Allocate(semantic, "__binary_");
            string leftVar = TemporaryNames.Allocate(semantic, "__left_");
            string rightVar = TemporaryNames.Allocate(semantic, "__right_");

            preludeLines.Add("let ");
            preludeLines.Add(resultVar);
            preludeLines.Add(";\n");

            List<string> declarationLines = new List<string>();
            List<string> leftAssignments = new List<string>();
            List<string> rightAssignments = new List<string>();

            if (hasLeftAfter) {
                SplitAfterLines(leftResult.AfterLines, declarationLines, leftAssignments);
            }
            if (hasRightAfter) {
                SplitAfterLines(rightResult.AfterLines, declarationLines, rightAssignments);
            }

            if (declarationLines.Count > 0) {
                preludeLines.AddRange(declarationLines);
            }

            if (hasLeftBefore) {
                preludeLines.AddRange(leftResult.BeforeLines);
            }
            preludeLines.Add("const ");
            preludeLines.Add(leftVar);
            preludeLines.Add(" = ");
            preludeLines.AddRange(left);
            preludeLines.Add(";\n");
            if (leftAssignments.Count > 0) {
                preludeLines.AddRange(leftAssignments);
            }

            bool isShortCircuit = op == BinaryOpTypes.BinAnd || op == BinaryOpTypes.BinOr || op == BinaryOpTypes.Coalesce;
            if (isShortCircuit) {
                preludeLines.Add("if (");
                if (op == BinaryOpTypes.BinAnd) {
                    preludeLines.Add("!");
                    preludeLines.Add(leftVar);
                } else if (op == BinaryOpTypes.BinOr) {
                    preludeLines.Add(leftVar);
                } else {
                    preludeLines.Add(leftVar);
                    preludeLines.Add(" !== null && ");
                    preludeLines.Add(leftVar);
                    preludeLines.Add(" !== undefined");
                }
                preludeLines.Add(") {\n");
                preludeLines.Add(resultVar);
                preludeLines.Add(" = ");
                preludeLines.Add(leftVar);
                preludeLines.Add(";\n");
                preludeLines.Add("} else {\n");

                if (hasRightBefore) {
                    preludeLines.AddRange(rightResult.BeforeLines);
                }
                preludeLines.Add("const ");
                preludeLines.Add(rightVar);
                preludeLines.Add(" = ");
                preludeLines.AddRange(right);
                preludeLines.Add(";\n");
                if (rightAssignments.Count > 0) {
                    preludeLines.AddRange(rightAssignments);
                }

                preludeLines.Add(resultVar);
                preludeLines.Add(" = ");
                preludeLines.Add(rightVar);
                preludeLines.Add(";\n");
                preludeLines.Add("}\n");
            } else {
                if (hasRightBefore) {
                    preludeLines.AddRange(rightResult.BeforeLines);
                }
                preludeLines.Add("const ");
                preludeLines.Add(rightVar);
                preludeLines.Add(" = ");
                preludeLines.AddRange(right);
                preludeLines.Add(";\n");
                if (rightAssignments.Count > 0) {
                    preludeLines.AddRange(rightAssignments);
                }
                preludeLines.Add(resultVar);
                preludeLines.Add(" = ");
                AppendCharacterCodeUnitOperand(preludeLines, new List<string> { leftVar }, convertLeftCharacterToCodeUnit);
                preludeLines.Add($" {op.ToStringOperator()} ");
                AppendCharacterCodeUnitOperand(preludeLines, new List<string> { rightVar }, convertRightCharacterToCodeUnit);
                preludeLines.Add(";\n");
            }

            lines.Add(resultVar);

            leftResult.BeforeLines = preludeLines;
            leftResult.AfterLines = null;
            return leftResult;
        }
        /// <summary>
        /// Determines whether an operand must be converted from its emitted JavaScript string representation
        /// to a UTF-16 code unit for a C# character arithmetic operation.
        /// </summary>
        static bool RequiresCharacterCodeUnit(BinaryExpressionSyntax binary, SemanticModel semantic, ExpressionSyntax operand) {
            if (!IsCharacterArithmetic(binary, semantic)) {
                return false;
            }

            TypeInfo operandType = semantic.GetTypeInfo(operand);
            ITypeSymbol typeSymbol = operandType.Type ?? operandType.ConvertedType;
            return typeSymbol?.SpecialType == SpecialType.System_Char;
        }

        /// <summary>
        /// Limits code-unit conversion to numeric C# operators. Character comparisons preserve their
        /// JavaScript string form, and string concatenation is intentionally outside this conversion.
        /// </summary>
        static bool IsCharacterArithmetic(BinaryExpressionSyntax binary, SemanticModel semantic) {
            switch (binary.Kind()) {
                case SyntaxKind.AddExpression:
                case SyntaxKind.SubtractExpression:
                case SyntaxKind.DivideExpression:
                case SyntaxKind.MultiplyExpression:
                case SyntaxKind.ModuloExpression:
                case SyntaxKind.BitwiseAndExpression:
                case SyntaxKind.BitwiseOrExpression:
                case SyntaxKind.ExclusiveOrExpression:
                case SyntaxKind.LeftShiftExpression:
                case SyntaxKind.RightShiftExpression:
                    break;
                default:
                    return false;
            }

            TypeInfo resultType = semantic.GetTypeInfo(binary);
            ITypeSymbol typeSymbol = resultType.Type ?? resultType.ConvertedType;
            return typeSymbol != null && IsNumericSpecialType(typeSymbol.SpecialType);
        }

        /// <summary>Appends a possibly character-valued operand as a UTF-16 code unit.</summary>
        static void AppendCharacterCodeUnitOperand(List<string> output, List<string> operand, bool convertToCodeUnit) {
            if (!convertToCodeUnit) {
                output.AddRange(operand);
                return;
            }

            output.Add("(");
            output.AddRange(operand);
            output.Add(").charCodeAt(0)");
        }

        static BinaryOpTypes ParseBinaryOperator(SyntaxKind kind) {
            switch (kind) {
                case SyntaxKind.AddExpression:
                    return BinaryOpTypes.Plus;
                case SyntaxKind.SubtractExpression:
                    return BinaryOpTypes.Minus;
                case SyntaxKind.DivideExpression:
                    return BinaryOpTypes.Divide;
                case SyntaxKind.MultiplyExpression:
                    return BinaryOpTypes.Multiply;
                case SyntaxKind.GreaterThanExpression:
                    return BinaryOpTypes.GreaterThan;
                case SyntaxKind.GreaterThanOrEqualExpression:
                    return BinaryOpTypes.GreaterThanOrEqual;
                case SyntaxKind.LessThanExpression:
                    return BinaryOpTypes.LessThan;
                case SyntaxKind.LessThanOrEqualExpression:
                    return BinaryOpTypes.LessThanOrEqual;
                case SyntaxKind.EqualsExpression:
                    return BinaryOpTypes.Equal;
                case SyntaxKind.LogicalAndExpression:
                    return BinaryOpTypes.BinAnd;
                case SyntaxKind.LogicalOrExpression:
                    return BinaryOpTypes.BinOr;
                case SyntaxKind.LogicalNotExpression:
                    return BinaryOpTypes.BinNot;
                case SyntaxKind.NotEqualsExpression:
                    return BinaryOpTypes.NotEqual;
                case SyntaxKind.IsExpression:
                    return BinaryOpTypes.InstanceOf;
                case SyntaxKind.BitwiseAndExpression:
                    return BinaryOpTypes.BitwiseAnd;
                case SyntaxKind.BitwiseNotExpression:
                    return BinaryOpTypes.BitwiseNot;
                case SyntaxKind.BitwiseOrExpression:
                    return BinaryOpTypes.BitwiseOr;
                case SyntaxKind.RightShiftExpression:
                    return BinaryOpTypes.RightShift;
                case SyntaxKind.LeftShiftExpression:
                    return BinaryOpTypes.LeftShift;
                case SyntaxKind.ExclusiveOrExpression:
                    return BinaryOpTypes.ExclusiveOr;
                case SyntaxKind.ModuloExpression:
                    return BinaryOpTypes.Modulo;
                case SyntaxKind.CoalesceExpression:
                    return BinaryOpTypes.Coalesce;
                case SyntaxKind.AsExpression:
                    return BinaryOpTypes.As;
                default:
                    throw new Exception("Unknown binary");
            }
        }

        static void SplitAfterLines(
            List<string> afterLines,
            List<string> declarations,
            List<string> assignments) {
            if (afterLines == null || afterLines.Count == 0) {
                return;
            }

            for (int i = 0; i < afterLines.Count; i++) {
                string line = afterLines[i];
                if (line == null) {
                    continue;
                }

                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("let ", StringComparison.Ordinal)) {
                    string remainder = trimmed[4..];
                    int eqIndex = remainder.IndexOf('=');
                    if (eqIndex > 0) {
                        string name = remainder[..eqIndex].Trim();
                        string value = remainder[(eqIndex + 1)..].Trim();
                        if (!string.IsNullOrWhiteSpace(name)) {
                            declarations.Add($"let {name};\n");
                            assignments.Add($"{name} = {value}\n");
                            continue;
                        }
                    }
                }

                assignments.Add(line);
            }
        }

        /// <summary>
        /// Processes pattern matching expressions into TypeScript checks.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="patternExpression">The pattern expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the pattern match.</returns>
        ExpressionResult ProcessIsPatternExpression(
            SemanticModel semantic,
            LayerContext context,
            IsPatternExpressionSyntax patternExpression,
            List<string> lines) {
            lines.Add("(() => { const __pattern = ");
            int startDepth = context.DepthClass;
            ProcessExpression(semantic, context, patternExpression.Expression, lines);
            context.PopClass(startDepth);
            lines.Add("; return ");

            List<string> conditionLines = new List<string>();
            if (!TryAppendPatternCondition(semantic, context, patternExpression.Pattern, "__pattern", conditionLines, out string declaredVariable)) {
                throw new NotSupportedException($"Unsupported pattern expression: {patternExpression.Pattern}");
            }

            lines.AddRange(conditionLines);
            if (!string.IsNullOrWhiteSpace(declaredVariable) &&
                TryGetPatternTypeScriptName(semantic, context, patternExpression.Pattern, out string patternType)) {
                lines.Add(" && ((");
                lines.Add(declaredVariable);
                lines.Add(" = <");
                lines.Add(patternType);
                lines.Add("><unknown>__pattern), true)");
            }

            lines.Add("; })()");
            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
        }

        /// <summary>
        /// Appends a TypeScript condition for a pattern match against a target identifier.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="pattern">The pattern to convert.</param>
        /// <param name="targetIdentifier">The identifier to test.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="declaredVariable">Outputs the declared variable name, if any.</param>
        /// <returns>True when the pattern was converted.</returns>
        bool TryAppendPatternCondition(
            SemanticModel semantic,
            LayerContext context,
            PatternSyntax pattern,
            string targetIdentifier,
            List<string> lines,
            out string declaredVariable) {
            declaredVariable = string.Empty;

            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern) {
                return TryAppendPatternCondition(semantic, context, parenthesizedPattern.Pattern, targetIdentifier, lines, out declaredVariable);
            }

            if (pattern is ConstantPatternSyntax constantPattern) {
                ISymbol constantSymbol = semantic.GetSymbolInfo(constantPattern.Expression).Symbol;
                if (constantSymbol is INamedTypeSymbol constantType) {
                    AppendPatternTypeCheck(context, constantType.ToDisplayString(), constantType, targetIdentifier, lines);
                    return true;
                }

                lines.Add(targetIdentifier);
                lines.Add(" === ");
                int constantDepth = context.DepthClass;
                ProcessExpression(semantic, context, constantPattern.Expression, lines);
                context.PopClass(constantDepth);
                return true;
            }

            if (pattern is DiscardPatternSyntax) {
                lines.Add("true");
                return true;
            }

            if (pattern is VarPatternSyntax varPattern) {
                if (varPattern.Designation is SingleVariableDesignationSyntax varDesignation) {
                    declaredVariable = varDesignation.Identifier.Text;
                }
                lines.Add("true");
                return true;
            }

            if (pattern is DeclarationPatternSyntax declarationPattern) {
                if (declarationPattern.Designation is SingleVariableDesignationSyntax designation) {
                    declaredVariable = designation.Identifier.Text;
                }

                string typeName = declarationPattern.Type.ToString();
                if (string.Equals(NormalizePatternTypeName(typeName), "var", StringComparison.OrdinalIgnoreCase)) {
                    lines.Add("true");
                    return true;
                }
                AppendPatternTypeCheck(context, typeName, semantic.GetTypeInfo(declarationPattern.Type).Type, targetIdentifier, lines);
                return true;
            }

            if (pattern is TypePatternSyntax typePattern) {
                AppendPatternTypeCheck(context, typePattern.Type.ToString(), semantic.GetTypeInfo(typePattern.Type).Type, targetIdentifier, lines);
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.IsKind(SyntaxKind.NotPattern)) {
                lines.Add("!(");
                if (!TryAppendPatternCondition(semantic, context, unaryPattern.Pattern, targetIdentifier, lines, out _)) {
                    return false;
                }
                lines.Add(")");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Appends a runtime check for a type pattern.
        /// </summary>
        /// <param name="typeName">The type name from the pattern.</param>
        /// <param name="patternType">Resolved Roslyn type, when available.</param>
        /// <param name="targetIdentifier">The identifier to test.</param>
        /// <param name="lines">The output lines to append to.</param>
        void AppendPatternTypeCheck(LayerContext context, string typeName, ITypeSymbol patternType, string targetIdentifier, List<string> lines) {
            string normalizedTypeName = NormalizePatternTypeName(typeName);
            if (IsGenericTypeParameterName(context, normalizedTypeName)) {
                lines.Add(targetIdentifier);
                lines.Add(" != null");
                return;
            }
            if (string.Equals(normalizedTypeName, "bool", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedTypeName, "Boolean", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("typeof ");
                lines.Add(targetIdentifier);
                lines.Add(" === \"boolean\"");
                return;
            }

            if (string.Equals(normalizedTypeName, "string", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedTypeName, "String", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedTypeName, "char", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedTypeName, "Char", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("typeof ");
                lines.Add(targetIdentifier);
                lines.Add(" === \"string\"");
                return;
            }

            if (IsNumericPatternType(normalizedTypeName)) {
                lines.Add("typeof ");
                lines.Add(targetIdentifier);
                lines.Add(" === \"number\"");
                return;
            }

            if (string.Equals(normalizedTypeName, "object", StringComparison.OrdinalIgnoreCase)) {
                lines.Add(targetIdentifier);
                lines.Add(" != null");
                return;
            }

            if (string.Equals(normalizedTypeName, "IEnumerable", StringComparison.OrdinalIgnoreCase)) {
                lines.Add(targetIdentifier);
                lines.Add(" != null && typeof ");
                lines.Add(targetIdentifier);
                lines.Add(".GetEnumerator === \"function\"");
                return;
            }

            if (string.Equals(normalizedTypeName, "IDisposable", StringComparison.OrdinalIgnoreCase)) {
                lines.Add(targetIdentifier);
                lines.Add(" != null && typeof ");
                lines.Add(targetIdentifier);
                lines.Add(".dispose === \"function\"");
                return;
            }

            if (patternType is INamedTypeSymbol namedPatternType && namedPatternType.TypeKind == TypeKind.Interface) {
                AppendInterfacePatternTypeCheck(context, namedPatternType, targetIdentifier, lines);
                return;
            }

            lines.Add(targetIdentifier);
            lines.Add(" instanceof ");
            lines.Add(normalizedTypeName);
        }

        /// <summary>Emits a structural runtime check for the abstract members required by an interface.</summary>
        /// <param name="context">Current conversion scope used to render the TypeScript interface name.</param>
        /// <param name="interfaceType">Resolved interface whose required members define the runtime shape.</param>
        /// <param name="targetIdentifier">Identifier containing the candidate value.</param>
        /// <param name="lines">Destination for the structural condition.</param>
        static void AppendInterfacePatternTypeCheck(
            LayerContext context,
            INamedTypeSymbol interfaceType,
            string targetIdentifier,
            List<string> lines) {

            string typeName = IsErasedFrameworkInterfacePattern(interfaceType)
                ? "any"
                : VariableUtil.GetVarType(interfaceType).ToTypeScriptString((TypeScriptProgram)context.Program);
            List<INamedTypeSymbol> interfaces = new List<INamedTypeSymbol> { interfaceType };
            interfaces.AddRange(interfaceType.AllInterfaces);
            HashSet<string> checkedMembers = new HashSet<string>(StringComparer.Ordinal);

            lines.Add(targetIdentifier);
            lines.Add(" != null");
            foreach (ISymbol member in interfaces.SelectMany(candidate => candidate.GetMembers())) {
                if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary && method.IsAbstract) {
                    string memberName = method.Name == "Dispose" ? "dispose" : method.Name;
                    if (!checkedMembers.Add("method:" + memberName)) {
                        continue;
                    }
                    lines.Add(" && typeof (<");
                    lines.Add(typeName);
                    lines.Add("><unknown>");
                    lines.Add(targetIdentifier);
                    lines.Add(").");
                    lines.Add(memberName);
                    lines.Add(" === \"function\"");
                } else if ((member is IPropertySymbol || member is IEventSymbol) && checkedMembers.Add("member:" + member.Name)) {
                    lines.Add(" && \"");
                    lines.Add(member.Name);
                    lines.Add("\" in (<object>");
                    lines.Add(targetIdentifier);
                    lines.Add(")");
                }
            }
        }

        /// <summary>Resolves the TypeScript type introduced by a declaration pattern.</summary>
        /// <param name="semantic">Semantic model for the pattern syntax.</param>
        /// <param name="context">Current conversion scope.</param>
        /// <param name="pattern">Pattern that may declare a typed variable.</param>
        /// <param name="typeName">Rendered TypeScript type when available.</param>
        /// <returns><c>true</c> when the pattern declares a concrete type.</returns>
        static bool TryGetPatternTypeScriptName(
            SemanticModel semantic,
            LayerContext context,
            PatternSyntax pattern,
            out string typeName) {

            typeName = string.Empty;
            while (pattern is ParenthesizedPatternSyntax parenthesized) {
                pattern = parenthesized.Pattern;
            }
            if (pattern is UnaryPatternSyntax unary && unary.IsKind(SyntaxKind.NotPattern)) {
                pattern = unary.Pattern;
                while (pattern is ParenthesizedPatternSyntax parenthesizedInner) {
                    pattern = parenthesizedInner.Pattern;
                }
            }
            if (pattern is not DeclarationPatternSyntax declarationPattern) {
                return false;
            }

            ITypeSymbol resolvedType = semantic.GetTypeInfo(declarationPattern.Type).Type;
            if (resolvedType is INamedTypeSymbol interfaceType && IsErasedFrameworkInterfacePattern(interfaceType)) {
                typeName = "any";
                return true;
            }
            HashSet<string> erasedTypeParameters = GetStaticClassGenericParameters(context);
            if (resolvedType is ITypeParameterSymbol typeParameter &&
                erasedTypeParameters?.Contains(typeParameter.Name) == true) {
                typeName = "any";
                return true;
            }

            VariableType variableType = VariableUtil.GetVarType(declarationPattern.Type, semantic);
            if (variableType == null) {
                return false;
            }
            typeName = variableType.ToTypeScriptString((TypeScriptProgram)context.Program);
            return !string.IsNullOrWhiteSpace(typeName);
        }

        /// <summary>Identifies framework interfaces used only as structural pattern contracts in browser output.</summary>
        /// <param name="interfaceType">Resolved pattern interface.</param>
        /// <returns><c>true</c> when no TypeScript runtime declaration exists for the interface.</returns>
        static bool IsErasedFrameworkInterfacePattern(INamedTypeSymbol interfaceType) {
            if (!string.Equals(interfaceType?.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal)) {
                return false;
            }

            return string.Equals(interfaceType.Name, "IFormattable", StringComparison.Ordinal) ||
                string.Equals(interfaceType.Name, "IConvertible", StringComparison.Ordinal);
        }

        static bool IsGenericTypeParameterName(LayerContext context, string typeName) {
            if (context == null || string.IsNullOrWhiteSpace(typeName)) {
                return false;
            }

            FunctionStack fn = context.GetCurrentFunction();
            if (fn?.Function?.GenericParameters != null &&
                fn.Function.GenericParameters.Contains(typeName, StringComparer.Ordinal)) {
                return true;
            }

            ConversionClass cl = context.GetCurrentClass();
            if (cl?.GenericArgs != null &&
                cl.GenericArgs.Contains(typeName, StringComparer.Ordinal)) {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Normalizes a pattern type name by stripping generic arguments and namespaces.
        /// </summary>
        /// <param name="typeName">The raw type name from syntax.</param>
        /// <returns>The simplified type name.</returns>
        string NormalizePatternTypeName(string typeName) {
            if (string.IsNullOrWhiteSpace(typeName)) {
                return string.Empty;
            }

            string trimmed = typeName.Trim();
            int genericIndex = trimmed.IndexOf('<');
            if (genericIndex >= 0) {
                trimmed = trimmed.Substring(0, genericIndex);
            }

            int namespaceIndex = trimmed.LastIndexOf('.');
            if (namespaceIndex >= 0 && namespaceIndex < trimmed.Length - 1) {
                trimmed = trimmed.Substring(namespaceIndex + 1);
            }

            return trimmed;
        }

        /// <summary>
        /// Determines whether the pattern type name maps to a numeric TypeScript value.
        /// </summary>
        /// <param name="typeName">The type name to inspect.</param>
        /// <returns>True when the type should be checked as a number.</returns>
        bool IsNumericPatternType(string typeName) {
            return string.Equals(typeName, "sbyte", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "byte", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "short", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "ushort", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "int", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "uint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "long", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "ulong", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "float", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "double", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "decimal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "Int16", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "UInt16", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "Int32", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "UInt32", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "Int64", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "UInt64", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "Single", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "Double", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Attempts to get a declared variable name from a pattern designation.
        /// </summary>
        /// <param name="pattern">The pattern to inspect.</param>
        /// <param name="declaredVariable">Outputs the declared variable name if present.</param>
        /// <returns>True when a designation exists on the pattern.</returns>
        static bool TryGetPatternDesignation(PatternSyntax pattern, out string declaredVariable) {
            declaredVariable = string.Empty;

            while (pattern is ParenthesizedPatternSyntax parenthesizedPattern) {
                pattern = parenthesizedPattern.Pattern;
            }

            if (pattern is UnaryPatternSyntax unaryPattern && unaryPattern.IsKind(SyntaxKind.NotPattern)) {
                pattern = unaryPattern.Pattern;
                while (pattern is ParenthesizedPatternSyntax parenthesizedInner) {
                    pattern = parenthesizedInner.Pattern;
                }
            }

            if (pattern is DeclarationPatternSyntax declarationPattern &&
                declarationPattern.Designation is SingleVariableDesignationSyntax designation) {
                declaredVariable = designation.Identifier.Text;
                return true;
            }

            if (pattern is VarPatternSyntax varPattern &&
                varPattern.Designation is SingleVariableDesignationSyntax varDesignation) {
                declaredVariable = varDesignation.Identifier.Text;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Processes switch expressions into IIFE-based TypeScript expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="switchExpression">The switch expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the switch expression.</returns>
        ExpressionResult ProcessSwitchExpression(
            SemanticModel semantic,
            LayerContext context,
            SwitchExpressionSyntax switchExpression,
            List<string> lines) {
            List<string> switchLines = new List<string>();
            switchLines.Add("(() => {");
            switchLines.Add("const __switch = ");
            int startDepth = context.DepthClass;
            ProcessExpression(semantic, context, switchExpression.GoverningExpression, switchLines);
            context.PopClass(startDepth);
            switchLines.Add(";");

            foreach (var arm in switchExpression.Arms) {
                switchLines.Add("if (");

                if (!TryAppendSwitchPatternCondition(semantic, context, arm.Pattern, switchLines, out string declaredVariable)) {
                    throw new NotSupportedException($"Unsupported switch expression pattern: {arm.Pattern}");
                }

                if (arm.WhenClause != null) {
                    switchLines.Add(" && (");
                    int whenDepth = context.DepthClass;
                    ProcessExpression(semantic, context, arm.WhenClause.Condition, switchLines);
                    context.PopClass(whenDepth);
                    switchLines.Add(")");
                }

                switchLines.Add(") {");

                if (!string.IsNullOrEmpty(declaredVariable)) {
                    switchLines.Add("const ");
                    switchLines.Add(declaredVariable);
                    switchLines.Add(" = __switch;");
                }

                if (arm.Expression is ThrowExpressionSyntax throwExpression) {
                    switchLines.Add("throw ");
                    int throwDepth = context.DepthClass;
                    ProcessExpression(semantic, context, throwExpression.Expression, switchLines);
                    context.PopClass(throwDepth);
                    switchLines.Add(";");
                } else {
                    switchLines.Add("return ");
                    int armDepth = context.DepthClass;
                    ProcessExpression(semantic, context, arm.Expression, switchLines);
                    context.PopClass(armDepth);
                    switchLines.Add(";");
                }
                switchLines.Add("}");
            }

            switchLines.Add("throw new Error(\"Non-exhaustive switch expression.\");");
            switchLines.Add("})()");

            // Any awaiting arm makes the plain IIFE illegal ('await' outside an async function); the
            // wrapper becomes an awaited async IIFE, which is only reachable from an async caller
            // because the awaiting arm already marked the enclosing function async.
            bool containsAwait = switchLines.Any(line => line != null && line.Contains("await ", StringComparison.Ordinal));
            if (containsAwait) {
                switchLines[0] = "(await (async () => {";
                switchLines[switchLines.Count - 1] = "})())";
            }

            lines.AddRange(switchLines);
            return new ExpressionResult(true);
        }

        /// <summary>
        /// Appends a TypeScript condition for the given switch pattern.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="pattern">The pattern to convert.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="declaredVariable">Outputs a declared variable name for var patterns.</param>
        /// <returns>True when the pattern was converted.</returns>
        bool TryAppendSwitchPatternCondition(
            SemanticModel semantic,
            LayerContext context,
            PatternSyntax pattern,
            List<string> lines,
            out string declaredVariable) {
            return TryAppendPatternCondition(semantic, context, pattern, "__switch", lines, out declaredVariable);
        }

        /// <summary>
        /// Processes collection expressions into TypeScript array literals.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="collectionExpression">The collection expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the collection.</returns>
        ExpressionResult ProcessCollectionExpression(
            SemanticModel semantic,
            LayerContext context,
            CollectionExpressionSyntax collectionExpression,
            List<string> lines) {
            ITypeSymbol convertedType = semantic.GetTypeInfo(collectionExpression).ConvertedType;
            bool constructsHashSet = convertedType is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.HashSet<T>";
            bool retainsInterfaceContract = convertedType is INamedTypeSymbol interfaceType && interfaceType.TypeKind == TypeKind.Interface &&
                (interfaceType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlyList<T>" ||
                 interfaceType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlyCollection<T>" ||
                 interfaceType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.ICollection<T>" ||
                 interfaceType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>");
            if (constructsHashSet) {
                context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("HashSet"));
                lines.Add("new ");
                lines.Add(VariableUtil.GetVarType(convertedType).ToTypeScriptString((TypeScriptProgram)context.Program));
                lines.Add("(");
            } else if (retainsInterfaceContract) {
                lines.Add("<");
                lines.Add(VariableUtil.GetVarType(convertedType).ToTypeScriptString((TypeScriptProgram)context.Program));
                lines.Add("><unknown>");
            }
            lines.Add("[");

            var elements = collectionExpression.Elements;
            for (int i = 0; i < elements.Count; i++) {
                CollectionElementSyntax element = elements[i];
                int startDepth = context.DepthClass;

                if (element is ExpressionElementSyntax expressionElement) {
                    ProcessExpression(semantic, context, expressionElement.Expression, lines);
                } else if (element is SpreadElementSyntax spreadElement) {
                    lines.Add("...");
                    ProcessExpression(semantic, context, spreadElement.Expression, lines);
                } else {
                    lines.Add(element.ToString());
                }

                context.PopClass(startDepth);

                if (i != elements.Count - 1) {
                    lines.Add(", ");
                }
            }

            lines.Add("]");
            if (constructsHashSet) {
                lines.Add(")");
            }
            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(convertedType));
        }

        /// <summary>
        /// Emits a generic type name with type arguments.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="generic">The generic name syntax.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessGenericNameSyntax(SemanticModel semantic, LayerContext context, GenericNameSyntax generic, List<string> lines) {
            lines.Add(generic.Identifier.ToString());

            lines.Add("<");

            int count = generic.TypeArgumentList.Arguments.Count;
            int i = 0;
            foreach (var genType in generic.TypeArgumentList.Arguments) {
                VariableType type = VariableUtil.GetVarType(genType, semantic);
                HashSet<string> erasedTypeParameters = GetStaticClassGenericParameters(context);
                if (erasedTypeParameters != null && erasedTypeParameters.Count > 0) {
                    type = ReplaceTypeParameters(type, erasedTypeParameters);
                }
                lines.Add(type.ToTypeScriptString((TypeScriptProgram)context.Program));

                if (i < count - 1) {
                    lines.Add(",");
                }

                i++;
            }
            lines.Add(">");
        }

        /// <summary>
        /// Emits an implicit array creation expression as a literal array.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="implicitArray">The implicit array creation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessImplicitArrayCreationExpression(SemanticModel semantic, LayerContext context, ImplicitArrayCreationExpressionSyntax implicitArray, List<string> lines) {
            // Start the array literal
            lines.Add("[");

            // Process each expression in the initializer
            for (int i = 0; i < implicitArray.Initializer.Expressions.Count; i++) {
                int expressionDepth = context.DepthClass;
                ProcessExpression(semantic, context, implicitArray.Initializer.Expressions[i], lines);
                context.PopClass(expressionDepth);

                // Add a comma separator if it's not the last element
                if (i < implicitArray.Initializer.Expressions.Count - 1) {
                    lines.Add(", ");
                }
            }

            // Close the array literal
            lines.Add("]");
        }

        /// <summary>
        /// Emits an await expression.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="awaitExpression">The await expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessAwait(SemanticModel semantic, LayerContext context, AwaitExpressionSyntax awaitExpression, List<string> lines) {
            lines.Add("await ");

            ProcessExpression(semantic, context, awaitExpression.Expression, lines);
        }

        /// <summary>
        /// Processes qualified names by emitting their left and right parts.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="qualifiedName">The qualified name syntax.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the name.</returns>
        protected override ExpressionResult ProcessQualifiedName(SemanticModel semantic, LayerContext context, QualifiedNameSyntax qualifiedName, List<string> lines) {
            // Process the left part of the qualified name (e.g., "System" in "System.Console")
            if (ProcessExpression(semantic, context, qualifiedName.Left, lines).Processed) {
                // Add the dot separator
                lines.Add(".");
            }

            // Process the right part of the qualified name (e.g., "Console" in "System.Console")
            return ProcessExpression(semantic, context, qualifiedName.Right, lines);
        }

        /// <summary>
        /// Emits a typeof expression, normalizing known primitives.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="typeOfExpression">The typeof expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessTypeOfExpression(SemanticModel semantic, LayerContext context, TypeOfExpressionSyntax typeOfExpression, List<string> lines) {
            ITypeSymbol typeSymbol = semantic.GetTypeInfo(typeOfExpression.Type).Type;
            if (typeSymbol == null) {
                lines.Add("Type.object");
                return;
            }

            if (typeSymbol.SpecialType == SpecialType.System_String ||
                typeSymbol.SpecialType == SpecialType.System_Char) {
                lines.Add("Type.string");
                return;
            }

            if (typeSymbol.SpecialType == SpecialType.System_Boolean) {
                lines.Add("Type.boolean");
                return;
            }

            if (typeSymbol.SpecialType == SpecialType.System_Object) {
                lines.Add("Type.object");
                return;
            }

            if (IsNumericSpecialType(typeSymbol.SpecialType)) {
                lines.Add("Type.number");
                return;
            }

            string fullName = typeSymbol.ToDisplayString();
            bool wrapForMemberAccess = typeOfExpression.Parent is MemberAccessExpressionSyntax;
            if (wrapForMemberAccess) {
                lines.Add("(");
            }
            lines.Add("Type.GetType(");
            lines.Add(QuoteString(fullName));
            lines.Add(") ?? Type.object");
            if (wrapForMemberAccess) {
                lines.Add(")");
            }
        }

        /// <summary>
        /// Normalizes a TypeScript type name for typeof emission.
        /// </summary>
        /// <param name="tsType">The TypeScript type name.</param>
        /// <returns>The normalized typeof target.</returns>
        static string NormalizeTypeForTypeof(string tsType) {
            if (string.IsNullOrWhiteSpace(tsType)) {
                return "Object";
            }

            if (tsType.EndsWith("[]", StringComparison.Ordinal)) {
                return "Array";
            }

            if (tsType.StartsWith("Array<", StringComparison.Ordinal)) {
                return "Array";
            }

            int genericIndex = tsType.IndexOf('<');
            if (genericIndex > 0) {
                tsType = tsType.Substring(0, genericIndex);
            }

            return tsType switch {
                "number" => "Number",
                "boolean" => "Boolean",
                "string" => "String",
                "any" => "Object",
                _ => tsType
            };
        }

        /// <summary>
        /// Processes simple lambda expressions with a single parameter.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="simpleLambda">The simple lambda expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessSimpleLambdaExpression(SemanticModel semantic, LayerContext context, SimpleLambdaExpressionSyntax simpleLambda, List<string> lines) {
            TypeScriptProgram tsProgram = (TypeScriptProgram)context.Program;
            TypeInfo type = semantic.GetTypeInfo(simpleLambda);
            int start = context.DepthClass;
            if (type.ConvertedType is INamedTypeSymbol namedFuncType) {
                var invoke = namedFuncType.DelegateInvokeMethod;
                if (invoke == null) {
                    throw new NotImplementedException();
                }

                ITypeSymbol returnType = invoke.ReturnType;
                ConversionClass returnClass = tsProgram.GetClassByName(returnType.Name);
                if (returnClass != null && context.GetCurrentClass() == null) {
                    start = context.AddClass(returnClass);
                }
            } else {
                throw new NotImplementedException();
            }

            bool isAsync = false;
            List<string> bodyLines = new List<string>();
            ExpressionResult bodyResult = default;
            bool hasBodyResult = false;

            if (simpleLambda.Body is ExpressionSyntax expressionBody) {
                bodyResult = ProcessExpression(semantic, context, expressionBody, bodyLines);
                hasBodyResult = true;
                if (bodyResult.Type != null && bodyResult.Type.TypeName.StartsWith("Promise<", StringComparison.Ordinal)) {
                    isAsync = true;
                }
                if (!isAsync && bodyLines.Any(l => l.Contains("await "))) {
                    isAsync = true;
                }
            } else if (simpleLambda.Body is BlockSyntax preprocessedBlock) {
                // Process the block BEFORE emitting the lambda header so async infection is visible: a
                // synchronous C# lambda whose body calls a method that became async in TS gains awaits
                // during conversion, and emitting `x => { await ... }` without `async` is invalid TS.
                bodyLines.Add("{\n");
                ProcessStatement(semantic, context, preprocessedBlock, bodyLines);
                bodyLines.Add("}\n");
                if (bodyLines.Any(l => l.Contains("await "))) {
                    isAsync = true;
                }
            }

            if (isAsync) {
                lines.Add("async ");
            }

            lines.Add(simpleLambda.Parameter.Identifier.Text);
            lines.Add(" => ");

            if (hasBodyResult && bodyResult.BeforeLines != null) {
                lines.AddRange(bodyResult.BeforeLines);
            }

            if (simpleLambda.Body is ExpressionSyntax || simpleLambda.Body is BlockSyntax) {
                lines.AddRange(bodyLines);
            }

            if (hasBodyResult && bodyResult.AfterLines != null) {
                lines.AddRange(bodyResult.AfterLines);
            }

            context.PopClass(start);
        }

        /// <summary>
        /// Processes explicit array creation expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="arrayCreation">The array creation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the array.</returns>
        protected override ExpressionResult ProcessArrayCreationExpression(SemanticModel semantic, LayerContext context, ArrayCreationExpressionSyntax arrayCreation, List<string> lines) {
            // Check if there's an initializer (e.g., new int[] { 1, 2, 3 })
            if (arrayCreation.Initializer != null) {
                lines.Add("[");
                for (int i = 0; i < arrayCreation.Initializer.Expressions.Count; i++) {
                    int expressionDepth = context.DepthClass;
                    ProcessExpression(semantic, context, arrayCreation.Initializer.Expressions[i], lines);
                    context.PopClass(expressionDepth);

                    if (i < arrayCreation.Initializer.Expressions.Count - 1) {
                        lines.Add(", ");
                    }
                }
                lines.Add("]");
            }
            // If it's an array with specified size (e.g., new int[5])
            else if (arrayCreation.Type.RankSpecifiers.Any()) {
                if (arrayCreation.Type.ElementType is PredefinedTypeSyntax predefined &&
                    predefined.Keyword.ToString() == "byte") {
                    lines.Add("new Uint8Array(");
                } else {
                    lines.Add("new Array(");
                }

                foreach (var rankSpecifier in arrayCreation.Type.RankSpecifiers) {
                    foreach (var size in rankSpecifier.Sizes) {
                        ProcessExpression(semantic, context, size, lines);
                    }
                }
                lines.Add(")");
            }

            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(arrayCreation.Type, semantic));
        }

        /// <summary>
        /// Processes parenthesized expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="parenthesizedExpression">The parenthesized expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override ExpressionResult ProcessParenthesizedExpression(SemanticModel semantic, LayerContext context, ParenthesizedExpressionSyntax parenthesizedExpression, List<string> lines) {
            lines.Add("(");
            ExpressionResult result = ProcessExpression(semantic, context, parenthesizedExpression.Expression, lines);
            lines.Add(")");
            return result;
        }

        /// <summary>
        /// Emits a base expression as a TypeScript super reference.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="baseExpression">The base expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessBaseExpression(SemanticModel semantic, LayerContext context, BaseExpressionSyntax baseExpression, List<string> lines) {
            lines.Add("super");

            context.AddClass(context.GetCurrentClass());

        }

        /// <summary>
        /// Processes collection and object initializer expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="initializerExpression">The initializer expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessInitializerExpression(SemanticModel semantic, LayerContext context, InitializerExpressionSyntax initializerExpression, List<string> lines) {
            bool isArray = false;
            if (initializerExpression.Kind().ToString() == "ArrayInitializerExpression") {
                isArray = true;
                lines.Add("[ ");
            } else {
                lines.Add("{ ");
            }

            for (int i = 0; i < initializerExpression.Expressions.Count; i++) {
                int expressionDepth = context.DepthClass;
                ProcessExpression(semantic, context, initializerExpression.Expressions[i], lines);
                context.PopClass(expressionDepth);

                if (i < initializerExpression.Expressions.Count - 1) {
                    lines.Add(", ");
                }
            }

            if (isArray) {
                lines.Add(" ]");
            } else {
                lines.Add(" }");
            }
        }

        /// <summary>
        /// Processes tuple expressions into array literals.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="tupleExpression">The tuple expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessTupleExpression(SemanticModel semantic, LayerContext context, TupleExpressionSyntax tupleExpression, List<string> lines) {
            lines.Add("[");

            for (int i = 0; i < tupleExpression.Arguments.Count; i++) {
                ProcessExpression(semantic, context, tupleExpression.Arguments[i].Expression, lines);

                if (i < tupleExpression.Arguments.Count - 1) {
                    lines.Add(", ");
                }
            }

            lines.Add("]");
        }

        /// <summary>
        /// Processes predefined type keywords into TypeScript type names.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="predefinedType">The predefined type syntax.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessPredefinedType(SemanticModel semantic, LayerContext context, PredefinedTypeSyntax predefinedType, List<string> lines) {
            var type = predefinedType.Keyword.ValueText;
            TypeScriptProgram tsProgram = (TypeScriptProgram)context.Program;

            string name;
            if (type == "void") {
                name = "void";
            } else {
                bool useBoxed = predefinedType.Parent is MemberAccessExpressionSyntax;
                name = useBoxed
                    ? TypeScriptTypeMap.GetTypeScriptBoxedTypeName(type)
                    : TypeScriptTypeMap.GetTypeScriptTypeName(type);
            }

            lines.Add(name);
            context.AddClass(tsProgram.GetClassByName(name));
        }

        /// <summary>
        /// Processes return statements, handling async return shaping.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="ret">The return statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessReturnStatement(SemanticModel semantic, LayerContext context, ReturnStatementSyntax ret, List<string> lines) {
            if (ret.Expression == null) {
                lines.Add("return;");
            } else {
                int start = context.Class.Count;

                List<string> retLines = new List<string>();
                ExpressionResult res = ProcessExpression(semantic, context, ret.Expression, retLines);

                if (res.Type != null &&
                    res.Type.TypeName.StartsWith("Promise<")) {
                    //lines.Add("await ");
                    FunctionStack currentFn = context.GetCurrentFunction();
                    if (currentFn != null && !currentFn.Function.IsAsync) {
                        currentFn.Function.IsAsync = true;
                    }
                }

                if (res.BeforeLines != null) {
                    lines.AddRange(res.BeforeLines);
                }

                if (res.AfterLines == null || res.AfterLines.Count == 0) {
                    lines.Add("return ");
                    lines.AddRange(retLines);
                } else {
                    lines.Add("var ___result = ");
                    lines.AddRange(retLines);
                    lines.Add(";\n");

                    lines.AddRange(res.AfterLines);
                    lines.Add("return ___result");
                }

                var fn = context.GetCurrentFunction().Function;

                if (fn.ReturnType != null &&
                    fn.ReturnType.GenericArgs != null &&
                    fn.ReturnType.GenericArgs.Count == 1) {
                    if (lines[1] == "new Array(" &&
                        lines[2] == "0" &&
                        lines[3] == ")" &&
                        fn.ReturnType.GenericArgs[0].Type == VariableDataType.UInt8) {
                        lines.RemoveRange(1, 3);
                        lines.Add("new Uint8Array()");
                    }
                }

                lines.Add(";");
                context.PopClass(start);
            }
        }

        /// <summary>
        /// Processes default(T) expressions into TypeScript literals.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="defaultExpression">The default expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessDefaultExpression(SemanticModel semantic, LayerContext context, DefaultExpressionSyntax defaultExpression, List<string> lines) {
            var type = defaultExpression.Type.ToString();

            // Add the default value based on the type
            if (type == "int" || type == "float" || type == "double" || type == "decimal" || type == "long" || type == "short" || type == "byte") {
                lines.Add("0");
            } else if (type == "bool") {
                lines.Add("false");
            } else if (type == "char") {
                lines.Add("'\\0'");
            } else {
                lines.Add("null"); // Default to null for reference types or unknown types
            }
        }

        /// <summary>
        /// Processes interpolated strings into template literals.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="interpolatedString">The interpolated string expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the string.</returns>
        protected override ExpressionResult ProcessInterpolatedStringExpression(SemanticModel semantic, LayerContext context, InterpolatedStringExpressionSyntax interpolatedString, List<string> lines) {
            // Add the backtick to start the template literal
            lines.Add("`");

            // Process each content part inside the interpolated string
            foreach (var content in interpolatedString.Contents) {
                if (content is InterpolationSyntax interpolation) {
                    // For interpolated expressions, wrap them in ${}
                    lines.Add("${");

                    int startClass = context.DepthClass;
                    ProcessExpression(semantic, context, interpolation.Expression, lines);
                    context.PopClass(startClass);

                    lines.Add("}");
                } else if (content is InterpolatedStringTextSyntax text) {
                    // Regular string content
                    lines.Add(text.TextToken.Text);
                }
            }

            // Add the backtick to close the template literal
            lines.Add("`");

            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
        }

        bool TryProcessEnumToString(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess) {
                return false;
            }

            if (invocationExpression.ArgumentList?.Arguments.Count > 0) {
                return false;
            }

            if (memberAccess.Name is not IdentifierNameSyntax memberName) {
                return false;
            }

            if (memberName.Identifier.Text == "ToLowerInvariant") {
                if (memberAccess.Expression is not InvocationExpressionSyntax innerInvocation) {
                    return false;
                }

                if (innerInvocation.ArgumentList?.Arguments.Count > 0) {
                    return false;
                }

                if (innerInvocation.Expression is not MemberAccessExpressionSyntax innerMemberAccess) {
                    return false;
                }

                if (innerMemberAccess.Name is not IdentifierNameSyntax innerName ||
                    innerName.Identifier.Text != "ToString") {
                    return false;
                }

                INamedTypeSymbol enumType = ResolveEnumType(semantic.GetTypeInfo(innerMemberAccess.Expression).Type);
                if (enumType == null) {
                    return false;
                }

                int depth = context.DepthClass;
                List<string> targetLines = new List<string>();
                ProcessExpression(semantic, context, innerMemberAccess.Expression, targetLines);
                context.PopClass(depth);

                lines.Add("NativeStringUtil.toCamelCase(");
                lines.Add(enumType.Name);
                lines.Add("[");
                lines.AddRange(targetLines);
                lines.Add("])");

                result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
                return true;
            }

            if (memberName.Identifier.Text == "ToString") {
                INamedTypeSymbol enumType = ResolveEnumType(semantic.GetTypeInfo(memberAccess.Expression).Type);
                if (enumType == null) {
                    return false;
                }

                int depth = context.DepthClass;
                List<string> targetLines = new List<string>();
                ProcessExpression(semantic, context, memberAccess.Expression, targetLines);
                context.PopClass(depth);

                lines.Add(enumType.Name);
                lines.Add("[");
                lines.AddRange(targetLines);
                lines.Add("]");

                result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handles primitive ToString calls by emitting the JavaScript toString without format arguments.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="invocationExpression">The invocation expression to inspect.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="result">The expression result when handled.</param>
        /// <returns>True when a primitive ToString was emitted.</returns>
        bool TryProcessPrimitiveToString(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess) {
                return false;
            }

            if (memberAccess.Name is not IdentifierNameSyntax memberName ||
                memberName.Identifier.Text != "ToString") {
                return false;
            }

            ITypeSymbol targetType = semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (!IsPrimitiveToStringTarget(targetType)) {
                return false;
            }

            int depth = context.DepthClass;
            List<string> targetLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, targetLines);
            context.PopClass(depth);

            lines.AddRange(targetLines);
            lines.Add(".toString()");

            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
            return true;
        }

        /// <summary>Supplies the erased C# value-type default to dictionary extension calls while retaining argument evaluation order.</summary>
        /// <param name="semantic">Semantic model identifying the framework extension and its constructed value type.</param>
        /// <param name="context">Conversion scope used to emit the receiver and arguments.</param>
        /// <param name="invocation">Framework dictionary lookup being considered.</param>
        /// <param name="lines">Destination for the runtime method call.</param>
        /// <param name="result">Resolved value type when this invocation is converted.</param>
        /// <returns>True when the framework dictionary extension was emitted.</returns>
        bool TryProcessDictionaryDefaultLookup(SemanticModel semantic, LayerContext context, InvocationExpressionSyntax invocation, List<string> lines, out ExpressionResult result) {
            result = new ExpressionResult(false);
            IMethodSymbol method = semantic.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (method == null || method.Name != "GetValueOrDefault" ||
                method.ContainingType.ToDisplayString() != "System.Collections.Generic.CollectionExtensions" ||
                invocation.Expression is not MemberAccessExpressionSyntax access) {
                return false;
            }
            bool reduced = method.ReducedFrom != null;
            var arguments = invocation.ArgumentList.Arguments;
            int firstArgument = reduced ? 0 : 1;
            int explicitCount = arguments.Count - firstArgument;
            if (explicitCount < 1 || explicitCount > 2 || arguments.Any(argument => argument.NameColon != null)) {
                throw new NotSupportedException("Dictionary default lookup requires positional key and optional default arguments.");
            }
            string implicitDefault = explicitCount == 1 ? GetDictionaryValueDefault(method.ReturnType) : null;
            int depth = context.DepthClass;
            ExpressionSyntax receiver = reduced ? access.Expression : arguments[0].Expression;
            ProcessExpression(semantic, context, receiver, lines);
            context.PopClass(depth);
            lines.Add(".GetValueOrDefault(");
            for (int index = firstArgument; index < arguments.Count; index++) {
                if (index > firstArgument) { lines.Add(", "); }
                ProcessExpression(semantic, context, arguments[index].Expression, lines);
                context.PopClass(depth);
            }
            if (explicitCount == 1) {
                lines.Add(", ");
                lines.Add(implicitDefault);
            }
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }

        /// <summary>Resolves defaults before generic erasure; unsupported value types must not silently become null.</summary>
        /// <param name="type">Constructed dictionary value type.</param>
        /// <returns>The TypeScript literal matching the C# zero-initialized value.</returns>
        static string GetDictionaryValueDefault(ITypeSymbol type) {
            if (type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) {
                return "null";
            } else if (type.SpecialType == SpecialType.System_Boolean) {
                return "false";
            } else if (type.SpecialType == SpecialType.System_Char) {
                return "'\\0'";
            } else if (IsNumericSpecialType(type.SpecialType) || type.TypeKind == TypeKind.Enum) {
                return "0";
            }
            throw new NotSupportedException($"Dictionary lookup default for '{type}' requires an explicit supported default value.");
        }

        /// <summary>
        /// Emits typed numeric CompareTo calls through the runtime, evaluating receiver and argument once in source order.
        /// Object overloads and user-defined comparisons retain their normal invocation handling.
        /// </summary>
        /// <param name="semantic">Semantic model used to identify the selected numeric overload.</param>
        /// <param name="context">Conversion context that records the required numeric runtime import.</param>
        /// <param name="invocationExpression">Invocation whose resolved method is inspected.</param>
        /// <param name="lines">Destination for the generated comparison expression.</param>
        /// <param name="result">Integer expression result when this overload is supported.</param>
        /// <returns>True only for a primitive numeric instance comparison with its matching typed parameter.</returns>
        bool TryProcessNumericCompareTo(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name.Identifier.Text != "CompareTo") {
                return false;
            }

            IMethodSymbol method = semantic.GetSymbolInfo(invocationExpression).Symbol as IMethodSymbol;
            if (method == null || method.IsStatic || method.Name != "CompareTo" ||
                !IsNumericSpecialType(method.ContainingType.SpecialType) ||
                method.Parameters.Length != 1 ||
                !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, method.ContainingType) ||
                invocationExpression.ArgumentList.Arguments.Count != 1) {
                return false;
            }

            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);
            List<string> argumentLines = new List<string>();
            ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0].Expression, argumentLines);
            context.PopClass(depth);

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeNumberUtil"));
            lines.Add("NativeNumberUtil.compareTo(");
            lines.AddRange(receiverLines);
            lines.Add(", ");
            lines.AddRange(argumentLines);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("int"));
            return true;
        }

        /// <summary>Lowers comparer-aware GroupBy and ToDictionary through iterable runtime helpers.</summary>
        bool TryProcessEnumerableGrouping(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocationExpression.ArgumentList.Arguments.Any(argument => argument.NameColon != null)) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (method?.ContainingType?.ToDisplayString() != "System.Linq.Enumerable") {
                return false;
            }

            bool isGroupBy = method.Name == "GroupBy";
            bool isToDictionary = method.Name == "ToDictionary";
            if (!isGroupBy && !isToDictionary) {
                return false;
            }

            var arguments = invocationExpression.ArgumentList.Arguments;


            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);
            var argumentLines = new List<List<string>>();
            foreach (ArgumentSyntax argument in arguments) {
                var converted = new List<string>();
                ProcessExpression(semantic, context, argument.Expression, converted);
                context.PopClass(depth);
                argumentLines.Add(converted);
            }

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeArrayUtil"));
            if (isGroupBy) {
                if (arguments.Count < 1 || arguments.Count > 2 ||
                    (arguments.Count == 2 && !IsEqualityComparerType(semantic.GetTypeInfo(arguments[1].Expression).Type))) {
                    return false;
                }
                lines.Add("NativeArrayUtil.groupBy(");
                lines.AddRange(receiverLines);
                lines.Add(", ");
                lines.AddRange(argumentLines[0]);
                lines.Add(", ");
                if (argumentLines.Count == 2) {
                    lines.AddRange(argumentLines[1]);
                } else {
                    lines.Add("null");
                }
                lines.Add(")");
            } else {
                if (arguments.Count < 1 || arguments.Count > 3) {
                    return false;
                }
                bool hasComparer = arguments.Count > 1 &&
                    IsEqualityComparerType(semantic.GetTypeInfo(arguments[^1].Expression).Type);
                int selectorCount = arguments.Count - (hasComparer ? 1 : 0);
                if (selectorCount < 1 || selectorCount > 2) {
                    return false;
                }
                lines.Add("NativeArrayUtil.toDictionary(");
                lines.AddRange(receiverLines);
                lines.Add(", ");
                lines.AddRange(argumentLines[0]);
                lines.Add(", ");
                if (selectorCount == 2) {
                    lines.AddRange(argumentLines[1]);
                } else {
                    lines.Add("null");
                }
                lines.Add(", ");
                if (hasComparer) {
                    lines.AddRange(argumentLines[selectorCount]);
                } else {
                    lines.Add("null");
                }
                lines.Add(")");
            }

            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }

        static bool IsEqualityComparerType(ITypeSymbol typeSymbol) {
            if (typeSymbol == null) {
                return false;
            }

            bool IsEqualityComparerInterface(ITypeSymbol candidate) =>
                candidate?.OriginalDefinition?.ToDisplayString() == "System.Collections.Generic.IEqualityComparer<T>";

            return IsEqualityComparerInterface(typeSymbol) ||
                typeSymbol.AllInterfaces.Any(IsEqualityComparerInterface);
        }
        /// <summary>Lowers common reduced Enumerable operators through iterable runtime helpers.</summary>
        bool TryProcessEnumerableCoreOperator(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            MemberAccessExpressionSyntax memberAccess = invocationExpression.Expression as MemberAccessExpressionSyntax;
            bool isConditionalMemberBinding = invocationExpression.Expression is MemberBindingExpressionSyntax;
            if (memberAccess == null && !isConditionalMemberBinding) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (method?.ReducedFrom == null || method.ContainingType?.ToDisplayString() != "System.Linq.Enumerable" ||
                invocationExpression.ArgumentList.Arguments.Any(argument => argument.NameColon != null)) {
                return false;
            }

            string helperName = null;
            int minimumArgumentCount = -1;
            int maximumArgumentCount = -1;
            switch (method.Name) {
                case "All":
                    helperName = "all";
                    minimumArgumentCount = maximumArgumentCount = 1;
                    break;
                case "Skip":
                    helperName = "skip";
                    minimumArgumentCount = maximumArgumentCount = 1;
                    break;
                case "Sum":
                    helperName = "sum";
                    minimumArgumentCount = maximumArgumentCount = 1;
                    break;
                case "OrderBy":
                    helperName = "orderBy";
                    minimumArgumentCount = 1;
                    maximumArgumentCount = 2;
                    break;
                case "ToArray":
                    helperName = "toArray";
                    minimumArgumentCount = maximumArgumentCount = 0;
                    break;
                case "Distinct":
                    helperName = "distinct";
                    minimumArgumentCount = 0;
                    maximumArgumentCount = 1;
                    break;
                case "ToList":
                    helperName = "toList";
                    minimumArgumentCount = maximumArgumentCount = 0;
                    break;
                case "Contains":
                    helperName = "contains";
                    minimumArgumentCount = 1;
                    maximumArgumentCount = 2;
                    break;
                case "Take":
                    helperName = "take";
                    minimumArgumentCount = maximumArgumentCount = 1;
                    if (method.Parameters.Length != 1 || method.Parameters[0].Type.SpecialType != SpecialType.System_Int32) {
                        return false;
                    }
                    break;
                case "Where":
                    helperName = "where";
                    minimumArgumentCount = maximumArgumentCount = 1;
                    break;
                case "Select":
                    helperName = "select";
                    minimumArgumentCount = maximumArgumentCount = 1;
                    break;
                case "Count":
                    helperName = "count";
                    minimumArgumentCount = 0;
                    maximumArgumentCount = 1;
                    break;
                default:
                    return false;
            }

            int argumentCount = invocationExpression.ArgumentList.Arguments.Count;
            if (argumentCount < minimumArgumentCount || argumentCount > maximumArgumentCount) {
                return false;
            }

            // String LINQ operations and byte-array ToArray have dedicated lowerings which preserve
            // their CLR-specific representations. This generic iterable path is intentionally broader.
            ITypeSymbol receiverType = memberAccess != null
                ? semantic.GetTypeInfo(memberAccess.Expression).Type
                : method.ReceiverType;
            if ((method.Name == "All" && receiverType?.SpecialType == SpecialType.System_String) ||
                (method.Name == "ToArray" && receiverType is IArrayTypeSymbol byteArray &&
                 byteArray.Rank == 1 && byteArray.ElementType.SpecialType == SpecialType.System_Byte)) {
                return false;
            }

            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            if (memberAccess != null) {
                ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
                context.PopClass(depth);
            } else {
                Stack<string> conditionalReceivers = ConditionalAccessReceivers.Value;
                if (conditionalReceivers == null || conditionalReceivers.Count == 0) {
                    return false;
                }
                receiverLines.Add(conditionalReceivers.Pop());
            }

            List<List<string>> argumentLines = new List<List<string>>();
            foreach (ArgumentSyntax argument in invocationExpression.ArgumentList.Arguments) {
                List<string> convertedArgument = new List<string>();
                ProcessExpression(semantic, context, argument.Expression, convertedArgument);
                context.PopClass(depth);
                argumentLines.Add(convertedArgument);
            }

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeArrayUtil"));
            lines.Add("NativeArrayUtil.");
            lines.Add(helperName);
            lines.Add("(");
            lines.AddRange(receiverLines);
            foreach (List<string> convertedArgument in argumentLines) {
                lines.Add(", ");
                lines.AddRange(convertedArgument);
            }
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }
        /// <summary>Lowers reduced Enumerable.Single for iterable sequences without extending Array prototypes.</summary>
        bool TryProcessEnumerableSingle(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocationExpression.ArgumentList.Arguments.Count > 1) {
                return false;
            }
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (method?.Name != "Single" || method.ReducedFrom == null ||
                method.ContainingType?.ToDisplayString() != "System.Linq.Enumerable") {
                return false;
            }
            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);
            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeArrayUtil"));
            lines.Add("NativeArrayUtil.single(");
            lines.AddRange(receiverLines);
            if (invocationExpression.ArgumentList.Arguments.Count == 1) {
                lines.Add(", ");
                List<string> predicateLines = new List<string>();
                ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0].Expression, predicateLines);
                context.PopClass(depth);
                lines.AddRange(predicateLines);
            }
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }

        /// <summary>
        /// Preserves the selected C# primitive parser rather than collapsing every numeric TryParse call
        /// onto JavaScript's untyped Number constructor.
        /// </summary>
        bool TryProcessNumericTryParse(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (method == null || !method.IsStatic || method.Name != "TryParse" ||
                invocationExpression.ArgumentList.Arguments.Any(argument => argument.NameColon != null)) {
                return false;
            }

            SpecialType numericType = method.ContainingType?.SpecialType ?? SpecialType.None;
            bool isInteger = TryGetNumericTryParseBounds(numericType, out string minimum, out string maximum);
            bool isFloatingPoint = numericType == SpecialType.System_Single ||
                numericType == SpecialType.System_Double || numericType == SpecialType.System_Decimal;
            if ((!isInteger && !isFloatingPoint) ||
                (method.Parameters.Length != 2 && method.Parameters.Length != 4) ||
                invocationExpression.ArgumentList.Arguments.Count != method.Parameters.Length ||
                method.Parameters[method.Parameters.Length - 1].RefKind != RefKind.Out) {
                return false;
            }

            int depth = context.DepthClass;
            List<List<string>> argumentLines = new List<List<string>>();
            List<string> beforeLines = new List<string>();
            List<string> afterLines = new List<string>();
            for (int argumentIndex = 0; argumentIndex < invocationExpression.ArgumentList.Arguments.Count; argumentIndex++) {
                ArgumentSyntax argument = invocationExpression.ArgumentList.Arguments[argumentIndex];
                List<string> currentArgumentLines = new List<string>();
                ExpressionResult argumentResult = ProcessExpression(semantic, context, argument.Expression, currentArgumentLines);
                context.PopClass(depth);

                if (argumentIndex == invocationExpression.ArgumentList.Arguments.Count - 1) {
                    bool isOutDeclaration = argument.Expression is DeclarationExpressionSyntax;
                    string temporaryOut = TemporaryNames.Allocate(semantic, "out_");
                    beforeLines.Add($"let {temporaryOut} = {{ value: undefined }};\n");

                    string outName = string.Concat(currentArgumentLines);
                    if (!isOutDeclaration && argumentResult.Variable != null && argumentResult.Variable.Modifier.HasFlag(ParameterModifier.Out)) {
                        outName = argumentResult.Variable.Name;
                    }
                    if (!string.Equals(outName, "_", StringComparison.Ordinal)) {
                        afterLines.Add(isOutDeclaration
                            ? $"let {outName} = {temporaryOut}.value;\n"
                            : $"{outName} = {temporaryOut}.value;\n");
                    }
                    currentArgumentLines.Clear();
                    currentArgumentLines.Add(temporaryOut);
                }
                argumentLines.Add(currentArgumentLines);
            }

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeNumberUtil"));
            bool hasFormatArguments = method.Parameters.Length == 4;
            lines.Add(isInteger
                ? hasFormatArguments ? "NativeNumberUtil.tryParseIntegerWithFormat(" : "NativeNumberUtil.tryParseInteger("
                : hasFormatArguments ? "NativeNumberUtil.tryParseFloatingPointWithFormat(" : "NativeNumberUtil.tryParseFloatingPoint(");
            for (int argumentIndex = 0; argumentIndex < argumentLines.Count; argumentIndex++) {
                if (argumentIndex > 0) {
                    lines.Add(", ");
                }
                lines.AddRange(argumentLines[argumentIndex]);
            }
            if (isInteger) {
                lines.Add(", ");
                lines.Add(minimum);
                lines.Add(", ");
                lines.Add(maximum);
            }
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
            result.BeforeLines = beforeLines;
            result.AfterLines = afterLines;
            return true;
        }

        /// <summary>Returns exact representable bounds for integral C# primitive parser overloads.</summary>
        static bool TryGetNumericTryParseBounds(SpecialType specialType, out string minimum, out string maximum) {
            minimum = null;
            maximum = null;
            switch (specialType) {
                case SpecialType.System_SByte: minimum = "-128"; maximum = "127"; return true;
                case SpecialType.System_Byte: minimum = "0"; maximum = "255"; return true;
                case SpecialType.System_Int16: minimum = "-32768"; maximum = "32767"; return true;
                case SpecialType.System_UInt16: minimum = "0"; maximum = "65535"; return true;
                case SpecialType.System_Int32: minimum = "-2147483648"; maximum = "2147483647"; return true;
                case SpecialType.System_UInt32: minimum = "0"; maximum = "4294967295"; return true;
                // JavaScript cannot faithfully represent every Int64/UInt64 value. Rejecting values
                // beyond the safe-integer range is preferable to accepting a rounded authorization value.
                case SpecialType.System_Int64: minimum = "Number.MIN_SAFE_INTEGER"; maximum = "Number.MAX_SAFE_INTEGER"; return true;
                case SpecialType.System_UInt64: minimum = "0"; maximum = "Number.MAX_SAFE_INTEGER"; return true;
                default: return false;
            }
        }

        /// <summary>
        /// Emits framework numeric clamping through the imported runtime helper because JavaScript Math has no Clamp member.
        /// </summary>
        /// <param name="semantic">Semantic model used to distinguish System.Math from user-defined methods.</param>
        /// <param name="context">Conversion scope that records the required numeric runtime import.</param>
        /// <param name="invocationExpression">Invocation whose selected framework overload is inspected.</param>
        /// <param name="lines">Destination for the generated clamping expression.</param>
        /// <param name="result">Converted numeric result when the framework overload is supported.</param>
        /// <returns>True only for a supported three-argument System.Math.Clamp overload.</returns>
        bool TryProcessMathClamp(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (method == null || !method.IsStatic || method.Name != "Clamp" ||
                method.ContainingType?.ToDisplayString() != "System.Math" ||
                method.Parameters.Length != 3 || invocationExpression.ArgumentList.Arguments.Count != 3 ||
                method.Parameters.Any(parameter => !IsNumericSpecialType(parameter.Type.SpecialType))) {
                return false;
            }
            if (invocationExpression.ArgumentList.Arguments.Any(argument => argument.NameColon != null)) {
                throw new NotSupportedException("System.Math.Clamp named arguments require an evaluation-order-preserving lowering.");
            }

            int depth = context.DepthClass;
            List<List<string>> argumentLines = new List<List<string>>();
            foreach (ArgumentSyntax argument in invocationExpression.ArgumentList.Arguments) {
                List<string> currentArgumentLines = new List<string>();
                ProcessExpression(semantic, context, argument.Expression, currentArgumentLines);
                context.PopClass(depth);
                argumentLines.Add(currentArgumentLines);
            }

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeNumberUtil"));
            lines.Add("NativeNumberUtil.clamp(");
            for (int index = 0; index < argumentLines.Count; index++) {
                if (index > 0) { lines.Add(", "); }
                lines.AddRange(argumentLines[index]);
            }
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }

        /// <summary>
        /// Lowers Enumerable.ToArray over byte[] to a detached typed-array copy because Uint8Array has no C#-style ToArray member.
        /// </summary>
        /// <param name="semantic">Semantic model used to distinguish LINQ from user-defined methods.</param>
        /// <param name="context">Conversion scope for processing the byte-array receiver.</param>
        /// <param name="invocationExpression">Selected zero-argument ToArray invocation.</param>
        /// <param name="lines">Destination for the generated typed-array copy expression.</param>
        /// <param name="result">Converted byte-array result.</param>
        /// <returns>True only for System.Linq.Enumerable.ToArray over a one-dimensional byte array.</returns>
        bool TryProcessByteArrayToArray(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocationExpression.ArgumentList.Arguments.Count != 0) {
                return false;
            }

            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            bool isEnumerableToArray = method?.Name == "ToArray" &&
                (method.ContainingType?.ToDisplayString() == "System.Linq.Enumerable" ||
                method.ReducedFrom?.ContainingType?.ToDisplayString() == "System.Linq.Enumerable");
            ITypeSymbol receiverType = semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (!isEnumerableToArray || receiverType is not IArrayTypeSymbol byteArray ||
                byteArray.Rank != 1 || byteArray.ElementType.SpecialType != SpecialType.System_Byte ||
                method.ReturnType is not IArrayTypeSymbol returnArray ||
                returnArray.Rank != 1 || returnArray.ElementType.SpecialType != SpecialType.System_Byte) {
                return false;
            }

            int depth = context.DepthClass;
            List<string> receiverLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, receiverLines);
            context.PopClass(depth);

            lines.Add("Uint8Array.from(");
            lines.AddRange(receiverLines);
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }

        /// <summary>
        /// Lowers the framework whitespace guard to a runtime that imports both exception classes without a module-order dependency.
        /// </summary>
        /// <param name="semantic">Semantic model used to distinguish System.ArgumentException from user-defined methods.</param>
        /// <param name="context">Conversion scope that records the dedicated guard runtime import.</param>
        /// <param name="invocationExpression">Invocation whose selected framework method is inspected.</param>
        /// <param name="lines">Destination for the generated guard call.</param>
        /// <param name="result">Void result when the framework guard is converted.</param>
        /// <returns>True only for the supported positional framework whitespace guard overload.</returns>
        bool TryProcessArgumentExceptionGuard(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (method == null || !method.IsStatic || method.Name != "ThrowIfNullOrWhiteSpace" ||
                method.ContainingType?.ToDisplayString() != "System.ArgumentException" ||
                method.Parameters.Length != 2 || invocationExpression.ArgumentList.Arguments.Count < 1 ||
                invocationExpression.ArgumentList.Arguments.Count > 2) {
                return false;
            }
            if (invocationExpression.ArgumentList.Arguments.Any(argument => argument.NameColon != null)) {
                throw new NotSupportedException("ArgumentException.ThrowIfNullOrWhiteSpace named arguments require an evaluation-order-preserving lowering.");
            }

            int depth = context.DepthClass;
            List<List<string>> argumentLines = new List<List<string>>();
            foreach (ArgumentSyntax argument in invocationExpression.ArgumentList.Arguments) {
                List<string> currentArgumentLines = new List<string>();
                ProcessExpression(semantic, context, argument.Expression, currentArgumentLines);
                context.PopClass(depth);
                argumentLines.Add(currentArgumentLines);
            }

            context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("ArgumentGuard"));
            lines.Add("ArgumentGuard.throwIfNullOrWhiteSpace(");
            for (int index = 0; index < argumentLines.Count; index++) {
                if (index > 0) { lines.Add(", "); }
                lines.AddRange(argumentLines[index]);
            }
            lines.Add(")");
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(method.ReturnType));
            return true;
        }

        /// <summary>
        /// Maps reference-only System.Object.ReferenceEquals calls to JavaScript identity comparison.
        /// </summary>
        /// <remarks>
        /// Value-type boxing is not representable in the browser runtime, so calls with operands
        /// that are not statically known reference types remain unsupported rather than silently
        /// changing .NET equality semantics.
        /// </remarks>
        bool TryProcessReferenceEquals(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            bool isReferenceEquals = invocationExpression.Expression switch {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == "ReferenceEquals",
                IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText == "ReferenceEquals",
                _ => false
            };
            if (!isReferenceEquals ||
                invocationExpression.ArgumentList.Arguments.Count != 2 ||
                invocationExpression.ArgumentList.Arguments.Any(argument => argument.NameColon != null)) {
                return false;
            }

            IMethodSymbol methodSymbol = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (methodSymbol == null || !methodSymbol.IsStatic ||
                methodSymbol.ContainingType?.SpecialType != SpecialType.System_Object ||
                !IsGuaranteedReferenceType(semantic.GetTypeInfo(invocationExpression.ArgumentList.Arguments[0].Expression).Type) ||
                !IsGuaranteedReferenceType(semantic.GetTypeInfo(invocationExpression.ArgumentList.Arguments[1].Expression).Type)) {
                return false;
            }

            int leftDepth = context.DepthClass;
            List<string> leftLines = new List<string>();
            ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[0].Expression, leftLines);
            context.PopClass(leftDepth);

            int rightDepth = context.DepthClass;
            List<string> rightLines = new List<string>();
            ProcessExpression(semantic, context, invocationExpression.ArgumentList.Arguments[1].Expression, rightLines);
            context.PopClass(rightDepth);

            lines.AddRange(leftLines);
            lines.Add(" === ");
            lines.AddRange(rightLines);
            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
            return true;
        }

        /// <summary>
        /// Determines whether the source type has reference identity without value-type boxing.
        /// </summary>
        static bool IsGuaranteedReferenceType(ITypeSymbol typeSymbol) {
            if (typeSymbol == null) {
                return false;
            }

            if (typeSymbol.TypeKind == TypeKind.Class || typeSymbol.TypeKind == TypeKind.Interface ||
                typeSymbol.TypeKind == TypeKind.Array || typeSymbol.TypeKind == TypeKind.Delegate) {
                return typeSymbol.SpecialType != SpecialType.System_Object;
            }

            return typeSymbol is ITypeParameterSymbol typeParameter && typeParameter.HasReferenceTypeConstraint;
        }

        /// <summary>
        /// Maps instance GetType() calls to the Type.of runtime helper.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="invocationExpression">The invocation expression to inspect.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="result">The expression result when handled.</param>
        /// <returns>True when the invocation was rewritten.</returns>
        bool TryProcessObjectGetType(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess) {
                return false;
            }

            if (memberAccess.Name is not IdentifierNameSyntax memberName ||
                memberName.Identifier.Text != "GetType") {
                return false;
            }

            if (invocationExpression.ArgumentList?.Arguments.Count > 0) {
                return false;
            }

            IMethodSymbol methodSymbol = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (methodSymbol == null ||
                methodSymbol.Parameters.Length != 0 ||
                methodSymbol.ContainingType == null ||
                methodSymbol.ContainingType.SpecialType != SpecialType.System_Object) {
                return false;
            }

            int depth = context.DepthClass;
            List<string> targetLines = new List<string>();
            ProcessExpression(semantic, context, memberAccess.Expression, targetLines);
            context.PopClass(depth);

            lines.Add("Type.of(");
            lines.AddRange(targetLines);
            lines.Add(")");

            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("Type"));
            return true;
        }

        /// <summary>
        /// Determines whether a type symbol should map ToString to the JavaScript primitive implementation.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to inspect.</param>
        /// <returns>True when the symbol is a primitive that should use toString().</returns>
        static bool IsPrimitiveToStringTarget(ITypeSymbol typeSymbol) {
            if (typeSymbol == null) {
                return false;
            }

            if (ResolveEnumType(typeSymbol) != null) {
                return false;
            }

            if (typeSymbol.SpecialType == SpecialType.System_String ||
                typeSymbol.SpecialType == SpecialType.System_Boolean ||
                typeSymbol.SpecialType == SpecialType.System_Char) {
                return true;
            }

            return IsNumericSpecialType(typeSymbol.SpecialType);
        }

        /// <summary>
        /// Determines whether a special type represents a numeric primitive.
        /// </summary>
        /// <param name="specialType">The special type to inspect.</param>
        /// <returns>True when the type is numeric.</returns>
        static bool IsNumericSpecialType(SpecialType specialType) {
            return specialType == SpecialType.System_SByte ||
                specialType == SpecialType.System_Byte ||
                specialType == SpecialType.System_Int16 ||
                specialType == SpecialType.System_UInt16 ||
                specialType == SpecialType.System_Int32 ||
                specialType == SpecialType.System_UInt32 ||
                specialType == SpecialType.System_Int64 ||
                specialType == SpecialType.System_UInt64 ||
                specialType == SpecialType.System_Single ||
                specialType == SpecialType.System_Double ||
                specialType == SpecialType.System_Decimal;
        }

        /// <summary>
        /// Processes String.Compare invocations that specify StringComparison.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="invocationExpression">The invocation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="result">The expression result describing the comparison.</param>
        /// <returns>True when the invocation is handled.</returns>
        bool TryProcessStringCompare(
            SemanticModel semantic,
            LayerContext context,
            InvocationExpressionSyntax invocationExpression,
            List<string> lines,
            out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess) {
                return false;
            }

            if (memberAccess.Name is not IdentifierNameSyntax memberName ||
                memberName.Identifier.Text != "Compare") {
                return false;
            }

            IMethodSymbol methodSymbol = semantic.GetSymbolInfo(invocationExpression.Expression).Symbol as IMethodSymbol;
            if (methodSymbol == null && semantic.GetSymbolInfo(invocationExpression.Expression).CandidateSymbols.Length > 0) {
                methodSymbol = semantic.GetSymbolInfo(invocationExpression.Expression).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            }

            if (methodSymbol == null ||
                !methodSymbol.IsStatic ||
                methodSymbol.ContainingType == null ||
                methodSymbol.ContainingType.SpecialType != SpecialType.System_String) {
                return false;
            }

            SeparatedSyntaxList<ArgumentSyntax> arguments = invocationExpression.ArgumentList.Arguments;
            if (arguments.Count < 2) {
                throw new NotSupportedException("String.Compare requires at least two arguments.");
            }

            if (arguments.Count != 3) {
                throw new NotSupportedException("String.Compare overload without StringComparison is not supported.");
            }

            if (!TryGetStringComparisonName(semantic, arguments[2].Expression, out string comparisonName)) {
                throw new NotSupportedException("String.Compare requires a StringComparison argument.");
            }

            string comparerName = comparisonName switch {
                "Ordinal" => "StringComparer.Ordinal",
                "OrdinalIgnoreCase" => "StringComparer.OrdinalIgnoreCase",
                _ => null
            };

            if (comparerName == null) {
                throw new NotSupportedException($"String.Compare with StringComparison.{comparisonName} is not supported.");
            }

            List<string> leftLines = new List<string>();
            int startLeft = context.DepthClass;
            ProcessExpression(semantic, context, arguments[0].Expression, leftLines);
            context.PopClass(startLeft);

            List<string> rightLines = new List<string>();
            int startRight = context.DepthClass;
            ProcessExpression(semantic, context, arguments[1].Expression, rightLines);
            context.PopClass(startRight);

            TypeScriptProgram tsProgram = (TypeScriptProgram)context.Program;
            ConversionClass comparerClass = tsProgram.GetClassByName("StringComparer");
            context.AddClass(comparerClass);

            lines.Add(comparerName);
            lines.Add(".Compare(");
            lines.AddRange(leftLines);
            lines.Add(", ");
            lines.AddRange(rightLines);
            lines.Add(")");

            result = new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("int"));
            return true;
        }

        /// <summary>
        /// Attempts to extract the StringComparison enum member name.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="expression">The expression supplying the comparison.</param>
        /// <param name="comparisonName">The resolved enum member name.</param>
        /// <returns>True when the comparison name is resolved.</returns>
        static bool TryGetStringComparisonName(SemanticModel semantic, ExpressionSyntax expression, out string comparisonName) {
            comparisonName = null;

            if (expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is IdentifierNameSyntax memberName) {
                comparisonName = memberName.Identifier.Text;
                return true;
            }

            ISymbol symbol = semantic.GetSymbolInfo(expression).Symbol;
            if (symbol is IFieldSymbol fieldSymbol &&
                fieldSymbol.ContainingType != null &&
                fieldSymbol.ContainingType.Name == "StringComparison") {
                comparisonName = fieldSymbol.Name;
                return true;
            }

            return false;
        }

        static INamedTypeSymbol ResolveEnumType(ITypeSymbol typeSymbol) {
            if (typeSymbol == null) {
                return null;
            }

            if (typeSymbol is INamedTypeSymbol namedTypeSymbol) {
                if (namedTypeSymbol.TypeKind == TypeKind.Enum) {
                    return namedTypeSymbol;
                }

                if (namedTypeSymbol.IsGenericType &&
                    namedTypeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                    namedTypeSymbol.TypeArguments.Length == 1 &&
                    namedTypeSymbol.TypeArguments[0] is INamedTypeSymbol innerNamed &&
                    innerNamed.TypeKind == TypeKind.Enum) {
                    return innerNamed;
                }
            }

            return null;
        }

        /// <summary>
        /// Processes element access expressions, translating dictionary access when applicable.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="elementAccess">The element access expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessElementAccessExpression(SemanticModel semantic, LayerContext context, ElementAccessExpressionSyntax elementAccess, List<string> lines) {
            int startClass = context.DepthClass;
            List<string> targetLines = new List<string>();
            ProcessExpression(semantic, context, elementAccess.Expression, targetLines);
            List<ConversionClass> saved = context.SavePopClass(startClass);

            ITypeSymbol expressionType = semantic.GetTypeInfo(elementAccess.Expression).Type;
            if (IsDictionaryLike(expressionType)) {
                lines.AddRange(targetLines);
                lines.Add(".get(");

                for (int i = 0; i < elementAccess.ArgumentList.Arguments.Count; i++) {
                    var argument = elementAccess.ArgumentList.Arguments[i];
                    startClass = context.DepthClass;
                    List<string> keyLines = new List<string>();
                    ProcessExpression(semantic, context, argument.Expression, keyLines);
                    context.PopClass(startClass);

                    lines.AddRange(keyLines);
                    if (i != elementAccess.ArgumentList.Arguments.Count - 1) {
                        lines.Add(", ");
                    }
                }

                lines.Add(")");
                context.LoadClass(saved);
                return;
            }

            if (TryProcessRangeElementAccess(semantic, context, elementAccess, targetLines, lines)) {
                context.LoadClass(saved);
                return;
            }

            if (TryProcessIndexFromEndAccess(semantic, context, elementAccess, targetLines, lines)) {
                context.LoadClass(saved);
                return;
            }

            lines.AddRange(targetLines);
            lines.Add("[");

            foreach (var argument in elementAccess.ArgumentList.Arguments) {
                startClass = context.DepthClass;
                ProcessExpression(semantic, context, argument.Expression, lines);
                context.PopClass(startClass);
            }

            lines.Add("]");

            context.LoadClass(saved);
        }

        /// <summary>
        /// Processes range element access into a slice expression when applicable.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="elementAccess">The element access expression.</param>
        /// <param name="targetLines">The rendered target expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>True when a range access was handled.</returns>
        bool TryProcessRangeElementAccess(
            SemanticModel semantic,
            LayerContext context,
            ElementAccessExpressionSyntax elementAccess,
            List<string> targetLines,
            List<string> lines) {
            if (elementAccess.ArgumentList.Arguments.Count != 1) {
                return false;
            }

            if (elementAccess.ArgumentList.Arguments[0].Expression is not RangeExpressionSyntax rangeExpression) {
                return false;
            }

            bool usesFromEnd = RangeUsesFromEnd(rangeExpression);
            if (usesFromEnd) {
                lines.Add("(() => { const __rangeTarget = ");
                lines.AddRange(targetLines);
                lines.Add("; return __rangeTarget.slice(");
                AppendRangeBoundExpression(semantic, context, rangeExpression.LeftOperand, "__rangeTarget", targetLines, lines, isStart: true);

                if (rangeExpression.RightOperand != null) {
                    lines.Add(", ");
                    AppendRangeBoundExpression(semantic, context, rangeExpression.RightOperand, "__rangeTarget", targetLines, lines, isStart: false);
                }

                lines.Add("); })()");
            } else {
                lines.AddRange(targetLines);
                lines.Add(".slice(");
                AppendRangeBoundExpression(semantic, context, rangeExpression.LeftOperand, null, targetLines, lines, isStart: true);

                if (rangeExpression.RightOperand != null) {
                    lines.Add(", ");
                    AppendRangeBoundExpression(semantic, context, rangeExpression.RightOperand, null, targetLines, lines, isStart: false);
                }

                lines.Add(")");
            }

            return true;
        }

        /// <summary>
        /// Processes index-from-end element access when applicable.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="elementAccess">The element access expression.</param>
        /// <param name="targetLines">The rendered target expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>True when an index-from-end access was handled.</returns>
        bool TryProcessIndexFromEndAccess(
            SemanticModel semantic,
            LayerContext context,
            ElementAccessExpressionSyntax elementAccess,
            List<string> targetLines,
            List<string> lines) {
            if (elementAccess.ArgumentList.Arguments.Count != 1) {
                return false;
            }

            ExpressionSyntax indexExpression = elementAccess.ArgumentList.Arguments[0].Expression;
            if (!TryGetFromEndOperand(indexExpression, out ExpressionSyntax operand)) {
                return false;
            }

            lines.Add("(() => { const __indexTarget = ");
            lines.AddRange(targetLines);
            lines.Add("; return __indexTarget[__indexTarget.length - ");
            int startDepth = context.DepthClass;
            ProcessExpression(semantic, context, operand, lines);
            context.PopClass(startDepth);
            lines.Add("]; })()");
            return true;
        }

        /// <summary>
        /// Determines whether a range expression uses from-end bounds.
        /// </summary>
        /// <param name="rangeExpression">The range expression to inspect.</param>
        /// <returns>True when either bound is a from-end index.</returns>
        bool RangeUsesFromEnd(RangeExpressionSyntax rangeExpression) {
            if (rangeExpression == null) {
                return false;
            }

            return IsFromEndIndex(rangeExpression.LeftOperand) || IsFromEndIndex(rangeExpression.RightOperand);
        }

        /// <summary>
        /// Checks whether the provided expression represents a from-end index.
        /// </summary>
        /// <param name="expression">The bound expression to inspect.</param>
        /// <returns>True when the bound is a from-end index.</returns>
        bool IsFromEndIndex(ExpressionSyntax expression) {
            if (expression == null) {
                return false;
            }

            return TryGetFromEndOperand(expression, out _);
        }

        /// <summary>
        /// Attempts to extract the operand for a from-end index expression.
        /// </summary>
        /// <param name="expression">The expression to inspect.</param>
        /// <param name="operand">Outputs the operand expression when found.</param>
        /// <returns>True when the expression represents a from-end index.</returns>
        bool TryGetFromEndOperand(ExpressionSyntax expression, out ExpressionSyntax operand) {
            operand = null;

            if (expression is PrefixUnaryExpressionSyntax prefix &&
                prefix.OperatorToken.IsKind(SyntaxKind.CaretToken)) {
                operand = prefix.Operand;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Appends a range bound expression, handling omitted bounds and from-end indices.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="bound">The bound expression.</param>
        /// <param name="targetIdentifier">Optional target identifier to use for length expressions.</param>
        /// <param name="targetLines">Rendered target expression lines for inline length expressions.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="isStart">True when emitting the start bound.</param>
        void AppendRangeBoundExpression(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax bound,
            string targetIdentifier,
            List<string> targetLines,
            List<string> lines,
            bool isStart) {
            if (bound == null) {
                if (isStart) {
                    lines.Add("0");
                }
                return;
            }

            if (TryGetFromEndOperand(bound, out ExpressionSyntax operand)) {
                if (!string.IsNullOrEmpty(targetIdentifier)) {
                    lines.Add(targetIdentifier);
                } else {
                    lines.AddRange(targetLines);
                }
                lines.Add(".length - ");
                int startDepth = context.DepthClass;
                ProcessExpression(semantic, context, operand, lines);
                context.PopClass(startDepth);
                return;
            }

            int depth = context.DepthClass;
            ProcessExpression(semantic, context, bound, lines);
            context.PopClass(depth);
        }

        /// <summary>
        /// Processes postfix unary expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="postfixUnary">The postfix unary expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessPostfixUnaryExpression(SemanticModel semantic, LayerContext context, PostfixUnaryExpressionSyntax postfixUnary, List<string> lines) {
            // Process the operand first
            int start = context.DepthClass;
            ProcessExpression(semantic, context, postfixUnary.Operand, lines);
            context.PopClass(start);

            // Add the postfix operator (e.g., ++ or --)
            lines.Add(postfixUnary.OperatorToken.ToString());
        }

        /// <summary>
        /// Processes prefix unary expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="prefixUnary">The prefix unary expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the unary expression.</returns>
        protected override ExpressionResult ProcessPrefixUnaryExpression(SemanticModel semantic, LayerContext context, PrefixUnaryExpressionSyntax prefixUnary, List<string> lines) {
            // Map the operator to the corresponding TypeScript operator
            string operatorSymbol = prefixUnary.OperatorToken.ToString();
            lines.Add(operatorSymbol);

            // Process into a private buffer first: some invocation lowerings expand to a binary
            // expression (ReferenceEquals -> left === right) even when the source operand is an invocation.
            int start = context.DepthClass;
            List<string> operandLines = new List<string>();
            ExpressionResult result = ProcessExpression(semantic, context, prefixUnary.Operand, operandLines);
            context.PopClass(start);
            string emittedOperand = string.Concat(operandLines);
            bool requiresGrouping = prefixUnary.Operand is BinaryExpressionSyntax ||
                prefixUnary.Operand is ConditionalExpressionSyntax ||
                prefixUnary.Operand is AssignmentExpressionSyntax ||
                emittedOperand.Contains(" === ") || emittedOperand.Contains(" !== ") ||
                emittedOperand.Contains(" == ") || emittedOperand.Contains(" != ") ||
                emittedOperand.Contains(" && ") || emittedOperand.Contains(" || ") ||
                emittedOperand.Contains(" ?? ");
            if (requiresGrouping) {
                lines.Add("(");
            }
            lines.AddRange(operandLines);
            if (requiresGrouping) {
                lines.Add(")");
            }

            return result;
        }

        /// <summary>
        /// Processes member binding expressions within conditional access chains.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="memberBinding">The member binding expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessMemberBindingExpression(SemanticModel semantic, LayerContext context, MemberBindingExpressionSyntax memberBinding, List<string> lines) {
            Stack<string> receivers = ConditionalAccessReceivers.Value;
            if (receivers != null && receivers.Count > 0) {
                lines.Add(receivers.Pop());
                lines.Add(".");
            }
            ISymbol memberSymbol = semantic.GetSymbolInfo(memberBinding.Name).Symbol;
            if (memberSymbol?.Name == "Length" &&
                (memberSymbol.ContainingType?.SpecialType == SpecialType.System_Array ||
                 memberSymbol.ContainingType?.SpecialType == SpecialType.System_String)) {
                lines.Add("length");
                return;
            }
            if (memberSymbol?.Name == "ToString" && memberSymbol.ContainingType?.ToDisplayString() == "System.Guid") {
                lines.Add("toString");
                return;
            }
            if (memberSymbol?.ContainingType?.SpecialType == SpecialType.System_String) {
                string stringMember = memberSymbol.Name switch {
                    "Trim" => "trim",
                    "TrimStart" => "trimStart",
                    "TrimEnd" => "trimEnd",
                    "ToLower" => "toLowerCase",
                    "ToLowerInvariant" => "toLowerCase",
                    "ToUpper" => "toUpperCase",
                    "ToUpperInvariant" => "toUpperCase",
                    _ => null
                };
                if (stringMember != null) {
                    lines.Add(stringMember);
                    return;
                }
            }
            if (memberSymbol != null) {
                ConversionClass owner = ((TypeScriptProgram)context.Program).GetClassByName(memberSymbol.ContainingType?.Name);
                if (memberSymbol is IMethodSymbol method) {
                    ConversionFunction function = owner?.Functions.FirstOrDefault(candidate =>
                        candidate.Name == method.Name && candidate.InParameters.Count == method.Parameters.Length);
                    lines.Add(!string.IsNullOrWhiteSpace(function?.Remap) ? function.Remap : function?.Name ?? memberSymbol.Name);
                    return;
                }
                ConversionVariable variable = owner?.Variables.FirstOrDefault(candidate => candidate.Name == memberSymbol.Name);
                lines.Add(!string.IsNullOrWhiteSpace(variable?.Remap) ? variable.Remap : variable?.Name ?? memberSymbol.Name);
                return;
            }
            ProcessExpression(semantic, context, memberBinding.Name, lines);
        }

        /// <summary>
        /// Processes conditional access expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="conditionalAccess">The conditional access expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessConditionalAccessExpression(SemanticModel semantic, LayerContext context, ConditionalAccessExpressionSyntax conditionalAccess, List<string> lines) {
            if (conditionalAccess.WhenNotNull is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberBindingExpressionSyntax &&
                invocation.ArgumentList.Arguments.Count == 0) {
                IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocation);
                if (method?.Name == "ToList" && method.ReducedFrom != null &&
                    method.ContainingType?.ToDisplayString() == "System.Linq.Enumerable") {
                    string receiver = TemporaryNames.Allocate(semantic, "__conditionalEnumerable");
                    context.AddClass(((TypeScriptProgram)context.Program).GetClassByName("NativeArrayUtil"));
                    lines.Add("(() => { const ");
                    lines.Add(receiver);
                    lines.Add(" = ");
                    int receiverDepth = context.DepthClass;
                    ProcessExpression(semantic, context, conditionalAccess.Expression, lines);
                    context.PopClass(receiverDepth);
                    lines.Add("; return ");
                    lines.Add(receiver);
                    lines.Add(" == null ? undefined : NativeArrayUtil.toList(");
                    lines.Add(receiver);
                    lines.Add("); })()");
                    return;
                }
            }

            if (ShouldAwaitConvertedConditionalInvocation(semantic, context, conditionalAccess)) {
                lines.Add("await ");
                context.GetCurrentFunction().Function.IsAsync = true;
            }
            string conditionalReceiver = TemporaryNames.Allocate(semantic, "__conditionalReceiver");
            lines.Add("(() => { const ");
            lines.Add(conditionalReceiver);
            lines.Add(" = ");
            int expressionDepth = context.DepthClass;
            ProcessExpression(semantic, context, conditionalAccess.Expression, lines);
            context.PopClass(expressionDepth);
            lines.Add("; return ");
            lines.Add(conditionalReceiver);
            lines.Add(" == null ? undefined : ");

            Stack<string> receivers = ConditionalAccessReceivers.Value ??= new Stack<string>();
            int originalReceiverCount = receivers.Count;
            receivers.Push(conditionalReceiver);
            try {
                ProcessExpression(semantic, context, (ExpressionSyntax)conditionalAccess.WhenNotNull, lines);
            } finally {
                while (receivers.Count > originalReceiverCount) {
                    receivers.Pop();
                }
            }
            lines.Add("; })()");
        }

        /// <summary>Detects a synchronous C# method whose emitted implementation became asynchronous.</summary>
        bool ShouldAwaitConvertedConditionalInvocation(
            SemanticModel semantic,
            LayerContext context,
            ConditionalAccessExpressionSyntax conditionalAccess) {

            if (conditionalAccess.WhenNotNull is not InvocationExpressionSyntax invocation) {
                return false;
            }
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocation);
            if (method?.ContainingType == null) {
                return false;
            }
            TypeScriptProgram program = (TypeScriptProgram)context.Program;
            ConversionClass owner = program.GetClassByName(method.ContainingType.Name);
            ConversionFunction function = owner?.Functions.FirstOrDefault(candidate =>
                candidate.Name == method.Name && candidate.InParameters.Count == method.Parameters.Length);
            if (function == null) {
                return false;
            }
            EnsureFunctionAsyncState(semantic, context, owner, function);
            return function.IsAsync;
        }

        /// <summary>
        /// Processes cast expressions into TypeScript type assertions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="castExpr">The cast expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the cast.</returns>
        protected override ExpressionResult ProcessCastExpression(SemanticModel semantic, LayerContext context, CastExpressionSyntax castExpr, List<string> lines) {
            VariableType varType = VariableUtil.GetVarType(castExpr.Type, semantic);
            HashSet<string> erasedTypeParameters = GetStaticClassGenericParameters(context);
            if (erasedTypeParameters != null && erasedTypeParameters.Count > 0) {
                varType = ReplaceTypeParameters(varType, erasedTypeParameters);
            }

            lines.Add("<");
            lines.Add(varType.ToTypeScriptString((TypeScriptProgram)context.Program)); // Type of the cast
            lines.Add(">");
            lines.Add("<unknown>");

            ProcessExpression(semantic, context, castExpr.Expression, lines); // Expression being cast

            return new ExpressionResult(true, VariablePath.Unknown, varType);
        }

        static HashSet<string> GetStaticClassGenericParameters(LayerContext context) {
            if (context == null) {
                return null;
            }

            FunctionStack fn = context.GetCurrentFunction();
            if (fn == null || !fn.Function.IsStatic) {
                return null;
            }

            ConversionClass cl = context.GetCurrentClass();
            if (cl?.GenericArgs == null || cl.GenericArgs.Count == 0) {
                return null;
            }

            HashSet<string> erased = new HashSet<string>(cl.GenericArgs, StringComparer.Ordinal);
            if (fn.Function.GenericParameters != null) {
                for (int i = 0; i < fn.Function.GenericParameters.Count; i++) {
                    erased.Remove(fn.Function.GenericParameters[i]);
                }
            }

            return erased.Count > 0 ? erased : null;
        }

        static VariableType ReplaceTypeParameters(VariableType source, HashSet<string> erased) {
            if (source == null) {
                return null;
            }

            VariableType clone = new VariableType(source);
            if (!string.IsNullOrWhiteSpace(clone.TypeName) && erased.Contains(clone.TypeName)) {
                clone.TypeName = "any";
                clone.Type = VariableDataType.Object;
                clone.Args = new List<VariableType>();
                clone.GenericArgs = new List<VariableType>();
                return clone;
            }

            if (clone.Args != null && clone.Args.Count > 0) {
                List<VariableType> replacedArgs = new List<VariableType>(clone.Args.Count);
                for (int i = 0; i < clone.Args.Count; i++) {
                    replacedArgs.Add(ReplaceTypeParameters(clone.Args[i], erased));
                }
                clone.Args = replacedArgs;
            }

            if (clone.GenericArgs != null && clone.GenericArgs.Count > 0) {
                List<VariableType> replacedGenerics = new List<VariableType>(clone.GenericArgs.Count);
                for (int i = 0; i < clone.GenericArgs.Count; i++) {
                    replacedGenerics.Add(ReplaceTypeParameters(clone.GenericArgs[i], erased));
                }
                clone.GenericArgs = replacedGenerics;
            }

            return clone;
        }

        /// <summary>
        /// Processes conditional (ternary) expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="conditional">The conditional expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessConditionalExpression(SemanticModel semantic, LayerContext context, ConditionalExpressionSyntax conditional, List<string> lines) {
            List<string> conditionLines = new List<string>();
            int startDepth = context.DepthClass;
            ExpressionResult condResult = ProcessExpression(semantic, context, conditional.Condition, conditionLines);
            context.PopClass(startDepth);

            List<string> whenTrueLines = new List<string>();
            ExpressionResult whenTrueResult = BuildConditionalBranchExpression(semantic, context, conditional.WhenTrue, whenTrueLines);

            List<string> whenFalseLines = new List<string>();
            ExpressionResult whenFalseResult = BuildConditionalBranchExpression(semantic, context, conditional.WhenFalse, whenFalseLines);

            bool needsPrelude = HasPreludeLines(condResult) ||
                HasPreludeLines(whenTrueResult) ||
                HasPreludeLines(whenFalseResult);

            if (!needsPrelude) {
                lines.AddRange(conditionLines);
                lines.Add(" ? ");
                lines.AddRange(whenTrueLines);
                lines.Add(" : ");
                lines.AddRange(whenFalseLines);
                return;
            }

            bool requiresAsyncIife = ContainsAwait(conditionLines) ||
                ContainsAwait(condResult.BeforeLines) || ContainsAwait(condResult.AfterLines) ||
                ContainsAwait(whenTrueLines) || ContainsAwait(whenTrueResult.BeforeLines) || ContainsAwait(whenTrueResult.AfterLines) ||
                ContainsAwait(whenFalseLines) || ContainsAwait(whenFalseResult.BeforeLines) || ContainsAwait(whenFalseResult.AfterLines);
            string resultVar = TemporaryNames.Allocate(semantic, "__cond_");
            lines.Add(requiresAsyncIife ? "(await (async () => {\n" : "(() => {\n");

            lines.Add("let ");
            lines.Add(resultVar);
            lines.Add(";\n");

            if (condResult.BeforeLines != null && condResult.BeforeLines.Count > 0) {
                lines.AddRange(condResult.BeforeLines);
            }

            bool hasCondAfter = condResult.AfterLines != null && condResult.AfterLines.Count > 0;
            if (hasCondAfter) {
                string condVar = TemporaryNames.Allocate(semantic, "__cond_");
                lines.Add("const ");
                lines.Add(condVar);
                lines.Add(" = ");
                lines.AddRange(conditionLines);
                lines.Add(";\n");
                lines.AddRange(condResult.AfterLines);
                lines.Add("if (");
                lines.Add(condVar);
                lines.Add(") {\n");
            } else {
                lines.Add("if (");
                lines.AddRange(conditionLines);
                lines.Add(") {\n");
            }

            AppendConditionalBranchAssignment(semantic, lines, resultVar, whenTrueLines, whenTrueResult);
            lines.Add("} else {\n");
            AppendConditionalBranchAssignment(semantic, lines, resultVar, whenFalseLines, whenFalseResult);
            lines.Add("}\n");
            lines.Add("return ");
            lines.Add(resultVar);
            lines.Add(";\n");
            lines.Add(requiresAsyncIife ? "})())" : "})()");
        }

        /// <summary>
        /// Appends a conditional branch, handling throw expressions via an IIFE wrapper.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="branchExpression">The branch expression to emit.</param>
        /// <param name="lines">The output lines to append to.</param>
        void AppendConditionalBranch(SemanticModel semantic, LayerContext context, ExpressionSyntax branchExpression, List<string> lines) {
            if (branchExpression is ThrowExpressionSyntax throwExpression) {
                lines.Add("(() => { throw ");
                int startDepth = context.DepthClass;
                ProcessExpression(semantic, context, throwExpression.Expression, lines);
                context.PopClass(startDepth);
                lines.Add("; })()");
                return;
            }

            int branchDepth = context.DepthClass;
            ProcessExpression(semantic, context, branchExpression, lines);
            context.PopClass(branchDepth);
        }

        /// <summary>Returns whether rendered TypeScript lines contain an await expression.</summary>
        static bool ContainsAwait(IEnumerable<string> lines) {
            return lines != null && lines.Any(line => line != null && line.Contains("await ", StringComparison.Ordinal));
        }
        /// <summary>
        /// Determines whether an expression result requires prelude or follow-up statements.
        /// </summary>
        /// <param name="result">The expression result to inspect.</param>
        /// <returns>True when prelude or follow-up lines are present.</returns>
        bool HasPreludeLines(ExpressionResult result) {
            return result.BeforeLines != null && result.BeforeLines.Count > 0 ||
                result.AfterLines != null && result.AfterLines.Count > 0;
        }

        /// <summary>
        /// Builds a conditional branch expression, handling throw expressions when needed.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="branchExpression">The branch expression to render.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result for the branch.</returns>
        ExpressionResult BuildConditionalBranchExpression(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax branchExpression,
            List<string> lines) {
            if (branchExpression is ThrowExpressionSyntax throwExpression) {
                lines.Add("(() => { throw ");
                int startDepth = context.DepthClass;
                ProcessExpression(semantic, context, throwExpression.Expression, lines);
                context.PopClass(startDepth);
                lines.Add("; })()");
                return new ExpressionResult(true);
            }

            int branchDepth = context.DepthClass;
            ExpressionResult result = ProcessExpression(semantic, context, branchExpression, lines);
            context.PopClass(branchDepth);
            return result;
        }

        /// <summary>
        /// Appends a conditional branch assignment into a prelude-based conditional expression.
        /// </summary>
        /// <param name="semantic">Compilation used to reserve source identifiers for generated temporaries.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="resultVar">The variable receiving the branch value.</param>
        /// <param name="branchLines">The rendered branch expression lines.</param>
        /// <param name="branchResult">The expression result for the branch.</param>
        void AppendConditionalBranchAssignment(
            SemanticModel semantic,
            List<string> lines,
            string resultVar,
            List<string> branchLines,
            ExpressionResult branchResult) {
            if (branchResult.BeforeLines != null && branchResult.BeforeLines.Count > 0) {
                lines.AddRange(branchResult.BeforeLines);
            }

            bool hasAfter = branchResult.AfterLines != null && branchResult.AfterLines.Count > 0;
            if (hasAfter) {
                string branchVar = TemporaryNames.Allocate(semantic, "__cond_");
                lines.Add("const ");
                lines.Add(branchVar);
                lines.Add(" = ");
                lines.AddRange(branchLines);
                lines.Add(";\n");
                lines.AddRange(branchResult.AfterLines);
                lines.Add(resultVar);
                lines.Add(" = ");
                lines.Add(branchVar);
                lines.Add(";\n");
                return;
            }

            lines.Add(resultVar);
            lines.Add(" = ");
            lines.AddRange(branchLines);
            lines.Add(";\n");
        }

        /// <summary>
        /// Processes parenthesized lambda expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="lambda">The lambda expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessLambdaExpression(SemanticModel semantic, LayerContext context, ParenthesizedLambdaExpressionSyntax lambda, List<string> lines) {
            int startIndex = lines.Count;

            lines.Add("(");
            for (int i = 0; i < lambda.ParameterList.Parameters.Count; i++) {
                var parameter = lambda.ParameterList.Parameters[i];
                lines.Add(parameter.Identifier.ToString());

                if (i < lambda.ParameterList.Parameters.Count - 1) {
                    lines.Add(", ");
                }
            }
            lines.Add(") => ");

            bool isAsync = false;

            if (lambda.Body is BlockSyntax block) {
                lines.Add("{\n");
                ExpressionResult result = ProcessBlock(semantic, context, block, lines);
                lines.Add("}\n");

                if (result.Type != null && result.Type.TypeName.StartsWith("Promise<", StringComparison.Ordinal)) {
                    isAsync = true;
                }

                // Async infection: a synchronous C# lambda whose body calls a method that became async in
                // TS gains awaits during conversion; without `async` on the lambda the emitted TS is invalid.
                if (!isAsync && lines.Skip(startIndex).Any(l => l.Contains("await "))) {
                    isAsync = true;
                }
            } else if (lambda.Body is ExpressionSyntax expressionBody) {
                List<string> bodyLines = new List<string>();
                ExpressionResult result = ProcessExpression(semantic, context, expressionBody, bodyLines);

                if (result.BeforeLines != null) {
                    lines.AddRange(result.BeforeLines);
                }

                if (result.Type != null && result.Type.TypeName.StartsWith("Promise<", StringComparison.Ordinal)) {
                    isAsync = true;
                }

                if (!isAsync && bodyLines.Any(l => l.Contains("await "))) {
                    isAsync = true;
                }

                lines.AddRange(bodyLines);

                if (result.AfterLines != null) {
                    lines.AddRange(result.AfterLines);
                }
            }

            if (isAsync) {
                lines.Insert(startIndex, "async ");
            }
        }


        /// <summary>
        /// Processes empty statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="emptyStatement">The empty statement syntax.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessEmptyStatement(SemanticModel semantic, LayerContext context, EmptyStatementSyntax emptyStatement, List<string> lines) {
            lines.Add(";\n");
        }

        /// <summary>
        /// Processes do-while statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="doStatement">The do statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessDoStatement(SemanticModel semantic, LayerContext context, DoStatementSyntax doStatement, List<string> lines) {
            // Start the `do` block
            lines.Add("do {\n");

            // Process the body of the `do` statement
            int start = context.DepthClass;
            ProcessStatement(semantic, context, doStatement.Statement, lines);
            context.PopClass(start);

            // Close the `do` block and start the `while` condition
            lines.Add("} while (");

            // Process the condition expression
            int start2 = context.DepthClass;
            ProcessExpression(semantic, context, doStatement.Condition, lines);
            context.PopClass(start2);

            // Close the `while` statement
            lines.Add(");\n");
        }

        /// <summary>
        /// Processes using statements into try/finally disposal patterns.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="usingStatement">The using statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessUsingStatement(SemanticModel semantic, LayerContext context, UsingStatementSyntax usingStatement, List<string> lines) {
            lines.Add("let ");

            // process the resource declaration (if any)
            List<string> nameLines = new List<string>();
            bool isSingleDeclaration = usingStatement.Declaration != null &&
                usingStatement.Declaration.Variables.Count == 1;
            bool isExpressionResource = usingStatement.Expression != null;
            string expressionResourceName = null;
            if (usingStatement.Declaration != null) {
                var declaration = usingStatement.Declaration;
                for (int i = 0; i < declaration.Variables.Count; i++) {
                    var variable = declaration.Variables[i];
                    nameLines.Add($"{variable.Identifier.ToString()}");

                    if (i < declaration.Variables.Count - 1) {
                        nameLines.Add(",");
                    }
                }
            } else if (usingStatement.Expression != null) {
                expressionResourceName = TemporaryNames.Allocate(semantic, "__using_");
                nameLines.Add(expressionResourceName);
            }


            List<string> declLines = new List<string>();
            declLines.Add("try {\n");

            // process the resource declaration (if any)
            if (usingStatement.Declaration != null) {
                ExpressionResult result = ProcessDeclaration(semantic, context, usingStatement.Declaration, declLines, true);

                if (result.Type != null) {
                    string typeName = result.Type.ToTypeScriptStringNoAsync((TypeScriptProgram)context.Program);
                    if (isSingleDeclaration) {
                        nameLines.Add($": {typeName} | null");
                    } else {
                        nameLines.Add($": {typeName}");
                    }
                }
            } else if (usingStatement.Expression != null) {
                List<string> expressionLines = new List<string>();
                ExpressionResult result = ProcessExpression(semantic, context, usingStatement.Expression, expressionLines);

                if (result.Type != null) {
                    string typeName = result.Type.ToTypeScriptStringNoAsync((TypeScriptProgram)context.Program);
                    if (isExpressionResource || isSingleDeclaration) {
                        nameLines.Add($": {typeName} | null");
                    } else {
                        nameLines.Add($": {typeName}");
                    }
                }

                declLines.Add($"{expressionResourceName} = ");
                declLines.AddRange(expressionLines);
                declLines.Add(";\n");
            }
            if (isExpressionResource || isSingleDeclaration) {
                nameLines.Add(" = null");
            }
            nameLines.Add(";\n");
            declLines.Add(";\n");

            // process the body of the using statement
            ProcessStatement(semantic, context, usingStatement.Statement, declLines);

            declLines.Add("} finally {\n");

            // optionally, add resource disposal logic in the finally block
            if (usingStatement.Declaration != null) {
                foreach (var variable in usingStatement.Declaration.Variables) {
                    declLines.Add($"{variable.Identifier.Text}?.dispose();\n");
                }
            } else if (usingStatement.Expression != null) {
                declLines.Add($"{expressionResourceName}?.dispose();\n");
            }

            declLines.Add("}\n\n");

            lines.AddRange(nameLines);
            lines.AddRange(declLines);
        }

        /// <summary>
        /// Processes lock statements, emitting a placeholder in TypeScript.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="lockStatement">The lock statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessLockStatement(SemanticModel semantic, LayerContext context, LockStatementSyntax lockStatement, List<string> lines) {
            // JavaScript omits synchronization, but the C# lock body still owns a lexical scope.
            // Keep that scope so repeated local names in separate locks never collide after emission.
            lines.Add("// Lock omitted in TypeScript\n");
            lines.Add("{\n");
            ProcessStatement(semantic, context, lockStatement.Statement, lines);
            lines.Add("}\n");
        }

        /// <summary>
        /// Processes try/catch/finally statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="tryStatement">The try statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessTryStatement(SemanticModel semantic, LayerContext context, TryStatementSyntax tryStatement, List<string> lines) {
            // Process the 'try' block
            lines.Add("try {\n");
            ProcessStatement(semantic, context, tryStatement.Block, lines);
            lines.Add("}\n");

            // Process the 'catch' block(s). TypeScript allows only ONE catch clause and this emitter has
            // always erased C# catch type filters (a typed C# catch becomes an untyped TS catch), so under
            // the established semantics the FIRST clause already catches everything: emitting one TS catch
            // per C# clause produced syntactically invalid `catch {} catch {}` output. For multi-catch we
            // therefore emit only the first clause and note the merge.
            var catchClauses = tryStatement.Catches.Count > 1
                ? new[] { tryStatement.Catches[0] }
                : tryStatement.Catches.ToArray();
            foreach (var catchClause in catchClauses) {
                string catchVarName = GetCatchVariableName(catchClause);
                lines.Add("catch (");
                lines.Add(catchVarName);
                lines.Add(") {\n");
                if (catchClause.Declaration != null && !catchClause.Declaration.Identifier.IsMissing
                    && !string.IsNullOrWhiteSpace(catchClause.Declaration.Identifier.Text)) {
                    FunctionStack fn = context.GetCurrentFunction();
                    ConversionVariable var = new ConversionVariable();
                    var.Name = catchVarName;
                    var.VarType = VariableUtil.GetVarType(catchClause.Declaration.Type, semantic);
                    fn.Stack.Add(var);
                }
                if (tryStatement.Catches.Count > 1) {
                    lines.Add("// cs2.ts: subsequent C# catch clauses merged into this one (catch type filters are erased in TS)\n");
                }
                ProcessStatement(semantic, context, catchClause.Block, lines);
                lines.Add("}\n");
            }

            // Process the 'finally' block, if it exists
            if (tryStatement.Finally != null) {
                lines.Add("finally {\n");
                ProcessStatement(semantic, context, tryStatement.Finally.Block, lines);
                lines.Add("}\n");
            }
        }

        /// <summary>
        /// Processes foreach statements into for-of loops.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="forEachStatement">The foreach statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessForEachStatement(SemanticModel semantic, LayerContext context, ForEachStatementSyntax forEachStatement, List<string> lines) {
            lines.Add("for (let ");
            lines.Add(forEachStatement.Identifier.Text);
            lines.Add(" of ");
            int exprDepth = context.DepthClass;
            ExpressionResult result = ProcessExpression(semantic, context, forEachStatement.Expression, lines);
            context.PopClass(exprDepth);
            lines.Add(") {\n");

            FunctionStack fn = context.GetCurrentFunction();
            if (fn != null) {
                VariableType elementType = null;
                ForEachStatementInfo forEachInfo = semantic.GetForEachStatementInfo(forEachStatement);
                if (forEachInfo.ElementType != null) {
                    elementType = VariableUtil.GetVarType(forEachInfo.ElementType);
                } else if (forEachStatement.Type != null) {
                    elementType = VariableUtil.GetVarType(forEachStatement.Type, semantic);
                }

                if (elementType != null) {
                    ConversionVariable var = new ConversionVariable {
                        Name = forEachStatement.Identifier.Text,
                        VarType = elementType
                    };
                    fn.Stack.Add(var);
                }
            }

            // Process the body of the forEach loop
            ProcessStatement(semantic, context, forEachStatement.Statement, lines);

            lines.Add("}\n");
        }

        /// <summary>
        /// Processes continue statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="continueStatement">The continue statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessContinueStatement(SemanticModel semantic, LayerContext context, ContinueStatementSyntax continueStatement, List<string> lines) {
            lines.Add("continue;\n");
        }

        /// <summary>
        /// Processes while statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="whileStatement">The while statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessWhileStatement(SemanticModel semantic, LayerContext context, WhileStatementSyntax whileStatement, List<string> lines) {
            int conditionDepth = context.DepthClass;
            List<string> conditionLines = new List<string>();
            ExpressionResult conditionResult = ProcessExpression(semantic, context, whileStatement.Condition, conditionLines);
            context.PopClass(conditionDepth);

            List<string> beforeLines = conditionResult.BeforeLines ?? new List<string>();
            List<string> afterLines = conditionResult.AfterLines ?? new List<string>();
            if (beforeLines.Count == 0 && afterLines.Count == 0) {
                lines.Add("while (");
                lines.AddRange(conditionLines);
                lines.Add(") {\n");
                ProcessStatement(semantic, context, whileStatement.Statement, lines);
                lines.Add("}\n");
                return;
            }

            // A condition can prepare temporaries (notably `out` wrappers) and assign
            // values that the body consumes. Evaluate them on every iteration, while
            // hoisting C# out declarations to the enclosing block where their scope lives.
            List<string> iterationPreamble = new List<string>();
            foreach (string rawBeforeLine in string.Concat(beforeLines).Split('\n')) {
                string beforeLine = rawBeforeLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(beforeLine)) {
                    continue;
                }
                beforeLine += "\n";
                if (beforeLine.StartsWith("let ", StringComparison.Ordinal)) {
                    int assignmentIndex = beforeLine.IndexOf(" = ", StringComparison.Ordinal);
                    if (assignmentIndex > 4) {
                        string name = beforeLine.Substring(4, assignmentIndex - 4);
                        lines.Add($"let {name};\n");
                        iterationPreamble.Add(beforeLine.Substring(4));
                        continue;
                    }

                    lines.Add(beforeLine);
                    continue;
                }
                iterationPreamble.Add(beforeLine);
            }

            List<string> iterationAssignments = new List<string>();
            foreach (string rawAfterLine in string.Concat(afterLines).Split('\n')) {
                string afterLine = rawAfterLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(afterLine)) {
                    continue;
                }
                afterLine += "\n";
                if (afterLine.StartsWith("let ", StringComparison.Ordinal)) {
                    int assignmentIndex = afterLine.IndexOf(" = ", StringComparison.Ordinal);
                    if (assignmentIndex > 4) {
                        string name = afterLine.Substring(4, assignmentIndex - 4);
                        lines.Add($"let {name};\n");
                        iterationAssignments.Add(afterLine.Substring(4));
                        continue;
                    }
                }
                iterationAssignments.Add(afterLine);
            }
            lines.Add("while (true) {\n");
            lines.AddRange(iterationPreamble);
            lines.Add("if (!(");
            lines.AddRange(conditionLines);
            lines.Add(")) { break; }\n");
            lines.AddRange(iterationAssignments);
            ProcessStatement(semantic, context, whileStatement.Statement, lines);
            lines.Add("}\n");
        }
        /// <summary>
        /// Processes for statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="forStatement">The for statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessForStatement(SemanticModel semantic, LayerContext context, ForStatementSyntax forStatement, List<string> lines) {
            lines.Add("for (");

            // Process initialization (if it exists)
            if (forStatement.Declaration != null) {
                ProcessDeclaration(semantic, context, forStatement.Declaration, lines);
            } else if (forStatement.Initializers.Any()) {
                foreach (var initializer in forStatement.Initializers) {
                    int initDepth = context.DepthClass;
                    ProcessExpression(semantic, context, initializer, lines);
                    context.PopClass(initDepth);
                }
            }
            lines.Add("; ");

            // Process condition (if it exists)
            if (forStatement.Condition != null) {
                int startClass = context.DepthClass;
                ProcessExpression(semantic, context, forStatement.Condition, lines);
                context.PopClass(startClass);
            }

            lines.Add("; ");

            // Process incrementors
            foreach (var incrementor in forStatement.Incrementors) {
                int startClass = context.DepthClass;
                ProcessExpression(semantic, context, incrementor, lines);
                context.PopClass(startClass);
            }
            lines.Add(") {\n");

            // Process the body of the for loop
            ProcessStatement(semantic, context, forStatement.Statement, lines);

            lines.Add("}\n");
        }

        /// <summary>
        /// Processes if/else statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="ifStatement">The if statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the condition.</returns>
        protected override ExpressionResult ProcessIfStatement(SemanticModel semantic, LayerContext context, IfStatementSyntax ifStatement, List<string> lines) {
            if (ifStatement.Condition is IsPatternExpressionSyntax patternExpression) {
                ExpressionResult patternResult = TryProcessPatternIfStatement(semantic, context, ifStatement, patternExpression, lines);
                if (patternResult.Processed) {
                    return patternResult;
                }
            }

            List<string> patternDeclarations = BuildCompositePatternDeclarations(semantic, context, ifStatement.Condition);

            int start = context.DepthClass;
            List<string> conditionLines = new List<string>();
            ExpressionResult condResult = ProcessExpression(semantic, context, ifStatement.Condition, conditionLines);
            context.PopClass(start);

            if (patternDeclarations.Count > 0) {
                if (condResult.BeforeLines == null) {
                    condResult.BeforeLines = new List<string>();
                }
                condResult.BeforeLines.InsertRange(0, patternDeclarations);
            }

            bool hasBeforeLines = condResult.BeforeLines != null && condResult.BeforeLines.Count > 0;
            bool hasAfterLines = condResult.AfterLines != null && condResult.AfterLines.Count > 0;
            bool needsPrelude = hasBeforeLines || hasAfterLines;
            bool wrapElseBlock = false;

            if (needsPrelude && lines.Count > 0 && lines[^1] == "else ") {
                lines.RemoveAt(lines.Count - 1);
                lines.Add("else {\n");
                wrapElseBlock = true;
            }

            if (hasBeforeLines) {
                lines.AddRange(condResult.BeforeLines);
            }

            if (hasAfterLines) {
                string condVar = TemporaryNames.Allocate(semantic, "__cond_");
                lines.Add("let ");
                lines.Add(condVar);
                lines.Add(" = ");
                lines.AddRange(conditionLines);
                lines.Add(";\n");
                lines.AddRange(condResult.AfterLines);
                lines.Add("if (");
                lines.Add(condVar);
                lines.Add(") {\n");
            } else {
                lines.Add("if (");
                lines.AddRange(conditionLines);
                lines.Add(") {\n");
            }

            condResult.BeforeLines = null;
            condResult.AfterLines = null;

            // Process the 'then' statements
            ExpressionResult result = ProcessStatement(semantic, context, ifStatement.Statement, lines);
            lines.Add("\n}\n");

            // Process 'else' part if exists
            if (ifStatement.Else != null) {
                lines.Add("else ");
                if (ifStatement.Else.Statement is IfStatementSyntax elseIfStatement) {
                    ProcessIfStatement(semantic, context, elseIfStatement, lines); // Handle else-if cases
                } else {
                    lines.Add("{\n");
                    ProcessStatement(semantic, context, ifStatement.Else.Statement, lines);
                    lines.Add("}\n");
                }
            }

            if (wrapElseBlock) {
                lines.Add("}\n");
            }

            return condResult;
        }

        /// <summary>Builds scope declarations for variables introduced inside a compound if condition.</summary>
        /// <param name="semantic">Semantic model used to resolve declaration pattern types.</param>
        /// <param name="context">Current conversion scope.</param>
        /// <param name="condition">Compound condition whose pattern variables must remain visible in the branch.</param>
        /// <returns>TypeScript declarations emitted before evaluating the condition.</returns>
        static List<string> BuildCompositePatternDeclarations(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax condition) {

            List<string> declarations = new List<string>();
            HashSet<string> declaredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (IsPatternExpressionSyntax patternExpression in condition.DescendantNodesAndSelf().OfType<IsPatternExpressionSyntax>()) {
                if (!TryGetPatternDesignation(patternExpression.Pattern, out string variableName) ||
                    string.IsNullOrWhiteSpace(variableName) ||
                    !declaredNames.Add(variableName) ||
                    !TryGetPatternTypeScriptName(semantic, context, patternExpression.Pattern, out string typeName)) {
                    continue;
                }
                declarations.Add($"let {variableName}!: {typeName};\n");
            }
            return declarations;
        }

        /// <summary>
        /// Processes a statement while preventing duplicated before/after lines from bubbling up.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="statement">The statement to process.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="depth">The current indentation depth.</param>
        /// <returns>The expression result describing the statement.</returns>
        protected override ExpressionResult ProcessStatement(SemanticModel semantic, LayerContext context, StatementSyntax statement, List<string> lines, int depth = 1) {
            int startDepth = context.DepthClass;
            ExpressionResult result;
            if (statement is CheckedStatementSyntax checkedStatement) {
                lines.Add("{\n");
                result = ProcessStatement(semantic, context, checkedStatement.Block, lines, depth);
                lines.Add("}\n");
            } else if (statement is LocalDeclarationStatementSyntax localDeclaration) {
                List<string> declLines = new List<string>();
                result = ProcessDeclaration(semantic, context, localDeclaration.Declaration, declLines, false);
                if (result.BeforeLines != null) {
                    lines.AddRange(result.BeforeLines);
                }
                lines.AddRange(declLines);
                lines.Add(";\n");
                if (result.AfterLines != null) {
                    lines.AddRange(result.AfterLines);
                }
                result.BeforeLines = null;
                result.AfterLines = null;
            } else {
                result = base.ProcessStatement(semantic, context, statement, lines, depth);
            }
            context.PopClass(startDepth);
            if (statement is ExpressionStatementSyntax) {
                result.BeforeLines = null;
                result.AfterLines = null;
            } else if (statement is LocalDeclarationStatementSyntax) {
                result.BeforeLines = null;
                result.AfterLines = null;
            }
            return result;
        }

        /// <summary>
        /// Processes if statements with pattern matching conditions that declare variables.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="ifStatement">The if statement being processed.</param>
        /// <param name="patternExpression">The pattern expression condition.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the condition.</returns>
        ExpressionResult TryProcessPatternIfStatement(
            SemanticModel semantic,
            LayerContext context,
            IfStatementSyntax ifStatement,
            IsPatternExpressionSyntax patternExpression,
            List<string> lines) {
            PatternSyntax declarationPattern = patternExpression.Pattern;
            while (declarationPattern is ParenthesizedPatternSyntax parenthesizedPattern) {
                declarationPattern = parenthesizedPattern.Pattern;
            }
            bool negativeGuard = declarationPattern is UnaryPatternSyntax unaryPattern && unaryPattern.IsKind(SyntaxKind.NotPattern);
            if (negativeGuard) {
                declarationPattern = ((UnaryPatternSyntax)declarationPattern).Pattern;
                while (declarationPattern is ParenthesizedPatternSyntax parenthesizedDeclaration) {
                    declarationPattern = parenthesizedDeclaration.Pattern;
                }
            }
            if (!TryGetPatternDesignation(declarationPattern, out string declaredVariable)) {
                return new ExpressionResult(false);
            }

            if (string.IsNullOrWhiteSpace(declaredVariable)) {
                return new ExpressionResult(false);
            }

            bool wrapElseBlock = false;
            if (lines.Count > 0 && lines[^1] == "else ") {
                lines.RemoveAt(lines.Count - 1);
                lines.Add("else {\n");
                wrapElseBlock = true;
            }

            // Negative guards expose the matched value to the enclosing scope and the else branch.
            // Test that same binding so TypeScript retains its narrowing after an early return.
            string patternTarget = negativeGuard ? declaredVariable : TemporaryNames.Allocate(semantic, "__patternTarget");

            lines.Add(negativeGuard ? "let " : "const ");
            lines.Add(patternTarget);
            lines.Add(" = ");
            int targetDepth = context.DepthClass;
            ProcessExpression(semantic, context, patternExpression.Expression, lines);
            context.PopClass(targetDepth);
            lines.Add(";\n");

            lines.Add("if (");
            if (!TryAppendPatternCondition(semantic, context, patternExpression.Pattern, patternTarget, lines, out _)) {
                throw new NotSupportedException($"Unsupported pattern expression: {patternExpression.Pattern}");
            }
            lines.Add(") {\n");

            if (!negativeGuard) {
                lines.Add("const ");
                lines.Add(declaredVariable);
                lines.Add(" = ");
                if (TryGetPatternTypeScriptName(semantic, context, patternExpression.Pattern, out string declaredType)) {
                    lines.Add("<");
                    lines.Add(declaredType);
                    lines.Add("><unknown>");
                }
                lines.Add(patternTarget);
                lines.Add(";\n");
            }

            ProcessStatement(semantic, context, ifStatement.Statement, lines);
            lines.Add("\n}\n");

            if (ifStatement.Else != null) {
                lines.Add("else ");
                if (ifStatement.Else.Statement is IfStatementSyntax elseIfStatement) {
                    ProcessIfStatement(semantic, context, elseIfStatement, lines);
                } else {
                    lines.Add("{\n");
                    ProcessStatement(semantic, context, ifStatement.Else.Statement, lines);
                    lines.Add("}\n");
                }
            }

            if (wrapElseBlock) {
                lines.Add("}\n");
            }

            return new ExpressionResult(true);
        }

        /// <summary>Returns the catch binding shared by the handler and its rethrows, avoiding source identifier collisions.</summary>
        /// <param name="catchClause">Handler whose caught exception must remain available.</param>
        /// <returns>The declared identifier or a deterministic unused synthetic identifier.</returns>
        static string GetCatchVariableName(CatchClauseSyntax catchClause) {
            string declared = catchClause.Declaration?.Identifier.Text;
            if (!string.IsNullOrWhiteSpace(declared)) {
                return declared;
            }
            string name = "__caughtException_" + catchClause.SpanStart;
            while (catchClause.SyntaxTree.GetRoot().DescendantTokens().Any(token => token.ValueText == name)) {
                name += "_";
            }
            return name;
        }

        /// <summary>
        /// Processes throw statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="throwStatement">The throw statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessThrowStatement(SemanticModel semantic, LayerContext context, ThrowStatementSyntax throwStatement, List<string> lines) {
            if (throwStatement.Expression == null) {
                CatchClauseSyntax catchClause = throwStatement.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault();
                if (catchClause == null) {
                    throw new InvalidOperationException("A rethrow requires an enclosing catch clause.");
                }
                lines.Add("throw " + GetCatchVariableName(catchClause) + ";\n");
            } else {
                lines.Add("throw ");
                ProcessExpression(semantic, context, throwStatement.Expression, lines);
                lines.Add(";\n");
            }
        }

        /// <summary>
        /// Processes switch statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="switchStatement">The switch statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessSwitchStatement(SemanticModel semantic, LayerContext context, SwitchStatementSyntax switchStatement, List<string> lines) {
            if (SwitchHasPatternLabels(switchStatement)) {
                ProcessPatternSwitchStatement(semantic, context, switchStatement, lines);
                return;
            }

            lines.Add("switch (");
            int depth = context.DepthClass;
            ProcessExpression(semantic, context, switchStatement.Expression, lines);
            context.PopClass(depth);
            lines.Add(") {\n");

            foreach (var section in switchStatement.Sections) {
                foreach (var label in section.Labels) {
                    if (label is CaseSwitchLabelSyntax caseLabel) {
                        lines.Add("case ");

                        depth = context.DepthClass;
                        ProcessExpression(semantic, context, caseLabel.Value, lines);
                        context.PopClass(depth);

                        lines.Add(":");

                    } else if (label is DefaultSwitchLabelSyntax) {
                        lines.Add("default: ");
                    }

                }
                lines.Add(" {\n");

                foreach (var stmt in section.Statements) {
                    ProcessStatement(semantic, context, stmt, lines);
                }
                lines.Add("}\n");
            }

            lines.Add("}\n\n");
        }

        /// <summary>
        /// Determines whether a switch statement includes pattern labels.
        /// </summary>
        /// <param name="switchStatement">The switch statement to inspect.</param>
        /// <returns>True when pattern labels are present.</returns>
        bool SwitchHasPatternLabels(SwitchStatementSyntax switchStatement) {
            foreach (var section in switchStatement.Sections) {
                foreach (var label in section.Labels) {
                    if (label is CasePatternSwitchLabelSyntax) {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Processes switch statements with pattern labels using if/else chains.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="switchStatement">The switch statement to emit.</param>
        /// <param name="lines">The output lines to append to.</param>
        void ProcessPatternSwitchStatement(
            SemanticModel semantic,
            LayerContext context,
            SwitchStatementSyntax switchStatement,
            List<string> lines) {
            lines.Add("{\n");
            lines.Add("const __switch = ");
            int depth = context.DepthClass;
            ProcessExpression(semantic, context, switchStatement.Expression, lines);
            context.PopClass(depth);
            lines.Add(";\n");

            bool firstBranch = true;
            foreach (var section in switchStatement.Sections) {
                foreach (var label in section.Labels) {
                    if (label is DefaultSwitchLabelSyntax) {
                        lines.Add(firstBranch ? "if (true) {\n" : "else {\n");
                        EmitPatternSwitchSectionBody(semantic, context, section, lines, declaredVariable: string.Empty);
                        lines.Add("}\n");
                        firstBranch = false;
                        continue;
                    }

                    lines.Add(firstBranch ? "if (" : "else if (");

                    string declaredVariable = string.Empty;
                    if (label is CasePatternSwitchLabelSyntax patternLabel) {
                        if (!TryAppendPatternCondition(semantic, context, patternLabel.Pattern, "__switch", lines, out declaredVariable)) {
                            throw new NotSupportedException($"Unsupported switch pattern: {patternLabel.Pattern}");
                        }

                        if (patternLabel.WhenClause != null) {
                            lines.Add(" && (");
                            int whenDepth = context.DepthClass;
                            ProcessExpression(semantic, context, patternLabel.WhenClause.Condition, lines);
                            context.PopClass(whenDepth);
                            lines.Add(")");
                        }
                    } else if (label is CaseSwitchLabelSyntax caseLabel) {
                        lines.Add("__switch === ");
                        int caseDepth = context.DepthClass;
                        ProcessExpression(semantic, context, caseLabel.Value, lines);
                        context.PopClass(caseDepth);
                    } else {
                        throw new NotSupportedException($"Unsupported switch label: {label}");
                    }

                    lines.Add(") {\n");
                    EmitPatternSwitchSectionBody(semantic, context, section, lines, declaredVariable);
                    lines.Add("}\n");
                    firstBranch = false;
                }
            }

            lines.Add("}\n");
        }

        /// <summary>
        /// Emits the body for a pattern switch section, including variable bindings.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="section">The switch section to emit.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="declaredVariable">The variable name to bind, if any.</param>
        void EmitPatternSwitchSectionBody(
            SemanticModel semantic,
            LayerContext context,
            SwitchSectionSyntax section,
            List<string> lines,
            string declaredVariable) {
            if (!string.IsNullOrWhiteSpace(declaredVariable)) {
                lines.Add("const ");
                lines.Add(declaredVariable);
                lines.Add(" = __switch;\n");
            }

            foreach (var stmt in section.Statements) {
                if (stmt is BreakStatementSyntax) {
                    continue;
                }

                ProcessStatement(semantic, context, stmt, lines);
            }
        }

        /// <summary>
        /// Processes variable declarations.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="declaration">The variable declaration syntax.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            List<string> lines
        ) {
            ProcessDeclaration(semantic, context, declaration, lines, false);
        }


        /// <summary>
        /// Processes variable declarations with optional suppression of the let keyword.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="declaration">The variable declaration syntax.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="skipLet">True to omit the let keyword.</param>
        /// <returns>The expression result describing the initializer.</returns>
        protected ExpressionResult ProcessDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            List<string> lines,
            bool skipLet
            ) {
            if (!skipLet) {
                lines.Add("let ");
            }

            FunctionStack fn = context.GetCurrentFunction();

            int start = context.DepthClass;

            ExpressionResult initResult = default(ExpressionResult);
            List<string> beforeLines = null;
            List<string> afterLines = null;

            for (int i = 0; i < declaration.Variables.Count; i++) {
                var variable = declaration.Variables[i];
                VariableType declaredVariableType = VariableUtil.GetVarType(declaration.Type, semantic);
                lines.Add($"{variable.Identifier.ToString()}");
                if (ShouldAnnotateLocalDeclaration(semantic, declaration, variable)) {
                    lines.Add(": ");
                    if (IsEventSnapshotLocal(semantic, variable)) {
                        TypeScriptProgram program = (TypeScriptProgram)context.Program;
                        context.AddClass(program.GetClassByName("Event"));
                        lines.Add("Event");
                    } else {
                        lines.Add(declaredVariableType.ToTypeScriptString((TypeScriptProgram)context.Program));
                    }
                }

                if (i < declaration.Variables.Count - 1) {
                    lines.Add(",");
                }

                if (fn != null) {
                    ConversionVariable var = new ConversionVariable();
                    var.Name = variable.Identifier.ToString();

                    var.VarType = declaredVariableType;
                    fn.Stack.Add(var);
                }

                if (variable.Initializer != null) {
                    lines.Add($" = ");

                    List<string> initLines = new List<string>();
                    initResult = ProcessExpression(semantic, context, variable.Initializer.Value, initLines);
                    if (initResult.BeforeLines != null && initResult.BeforeLines.Count > 0) {
                        beforeLines ??= new List<string>();
                        beforeLines.AddRange(initResult.BeforeLines);
                    }
                    if (initResult.AfterLines != null && initResult.AfterLines.Count > 0) {
                        afterLines ??= new List<string>();
                        afterLines.AddRange(initResult.AfterLines);
                    }

                    if (initResult.Type != null &&
                        initResult.Type.TypeName.StartsWith("Promise<")) {
                        //lines.Add("await ");

                        if (!fn.Function.IsAsync) {
                            fn.Function.IsAsync = true;
                        }
                    }

                    lines.AddRange(initLines);
                }
            }

            context.PopClass(start);

            if (beforeLines != null) {
                initResult.BeforeLines = beforeLines;
            }
            if (afterLines != null) {
                initResult.AfterLines = afterLines;
            }

            return initResult;
        }

        /// <summary>Retains local types when TypeScript cannot infer the source collection element contract.</summary>
        static bool ShouldAnnotateLocalDeclaration(
            SemanticModel semantic,
            VariableDeclarationSyntax declaration,
            VariableDeclaratorSyntax variable) {

            if (declaration?.Type == null || declaration.Type.ToString() == "var") {
                return false;
            }
            if (variable.Initializer == null) {
                return true;
            }
            if (variable.Initializer.Value is not InvocationExpressionSyntax invocation) {
                return false;
            }
            IMethodSymbol method = GetInvocationMethodSymbol(semantic, invocation);
            if (method?.Name != "Empty" || method.ContainingType?.SpecialType != SpecialType.System_Array) {
                return false;
            }
            return semantic.GetTypeInfo(declaration.Type).Type?.TypeKind == TypeKind.Interface;
        }

        /// <summary>Detects a delegate local used exclusively to snapshot a field-like event.</summary>
        static bool IsEventSnapshotLocal(SemanticModel semantic, VariableDeclaratorSyntax variable) {
            if (semantic?.GetDeclaredSymbol(variable) is not ILocalSymbol local ||
                local.Type?.TypeKind != TypeKind.Delegate) {
                return false;
            }

            SyntaxNode scope = variable.Ancestors().FirstOrDefault(node =>
                node is BaseMethodDeclarationSyntax ||
                node is AccessorDeclarationSyntax ||
                node is LocalFunctionStatementSyntax);
            if (scope == null) {
                return false;
            }

            List<AssignmentExpressionSyntax> assignments = scope.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(assignment => SymbolEqualityComparer.Default.Equals(
                    semantic.GetSymbolInfo(assignment.Left).Symbol,
                    local))
                .ToList();
            return assignments.Count > 0 && assignments.All(assignment =>
                GetEventSymbol(semantic, assignment.Right) != null);
        }

        /// <summary>
        /// Processes literal expressions into TypeScript literals.
        /// </summary>
        /// <param name="context">The active conversion context.</param>
        /// <param name="literalExpression">The literal expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the literal.</returns>
        protected override ExpressionResult ProcessLiteralExpression(LayerContext context, LiteralExpressionSyntax literalExpression, List<string> lines) {
            string literalValue;
            string type;

            switch (literalExpression.Kind()) {
                case SyntaxKind.TrueLiteralExpression:
                    literalValue = "true";
                    type = "bool";
                    break;
                case SyntaxKind.FalseLiteralExpression:
                    literalValue = "false";
                    type = "bool";
                    break;
                case SyntaxKind.NumericLiteralExpression:
                    string tokenText = literalExpression.Token.Text;
                    string valueToken = tokenText.ToLowerInvariant();
                    bool isUnsignedLong = valueToken.EndsWith("ul") || valueToken.EndsWith("lu");
                    if (valueToken.Contains("f")) {
                        type = "float";
                    } else if (isUnsignedLong) {
                        type = "ulong";
                        literalValue = Regex.Replace(tokenText, "(?i)(ul|lu)$", "") + "n";
                        break;
                    } else if (valueToken.Contains("l")) {
                        type = "int64";
                    } else {
                        type = "int32";
                    }
                    literalValue = literalExpression.Token.ValueText;
                    break;
                case SyntaxKind.CharacterLiteralExpression: {
                        type = "char";
                        literalValue = StringUtil.FormatDoubleQuotedLiteral(literalExpression.Token.ValueText);
                        break;
                }
                case SyntaxKind.StringLiteralExpression: {
                        type = "string";
                        literalValue = StringUtil.FormatDoubleQuotedLiteral(literalExpression.Token.ValueText);
                        break;
                }
                case SyntaxKind.NullLiteralExpression:
                    type = "null";
                    literalValue = "null";
                    break;
                case SyntaxKind.DefaultLiteralExpression:
                    type = "null";
                    literalValue = "null";
                    break;
                default:
                    throw new Exception("Unsupported literal type");
            }

            lines.Add(literalValue);

            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(type));
        }

        /// <summary>
        /// Processes arrow expression clauses into assignment expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="arrowExpression">The arrow expression clause.</param>
        /// <param name="lines">The output lines to append to.</param>
        public override void ProcessArrowExpressionClause(SemanticModel semantic, LayerContext context, ArrowExpressionClauseSyntax arrowExpression, List<string> lines) {
            FunctionStack functionStack = context.GetCurrentFunction();
            bool returnsValue = functionStack != null &&
                functionStack.Function.ReturnType != null &&
                functionStack.Function.ReturnType.Type != VariableDataType.Void;

            if (returnsValue) {
                lines.Add("return ");
            }

            ProcessExpression(semantic, context, arrowExpression.Expression, lines);
            lines.Add(";");
        }

        /// <summary>
        /// Processes declaration expressions, including out variable declarations.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="declaration">The declaration expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the declaration.</returns>
        protected override ExpressionResult ProcessDeclarationExpressionSyntax(SemanticModel semantic, LayerContext context, DeclarationExpressionSyntax declaration, List<string> lines) {
            if (declaration.Designation is SingleVariableDesignationSyntax single) {
                string identifier = single.Identifier.Text;
                lines.Add(identifier);

                var variableType = VariableUtil.GetVarType(declaration.Type, semantic);

                ConversionVariable conversionVariable = null;
                var fn = context.GetCurrentFunction();
                if (fn != null) {
                    conversionVariable = new ConversionVariable {
                        Name = identifier,
                        VarType = variableType,
                        Modifier = ParameterModifier.Out
                    };
                    fn.Stack.Add(conversionVariable);
                }

                var result = new ExpressionResult(true, conversionVariable != null ? VariablePath.FunctionStack : VariablePath.Unknown, variableType);
                if (conversionVariable != null) {
                    result.Variable = conversionVariable;
                }

                return result;
            }

            if (declaration.Designation is DiscardDesignationSyntax) {
                lines.Add("_");
                return new ExpressionResult(true);
            }

            lines.Add(declaration.Designation.ToString());
            return new ExpressionResult(true);
        }
    }
}
