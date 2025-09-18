using cs2.core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using Nucleus;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace cs2.ts {
    /// <summary>
    /// Processes Roslyn syntax nodes into TypeScript code lines following project rules and mappings.
    /// </summary>
    public class TypeScriptConversiorProcessor : ConversionProcessor {
        private static bool IsInsideObjectInitializer(AssignmentExpressionSyntax assignment) {
            // Walk up until we find the nearest InitializerExpression
            var initializer = assignment.Parent?.AncestorsAndSelf()
                .OfType<InitializerExpressionSyntax>()
                .FirstOrDefault();

            if (initializer == null)
                return false;

            // Then check if that initializer belongs to an ObjectCreationExpression
            var objectCreation = initializer.Parent as ObjectCreationExpressionSyntax;
            return objectCreation != null;
        }

        /// <summary>
        /// Handles assignment expressions, including event-like callbacks (+=/-=) and object initializers.
        /// </summary>
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

                FunctionStack? functionStack = context.GetCurrentFunction();
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

            if (assignResult.Type?.Type == VariableDataType.Callback && (operatorVal == "+=" || operatorVal == "-=")) {
                lines.Add($" = ");
            } else {
                bool isInsideObject = IsInsideObjectInitializer(assignment);
                lines.Add(isInsideObject ? " : " : $" {operatorVal} ");
            }

            startDepth = context.Class.Count;

            List<string> initLines = new List<string>();
            ExpressionResult result = ProcessExpression(semantic, context, assignment.Right, initLines);
            context.PopClass(startDepth);

            FunctionStack? fn = context.GetCurrentFunction();
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
        /// Resolves the ConversionClass for a given VariableType within the TypeScript program.
        /// </summary>
        public static ConversionClass GetClass(TypeScriptProgram program, VariableType varType) {
            string name = varType.GetTypeScriptType(program);
            ConversionClass found = program.Classes.FirstOrDefault(c => c.Name == name);

            if (found == null) {
                name = varType.GetTypeScriptTypeNoGeneric(program);
                found = program.Classes.FirstOrDefault(c => c.Name == name);
            }

            return found;
        }

        private static bool IsDictionaryLike(ITypeSymbol? type) {
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
        protected override ExpressionResult ProcessIdentifierNameSyntax(SemanticModel semantic, LayerContext context, IdentifierNameSyntax identifier, List<string> lines, List<ExpressionResult> refTypes) {
            string name = identifier.ToString();
            bool isMethod = false;

            ISymbol? nsSymbol = semantic.GetSymbolInfo(identifier).Symbol;
            if (nsSymbol is INamespaceSymbol namespaceSymbol) {
                if (namespaceSymbol.IsNamespace) {
                    return new ExpressionResult(false);
                }
            } else if (nsSymbol is IMethodSymbol methodSymbol) {
                isMethod = true;
            }

            int layer = context.GetClassLayer();

            // abstract class
            ConversionClass? staticClass = context.Program.Classes.Find(c => c.Name == name);

            ConversionClass? currentClass = context.GetCurrentClass();

            var classVars = new List<ConversionVariable>();
            context.Class.ForEach(fn => {
                var result = fn?.Variables.Where(var => var.Name == name);
                if (result != null) {
                    classVars.AddRange(result);
                }
            });

            // variable from the current class
            ConversionVariable? classVar = currentClass?.Variables.Find(c => c.Name == name);
            if (classVar == null) {
                // look at base classes
                currentClass?.Extensions.ForEach(c => {
                    ConversionClass cl = context.Program.Classes.FirstOrDefault(k => k.Name == c);
                    ConversionVariable? foundVar = cl?.Variables.Find(c => c.Name == name);
                    if (foundVar != null) {
                        classVar = foundVar;
                    }
                });
            }

            if (classVar == null && staticClass == null) {
                string camelCame = StringUtil.ToCamelCase(name);
                classVar = currentClass?.Variables.Find(c => c.Name == camelCame);
                if (classVar != null) {
                    name = camelCame;
                }
            }

            // function from the current class
            ConversionFunction? classFn = currentClass?.Functions.Find(c => c.Name == name);
            if (classFn == null) {
                // look at base classes
                currentClass?.Extensions.ForEach(c => {
                    ConversionClass cl = context.Program.Classes.FirstOrDefault(k => k.Name == c);
                    ConversionFunction? foundFn = cl?.Functions.Find(c => c.Name == name);
                    if (foundFn != null) {
                        classFn = foundFn;
                    }
                });
            }

            bool paramsMatch = classFn?.InParameters?.Count == refTypes?.Count;

            // here: dynamic system for typed functions. Like BinaryWriter writeByte, writeInt
            if (currentClass != null && classFn == null && isMethod) {
                // search for closest version
                string lowercase = name.ToLowerInvariant();
                string searchName = lowercase;
                for (int i = 0; i < refTypes?.Count; i++) {
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

            // current function
            FunctionStack? currentFn = context.GetCurrentFunction();
            // in-parameter for the current function
            ConversionVariable? functionInVar = currentFn?.Function.InParameters?.Find(c => c.Name == name);

            // current stack
            ConversionVariable? stackVar = currentFn?.Stack.Find(c => c.Name == name);

            var matchingVars = new List<ConversionVariable>();
            context.Function.ForEach(fn => {
                var result = fn?.Stack.Where(var => var.Name == name);
                if (result != null) {
                    matchingVars.AddRange(result);
                }
            });

            if (name == "Dispose") {
                name = "dispose";
            }

            if (currentClass == null) {
                lines.Add(name);
            } else {
                if (layer == 1) {
                    bool isClassVar = (classVar != null &&
                        functionInVar == null &&
                        matchingVars.Count == 0) ||
                        (classFn != null &&
                        functionInVar == null &&
                        matchingVars.Count == 0);


                    if (isClassVar) {
                        // semantic
                        ISymbol? symbol = semantic.GetSymbolInfo(identifier).Symbol;

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
                    ConversionVariable? varOnClass = currentClass.Variables.FirstOrDefault(c => c.Name == name);
                    if (varOnClass == null || string.IsNullOrEmpty(varOnClass.Remap)) {
                        lines.Add(name);
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
            } else if (currentClass?.DeclarationType == MemberDeclarationType.Enum) {

            } else {
                //Debugger.Break();
            }

            return new ExpressionResult(true);
        }

        protected override ExpressionResult ProcessObjectCreationExpressionSyntax(SemanticModel semantic, LayerContext context, ObjectCreationExpressionSyntax objectCreation, List<string> lines) {
            if (objectCreation.Initializer is InitializerExpressionSyntax initializer) {
                if (TryProcessDictionaryCreation(semantic, context, objectCreation, initializer, lines, out var dictResult)) {
                    return dictResult;
                }

                return ProcessExpression(semantic, context, objectCreation.Initializer, lines);
            }

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

                int index = constructors.IndexOf(fn);
                afterLines.Add($"{index + 1}");
            }

            lines.AddRange(newLines);
            lines.AddRange(afterLines);
            lines.AddRange(finalLines);
            return result;
        }

        
        private bool TryProcessDictionaryCreation(
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

        private static bool IsDictionaryType(ITypeSymbol? typeSymbol) {
            if (typeSymbol is INamedTypeSymbol named) {
                var constructedFrom = named.ConstructedFrom ?? named;
                if (constructedFrom.Name == "Dictionary") {
                    string ns = constructedFrom.ContainingNamespace?.ToDisplayString();
                    return ns == "System.Collections.Generic";
                }
            }
            return false;
        }

        private string BuildExpressionString(SemanticModel semantic, LayerContext context, ExpressionSyntax expression, List<string> beforeLines) {
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

protected override ExpressionResult ProcessMemberAccessExpressionSyntax(SemanticModel semantic, LayerContext context, MemberAccessExpressionSyntax memberAccess, List<string> lines, List<ExpressionResult> refTypes) {
            if (ProcessExpression(semantic, context, memberAccess.Expression, lines).Processed) {
                lines.Add(".");
            }
            return ProcessExpression(semantic, context, memberAccess.Name, lines, refTypes);
        }

        protected override ExpressionResult ProcessInvocationExpressionSyntax(SemanticModel semantic, LayerContext context, InvocationExpressionSyntax invocationExpression, List<string> lines) {
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

            List<string> beforeLines = new List<string>();
            List<string> addLines = new List<string>();

            foreach (var arg in invocationExpression.ArgumentList.Arguments) {
                string refKeyword = arg.RefKindKeyword.ToString();
                string strName = string.Empty;
                bool isOut = false;
                if (refKeyword == "out") {
                    isOut = true;
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
                    if (res.Variable != null && res.Variable.Modifier == ParameterModifier.Out) {
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
                addLines.Add($"{pair.Key} = {pair.Value}.value;\n");
            }

            result.BeforeLines = beforeLines;
            result.AfterLines = addLines;
            return result;
        }

        protected override void ProcessThisExpressionSyntax(SemanticModel semantic, LayerContext context, ThisExpressionSyntax thisExpression, List<string> lines) {
            lines.Add("this");
            context.AddClass(context.Class[0]);
        }

        protected override ExpressionResult ProcessBinaryExpressionSyntax(SemanticModel semantic, LayerContext context, BinaryExpressionSyntax binary, List<string> lines) {
            BinaryOpTypes op = ParseBinaryExpression(semantic, context, binary, out List<string> left, out List<string> right, out ExpressionResult result);
            lines.AddRange(left);

            lines.Add($" {op.ToStringOperator()} ");

            lines.AddRange(right);
            return result;
        }

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

        protected override void ProcessAwait(SemanticModel semantic, LayerContext context, AwaitExpressionSyntax awaitExpression, List<string> lines) {
            lines.Add("await ");

            ProcessExpression(semantic, context, awaitExpression.Expression, lines);
        }

        protected override ExpressionResult ProcessQualifiedName(SemanticModel semantic, LayerContext context, QualifiedNameSyntax qualifiedName, List<string> lines) {
            // Process the left part of the qualified name (e.g., "System" in "System.Console")
            if (ProcessExpression(semantic, context, qualifiedName.Left, lines).Processed) {
                // Add the dot separator
                lines.Add(".");
            }

            // Process the right part of the qualified name (e.g., "Console" in "System.Console")
            return ProcessExpression(semantic, context, qualifiedName.Right, lines);
        }

        protected override void ProcessTypeOfExpression(SemanticModel semantic, LayerContext context, TypeOfExpressionSyntax typeOfExpression, List<string> lines) {
            var tsProgram = (TypeScriptProgram)context.Program;
            VariableType variableType = VariableUtil.GetVarType(typeOfExpression.Type, semantic);
            string tsType = variableType.ToTypeScriptString(tsProgram);

            string target = NormalizeTypeForTypeof(tsType);
            lines.Add("typeof ");
            lines.Add(target);
        }

        private static string NormalizeTypeForTypeof(string tsType) {
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

        protected override void ProcessSimpleLambdaExpression(SemanticModel semantic, LayerContext context, SimpleLambdaExpressionSyntax simpleLambda, List<string> lines) {
            TypeInfo type = semantic.GetTypeInfo(simpleLambda);
            int start;
            if (type.ConvertedType is INamedTypeSymbol namedFuncType) {
                var invoke = namedFuncType.DelegateInvokeMethod;
                if (invoke == null) {
                    throw new NotImplementedException();
                }

                ITypeSymbol returnType = invoke.ReturnType;
                start = context.AddClass(context.Program.Classes.Find(c => c.Name == returnType.Name));
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

        protected override void ProcessParenthesizedExpression(SemanticModel semantic, LayerContext context, ParenthesizedExpressionSyntax parenthesizedExpression, List<string> lines) {
            lines.Add("(");
            ProcessExpression(semantic, context, parenthesizedExpression.Expression, lines);
            lines.Add(")");
        }

        protected override void ProcessBaseExpression(SemanticModel semantic, LayerContext context, BaseExpressionSyntax baseExpression, List<string> lines) {
            lines.Add("super");

            context.AddClass(context.GetCurrentClass());

        }

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

        protected override void ProcessPredefinedType(SemanticModel semantic, LayerContext context, PredefinedTypeSyntax predefinedType, List<string> lines) {
            var type = predefinedType.Keyword.ValueText;

            string name = type;
            switch (type) {
                case "uint":
                case "int":
                case "long":
                case "float":
                case "double":
                case "decimal":
                case "short":
                case "byte":
                    name = "Number";
                    break;
                case "bool":
                    name = "Boolean";
                    break;
                case "string":
                    name = "String";
                    break;
                case "char":
                    name = "String";
                    break;
                case "object":
                    name = "any";
                    break;
                case "void":
                    name = "void";
                    break;
            }

            lines.Add(name);
            context.AddClass(context.Program.Classes.Find(c => c.Name == name));
        }

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

        protected override void ProcessElementAccessExpression(SemanticModel semantic, LayerContext context, ElementAccessExpressionSyntax elementAccess, List<string> lines) {
            int startClass = context.DepthClass;
            List<string> targetLines = new List<string>();
            ProcessExpression(semantic, context, elementAccess.Expression, targetLines);
            List<ConversionClass> saved = context.SavePopClass(startClass);

            ITypeSymbol? expressionType = semantic.GetTypeInfo(elementAccess.Expression).Type;
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

        protected override void ProcessPostfixUnaryExpression(SemanticModel semantic, LayerContext context, PostfixUnaryExpressionSyntax postfixUnary, List<string> lines) {
            // Process the operand first
            int start = context.DepthClass;
            ProcessExpression(semantic, context, postfixUnary.Operand, lines);
            context.PopClass(start);

            // Add the postfix operator (e.g., ++ or --)
            lines.Add(postfixUnary.OperatorToken.ToString());
        }

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

        protected override void ProcessMemberBindingExpression(SemanticModel semantic, LayerContext context, MemberBindingExpressionSyntax memberBinding, List<string> lines) {
            // Access the member (property, method, etc.)
            string name = memberBinding.Name.ToString();

            if (name == "Dispose") {
                name = "dispose";
            }

            lines.Add(name);
        }

        protected override void ProcessConditionalAccessExpression(SemanticModel semantic, LayerContext context, ConditionalAccessExpressionSyntax conditionalAccess, List<string> lines) {
            // Process the expression being accessed conditionally
            ProcessExpression(semantic, context, conditionalAccess.Expression, lines);
            lines.Add("?.");

            // Process the member or invocation being accessed conditionally
            ProcessExpression(semantic, context, (ExpressionSyntax)conditionalAccess.WhenNotNull, lines);
        }

        protected override ExpressionResult ProcessCastExpression(SemanticModel semantic, LayerContext context, CastExpressionSyntax castExpr, List<string> lines) {
            VariableType varType = VariableUtil.GetVarType(castExpr.Type, semantic);

            lines.Add("<");
            lines.Add(varType.ToTypeScriptString((TypeScriptProgram)context.Program)); // Type of the cast
            lines.Add(">");
            lines.Add("<unknown>");

            ProcessExpression(semantic, context, castExpr.Expression, lines); // Expression being cast

            return new ExpressionResult(true, VariablePath.Unknown, varType);
        }

        protected override void ProcessConditionalExpression(SemanticModel semantic, LayerContext context, ConditionalExpressionSyntax conditional, List<string> lines) {
            // Process the condition (before the ?)
            ProcessExpression(semantic, context, conditional.Condition, lines);
            lines.Add(" ? ");

            // Process the true branch (after the ? and before the :)
            ProcessExpression(semantic, context, conditional.WhenTrue, lines);
            lines.Add(" : ");

            // Process the false branch (after the :)
            ProcessExpression(semantic, context, conditional.WhenFalse, lines);
        }

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


        protected override void ProcessEmptyStatement(SemanticModel semantic, LayerContext context, EmptyStatementSyntax emptyStatement, List<string> lines) {
            lines.Add(";\n");
        }

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

        protected override void ProcessLockStatement(SemanticModel semantic, LayerContext context, LockStatementSyntax lockStatement, List<string> lines) {
            // You can implement custom locking logic here if needed, otherwise omit the lock
            lines.Add("// Lock omitted in TypeScript\n");

            // Process the body of the lock statement
            ProcessStatement(semantic, context, lockStatement.Statement, lines);
        }

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

                    FunctionStack? fn = context.GetCurrentFunction();
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

        protected override void ProcessContinueStatement(SemanticModel semantic, LayerContext context, ContinueStatementSyntax continueStatement, List<string> lines) {
            lines.Add("continue;\n");
        }

        protected override void ProcessWhileStatement(SemanticModel semantic, LayerContext context, WhileStatementSyntax whileStatement, List<string> lines) {
            lines.Add("while (");
            ProcessExpression(semantic, context, whileStatement.Condition, lines);
            lines.Add(") {\n");

            // Process the body of the while loop
            ProcessStatement(semantic, context, whileStatement.Statement, lines);

            lines.Add("}\n");
        }

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

        protected override ExpressionResult ProcessIfStatement(SemanticModel semantic, LayerContext context, IfStatementSyntax ifStatement, List<string> lines) {
            lines.Add("if (");

            int start = context.DepthClass;
            ExpressionResult condResult = ProcessExpression(semantic, context, ifStatement.Condition, lines);
            context.PopClass(start);

            lines.Add(") {\n");

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

            return condResult;
        }

        protected override void ProcessThrowStatement(SemanticModel semantic, LayerContext context, ThrowStatementSyntax throwStatement, List<string> lines) {
            if (throwStatement.Expression == null) {
                lines.Add("throw new Error('Throw empty. TODO: Throw exception');\n");
            } else {
                lines.Add("throw ");
                ProcessExpression(semantic, context, throwStatement.Expression, lines);
                lines.Add(";\n");
            }
        }

        protected override void ProcessSwitchStatement(SemanticModel semantic, LayerContext context, SwitchStatementSyntax switchStatement, List<string> lines) {
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

        protected override void ProcessDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            List<string> lines
        ) {
            ProcessDeclaration(semantic, context, declaration, lines, false);
        }


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

            FunctionStack? fn = context.GetCurrentFunction();

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

        public override void ProcessArrowExpressionClause(SemanticModel semantic, LayerContext context, ArrowExpressionClauseSyntax arrowExpression, List<string> lines) {
            lines.Add(" = ");
            ProcessExpression(semantic, context, arrowExpression.Expression, lines);
        }

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




