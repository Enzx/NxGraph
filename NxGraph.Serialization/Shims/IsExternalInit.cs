#if NETSTANDARD2_1
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Marker the compiler requires to emit init-only setters (records, <c>init</c>
    /// properties). Present in net5.0+; absent from netstandard2.1, so this assembly
    /// carries its own copy — the sibling assemblies' copies are internal.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
