using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// An interface for observing state changes in a finite state machine.
/// This interface allows for asynchronous notification of state transitions, entry, and exit events.
/// Implementations of this interface can be used to monitor the state machine's behavior
/// </summary>
/// <example>
///  Example usage:
///  ```csharp
///  public class MyStateObserver : IAsyncStateObserver
///  {
///    public async ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
///    {
///       Console.WriteLine($"State {id} entered.");
///    }
/// 
///    public async ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
///    {
///       Console.WriteLine($"State {id} exited.");
///    }
/// 
///    public async ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
///    {
///       Console.WriteLine($"Transition from {from} to {to}.");
///    }
///  }
///  var observer = new MyStateObserver();
///  var stateMachine = new StateMachine(graph, observer);
///  await stateMachine.ExecuteAsync();
///  ```
/// </example>
public interface IAsyncStateMachineObserver
{
    ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnStateFailed(NodeId id, Exception ex, CancellationToken ct = default)
    {
        return default;
    }

    //--- State Machine Lifecycle Events ---

    ValueTask OnStateMachineReset(NodeId graphId, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnStateMachineStarted(NodeId graphId, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnStateMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnStateMachineCancelled(NodeId graphId, CancellationToken ct = default)
    {
        return default;
    }

    ValueTask StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next,
        CancellationToken ct = default)
    {
        return default;
    }

    ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct)
    {
        return default;
    }
}