using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Compiles a C# snippet in memory and runs one of its public static methods as ordinary managed code, so tests can
/// compare the managed result (including real managed P/Invoke) with the natively compiled output of the same source.
/// Every call loads the snippet into its own collectible load context, so static fields always start fresh and the
/// assembly is unloaded afterwards.
/// </summary>
public static class CPPManagedSnippetRunner {
    /// <summary>
    /// Compiles <paramref name="source"/>, invokes the parameterless public static method, and returns its value as a
    /// <see cref="long"/>.
    /// </summary>
    /// <param name="source">C# source text to compile (unsafe code allowed).</param>
    /// <param name="typeName">Full metadata name of the type that declares the method.</param>
    /// <param name="methodName">Name of the public static, parameterless method to invoke.</param>
    /// <returns>The method's return value converted to <see cref="long"/>.</returns>
    /// <exception cref="InvalidOperationException">The snippet failed to emit, or the type or method was not found.</exception>
    public static long Run(string source, string typeName, string methodName) {
        CSharpCompilation compilation = CPPPInvokeTestCompilation.Create(source);
        using MemoryStream assemblyStream = new MemoryStream();
        EmitResult emitResult = compilation.Emit(assemblyStream);
        if (!emitResult.Success) {
            throw new InvalidOperationException(
                "Managed snippet emission failed: " + string.Join(Environment.NewLine, emitResult.Diagnostics));
        }

        assemblyStream.Position = 0;
        AssemblyLoadContext loadContext = new AssemblyLoadContext("CPPManagedSnippet-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try {
            Assembly assembly = loadContext.LoadFromStream(assemblyStream);
            Type type = assembly.GetType(typeName)
                ?? throw new InvalidOperationException("Managed snippet type not found: " + typeName);
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)
                ?? throw new InvalidOperationException("Managed snippet method not found: " + typeName + "." + methodName);
            return Convert.ToInt64(method.Invoke(null, null));
        } finally {
            loadContext.Unload();
        }
    }
}
