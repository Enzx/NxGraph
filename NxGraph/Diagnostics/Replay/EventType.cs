namespace NxGraph.Diagnostics.Replay;

public enum EventType : byte
{
    StateEntered,
    StateExited,
    Transition,
    StateFailed,
    StateMachineReset,
    StateMachineStarted,
    StateMachineCompleted,
    StateMachineCancelled,
    StatusChanged,
    Log
}