namespace NxGraph.Graphs;

/// <summary>
/// Represents the outgoing edges of a node in a finite state machine (FSM) graph:
/// one success destination and an optional failure destination. Either side may be
/// <see cref="NodeId.Default"/>, meaning the machine terminates on that outcome.
/// </summary>
public readonly struct Transition
{
    public Transition(NodeId destination)
        : this(destination, NodeId.Default)
    {
    }

    public Transition(NodeId destination, NodeId failureDestination)
    {
        Destination = destination;
        FailureDestination = failureDestination;
    }

    /// <summary>Where the machine goes when the node returns Success.</summary>
    public NodeId Destination { get; }

    /// <summary>
    /// Where the machine goes when the node returns Failure.
    /// <see cref="NodeId.Default"/> (the default) preserves the classic behaviour:
    /// a failing node terminates the machine with <c>Result.Failure</c>.
    /// </summary>
    public NodeId FailureDestination { get; }

    public static readonly Transition Empty = new(NodeId.Default);

    /// <summary>True when the success edge is unset (the node is terminal on success).</summary>
    public bool IsEmpty => Destination == NodeId.Default;

    /// <summary>True when a failure edge is present.</summary>
    public bool HasFailureDestination => FailureDestination != NodeId.Default;
}
