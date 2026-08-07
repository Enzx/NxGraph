using System;
using System.Collections.Generic;
using UnityEngine;

namespace NxGraph.Unity.Editor
{
    /// <summary>
    /// The editable document behind a graph. This is deliberately <em>not</em> a runtime
    /// <see cref="Graphs.Graph"/>: that type is immutable, identified by dense array indexes, and
    /// holds delegate-carrying nodes that Unity cannot serialize. An editor needs the opposite —
    /// a mutable, Unity-serialized model with stable identity that survives reordering — and
    /// compiles down to a <see cref="Graphs.Graph"/> on demand. See
    /// <see cref="Compile.NxGraphCompiler"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "NxGraph/Graph", fileName = "NewGraph")]
    public sealed class NxGraphAsset : ScriptableObject
    {
        [SerializeField] private List<NxNodeData> nodes = new();
        [SerializeField] private List<NxEdgeData> edges = new();

        [SerializeField]
        [Tooltip("UID of the node the machine starts at. A graph has exactly one start node.")]
        private string startNodeUid = string.Empty;

        public List<NxNodeData> Nodes => nodes;

        public List<NxEdgeData> Edges => edges;

        public string StartNodeUid
        {
            get => startNodeUid;
            set => startNodeUid = value;
        }

        public NxNodeData FindNode(string uid) => nodes.Find(n => n.Uid == uid);

        /// <summary>Adds a node with a freshly minted UID and returns it.</summary>
        public NxNodeData AddNode(string displayName, Vector2 position)
        {
            NxNodeData node = new()
            {
                Uid = Guid.NewGuid().ToString("N"),
                DisplayName = displayName,
                Position = position,
            };

            nodes.Add(node);

            // The first node added becomes the start node; without one the graph cannot compile,
            // and silently picking index 0 later would make the choice invisible in the UI.
            if (string.IsNullOrEmpty(startNodeUid))
            {
                startNodeUid = node.Uid;
            }

            return node;
        }

        public void RemoveNode(string uid)
        {
            nodes.RemoveAll(n => n.Uid == uid);
            edges.RemoveAll(e => e.FromUid == uid || e.ToUid == uid);

            if (startNodeUid == uid)
            {
                startNodeUid = nodes.Count > 0 ? nodes[0].Uid : string.Empty;
            }
        }

        /// <summary>
        /// Connects two nodes. A node carries at most one success edge and at most one failure
        /// edge — the same rule <c>GraphBuilder</c> enforces — so an existing edge of the same
        /// kind is replaced rather than added alongside.
        /// </summary>
        public void Connect(string fromUid, string toUid, bool isFailure)
        {
            edges.RemoveAll(e => e.FromUid == fromUid && e.IsFailure == isFailure);
            edges.Add(new NxEdgeData { FromUid = fromUid, ToUid = toUid, IsFailure = isFailure });
        }

        public void Disconnect(string fromUid, string toUid, bool isFailure) =>
            edges.RemoveAll(e => e.FromUid == fromUid && e.ToUid == toUid && e.IsFailure == isFailure);
    }

    /// <summary>
    /// One authored step. <see cref="Uid"/> is the identity that outlives list reordering and
    /// index churn; it is handed to <c>GraphBuilder.SetUid</c> at compile time so a built graph
    /// can be mapped back to the asset through <c>Graph.TryGetNodeByUid</c> — which is what makes
    /// runtime highlighting and selection sync possible.
    /// </summary>
    [Serializable]
    public sealed class NxNodeData
    {
        [SerializeField] private string uid = string.Empty;
        [SerializeField] private string displayName = "Step";
        [SerializeField] private Vector2 position;
        [SerializeField] private NxNodeOutcome outcome = NxNodeOutcome.Success;

        public string Uid
        {
            get => uid;
            set => uid = value;
        }

        public string DisplayName
        {
            get => displayName;
            set => displayName = value;
        }

        public Vector2 Position
        {
            get => position;
            set => position = value;
        }

        /// <summary>What the compiled step returns. Enough to exercise both edges of the fault model.</summary>
        public NxNodeOutcome Outcome
        {
            get => outcome;
            set => outcome = value;
        }
    }

    /// <summary>A wire between two nodes, on either the success or the failure channel.</summary>
    [Serializable]
    public sealed class NxEdgeData
    {
        [SerializeField] private string fromUid = string.Empty;
        [SerializeField] private string toUid = string.Empty;
        [SerializeField] private bool isFailure;

        public string FromUid
        {
            get => fromUid;
            set => fromUid = value;
        }

        public string ToUid
        {
            get => toUid;
            set => toUid = value;
        }

        public bool IsFailure
        {
            get => isFailure;
            set => isFailure = value;
        }
    }

    public enum NxNodeOutcome
    {
        Success = 0,
        Failure = 1,
    }
}
