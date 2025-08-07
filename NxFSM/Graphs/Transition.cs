namespace NxFSM.Graphs;

public readonly record struct Transition(NodeId Destination)
{
    public static readonly Transition Empty = new(default);
    public bool IsEmpty => Destination.Value == default;
}