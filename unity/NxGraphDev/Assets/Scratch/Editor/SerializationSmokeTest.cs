using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Graphs;
using NxGraph.Serialization;
using NxGraph.Serialization.Abstraction;
using UnityEditor;
using UnityEngine;

namespace NxGraphDev.Editor
{
    /// <summary>
    /// Proves the bundled serialization package works inside Unity's runtime, not merely that it
    /// compiles. Both formats are exercised because they fail differently: System.Text.Json is
    /// reflection-heavy, and MessagePack resolves formatters dynamically — a missing or
    /// conflicting BCL facade shows up here as a TypeLoadException, which is exactly the failure
    /// mode that bundling dependencies risks.
    /// <para>
    /// This lives in the dev project rather than the package on purpose: the core package must
    /// not depend on the optional serialization package.
    /// </para>
    /// </summary>
    public static class SerializationSmokeTest
    {
        [MenuItem("Window/NxGraph/Run Serialization Smoke Test")]
        public static void Run()
        {
            try
            {
                RunAsync().GetAwaiter().GetResult();
                Debug.Log($"[NxGraph] Serialization smoke test PASSED (payload v{SerializationVersion.Version}).");
            }
            catch (Exception e)
            {
                Debug.LogError($"[NxGraph] Serialization smoke test FAILED: {e}");
                throw;
            }
        }

        /// <summary>Batch-mode entry point: <c>-executeMethod NxGraphDev.Editor.SerializationSmokeTest.RunBatch</c>.</summary>
        public static void RunBatch()
        {
            try
            {
                RunAsync().GetAwaiter().GetResult();
                Debug.Log($"[NxGraph] Serialization smoke test PASSED (payload v{SerializationVersion.Version}).");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NxGraph] Serialization smoke test FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static async Task RunAsync()
        {
            Graph graph = BuildGraph();
            GraphSerializer serializer = new(new NoopCodec());

            using (MemoryStream json = new())
            {
                await serializer.ToJsonAsync(graph, json);
                json.Position = 0;
                Graph restored = await serializer.FromJsonAsync(json);
                Expect(restored.NodeCount == graph.NodeCount,
                    $"JSON round-trip changed the node count: {graph.NodeCount} -> {restored.NodeCount}");
            }

            using (MemoryStream binary = new())
            {
                await serializer.ToBinaryAsync(graph, binary);
                binary.Position = 0;
                Graph restored = await serializer.FromBinaryAsync(binary);
                Expect(restored.NodeCount == graph.NodeCount,
                    $"MessagePack round-trip changed the node count: {graph.NodeCount} -> {restored.NodeCount}");
            }
        }

        private static Graph BuildGraph()
        {
            GraphBuilder builder = new();

            NodeId first = builder.AddNode(new NxGraph.Fsm.RelayState(() => Result.Success), true);
            NodeId second = builder.AddNode(new NxGraph.Fsm.RelayState(() => Result.Success), false);
            NodeId handler = builder.AddNode(new NxGraph.Fsm.RelayState(() => Result.Success), false);

            builder.SetName(first, "First");
            builder.SetName(second, "Second");
            builder.SetName(handler, "Handler");

            builder.AddTransition(first, second);
            builder.AddFailureTransition(first, handler);

            return builder.Build();
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Node logic rides the wire through a codec. This one is deliberately trivial: the test
        /// is about the serializer and its dependencies loading and running, not about any
        /// particular logic representation.
        /// </summary>
        private sealed class NoopCodec : ILogicTextCodec
        {
            public string Serialize(IAsyncLogic data) => "noop";

            public IAsyncLogic Deserialize(string s) => new NoopLogic();
        }

        private sealed class NoopLogic : IAsyncLogic
        {
            public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) =>
                new(Result.Success);
        }
    }
}
