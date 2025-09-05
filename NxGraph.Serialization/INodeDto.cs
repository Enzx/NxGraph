using System.Text.Json.Serialization;

namespace NxGraph.Serialization;

/// <summary>
/// Interface for node DTOs used in graph serialization.
/// </summary>

internal interface INodeDto
{
    int Index { get;  }
}
