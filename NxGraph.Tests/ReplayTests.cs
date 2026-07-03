using NxGraph.Diagnostics.Replay;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

using Authoring;
using Fsm;
using Graphs;
using System.IO;

[TestFixture]
[Category("replay")]
public class ReplayTests
{
    private class EventSequenceVerifier
    {
        public readonly List<string> RecordedEvents = [];

        public void ProcessEvent(ReplayEvent evt)
        {
            switch (evt.Type)
            {
                case EventType.StateEntered:
                    RecordedEvents.Add($"Entered: {evt.SourceId}");
                    break;
                case EventType.StateExited:
                    RecordedEvents.Add($"Exited: {evt.SourceId}");
                    break;
                case EventType.Transition:
                    RecordedEvents.Add($"Transitioned from {evt.SourceId} to {evt.TargetId}");
                    break;
                case EventType.StateMachineStarted:
                    RecordedEvents.Add("FSM:Started");
                    break;
                case EventType.StateMachineCompleted:
                    RecordedEvents.Add($"FSM:Completed:result: {evt.Message}");
                    break;
                case EventType.StateMachineReset:
                    RecordedEvents.Add("FSM:Reset");
                    break;
                case EventType.StatusChanged:
                    RecordedEvents.Add($"FSM:StatusChanged:{evt.Message}");
                    break;
                case EventType.StateMachineCancelled:
                    RecordedEvents.Add("FSM:Cancelled");
                    break;
                case EventType.StateFailed:
                    break;
                case EventType.Log:
                    RecordedEvents.Add($"Log: {evt.SourceId} - {evt.Message}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(evt), evt.Type, null);
            }
        }
    }

    [Test]
    public async Task Should_Record_State_Machine_Execution()
    {
        // Arrange
        ReplayRecorder recorder = new();

        // Act
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(recorder);

        await fsm.ExecuteAsync();

        // Assert
        ReplayEvent[] events = recorder.GetEvents().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.Not.Empty);
            Assert.That(events.Count(e => e.Type == EventType.StateEntered), Is.EqualTo(2));
            Assert.That(events.Count(e => e.Type == EventType.StateExited), Is.EqualTo(2));
            Assert.That(events.Count(e => e.Type == EventType.Transition), Is.EqualTo(1));
            Assert.That(events.Count(e => e.Type == EventType.StateMachineStarted), Is.EqualTo(1));
            Assert.That(events.Count(e => e.Type == EventType.StateMachineCompleted), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_Serialize_And_Deserialize_Replay_Data()
    {
        // Arrange
        ReplayRecorder recorder = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(recorder);

        // Act    
        await fsm.ExecuteAsync();

        // Serialize
        StateMachineReplay replay = new(recorder.GetEvents().ToArray());
        byte[] serialized = replay.Serialize();

        // Deserialize
        ReplayEvent[] deserializedEvents = StateMachineReplay.Deserialize(serialized);

        // Assert
        Assert.That(deserializedEvents.Length, Is.EqualTo(recorder.GetEvents().Length));
        ReplayEvent[] events = recorder.GetEvents().ToArray();
        // Compare specific events
        for (int i = 0; i < deserializedEvents.Length; i++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(deserializedEvents[i].Type, Is.EqualTo(events[i].Type));
                Assert.That(deserializedEvents[i].SourceId.Index, Is.EqualTo(events[i].SourceId.Index));
                if (!events[i].TargetId.HasValue)
                {
                    return;
                }

                NodeId? nodeId = deserializedEvents[i].TargetId;
                if (nodeId != null)
                {
                    Assert.That(nodeId.Value.Index,
                        Is.EqualTo(events[i].TargetId?.Index));
                }
            });
        }
    }

    [Test]
    public async Task Should_Replay_Events_In_Correct_Sequence()
    {
        // Arrange
        ReplayRecorder recorder = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(recorder);

        // Act
        await fsm.ExecuteAsync();

        StateMachineReplay replay = new(recorder.GetEvents().ToArray());
        EventSequenceVerifier verifier = new();

        replay.ReplayAll(verifier.ProcessEvent);

        // Assert - check sequence matches what we expect
        Assert.That(verifier.RecordedEvents, Does.Contain("FSM:Started"));
        Assert.That(verifier.RecordedEvents, Does.Contain($"Entered: (0)"));
        Assert.That(verifier.RecordedEvents, Does.Contain($"Exited: (0)"));
        Assert.That(verifier.RecordedEvents, Does.Contain($"Transitioned from (0) to (1)"));
        Assert.That(verifier.RecordedEvents, Does.Contain($"Entered: (1)"));
        Assert.That(verifier.RecordedEvents, Does.Contain($"Exited: (1)"));
        Assert.That(verifier.RecordedEvents, Does.Contain($"FSM:Completed:result: Success"));

        // Verify correct sequence of operations
        int startedIdx = verifier.RecordedEvents.IndexOf("FSM:Started");
        int entered0Idx = verifier.RecordedEvents.FindIndex(e => e == "Entered: (0)");
        int exited0Idx = verifier.RecordedEvents.FindIndex(e => e == "Exited: (0)");
        int transitionIdx = verifier.RecordedEvents.FindIndex(e => e.StartsWith("Transitioned from"));
        int entered1Idx = verifier.RecordedEvents.FindIndex(e => e == "Entered: (1)");

        Assert.Multiple(() =>
        {
            Assert.That(startedIdx, Is.LessThan(entered0Idx));
            Assert.That(entered0Idx, Is.LessThan(exited0Idx));
            Assert.That(exited0Idx, Is.LessThan(transitionIdx));
            Assert.That(transitionIdx, Is.LessThan(entered1Idx));
        });
    }

    [Test]
    public async Task Should_Handle_Cancellation_Replay()
    {
        // Arrange
        ReplayRecorder recorder = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .WaitForAsync(1.Seconds())
            .ToAsyncStateMachine(recorder);

        // Act - Execute with cancellation
        CancellationTokenSource cts = new();
        ValueTask<Result> executeTask = fsm.ExecuteAsync(cts.Token);
        await Task.Delay(100, cts.Token); // Give it time to start
        await cts.CancelAsync();

        try
        {
            await executeTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        ReplayEvent[] events = recorder.GetEvents().ToArray();

        // Create replayer
        StateMachineReplay replay = new(events);
        EventSequenceVerifier verifier = new();
        replay.ReplayAll(verifier.ProcessEvent);

        // Verify cancellation was recorded and replayed
        Assert.That(verifier.RecordedEvents, Does.Contain("FSM:Cancelled"));
    }

    [Test]
    public async Task Should_Replay_Complex_State_Machine()
    {
        // Arrange - complex state machine with multiple transitions
        ReplayRecorder recorder = new();
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(recorder);

        // Act
        await fsm.ExecuteAsync();

        // Count states and transitions
        ReplayEvent[] events = recorder.GetEvents().ToArray();
        int enteredCount = events.Count(e => e.Type == EventType.StateEntered);
        int exitedCount = events.Count(e => e.Type == EventType.StateExited);
        int transitionCount = events.Count(e => e.Type == EventType.Transition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(enteredCount, Is.EqualTo(4), "Should enter 4 states");
            Assert.That(exitedCount, Is.EqualTo(4), "Should exit 4 states");
            Assert.That(transitionCount, Is.EqualTo(3), "Should have 3 transitions");
        });

        // Test serialization round trip
        StateMachineReplay replay = new(events);
        byte[] serialized = replay.Serialize();

        // Load and verify
        ReplayEvent[] deserializedEvents = StateMachineReplay.Deserialize(serialized);
        StateMachineReplay newReplay = new(deserializedEvents);

        EventSequenceVerifier verifier = new();
        newReplay.ReplayAll(verifier.ProcessEvent);

        // Check all expected transitions were replayed
        Assert.That(verifier.RecordedEvents.Count(e => e.StartsWith("Transitioned")), Is.EqualTo(3));
    }


    [Test]
    public async Task Should_Record_And_Replay_Log_Events()
    {
        ReplayRecorder recorder = new();

        LoggingTestState loggingState = new("Test log message 1", "Test log message 2");
        AsyncStateMachine fsm = GraphBuilder.StartWithAsync(loggingState).ToAsyncStateMachine(recorder);


        // Act
        await fsm.ExecuteAsync();

        // Assert
        ReplayEvent[] events = recorder.GetEvents().ToArray();
        ReplayEvent[] logEvents = events.Where(e => e.Type == EventType.Log).ToArray();
    
        Assert.Multiple(() =>
        {
            Assert.That(logEvents, Is.Not.Empty);
            Assert.That(logEvents, Has.Length.EqualTo(2));
            Assert.That(logEvents[0].Message, Is.EqualTo("Test log message 1"));
            Assert.That(logEvents[1].Message, Is.EqualTo("Test log message 2"));
        });

        // Test replay
        EventSequenceVerifier verifier = new();
        StateMachineReplay replay = new(events);
        replay.ReplayAll(verifier.ProcessEvent);

        Assert.Multiple(() =>
        {
            // Verify log messages were captured in replay
            Assert.That(verifier.RecordedEvents.Count(e => e.StartsWith("Log:")), Is.EqualTo(2));
            Assert.That(verifier.RecordedEvents, Does.Contain("Log: (0) - Test log message 1"));
            Assert.That(verifier.RecordedEvents, Does.Contain("Log: (0) - Test log message 2"));
        });
    }

// Test state that logs messages
    private class LoggingTestState(params string[] messages) : AsyncState
    {
        protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            foreach (string message in messages)
            {
                await LogAsync(message, ct);
            }

            return Result.Success;
        }
    }

    [Test]
    public void Sync_state_machine_records_replay_events_via_recorder()
    {
        ReplayRecorder recorder = new();
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .ToStateMachine(recorder);

        // Run to completion — sync machine ticks via Execute().
        Result r;
        do { r = fsm.Execute(); } while (r == Result.InProgress);

        ReadOnlyMemory<ReplayEvent> events = recorder.GetEvents();
        Assert.Multiple(() =>
        {
            Assert.That(events.Length, Is.GreaterThan(0));
            Assert.That(events.ToArray().Any(e => e.Type == EventType.StateMachineStarted), Is.True);
            Assert.That(events.ToArray().Any(e => e.Type == EventType.StateMachineCompleted), Is.True);
        });
    }

    [Test]
    public void Recorder_DroppedCount_increments_when_ring_overflows()
    {
        ReplayRecorder recorder = new(capacity: 4);
        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .To(() => Result.Success)
            .To(() => Result.Success)
            .ToStateMachine(recorder);
        // Auto-restart is on by default, which would loop forever. Keep deterministic by
        // disabling auto-reset and only running once.
        fsm.SetResetPolicy(RestartPolicy.Manual);

        Result r;
        do { r = fsm.Execute(); } while (r == Result.InProgress);

        Assert.That(recorder.DroppedCount, Is.GreaterThan(0),
            "A four-node sync machine emits more than four events; ring of size 4 must have dropped some.");
    }

    [Test]
    public void Deserialize_rejects_payload_without_magic_header()
    {
        // Payload starts with the old format (4-byte count = 0, no events). Without the
        // magic header it must be rejected — silently accepting it would lock in the
        // unsafe pre-magic format.
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);
        writer.Write(0);
        Assert.Throws<InvalidDataException>(() => StateMachineReplay.Deserialize(ms.ToArray()));
    }

    [Test]
    public void Deserialize_rejects_payload_with_unknown_version()
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);
        writer.Write("NXRP"u8.ToArray());
        writer.Write((byte)99); // version
        writer.Write(0); // count
        Assert.Throws<InvalidDataException>(() => StateMachineReplay.Deserialize(ms.ToArray()));
    }

    [Test]
    public void Deserialize_rejects_payload_with_implausibly_large_count()
    {
        // count = int.MaxValue would cause `new ReplayEvent[count]` to OOM. The bound on
        // count vs remaining bytes catches this immediately.
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);
        writer.Write("NXRP"u8.ToArray());
        writer.Write((byte)1); // version
        writer.Write(int.MaxValue); // count
        // No events follow.
        Assert.Throws<InvalidDataException>(() => StateMachineReplay.Deserialize(ms.ToArray()));
    }

    [Test]
    public async Task Recorder_survives_a_failure_edge_and_records_the_result_failure()
    {
        // Regression: the failure-edge path passes a null Exception to OnStateFailed;
        // ReplayRecorder used to call ex.ToString() unconditionally and crash the run with
        // a NullReferenceException — on exactly the path it exists to diagnose.
        ReplayRecorder recorder = new();
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .OnErrorAsync(_ => ResultHelpers.Success)
            .Build();

        Result result = await graph.ToAsyncStateMachine(recorder).ExecuteAsync();

        ReplayEvent failed = recorder.GetEvents().ToArray().Single(e => e.Type == EventType.StateFailed);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(failed.Message, Is.EqualTo("node returned Failure"));
        });
    }

    [Test]
    public void Sync_recorder_survives_a_failure_edge_and_records_the_result_failure()
    {
        ReplayRecorder recorder = new();
        Graph graph = GraphBuilder
            .StartWith(() => Result.Failure)
            .OnError(() => Result.Success)
            .Build();

        StateMachine fsm = graph.ToStateMachine(recorder);
        Result result;
        do { result = fsm.Execute(); } while (result == Result.InProgress);

        ReplayEvent failed = recorder.GetEvents().ToArray().Single(e => e.Type == EventType.StateFailed);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(failed.Message, Is.EqualTo("node returned Failure"));
        });
    }

    [Test]
    public void Deserialize_rejects_payload_with_unknown_event_type_byte()
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);
        writer.Write("NXRP"u8.ToArray());
        writer.Write((byte)1); // version
        writer.Write(1); // count
        writer.Write((byte)200); // bogus EventType
        // Pad to satisfy the min-size bound check (count=1 needs >=15 bytes remaining).
        writer.Write(0); // source idx
        writer.Write(false); // has-target
        writer.Write(0L); // timestamp
        writer.Write(string.Empty); // message
        Assert.Throws<InvalidDataException>(() => StateMachineReplay.Deserialize(ms.ToArray()));
    }
}
