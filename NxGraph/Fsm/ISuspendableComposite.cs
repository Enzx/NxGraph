namespace NxGraph.Fsm;

/// <summary>
/// Deep-suspend capture contract for composite node logic. <c>SuspendDeep()</c> on
/// <see cref="Async.AsyncStateMachine"/>/<see cref="StateMachine"/> walks the machine's own
/// graph once and collects a <see cref="CompositeSnapshot"/> from every logic node that
/// implements this interface; <c>ResumeDeep(...)</c> hands each captured entry back. The
/// machine walk never recurses — a composite recurses by calling <c>SuspendDeep()</c> /
/// <c>ResumeDeep(...)</c> on its own child machine(s), exactly how the blackboard stamping
/// walk stops at settable nodes and lets machines re-walk their own graphs.
/// <para>
/// The rule for implementing it: a composite implements <see cref="ISuspendableComposite"/>
/// <b>iff</b> it persists visit or cross-visit state across <c>Execute</c>/<c>ExecuteAsync</c>
/// calls (history's remembered child position, a sync RoundPerTick composite's mid-visit
/// bookkeeping, a nested sync machine mid-run between parent ticks). Composites that run
/// their children to terminal inside one execution and keep no durable fields — the async
/// parallel composites — must not implement it: absence of capture is correct, not a gap.
/// </para>
/// <para>
/// <b>Container contract.</b> This is the third leg of the contract for custom container
/// nodes, beside <see cref="Graphs.ISubGraphProvider"/> (so the agent walk reaches child
/// graphs) and <see cref="Blackboards.IBlackboardSettable"/> forwarding (so the stamped
/// context reaches child machines): containers that hold durable position should also
/// implement <see cref="ISuspendableComposite"/> so deep suspend can capture it. A container
/// that does not implement it contributes nothing to the deep snapshot and re-enters fresh
/// after a resume — today's shallow behavior.
/// </para>
/// </summary>
public interface ISuspendableComposite
{
    /// <summary>
    /// Captures this composite's internal state into a <see cref="CompositeSnapshot"/> whose
    /// <see cref="CompositeSnapshot.NodeIndex"/> is <paramref name="nodeIndex"/> (the owner
    /// node's index in the capturing machine's graph). Child/region machines are captured via
    /// their own <c>SuspendDeep()</c>, in region order. Cold path — allocation is expected.
    /// </summary>
    CompositeSnapshot SuspendComposite(int nodeIndex);

    /// <summary>
    /// Restores a snapshot produced by <see cref="SuspendComposite"/> onto this composite.
    /// Implementations must validate the entry's shape (child snapshot count, done-bit count)
    /// against their own structure — throwing <see cref="InvalidOperationException"/> naming
    /// <see cref="CompositeSnapshot.NodeIndex"/> on mismatch — and deep-resume their child
    /// machines, which re-run the full resume validation recursively. Derivable bookkeeping
    /// (remaining region count, failure aggregation) is recomputed here, never trusted from
    /// the wire.
    /// </summary>
    void ResumeComposite(CompositeSnapshot snapshot);
}
