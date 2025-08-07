using NxFSM.Graphs;

namespace NxFSM.Fsm;

/// <summary>
/// A director is a node that selects the next node to run based on some logic.
/// </summary>
public interface IDirector
{
    /// <summary>
    /// Selects the next node to run based on some logic.
    /// </summary>
    /// <returns></returns>
    NodeId SelectNext();
}