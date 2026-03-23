namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>Creates a <see cref="TimeSpan"/> from <paramref name="milliseconds"/>.</summary>
    public static TimeSpan Milliseconds(this int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    /// <summary>Creates a <see cref="TimeSpan"/> from <paramref name="milliseconds"/>.</summary>
    public static TimeSpan Milliseconds(this double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    /// <summary>Creates a <see cref="TimeSpan"/> from <paramref name="seconds"/>.</summary>
    public static TimeSpan Seconds(this int seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>Creates a <see cref="TimeSpan"/> from <paramref name="seconds"/>.</summary>
    public static TimeSpan Seconds(this double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>Creates a <see cref="TimeSpan"/> from <paramref name="minutes"/>.</summary>
    public static TimeSpan Minutes(this int minutes) => TimeSpan.FromMinutes(minutes);

    /// <summary>Creates a <see cref="TimeSpan"/> from <paramref name="minutes"/>.</summary>
    public static TimeSpan Minutes(this double minutes) => TimeSpan.FromMinutes(minutes);

    /// <summary>Creates a <see cref="TimeSpan"/> from <paramref name="hours"/>.</summary>
    public static TimeSpan Hours(this int hours) => TimeSpan.FromHours(hours);

    /// <summary>Creates a <see cref="TimeSpan"/> from <paramref name="hours"/>.</summary>
    public static TimeSpan Hours(this double hours) => TimeSpan.FromHours(hours);

    /// <summary>Creates a <see cref="TimeSpan"/> from <paramref name="days"/>.</summary>
    public static TimeSpan Days(this int days) => TimeSpan.FromDays(days);

    /// <summary>Creates a <see cref="TimeSpan"/> from <paramref name="days"/>.</summary>
    public static TimeSpan Days(this double days) => TimeSpan.FromDays(days);
}