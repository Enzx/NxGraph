using NxGraph;
using NxGraph.Authoring;
using NxGraph.Diagnostics.Export;

namespace NxFSM.Examples;

public abstract class ExportersExample
{
    public static void Run()
    {
        string mermaid = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Start")
            .ToAsync(_ => ResultHelpers.Success).SetName("Process")
            .ToAsync(_ => ResultHelpers.Success).SetName("End")
            .Build()
            .ToMermaid();

        Console.WriteLine("=== Mermaid Export ===");
        Console.WriteLine(mermaid);
    }
}
