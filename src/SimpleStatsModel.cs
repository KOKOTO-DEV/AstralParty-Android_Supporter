using System;

namespace AstralParty.Android;

internal readonly record struct SimpleStatsModel(long PlayerId, bool? StatisticsPublic, bool? HistoryPublic, int? CreditScore,
    int? WinCount = null, int? PlayCount = null, int? PraiseCount = null,
    RecentHistory? Recent = null)
{
    private static readonly System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.InvariantCulture;
    public string Caption => "승률: " + WinRate + " · 좋아요: " + Likes
        + "\n매너: " + (CreditScore?.ToString(Culture) ?? "확인 불가");

    private string WinRate => WinCount.HasValue && PlayCount.HasValue && WinCount >= 0 && PlayCount >= WinCount
        ? (PlayCount == 0 ? "—" : (100m * WinCount.Value / PlayCount.Value).ToString("0.00", Culture) + "%")
            + " (" + WinCount.Value.ToString(Culture) + "/" + PlayCount.Value.ToString(Culture) + ")"
        : StatisticsPublic == false ? "비공개" : "확인 불가";
    private string Likes => PraiseCount.HasValue && PraiseCount >= 0
        ? (decimal.Truncate(PraiseCount.Value * 10m / 3m) / 10m).ToString("0.0", Culture)
            + " (" + PraiseCount.Value.ToString(Culture) + ")" : "확인 불가";

    public SimpleStatsModel WithProfile(string payload)
    {
        var parts = payload.Split('|');
        if (parts.Length is not (5 or 7) || !long.TryParse(parts[0], out var id) || id != PlayerId
            || parts[1] is not ("0" or "1")) return this;
        int? wins = int.TryParse(parts[2], out var w) && w >= 0 ? w : null;
        int? games = int.TryParse(parts[3], out var p) && p >= 0 ? p : null;
        int? likes = int.TryParse(parts[4], out var l) && l >= 0 ? l : null;
        var visible = parts[1] == "1";
        if (!visible || !wins.HasValue || !games.HasValue || wins > games) { wins = null; games = null; }
        var recent = parts.Length == 7 ? RecentHistory.Parse(parts[5], parts[6]) : null;
        return this with { StatisticsPublic = visible, WinCount = wins, PlayCount = games, PraiseCount = likes, Recent = recent };
    }
}



