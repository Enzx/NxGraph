namespace NxGraph.Fsm;

/// <summary>Outcome policy when a timeout occurs.</summary>
public enum TimeoutBehavior
{
    /// <summary>Return <see cref="Result.Failure"/> from the node.</summary>
    Fail = 0,

    /// <summary>Throw a <see cref="TimeoutException"/>.</summary>
    Throw = 1,
}