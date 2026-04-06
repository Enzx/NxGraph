using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using UnityEngine;

namespace NxGraph.Samples.QuickStart
{
    /// <summary>
    /// Demonstrates two ways of running an NxGraph <see cref="StateMachine"/> in Unity:
    /// <list type="bullet">
    ///   <item><b>Frame-stepped</b> – <see cref="StateMachine.Tick"/> advances one node
    ///         per frame. Auto-starts on first call; ideal for gameplay FSMs.</item>
    ///   <item><b>Blocking</b> – <see cref="StateMachine.Execute"/> (inherited from
    ///         <see cref="State"/>) runs the entire FSM in a single call
    ///         (fine for trivial graphs).</item>
    /// </list>
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
