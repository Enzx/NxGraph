namespace NxFSM.Examples.BlackboardDemo;

/// <summary>
/// The agent: the entity that *has* the state machine and the blackboards. Identity and
/// innate traits live here; per-run working memory lives on the boards.
/// </summary>
public sealed class Guard(string name, int patrolDirection)
{
    public string Name { get; } = name;

    /// <summary>+1 patrols clockwise, -1 counter-clockwise.</summary>
    public int PatrolDirection { get; } = patrolDirection;
}
