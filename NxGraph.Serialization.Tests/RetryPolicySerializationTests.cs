using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

[TestFixture]
[Category("serialization")]
public class RetryPolicySerializationTests
{
    private readonly GraphSerializer _serializer = new(new DummyLogicTextCodec());

    private static Graph BuildGraphWithRetry()
    {
        GraphBuilder builder = new();
        NodeId start = builder.AddNode(new DummyState { Data = "flaky" }, isStart: true);
        NodeId next = builder.AddNode(new DummyState { Data = "done" });
        builder.AddTransition(start, next);
        builder.SetRetryPolicy(start,
            new RetryPolicy(3, TimeSpan.FromMilliseconds(250), BackoffKind.Exponential));
        return builder.Build(throwOnError: false);
    }

    [Test]
    public async Task json_roundtrip_preserves_retry_policies()
    {
        Graph graph = BuildGraphWithRetry();

        await using MemoryStream stream = new();
        await _serializer.ToJsonAsync(graph, stream);
        stream.Position = 0;
        Graph roundTripped = await _serializer.FromJsonAsync(stream);

        RetryPolicy[]? policies = roundTripped.RetryPolicies;
        Assert.That(policies, Is.Not.Null);
        RetryPolicy policy = policies![0];
        Assert.Multiple(() =>
        {
            Assert.That(policy.MaxAttempts, Is.EqualTo(3));
            Assert.That(policy.Backoff, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
            Assert.That(policy.BackoffKind, Is.EqualTo(BackoffKind.Exponential));
            Assert.That(policies[1].MaxAttempts, Is.Zero, "Node without a policy stays policy-free.");
        });
    }

    [Test]
    public async Task binary_roundtrip_preserves_retry_policies()
    {
        Graph graph = BuildGraphWithRetry();

        await using MemoryStream stream = new();
        await _serializer.ToBinaryAsync(graph, stream);
        stream.Position = 0;
        Graph roundTripped = await _serializer.FromBinaryAsync(stream);

        RetryPolicy[]? policies = roundTripped.RetryPolicies;
        Assert.That(policies, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(policies![0].MaxAttempts, Is.EqualTo(3));
            Assert.That(policies[0].BackoffKind, Is.EqualTo(BackoffKind.Exponential));
        });
    }

    [Test]
    public async Task json_roundtrip_preserves_outcome_codes_and_names()
    {
        GraphBuilder builder = new();
        NodeId start = builder.AddNode(new DummyState { Data = "start" }, isStart: true);
        NodeId done = builder.AddNode(new DummyState { Data = "done" });
        builder.AddTransition(start, done);
        builder.SetOutcome(done, 7, "Approved");
        Graph graph = builder.Build(throwOnError: false);

        await using MemoryStream stream = new();
        await _serializer.ToJsonAsync(graph, stream);
        stream.Position = 0;
        Graph roundTripped = await _serializer.FromJsonAsync(stream);

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.OutcomeCodes, Is.Not.Null);
            Assert.That(roundTripped.OutcomeCodes![1], Is.EqualTo(7));
            Assert.That(roundTripped.OutcomeNames, Is.Not.Null);
            Assert.That(roundTripped.OutcomeNames![7], Is.EqualTo("Approved"));
        });
    }

    [Test]
    public async Task graph_without_retry_policies_roundtrips_with_none()
    {
        GraphBuilder builder = new();
        builder.AddNode(new DummyState { Data = "only" }, isStart: true);
        Graph graph = builder.Build(throwOnError: false);

        await using MemoryStream stream = new();
        await _serializer.ToBinaryAsync(graph, stream);
        stream.Position = 0;
        Graph roundTripped = await _serializer.FromBinaryAsync(stream);

        Assert.That(roundTripped.RetryPolicies, Is.Null);
    }
}
