using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp;

/// <summary>
/// Creates consistent source-located hard errors for semantic native ownership failures.
/// </summary>
public sealed class CPPOwnershipDiagnosticFactory {
    /// <summary>
    /// Creates one actionable ownership diagnostic at the exact syntax that violates the native lifetime contract.
    /// </summary>
    /// <param name="code">Stable <c>CPPOWN</c> diagnostic code.</param>
    /// <param name="node">Source syntax that caused the ownership failure.</param>
    /// <param name="member">Containing source member used for report grouping.</param>
    /// <param name="message">Explanation containing the ownership origin and invalid state transition or sink.</param>
    /// <param name="recommendation">Concrete source correction required to establish a safe contract.</param>
    /// <returns>A hard-error diagnostic with one-based source coordinates.</returns>
    public CPPConversionDiagnostic Create(string code, SyntaxNode node, ISymbol member, string message, string recommendation) {
        if (string.IsNullOrWhiteSpace(code)) {
            throw new ArgumentException("An ownership diagnostic code is required.", nameof(code));
        }
        if (node == null) {
            throw new ArgumentNullException(nameof(node));
        }
        if (member == null) {
            throw new ArgumentNullException(nameof(member));
        }
        if (string.IsNullOrWhiteSpace(message)) {
            throw new ArgumentException("An ownership diagnostic message is required.", nameof(message));
        }
        if (string.IsNullOrWhiteSpace(recommendation)) {
            throw new ArgumentException("An ownership diagnostic recommendation is required.", nameof(recommendation));
        }

        FileLinePositionSpan lineSpan = node.GetLocation().GetLineSpan();
        bool hasSourceLocation = lineSpan.IsValid;
        return new CPPConversionDiagnostic {
            Severity = CPPDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            SourceTypeName = member.ContainingType?.Name ?? string.Empty,
            SourceMemberName = member.Name,
            SyntaxKind = node.Kind().ToString(),
            FilePath = hasSourceLocation ? lineSpan.Path : string.Empty,
            LineNumber = hasSourceLocation ? lineSpan.StartLinePosition.Line + 1 : 0,
            ColumnNumber = hasSourceLocation ? lineSpan.StartLinePosition.Character + 1 : 0,
            Recommendation = recommendation
        };
    }
}
