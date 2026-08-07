using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NxGraph.Unity.Editor.Window
{
    /// <summary>
    /// The canvas. Reads an <see cref="NxGraphAsset"/> into views, writes structural edits back,
    /// and reports every change so the window can recompile and revalidate.
    /// <para>
    /// GraphView is the same API Shader Graph and VFX Graph are built on. It still lives under
    /// <c>UnityEditor.Experimental</c> and has moved between Unity versions, so the interaction
    /// surface used here is kept deliberately small and funnelled through
    /// <see cref="OnGraphViewChanged"/> — a later port to a UI Toolkit renderer would replace
    /// this file and leave the asset model and compiler untouched.
    /// </para>
    /// </summary>
    public sealed class NxGraphView : GraphView
    {
        private readonly Dictionary<string, NxNodeView> _nodeViews = new(StringComparer.Ordinal);
        private NxGraphAsset _asset;

        public NxGraphView()
        {
            style.flexGrow = 1f;

            Insert(0, new GridBackground());
            this.AddManipulator(new ContentZoomer());
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            graphViewChanged = OnGraphViewChanged;
        }

        /// <summary>Raised after any edit that changes what the graph compiles to.</summary>
        public event Action GraphChanged;

        public void Populate(NxGraphAsset asset)
        {
            _asset = asset;

            // graphViewChanged fires for programmatic removals too; muting it keeps a rebuild
            // from being mistaken for the author deleting everything.
            graphViewChanged = null;
            DeleteElements(graphElements.ToList());
            _nodeViews.Clear();
            graphViewChanged = OnGraphViewChanged;

            if (_asset is null)
            {
                return;
            }

            foreach (NxNodeData node in _asset.Nodes)
            {
                CreateNodeView(node);
            }

            foreach (NxEdgeData edge in _asset.Edges)
            {
                if (!_nodeViews.TryGetValue(edge.FromUid, out NxNodeView from) ||
                    !_nodeViews.TryGetValue(edge.ToUid, out NxNodeView to))
                {
                    // A dangling edge in the asset. The compiler reports it; drawing it is
                    // impossible, so the canvas just omits it.
                    continue;
                }

                Port output = edge.IsFailure ? from.Failure : from.Success;
                AddElement(output.ConnectTo(to.Input));
            }

            RefreshStartNode();
        }

        /// <summary>Re-applies node titles and the start-node highlight without a full rebuild.</summary>
        public void RefreshNodes()
        {
            foreach (NxNodeView view in _nodeViews.Values)
            {
                view.Refresh();
            }

            RefreshStartNode();
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter) =>
            ports.Where(p =>
                    p.direction != startPort.direction &&
                    p.node != startPort.node)
                .ToList();

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (_asset is null)
            {
                return;
            }

            Vector2 position = contentViewContainer.WorldToLocal(evt.localMousePosition);

            evt.menu.AppendAction("Add Step", _ =>
            {
                Undo.RecordObject(_asset, "Add Step");
                NxNodeData node = _asset.AddNode("Step", position);
                CreateNodeView(node);
                RefreshStartNode();
                MarkDirty();
            });

            if (evt.target is NxNodeView target)
            {
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Set as Start", _ =>
                {
                    Undo.RecordObject(_asset, "Set Start Node");
                    _asset.StartNodeUid = target.Data.Uid;
                    RefreshStartNode();
                    MarkDirty();
                });
            }

            base.BuildContextualMenu(evt);
        }

        private void CreateNodeView(NxNodeData node)
        {
            NxNodeView view = new(node);
            _nodeViews[node.Uid] = view;
            AddElement(view);
        }

        private void RefreshStartNode()
        {
            foreach (KeyValuePair<string, NxNodeView> pair in _nodeViews)
            {
                pair.Value.SetIsStart(_asset is not null && pair.Key == _asset.StartNodeUid);
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_asset is null)
            {
                return change;
            }

            bool structural = false;

            if (change.elementsToRemove is not null)
            {
                Undo.RecordObject(_asset, "Delete Graph Elements");

                foreach (GraphElement element in change.elementsToRemove)
                {
                    switch (element)
                    {
                        case NxNodeView node:
                            _asset.RemoveNode(node.Data.Uid);
                            _nodeViews.Remove(node.Data.Uid);
                            structural = true;
                            break;

                        case Edge edge when Resolve(edge) is { } wire:
                            _asset.Disconnect(wire.From, wire.To, wire.IsFailure);
                            structural = true;
                            break;
                    }
                }
            }

            if (change.edgesToCreate is not null)
            {
                Undo.RecordObject(_asset, "Connect Nodes");

                foreach (Edge edge in change.edgesToCreate)
                {
                    if (Resolve(edge) is not { } wire)
                    {
                        continue;
                    }

                    // Connect replaces an existing edge of the same kind, mirroring the
                    // one-success-edge-per-node rule GraphBuilder enforces. The stale Edge
                    // element is dropped here so the canvas agrees with the model.
                    Edge stale = edges.FirstOrDefault(e =>
                        e != edge && Resolve(e) is { } other &&
                        other.From == wire.From && other.IsFailure == wire.IsFailure);

                    if (stale is not null)
                    {
                        stale.input?.Disconnect(stale);
                        stale.output?.Disconnect(stale);
                        RemoveElement(stale);
                    }

                    _asset.Connect(wire.From, wire.To, wire.IsFailure);
                    structural = true;
                }
            }

            if (structural)
            {
                RefreshStartNode();
                MarkDirty();
            }
            else if (change.movedElements is not null)
            {
                // Positions are cosmetic — they are saved, but they do not change what the graph
                // compiles to, so no recompile is triggered.
                EditorUtility.SetDirty(_asset);
            }

            return change;
        }

        private static Wire? Resolve(Edge edge)
        {
            if (edge.output?.node is not NxNodeView from || edge.input?.node is not NxNodeView to)
            {
                return null;
            }

            return new Wire(from.Data.Uid, to.Data.Uid, ReferenceEquals(edge.output, from.Failure));
        }

        private void MarkDirty()
        {
            EditorUtility.SetDirty(_asset);
            GraphChanged?.Invoke();
        }

        private readonly struct Wire
        {
            public Wire(string from, string to, bool isFailure)
            {
                From = from;
                To = to;
                IsFailure = isFailure;
            }

            public string From { get; }

            public string To { get; }

            public bool IsFailure { get; }
        }
    }
}
