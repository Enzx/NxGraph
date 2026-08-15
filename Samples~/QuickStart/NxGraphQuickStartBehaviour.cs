using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using UnityEngine;

namespace NxGraph.Samples.QuickStart
{
    /// <summary>
    /// Demonstrates running an NxGraph <see cref="StateMachine"/> in Unity. The sync machine
    /// is <b>frame-stepped</b>: each <see cref="State.Execute"/> call advances exactly one
    /// node and returns <see cref="Result.InProgress"/> until the run reaches a terminal
    /// result — a natural fit for calling once per frame from <c>Update()</c>. The first
    /// call auto-starts the run; <see cref="RestartPolicy"/> controls what happens after a
    /// terminal result (Auto restarts on the next call).
    /// </summary>
    public sealed class NxGraphQuickStartBehaviour : MonoBehaviour
    {
        private StateMachine _fsm;

        private void Awake()
        {
            _fsm = GraphBuilder
                .StartWith(() => { Debug.Log("Acquire"); return Result.Success; }).SetName("Acquire")
                .To(() => { Debug.Log("Process"); return Result.Success; }).SetName("Process")
                .To(() => { Debug.Log("Release"); return Result.Success; }).SetName("Release")
                .ToStateMachine();

            // Auto-reset is ON by default — after completing, the FSM resets
            // and re-starts on the next Execute() call.
            // Call _fsm.SetAutoReset(false) to stop after completion.
        }

        private void Update()
        {
            // Execute exactly one node per frame.
            // On the first call, the FSM auto-starts (no separate Start() needed).
            Result result = _fsm.Execute();

            if (result.IsCompleted)
            {
                Debug.Log($"NxGraph Quick Start finished with result: {result}");
            }
        }
    }
}
