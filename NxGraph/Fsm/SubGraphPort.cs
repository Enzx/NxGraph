using System.Runtime.CompilerServices;
using NxGraph.Behaviors;
using NxGraph.Blackboards;

namespace NxGraph.Fsm;

/// <summary>
/// One declared port on a machine-wrapping composite that runs its child graph on the child's
/// <b>own</b> Graph-scoped board (see <see cref="SubGraphPorts"/>). The interface is the
/// non-generic read surface for serializers and diagnostics — ports are constructed only
/// through <see cref="SubGraphPort"/> and the <see cref="SubGraphPorts"/> builder, so the
/// typed pair (source and target share one <c>T</c>) is enforced by construction and
/// application is a typed copy with no boxing.
/// </summary>
public interface ISubGraphPort
{
    /// <summary>
    /// <see langword="true"/> for an input port (parent source → child target, applied at
    /// every fresh child start); <see langword="false"/> for an output port (child source →
    /// parent target, copied when the child reaches a terminal).
    /// </summary>
    bool IsInput { get; }

    /// <summary>The value type shared by the port's source and target.</summary>
    Type ValueType { get; }

    /// <summary>
    /// The target key's registered name — child-side for an input, parent-side for an
    /// output. The serialization identity of the target.
    /// </summary>
    string TargetKeyName { get; }

    /// <summary>
    /// The source key's registered name: always set for an output (child-side key), set for
    /// an input bound to a parent key, and <see langword="null"/> for an input carrying a
    /// literal.
    /// </summary>
    string? SourceKeyName { get; }

    /// <summary>
    /// The input literal, boxed — meaningful only when <see cref="IsInput"/> is
    /// <see langword="true"/> and <see cref="SourceKeyName"/> is <see langword="null"/>.
    /// Cold-path surface (serialization and diagnostics only); the run loop resolves the
    /// literal typed.
    /// </summary>
    object? SourceLiteral { get; }
}

/// <summary>
/// Factories for individual port declarations. <see cref="In{T}"/>/<see cref="Out{T}"/>
/// create live-key ports (validated against the child graph's declared schema when the
/// owning composite is constructed); <see cref="UnboundIn{T}"/>/<see cref="UnboundOut{T}"/>
/// create the name-bound deserialization rebind forms, which resolve their key names per
/// application against the boards actually bound at that moment, with targeted
/// miss/type-mismatch errors.
/// </summary>
public static class SubGraphPort
{
    /// <summary>
    /// Declares an input port: <paramref name="source"/> resolves in the <b>parent</b>
    /// context (a literal or a parent-side key — the one binding primitive), and its value is
    /// written to <paramref name="target"/> on the child's own board at every fresh child
    /// start, after the board resets to its registered defaults. <paramref name="target"/>
    /// must be a key of the child graph's declared Graph schema (checked when the owning
    /// composite is constructed). A Node-scoped source key is rejected here — the parent's
    /// Node scratch never crosses the composite boundary.
    /// </summary>
    public static ISubGraphPort In<T>(BlackboardValue<T> source, BlackboardKey<T> target)
    {
        if (!target.IsValid)
        {
            throw new ArgumentException(
                "Invalid input port target key — obtain keys via BlackboardSchema.Register<T>(...) on the " +
                "child graph's Graph-scoped schema.", nameof(target));
        }

        if (target.Schema!.Scope != BlackboardScope.Graph)
        {
            throw new ArgumentException(
                $"Input port target '{target.Name}' is {target.Schema.Scope}-scoped — an input port writes " +
                "the child's own Graph-scoped board, so the target must be a key of the child graph's " +
                "declared Graph schema.", nameof(target));
        }

        BlackboardKey<T> sourceKey = source.BoundKey;
        if (sourceKey.IsValid && sourceKey.Schema!.Scope == BlackboardScope.Node)
        {
            throw new ArgumentException(
                $"Input port source key '{sourceKey.Name}' is Node-scoped — the parent's Node scratch is " +
                "per-machine and never crosses the composite boundary, so the source could never resolve. " +
                "Use a Graph- or Global-scoped parent key, or a literal.", nameof(source));
        }

        return new InPort<T>(source, target, target.Name);
    }

    /// <summary>
    /// Declares an output port: when the child run reaches a terminal — success and failure
    /// alike, before the composite returns — the value of <paramref name="source"/> (a key of
    /// the child graph's declared Graph schema; checked when the owning composite is
    /// constructed) is copied to <paramref name="target"/>, resolved in the <b>parent</b>
    /// context (Graph or Global scope; Node scope is rejected here — the ports rule).
    /// </summary>
    public static ISubGraphPort Out<T>(BlackboardKey<T> source, BlackboardKey<T> target)
    {
        if (!source.IsValid)
        {
            throw new ArgumentException(
                "Invalid output port source key — obtain keys via BlackboardSchema.Register<T>(...) on the " +
                "child graph's Graph-scoped schema.", nameof(source));
        }

        if (source.Schema!.Scope != BlackboardScope.Graph)
        {
            throw new ArgumentException(
                $"Output port source '{source.Name}' is {source.Schema.Scope}-scoped — an output port reads " +
                "the child's own Graph-scoped board, so the source must be a key of the child graph's " +
                "declared Graph schema.", nameof(source));
        }

        if (!target.IsValid)
        {
            throw new ArgumentException(
                "Invalid output port target key — obtain keys via BlackboardSchema.Register<T>(...) on a " +
                "Graph- or Global-scoped parent schema.", nameof(target));
        }

        if (target.Schema!.Scope == BlackboardScope.Node)
        {
            throw new ArgumentException(
                $"Output port target '{target.Name}' is Node-scoped — Node scratch resets before the " +
                "parent's next node runs, so the copied value would be gone before anyone could read it. " +
                "Register output targets on a Graph- or Global-scoped schema.", nameof(target));
        }

        return new OutPort<T>(source, source.Name, target, target.Name);
    }

    /// <summary>
    /// Creates a name-bound input port — the deserialization rebind form. The target name
    /// resolves per application against the owned board's schema; a key-bound source
    /// (<see cref="BlackboardValue{T}.Bound"/>) resolves against the parent context's schemas
    /// (Graph, then Global). Misses and type mismatches are targeted errors at application.
    /// </summary>
    public static ISubGraphPort UnboundIn<T>(BlackboardValue<T> source, string targetKeyName)
    {
        ValidateName(targetKeyName, nameof(targetKeyName));
        return new InPort<T>(source, default, targetKeyName);
    }

    /// <summary>
    /// Creates a name-bound output port — the deserialization rebind form. The source name
    /// resolves per copy against the owned board's schema, the target name against the parent
    /// context's schemas (Graph, then Global), with targeted miss/type-mismatch errors.
    /// </summary>
    public static ISubGraphPort UnboundOut<T>(string sourceKeyName, string targetKeyName)
    {
        ValidateName(sourceKeyName, nameof(sourceKeyName));
        ValidateName(targetKeyName, nameof(targetKeyName));
        return new OutPort<T>(default, sourceKeyName, default, targetKeyName);
    }

    private static void ValidateName(string name, string paramName)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Port key name cannot be null or empty.", paramName);
        }
    }
}

/// <summary>
/// The optional ports declaration a machine-wrapping composite accepts: its presence means
/// the composite creates one <see cref="Blackboard"/> from the child graph's declared
/// Graph-scoped schema at construction and substitutes it for the Graph slot when stamping
/// the child (Global forwards unchanged; Node scratch is already per-machine). Inputs apply
/// in declaration order at every fresh child start, after the owned board resets to its
/// registered defaults; outputs copy back in declaration order when the child reaches a
/// terminal — success and failure alike. A history lift-back re-entry applies nothing: the
/// board, like the position, is the survived state.
/// <para>
/// The builder is delegate-free, so the declaration is serializable by construction. It may
/// also be empty — <c>SubGraphPorts.OwnBoard()</c> with no ports still isolates the child on
/// its own board. Declaring ports over a child graph that declares no Graph schema is a
/// construction <see cref="ArgumentException"/> on the owning composite: there is no board
/// to own.
/// </para>
/// </summary>
public sealed class SubGraphPorts
{
    private readonly List<SubGraphPortBase> _ports = [];

    private SubGraphPorts()
    {
    }

    /// <summary>Starts a declaration: the composite owns the child's Graph board.</summary>
    public static SubGraphPorts OwnBoard() => new();

    /// <summary>Adds an input port (see <see cref="SubGraphPort.In{T}"/>).</summary>
    public SubGraphPorts In<T>(BlackboardValue<T> source, BlackboardKey<T> target) =>
        Add(SubGraphPort.In(source, target));

    /// <summary>Adds an output port (see <see cref="SubGraphPort.Out{T}"/>).</summary>
    public SubGraphPorts Out<T>(BlackboardKey<T> source, BlackboardKey<T> target) =>
        Add(SubGraphPort.Out(source, target));

    /// <summary>
    /// Adds a prebuilt port — one produced by the <see cref="SubGraphPort"/> factories,
    /// including the name-bound rebind forms. Foreign <see cref="ISubGraphPort"/>
    /// implementations are rejected: the typed pair contract lives in the library's own
    /// port classes.
    /// </summary>
    public SubGraphPorts Add(ISubGraphPort port)
    {
        if (port is not SubGraphPortBase known)
        {
            throw new ArgumentException(
                "Unknown port implementation — construct ports via SubGraphPort.In/Out (or the Unbound " +
                "factories); ISubGraphPort is a read surface, not an extension point.", nameof(port));
        }

        _ports.Add(known);
        return this;
    }

    /// <summary>The declared ports, in declaration order.</summary>
    public IReadOnlyList<ISubGraphPort> Ports => _ports;

    /// <summary>Snapshots the declaration for a machine (later builder mutation is invisible).</summary>
    internal SubGraphPortBase[] Snapshot() => _ports.ToArray();
}

/// <summary>
/// Internal port implementation base: the machines walk one array of these per run boundary
/// — <see cref="ApplyInput"/> at every fresh child start (no-op on output ports) and
/// <see cref="CopyOutput"/> at the child's terminal (no-op on input ports) — so declaration
/// order is application order and both walks are typed copies with zero allocation.
/// </summary>
internal abstract class SubGraphPortBase : ISubGraphPort
{
    public abstract bool IsInput { get; }

    public abstract Type ValueType { get; }

    public abstract string TargetKeyName { get; }

    public abstract string? SourceKeyName { get; }

    public abstract object? SourceLiteral { get; }

    /// <summary>Live-key validation against the child graph's declared Graph schema (composite construction).</summary>
    internal abstract void Validate(BlackboardSchema childSchema, string paramName);

    /// <summary>Resolves the source in the parent context and writes the child-side target on the owned board.</summary>
    internal virtual void ApplyInput(in BlackboardContext parent, Blackboard ownedBoard)
    {
    }

    /// <summary>Reads the child-side source and writes the parent-side target.</summary>
    internal virtual void CopyOutput(in BlackboardContext child, in BlackboardContext parent)
    {
    }
}

internal sealed class InPort<T>(BlackboardValue<T> source, BlackboardKey<T> target, string targetName)
    : SubGraphPortBase
{
    private readonly BlackboardValue<T> _source = source;
    private readonly BlackboardKey<T> _target = target; // valid only for the live form
    private readonly string _targetName = targetName;

    public override bool IsInput => true;

    public override Type ValueType => typeof(T);

    public override string TargetKeyName => _targetName;

    public override string? SourceKeyName => _source.KeyName;

    public override object? SourceLiteral => _source.IsBound ? null : _source.Literal;

    internal override void Validate(BlackboardSchema childSchema, string paramName)
    {
        if (_target.IsValid && !ReferenceEquals(_target.Schema, childSchema))
        {
            throw new ArgumentException(
                $"Input port target '{_targetName}' does not belong to the child graph's declared " +
                "Graph schema — an input port writes the child's own board, so the target must be one of " +
                "the child's declared keys.", paramName);
        }
    }

    internal override void ApplyInput(in BlackboardContext parent, Blackboard ownedBoard)
    {
        T value = _source.Resolve(in parent);
        if (_target.IsValid)
        {
            ownedBoard.Set(_target, value);
            return;
        }

        ownedBoard.Set(PortKeys.ResolveChild<T>(ownedBoard, _targetName), value);
    }
}

internal sealed class OutPort<T>(BlackboardKey<T> source, string sourceName, BlackboardKey<T> target,
    string targetName) : SubGraphPortBase
{
    private readonly BlackboardKey<T> _source = source; // valid only for the live form
    private readonly string _sourceName = sourceName;
    private readonly BlackboardKey<T> _target = target; // valid only for the live form
    private readonly string _targetName = targetName;

    public override bool IsInput => false;

    public override Type ValueType => typeof(T);

    public override string TargetKeyName => _targetName;

    public override string? SourceKeyName => _sourceName;

    public override object? SourceLiteral => null;

    internal override void Validate(BlackboardSchema childSchema, string paramName)
    {
        if (_source.IsValid && !ReferenceEquals(_source.Schema, childSchema))
        {
            throw new ArgumentException(
                $"Output port source '{_sourceName}' does not belong to the child graph's declared " +
                "Graph schema — an output port reads the child's own board, so the source must be one of " +
                "the child's declared keys.", paramName);
        }
    }

    internal override void CopyOutput(in BlackboardContext child, in BlackboardContext parent)
    {
        T value;
        if (_source.IsValid)
        {
            value = child.Get(_source);
        }
        else
        {
            Blackboard? board = child.Graph;
            if (board is null)
            {
                PortKeys.ThrowNoChildBoard(_sourceName);
            }

            value = board!.Get(PortKeys.ResolveChild<T>(board, _sourceName));
        }

        if (_target.IsValid)
        {
            parent.Set(_target, value);
            return;
        }

        parent.Set(PortKeys.ResolveParent<T>(in parent, _targetName), value);
    }
}

/// <summary>
/// Name-bound port key resolution (the deserialization rebind path): child-side names resolve
/// against the owned board's schema, parent-side names against the parent context's schemas
/// in Graph → Global order (Node is rejected at construction and never probed). One
/// dictionary lookup per application, zero allocation; failures are targeted
/// <c>NoInlining</c> throws in the event-entry rebind style.
/// </summary>
internal static class PortKeys
{
    internal static BlackboardKey<T> ResolveChild<T>(Blackboard ownedBoard, string name)
    {
        if (ownedBoard.Schema.TryResolve(name, out BlackboardKey<T> key))
        {
            return key;
        }

        return ThrowUnresolvedChild<T>(ownedBoard, name);
    }

    internal static BlackboardKey<T> ResolveParent<T>(in BlackboardContext parent, string name)
    {
        if (parent.Graph is { } graph && graph.Schema.TryResolve(name, out BlackboardKey<T> key))
        {
            return key;
        }

        if (parent.Global is { } global && global.Schema.TryResolve(name, out key))
        {
            return key;
        }

        return ThrowUnresolvedParent<T>(in parent, name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowNoChildBoard(string keyName) =>
        throw new InvalidOperationException(
            $"Sub-graph port '{keyName}' cannot resolve — the composite owns the child's Graph board but " +
            "none exists. A deserialized child graph carries no schema declarations: bind a Graph-scoped " +
            "board on the child machine (SetBlackboard) to supply the owned board before running.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BlackboardKey<T> ThrowUnresolvedChild<T>(Blackboard ownedBoard, string name)
    {
        ReportMismatch<T>(ownedBoard, name, "child");

        throw new InvalidOperationException(
            $"Sub-graph port key '{name}' does not exist on the child's own board schema " +
            $"'{ownedBoard.Schema.Name ?? "<unnamed>"}' — a deserialized port resolves its child-side key " +
            "by name against the owned board.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BlackboardKey<T> ThrowUnresolvedParent<T>(in BlackboardContext parent, string name)
    {
        ReportMismatch<T>(parent.Graph, name, "parent Graph");
        ReportMismatch<T>(parent.Global, name, "parent Global");

        throw new InvalidOperationException(
            $"Sub-graph port key '{name}' does not exist on any parent-side blackboard schema (checked " +
            "Graph and Global scopes) — a deserialized port resolves its parent-side key by name against " +
            "the boards bound at application time.");
    }

    private static void ReportMismatch<T>(Blackboard? board, string name, string sideWord)
    {
        if (board is not null && board.Schema.TryGetKey(name, out BlackboardKeyDescriptor descriptor))
        {
            throw new InvalidOperationException(
                $"Sub-graph port key '{name}' is declared as '{descriptor.ValueType}' on the {sideWord} " +
                $"schema '{board.Schema.Name ?? "<unnamed>"}' but the port carries '{typeof(T)}'.");
        }
    }
}
