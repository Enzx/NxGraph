namespace NxGraph.Fsm;

/// <summary>
/// Allocation-free set of region indices for the dynamic parallel composites
/// (<see cref="DynamicParallelState"/> / <see cref="Async.AsyncDynamicParallelState"/>),
/// backed by a single <see cref="ulong"/> — which caps dynamic composites at 64 regions.
/// <para>
/// Build masks with <see cref="Of"/> at setup time (its <c>params</c> array allocates), or
/// compose them allocation-free inside a selector with <see cref="Bit"/> and
/// <see cref="op_BitwiseOr"/>: <c>RegionMask.Bit(0) | RegionMask.Bit(2)</c>. Selectors run at
/// composite entry — once per composite execution — so they must stay allocation-free to
/// preserve the 0 B guarantee.
/// </para>
/// </summary>
public readonly struct RegionMask : IEquatable<RegionMask>
{
    private readonly ulong _bits;

    private RegionMask(ulong bits) => _bits = bits;

    internal ulong Bits => _bits;

    /// <summary>The empty selection. A selector returning it makes the composite succeed as a vacuous join.</summary>
    public static RegionMask None => default;

    /// <summary>A mask with the single region <paramref name="index"/> (0–63) selected. Allocation-free.</summary>
    public static RegionMask Bit(int index)
    {
        if ((uint)index >= 64)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Region index must be in 0..63.");
        }

        return new RegionMask(1UL << index);
    }

    /// <summary>
    /// A mask with the given region indices selected. The <c>params</c> array allocates —
    /// intended for setup time; inside a selector compose with <see cref="Bit"/> and <c>|</c> instead.
    /// </summary>
    public static RegionMask Of(params int[] indices)
    {
        ulong bits = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            if ((uint)indices[i] >= 64)
            {
                throw new ArgumentOutOfRangeException(nameof(indices), indices[i],
                    "Region index must be in 0..63.");
            }

            bits |= 1UL << indices[i];
        }

        return new RegionMask(bits);
    }

    /// <summary>A mask with the first <paramref name="count"/> regions (0–64) selected.</summary>
    public static RegionMask All(int count)
    {
        if ((uint)count > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Region count must be in 0..64.");
        }

        return new RegionMask(count == 64 ? ulong.MaxValue : (1UL << count) - 1);
    }

    /// <summary><see langword="true"/> when region <paramref name="index"/> is selected.</summary>
    public bool Contains(int index) => (uint)index < 64 && (_bits >> index & 1UL) != 0;

    /// <summary><see langword="true"/> when no region is selected.</summary>
    public bool IsEmpty => _bits == 0;

    /// <summary>Number of selected regions.</summary>
    public int Count
    {
        get
        {
            // SWAR popcount — portable across net8.0/netstandard2.1 without BitOperations.
            ulong v = _bits;
            v -= (v >> 1) & 0x5555555555555555UL;
            v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
            v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((v * 0x0101010101010101UL) >> 56);
        }
    }

    /// <summary>Union of two masks. Allocation-free selector composition.</summary>
    public static RegionMask operator |(RegionMask left, RegionMask right) =>
        new(left._bits | right._bits);

    public bool Equals(RegionMask other) => _bits == other._bits;
    public override bool Equals(object? obj) => obj is RegionMask other && Equals(other);
    public override int GetHashCode() => _bits.GetHashCode();
    public static bool operator ==(RegionMask left, RegionMask right) => left.Equals(right);
    public static bool operator !=(RegionMask left, RegionMask right) => !left.Equals(right);

    public override string ToString() => $"RegionMask(0x{_bits:X})";
}
