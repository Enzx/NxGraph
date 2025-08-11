using System.Diagnostics;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// An observer that traces the execution of a state machine using OpenTelemetry.
/// </summary>
public sealed class TracingObserver : IAsyncStateMachineObserver
{
    private static readonly ActivitySource Source = new("NxGraph");

    private readonly AsyncLocal<Activity?> _current = new();

    public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
    {
        if (!Source.HasListeners()) return ValueTask.CompletedTask;

        // Parent links to ambient Activity if any.
        Activity? activity = Source.StartActivity(
            $"Node {id.Name} ({id.Index})",
            ActivityKind.Internal,
            parentId: Activity.Current?.Id);
        if (activity is null)
        {
            return ValueTask.CompletedTask;
        }

        activity.SetTag("nx.node.name", id.Name);
        activity.SetTag("nx.node.index", id.Index);
        _current.Value = activity;

        return ValueTask.CompletedTask;
    }

    public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
    {
        Activity? act = _current.Value;
        if (act is null) return ValueTask.CompletedTask;

        act.SetTag("nx.transition.to.name", to.Name);
        act.SetTag("nx.transition.to.index", to.Index);

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateFailed(NodeId id, Exception ex, CancellationToken ct = default)
    {
        Activity? act = _current.Value;
        if (act is null) return ValueTask.CompletedTask;

        act.SetTag("exception.type", ex.GetType().FullName);
        act.SetTag("exception.message", ex.Message);
        act.SetTag("exception.stacktrace", ex.StackTrace);
        act.SetStatus(ActivityStatusCode.Error, ex.Message);

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
    {
        if (!Source.HasListeners())
        {
            return ValueTask.CompletedTask;
        }

        Activity? act = _current.Value;
        if (act is null)
        {
            return ValueTask.CompletedTask;
        }

        act.SetTag("nx.result", "Success"); // or set in failure path as Error above
        act.Stop();
        _current.Value = null;

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateMachineReset(NodeId graphId, CancellationToken ct = default)
    {
        if (!Source.HasListeners())
        {
            return ValueTask.CompletedTask;
        }

        Activity? act = _current.Value;
        if (act is not null)
        {
            act.SetTag("nx.graph.reset", true);
            act.Stop();
            _current.Value = null;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateMachineStarted(NodeId graphId, CancellationToken ct = default)
    {
        if (!Source.HasListeners())
        {
            return ValueTask.CompletedTask;
        }

        Activity? act = Source.StartActivity(
            $"StateMachine {graphId.Name} ({graphId.Index})");
        if (act is null)
        {
            return ValueTask.CompletedTask;
        }

        act.SetTag("nx.graph.id", graphId.ToString());
        _current.Value = act;

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
    {
        Activity? act = _current.Value;
        if (act is null)
        {
            return ValueTask.CompletedTask;
        }

        act.SetTag("nx.graph.result", result.ToString());
        act.Stop();
        _current.Value = null;

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateMachineCancelled(NodeId graphId, CancellationToken ct = default)
    {
        Activity? act = _current.Value;
        if (act is null)
        {
            return ValueTask.CompletedTask;
        }

        act.SetTag("nx.graph.cancelled", true);
        act.Stop();
        _current.Value = null;

        return ValueTask.CompletedTask;
    }

    public ValueTask StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next,
        CancellationToken ct = default)
    {
        Activity? act = _current.Value;
        if (act is null)
        {
            return ValueTask.CompletedTask;
        }

        act.SetTag("nx.graph.status.prev", prev.ToString());
        act.SetTag("nx.graph.status.next", next.ToString());

        return ValueTask.CompletedTask;
    }
}