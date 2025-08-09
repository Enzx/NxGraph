namespace NxGraph.Graphs;

public interface IGraph
{
    Node StartNode { get; }
    int NodeCount { get; }
    bool TryGetTransition(NodeId from, out Transition transition);
    bool TryGetNode(NodeId id, out Node node);
    void SetAgent<TAgent>(TAgent agent);
  
}