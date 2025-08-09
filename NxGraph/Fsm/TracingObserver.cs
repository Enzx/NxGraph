using System.Diagnostics;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

public sealed class TracingObserver : IAsyncStateObserver
{
    private readonly DiagnosticListener _listener = new("NxGraph");

    public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
    {
        if (_listener.IsEnabled("NxGraph.Fsm.StateEntered"))
        {
            _listener.Write("NxGraph.Fsm.StateEntered", new { NodeId = id });
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
    {
        if (_listener.IsEnabled("NxGraph.Fsm.StateExited"))
        {
            _listener.Write("NxGraph.Fsm.StateExited", new { NodeId = id });
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
    {
        if (_listener.IsEnabled("NxGraph.Fsm.Transition"))
        {
            _listener.Write("NxGraph.Fsm.Transition", new { From = from, To = to });
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateFailed(NodeId id, Exception ex, CancellationToken ct = default)
    {
        if (_listener.IsEnabled("NxGraph.Fsm.StateFailed"))
        {
            _listener.Write("NxGraph.Fsm.StateFailed", new { NodeId = id, Exception = ex });
        }

        return ValueTask.CompletedTask;
    }
}