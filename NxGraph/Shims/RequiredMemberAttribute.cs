// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;
#if  NETSTANDARD2_1

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
internal sealed class RequiredMemberAttribute : Attribute {}
#endif
