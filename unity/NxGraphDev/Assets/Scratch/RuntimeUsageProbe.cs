using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;
using UnityEngine;

namespace NxGraphDev
{
    /// <summary>
    /// A plain MonoBehaviour in Assets/ with no asmdef, proving the ordinary Unity workflow
    /// works: the package's plugins are auto-referenced into Assembly-CSharp, so gameplay code
    /// can just <c>using NxGraph.Authoring;</c> and build a machine.
    /// </summary>
    public sealed class RuntimeUsageProbe : MonoBehaviour
    {
        private StateMachine _machine;

        private void Start()
        {
            Graph graph = GraphBuilder
                .StartWith(() =>
                {
                    Debug.Log("[NxGraph] first step");
                    return Result.Success;
                })
                .SetName("First")
                .To(() =>
                {
                    Debug.Log("[NxGraph] second step");
                    return Result.Success;
                })
                .SetName("Second")
                .Build();

            _machine = graph.ToStateMachine();
            _machine.SetStepMode(ParallelStepMode.RunToJoin);
        }

        private void Update()
        {
            if (_machine is null)
            {
                return;
            }

            Result result = _machine.Execute();
            if (result != Result.InProgress)
            {
                Debug.Log($"[NxGraph] machine finished: {result}");
                _machine = null;
            }
        }
    }
}
