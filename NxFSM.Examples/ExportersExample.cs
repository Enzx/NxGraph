using NxGraph;
using NxGraph.Authoring;
using NxGraph.Diagnostics.Export;

namespace NxFSM.Examples;

public abstract class ExportersExample
{
    public static void Run()
    {
        string mermaid = GraphBuilder.StartWith(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .Build()
            .ToMermaid();
        Console.WriteLine("Mermaid:");
        Console.WriteLine(mermaid);
    }
}