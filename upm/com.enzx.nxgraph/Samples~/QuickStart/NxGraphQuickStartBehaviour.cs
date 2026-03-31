using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using UnityEngine;

namespace NxGraph.Samples.QuickStart
{
    public sealed class NxGraphQuickStartBehaviour : MonoBehaviour
    {
        private StateMachine? _fsm;

        private void Awake()
        {
            _fsm = GraphBuilder
                .StartWith(() => Result.Success).SetName("Acquire")
                .To(() => Result.Success).SetName("Process")
                .To(() => Result.Success).SetName("Release")
                .ToStateMachine();
        }

        private void Start()
        {
            if (_fsm is null)
            {
                return;
            }

            Result result = _fsm.Execute();
            Debug.Log($"NxGraph Quick Start finished with result: {result}");
        }
    }
}

