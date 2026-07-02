namespace NxGraph.Blackboards;

/// <summary>
/// Stamping channel: receives the machine's routed <see cref="BlackboardContext"/>.
/// Implemented by the state base classes, blackboard-driven directors, and the state
/// machines themselves (so composite/nested machines forward the context into their
/// child graphs). Runners re-stamp at every run start.
/// </summary>
public interface IBlackboardSettable
{
    /// <summary>Receives the routed context. Called at bind time and re-stamped at run start.</summary>
    void SetBlackboards(in BlackboardContext context);
}

/// <summary>
/// Binding surface: accepts one board and routes it into the receiver's context by the
/// board's schema scope. Implemented by the state-machine bases; the target of the
/// <c>WithBlackboard(...)</c> fluent extension.
/// </summary>
public interface IBlackboardBindable
{
    /// <summary>Binds <paramref name="blackboard"/> into the scope slot its schema declares (replace semantics).</summary>
    void SetBlackboard(Blackboard blackboard);
}
