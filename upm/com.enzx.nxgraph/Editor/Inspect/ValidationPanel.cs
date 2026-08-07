using System.Collections.Generic;
using NxGraph.Diagnostics.Validations;
using NxGraph.Graphs;
using UnityEngine;
using UnityEngine.UIElements;

namespace NxGraph.Unity.Editor.Inspect
{
    /// <summary>
    /// Shows what the library thinks of the graph the author just drew.
    /// <para>
    /// The validator is not editor-specific — it is the same <c>graph.Validate()</c> the build
    /// runs, so an editor that keeps this panel green produces graphs that pass CI. Errors here
    /// are structural (a broken edge, an unreachable node, no terminal path); warnings are
    /// smells the runtime tolerates.
    /// </para>
    /// </summary>
    public sealed class ValidationPanel : VisualElement
    {
        private readonly Label _summary;
        private readonly ScrollView _list;

        public ValidationPanel()
        {
            style.borderTopWidth = 1f;
            style.borderTopColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
            style.minHeight = 96f;
            style.maxHeight = 200f;
            style.paddingLeft = 6f;
            style.paddingRight = 6f;
            style.paddingTop = 4f;
            style.paddingBottom = 4f;

            _summary = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4f } };
            Add(_summary);

            _list = new ScrollView { style = { flexGrow = 1f } };
            Add(_list);
        }

        /// <summary>Reports a document that could not be compiled at all.</summary>
        public void ShowCompileErrors(IReadOnlyList<string> errors)
        {
            _list.Clear();
            _summary.text = $"Does not compile — {errors.Count} problem{(errors.Count == 1 ? "" : "s")}";
            _summary.style.color = new StyleColor(SeverityColor(Severity.Error));

            foreach (string error in errors)
            {
                _list.Add(Row(Severity.Error, error));
            }
        }

        /// <summary>Reports the validator's verdict on a graph that did compile.</summary>
        public void ShowValidation(Graph graph)
        {
            _list.Clear();

            GraphValidationResult result = graph.Validate();
            IReadOnlyList<GraphDiagnostic> diagnostics = result.Diagnostics;

            if (diagnostics.Count == 0)
            {
                _summary.text = $"Compiles — {graph.NodeCount} node(s), no diagnostics";
                _summary.style.color = new StyleColor(SeverityColor(Severity.Info));
                return;
            }

            int errors = 0;
            int warnings = 0;
            foreach (GraphDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == Severity.Error) errors++;
                else if (diagnostic.Severity == Severity.Warning) warnings++;
            }

            _summary.text = $"Compiles — {errors} error(s), {warnings} warning(s)";
            _summary.style.color = new StyleColor(SeverityColor(
                errors > 0 ? Severity.Error : warnings > 0 ? Severity.Warning : Severity.Info));

            foreach (GraphDiagnostic diagnostic in diagnostics)
            {
                string node = diagnostic.Node == NodeId.Default ? "" : $"[{diagnostic.Node}] ";
                _list.Add(Row(diagnostic.Severity, node + diagnostic.Message));
            }
        }

        /// <summary>Resets the panel to its empty state. Named to avoid hiding <c>VisualElement.Clear</c>.</summary>
        public void ShowNoGraph()
        {
            _list.Clear();
            _summary.text = "No graph selected.";
            _summary.style.color = new StyleColor(SeverityColor(Severity.Info));
        }

        private static Label Row(Severity severity, string message) =>
            new($"{severity}: {message}")
            {
                style =
                {
                    color = new StyleColor(SeverityColor(severity)),
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = 2f,
                },
            };

        private static Color SeverityColor(Severity severity) => severity switch
        {
            Severity.Error => new Color(0.92f, 0.45f, 0.40f),
            Severity.Warning => new Color(0.94f, 0.78f, 0.35f),
            _ => new Color(0.68f, 0.68f, 0.68f),
        };
    }
}
