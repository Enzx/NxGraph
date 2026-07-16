namespace NxGraph.Tokens;

/// <summary>
/// Discriminates how a <see cref="JoinState"/> decides it has enough arrived tokens to fire.
/// </summary>
public enum JoinKind : byte
{
    /// <summary>Fire when all <c>n</c> expected tokens have arrived (classic AND-join).</summary>
    All = 0,

    /// <summary>Fire on every arrival — the join is a mid-graph merge point.</summary>
    Any = 1,

    /// <summary>Fire when <c>m</c> of the expected tokens have arrived (M-of-N quorum).</summary>
    Quorum = 2,
}

/// <summary>
/// The firing rule of a <see cref="JoinState"/>. A join accumulates arriving tokens and
/// <b>fires</b> whenever the accumulated count reaches <see cref="RequiredCount"/>: the
/// requirement is consumed, one token continues along the join's success edge, and the other
/// consumed tokens retire. Joins re-arm — they can fire many times per run, which is what
/// makes <see cref="Any"/> a merge point and lets stream-style token flows pass through.
/// </summary>
public readonly struct JoinPolicy
{
    private JoinPolicy(JoinKind kind, int count)
    {
        Kind = kind;
        Count = count;
    }

    /// <summary>The firing rule discriminator.</summary>
    public JoinKind Kind { get; }

    /// <summary>The declared count (<c>n</c> for All, <c>m</c> for Quorum, 1 for Any).</summary>
    public int Count { get; }

    /// <summary>Arrivals consumed per firing. A default-constructed policy is invalid (0).</summary>
    public int RequiredCount => Count;

    /// <summary>Fire when <paramref name="expectedTokens"/> tokens have arrived.</summary>
    public static JoinPolicy All(int expectedTokens)
    {
        if (expectedTokens < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedTokens),
                "An All-join must expect at least one token.");
        }

        return new JoinPolicy(JoinKind.All, expectedTokens);
    }

    /// <summary>Fire on every arrival (mid-graph merge).</summary>
    public static JoinPolicy Any => new(JoinKind.Any, 1);

    /// <summary>Fire when <paramref name="requiredTokens"/> of the incoming tokens have arrived.</summary>
    public static JoinPolicy Quorum(int requiredTokens)
    {
        if (requiredTokens < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredTokens),
                "A quorum join must require at least one token.");
        }

        return new JoinPolicy(JoinKind.Quorum, requiredTokens);
    }

    public override string ToString() => Kind switch
    {
        JoinKind.Any => "Any",
        JoinKind.Quorum => $"Quorum({Count})",
        _ => $"All({Count})",
    };
}
