using System.Runtime.CompilerServices;

namespace NxGraph.Serialization;

/// <summary>
/// Argument guards whose BCL equivalents postdate netstandard2.1. Unlike the files under
/// <c>Shims/</c> this compiles into both target frameworks: the call sites are shared, so the
/// helper — not the caller — carries the conditional.
/// </summary>
internal static class Guard
{
    /// <summary>
    /// netstandard2.1 stand-in for <c>ArgumentException.ThrowIfNullOrEmpty</c> (net7.0+),
    /// throwing the same two exception types for the same two cases.
    /// </summary>
    public static void NotNullOrEmpty(
        string? value,
        [CallerArgumentExpression("value")] string? paramName = null)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrEmpty(value, paramName);
#else
        if (value is null)
        {
            throw new System.ArgumentNullException(paramName);
        }

        if (value.Length == 0)
        {
            throw new ArgumentException("The value cannot be an empty string.", paramName);
        }
#endif
    }
}

#if NETSTANDARD2_1
/// <summary>
/// Cancellable text-IO overloads that net8.0 has as instance methods and netstandard2.1 does
/// not. Declared in the serializers' own namespace so the call sites bind to them without an
/// import, and compiled only for netstandard2.1 so they can never shadow the real instance
/// methods on net8.0.
/// <para>
/// The token cannot interrupt an in-flight read or flush on this framework, so it is honored
/// at entry only — the observable difference is that a cancellation requested mid-operation
/// surfaces after it completes rather than during.
/// </para>
/// </summary>
internal static class TextIoPolyfills
{
    public static Task FlushAsync(this StreamWriter writer, CancellationToken ct) =>
        ct.IsCancellationRequested ? Task.FromCanceled(ct) : writer.FlushAsync();

    public static Task<string> ReadToEndAsync(this StreamReader reader, CancellationToken ct) =>
        ct.IsCancellationRequested ? Task.FromCanceled<string>(ct) : reader.ReadToEndAsync();
}
#endif
