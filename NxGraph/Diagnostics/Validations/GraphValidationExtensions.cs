using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Validations;

public static class GraphValidationExtensions
{
    /// <summary>
    /// Validate and throw in DEBUG builds when errors exist. Returns the result regardless.
    /// </summary>
    public static GraphValidationResult ValidateAndThrowIfErrorsDebug(this Graph graph,
        IReadOnlyList<NodeId>? all = null)
    {
        GraphValidationResult res = graph.Validate(new GraphValidationOptions { AllNodes = all });
#if DEBUG
            if (res.HasErrors) throw new GraphValidationException(res);
#endif
        return res;
    }
}