namespace NxGraph.Diagnostics.Replay;

/// <summary>
/// The <b>asynchronous</b> half of a node's log-report channel: the slot the async runtimes
/// (<c>AsyncStateMachine</c>, <c>AsyncTokenMachine</c>) wire so the node can emit messages to
/// the machine observer's <c>OnLogReport</c>. The slot is <b>machine-owned</b>: every machine
/// reassigns it on every visit — its own callback from its observer, <see langword="null"/>
/// when it has no observer — so machines sharing one <c>Graph</c> each attribute reports to
/// their own observer and a stale callback never survives into a later run.
/// </summary>
public interface ILogReporter
{
    /// <summary>
    /// The machine-wired async report callback, or <see langword="null"/> when the running
    /// machine has no observer. Never invoke it outside a run — it belongs to whichever
    /// machine last visited this node.
    /// </summary>
    Func<string, CancellationToken, ValueTask>? LogReport { get; set; }
}

/// <summary>
/// The <b>synchronous</b> half of the same channel, for nodes the sync runtimes
/// (<c>StateMachine</c>, <c>TokenMachine</c>) can execute. The split is deliberate: a sync
/// node must not be forced to await, so the sync machines wire a plain
/// <see cref="Action{String}"/> here while the async machines wire
/// <see cref="ILogReporter.LogReport"/> — and each family <b>clears the other family's slot</b>
/// on every visit, because a node that reads both (preferring the sync one) would otherwise
/// deliver this run's reports through a callback a differently-typed machine left behind.
/// <para>
/// Implementing this is the sync half of the report capability, <b>not</b> a base-class
/// membership: <c>State</c> implements it, and so does any non-<c>State</c> node that owns a
/// report channel (the data-built branch states, which are plain <c>ILogic</c>/<c>IAsyncLogic</c>
/// implementations). The machines target this interface rather than <c>State</c> precisely so
/// the second group is reached; resolution goes through <c>LogicWrappers</c>, so a node behind a
/// timeout decorator is wired too.
/// </para>
/// <para>
/// Internal on purpose: it exposes a machine-owned mutable slot that no caller outside the
/// library may write, and keeping it internal leaves <c>State</c>'s public surface unchanged.
/// </para>
/// </summary>
internal interface ISyncLogReporter : ILogReporter
{
    /// <summary>
    /// The machine-wired sync report callback, or <see langword="null"/> when the running
    /// machine has no observer (which is what makes report-formatting nodes free there).
    /// </summary>
    Action<string>? SyncLogReport { get; set; }
}
