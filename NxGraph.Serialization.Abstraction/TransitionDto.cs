using System.Runtime.Serialization;

namespace NxGraph.Serialization.Abstraction;

[DataContract]
public sealed class TransitionDto(int destination)
{
  

    [DataMember(Order = 0)]
    public int Destination { get; set; } = destination;
}