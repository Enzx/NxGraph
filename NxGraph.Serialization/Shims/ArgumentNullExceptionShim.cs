#if NETSTANDARD2_1
// See the sibling file in NxGraph.Serialization.Abstraction for why this alias is safe.
global using ArgumentNullException = NxGraph.Serialization.Shims.ArgumentNullExceptionShim;

// CallerArgumentExpressionAttribute is deliberately NOT redefined here: the Abstraction
// assembly's copy is already visible through the InternalsVisibleTo this project relies on
// for the entry-codec hooks, and a second source-level definition makes every use ambiguous
// (CS0436, an error under TreatWarningsAsErrors).
using System.Runtime.CompilerServices;

namespace NxGraph.Serialization.Shims
{
    /// <summary>
    /// netstandard2.1 stand-in for <c>System.ArgumentNullException.ThrowIfNull</c> (net6.0+).
    /// </summary>
    internal static class ArgumentNullExceptionShim
    {
        public static void ThrowIfNull(
            object? argument,
            [CallerArgumentExpression("argument")] string? paramName = null)
        {
            if (argument is null)
            {
                throw new System.ArgumentNullException(paramName);
            }
        }
    }
}
#endif
