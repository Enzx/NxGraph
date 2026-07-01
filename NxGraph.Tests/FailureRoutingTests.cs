using NxGraph.Authoring;
using NxGraph.Diagnostics.Export;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class FailureRoutingTests
{
    private sealed class RecordingAsyncObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> Events = [];

        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
        {
            Events.Add($"entered:{id.Index}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
        {
            Events.Add($"exited:{id.Index}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStateFailed(NodeId id, Exception? ex, CancellationToken ct = default)
        {
            Events.Add($"failed:{id.Index}:{(ex is null ? "null" : ex.GetType().Name)}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
        {
            Events.Add($"transition:{from.Index}->{to.Index}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSyncObserver : IStateMachineObserver
    {
        public readonly List<string> Events = [];

        public void OnStateEntered(NodeId id) => Events.Add($"entered:{id.Index}");
        public void OnStateExited(NodeId id) => Events.Add($"exited:{id.Index}");
        public void OnStateFailed(NodeId id, Exception? ex) => Events.Add($"failed:{id.Index}");
        public void OnTransition(NodeId from, NodeId to) => Events.Add($"transition:{from.Index}->{to.Index}");
    }

    [Test]
    public async Task async_failure_routes_to_handler_and_machine_succeeds()
    {
        bool cleanupRan = false;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .OnErrorAsync(_ =>
            {
                cleanupRan = true;
                return ResultHelpers.Success;
            })
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(cleanupRan, Is.True);
        });
    }

    [Test]
    public async Task async_failure_routing_fires_failed_transition_entered_in_order()
    {
        RecordingAsyncObserver observer = new();
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .OnErrorAsync(_ => ResultHelpers.Success)
            .Build();

        await graph.ToAsyncStateMachine(observer).ExecuteAsync();

        Assert.That(observer.Events, Is.EqualTo(new[]
        {
            "entered:0",
            "failed:0:null",
            "transition:0->1",
            "entered:1",
            "exited:1",
        }));
    }

    [Test]
    public async Task async_failure_without_error_edge_still_terminates_with_failure()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Failure));
    }

    [Test]
    public void sync_failure_routes_to_handler_and_machine_succeeds()
    {
        bool cleanupRan = false;
        Graph graph = GraphBuilder
            .StartWith(() => Result.Failure)
            .OnError(() =>
            {
                cleanupRan = true;
                return Result.Success;
            })
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(cleanupRan, Is.True);
        });
    }

    [Test]
    public void sync_failure_routing_fires_failed_transition_entered_in_order()
    {
        RecordingSyncObserver observer = new();
        Graph graph = GraphBuilder
            .StartWith(() => Result.Failure)
            .OnError(() => Result.Success)
            .Build();

        StateMachine machine = graph.ToStateMachine(observer);
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.That(observer.Events, Is.EqualTo(new[]
        {
            "entered:0",
            "failed:0",
            "transition:0->1",
            "entered:1",
            "exited:1",
        }));
    }

    [Test]
    public async Task failure_handler_can_continue_the_success_chain()
    {
        bool recovered = false;
        StateToken start = GraphBuilder.StartWithAsync(_ => ResultHelpers.Failure);
        StateToken handler = start.Builder.TokenFor(
            start.Builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success)));
        handler.ToAsync(_ =>
        {
            recovered = true;
            return ResultHelpers.Success;
        });

        Graph graph = start.OnError(handler).Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(recovered, Is.True);
        });
    }

    [Test]
    public void duplicate_failure_edge_throws()
    {
        StateToken start = GraphBuilder.StartWith(() => Result.Failure)
            .OnError(() => Result.Success);

        Assert.Throws<InvalidOperationException>(() => start.OnError(() => Result.Success));
    }

    [Test]
    public void validator_walks_failure_edges_for_reachability()
    {
        GraphBuilder builder = new();
        NodeId start = builder.AddNode(new RelayState(() => Result.Failure), isStart: true);
        NodeId handler = builder.AddNode(new RelayState(() => Result.Success));
        builder.AddFailureTransition(start, handler);
        Graph graph = builder.Build(throwOnError: false);

        GraphValidationResult result = graph.Validate(new GraphValidationOptions
        {
            AllNodes = builder.GetAllNodeIds(),
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase)), Is.False,
                "The failure handler is reachable via the failure edge and must not be flagged.");
        });
    }

    [Test]
    public void mermaid_export_emits_dashed_fail_edge()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Failure).SetName("Work")
            .OnError(() => Result.Success)
            .Build();

        string mermaid = graph.ToMermaid();
        Assert.That(mermaid, Does.Contain("-. fail .->"));
    }
}
