using NxGraph.Diagnostics;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

/// <summary>
/// Exception thrown when graph validation detects errors.
/// </summary>
public sealed class GraphValidationException(GraphValidationResult result) : Exception(result.ToString())
{
    // ReSharper disable once UnusedMember.Global
    public GraphValidationResult Result { get; } = result ?? throw new ArgumentNullException(nameof(result));
}

public partial class GraphBuilder
{
    /// <summary>
    /// Builds the graph, then validates it. In DEBUG builds, throws on validation errors.
    /// In RELEASE builds, you can opt-in to throwing by passing throwOnError:true.
    /// </summary>
    public Graph Build(bool throwOnError = false)
    {
        Graph graph = InternalBuild();

        GraphValidationOptions options = new()
        {
            AllNodes = GetAllNodeIds(),
            WarnOnSelfLoop = true,
            StrictNoTerminalPath = false
        };

        GraphValidationResult result = graph.Validate(options);

#if DEBUG
        if (result.HasErrors)
        {
            throw new GraphValidationException(result);
        }
#endif

        if (!System.Diagnostics.Debugger.IsAttached && throwOnError && result.HasErrors)
        {
            throw new GraphValidationException(result);
        }

        return graph;
    }


    /// <summary>
    /// Utility to validate without building (when you already have a graph).
    /// </summary>
    public GraphValidationResult Validate(Graph graph, GraphValidationOptions? options = null)
    {
        return graph.Validate(options ?? new GraphValidationOptions { AllNodes = GetAllNodeIds() });
    }
}