using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;

namespace AstralParty.Android;

internal static partial class RoomStats
{
    private static GComponent activeScreen;
    private static long expandedId;
    private static GComponent settingsBar;
    private static bool enabled = PlayerPrefs.GetInt("AstralParty.Android.Stats.Enabled", 1) != 0;
    private static bool useNames = PlayerPrefs.GetInt("AstralParty.Android.Stats.Names", 0) != 0;

    private static void UpdateSettings(GComponent screen)
    {
        if (settingsBar == null || settingsBar.isDisposed)
        {
            settingsBar = new GComponent { sortingOrder = 20 };
            screen.AddChild(settingsBar);
        }
        settingsBar.SetSize(194, 118);
        settingsBar.SetXY(Math.Max(0, screen.width - 210), 8);
        if (settingsBar.numChildren != 0) return;
        AddAction(settingsBar, enabled ? "Stats: On" : "Stats: Off", () =>
        {
            enabled = !enabled;
            PlayerPrefs.SetInt("AstralParty.Android.Stats.Enabled", enabled ? 1 : 0);
            PlayerPrefs.Save();
            ResetSettingsButtons();
        });
        AddAction(settingsBar, useNames ? "Real names" : "Nicknames", () =>
        {
            useNames = !useNames;
            PlayerPrefs.SetInt("AstralParty.Android.Stats.Names", useNames ? 1 : 0);
            PlayerPrefs.Save();
            ResetSettingsButtons();
        });
    }

    private static void ResetSettingsButtons()
    {
        while (settingsBar.numChildren > 0) settingsBar.GetChildAt(0).Dispose();
    }

    private static readonly Dictionary<GTextField,GGraph> Backgrounds = new Dictionary<GTextField,GGraph>();
    private static readonly Dictionary<GTextField,long> PlayerIds = new Dictionary<GTextField,long>();
    private static void Remove(GTextField label)
    {
        if (Backgrounds.TryGetValue(label, out var graph) && !graph.isDisposed) graph.Dispose();
        Backgrounds.Remove(label);
        if (ActionBars.TryGetValue(label, out var bar) && !bar.isDisposed) bar.Dispose();
        ActionBars.Remove(label);
        PlayerIds.Remove(label);
        if (!label.isDisposed) label.Dispose();
    }

    private static readonly Dictionary<GComponent, GTextField> Labels = new Dictionary<GComponent, GTextField>();

    internal static void Update(GComponent screen)
    {
        var name = screen?.GetType().Name;
        if (!Bootstrap.Visible(screen) || (name != "UIRoomHeroPanel" && name != "UIRoomWaitPanel"))
            screen = null;
        if (screen != activeScreen)
        {
            foreach (var previous in Labels.Values) Remove(previous);
            Labels.Clear();
            if (settingsBar != null && !settingsBar.isDisposed) settingsBar.Dispose();
            settingsBar = null;
            expandedId = 0;
            activeScreen = screen;
        }
        if (screen == null)
        {
            ProfileBridge.Suspend();
            return;
        }

        UpdateSettings(screen);
        if (!enabled)
        {
            foreach (var label in Labels.Values) Remove(label);
            Labels.Clear();
            ProfileBridge.Suspend();
            return;
        }
        var rows = new List<GComponent>();
        bool choosing = name == "UIRoomHeroPanel";
        if (choosing)
        {
            for (int seat = 1; seat <= 4; seat++)
                if (Bootstrap.Read(screen, "com_player_" + seat) is GComponent row) rows.Add(row);
        }
        else if (Bootstrap.Read(Bootstrap.Read(screen, "com_Player"), "list_playerWait") is GList list)
        {
            for (int index = 0; index < list.numChildren; index++)
                if (list.GetChildAt(index) is GComponent row) rows.Add(row);
        }

        var participants = new HashSet<long>();
        foreach (var row in rows)
        {
            if (!Bootstrap.Visible(row)) continue;
            var player = Bootstrap.Read(row, choosing ? "playerData" : "playerInfo");
            long id = Convert.ToInt64(Bootstrap.Read(player, "Id"));
            if (id > 0 && !(Bootstrap.Read(player, "IsBot") is true)) participants.Add(id);
        }
        ProfileBridge.KeepParticipants(participants);
        foreach (var bar in ActionBars.Values) bar.visible = false;
        foreach (var entry in new List<KeyValuePair<GComponent,GTextField>>(Labels))
        {
            if (entry.Key.isDisposed || entry.Value.isDisposed || !rows.Contains(entry.Key))
            {
                Remove(entry.Value);
                Labels.Remove(entry.Key);
            }
            else { entry.Value.visible = false; Backgrounds[entry.Value].visible = false; }
        }

        foreach (var row in rows)
        {
            if (!Bootstrap.Visible(row)) continue;
            var player = Bootstrap.Read(row, choosing ? "playerData" : "playerInfo");
            var id = Convert.ToInt64(Bootstrap.Read(player, "Id"));
            if (id <= 0 || Bootstrap.Read(player, "IsBot") is true) continue;
            var info = Bootstrap.Read(row, choosing ? "com_PlayerLabel" : "com_Label") as GComponent;
            if (info == null) continue;
            var server = Bootstrap.Read(player, "serverPlayer");
            var credit = Convert.ToInt64(Bootstrap.Read(server, "Id")) == id
                ? Bootstrap.Read(server, "CreditInfo") : null;
            var profile = Convert.ToInt64(Bootstrap.Read(server, "Id")) == id
                ? Bootstrap.Read(server, "ShowPlayer") : null;
            var data = new SimpleStatsModel(id, Bootstrap.Read(profile, "IsShowData") as bool?, null,
                credit == null ? (int?)null : Math.Max(0, Math.Min(100, 100 - Convert.ToInt32(Bootstrap.Read(credit, "DeductScore")))))
                .WithProfile(ProfileBridge.Read(id));

            if (!Labels.TryGetValue(row, out var label))
            {
                label = Bootstrap.Label("", 24);
                var graph = new GGraph { sortingOrder = 10 };
                var ownedLabel = label;
                graph.onClick.Add(new EventCallback0(() =>
                {
                    if (PlayerIds.TryGetValue(ownedLabel, out var playerId))
                        expandedId = expandedId == playerId ? 0 : playerId;
                }));
                screen.AddChild(graph);
                Backgrounds.Add(label, graph);
                label.sortingOrder = 11;
                screen.AddChild(label);
                Labels[row] = label;
            }
            var native = Bootstrap.Read(Bootstrap.Read(info, "com_Name"), "txt_PlayerName") as GTextField;
            if (native != null)
            {
                var format = label.textFormat;
                format.font = native.textFormat.font;
                Bootstrap.KoreanFont = format.font;
                label.textFormat = format;
            }
            PlayerIds[label] = id;
            var caption = choosing ? data.Caption.Replace(" · 좋아요:", "\n좋아요:") : data.Caption;
            label.text = caption + (data.Recent == null ? "" : "\n" + data.Recent.Summary);
            var recent = data.Recent;
            var showDetails = recent != null && recent.Rows.Count > 0;
            if (showDetails) label.text += expandedId == id ? " ▴\n" + recent.Column(0, useNames) + "\n" + recent.Column(5, useNames) : " ▾";
            var background = Backgrounds[label];
            var manner = Bootstrap.Label(data.Caption.Split('\n')[1], 24);
            manner.textFormat = label.textFormat;
            var actionOffset = 24 + manner.textWidth;
            var lineHeight = manner.textHeight;
            manner.Dispose();
            var own = CurrentGame?.account?.GetPlayerID() == id;
            var boxWidth = Math.Max(label.textWidth + 16, actionOffset + (own ? 118 : choosing ? 176 : 354) + 8);
            var boxHeight = label.textHeight + 10;
            var position = screen.GlobalToLocal(info.LocalToGlobal(new Vector2(choosing ? 0 : Math.Max(0, info.width - 88), 0)));
            var x = Math.Max(0, Math.Min(screen.width - boxWidth, choosing ? position.x : position.x - boxWidth));
            var y = Math.Max(0, position.y - boxHeight + 6);
            background.DrawRect(boxWidth, boxHeight, 0, Color.clear, new Color(.07f,.09f,.13f,showDetails && expandedId == id ? .95f : (choosing ? .85f : .88f)));
            background.SetXY(x,y);
            background.touchable = showDetails;
            background.visible = true;
            label.SetXY(x+8,y+5);
            label.visible = true;
            UpdateActions(screen, label, id, x + actionOffset, y + lineHeight * (choosing ? 2 : 1) + 3, choosing, label);
        }
    }
}
