using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central ref-counted lock for player-facing input (move, look, interact).
/// Pause, cinematics, and NPC capture each acquire/release a named reason so
/// overlapping systems do not fight each other.
/// </summary>
public static class PlayerControlGate
{
    public const string ReasonPause = "pause";
    public const string ReasonCaptured = "captured";
    public const string ReasonCinematic = "cinematic";
    public const string ReasonReading = "reading";
    public const string ReasonLockpick = "lockpick";

    static readonly Dictionary<string, int> Counts = new();

    public static bool IsInputBlocked
    {
        get
        {
            foreach (var kv in Counts)
            {
                if (kv.Value > 0)
                    return true;
            }

            return false;
        }
    }

    public static bool IsAcquired(string reason) =>
        !string.IsNullOrEmpty(reason) && Counts.TryGetValue(reason, out int c) && c > 0;

    public static void Acquire(string reason)
    {
        if (string.IsNullOrEmpty(reason))
            reason = "unknown";

        Counts.TryGetValue(reason, out int c);
        Counts[reason] = c + 1;
    }

    public static void Release(string reason)
    {
        if (string.IsNullOrEmpty(reason))
            return;

        if (!Counts.TryGetValue(reason, out int c) || c <= 0)
            return;

        c--;
        if (c <= 0)
            Counts.Remove(reason);
        else
            Counts[reason] = c;
    }

    /// <summary>Clears every lock (e.g. when leaving gameplay for main menu).</summary>
    public static void Clear() => Counts.Clear();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnDomainReload() => Counts.Clear();
}
