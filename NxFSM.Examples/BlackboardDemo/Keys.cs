using NxGraph.Blackboards;

namespace NxFSM.Examples.BlackboardDemo;

/// <summary>What a guard is currently doing — stored on the guard's own (Graph-scoped) board.</summary>
public enum GuardMode
{
    Patrol = 0,
    Investigate = 1,
    Chase = 2,
    Rest = 3,
}

/// <summary>
/// The shared world memory: one <see cref="BlackboardScope.Global"/> board bound to every
/// guard's machine. A write by one guard (raising the alarm) is visible to all others.
/// </summary>
public static class WorldKeys
{
    public static readonly BlackboardSchema Schema = new("patrol-world", BlackboardScope.Global);

    public static readonly BlackboardKey<int> AlarmLevel = Schema.Register<int>("AlarmLevel");
    public static readonly BlackboardKey<int> IntruderRoom = Schema.Register<int>("IntruderRoom", -1);
    public static readonly BlackboardKey<int> LastSeenRoom = Schema.Register<int>("LastSeenRoom", -1);
    public static readonly BlackboardKey<int> Sightings = Schema.Register<int>("Sightings");
    public static readonly BlackboardKey<bool> IntruderCaught = Schema.Register<bool>("IntruderCaught");
    public static readonly BlackboardKey<string> CaughtBy = Schema.Register<string>("CaughtBy", "");
}

/// <summary>
/// Per-guard working memory: a <see cref="BlackboardScope.Graph"/> schema — every guard's
/// machine binds its own board over this one shared layout.
/// </summary>
public static class GuardKeys
{
    public static readonly BlackboardSchema Schema = new("guard");

    public static readonly BlackboardKey<GuardMode> Mode = Schema.Register<GuardMode>("Mode");
    public static readonly BlackboardKey<int> Room = Schema.Register<int>("Room");
    public static readonly BlackboardKey<int> Suspicion = Schema.Register<int>("Suspicion");
    public static readonly BlackboardKey<int> Stamina = Schema.Register<int>("Stamina", 100);
}
