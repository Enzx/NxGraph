using System.Collections.Generic;
using NxGraph.Diagnostics.Export;
using NxGraph.Graphs;
using NxGraph.Unity.Editor.Compile;
using NxGraph.Unity.Editor.Inspect;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NxGraph.Unity.Editor.Window
{
    /// <summary>
    /// Edits an <see cref="NxGraphAsset"/>: canvas on top, live diagnostics underneath.
    /// <para>
    /// Every structural edit recompiles the asset to a real <see cref="Graph"/> and runs the
    /// library's own validator over it, so the feedback in the panel is the same verdict the
    /// build would give — the editor never grows a second, divergent idea of what a valid graph
    /// is.
    /// </para>
    /// </summary>
    public sealed class NxGraphEditorWindow : EditorWindow
    {
        private NxGraphAsset _asset;
        private NxGraphView _view;
        private ValidationPanel _diagnostics;

        [MenuItem("Window/NxGraph/Graph Editor")]
        public static void Open() => Open(null);

        public static void Open(NxGraphAsset asset)
        {
            NxGraphEditorWindow window = GetWindow<NxGraphEditorWindow>();
            window.titleContent = new GUIContent("NxGraph");
            window.minSize = new Vector2(560f, 360f);

            if (asset is not null)
            {
                window.Load(asset);
            }

            window.Show();
        }

        /// <summary>Opens the window when an <see cref="NxGraphAsset"/> is double-clicked in the Project view.</summary>
        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            // Unity 6.4 deprecated the int-keyed asset lookups in favour of EntityId overloads
            // that older versions do not have. Since [OnOpenAsset] still hands us an int, the
            // obsolete call is the only form that works across the supported range; the warning
            // is suppressed here rather than project-wide so it resurfaces the moment this
            // package drops support for pre-6.4 Unity.
#pragma warning disable CS0618
            string path = AssetDatabase.GetAssetPath(instanceId);
#pragma warning restore CS0618

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            NxGraphAsset asset = AssetDatabase.LoadAssetAtPath<NxGraphAsset>(path);
            if (asset is null)
            {
                return false;
            }

            Open(asset);
            return true;
        }

        private void CreateGUI()
        {
            rootVisualElement.Add(BuildToolbar());

            _view = new NxGraphView();
            _view.GraphChanged += Recompile;
            rootVisualElement.Add(_view);

            _diagnostics = new ValidationPanel();
            rootVisualElement.Add(_diagnostics);

            // A domain reload drops the view but keeps _asset (it is a serialized reference), so
            // reattach rather than showing an empty canvas.
            if (_asset is not null)
            {
                Load(_asset);
            }
            else
            {
                _diagnostics.ShowNoGraph();
            }
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is NxGraphAsset asset && asset != _asset)
            {
                Load(asset);
            }
        }

        private VisualElement BuildToolbar()
        {
            VisualElement toolbar = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    paddingLeft = 4f, paddingRight = 4f, paddingTop = 2f, paddingBottom = 2f,
                    borderBottomWidth = 1f,
                    borderBottomColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f)),
                },
            };

            ObjectField assetField = new("Graph")
            {
                objectType = typeof(NxGraphAsset),
                allowSceneObjects = false,
                style = { width = 320f },
            };
            assetField.RegisterValueChangedCallback(evt => Load(evt.newValue as NxGraphAsset));
            toolbar.Add(assetField);

            toolbar.Add(new Button(Recompile) { text = "Revalidate" });
            toolbar.Add(new Button(CopyMermaid) { text = "Copy Mermaid" });

            return toolbar;
        }

        private void Load(NxGraphAsset asset)
        {
            _asset = asset;

            if (_view is null)
            {
                // CreateGUI has not run yet; it picks _asset up when it does.
                return;
            }

            _view.Populate(asset);
            Recompile();
        }

        private void Recompile()
        {
            if (_diagnostics is null)
            {
                return;
            }

            if (_asset is null)
            {
                _diagnostics.ShowNoGraph();
                return;
            }

            List<string> errors = new();
            Graph graph = NxGraphCompiler.Compile(_asset, errors, out _);

            if (graph is null)
            {
                _diagnostics.ShowCompileErrors(errors);
                return;
            }

            _diagnostics.ShowValidation(graph);
        }

        /// <summary>
        /// Exports the graph as Mermaid. A useful escape hatch long before the canvas is good:
        /// the exporter is the library's own, so it renders composites, fork/join and branch
        /// labels that this editor does not draw yet.
        /// </summary>
        private void CopyMermaid()
        {
            if (_asset is null)
            {
                return;
            }

            List<string> errors = new();
            Graph graph = NxGraphCompiler.Compile(_asset, errors, out _);

            if (graph is null)
            {
                _diagnostics.ShowCompileErrors(errors);
                return;
            }

            EditorGUIUtility.systemCopyBuffer = graph.ToMermaid();
            ShowNotification(new GUIContent("Mermaid copied"));
        }
    }
}
