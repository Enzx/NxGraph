using NxGraph;
using NxGraph.Authoring;
using NxGraph.Diagnostics.Export;

namespace NxFSM.Examples;

public abstract class ExportersExample
{
    public static void Run()
    {
        string mermaid = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .Build()
            .ToMermaid();
        Console.WriteLine("Mermaid:");
        Console.WriteLine(mermaid);
    }
}
