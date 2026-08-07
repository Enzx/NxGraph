#if NETSTANDARD2_1
// The alias makes the netstandard2.1 build resolve the simple name
// `ArgumentNullException` to the shim below, so the ~30 `ArgumentNullException.ThrowIfNull`
// call sites across the serialization assemblies compile unchanged on both TFMs.
// Safe because these assemblies never write `new ArgumentNullException(...)`, never catch it,
// and never cref it — verified before the alias was introduced. Qualified `System.` uses are
// unaffected by a simple-name alias, which is how the shim still throws the real exception.
global using ArgumentNullException = NxGraph.Serialization.Abstraction.Shims.ArgumentNullExceptionShim;

using System.Runtime.CompilerServices;

namespace NxGraph.Serialization.Abstraction.Shims
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
