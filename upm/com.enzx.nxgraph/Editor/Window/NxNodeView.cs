using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NxGraph.Unity.Editor.Window
{
    /// <summary>
    /// One authored step on the canvas: an input port, a success output, and a failure output —
    /// the shape of a <c>Transition</c>, which carries a success destination and an optional
    /// failure destination.
    /// </summary>
    public sealed class NxNodeView : Node
    {
        public NxNodeView(NxNodeData data)
        {
            Data = data;
            viewDataKey = data.Uid;

            Input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            Input.portName = "in";
            inputContainer.Add(Input);

            Success = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
            Success.portName = "success";
            outputContainer.Add(Success);

            Failure = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
            Failure.portName = "failure";
            Failure.portColor = new Color(0.85f, 0.4f, 0.35f);
            outputContainer.Add(Failure);

            Refresh();
        }

        public NxNodeData Data { get; }

        public Port Input { get; }

        public Port Success { get; }

        public Port Failure { get; }

        /// <summary>Re-reads the model. Called after an inspector edit or a start-node change.</summary>
        public void Refresh()
        {
            title = string.IsNullOrWhiteSpace(Data.DisplayName) ? "Step" : Data.DisplayName;

            EnableInClassList("nx-node--failing", Data.Outcome == NxNodeOutcome.Failure);
            style.borderTopColor = style.borderBottomColor =
                style.borderLeftColor = style.borderRightColor =
                    Data.Outcome == NxNodeOutcome.Failure
                        ? new StyleColor(new Color(0.85f, 0.4f, 0.35f))
                        : new StyleColor(new Color(0.24f, 0.24f, 0.24f));

            SetPosition(new Rect(Data.Position, Vector2.zero));
        }

        /// <summary>Marks this node as the graph's entry point.</summary>
        public void SetIsStart(bool isStart)
        {
            EnableInClassList("nx-node--start", isStart);
            style.borderTopWidth = style.borderBottomWidth =
                style.borderLeftWidth = style.borderRightWidth = isStart ? 2f : 1f;

            titleContainer.style.backgroundColor = isStart
                ? new StyleColor(new Color(0.18f, 0.36f, 0.26f))
                : new StyleColor(StyleKeyword.Null);
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            Data.Position = newPos.position;
        }
    }
}
