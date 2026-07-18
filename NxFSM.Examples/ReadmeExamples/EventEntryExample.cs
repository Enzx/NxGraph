using System.Threading.Channels;
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// Event entry points (README "Event entry points"): one graph responding to several
/// externally-raised, typed events, each entering the flow at its own entry chain. An event
/// is a run trigger — one event, one run — and the payload travels through an ordinary
/// Graph-scoped <see cref="BlackboardKey{T}"/>, so durability falls out of the blackboard.
/// </summary>
public static class EventEntryExample
{
    public sealed record OrderPlaced(string OrderId, decimal Amount);

    public readonly record struct OrderCanceled(string OrderId);

    private static ValueTask<Result> ReserveStockAsync(OrderPlaced order)
    {
        Console.WriteLine($"  Reserving stock for {order.OrderId}");
        return ResultHelpers.Success;
    }

    private static ValueTask<Result> ChargeAsync(OrderPlaced order)
    {
        Console.WriteLine($"  Charging {order.Amount:C}");
        return ResultHelpers.Success;
    }

    private static ValueTask<Result> RefundAsync(OrderCanceled order)
    {
        Console.WriteLine($"  Refunding {order.OrderId}");
        return ResultHelpers.Success;
    }

    private static ValueTask<Result> LogUnsolicitedAsync()
    {
        Console.WriteLine("  Plain run — no event raised.");
        return ResultHelpers.Success;
    }

    public static async ValueTask RunAsync()
    {
        Console.WriteLine("=== Event entry points (typed multi-entry dispatch) ===");

        var shop = new BlackboardSchema("shop"); // Graph scope (default)
        BlackboardKey<OrderPlaced> orderPlaced = shop.Register<OrderPlaced>("orderPlaced");
        BlackboardKey<OrderCanceled> orderCanceled = shop.Register<OrderCanceled>("orderCanceled");

        Graph graph = GraphBuilder.StartWithEvents()
            .On(orderPlaced, e => e
                .ToAsync(orderPlaced, (order, bb, ct) => ReserveStockAsync(order)) // payload via the consumer sugar
                .ToAsync((bb, ct) => ChargeAsync(bb.Get(orderPlaced)))             // or via bb.Get
                .WithOutcome(1, "Placed"))
            .On(orderCanceled, e => e
                .ToAsync(orderCanceled, (order, bb, ct) => RefundAsync(order))
                .WithOutcome(2, "Canceled"))
            .Otherwise(e => e.ToAsync((bb, ct) => LogUnsolicitedAsync()))          // optional plain-run entry
            .WithSchema(shop)
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop));

        Result placed = await machine.ExecuteAsync(new OrderPlaced("o-1", 42m)); // dispatch by CLR event type
        Console.WriteLine($"  OrderPlaced run: {placed}, outcome: {machine.LastOutcomeName}");

        Result canceled = await machine.ExecuteAsync(new OrderCanceled("o-1"));
        Console.WriteLine($"  OrderCanceled run: {canceled}");

        Result plain = await machine.ExecuteAsync(); // routes to the Otherwise chain
        Console.WriteLine($"  Plain run: {plain}");

        // Host-side buffering: feed the machine from your own queue — three lines with a Channel<T>.
        Channel<OrderPlaced> queue = Channel.CreateUnbounded<OrderPlaced>();
        queue.Writer.TryWrite(new OrderPlaced("o-2", 7m));
        queue.Writer.Complete();
        await foreach (OrderPlaced evt in queue.Reader.ReadAllAsync())
        {
            await machine.ExecuteAsync(evt); // one queued event = one run
        }

        RunSync(shop, orderPlaced, orderCanceled);
    }

    private static void RunSync(BlackboardSchema shop, BlackboardKey<OrderPlaced> orderPlaced,
        BlackboardKey<OrderCanceled> orderCanceled)
    {
        Console.WriteLine("=== Event entry points (sync twin) ===");

        // Sync-authored twin over the same schema and keys — the sync runtime needs ILogic nodes.
        Graph graph = GraphBuilder.StartWithEvents()
            .On(orderPlaced, e => e
                .To(orderPlaced, (order, bb) =>
                {
                    Console.WriteLine($"  Reserving stock for {order.OrderId}");
                    return Result.Success;
                })
                .WithOutcome(1, "Placed"))
            .On(orderCanceled, e => e
                .To(orderCanceled, (order, bb) =>
                {
                    Console.WriteLine($"  Refunding {order.OrderId}");
                    return Result.Success;
                })
                .WithOutcome(2, "Canceled"))
            .WithSchema(shop)
            .Build();

        // The sync raise arms the run and advances one tick; plain Execute() ticks continue it.
        StateMachine machine = graph.ToStateMachine().WithBlackboard(new Blackboard(shop));
        Result result = machine.Execute(new OrderCanceled("o-3"));
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Console.WriteLine($"  Sync OrderCanceled run: {result}, outcome: {machine.LastOutcomeName}");
    }
}
