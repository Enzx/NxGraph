namespace NxGraph.Graphs;

public readonly struct NodeId : IEquatable<NodeId>
{
    private NodeId(int index)
    {
        Index = index;
    }

    private NodeId(NodeId id, string name)
        : this(id.Index)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public NodeId(NodeId id)
        : this(id, id.Name)
    {
    }

    public readonly int Index;

    public readonly string Name { get; private init; } = string.Empty;

    public override string ToString()
    {
        return $"{Name}({Index})";
    }

    public bool Equals(NodeId other)
    {
        return Index == other.Index;
    }

    public override bool Equals(object? obj)
    {
        return obj is NodeId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Index;
    }

    public static bool operator ==(NodeId left, NodeId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(NodeId left, NodeId right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Represents the default NodeId with an index of -1 and a name of "Default".
    /// </summary>
    public static NodeId Default => new(-1) { Name = "Default" };

    /// <summary>
    /// Represents the start NodeId with an index of 0 and a name of "Start".
    /// </summary>
    public static NodeId Start => new(0);


    internal NodeId Next()
    {
        if (Index == int.MaxValue)
        {
            throw new InvalidOperationException("Cannot increment NodeId beyond maximum value.");
        }

        return new NodeId(Index + 1);
    }

    /// <summary>
    /// Creates a new NodeId with the same index as this one, but with a specified name.
    /// </summary>
    /// <param name="name">The name to assign to the new NodeId.</param>
    /// <returns>A new NodeId with the same index as this one, but with the specified name.</returns>
    /// <exception cref="ArgumentException">Thrown if the provided name is null or empty.</exception>
    public NodeId WithName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Name cannot be null or empty.", nameof(name));
        }

        return new NodeId(this, name);
    }
}