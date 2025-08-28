using NxGraph.Diagnostics.Replay;

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
        StateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .ToStateMachine(recorder);

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
        StateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .ToStateMachine(recorder);

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
        StateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .ToStateMachine(recorder);

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
        StateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .WaitFor(1.Seconds())
            .ToStateMachine(recorder);

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
        StateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .ToStateMachine(recorder);

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

        // Save to file for debugging (optional)
        await File.WriteAllBytesAsync("replay_test.bin", serialized);

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
        StateMachine stateMachine = GraphBuilder.StartWith(loggingState).ToStateMachine(recorder);


        // Act
        await stateMachine.ExecuteAsync();

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
    private class LoggingTestState(params string[] messages) : State
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
}