using cs2.core;
using cs2.go.util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace cs2.go {
    /// <summary>
    /// Processes C# syntax nodes into Go source tokens.
    /// </summary>
    public class GoConversiorProcessor : ConversionProcessor {
        /// <summary>
        /// Holds the Go program metadata for type mappings.
        /// </summary>
        readonly GoProgram Program;

        /// <summary>
        /// Tracks Go import requirements during conversion.
        /// </summary>
        readonly GoImportTracker ImportTracker;

        /// <summary>
        /// Tracks helper usage required by emitted output.
        /// </summary>
        readonly GoHelperUsage HelperUsage;

        /// <summary>
        /// Maps static member calls to Go packages or helpers.
        /// </summary>
        readonly Dictionary<string, string> StaticMemberRemaps;

        /// <summary>
        /// Initializes a new Go conversion processor.
        /// </summary>
        /// <param name="program">The Go program metadata.</param>
        /// <param name="importTracker">The import tracker.</param>
        /// <param name="helperUsage">The helper usage tracker.</param>
        public GoConversiorProcessor(GoProgram program, GoImportTracker importTracker, GoHelperUsage helperUsage) {
            Program = program ?? throw new ArgumentNullException(nameof(program));
            ImportTracker = importTracker ?? throw new ArgumentNullException(nameof(importTracker));
            HelperUsage = helperUsage ?? throw new ArgumentNullException(nameof(helperUsage));

            StaticMemberRemaps = new Dictionary<string, string>(StringComparer.Ordinal) {
                { "Console.WriteLine", "fmt.Println" },
                { "Console.Write", "fmt.Print" },
                { "Math.Abs", "math.Abs" },
                { "Math.Round", "math.Round" },
                { "Math.Floor", "math.Floor" },
                { "Math.Ceiling", "math.Ceil" },
                { "Math.Sqrt", "math.Sqrt" },
                { "Math.Pow", "math.Pow" },
                { "Math.PI", "math.Pi" }
            };
        }

        /// <summary>
        /// Processes assignment expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="assignment">The assignment expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessAssignmentExpressionSyntax(SemanticModel semantic, LayerContext context, AssignmentExpressionSyntax assignment, List<string> lines) {
            int startDepth = context.Class.Count;
            ExpressionResult assignResult = ProcessExpression(semantic, context, assignment.Left, lines);
            context.PopClass(startDepth);

            string operatorVal = assignment.OperatorToken.ToString();
            lines.Add($" {operatorVal} ");

            startDepth = context.Class.Count;
            ProcessExpression(semantic, context, assignment.Right, lines);
            context.PopClass(startDepth);
        }

        /// <summary>
        /// Processes identifier expressions, including remaps and instance access.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="identifier">The identifier expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="refTypes">Optional reference types.</param>
        /// <returns>The expression result describing the identifier.</returns>
        protected override ExpressionResult ProcessIdentifierNameSyntax(SemanticModel semantic, LayerContext context, IdentifierNameSyntax identifier, List<string> lines, List<ExpressionResult> refTypes) {
            string name = identifier.ToString();

            ISymbol symbol = semantic.GetSymbolInfo(identifier).Symbol;
            if (symbol is INamespaceSymbol namespaceSymbol && namespaceSymbol.IsNamespace) {
                return new ExpressionResult(false);
            }

            ConversionClass currentClass = context.GetCurrentClass();
            FunctionStack currentFn = context.GetCurrentFunction();

            ConversionVariable functionInVar = currentFn?.Function?.InParameters?.Find(c => c.Name == name);
            ConversionVariable stackVar = currentFn?.Stack?.Find(c => c.Name == name);

            bool isLocal = functionInVar != null || stackVar != null;

            ConversionVariable classVar = currentClass?.Variables?.Find(c => c.Name == name);
            ConversionFunction classFn = currentClass?.Functions?.Find(c => c.Name == name || c.Remap == name);

            if (currentClass != null && !isLocal && context.GetClassLayer() == 1) {
                bool isClassMember = classVar != null || classFn != null;
                if (isClassMember && !(symbol is INamedTypeSymbol)) {
                    lines.Add("self.");
                }
            }

            if (classFn != null && !string.IsNullOrEmpty(classFn.RemapClass)) {
                lines.Add($"{classFn.RemapClass}.");
                TrackRemapImport(classFn.RemapClass);
                lines.Add(string.IsNullOrEmpty(classFn.Remap) ? name : classFn.Remap);
                return new ExpressionResult(true, VariablePath.Unknown, classFn.ReturnType) { Class = currentClass };
            }

            if (classVar != null && !string.IsNullOrEmpty(classVar.RemapClass)) {
                lines.Add($"{classVar.RemapClass}.");
                TrackRemapImport(classVar.RemapClass);
                lines.Add(string.IsNullOrEmpty(classVar.Remap) ? name : classVar.Remap);
                return new ExpressionResult(true, VariablePath.Unknown, classVar.VarType) { Class = currentClass, Variable = classVar };
            }

            string resolvedName = name;
            if (classVar != null && !string.IsNullOrEmpty(classVar.Remap)) {
                resolvedName = classVar.Remap;
            } else if (classFn != null && !string.IsNullOrEmpty(classFn.Remap)) {
                resolvedName = classFn.Remap;
            }

            lines.Add(resolvedName);
            VariableType returnType = classVar?.VarType ?? classFn?.ReturnType;
            return new ExpressionResult(true, VariablePath.Unknown, returnType) { Class = currentClass, Variable = classVar };
        }

        /// <summary>
        /// Processes object creation expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="objectCreation">The object creation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the object creation.</returns>
        protected override ExpressionResult ProcessObjectCreationExpressionSyntax(SemanticModel semantic, LayerContext context, ObjectCreationExpressionSyntax objectCreation, List<string> lines) {
            VariableType type = VariableUtil.GetVarType(objectCreation.Type, semantic);

            if (type.Type == VariableDataType.Array) {
                return ProcessArrayCreationExpression(semantic, context, objectCreation.Type as ArrayTypeSyntax, objectCreation.Initializer, lines, type);
            }

            if (type.Type == VariableDataType.List || type.TypeName == "List") {
                string elementType = ResolveGenericArg(type.GenericArgs, 0, "interface{}");
                lines.Add($"make([]{elementType}, 0)");
                return new ExpressionResult(true, VariablePath.Unknown, type);
            }

            if (type.Type == VariableDataType.Dictionary || type.TypeName == "Dictionary") {
                string keyType = ResolveGenericArg(type.GenericArgs, 0, "interface{}");
                string valueType = ResolveGenericArg(type.GenericArgs, 1, "interface{}");
                lines.Add($"make(map[{keyType}]{valueType})");
                return new ExpressionResult(true, VariablePath.Unknown, type);
            }

            string typeName = type.TypeName;
            if (Program.TypeMap.TryGetValue(typeName, out string mappedName)) {
                typeName = mappedName;
            }

            if (typeName.Contains(".") || typeName.Contains("[")) {
                typeName = type.TypeName;
            }

            string ctorName = $"New{typeName}";

            if (objectCreation.ArgumentList == null || objectCreation.ArgumentList.Arguments.Count == 0) {
                lines.Add($"&{typeName}{{}}");
                return new ExpressionResult(true, VariablePath.Unknown, type);
            }

            lines.Add($"{ctorName}(");
            AppendArgumentList(semantic, context, objectCreation.ArgumentList, lines);
            lines.Add(")");
            return new ExpressionResult(true, VariablePath.Unknown, type);
        }

        /// <summary>
        /// Processes member access expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="memberAccess">The member access expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="refTypes">Optional reference types.</param>
        /// <returns>The expression result describing the member access.</returns>
        protected override ExpressionResult ProcessMemberAccessExpressionSyntax(SemanticModel semantic, LayerContext context, MemberAccessExpressionSyntax memberAccess, List<string> lines, List<ExpressionResult> refTypes) {
            string memberName = memberAccess.Name.Identifier.Text;

            if (TryProcessLengthAccess(semantic, context, memberAccess, lines)) {
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("int"));
            }

            if (TryProcessStaticMemberRemap(memberAccess, lines)) {
                return new ExpressionResult(true, VariablePath.Static, null);
            }

            ISymbol memberSymbol = semantic.GetSymbolInfo(memberAccess).Symbol;
            if (memberSymbol is IMethodSymbol methodSymbol && methodSymbol.IsStatic) {
                string staticName = $"{methodSymbol.ContainingType.Name}_{memberSymbol.Name}";
                lines.Add(staticName);
                return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType(methodSymbol.ReturnType));
            }

            if (memberSymbol is IPropertySymbol propertySymbol && propertySymbol.IsStatic) {
                string staticName = $"{propertySymbol.ContainingType.Name}_{propertySymbol.Name}";
                lines.Add(staticName);
                return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType(propertySymbol.Type));
            }

            if (memberSymbol is IFieldSymbol fieldSymbol && fieldSymbol.IsStatic) {
                string staticName = $"{fieldSymbol.ContainingType.Name}_{fieldSymbol.Name}";
                lines.Add(staticName);
                return new ExpressionResult(true, VariablePath.Static, VariableUtil.GetVarType(fieldSymbol.Type));
            }

            List<string> leftLines = new List<string>();
            int startLeft = context.DepthClass;
            ExpressionResult leftResult = ProcessExpression(semantic, context, memberAccess.Expression, leftLines);
            context.PopClass(startLeft);

            lines.AddRange(leftLines);
            lines.Add(".");

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
            if (invocationExpression.Expression is IdentifierNameSyntax identifierName && identifierName.Identifier.Text == "nameof") {
                lines.Add($"\"{GetNameofValue(semantic, invocationExpression)}\"");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
            }

            if (TryProcessStaticInvocationRemap(semantic, context, invocationExpression, lines, out ExpressionResult remapResult)) {
                return remapResult;
            }

            List<string> exprLines = new List<string>();
            int startExpr = context.DepthClass;
            ExpressionResult exprResult = ProcessExpression(semantic, context, invocationExpression.Expression, exprLines);
            context.PopClass(startExpr);

            lines.AddRange(exprLines);
            lines.Add("(");
            AppendArgumentList(semantic, context, invocationExpression.ArgumentList, lines);
            lines.Add(")");

            return new ExpressionResult(true, exprResult.VarPath, exprResult.Type) { Class = exprResult.Class, Variable = exprResult.Variable };
        }

        /// <summary>
        /// Processes this expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="thisExpression">The this expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessThisExpressionSyntax(SemanticModel semantic, LayerContext context, ThisExpressionSyntax thisExpression, List<string> lines) {
            lines.Add("self");
        }

        /// <summary>
        /// Processes binary expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="binary">The binary expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the binary expression.</returns>
        protected override ExpressionResult ProcessBinaryExpressionSyntax(SemanticModel semantic, LayerContext context, BinaryExpressionSyntax binary, List<string> lines) {
            List<string> left;
            List<string> right;
            ExpressionResult result;
            BinaryOpTypes opType = ParseBinaryExpression(semantic, context, binary, out left, out right, out result);

            if (opType == BinaryOpTypes.Coalesce) {
                HelperUsage.NeedsTernary = true;
                lines.Add("ternary(");
                lines.AddRange(left);
                lines.Add(" != nil, ");
                lines.AddRange(left);
                lines.Add(", ");
                lines.AddRange(right);
                lines.Add(")");
                return new ExpressionResult(true, VariablePath.Unknown, result.Type);
            }

            if (opType == BinaryOpTypes.InstanceOf) {
                HelperUsage.NeedsTypeCheck = true;
                lines.Add("isType[");
                lines.AddRange(right);
                lines.Add("](");
                lines.AddRange(left);
                lines.Add(")");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("bool"));
            }

            if (opType == BinaryOpTypes.As) {
                lines.Add("(");
                lines.AddRange(left);
                lines.Add(").(");
                lines.AddRange(right);
                lines.Add(")");
                return new ExpressionResult(true, VariablePath.Unknown, result.Type);
            }

            string op = ResolveBinaryOperator(opType);
            lines.AddRange(left);
            lines.Add($" {op} ");
            lines.AddRange(right);

            return new ExpressionResult(true, VariablePath.Unknown, result.Type);
        }

        /// <summary>
        /// Processes generic name expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="generic">The generic name syntax.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessGenericNameSyntax(SemanticModel semantic, LayerContext context, GenericNameSyntax generic, List<string> lines) {
            lines.Add(generic.Identifier.Text);
            if (generic.TypeArgumentList != null && generic.TypeArgumentList.Arguments.Count > 0) {
                lines.Add("[");
                for (int i = 0; i < generic.TypeArgumentList.Arguments.Count; i++) {
                    TypeSyntax arg = generic.TypeArgumentList.Arguments[i];
                    VariableType varType = VariableUtil.GetVarType(arg, semantic);
                    lines.Add(varType.ToGoString(Program, ImportTracker));
                    if (i != generic.TypeArgumentList.Arguments.Count - 1) {
                        lines.Add(", ");
                    }
                }
                lines.Add("]");
            }
        }

        /// <summary>
        /// Processes implicit array creation expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="implicitArray">The implicit array creation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessImplicitArrayCreationExpression(SemanticModel semantic, LayerContext context, ImplicitArrayCreationExpressionSyntax implicitArray, List<string> lines) {
            IArrayTypeSymbol arrayType = semantic.GetTypeInfo(implicitArray).ConvertedType as IArrayTypeSymbol;
            string elementType = "interface{}";
            if (arrayType != null) {
                VariableType varType = VariableUtil.GetVarType(arrayType.ElementType);
                elementType = varType.ToGoString(Program, ImportTracker);
            }

            lines.Add($"[]{elementType}{{");
            AppendInitializerExpressions(semantic, context, implicitArray.Initializer, lines);
            lines.Add("}");
        }

        /// <summary>
        /// Processes await expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="awaitExpression">The await expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessAwait(SemanticModel semantic, LayerContext context, AwaitExpressionSyntax awaitExpression, List<string> lines) {
            lines.Add("<-");
            ProcessExpression(semantic, context, awaitExpression.Expression, lines);
        }

        /// <summary>
        /// Processes qualified names.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="qualifiedName">The qualified name syntax.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the qualified name.</returns>
        protected override ExpressionResult ProcessQualifiedName(SemanticModel semantic, LayerContext context, QualifiedNameSyntax qualifiedName, List<string> lines) {
            lines.Add(qualifiedName.Right.ToString());
            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType(qualifiedName.Right, semantic));
        }

        /// <summary>
        /// Processes typeof expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="typeOfExpression">The typeof expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessTypeOfExpression(SemanticModel semantic, LayerContext context, TypeOfExpressionSyntax typeOfExpression, List<string> lines) {
            VariableType varType = VariableUtil.GetVarType(typeOfExpression.Type, semantic);
            string typeName = varType.ToGoString(Program, ImportTracker);
            ImportTracker.AddImport("reflect");
            lines.Add($"reflect.TypeOf((*{typeName})(nil)).Elem()");
        }

        /// <summary>
        /// Processes simple lambda expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="simpleLambda">The lambda expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessSimpleLambdaExpression(SemanticModel semantic, LayerContext context, SimpleLambdaExpressionSyntax simpleLambda, List<string> lines) {
            EmitLambdaHeader(semantic, simpleLambda.Parameter, simpleLambda, lines);
            EmitLambdaBody(semantic, context, simpleLambda.Body, lines);
        }

        /// <summary>
        /// Processes array creation expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="arrayCreation">The array creation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the array creation.</returns>
        protected override ExpressionResult ProcessArrayCreationExpression(SemanticModel semantic, LayerContext context, ArrayCreationExpressionSyntax arrayCreation, List<string> lines) {
            VariableType varType = VariableUtil.GetVarType(arrayCreation.Type, semantic);
            ProcessArrayCreationExpression(semantic, context, arrayCreation.Type, arrayCreation.Initializer, lines, varType);
            return new ExpressionResult(true, VariablePath.Unknown, varType);
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
        /// Processes base expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="baseExpression">The base expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessBaseExpression(SemanticModel semantic, LayerContext context, BaseExpressionSyntax baseExpression, List<string> lines) {
            lines.Add("self");
        }

        /// <summary>
        /// Processes initializer expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="initializerExpression">The initializer expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessInitializerExpression(SemanticModel semantic, LayerContext context, InitializerExpressionSyntax initializerExpression, List<string> lines) {
            lines.Add("{");
            AppendInitializerExpressions(semantic, context, initializerExpression, lines);
            lines.Add("}");
        }

        /// <summary>
        /// Processes tuple expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="tupleExpression">The tuple expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessTupleExpression(SemanticModel semantic, LayerContext context, TupleExpressionSyntax tupleExpression, List<string> lines) {
            lines.Add("[]interface{}{");
            for (int i = 0; i < tupleExpression.Arguments.Count; i++) {
                ArgumentSyntax arg = tupleExpression.Arguments[i];
                ProcessExpression(semantic, context, arg.Expression, lines);
                if (i != tupleExpression.Arguments.Count - 1) {
                    lines.Add(", ");
                }
            }
            lines.Add("}");
        }

        /// <summary>
        /// Processes predefined types.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="predefinedType">The predefined type syntax.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessPredefinedType(SemanticModel semantic, LayerContext context, PredefinedTypeSyntax predefinedType, List<string> lines) {
            string type = predefinedType.Keyword.Text;
            string mapped = GoTypeMap.GetGoTypeName(type);
            lines.Add(mapped);
        }

        /// <summary>
        /// Processes default expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="defaultExpression">The default expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessDefaultExpression(SemanticModel semantic, LayerContext context, DefaultExpressionSyntax defaultExpression, List<string> lines) {
            VariableType varType = VariableUtil.GetVarType(defaultExpression.Type, semantic);
            string defaultValue = GetDefaultValue(varType);
            lines.Add(defaultValue);
        }

        /// <summary>
        /// Processes interpolated string expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="interpolatedString">The interpolated string.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the interpolated string.</returns>
        protected override ExpressionResult ProcessInterpolatedStringExpression(SemanticModel semantic, LayerContext context, InterpolatedStringExpressionSyntax interpolatedString, List<string> lines) {
            ImportTracker.AddImport("fmt");
            lines.Add("fmt.Sprintf(\"");

            List<ExpressionSyntax> arguments = new List<ExpressionSyntax>();

            foreach (var content in interpolatedString.Contents) {
                if (content is InterpolatedStringTextSyntax text) {
                    lines.Add(text.TextToken.ValueText.Replace("\"", "\\\""));
                } else if (content is InterpolationSyntax interpolation) {
                    lines.Add("%v");
                    arguments.Add(interpolation.Expression);
                }
            }

            lines.Add("\"");
            for (int i = 0; i < arguments.Count; i++) {
                lines.Add(", ");
                ProcessExpression(semantic, context, arguments[i], lines);
            }

            lines.Add(")");
            return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
        }

        /// <summary>
        /// Processes element access expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="elementAccess">The element access expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessElementAccessExpression(SemanticModel semantic, LayerContext context, ElementAccessExpressionSyntax elementAccess, List<string> lines) {
            ProcessExpression(semantic, context, elementAccess.Expression, lines);
            lines.Add("[");

            for (int i = 0; i < elementAccess.ArgumentList.Arguments.Count; i++) {
                ArgumentSyntax arg = elementAccess.ArgumentList.Arguments[i];
                ProcessExpression(semantic, context, arg.Expression, lines);
                if (i != elementAccess.ArgumentList.Arguments.Count - 1) {
                    lines.Add(", ");
                }
            }

            lines.Add("]");
        }

        /// <summary>
        /// Processes postfix unary expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="postfixUnary">The postfix unary expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessPostfixUnaryExpression(SemanticModel semantic, LayerContext context, PostfixUnaryExpressionSyntax postfixUnary, List<string> lines) {
            ProcessExpression(semantic, context, postfixUnary.Operand, lines);
            lines.Add(postfixUnary.OperatorToken.Text);
        }

        /// <summary>
        /// Processes prefix unary expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="prefixUnary">The prefix unary expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the prefix unary expression.</returns>
        protected override ExpressionResult ProcessPrefixUnaryExpression(SemanticModel semantic, LayerContext context, PrefixUnaryExpressionSyntax prefixUnary, List<string> lines) {
            lines.Add(prefixUnary.OperatorToken.Text);
            ProcessExpression(semantic, context, prefixUnary.Operand, lines);
            return new ExpressionResult(true, VariablePath.Unknown, null);
        }

        /// <summary>
        /// Processes member binding expressions (used in conditional access).
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="memberBinding">The member binding expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessMemberBindingExpression(SemanticModel semantic, LayerContext context, MemberBindingExpressionSyntax memberBinding, List<string> lines) {
            lines.Add(memberBinding.Name.Identifier.Text);
        }

        /// <summary>
        /// Processes conditional access expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="conditionalAccess">The conditional access expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessConditionalAccessExpression(SemanticModel semantic, LayerContext context, ConditionalAccessExpressionSyntax conditionalAccess, List<string> lines) {
            ProcessExpression(semantic, context, conditionalAccess.Expression, lines);
            lines.Add(".");
            ProcessExpression(semantic, context, conditionalAccess.WhenNotNull, lines);
        }

        /// <summary>
        /// Processes cast expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="castExpr">The cast expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the cast expression.</returns>
        protected override ExpressionResult ProcessCastExpression(SemanticModel semantic, LayerContext context, CastExpressionSyntax castExpr, List<string> lines) {
            VariableType varType = VariableUtil.GetVarType(castExpr.Type, semantic);
            string typeName = varType.ToGoString(Program, ImportTracker);
            lines.Add($"{typeName}(");
            ProcessExpression(semantic, context, castExpr.Expression, lines);
            lines.Add(")");
            return new ExpressionResult(true, VariablePath.Unknown, varType);
        }

        /// <summary>
        /// Processes conditional expressions (ternary).
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="conditional">The conditional expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessConditionalExpression(SemanticModel semantic, LayerContext context, ConditionalExpressionSyntax conditional, List<string> lines) {
            HelperUsage.NeedsTernary = true;
            lines.Add("ternary(");
            ProcessExpression(semantic, context, conditional.Condition, lines);
            lines.Add(", ");
            ProcessExpression(semantic, context, conditional.WhenTrue, lines);
            lines.Add(", ");
            ProcessExpression(semantic, context, conditional.WhenFalse, lines);
            lines.Add(")");
        }

        /// <summary>
        /// Processes parenthesized lambda expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="lambda">The lambda expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessLambdaExpression(SemanticModel semantic, LayerContext context, ParenthesizedLambdaExpressionSyntax lambda, List<string> lines) {
            EmitLambdaHeader(semantic, lambda.ParameterList.Parameters, lambda, lines);
            EmitLambdaBody(semantic, context, lambda.Body, lines);
        }

        /// <summary>
        /// Processes return statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="ret">The return statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessReturnStatement(SemanticModel semantic, LayerContext context, ReturnStatementSyntax ret, List<string> lines) {
            if (ret.Expression == null) {
                lines.Add("return\n");
                return;
            }

            lines.Add("return ");
            ProcessExpression(semantic, context, ret.Expression, lines);
            lines.Add("\n");
        }

        /// <summary>
        /// Processes empty statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="emptyStatement">The empty statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessEmptyStatement(SemanticModel semantic, LayerContext context, EmptyStatementSyntax emptyStatement, List<string> lines) {
            lines.Add("\n");
        }

        /// <summary>
        /// Processes do-while statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="doStatement">The do statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessDoStatement(SemanticModel semantic, LayerContext context, DoStatementSyntax doStatement, List<string> lines) {
            lines.Add("for {\n");
            ProcessStatement(semantic, context, doStatement.Statement, lines, 1);
            lines.Add("\tif !(");
            ProcessExpression(semantic, context, doStatement.Condition, lines);
            lines.Add(") {\n\t\tbreak\n\t}\n}\n");
        }

        /// <summary>
        /// Processes using statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="usingStatement">The using statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessUsingStatement(SemanticModel semantic, LayerContext context, UsingStatementSyntax usingStatement, List<string> lines) {
            if (usingStatement.Declaration != null) {
                ProcessDeclaration(semantic, context, usingStatement.Declaration, lines);
                if (usingStatement.Declaration.Variables.Count > 0) {
                    string name = usingStatement.Declaration.Variables[0].Identifier.Text;
                    lines.Add($"defer {name}.Dispose()\n");
                }
            } else if (usingStatement.Expression != null) {
                lines.Add("// TODO: using statement\n");
            }

            ProcessStatement(semantic, context, usingStatement.Statement, lines, 1);
        }

        /// <summary>
        /// Processes lock statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="lockStatement">The lock statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessLockStatement(SemanticModel semantic, LayerContext context, LockStatementSyntax lockStatement, List<string> lines) {
            List<string> exprLines = new List<string>();
            ProcessExpression(semantic, context, lockStatement.Expression, exprLines);
            string expr = JoinTokens(exprLines);
            lines.Add($"{expr}.Lock()\n");
            lines.Add($"defer {expr}.Unlock()\n");
            ProcessStatement(semantic, context, lockStatement.Statement, lines, 1);
        }

        /// <summary>
        /// Processes try statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="tryStatement">The try statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessTryStatement(SemanticModel semantic, LayerContext context, TryStatementSyntax tryStatement, List<string> lines) {
            lines.Add("// TODO: try/catch not directly supported in Go\n");
            ProcessBlock(semantic, context, tryStatement.Block, lines);
        }

        /// <summary>
        /// Processes foreach statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="forEachStatement">The foreach statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessForEachStatement(SemanticModel semantic, LayerContext context, ForEachStatementSyntax forEachStatement, List<string> lines) {
            string identifier = forEachStatement.Identifier.Text;
            lines.Add($"for _, {identifier} := range ");
            ProcessExpression(semantic, context, forEachStatement.Expression, lines);
            lines.Add(" {\n");
            ProcessStatement(semantic, context, forEachStatement.Statement, lines, 1);
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
            lines.Add("continue\n");
        }

        /// <summary>
        /// Processes while statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="whileStatement">The while statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessWhileStatement(SemanticModel semantic, LayerContext context, WhileStatementSyntax whileStatement, List<string> lines) {
            lines.Add("for ");
            ProcessExpression(semantic, context, whileStatement.Condition, lines);
            lines.Add(" {\n");
            ProcessStatement(semantic, context, whileStatement.Statement, lines, 1);
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
            lines.Add("for ");

            string init = BuildForInitializer(semantic, context, forStatement);
            string condition = BuildForCondition(semantic, context, forStatement);
            string increment = BuildForIncrement(semantic, context, forStatement);

            lines.Add(init);
            lines.Add("; ");
            lines.Add(condition);
            lines.Add("; ");
            lines.Add(increment);
            lines.Add(" {\n");

            ProcessStatement(semantic, context, forStatement.Statement, lines, 1);
            lines.Add("}\n");
        }

        /// <summary>
        /// Processes if statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="ifStatement">The if statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the if statement.</returns>
        protected override ExpressionResult ProcessIfStatement(SemanticModel semantic, LayerContext context, IfStatementSyntax ifStatement, List<string> lines) {
            lines.Add("if ");
            ProcessExpression(semantic, context, ifStatement.Condition, lines);
            lines.Add(" {\n");
            ProcessStatement(semantic, context, ifStatement.Statement, lines, 1);
            lines.Add("}\n");

            if (ifStatement.Else != null) {
                if (ifStatement.Else.Statement is IfStatementSyntax elseIf) {
                    lines.Add("else ");
                    ProcessIfStatement(semantic, context, elseIf, lines);
                } else {
                    lines.Add("else {\n");
                    ProcessStatement(semantic, context, ifStatement.Else.Statement, lines, 1);
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
            lines.Add("panic(");
            if (throwStatement.Expression != null) {
                ProcessExpression(semantic, context, throwStatement.Expression, lines);
            }
            lines.Add(")\n");
        }

        /// <summary>
        /// Processes switch statements.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="switchStatement">The switch statement.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessSwitchStatement(SemanticModel semantic, LayerContext context, SwitchStatementSyntax switchStatement, List<string> lines) {
            lines.Add("switch ");
            ProcessExpression(semantic, context, switchStatement.Expression, lines);
            lines.Add(" {\n");

            foreach (SwitchSectionSyntax section in switchStatement.Sections) {
                foreach (SwitchLabelSyntax label in section.Labels) {
                    if (label is CaseSwitchLabelSyntax caseLabel) {
                        lines.Add("case ");
                        ProcessExpression(semantic, context, caseLabel.Value, lines);
                        lines.Add(":\n");
                    } else if (label is DefaultSwitchLabelSyntax) {
                        lines.Add("default:\n");
                    }
                }

                foreach (StatementSyntax statement in section.Statements) {
                    ProcessStatement(semantic, context, statement, lines, 1);
                }
            }

            lines.Add("}\n");
        }

        /// <summary>
        /// Processes variable declarations.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="declaration">The variable declaration.</param>
        /// <param name="lines">The output lines to append to.</param>
        protected override void ProcessDeclaration(SemanticModel semantic, LayerContext context, VariableDeclarationSyntax declaration, List<string> lines) {
            VariableType varType = VariableUtil.GetVarType(declaration.Type, semantic);
            string typeName = varType.ToGoString(Program, ImportTracker);

            foreach (VariableDeclaratorSyntax variable in declaration.Variables) {
                string name = variable.Identifier.Text;

                ConversionVariable stackVar = new ConversionVariable();
                stackVar.Name = name;
                stackVar.VarType = varType;
                context.GetCurrentFunction()?.Stack.Add(stackVar);

                if (variable.Initializer != null) {
                    lines.Add($"{name} := ");
                    ProcessExpression(semantic, context, variable.Initializer.Value, lines);
                    lines.Add("\n");
                } else {
                    lines.Add($"var {name} {typeName}\n");
                }
            }
        }

        /// <summary>
        /// Processes literal expressions.
        /// </summary>
        /// <param name="context">The active conversion context.</param>
        /// <param name="literalExpression">The literal expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the literal.</returns>
        protected override ExpressionResult ProcessLiteralExpression(LayerContext context, LiteralExpressionSyntax literalExpression, List<string> lines) {
            if (literalExpression.IsKind(SyntaxKind.NullLiteralExpression)) {
                lines.Add("nil");
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("object"));
            }

            if (literalExpression.IsKind(SyntaxKind.StringLiteralExpression)) {
                lines.Add(literalExpression.Token.Text);
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("string"));
            }

            if (literalExpression.IsKind(SyntaxKind.CharacterLiteralExpression)) {
                lines.Add(literalExpression.Token.Text);
                return new ExpressionResult(true, VariablePath.Unknown, VariableUtil.GetVarType("char"));
            }

            lines.Add(literalExpression.Token.Text);
            return new ExpressionResult(true);
        }

        /// <summary>
        /// Processes arrow expression clauses.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="arrowExpression">The arrow expression clause.</param>
        /// <param name="lines">The output lines to append to.</param>
        public override void ProcessArrowExpressionClause(SemanticModel semantic, LayerContext context, ArrowExpressionClauseSyntax arrowExpression, List<string> lines) {
            lines.Add("return ");
            ProcessExpression(semantic, context, arrowExpression.Expression, lines);
            lines.Add("\n");
        }

        /// <summary>
        /// Processes declaration expressions (out var patterns).
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="declaration">The declaration expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>The expression result describing the declaration.</returns>
        protected override ExpressionResult ProcessDeclarationExpressionSyntax(SemanticModel semantic, LayerContext context, DeclarationExpressionSyntax declaration, List<string> lines) {
            if (declaration.Designation is SingleVariableDesignationSyntax designation) {
                string name = designation.Identifier.Text;
                lines.Add(name);

                VariableType varType = VariableUtil.GetVarType(declaration.Type, semantic);
                ConversionVariable stackVar = new ConversionVariable();
                stackVar.Name = name;
                stackVar.VarType = varType;
                context.GetCurrentFunction()?.Stack.Add(stackVar);

                return new ExpressionResult(true, VariablePath.Unknown, varType) { Variable = stackVar };
            }

            return new ExpressionResult(false);
        }

        /// <summary>
        /// Builds a for-loop initializer segment.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="forStatement">The for statement.</param>
        /// <returns>The initializer segment string.</returns>
        string BuildForInitializer(SemanticModel semantic, LayerContext context, ForStatementSyntax forStatement) {
            if (forStatement.Declaration != null) {
                List<string> tokens = new List<string>();
                ProcessDeclaration(semantic, context, forStatement.Declaration, tokens);
                return TrimStatementTokens(tokens);
            }

            if (forStatement.Initializers.Count > 0) {
                List<string> tokens = new List<string>();
                for (int i = 0; i < forStatement.Initializers.Count; i++) {
                    ProcessExpression(semantic, context, forStatement.Initializers[i], tokens);
                    if (i != forStatement.Initializers.Count - 1) {
                        tokens.Add(", ");
                    }
                }
                return JoinTokens(tokens);
            }

            return string.Empty;
        }

        /// <summary>
        /// Builds a for-loop condition segment.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="forStatement">The for statement.</param>
        /// <returns>The condition segment string.</returns>
        string BuildForCondition(SemanticModel semantic, LayerContext context, ForStatementSyntax forStatement) {
            if (forStatement.Condition == null) {
                return string.Empty;
            }

            List<string> tokens = new List<string>();
            ProcessExpression(semantic, context, forStatement.Condition, tokens);
            return JoinTokens(tokens);
        }

        /// <summary>
        /// Builds a for-loop increment segment.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="forStatement">The for statement.</param>
        /// <returns>The increment segment string.</returns>
        string BuildForIncrement(SemanticModel semantic, LayerContext context, ForStatementSyntax forStatement) {
            if (forStatement.Incrementors.Count == 0) {
                return string.Empty;
            }

            List<string> tokens = new List<string>();
            for (int i = 0; i < forStatement.Incrementors.Count; i++) {
                ProcessExpression(semantic, context, forStatement.Incrementors[i], tokens);
                if (i != forStatement.Incrementors.Count - 1) {
                    tokens.Add(", ");
                }
            }

            return JoinTokens(tokens);
        }

        /// <summary>
        /// Attempts to emit Go's len() for length-like properties.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="memberAccess">The member access expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>True when the access was rewritten.</returns>
        bool TryProcessLengthAccess(SemanticModel semantic, LayerContext context, MemberAccessExpressionSyntax memberAccess, List<string> lines) {
            string memberName = memberAccess.Name.Identifier.Text;
            if (memberName != "Length" && memberName != "Count") {
                return false;
            }

            ITypeSymbol typeSymbol = semantic.GetTypeInfo(memberAccess.Expression).Type;
            if (typeSymbol == null) {
                return false;
            }

            bool isLengthType = typeSymbol.TypeKind == TypeKind.Array ||
                typeSymbol.SpecialType == SpecialType.System_String ||
                typeSymbol.Name == "List" ||
                typeSymbol.Name == "Dictionary";

            if (!isLengthType) {
                return false;
            }

            lines.Add("len(");
            ProcessExpression(semantic, context, memberAccess.Expression, lines);
            lines.Add(")");
            return true;
        }

        /// <summary>
        /// Attempts to emit static member remaps without arguments.
        /// </summary>
        /// <param name="memberAccess">The member access expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <returns>True when the access was remapped.</returns>
        bool TryProcessStaticMemberRemap(MemberAccessExpressionSyntax memberAccess, List<string> lines) {
            if (memberAccess.Expression is IdentifierNameSyntax leftName) {
                string key = $"{leftName.Identifier.Text}.{memberAccess.Name.Identifier.Text}";
                if (StaticMemberRemaps.TryGetValue(key, out string remap)) {
                    TrackRemapImport(remap);
                    lines.Add(remap);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to emit static member remaps with arguments.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="invocation">The invocation expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="result">The expression result describing the invocation.</param>
        /// <returns>True when a remap was applied.</returns>
        bool TryProcessStaticInvocationRemap(SemanticModel semantic, LayerContext context, InvocationExpressionSyntax invocation, List<string> lines, out ExpressionResult result) {
            result = new ExpressionResult(false);

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is IdentifierNameSyntax leftName) {
                string key = $"{leftName.Identifier.Text}.{memberAccess.Name.Identifier.Text}";
                if (StaticMemberRemaps.TryGetValue(key, out string remap)) {
                    TrackRemapImport(remap);
                    lines.Add(remap);
                    lines.Add("(");
                    AppendArgumentList(semantic, context, invocation.ArgumentList, lines);
                    lines.Add(")");
                    result = new ExpressionResult(true, VariablePath.Static, null);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Appends a list of arguments to the output tokens.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="argumentList">The argument list to emit.</param>
        /// <param name="lines">The output lines to append to.</param>
        void AppendArgumentList(SemanticModel semantic, LayerContext context, ArgumentListSyntax argumentList, List<string> lines) {
            if (argumentList == null || argumentList.Arguments.Count == 0) {
                return;
            }

            for (int i = 0; i < argumentList.Arguments.Count; i++) {
                ArgumentSyntax arg = argumentList.Arguments[i];
                if (arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) {
                    lines.Add("&");
                }

                ProcessExpression(semantic, context, arg.Expression, lines);
                if (i != argumentList.Arguments.Count - 1) {
                    lines.Add(", ");
                }
            }
        }

        /// <summary>
        /// Appends initializer expression values.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="initializer">The initializer expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        void AppendInitializerExpressions(SemanticModel semantic, LayerContext context, InitializerExpressionSyntax initializer, List<string> lines) {
            if (initializer == null) {
                return;
            }

            for (int i = 0; i < initializer.Expressions.Count; i++) {
                ProcessExpression(semantic, context, initializer.Expressions[i], lines);
                if (i != initializer.Expressions.Count - 1) {
                    lines.Add(", ");
                }
            }
        }

        /// <summary>
        /// Resolves an operator string for a binary operator type.
        /// </summary>
        /// <param name="opType">The binary operator type.</param>
        /// <returns>The Go operator string.</returns>
        static string ResolveBinaryOperator(BinaryOpTypes opType) {
            return opType switch {
                BinaryOpTypes.Plus => "+",
                BinaryOpTypes.Minus => "-",
                BinaryOpTypes.Divide => "/",
                BinaryOpTypes.Multiply => "*",
                BinaryOpTypes.Modulo => "%",
                BinaryOpTypes.GreaterThan => ">",
                BinaryOpTypes.GreaterThanOrEqual => ">=",
                BinaryOpTypes.LessThan => "<",
                BinaryOpTypes.LessThanOrEqual => "<=",
                BinaryOpTypes.Equal => "==",
                BinaryOpTypes.NotEqual => "!=",
                BinaryOpTypes.BinAnd => "&&",
                BinaryOpTypes.BinOr => "||",
                BinaryOpTypes.BitwiseAnd => "&",
                BinaryOpTypes.BitwiseOr => "|",
                BinaryOpTypes.ExclusiveOr => "^",
                BinaryOpTypes.LeftShift => "<<",
                BinaryOpTypes.RightShift => ">>",
                _ => ""
            };
        }

        /// <summary>
        /// Builds the default value for a variable type.
        /// </summary>
        /// <param name="varType">The variable type.</param>
        /// <returns>The Go default value expression.</returns>
        string GetDefaultValue(VariableType varType) {
            switch (varType.Type) {
                case VariableDataType.Boolean:
                    return "false";
                case VariableDataType.String:
                    return "\"\"";
                case VariableDataType.Char:
                    return "'\\0'";
                case VariableDataType.Int8:
                case VariableDataType.Int16:
                case VariableDataType.Int32:
                case VariableDataType.Int64:
                case VariableDataType.UInt8:
                case VariableDataType.UInt16:
                case VariableDataType.UInt32:
                case VariableDataType.UInt64:
                case VariableDataType.Single:
                case VariableDataType.Double:
                    return "0";
                default:
                    return "nil";
            }
        }

        /// <summary>
        /// Resolves a generic argument type as a Go string.
        /// </summary>
        /// <param name="args">The generic argument list.</param>
        /// <param name="index">The index to resolve.</param>
        /// <param name="fallback">The fallback type when missing.</param>
        /// <returns>The Go type string.</returns>
        string ResolveGenericArg(List<VariableType> args, int index, string fallback) {
            if (args == null || args.Count <= index) {
                return fallback;
            }

            return args[index].ToGoString(Program, ImportTracker);
        }

        /// <summary>
        /// Emits a lambda header for simple or parenthesized lambda expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="parameter">The lambda parameter.</param>
        /// <param name="lambdaExpression">The lambda expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        void EmitLambdaHeader(SemanticModel semantic, ParameterSyntax parameter, LambdaExpressionSyntax lambdaExpression, List<string> lines) {
            lines.Add("func(");
            if (parameter != null) {
                string name = parameter.Identifier.Text;
                string typeName = ResolveLambdaParameterType(semantic, lambdaExpression, parameter) ?? "interface{}";
                lines.Add($"{name} {typeName}");
            }
            lines.Add(") ");
            string returnType = ResolveLambdaReturnType(semantic, lambdaExpression);
            if (!string.IsNullOrWhiteSpace(returnType)) {
                lines.Add(returnType + " ");
            }
        }

        /// <summary>
        /// Emits a lambda header for parenthesized lambda expressions.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="parameters">The lambda parameters.</param>
        /// <param name="lambdaExpression">The lambda expression.</param>
        /// <param name="lines">The output lines to append to.</param>
        void EmitLambdaHeader(SemanticModel semantic, SeparatedSyntaxList<ParameterSyntax> parameters, LambdaExpressionSyntax lambdaExpression, List<string> lines) {
            lines.Add("func(");
            for (int i = 0; i < parameters.Count; i++) {
                ParameterSyntax param = parameters[i];
                string name = param.Identifier.Text;
                string typeName = ResolveLambdaParameterType(semantic, lambdaExpression, param) ?? "interface{}";
                lines.Add($"{name} {typeName}");
                if (i != parameters.Count - 1) {
                    lines.Add(", ");
                }
            }
            lines.Add(") ");
            string returnType = ResolveLambdaReturnType(semantic, lambdaExpression);
            if (!string.IsNullOrWhiteSpace(returnType)) {
                lines.Add(returnType + " ");
            }
        }

        /// <summary>
        /// Emits the lambda body.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="body">The lambda body.</param>
        /// <param name="lines">The output lines to append to.</param>
        void EmitLambdaBody(SemanticModel semantic, LayerContext context, CSharpSyntaxNode body, List<string> lines) {
            lines.Add("{\n");
            if (body is BlockSyntax block) {
                ProcessBlock(semantic, context, block, lines);
            } else if (body is ExpressionSyntax expr) {
                lines.Add("\treturn ");
                ProcessExpression(semantic, context, expr, lines);
                lines.Add("\n");
            }
            lines.Add("}");
        }

        /// <summary>
        /// Resolves the lambda return type, if possible.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="lambdaExpression">The lambda expression.</param>
        /// <returns>The Go return type name or null.</returns>
        string ResolveLambdaReturnType(SemanticModel semantic, LambdaExpressionSyntax lambdaExpression) {
            INamedTypeSymbol convertedType = semantic.GetTypeInfo(lambdaExpression).ConvertedType as INamedTypeSymbol;
            if (convertedType == null || convertedType.DelegateInvokeMethod == null) {
                return string.Empty;
            }

            ITypeSymbol returnType = convertedType.DelegateInvokeMethod.ReturnType;
            if (returnType == null || returnType.SpecialType == SpecialType.System_Void) {
                return string.Empty;
            }

            VariableType varType = VariableUtil.GetVarType(returnType);
            return varType.ToGoString(Program, ImportTracker);
        }

        /// <summary>
        /// Resolves a lambda parameter type, if possible.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="lambdaExpression">The lambda expression.</param>
        /// <param name="parameter">The parameter to resolve.</param>
        /// <returns>The Go type name or null.</returns>
        string ResolveLambdaParameterType(SemanticModel semantic, LambdaExpressionSyntax lambdaExpression, ParameterSyntax parameter) {
            if (parameter.Type != null) {
                VariableType varType = VariableUtil.GetVarType(parameter.Type, semantic);
                return varType.ToGoString(Program, ImportTracker);
            }

            INamedTypeSymbol convertedType = semantic.GetTypeInfo(lambdaExpression).ConvertedType as INamedTypeSymbol;
            if (convertedType == null || convertedType.DelegateInvokeMethod == null) {
                return null;
            }

            var parameters = convertedType.DelegateInvokeMethod.Parameters;
            for (int i = 0; i < parameters.Length; i++) {
                if (parameters[i].Name == parameter.Identifier.Text) {
                    VariableType varType = VariableUtil.GetVarType(parameters[i].Type);
                    return varType.ToGoString(Program, ImportTracker);
                }
            }

            return null;
        }

        /// <summary>
        /// Builds a Go representation for an array creation expression.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="context">The active conversion context.</param>
        /// <param name="arrayTypeSyntax">The array type syntax.</param>
        /// <param name="initializer">The initializer, if any.</param>
        /// <param name="lines">The output lines to append to.</param>
        /// <param name="varType">The resolved variable type.</param>
        /// <returns>The expression result describing the array creation.</returns>
        ExpressionResult ProcessArrayCreationExpression(SemanticModel semantic, LayerContext context, ArrayTypeSyntax arrayTypeSyntax, InitializerExpressionSyntax initializer, List<string> lines, VariableType varType) {
            string elementType = varType.GenericArgs.Count > 0
                ? varType.GenericArgs[0].ToGoString(Program, ImportTracker)
                : "interface{}";

            if (initializer != null) {
                lines.Add($"[]{elementType}{{");
                AppendInitializerExpressions(semantic, context, initializer, lines);
                lines.Add("}");
                return new ExpressionResult(true, VariablePath.Unknown, varType);
            }

            string size = "0";
            if (arrayTypeSyntax != null && arrayTypeSyntax.RankSpecifiers.Count > 0) {
                var sizes = arrayTypeSyntax.RankSpecifiers[0].Sizes;
                if (sizes.Count > 0) {
                    List<string> sizeTokens = new List<string>();
                    ProcessExpression(semantic, context, sizes[0], sizeTokens);
                    size = JoinTokens(sizeTokens);
                }
            }

            lines.Add($"make([]{elementType}, {size})");
            return new ExpressionResult(true, VariablePath.Unknown, varType);
        }

        /// <summary>
        /// Resolves the nameof() value as a string.
        /// </summary>
        /// <param name="semantic">The semantic model for the current document.</param>
        /// <param name="invocationExpression">The invocation expression.</param>
        /// <returns>The resolved name.</returns>
        static string GetNameofValue(SemanticModel semantic, InvocationExpressionSyntax invocationExpression) {
            if (invocationExpression.ArgumentList.Arguments.Count == 0) {
                return string.Empty;
            }

            ExpressionSyntax expr = invocationExpression.ArgumentList.Arguments[0].Expression;
            if (expr is IdentifierNameSyntax identifier) {
                return identifier.Identifier.Text;
            }

            return expr.ToString();
        }

        /// <summary>
        /// Joins token lists into a single string.
        /// </summary>
        /// <param name="tokens">The token list.</param>
        /// <returns>The concatenated string.</returns>
        static string JoinTokens(List<string> tokens) {
            return tokens == null ? string.Empty : string.Join(string.Empty, tokens);
        }

        /// <summary>
        /// Trims trailing statement markers from tokens.
        /// </summary>
        /// <param name="tokens">The token list.</param>
        /// <returns>The trimmed statement string.</returns>
        static string TrimStatementTokens(List<string> tokens) {
            string value = JoinTokens(tokens);
            value = value.Replace("\n", "");
            if (value.EndsWith(";", StringComparison.Ordinal)) {
                value = value.Substring(0, value.Length - 1);
            }
            return value.Trim();
        }

        /// <summary>
        /// Tracks imports required by a remapped static member.
        /// </summary>
        /// <param name="remap">The remap target string.</param>
        void TrackRemapImport(string remap) {
            if (string.IsNullOrWhiteSpace(remap)) {
                return;
            }

            int dotIndex = remap.IndexOf('.');
            if (dotIndex <= 0) {
                return;
            }

            string alias = remap.Substring(0, dotIndex);
            if (Program.TryGetPackageImport(alias, out string importPath)) {
                ImportTracker.AddImport(importPath, alias);
            } else {
                ImportTracker.AddImport(alias);
            }
        }
    }
}
