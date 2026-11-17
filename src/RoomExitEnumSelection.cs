using System;
using System.Collections.Generic;
using System.Linq;

namespace AstralParty.Android;

/// <summary>
/// Choose only a *named* enum member discovered in the current game, never
/// invent protocol enum numbers. Prefer normal leave for guests and close/
/// force-close for hosts where the protocol exposes distinct enum members.
/// </summary>
internal static class RoomExitEnumSelection
{
    internal readonly record struct Value(string Name, int Number);

    public static Value Choose(IReadOnlyList<Value> values, bool host)
    {
        if (values.Count == 0)
            throw new InvalidOperationException("ForceExitType has no readable enum constants.");

        var candidates = values.Where(v => !string.IsNullOrWhiteSpace(v.Name)
                && !string.Equals(v.Name, "value__", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException("ForceExitType constants are unavailable.");

        // Match before broad substring rules; a 'NotForce' value MUST NOT be
        // mistaken for an affirmative force-exit mode.
        var best = candidates.Select(v => (Value: v, Score: Score(v.Name, host)))
            .OrderByDescending(v => v.Score)
            .ThenBy(v => v.Value.Number)
            .First();
        if (best.Score <= 0)
            throw new InvalidOperationException("ForceExitType member names cannot be safely mapped: "
                + string.Join(", ", candidates.Select(v => v.Name + "=" + v.Number)));
        return best.Value;
    }

    private static int Score(string name, bool host)
    {
        var n = name.Replace("_", "").ToLowerInvariant();
        var negativeForce = n.Contains("notforce")
            || n.Contains("noforce")
            || n.Contains("nonforce")
            || n.Contains("withoutforce");
        var unspecified = n.Contains("unspec")
            || n.Contains("unknown")
            || n.Contains("invalid");
        if (unspecified) return -100;
        if (host)
        {
            if (n.Contains("close") || n.Contains("disband")
                || n.Contains("dismiss") || n.Contains("dissolve")
                || n.Contains("destroy")) return 120;
            if (n.Contains("host") || n.Contains("master")
                || n.Contains("owner")) return 110;
            if (!negativeForce && n.Contains("force")) return 90;
            if (n.Contains("normal") || n.Contains("leave")
                || n.Contains("exit")) return 70;
            if (negativeForce || n == "none" || n == "default") return 40;
        }
        else
        {
            if (negativeForce) return 130;
            if (n.Contains("normal") || n.Contains("leave")) return 120;
            if (n.Contains("exit") && !n.Contains("force")) return 110;
            if (n == "none" || n == "default") return 90;
            if (n.Contains("force")) return 10;
        }
        return 0;
    }
}
