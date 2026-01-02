using cs2.core;
using cs2.ts.util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace cs2.ts {
    /// <summary>
    /// Emits TypeScript class, interface, enum, and delegate declarations.
    /// </summary>
    public class TypeScriptClassEmitter {
        /// <summary>
        /// Holds the conversion processor used for syntax emission.
        /// </summary>
        readonly TypeScriptConversiorProcessor Conversion;

        /// <summary>
        /// Holds the conversion program metadata.
        /// </summary>
        readonly ConversionProgram Program;

        /// <summary>
        /// Holds the TypeScript program metadata.
        /// </summary>
        readonly TypeScriptProgram TypeScriptProgram;

        /// <summary>
        /// Holds the conversion options for emission.
        /// </summary>
        readonly TypeScriptConversionOptions ConversionOptions;

        /// <summary>
        /// Tracks reflection import requirements.
        /// </summary>
        readonly TypeScriptReflectionImportTracker ReflectionTracker;

        /// <summary>
        /// Holds the variable sorter delegate, if provided.
        /// </summary>
        readonly Action<ConversionClass> SortVariablesAction;

        /// <summary>
        /// Initializes a new class emitter with the required dependencies.
        /// </summary>
        /// <param name="conversion">The conversion processor used to render syntax.</param>
        /// <param name="program">The conversion program.</param>
        /// <param name="typeScriptProgram">The TypeScript program metadata.</param>
        /// <param name="options">The conversion options.</param>
        /// <param name="reflectionTracker">The reflection import tracker.</param>
        /// <param name="sortVariablesAction">Optional variable sorter.</param>
        public TypeScriptClassEmitter(
            TypeScriptConversiorProcessor conversion,
            ConversionProgram program,
            TypeScriptProgram typeScriptProgram,
            TypeScriptConversionOptions options,
            TypeScriptReflectionImportTracker reflectionTracker,
            Action<ConversionClass> sortVariablesAction) {
            Conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
            Program = program ?? throw new ArgumentNullException(nameof(program));
            TypeScriptProgram = typeScriptProgram ?? throw new ArgumentNullException(nameof(typeScriptProgram));
            ConversionOptions = options ?? throw new ArgumentNullException(nameof(options));
            ReflectionTracker = reflectionTracker ?? throw new ArgumentNullException(nameof(reflectionTracker));
            SortVariablesAction = sortVariablesAction;
        }

        /// <summary>
        /// Emits the declaration for the provided conversion class.
        /// </summary>
        /// <param name="cl">The class to emit.</param>
        /// <param name="writer">The output writer.</param>
        public void EmitClass(ConversionClass cl, TypeScriptOutputWriter writer) {
            if (cl == null || writer == null) {
                return;
            }

            if (cl.IsNative) {
                return;
            }

            INamedTypeSymbol typeSymbol = cl.TypeSymbol as INamedTypeSymbol;

            bool emitReflection = ConversionOptions.Reflection.EnableReflection && typeSymbol != null;
            bool emitStaticReflection = emitReflection && ConversionOptions.Reflection.UseStaticReflectionCache;
            bool emitTrailingReflection = emitReflection && !ConversionOptions.Reflection.UseStaticReflectionCache;

            string implements;
            string extends;
            TypeScriptUtils.GetInheritance(Program, cl, out implements, out extends);

            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                writer.WriteLine($"export interface {cl.Name}{extends} {{");
            } else if (cl.DeclarationType == MemberDeclarationType.Abstract) {
                writer.WriteLine($"export abstract class {cl.Name}{extends} {{");
            } else if (cl.DeclarationType == MemberDeclarationType.Delegate) {
                EmitDelegate(cl, writer, emitReflection, typeSymbol);
                return;
            } else if (cl.DeclarationType == MemberDeclarationType.Enum) {
                EmitEnum(cl, writer, emitReflection, typeSymbol);
                return;
            } else {
                if (cl.GenericArgs != null && cl.GenericArgs.Count > 0) {
                    string generics = string.Join(", ", cl.GenericArgs);
                    writer.WriteLine($"export class {cl.Name}<{generics}>{extends}{implements} {{");
                } else {
                    writer.WriteLine($"export class {cl.Name}{extends}{implements} {{");
                }
            }

            if (SortVariablesAction != null) {
                SortVariablesAction(cl);
            }

            EmitVariables(cl, writer);

            if (emitStaticReflection && cl.DeclarationType != MemberDeclarationType.Interface &&
                cl.DeclarationType != MemberDeclarationType.Delegate &&
                cl.DeclarationType != MemberDeclarationType.Enum) {
                TypeScriptReflectionEmitter.EmitPrivateStaticReflectionField(writer.Writer, typeSymbol, cl.Name, ConversionOptions.Reflection);
                ReflectionTracker.NeedsTypeImport = true;
                writer.WriteLine();
            }

            EmitStaticConstructor(cl, writer);
            EmitConstructors(cl, writer);
            EmitFunctions(cl, writer);

            writer.WriteLine("}");
            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                if (emitReflection && typeSymbol != null) {
                    TypeScriptReflectionEmitter.EmitInterfaceNamespaceReflection(writer.Writer, typeSymbol, cl.Name, ConversionOptions.Reflection);
                    ReflectionTracker.NeedsMetadataImport = true;
                }
            } else if (emitTrailingReflection && typeSymbol != null) {
                TypeScriptReflectionEmitter.EmitRegisterForType(writer.Writer, typeSymbol, cl.Name, ConversionOptions.Reflection.RegisterTypeIdent);
                ReflectionTracker.NeedsTypeImport = true;
            }
            writer.WriteLine();
        }

        /// <summary>
        /// Emits delegate declarations.
        /// </summary>
        /// <param name="cl">The delegate class.</param>
        /// <param name="writer">The output writer.</param>
        /// <param name="emitReflection">Whether reflection metadata should be emitted.</param>
        /// <param name="typeSymbol">The Roslyn type symbol.</param>
        void EmitDelegate(ConversionClass cl, TypeScriptOutputWriter writer, bool emitReflection, INamedTypeSymbol typeSymbol) {
            ConversionFunction del = cl.Functions[0];
            string generic = del.GetGenericArguments();

            writer.Write($"export type {del.Remap}{generic} = (");

            for (int k = 0; k < del.InParameters.Count; k++) {
                var param = del.InParameters[k];
                string type = param.VarType.ToTypeScriptString(TypeScriptProgram);
                string def = param.DefaultValue;
                if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                    def = "";
                }

                writer.Write($"{param.Name}: {type}{def}");

                if (k != del.InParameters.Count - 1) {
                    writer.Write(", ");
                }
            }

            writer.Write(") => ");

            if (del.ReturnType == null) {
                writer.Write("void");
            } else {
                writer.Write(del.ReturnType.ToTypeScriptString(TypeScriptProgram));
            }

            writer.WriteLine(";");
            if (emitReflection && typeSymbol != null) {
                writer.WriteLine();
                TypeScriptReflectionEmitter.EmitInterfaceNamespaceReflection(writer.Writer, typeSymbol, del.Remap, ConversionOptions.Reflection);
                ReflectionTracker.NeedsMetadataImport = true;
            }
            writer.WriteLine();
        }

        /// <summary>
        /// Emits enum declarations.
        /// </summary>
        /// <param name="cl">The enum class.</param>
        /// <param name="writer">The output writer.</param>
        /// <param name="emitReflection">Whether reflection metadata should be emitted.</param>
        /// <param name="typeSymbol">The Roslyn type symbol.</param>
        void EmitEnum(ConversionClass cl, TypeScriptOutputWriter writer, bool emitReflection, INamedTypeSymbol typeSymbol) {
            writer.WriteLine($"export enum {cl.Name} {{");
            if (cl.EnumMembers != null) {
                for (int j = 0; j < cl.EnumMembers.Count; j++) {
                    if (j == cl.EnumMembers.Count - 1) {
                        writer.WriteLine(cl.EnumMembers[j].ToString());
                    } else {
                        writer.WriteLine($"{cl.EnumMembers[j]},");
                    }
                }
            }
            writer.WriteLine("}");
            if (emitReflection && typeSymbol != null) {
                TypeScriptReflectionEmitter.EmitEnumNamespaceReflection(writer.Writer, typeSymbol, cl.Name, ConversionOptions.Reflection);
                ReflectionTracker.NeedsEnumImport = true;
            }
            writer.WriteLine();
        }

        /// <summary>
        /// Emits variables and properties for the class.
        /// </summary>
        /// <param name="cl">The owning class.</param>
        /// <param name="writer">The output writer.</param>
        void EmitVariables(ConversionClass cl, TypeScriptOutputWriter writer) {
            for (int j = 0; j < cl.Variables.Count; j++) {
                ConversionVariable var = cl.Variables[j];
                if (EmitVariable(cl, var, writer)) {
                    if (j != cl.Variables.Count - 1) {
                        writer.WriteLine();
                    }
                }
            }

            if (cl.Variables.Count > 0) {
                writer.WriteLine();
            }
        }

        /// <summary>
        /// Writes a TypeScript field or property for the given conversion variable.
        /// </summary>
        /// <param name="cl">The owning class.</param>
        /// <param name="var">The variable to emit.</param>
        /// <param name="writer">The output writer.</param>
        /// <returns>True when output was emitted for the variable.</returns>
        bool EmitVariable(ConversionClass cl, ConversionVariable var, TypeScriptOutputWriter writer) {
            string access = var.AccessType.ToString().ToLowerInvariant();
            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                access = "";
            }

            string type = var.VarType.ToTypeScriptString(TypeScriptProgram);

            string accessType = "";
            if (var.DeclarationType != MemberDeclarationType.Class) {
                accessType += " ";
                accessType += var.DeclarationType.ToString().ToLowerInvariant();
            }

            string isStatic = "";
            if (var.IsStatic) {
                isStatic = " static";
            }

            string assignment = "";
            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                if (var.IsGet && var.IsSet) {
                    writer.WriteLine($"{access} get {var.Name}(): {type};".TrimStart());
                    writer.WriteLine($"{access} set {var.Name}(val: {type});".TrimStart());
                } else {
                    if (var.IsGet) {
                        writer.WriteLine($"{access}{accessType} get {var.Name}(): {type};".TrimStart());
                    }
                    if (var.IsSet) {
                        writer.WriteLine($"{access}{accessType} set {var.Name}(value: {type});".TrimStart());
                    }
                    if (!var.IsGet && !var.IsSet) {
                        writer.WriteLine($"{access} {var.Name}: {type};".TrimStart());
                    }
                }
            } else {
                if (!string.IsNullOrEmpty(var.Assignment)) {
                    string ass = var.Assignment;
                    ass = ass.Replace("=>", "").Trim();
                    assignment = $" = {ass}";
                }

                string definiteAssignment = string.IsNullOrEmpty(assignment) ? "!" : string.Empty;

                if (var.DeclarationType == MemberDeclarationType.Abstract) {
                    if (var.IsGet && var.IsSet) {
                        writer.WriteLine($"{access}{isStatic} abstract get {var.Name}(): {type};".TrimStart());
                        writer.WriteLine($"{access}{isStatic} abstract set {var.Name}(value: {type});".TrimStart());
                        return true;
                    } else if (var.IsGet) {
                        writer.WriteLine($"{access}{isStatic} abstract get {var.Name}(): {type};".TrimStart());
                        return true;
                    }
                }
                if (var.IsGet && var.IsSet) {
                    writer.WriteLine($"private{isStatic} _{var.Name}{definiteAssignment}: {type}{assignment};".TrimStart());
                    writer.WriteLine($"{access}{isStatic} get {var.Name}(): {type} {{".TrimStart());
                    writer.WriteLine($"return this._{var.Name};");
                    writer.WriteLine("}");
                    writer.WriteLine($"{access}{isStatic} set {var.Name}(value: {type}) {{".TrimStart());
                    writer.WriteLine($"this._{var.Name} = value;");
                    writer.WriteLine("}");
                    return true;
                } else if (var.IsGet) {
                    writer.WriteLine($"private _{var.Name}{definiteAssignment}: {type}{assignment};".TrimStart());
                    writer.WriteLine($"{access}{isStatic} get {var.Name}(): {type} {{".TrimStart());
                    writer.WriteLine($"return this._{var.Name};");
                    writer.WriteLine("}");
                    writer.WriteLine($"private{isStatic} set {var.Name}(value: {type}) {{".TrimStart());
                    writer.WriteLine($"this._{var.Name} = value;");
                    writer.WriteLine("}");
                    return true;
                } else if (var.ArrowExpression != null) {
                    writer.WriteLine($"{access}{isStatic} get {var.Name}(): {type} {{".TrimStart());

                    writer.Write("return ");

                    List<string> lines = new List<string>();
                    TypeScriptLayerContext context = new TypeScriptLayerContext(TypeScriptProgram);

                    int start = context.DepthClass;
                    context.AddClass(cl);
                    Conversion.ProcessExpression(cl.Semantic, context, var.ArrowExpression, lines);
                    context.PopClass(start);

                    for (int k = 0; k < lines.Count; k++) {
                        string str = lines[k];
                        writer.Write(str);
                    }
                    writer.Write(";\n");

                    writer.WriteLine("}");
                    return true;
                } else if (var.GetBlock != null || var.SetBlock != null) {
                    if (var.GetBlock != null) {
                        ConversionFunction fn = new ConversionFunction();
                        fn.Name = $"get_{var.Name}";

                        fn.RawBlock = var.GetBlock;

                        writer.WriteLine($"{access}{isStatic} get {var.Name}(): {type} {{".TrimStart());
                        List<string> lines = fn.WriteLines(Conversion, Program, cl);
                        TypeScriptFunction.PrintLines(writer, lines);
                        writer.WriteLine();
                        writer.WriteLine("}");
                    }

                    if (var.SetBlock != null) {
                        ConversionFunction fn = new ConversionFunction();
                        ConversionVariable value = new ConversionVariable();
                        value.VarType = var.VarType;
                        value.Name = "value";
                        fn.InParameters.Add(value);
                        fn.Name = $"set_{var.Name}";

                        fn.RawBlock = var.SetBlock;

                        writer.WriteLine($"{access}{isStatic} set {var.Name}(value: {type}) {{".TrimStart());
                        List<string> lines = fn.WriteLines(Conversion, Program, cl);
                        TypeScriptFunction.PrintLines(writer, lines);
                        writer.WriteLine();
                        writer.WriteLine("}");
                    }
                } else {
                    writer.WriteLine($"{access}{accessType}{isStatic} {var.Name}{definiteAssignment}: {type}{assignment};".TrimStart());
                }
            }

            return false;
        }

        /// <summary>
        /// Emits a static initializer block for classes with static constructors.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="writer">The output writer.</param>
        void EmitStaticConstructor(ConversionClass cl, TypeScriptOutputWriter writer) {
            var constructors = cl.Functions.Where(c => c.IsConstructor && c.IsStatic).ToList();
            if (constructors.Count == 0) {
                return;
            }

            ConversionFunction fn = constructors[0];
            writer.WriteLine("static {");

            List<string> lines = fn.WriteLines(Conversion, Program, cl);
            TypeScriptFunction.PrintLines(writer, lines);

            writer.WriteLine("}");
            writer.WriteLine();
        }

        /// <summary>
        /// Emits constructor overloads and factory methods for TypeScript.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="writer">The output writer.</param>
        void EmitConstructors(ConversionClass cl, TypeScriptOutputWriter writer) {
            var constructors = cl.Functions.Where(c => c.IsConstructor && !c.IsStatic).ToList();
            var classOverrides = cl.Extensions.Count(over => {
                var extCl = TypeScriptProgram.GetClassByName(over);
                if (extCl != null) {
                    return extCl.DeclarationType != MemberDeclarationType.Interface;
                }

                return false;
            });

            if (constructors.Count == 1) {
                ConversionFunction fn = constructors[0];
                writer.Write("constructor(");

                for (int k = 0; k < fn.InParameters.Count; k++) {
                    var param = fn.InParameters[k];
                    string type = param.VarType.ToTypeScriptString(TypeScriptProgram);
                    string def = param.DefaultValue;
                    if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                        def = "";
                    }

                    writer.Write($"{param.Name}: {type}{def}");

                    if (k != fn.InParameters.Count - 1) {
                        writer.Write(", ");
                    }
                }

                writer.WriteLine(") {");

                if (classOverrides > 0) {
                    EmitBaseConstructorCall(cl, fn, writer);
                }

                List<string> lines = fn.WriteLines(Conversion, Program, cl);
                TypeScriptFunction.PrintLines(writer, lines);

                writer.WriteLine("}");
                writer.WriteLine();
            } else {
                string generic = cl.GetGenericArguments();

                if (constructors.Count > 1 && classOverrides > 0) {
                    writer.WriteLine("constructor(...__baseArgs: any[]) {");
                    writer.WriteLine("super(...__baseArgs);");
                    writer.WriteLine("}");
                    writer.WriteLine();
                }

                for (int i = 0; i < constructors.Count; i++) {
                    ConversionFunction fn = constructors[i];

                    List<string> bodyLines = new List<string>();
                    AppendConstructorFactoryInitializer(cl, fn, constructors, bodyLines);

                    List<string> lines = new List<string>();
                    LayerContext context = new TypeScriptLayerContext(TypeScriptProgram);

                    int start = context.DepthClass;
                    int startFn = context.DepthFunction;

                    context.AddClass(cl);
                    context.AddFunction(new FunctionStack(fn));

                    if (fn.ArrowExpression != null) {
                        Conversion.ProcessArrowExpressionClause(cl.Semantic, context, fn.ArrowExpression, lines);
                    } else if (fn.RawBlock != null) {
                        Conversion.ProcessBlock(cl.Semantic, context, fn.RawBlock, lines);
                    }

                    context.PopClass(start);
                    context.PopFunction(startFn);

                    RewriteConstructorFactoryTokens(lines, "__obj");
                    bodyLines.AddRange(lines);

                    string returnType = fn.IsAsync ? $"Promise<{cl.Name}{generic}>" : $"{cl.Name}{generic}";
                    writer.Write($"static {fn.GetAsync()}{fn.Name}{generic}(");

                    for (int k = 0; k < fn.InParameters.Count; k++) {
                        var param = fn.InParameters[k];
                        string type = param.VarType.ToTypeScriptString(TypeScriptProgram);
                        string def = param.DefaultValue;
                        if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                            def = "";
                        }

                        writer.Write($"{param.Name}: {type}{def}");

                        if (k != fn.InParameters.Count - 1) {
                            writer.Write(", ");
                        }
                    }

                    writer.WriteLine($"): {returnType} {{");
                    TypeScriptFunction.PrintLines(writer, bodyLines);
                    writer.WriteLine("return __obj;");
                    writer.WriteLine("}");
                    writer.WriteLine();
                }
            }
        }

        /// <summary>
        /// Emits the appropriate base constructor call for a single constructor.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="fn">The constructor function.</param>
        /// <param name="writer">The output writer.</param>
        void EmitBaseConstructorCall(ConversionClass cl, ConversionFunction fn, TypeScriptOutputWriter writer) {
            ConstructorInitializerSyntax initializer = fn.ConstructorInitializer;
            if (initializer == null || initializer.IsKind(SyntaxKind.ThisConstructorInitializer)) {
                writer.WriteLine("super();");
                return;
            }

            List<string> beforeLines = new List<string>();
            List<string> afterLines = new List<string>();
            List<ExpressionResult> argumentResults = new List<ExpressionResult>();
            var arguments = initializer.ArgumentList != null ? initializer.ArgumentList.Arguments : default;
            List<string> argLines = BuildConstructorInitializerArguments(cl, fn, arguments, beforeLines, afterLines, argumentResults);

            TypeScriptFunction.PrintLines(writer, beforeLines);
            writer.Write("super(");
            writer.WriteLines(argLines, string.Empty);
            writer.WriteLine(");");
            TypeScriptFunction.PrintLines(writer, afterLines);
        }

        /// <summary>
        /// Appends the instance creation statement for a constructor factory method.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="fn">The constructor function.</param>
        /// <param name="constructors">All constructor overloads for the class.</param>
        /// <param name="bodyLines">The body lines to append to.</param>
        void AppendConstructorFactoryInitializer(
            ConversionClass cl,
            ConversionFunction fn,
            List<ConversionFunction> constructors,
            List<string> bodyLines) {
            ConstructorInitializerSyntax initializer = fn.ConstructorInitializer;
            bool isThisInitializer = initializer != null && initializer.IsKind(SyntaxKind.ThisConstructorInitializer);
            bool isBaseInitializer = initializer != null && initializer.IsKind(SyntaxKind.BaseConstructorInitializer);

            List<string> beforeLines = new List<string>();
            List<string> afterLines = new List<string>();
            List<ExpressionResult> argumentResults = new List<ExpressionResult>();
            var arguments = initializer?.ArgumentList != null ? initializer.ArgumentList.Arguments : default;
            List<string> argLines = BuildConstructorInitializerArguments(cl, fn, arguments, beforeLines, afterLines, argumentResults);

            bodyLines.AddRange(beforeLines);
            if (isThisInitializer) {
                ConversionFunction target = ResolveConstructorInitializerTarget(constructors, fn, argumentResults);
                if (target == null) {
                    throw new InvalidOperationException($"Constructor initializer not found for {cl.Name}.");
                }

                if (target.IsAsync) {
                    fn.IsAsync = true;
                }

                bodyLines.Add("const __obj = ");
                if (target.IsAsync) {
                    bodyLines.Add("await ");
                }
                bodyLines.Add(cl.Name);
                bodyLines.Add(".");
                bodyLines.Add(target.Name);
                bodyLines.Add("(");
                bodyLines.AddRange(argLines);
                bodyLines.Add(");\n");
            } else {
                bodyLines.Add("const __obj = new ");
                bodyLines.Add(cl.Name);
                bodyLines.Add("(");
                if (isBaseInitializer) {
                    bodyLines.AddRange(argLines);
                }
                bodyLines.Add(");\n");
            }
            bodyLines.AddRange(afterLines);
        }

        /// <summary>
        /// Builds argument tokens for a constructor initializer invocation.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="fn">The constructor function.</param>
        /// <param name="arguments">The constructor initializer arguments.</param>
        /// <param name="beforeLines">Lines that must appear before the invocation.</param>
        /// <param name="afterLines">Lines that must appear after the invocation.</param>
        /// <param name="argumentResults">Optional expression results for argument matching.</param>
        /// <returns>The tokens for the argument list.</returns>
        List<string> BuildConstructorInitializerArguments(
            ConversionClass cl,
            ConversionFunction fn,
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            List<string> beforeLines,
            List<string> afterLines,
            List<ExpressionResult> argumentResults) {
            List<string> argLines = new List<string>();
            if (arguments.Count == 0) {
                return argLines;
            }

            LayerContext context = new TypeScriptLayerContext(TypeScriptProgram);
            int start = context.DepthClass;
            int startFn = context.DepthFunction;

            context.AddClass(cl);
            context.AddFunction(new FunctionStack(fn));

            for (int i = 0; i < arguments.Count; i++) {
                List<string> exprLines = new List<string>();
                int startArg = context.DepthClass;
                ExpressionResult result = Conversion.ProcessExpression(cl.Semantic, context, arguments[i].Expression, exprLines);
                context.PopClass(startArg);

                if (result.BeforeLines != null && result.BeforeLines.Count > 0) {
                    beforeLines.AddRange(result.BeforeLines);
                }

                argLines.AddRange(exprLines);

                if (result.AfterLines != null && result.AfterLines.Count > 0) {
                    afterLines.AddRange(result.AfterLines);
                }

                argumentResults?.Add(result);

                if (i != arguments.Count - 1) {
                    argLines.Add(", ");
                }
            }

            context.PopClass(start);
            context.PopFunction(startFn);

            return argLines;
        }

        /// <summary>
        /// Resolves the target constructor for a this-initializer.
        /// </summary>
        /// <param name="constructors">All constructor overloads for the class.</param>
        /// <param name="current">The constructor containing the initializer.</param>
        /// <param name="argumentResults">Expression results for initializer arguments.</param>
        /// <returns>The matching constructor overload.</returns>
        ConversionFunction ResolveConstructorInitializerTarget(
            List<ConversionFunction> constructors,
            ConversionFunction current,
            List<ExpressionResult> argumentResults) {
            if (constructors == null) {
                return null;
            }

            int argumentCount = argumentResults?.Count ?? 0;
            List<ConversionFunction> candidates = constructors
                .Where(c => c != current && c.InParameters.Count == argumentCount)
                .ToList();

            if (candidates.Count == 1) {
                return candidates[0];
            }

            if (candidates.Count == 0) {
                return null;
            }

            if (argumentResults != null && argumentResults.Count > 0) {
                for (int i = 0; i < candidates.Count; i++) {
                    ConversionFunction candidate = candidates[i];
                    bool match = true;
                    for (int argIndex = 0; argIndex < argumentResults.Count; argIndex++) {
                        ExpressionResult argResult = argumentResults[argIndex];
                        if (argResult.Type == null ||
                            candidate.InParameters[argIndex].VarType.TypeName != argResult.Type.TypeName) {
                            match = false;
                            break;
                        }
                    }

                    if (match) {
                        return candidate;
                    }
                }
            }

            return candidates[0];
        }

        /// <summary>
        /// Rewrites constructor factory tokens to target the generated instance and return it on bare returns.
        /// </summary>
        /// <param name="lines">The tokens to rewrite.</param>
        /// <param name="instanceName">The instance variable name.</param>
        static void RewriteConstructorFactoryTokens(List<string> lines, string instanceName) {
            if (lines == null || lines.Count == 0) {
                return;
            }

            for (int i = 0; i < lines.Count; i++) {
                string value = lines[i];
                if (string.IsNullOrEmpty(value)) {
                    continue;
                }

                if (value.Contains("this.", StringComparison.Ordinal)) {
                    value = value.Replace("this.", instanceName + ".", StringComparison.Ordinal);
                    lines[i] = value;
                }

                if (value == "this") {
                    lines[i] = instanceName;
                    continue;
                }

                if (value.StartsWith("this", StringComparison.Ordinal) && value.Length > 4) {
                    char nextChar = value[4];
                    if (!char.IsLetterOrDigit(nextChar) && nextChar != '_') {
                        lines[i] = instanceName + value.Substring(4);
                        continue;
                    }
                }

                if (value == "return") {
                    int nextIndex = i + 1;
                    while (nextIndex < lines.Count && string.IsNullOrWhiteSpace(lines[nextIndex])) {
                        nextIndex++;
                    }

                    if (nextIndex < lines.Count && lines[nextIndex].StartsWith(";", StringComparison.Ordinal)) {
                        lines[i] = $"return {instanceName}";
                    }
                    continue;
                }

                if (value.StartsWith("return", StringComparison.Ordinal) && value.Length > 6) {
                    string remainder = value.Substring(6);
                    if (remainder.TrimStart().StartsWith(";", StringComparison.Ordinal)) {
                        lines[i] = $"return {instanceName}{remainder}";
                    }
                }
            }
        }

        /// <summary>
        /// Emits non-constructor functions for the class.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="writer">The output writer.</param>
        void EmitFunctions(ConversionClass cl, TypeScriptOutputWriter writer) {
            var functions = cl.Functions.Where(c => !c.IsConstructor).ToList();

            for (int j = 0; j < functions.Count; j++) {
                ConversionFunction fn = functions[j];

                string access = fn.AccessType.ToString().ToLowerInvariant();
                if (cl.DeclarationType == MemberDeclarationType.Class) {
                    access += " ";
                } else {
                    access = "";
                }

                if (j != 0) {
                    writer.WriteLine();
                }

                List<string> lines = fn.WriteLines(Conversion, Program, cl);

                string generic = fn.GetGenericArguments();
                string clType = fn.GetClassType();
                string async = fn.GetAsync();

                if (cl.DeclarationType == MemberDeclarationType.Interface) {
                    async = "";
                }

                if (fn.IsStatic) {
                    writer.Write($"{access}static {async}{fn.Remap}{generic}(");
                } else {
                    writer.Write($"{access}{clType}{async}{fn.Remap}{generic}(");
                }

                for (int k = 0; k < fn.InParameters.Count; k++) {
                    var param = fn.InParameters[k];
                    string type = param.VarType.ToTypeScriptString(TypeScriptProgram);
                    string def = param.DefaultValue;
                    if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                        def = "";
                    }

                    if (param.Modifier.HasFlag(core.ParameterModifier.Out)) {
                        writer.Write($"{param.Name}: {{ value: {type}{def} }}");
                    } else {
                        writer.Write($"{param.Name}: {type}{def}");
                    }

                    if (k != fn.InParameters.Count - 1) {
                        writer.Write(", ");
                    }
                }

                string returnParameter = null;
                if (fn.ReturnType != null) {
                    returnParameter = fn.ReturnType.ToTypeScriptString(TypeScriptProgram);
                }
                if (string.IsNullOrWhiteSpace(returnParameter)) {
                    returnParameter = "void";
                }
                if (fn.IsAsync) {
                    returnParameter = returnParameter == "void"
                        ? "Promise<void>"
                        : $"Promise<{returnParameter}>";
                }

                if (cl.DeclarationType == MemberDeclarationType.Interface || !fn.HasBody) {
                    writer.WriteLine($"): {returnParameter};");
                } else {
                    writer.WriteLine($"): {returnParameter} {{");

                    TypeScriptFunction.PrintLines(writer, lines);
                    writer.WriteLine("}");
                }
            }
        }
    }
}
