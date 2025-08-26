using System.Runtime.Serialization;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

[DataContract] public sealed class NodeTextDto( string name, string logic) : INodeDto
{
    [DataMember(Order = 0)] public string Name { get; } = name;
    [DataMember(Order = 1)] public string Logic { get; } = logic;
}