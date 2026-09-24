using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Computes the stable id used to identify a method across the P/Invoke analysis stage and the parallel emission
/// workers that later look up its lowered import or callback in a <see cref="CPPPInvokePlan"/>.
/// </summary>
public static class CPPPInvokeMethodIds {
    /// <summary>
    /// Returns the documentation-comment id used to match one method across the analysis stage and parallel emission
    /// workers.
    /// </summary>
    /// <param name="method">Method symbol from any compilation of the project.</param>
    /// <returns>An id such as <c>M:Native.User32.GetCursorPos(Native.NativePoint@)</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="method"/> has no documentation-comment id (Roslyn returns none for certain synthetic or
    /// malformed symbols).
    /// </exception>
    public static string Get(IMethodSymbol method) {
        if (method == null) {
            throw new ArgumentNullException(nameof(method));
        }

        string id = method.OriginalDefinition.GetDocumentationCommentId();
        if (string.IsNullOrWhiteSpace(id)) {
            throw new InvalidOperationException($"Method '{method.ToDisplayString()}' has no documentation-comment id.");
        }

        return id;
    }
}
