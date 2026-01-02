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

namespace cs2.ts {
    /// <summary>
    /// Processes Roslyn syntax nodes into TypeScript code lines following project rules and mappings.
    /// </summary>
    public class TypeScriptConversiorProcessor : ConversionProcessor {
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

            // Then check if that initializer belongs to an ObjectCreationExpression
            return initializer.Parent is ObjectCreationExpressionSyntax;
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
                        if (functionStack.Function.ReturnType != null) {
                            functionStack.Function.ReturnType = new VariableType(functionStack.Function.ReturnType);
                            functionStack.Function.ReturnType.TypeName = $"Promise<{functionStack.Function.ReturnType.TypeName}>";
                        }
                    }
                }

                lines.AddRange(valueLines);
                lines.Add(")");

                return;
            }

            int startDepth = context.Class.Count;
            ExpressionResult assignResult = ProcessExpression(semantic, context, assignment.Left, lines);
            context.PopClass(startDepth);

            string operatorVal = assignment.OperatorToken.ToString();

            if (assignResult.Type != null && assignResult.Type.Type == VariableDataType.Callback && (operatorVal == "+=" || operatorVal == "-=")) {
                lines.Add($" = ");
            } else {
                bool isInsideObject = IsInsideObjectInitializer(assignment);
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
                    if (fn.Function.ReturnType != null) {
                        fn.Function.ReturnType = new VariableType(fn.Function.ReturnType);
                        fn.Function.ReturnType.TypeName = $"Promise<{fn.Function.ReturnType.TypeName}>";
                    }
                }
            }

            lines.AddRange(initLines);

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
            if (expression is SwitchExpressionSyntax switchExpression) {
                return ProcessSwitchExpression(semantic, context, switchExpression, lines);
            }

            if (expression is IsPatternExpressionSyntax patternExpression) {
                return ProcessIsPatternExpression(semantic, context, patternExpression, lines);
            }

            if (expression is CollectionExpressionSyntax collectionExpression) {
                return ProcessCollectionExpression(semantic, context, collectionExpression, lines);
            }

            return base.ProcessExpression(semantic, context, expression, lines, refTypes);
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
            bool isMethod = identifier.Parent is InvocationExpressionSyntax ||
                (identifier.Parent is MemberAccessExpressionSyntax memberAccessSyntax &&
                memberAccessSyntax.Parent is InvocationExpressionSyntax);

            ISymbol nsSymbol = semantic.GetSymbolInfo(identifier).Symbol;
            IMethodSymbol invokedMethod = nsSymbol as IMethodSymbol;
            if (nsSymbol is INamespaceSymbol namespaceSymbol) {
                if (namespaceSymbol.IsNamespace) {
                    return new ExpressionResult(false);
                }
            } else if (invokedMethod != null) {
                isMethod = true;
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

            bool paramsMatch = false;
            if (classFn != null && classFn.InParameters != null && refTypes != null) {
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

                if (overload != null) {
                    classFn = overload;
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
                if (layer == 1 && !forcedStaticPrefix) {
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

                if (classFn.ReturnType != null) {
                    // invoked function
                    if (classFn.ReturnType.Type != VariableDataType.Void) {
                        context.AddClass(GetClass((TypeScriptProgram)context.Program, classFn.ReturnType));

                        if (classFn.IsAsync) {
                            VariableType cloned = new VariableType(classFn.ReturnType);
                            cloned.TypeName = $"Promise<{cloned.TypeName}>";
                            return new ExpressionResult(true, VariablePath.Unknown, cloned);
                        }
                        return new ExpressionResult(true, VariablePath.Unknown, classFn.ReturnType);
                    }
                } else if (classFn.IsAsync) {
                    VariableType cloned = new VariableType(VariableDataType.Void);
                    cloned.TypeName = $"Promise<{cloned.TypeName}>";
                    return new ExpressionResult(true, VariablePath.Unknown, cloned);
                }
            } else if (currentClass != null && currentClass.DeclarationType == MemberDeclarationType.Enum) {

            } else {
                //Debugger.Break();
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

            int startDepth = context.DepthClass;
            ExpressionResult result = ProcessExpression(semantic, context, objectCreation.Type, afterLines);
            context.PopClass(startDepth);

            bool foundMultiple = false;
            List<ConversionFunction> constructors = null;
            if (result.Class != null) {
                constructors = result.Class.Functions.Where(c => c.IsConstructor && !c.IsStatic).ToList();
                if (constructors.Count > 1) {
                    foundMultiple = true;
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
                ConversionFunction fn = constructors.Find(c => {
                    if (c.InParameters.Count == types.Count) {
                        // match parameters
                        for (int i = 0; i < types.Count; i++) {
                            ConversionVariable var = c.InParameters[i];
                            ExpressionResult res = types[i];

                            if (res.Type == null) {
                                return false;
                            }

                            if (var.VarType.TypeName != res.Type.TypeName) {
                                return false;
                            }
                        }
                    } else {
                        return false;
                    }

                    return true;
                });

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
            }

            lines.AddRange(newLines);
            lines.AddRange(afterLines);
            lines.AddRange(finalLines);
            return result;
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

            if (objectCreation.ArgumentList != null && objectCreation.ArgumentList.Arguments.Count > 0) {
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

                lines.AddRange(leftLines);
                lines.Add(".");
            }
            return ProcessExpression(semantic, context, memberAccess.Name, lines, refTypes);
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

            if (TryProcessArrayEmpty(semantic, invocationExpression, lines, out ExpressionResult emptyResult)) {
                return emptyResult;
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

            List<string> argLines = ["("];
            int count = 0;
            List<ExpressionResult> types = new List<ExpressionResult>();

            Dictionary<string, string> outs = new Dictionary<string, string>();
            HashSet<string> outDeclarations = new HashSet<string>();

            List<string> beforeLines = new List<string>();
            List<string> addLines = new List<string>();

            foreach (var arg in invocationExpression.ArgumentList.Arguments) {
                string refKeyword = arg.RefKindKeyword.ToString();
                string strName = string.Empty;
                bool isOut = false;
                bool isOutDeclaration = false;
                if (refKeyword == "out") {
                    isOut = true;
                    isOutDeclaration = arg.Expression is DeclarationExpressionSyntax;
                    beforeLines.Add("let ");
                    strName = "out_" + Guid.NewGuid().ToString().ToLower().Remove(16);
                    strName = StringUtil.Replace(strName, "-", "");
                    beforeLines.Add(strName);
                    beforeLines.Add(" = { value: null };\n");
                }

                int startArg = context.DepthClass;
                int argLinesIndex = argLines.Count;
                ExpressionResult res = ProcessExpression(semantic, context, arg.Expression, argLines);
                types.Add(res);
                context.PopClass(startArg);

                if (isOut) {
                    string outName = argLines[argLinesIndex];
                    if (!isOutDeclaration && res.Variable != null && res.Variable.Modifier.HasFlag(ParameterModifier.Out)) {
                        outName = res.Variable.Name;
                    }

                    if (isOutDeclaration) {
                        outs.Add(outName, strName);
                        outDeclarations.Add(outName);
                    } else if (res.Variable != null && res.Variable.Modifier.HasFlag(ParameterModifier.Out)) {
                        outs.Add(outName + ".value", strName);
                    } else {
                        outs.Add(outName, strName);
                    }
                    argLines.RemoveAt(argLinesIndex);
                    argLines.Add(strName);
                }

                count++;
                if (count != invocationExpression.ArgumentList.Arguments.Count) {
                    argLines.Add(", ");
                }
            }

            AppendOptionalArguments(semantic, invocationExpression, argLines, ref count);
            argLines.Add(")");

            List<string> invoLines = new List<string>();
            ExpressionResult result = ProcessExpression(semantic, context, invocationExpression.Expression, invoLines, types);

            if (result.Type != null &&
                result.Type.TypeName.StartsWith("Promise<")) {
                lines.Add("await ");
                context.GetCurrentFunction().Function.IsAsync = true;
            }

            lines.AddRange(invoLines);

            lines.AddRange(argLines);

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

        static void AppendOptionalArguments(
            SemanticModel semantic,
            InvocationExpressionSyntax invocationExpression,
            List<string> argLines,
            ref int count) {
            IMethodSymbol methodSymbol = GetInvocationMethodSymbol(semantic, invocationExpression);
            if (methodSymbol == null || invocationExpression.ArgumentList == null) {
                return;
            }

            if (invocationExpression.ArgumentList.Arguments.Any(a => a.NameColon != null)) {
                return;
            }

            var parameters = methodSymbol.Parameters;
            if (count >= parameters.Length) {
                return;
            }

            for (int i = count; i < parameters.Length; i++) {
                IParameterSymbol parameter = parameters[i];
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
                int startLeft = context.DepthClass;
                ExpressionResult throwResult = ProcessExpression(semantic, context, binary.Left, throwLeft);
                context.PopClass(startLeft);

                List<string> thrown = new List<string>();
                int startRight = context.DepthClass;
                ProcessExpression(semantic, context, throwExpression.Expression, thrown);
                context.PopClass(startRight);

                lines.AddRange(throwLeft);
                lines.Add(" ?? ");
                lines.Add("(() => { throw ");
                lines.AddRange(thrown);
                lines.Add("; })()");
                return throwResult;
            }

            BinaryOpTypes op = ParseBinaryExpression(semantic, context, binary, out List<string> left, out List<string> right, out ExpressionResult result);
            lines.AddRange(left);

            lines.Add($" {op.ToStringOperator()} ");

            lines.AddRange(right);
            return result;
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

            if (!TryAppendPatternCondition(semantic, context, patternExpression.Pattern, "__pattern", lines, out _)) {
                throw new NotSupportedException($"Unsupported pattern expression: {patternExpression.Pattern}");
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
                AppendPatternTypeCheck(typeName, targetIdentifier, lines);
                return true;
            }

            if (pattern is TypePatternSyntax typePattern) {
                AppendPatternTypeCheck(typePattern.Type.ToString(), targetIdentifier, lines);
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
        /// <param name="targetIdentifier">The identifier to test.</param>
        /// <param name="lines">The output lines to append to.</param>
        void AppendPatternTypeCheck(string typeName, string targetIdentifier, List<string> lines) {
            string normalizedTypeName = NormalizePatternTypeName(typeName);
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

            lines.Add(targetIdentifier);
            lines.Add(" instanceof ");
            lines.Add(normalizedTypeName);
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
        bool TryGetPatternDesignation(PatternSyntax pattern, out string declaredVariable) {
            declaredVariable = string.Empty;

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
            lines.Add("(() => {");
            lines.Add("const __switch = ");
            int startDepth = context.DepthClass;
            ProcessExpression(semantic, context, switchExpression.GoverningExpression, lines);
            context.PopClass(startDepth);
            lines.Add(";");

            foreach (var arm in switchExpression.Arms) {
                lines.Add("if (");

                if (!TryAppendSwitchPatternCondition(semantic, context, arm.Pattern, lines, out string declaredVariable)) {
                    throw new NotSupportedException($"Unsupported switch expression pattern: {arm.Pattern}");
                }

                if (arm.WhenClause != null) {
                    lines.Add(" && (");
                    int whenDepth = context.DepthClass;
                    ProcessExpression(semantic, context, arm.WhenClause.Condition, lines);
                    context.PopClass(whenDepth);
                    lines.Add(")");
                }

                lines.Add(") {");

                if (!string.IsNullOrEmpty(declaredVariable)) {
                    lines.Add("const ");
                    lines.Add(declaredVariable);
                    lines.Add(" = __switch;");
                }

                if (arm.Expression is ThrowExpressionSyntax throwExpression) {
                    lines.Add("throw ");
                    int throwDepth = context.DepthClass;
                    ProcessExpression(semantic, context, throwExpression.Expression, lines);
                    context.PopClass(throwDepth);
                    lines.Add(";");
                } else {
                    lines.Add("return ");
                    int armDepth = context.DepthClass;
                    ProcessExpression(semantic, context, arm.Expression, lines);
                    context.PopClass(armDepth);
                    lines.Add(";");
                }
                lines.Add("}");
            }

            lines.Add("throw new Error(\"Non-exhaustive switch expression.\");");
            lines.Add("})()");
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
            return new ExpressionResult(true);
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
                ProcessExpression(semantic, context, implicitArray.Initializer.Expressions[i], lines);

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
            var tsProgram = (TypeScriptProgram)context.Program;
            VariableType variableType = VariableUtil.GetVarType(typeOfExpression.Type, semantic);
            string tsType = variableType.ToTypeScriptString(tsProgram);

            string target = NormalizeTypeForTypeof(tsType);
            lines.Add("typeof ");
            lines.Add(target);
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
            int start;
            if (type.ConvertedType is INamedTypeSymbol namedFuncType) {
                var invoke = namedFuncType.DelegateInvokeMethod;
                if (invoke == null) {
                    throw new NotImplementedException();
                }

                ITypeSymbol returnType = invoke.ReturnType;
                start = context.AddClass(tsProgram.GetClassByName(returnType.Name));
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
            }

            if (isAsync) {
                lines.Add("async ");
            }

            lines.Add(simpleLambda.Parameter.Identifier.Text);
            lines.Add(" => ");

            if (hasBodyResult && bodyResult.BeforeLines != null) {
                lines.AddRange(bodyResult.BeforeLines);
            }

            if (simpleLambda.Body is ExpressionSyntax) {
                lines.AddRange(bodyLines);
            } else if (simpleLambda.Body is BlockSyntax block) {
                lines.Add("{\n");
                ProcessStatement(semantic, context, block, lines);
                lines.Add("}\n");
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
                    ProcessExpression(semantic, context, arrayCreation.Initializer.Expressions[i], lines);

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
        protected override void ProcessParenthesizedExpression(SemanticModel semantic, LayerContext context, ParenthesizedExpressionSyntax parenthesizedExpression, List<string> lines) {
            lines.Add("(");
            ProcessExpression(semantic, context, parenthesizedExpression.Expression, lines);
            lines.Add(")");
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
                ProcessExpression(semantic, context, initializerExpression.Expressions[i], lines);

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

            // Process the operand
            int start = context.DepthClass;
            ExpressionResult result = ProcessExpression(semantic, context, prefixUnary.Operand, lines);
            context.PopClass(start);

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
            // Reuse identifier processing to apply remaps (ex: Length -> length).
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
            // Process the expression being accessed conditionally
            ProcessExpression(semantic, context, conditionalAccess.Expression, lines);
            lines.Add("?.");

            // Process the member or invocation being accessed conditionally
            ProcessExpression(semantic, context, (ExpressionSyntax)conditionalAccess.WhenNotNull, lines);
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

            lines.Add("<");
            lines.Add(varType.ToTypeScriptString((TypeScriptProgram)context.Program)); // Type of the cast
            lines.Add(">");
            lines.Add("<unknown>");

            ProcessExpression(semantic, context, castExpr.Expression, lines); // Expression being cast

            return new ExpressionResult(true, VariablePath.Unknown, varType);
        }

        /// <summary>
        /// Processes conditional (ternary) expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="conditional">The conditional expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessConditionalExpression(SemanticModel semantic, LayerContext context, ConditionalExpressionSyntax conditional, List<string> lines) {
            // Process the condition (before the ?)
            ProcessExpression(semantic, context, conditional.Condition, lines);
            lines.Add(" ? ");

            // Process the true branch (after the ? and before the :)
            AppendConditionalBranch(semantic, context, conditional.WhenTrue, lines);
            lines.Add(" : ");

            // Process the false branch (after the :)
            AppendConditionalBranch(semantic, context, conditional.WhenFalse, lines);
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

            ProcessExpression(semantic, context, branchExpression, lines);
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
                Debugger.Break();
            }


            List<string> declLines = new List<string>();
            declLines.Add("try {\n");

            // process the resource declaration (if any)
            if (usingStatement.Declaration != null) {
                ExpressionResult result = ProcessDeclaration(semantic, context, usingStatement.Declaration, declLines, true);

                if (result.Type != null) {
                    nameLines.Add($": {result.Type.ToTypeScriptStringNoAsync((TypeScriptProgram)context.Program)}");
                }
            } else if (usingStatement.Expression != null) {
                ExpressionResult result = ProcessExpression(semantic, context, usingStatement.Expression, declLines);

                if (result.Type != null) {
                    nameLines.Add($": {result.Type.ToTypeScriptStringNoAsync((TypeScriptProgram)context.Program)}");
                }
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
                declLines.Add(usingStatement.Expression.ToString() + "?.dispose();\n");
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
            // You can implement custom locking logic here if needed, otherwise omit the lock
            lines.Add("// Lock omitted in TypeScript\n");

            // Process the body of the lock statement
            ProcessStatement(semantic, context, lockStatement.Statement, lines);
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

            // Process the 'catch' block(s)
            foreach (var catchClause in tryStatement.Catches) {
                lines.Add("catch (");
                if (catchClause.Declaration != null) {
                    lines.Add(catchClause.Declaration.Identifier.Text);

                    FunctionStack fn = context.GetCurrentFunction();
                    ConversionVariable var = new ConversionVariable();
                    var.Name = catchClause.Declaration.Identifier.Text;
                    var.VarType = VariableUtil.GetVarType(catchClause.Declaration.Type, semantic);
                    fn.Stack.Add(var);
                } else {
                    lines.Add("err"); // Default error variable if none provided
                }
                lines.Add(") {\n");
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
            ExpressionResult result = ProcessExpression(semantic, context, forEachStatement.Expression, lines);
            lines.Add(") {\n");

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
            lines.Add("while (");
            ProcessExpression(semantic, context, whileStatement.Condition, lines);
            lines.Add(") {\n");

            // Process the body of the while loop
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
                    ProcessExpression(semantic, context, initializer, lines);
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

            int start = context.DepthClass;
            List<string> conditionLines = new List<string>();
            ExpressionResult condResult = ProcessExpression(semantic, context, ifStatement.Condition, conditionLines);
            context.PopClass(start);

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
                string condVar = "__cond_" + Guid.NewGuid().ToString("N")[..8];
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
            ExpressionResult result = base.ProcessStatement(semantic, context, statement, lines, depth);
            if (statement is ExpressionStatementSyntax) {
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
            if (!TryGetPatternDesignation(patternExpression.Pattern, out string declaredVariable)) {
                return new ExpressionResult(false);
            }

            if (string.IsNullOrWhiteSpace(declaredVariable)) {
                return new ExpressionResult(false);
            }

            lines.Add("const __patternTarget = ");
            int targetDepth = context.DepthClass;
            ProcessExpression(semantic, context, patternExpression.Expression, lines);
            context.PopClass(targetDepth);
            lines.Add(";\n");

            lines.Add("if (");
            if (!TryAppendPatternCondition(semantic, context, patternExpression.Pattern, "__patternTarget", lines, out _)) {
                throw new NotSupportedException($"Unsupported pattern expression: {patternExpression.Pattern}");
            }
            lines.Add(") {\n");

            lines.Add("const ");
            lines.Add(declaredVariable);
            lines.Add(" = __patternTarget;\n");

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

            return new ExpressionResult(true);
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
                lines.Add("throw new Error('Throw empty. TODO: Throw exception');\n");
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

            for (int i = 0; i < declaration.Variables.Count; i++) {
                var variable = declaration.Variables[i];
                lines.Add($"{variable.Identifier.ToString()}");

                if (i < declaration.Variables.Count - 1) {
                    lines.Add(",");
                }

                if (fn != null) {
                    ConversionVariable var = new ConversionVariable();
                    var.Name = variable.Identifier.ToString();

                    var.VarType = VariableUtil.GetVarType(declaration.Type, semantic);
                    fn.Stack.Add(var);
                }

                if (variable.Initializer != null) {
                    lines.Add($" = ");

                    List<string> initLines = new List<string>();
                    initResult = ProcessExpression(semantic, context, variable.Initializer.Value, initLines);

                    if (initResult.Type != null &&
                        initResult.Type.TypeName.StartsWith("Promise<")) {
                        //lines.Add("await ");

                        if (!fn.Function.IsAsync) {
                            fn.Function.IsAsync = true;
                            if (fn.Function.ReturnType != null) {
                                fn.Function.ReturnType = new VariableType(fn.Function.ReturnType);
                                fn.Function.ReturnType.TypeName = $"Promise<{fn.Function.ReturnType.TypeName}>";
                            }
                        }
                    }

                    lines.AddRange(initLines);
                }
            }

            context.PopClass(start);

            return initResult;
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
                    string valueToken = literalExpression.Token.ToString().ToLowerInvariant();
                    if (valueToken.Contains("f")) {
                        type = "float";
                } else if (valueToken.Contains("l")) {
                        type = "int64";
                } else {
                        type = "int32";
                }
                    literalValue = literalExpression.Token.ValueText;
                    break;
                case SyntaxKind.CharacterLiteralExpression: {
                        type = "char";
                        string value = literalExpression.Token.ValueText;
                        literalValue = Regex.Replace(value, @"(?<!\\)\\(?!\\)", @"\\");
                        literalValue = Regex.Replace(literalValue, @"\r?\n", match => {
                            return match.Value == "\r\n" ? "\\r\\n" : "\\n";
                    });
                        literalValue = $"\"{literalValue}\"";
                        break;
                }
                case SyntaxKind.StringLiteralExpression: {
                        type = "string";
                        string value = literalExpression.Token.ValueText;
                        literalValue = Regex.Replace(value, @"(?<!\\)\\(?!\\)", @"\\");
                        literalValue = Regex.Replace(literalValue, @"\r?\n", match => {
                            return match.Value == "\r\n" ? "\\r\\n" : "\\n";
                    });
                        literalValue = $"\"{literalValue}\"";
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
