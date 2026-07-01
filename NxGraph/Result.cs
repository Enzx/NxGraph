using System.Runtime.CompilerServices;

namespace NxGraph;

/// <summary>
/// Represents the outcome of a state-machine operation.
/// <para>
/// Use the static fields <see cref="Success"/>, <see cref="Failure"/>, and
/// <see cref="InProgress"/> for standard results, or the factory methods
/// <see cref="Ok"/> / <see cref="Fail"/> to attach a diagnostic message.
/// </para>
/// <para>
/// Equality is based on <see cref="Code"/> only — two results with the same
/// code but different messages are considered equal. This keeps assertions
/// like <c>Is.EqualTo(Result.Success)</c> working regardless of message.
/// </para>
/// </summary>
public readonly struct Result : IEquatable<Result>
{
    /// <summary>
    /// Internal status code that distinguishes result kinds.
    /// </summary>
    public enum StatusCode : byte
    {
        /// <summary>The operation completed successfully.</summary>
        Success = 0,

        /// <summary>The operation failed.</summary>
        Failure = 1,

        /// <summary>
        /// The operation has not finished yet — it is a status report, not a command.
        /// <para>
        /// Returned by the stepped runtimes (<see cref="Fsm.StateMachine.Execute"/>,
        /// <c>AsyncStateMachine.StepAsync</c>) while more nodes remain, and by sync node
        /// logic to indicate the current node needs another tick (e.g. a multi-frame
        /// Unity state such as a timer or animation wait).
        /// </para>
        /// </summary>
        InProgress = 2,

        /// <inheritdoc cref="InProgress"/>
        [Obsolete("Renamed to StatusCode.InProgress — 'Continue' read like a command rather than a status.")]
        Continue = 2,
    }

    // ── Static singletons ───────────────────────────────────────────────

    /// <summary>A successful result with no message.</summary>
    public static readonly Result Success = new(StatusCode.Success);

    /// <summary>A failed result with no message.</summary>
    public static readonly Result Failure = new(StatusCode.Failure);

    /// <summary>
    /// Indicates the state machine has more work to do.
    /// Returned by the stepped runtimes during frame-stepped execution; async node logic
    /// must <b>never</b> return this value (sync multi-frame states may).
    /// </summary>
    public static readonly Result InProgress = new(StatusCode.InProgress);

    /// <inheritdoc cref="InProgress"/>
    [Obsolete("Renamed to Result.InProgress — 'Continue' read like a command rather than a status.")]
    public static readonly Result Continue = new(StatusCode.InProgress);

    // ── Factory methods ─────────────────────────────────────────────────

    /// <summary>Creates a successful result with an optional diagnostic message.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result Ok(string? message = null) => new(StatusCode.Success, message);

    /// <summary>Creates a failed result with an optional diagnostic message.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result Fail(string? message = null) => new(StatusCode.Failure, message);

    // ── Instance state ──────────────────────────────────────────────────

    /// <summary>The status code of this result.</summary>
    public StatusCode Code { get; }

    /// <summary>Optional diagnostic message (may be <see langword="null"/>).</summary>
    public string? Message { get; }

    // ── Convenience properties ──────────────────────────────────────────

    /// <summary><see langword="true"/> when <see cref="Code"/> is <see cref="StatusCode.Success"/>.</summary>
    public bool IsSuccess
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Code == StatusCode.Success;
    }

    /// <summary><see langword="true"/> when <see cref="Code"/> is <see cref="StatusCode.Failure"/>.</summary>
    public bool IsFailure
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Code == StatusCode.Failure;
    }

    /// <summary><see langword="true"/> when <see cref="Code"/> is <see cref="StatusCode.InProgress"/>.</summary>
    public bool IsInProgress
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Code == StatusCode.InProgress;
    }

    /// <summary>
    /// <see langword="true"/> when the state machine has finished
    /// (<see cref="StatusCode.Success"/> or <see cref="StatusCode.Failure"/>).
    /// Equivalent to <c>Code != StatusCode.InProgress</c>.
    /// </summary>
    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Code != StatusCode.InProgress;
    }

    // ── Constructor ─────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Result(StatusCode code, string? message = null)
    {
        Code = code;
        Message = message;
    }

    // ── Equality (based on Code only) ───────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Result other) => Code == other.Code;

    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    public override int GetHashCode() => (int)Code;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Result left, Result right) => left.Code == right.Code;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Result left, Result right) => left.Code != right.Code;

    // ── ToString ────────────────────────────────────────────────────────

    public override string ToString() =>
        Message is not null ? $"{Code}: {Message}" : Code.ToString();
}
