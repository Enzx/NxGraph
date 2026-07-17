using NxGraph;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// Step I/O ports (README "Step I/O (ports)"): a port is an ordinary Graph-scoped
/// <see cref="BlackboardKey{T}"/> — one per producing step, named for the datum. The port DSL
/// overloads read/write the port around the step lambda, so a produce → pipe → consume chain
/// needs no manual Get/Set and the graph stays a shareable template.
/// </summary>
public static class PortsExample
{
    private static class FlowIo
    {
        public static readonly BlackboardSchema Schema = new("flow-io"); // Graph scope (default)
        public static readonly BlackboardKey<string> Draft = Schema.Register<string>("draft", "");
        public static readonly BlackboardKey<string> Final = Schema.Register<string>("final", "");
    }

    private static Result Publish(string text)
    {
        Console.WriteLine($"  Publishing: {text}");
        return Result.Success;
    }

    private static ValueTask<Result> PublishAsync(string text)
    {
        return new ValueTask<Result>(Publish(text));
    }

    public static async ValueTask RunAsync()
    {
        Console.WriteLine("=== Step I/O Ports (async produce → pipe → consume) ===");

        // Async: produce → pipe → consume.
        Graph graph = GraphBuilder
            .Start()
            .ToAsync(FlowIo.Draft, (bb, ct) => new ValueTask<string>("a draft"))                  // producer: value → port
            .ToAsync(FlowIo.Draft, FlowIo.Final, (draft, bb, ct) => new ValueTask<string>($"[polished] {draft}")) // pipe
            .ToAsync(FlowIo.Final, (text, bb, ct) => PublishAsync(text))                          // consumer: port → Result
            .WithSchema(FlowIo.Schema)
            .Build();

        Result result = await graph.ToAsyncStateMachine()
            .WithBlackboard(new Blackboard(FlowIo.Schema))
            .ExecuteAsync();
        Console.WriteLine($"  Async port chain: {result}");

        RunSync();
    }

    private static void RunSync()
    {
        Console.WriteLine("=== Step I/O Ports (sync twin) ===");

        // Sync twin — the same shapes without the CancellationToken.
        Graph graph = GraphBuilder
            .Start()
            .To(FlowIo.Draft, bb => "a draft")
            .To(FlowIo.Draft, FlowIo.Final, (draft, bb) => $"[polished] {draft}")
            .To(FlowIo.Final, (text, bb) => Publish(text))
            .WithSchema(FlowIo.Schema)
            .Build();

        StateMachine machine = graph.ToStateMachine().WithBlackboard(new Blackboard(FlowIo.Schema));
        Result result = machine.Execute();
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Console.WriteLine($"  Sync port chain: {result}");
    }
}
