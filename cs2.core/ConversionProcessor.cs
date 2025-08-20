using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;

namespace cs2.core {
    public abstract class ConversionProcessor {
        public virtual void ProcessClass(ConversionClass cl, ConversionProgram program) {
            var constructors = cl.Functions.Where(c => c.IsConstructor && !c.IsStatic).ToList();
            var classOverrides = cl.Extensions.Count(over => {
                var extCl = program.Classes.FirstOrDefault(c => c.Name == over);
                if (extCl != null) {
                    return extCl.DeclarationType != MemberDeclarationType.Interface;
                }

                return false;
            });

            if (constructors.Count > 1) {
                for (int i = 0; i < constructors.Count; i++) {
                    ConversionFunction fn = constructors[i];
                    fn.Name = $"New{i + 1}";
                }
            }

            if (classOverrides > 0) {
                // look for matching functions
                List<ConversionClass> classes = program.Classes.Where(c => cl.Extensions.Contains(c.Name)).ToList();
                List<ConversionFunction> matchingFns = classes
                    .SelectMany(c => c.Functions)
                    .Where(f => cl.Functions.Any(clFn => clFn.Name == f.Name))
                    .ToList();

                int counter = 0;
                for (int i = 0; i < matchingFns.Count; i++) {
                    ConversionFunction match = matchingFns[i];

                    ConversionFunction mainFn = cl.Functions.Find(c => c.Name == match.Name);
                    if (match.InParameters.Count != mainFn.InParameters.Count) {
                        mainFn.Remap += ++counter;
                    }
                }
            }
        }

        public virtual ExpressionResult ProcessBlock(SemanticModel semantic, LayerContext context, BlockSyntax block, List<string> lines, int depth = 1) {
            ExpressionResult result = default;

            foreach (var statement in block.Statements) {
                List<string> newLines = new List<string>();

                int start = context.DepthClass;
                result = ProcessStatement(semantic, context, statement, newLines, depth);
                context.PopClass(start);

                if (result.BeforeLines != null) {
                    lines.AddRange(result.BeforeLines);
                }

                lines.AddRange(newLines);

                if (result.AfterLines != null) {
                    lines.AddRange(result.AfterLines);
                }
            }

            return result;
        }

        public virtual ExpressionResult ProcessExpression(
            SemanticModel semantic,
            LayerContext context,
            ExpressionSyntax expression,
            List<string> lines,
            List<ExpressionResult> refTypes = null
            ) {
            if (expression is AssignmentExpressionSyntax assignment) {
                ProcessAssignmentExpressionSyntax(semantic, context, assignment, lines);
            } else if (expression is IdentifierNameSyntax identifier) {
                return ProcessIdentifierNameSyntax(semantic, context, identifier, lines, refTypes);
            } else if (expression is ObjectCreationExpressionSyntax objectCreation) {
                return ProcessObjectCreationExpressionSyntax(semantic, context, objectCreation, lines);
            } else if (expression is MemberAccessExpressionSyntax memberAccess) {
                return ProcessMemberAccessExpressionSyntax(semantic, context, memberAccess, lines, refTypes);
            } else if (expression is InvocationExpressionSyntax invocationExpression) {
                return ProcessInvocationExpressionSyntax(semantic, context, invocationExpression, lines);
            } else if (expression is ThisExpressionSyntax thisExpression) {
                ProcessThisExpressionSyntax(semantic, context, thisExpression, lines);
            } else if (expression is LiteralExpressionSyntax literal) {
                return ProcessLiteralExpression(context, literal, lines);
            } else if (expression is BinaryExpressionSyntax binary) {
                return ProcessBinaryExpressionSyntax(semantic, context, binary, lines);
            } else if (expression is GenericNameSyntax generic) {
                ProcessGenericNameSyntax(semantic, context, generic, lines);
            } else if (expression is ParenthesizedLambdaExpressionSyntax lambda) {
                ProcessLambdaExpression(semantic, context, lambda, lines);
            } else if (expression is ConditionalExpressionSyntax conditional) {
                ProcessConditionalExpression(semantic, context, conditional, lines);
            } else if (expression is CastExpressionSyntax castExpr) {
                return ProcessCastExpression(semantic, context, castExpr, lines);
            } else if (expression is ConditionalAccessExpressionSyntax conditionalAccess) {
                ProcessConditionalAccessExpression(semantic, context, conditionalAccess, lines);
            } else if (expression is MemberBindingExpressionSyntax memberBinding) {
                ProcessMemberBindingExpression(semantic, context, memberBinding, lines);
            } else if (expression is PrefixUnaryExpressionSyntax prefixUnary) {
                return ProcessPrefixUnaryExpression(semantic, context, prefixUnary, lines);
            } else if (expression is PostfixUnaryExpressionSyntax postfixUnary) {
                ProcessPostfixUnaryExpression(semantic, context, postfixUnary, lines);
            } else if (expression is ElementAccessExpressionSyntax elementAccess) {
                ProcessElementAccessExpression(semantic, context, elementAccess, lines);
            } else if (expression is InterpolatedStringExpressionSyntax interpolatedString) {
                return ProcessInterpolatedStringExpression(semantic, context, interpolatedString, lines);
            } else if (expression is DefaultExpressionSyntax defaultExpr) {
                ProcessDefaultExpression(semantic, context, defaultExpr, lines);
            } else if (expression is PredefinedTypeSyntax predefinedType) {
                ProcessPredefinedType(semantic, context, predefinedType, lines);
            } else if (expression is TupleExpressionSyntax tupleExpression) {
                ProcessTupleExpression(semantic, context, tupleExpression, lines);
            } else if (expression is InitializerExpressionSyntax initializerExpression) {
                ProcessInitializerExpression(semantic, context, initializerExpression, lines);
            } else if (expression is BaseExpressionSyntax baseExpression) {
                ProcessBaseExpression(semantic, context, baseExpression, lines);
            } else if (expression is ParenthesizedExpressionSyntax parenthesizedExpression) {
                ProcessParenthesizedExpression(semantic, context, parenthesizedExpression, lines);
            } else if (expression is ArrayCreationExpressionSyntax arrayCreation) {
                return ProcessArrayCreationExpression(semantic, context, arrayCreation, lines);
            } else if (expression is SimpleLambdaExpressionSyntax simpleLambda) {
                ProcessSimpleLambdaExpression(semantic, context, simpleLambda, lines);
            } else if (expression is TypeOfExpressionSyntax typeOfExpression) {
                ProcessTypeOfExpression(semantic, context, typeOfExpression, lines);
            } else if (expression is QualifiedNameSyntax qualifiedName) {
                return ProcessQualifiedName(semantic, context, qualifiedName, lines);
            } else if (expression is AwaitExpressionSyntax awaitExpression) {
                ProcessAwait(semantic, context, awaitExpression, lines);
            } else if (expression is ImplicitArrayCreationExpressionSyntax implicitArray) {
                ProcessImplicitArrayCreationExpression(semantic, context, implicitArray, lines);
            } else if (expression is AliasQualifiedNameSyntax) {
                // ignore?
            } else if (expression is DeclarationExpressionSyntax declaration) {
                return ProcessDeclarationExpressionSyntax(semantic, context, declaration, lines);
            } else {
                //Debugger.Break();
                return new ExpressionResult(false);
            }

            return new ExpressionResult(true);
        }

        protected abstract void ProcessAssignmentExpressionSyntax(SemanticModel semantic, LayerContext context, AssignmentExpressionSyntax generic, List<string> lines);

        protected abstract ExpressionResult ProcessDeclarationExpressionSyntax(SemanticModel semantic, LayerContext context, DeclarationExpressionSyntax declaration, List<string> lines);

        protected abstract ExpressionResult ProcessIdentifierNameSyntax(SemanticModel semantic, LayerContext context, IdentifierNameSyntax identifier, List<string> lines, List<ExpressionResult> refTypes);

        protected abstract ExpressionResult ProcessObjectCreationExpressionSyntax(SemanticModel semantic, LayerContext context, ObjectCreationExpressionSyntax objectCreation, List<string> lines);

        protected abstract ExpressionResult ProcessMemberAccessExpressionSyntax(SemanticModel semantic, LayerContext context, MemberAccessExpressionSyntax memberAccess, List<string> lines, List<ExpressionResult> refTypes);

        protected abstract ExpressionResult ProcessInvocationExpressionSyntax(SemanticModel semantic, LayerContext context, InvocationExpressionSyntax invocationExpression, List<string> lines);

        protected abstract void ProcessThisExpressionSyntax(SemanticModel semantic, LayerContext context, ThisExpressionSyntax thisExpression, List<string> lines);

        protected abstract ExpressionResult ProcessBinaryExpressionSyntax(SemanticModel semantic, LayerContext context, BinaryExpressionSyntax binary, List<string> lines);

        protected abstract void ProcessGenericNameSyntax(SemanticModel semantic, LayerContext context, GenericNameSyntax generic, List<string> lines);

        protected abstract void ProcessImplicitArrayCreationExpression(SemanticModel semantic, LayerContext context, ImplicitArrayCreationExpressionSyntax implicitArray, List<string> lines);

        protected abstract void ProcessAwait(SemanticModel semantic, LayerContext context, AwaitExpressionSyntax awaitExpression, List<string> lines);

        protected abstract ExpressionResult ProcessQualifiedName(SemanticModel semantic, LayerContext context, QualifiedNameSyntax qualifiedName, List<string> lines);

        protected abstract void ProcessTypeOfExpression(SemanticModel semantic, LayerContext context, TypeOfExpressionSyntax typeOfExpression, List<string> lines);

        protected abstract void ProcessSimpleLambdaExpression(SemanticModel semantic, LayerContext context, SimpleLambdaExpressionSyntax simpleLambda, List<string> lines);

        protected abstract ExpressionResult ProcessArrayCreationExpression(SemanticModel semantic, LayerContext context, ArrayCreationExpressionSyntax arrayCreation, List<string> lines);

        protected abstract void ProcessParenthesizedExpression(SemanticModel semantic, LayerContext context, ParenthesizedExpressionSyntax parenthesizedExpression, List<string> lines);

        protected abstract void ProcessBaseExpression(SemanticModel semantic, LayerContext context, BaseExpressionSyntax baseExpression, List<string> lines);

        protected abstract void ProcessInitializerExpression(SemanticModel semantic, LayerContext context, InitializerExpressionSyntax initializerExpression, List<string> lines);

        protected abstract void ProcessTupleExpression(SemanticModel semantic, LayerContext context, TupleExpressionSyntax tupleExpression, List<string> lines);

        protected abstract void ProcessPredefinedType(SemanticModel semantic, LayerContext context, PredefinedTypeSyntax predefinedType, List<string> lines);

        protected abstract void ProcessDefaultExpression(SemanticModel semantic, LayerContext context, DefaultExpressionSyntax defaultExpression, List<string> lines);

        protected abstract ExpressionResult ProcessInterpolatedStringExpression(SemanticModel semantic, LayerContext context, InterpolatedStringExpressionSyntax interpolatedString, List<string> lines);

        protected abstract void ProcessElementAccessExpression(SemanticModel semantic, LayerContext context, ElementAccessExpressionSyntax elementAccess, List<string> lines);

        protected abstract void ProcessPostfixUnaryExpression(SemanticModel semantic, LayerContext context, PostfixUnaryExpressionSyntax postfixUnary, List<string> lines);

        protected abstract ExpressionResult ProcessPrefixUnaryExpression(SemanticModel semantic, LayerContext context, PrefixUnaryExpressionSyntax prefixUnary, List<string> lines);

        protected abstract void ProcessMemberBindingExpression(SemanticModel semantic, LayerContext context, MemberBindingExpressionSyntax memberBinding, List<string> lines);

        protected abstract void ProcessConditionalAccessExpression(SemanticModel semantic, LayerContext context, ConditionalAccessExpressionSyntax conditionalAccess, List<string> lines);

        protected abstract ExpressionResult ProcessCastExpression(SemanticModel semantic, LayerContext context, CastExpressionSyntax castExpr, List<string> lines);

        protected abstract void ProcessConditionalExpression(SemanticModel semantic, LayerContext context, ConditionalExpressionSyntax conditional, List<string> lines);

        protected abstract void ProcessLambdaExpression(SemanticModel semantic, LayerContext context, ParenthesizedLambdaExpressionSyntax lambda, List<string> lines);

        protected virtual ExpressionResult ProcessStatement(SemanticModel semantic, LayerContext context, StatementSyntax statement, List<string> lines, int depth = 1) {
            if (statement is ExpressionStatementSyntax) {
                ExpressionSyntax expression = ((ExpressionStatementSyntax)statement).Expression;

                List<string> newLines = new List<string>();
                ExpressionResult result = ProcessExpression(semantic, context, expression, newLines);
                if (depth == 0) {
                    newLines.Add(";");
                } else {
                    newLines.Add(";\n");
                }

                if (result.BeforeLines != null) {
                    lines.AddRange(result.BeforeLines);
                }

                lines.AddRange(newLines);

                if (result.AfterLines != null) {
                    lines.AddRange(result.AfterLines);
                }

                return result;
            } else if (statement is ReturnStatementSyntax ret) {
                ProcessReturnStatement(semantic, context, ret, lines);
            } else if (statement is LocalDeclarationStatementSyntax local) {
                ProcessDeclaration(semantic, context, local.Declaration, lines);
                lines.Add(";\n");
            } else if (statement is SwitchStatementSyntax swtc) {
                ProcessSwitchStatement(semantic, context, swtc, lines);
            } else if (statement is BlockSyntax block) {
                ProcessBlock(semantic, context, block, lines);
            } else if (statement is BreakStatementSyntax brk) {
                lines.Add("break;\n");
            } else if (statement is ThrowStatementSyntax thrw) {
                ProcessThrowStatement(semantic, context, thrw, lines);
            } else if (statement is IfStatementSyntax ifStmt) {
                ExpressionResult result = ProcessIfStatement(semantic, context, ifStmt, lines);
                return result;
            } else if (statement is ForStatementSyntax forStmt) {
                ProcessForStatement(semantic, context, forStmt, lines);
            } else if (statement is WhileStatementSyntax whileStmt) {
                ProcessWhileStatement(semantic, context, whileStmt, lines);
            } else if (statement is ContinueStatementSyntax continueStatement) {
                ProcessContinueStatement(semantic, context, continueStatement, lines);
            } else if (statement is ForEachStatementSyntax forEachStmt) {
                ProcessForEachStatement(semantic, context, forEachStmt, lines);
            } else if (statement is TryStatementSyntax tryStmt) {
                ProcessTryStatement(semantic, context, tryStmt, lines);
            } else if (statement is LockStatementSyntax lockStmt) {
                ProcessLockStatement(semantic, context, lockStmt, lines);
            } else if (statement is UsingStatementSyntax usingStatement) {
                ProcessUsingStatement(semantic, context, usingStatement, lines);
            } else if (statement is DoStatementSyntax doStatement) {
                ProcessDoStatement(semantic, context, doStatement, lines);
            } else if (statement is EmptyStatementSyntax emptyStatement) {
                ProcessEmptyStatement(semantic, context, emptyStatement, lines);
            } else {
                Debugger.Break();
            }

            return new ExpressionResult(false);
        }

        protected abstract void ProcessReturnStatement(SemanticModel semantic, LayerContext context, ReturnStatementSyntax ret, List<string> lines);

        protected abstract void ProcessEmptyStatement(SemanticModel semantic, LayerContext context, EmptyStatementSyntax emptyStatement, List<string> lines);

        protected abstract void ProcessDoStatement(SemanticModel semantic, LayerContext context, DoStatementSyntax doStatement, List<string> lines);

        protected abstract void ProcessUsingStatement(SemanticModel semantic, LayerContext context, UsingStatementSyntax usingStatement, List<string> lines);

        protected abstract void ProcessLockStatement(SemanticModel semantic, LayerContext context, LockStatementSyntax lockStatement, List<string> lines);

        protected abstract void ProcessTryStatement(SemanticModel semantic, LayerContext context, TryStatementSyntax tryStatement, List<string> lines);

        protected abstract void ProcessForEachStatement(SemanticModel semantic, LayerContext context, ForEachStatementSyntax forEachStatement, List<string> lines);

        protected abstract void ProcessContinueStatement(SemanticModel semantic, LayerContext context, ContinueStatementSyntax continueStatement, List<string> lines);

        protected abstract void ProcessWhileStatement(SemanticModel semantic, LayerContext context, WhileStatementSyntax whileStatement, List<string> lines);

        protected abstract void ProcessForStatement(SemanticModel semantic, LayerContext context, ForStatementSyntax forStatement, List<string> lines);

        protected abstract ExpressionResult ProcessIfStatement(SemanticModel semantic, LayerContext context, IfStatementSyntax ifStatement, List<string> lines);

        protected abstract void ProcessThrowStatement(SemanticModel semantic, LayerContext context, ThrowStatementSyntax throwStatement, List<string> lines);

        protected abstract void ProcessSwitchStatement(SemanticModel semantic, LayerContext context, SwitchStatementSyntax switchStatement, List<string> lines);

        protected abstract void ProcessDeclaration(
            SemanticModel semantic,
            LayerContext context,
            VariableDeclarationSyntax declaration,
            List<string> lines
            );

        protected abstract ExpressionResult ProcessLiteralExpression(LayerContext context, LiteralExpressionSyntax literalExpression, List<string> lines);

        public abstract void ProcessArrowExpressionClause(SemanticModel semantic, LayerContext context, ArrowExpressionClauseSyntax arrowExpression, List<string> lines);

        protected virtual BinaryOpTypes ParseBinaryExpression(
            SemanticModel semantic,
            LayerContext context,
            BinaryExpressionSyntax binaryExp,
            out List<string> left,
            out List<string> right,
            out ExpressionResult result
        ) {
            left = new List<string>();
            right = new List<string>();

            int start1 = context.DepthClass;
            result = ProcessExpression(semantic, context, binaryExp.Left, left);
            context.PopClass(start1);

            int start2 = context.DepthClass;
            ProcessExpression(semantic, context, binaryExp.Right, right);
            context.PopClass(start2);

            BinaryOpTypes type;
            SyntaxKind kind = binaryExp.Kind();
            switch (binaryExp.Kind()) {
                case SyntaxKind.AddExpression:
                    type = BinaryOpTypes.Plus;
                    break;
                case SyntaxKind.SubtractExpression:
                    type = BinaryOpTypes.Minus;
                    break;
                case SyntaxKind.DivideExpression:
                    type = BinaryOpTypes.Divide;
                    break;
                case SyntaxKind.MultiplyExpression:
                    type = BinaryOpTypes.Multiply;
                    break;
                case SyntaxKind.GreaterThanExpression:
                    type = BinaryOpTypes.GreaterThan;
                    break;
                case SyntaxKind.GreaterThanOrEqualExpression:
                    type = BinaryOpTypes.GreaterThanOrEqual;
                    break;
                case SyntaxKind.LessThanExpression:
                    type = BinaryOpTypes.LessThan;
                    break;
                case SyntaxKind.LessThanOrEqualExpression:
                    type = BinaryOpTypes.LessThanOrEqual;
                    break;
                case SyntaxKind.EqualsExpression:
                    type = BinaryOpTypes.Equal;
                    break;
                case SyntaxKind.LogicalAndExpression:
                    type = BinaryOpTypes.BinAnd;
                    break;
                case SyntaxKind.LogicalOrExpression:
                    type = BinaryOpTypes.BinOr;
                    break;
                case SyntaxKind.LogicalNotExpression:
                    type = BinaryOpTypes.BinNot;
                    break;
                case SyntaxKind.NotEqualsExpression:
                    type = BinaryOpTypes.NotEqual;
                    break;
                case SyntaxKind.IsExpression:
                    type = BinaryOpTypes.InstanceOf;
                    break;
                case SyntaxKind.BitwiseAndExpression:
                    type = BinaryOpTypes.BitwiseAnd;
                    break;
                case SyntaxKind.BitwiseNotExpression:
                    type = BinaryOpTypes.BitwiseNot;
                    break;
                case SyntaxKind.BitwiseOrExpression:
                    type = BinaryOpTypes.BitwiseOr;
                    break;
                case SyntaxKind.RightShiftExpression:
                    type = BinaryOpTypes.RightShift;
                    break;
                case SyntaxKind.LeftShiftExpression:
                    type = BinaryOpTypes.LeftShift;
                    break;
                case SyntaxKind.ExclusiveOrExpression:
                    type = BinaryOpTypes.ExclusiveOr;
                    break;
                case SyntaxKind.ModuloExpression:
                    type = BinaryOpTypes.Modulo;
                    break;
                case SyntaxKind.CoalesceExpression:
                    type = BinaryOpTypes.Coalesce;
                    break;
                case SyntaxKind.AsExpression:
                    type = BinaryOpTypes.As;
                    break;
                default:
                    throw new Exception("Unknown binary");
            }


            return type;
        }
    }
}
