namespace NxGraph.Graphs;

public readonly struct NodeId : IEquatable<NodeId>
{
    private NodeId(int value) => Value = value;
    internal int Value { get; }

    public readonly string Name { get; init; } = string.Empty;
    public override string ToString() => $"{Name}({Value})";
    public bool Equals(NodeId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is NodeId other && Equals(other);
    public override int GetHashCode() => Value;
    public static bool operator ==(NodeId left, NodeId right) => left.Equals(right);
    public static bool operator !=(NodeId left, NodeId right) => !left.Equals(right);
    public static NodeId Default => default;

    internal NodeId Next()
    {
        if (Value == int.MaxValue)
        {
            throw new InvalidOperationException("Cannot increment NodeId beyond maximum value.");
        }
        return new NodeId(Value + 1);
    }
}