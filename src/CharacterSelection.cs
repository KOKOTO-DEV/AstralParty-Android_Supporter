using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Tools;
using System.Reflection;
using FairyGUI;
using UnityEngine;

namespace AstralParty.Android;

internal static partial class CharacterSelection
{
    private const string Prefix = "AstralParty.Android.CharacterSelect.";
    private static readonly string[] Keys = { "P1", "P2", "P3", "P4", "Dealer", "Support", "Card", "Tank" };
    private static readonly string[] Labels = { "P1", "P2", "P3", "P4", "Dealer", "Support", "Card", "Tank", "Random", "Sort" };
    private static readonly HeroTag[] SeatOrder = (HeroTag[])Enum.GetValues(typeof(HeroTag));
    private static GComponent toolbar;
    private static GList activeList;
    private static IList originalCards;
    private static int[] originalOrder = new int[0];
    private static object activePanel;
    private static HeroTags tags;
    private static readonly Dictionary<string, string> Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static bool settingsLoaded;
    private static int mode = Math.Max(0, Math.Min(2, PlayerPrefs.GetInt(Prefix + "Mode", 0)));

    internal static void Update(object panel, GComponent screen)
    {

        var list = Bootstrap.Read(screen, "list_RendererSelectRole") as GList;
        var cards = Bootstrap.Read(panel, "HeroCardDatas") as IList;
        if (panel?.GetType().Name != "RoomHeroPanel" || !Bootstrap.Visible(screen) || list == null || cards == null)
        {
            if (toolbar != null && !toolbar.isDisposed) toolbar.Dispose();
            toolbar = null;
            ClearStripes();
            originalCards = null;
            activePanel = null;
            activeList = null;
            return;
        }

        activePanel = panel;
        LoadSettings();
        var current = cards.Cast<object>().Select(Id).ToArray();
        if (current.Length == 0 || current.Any(id => id <= 0) || current.Distinct().Count() != current.Length) return;
        if (!ReferenceEquals(originalCards, cards) || !new HashSet<int>(originalOrder).SetEquals(current))
        {
            originalCards = cards;
            originalOrder = current;
            ExportRoster(current);
        }

        tags = new HeroTags(Keys.SelectMany((key, index) => ParseIds(Setting(key, ""))
            .Select(id => (id, (HeroTag)index))));
        var priority = mode == 1 ? SeatOrder.Skip(4).Concat(SeatOrder.Take(4)).ToArray() : SeatOrder;
        var desired = mode == 2 ? originalOrder : tags.Sort(originalOrder, priority);
        if (!current.SequenceEqual(desired)) Reorder(panel, list, cards, current, desired);
        if (toolbar == null || toolbar.isDisposed || toolbar.parent != screen || activeList != list)
        {
            if (toolbar != null && !toolbar.isDisposed) toolbar.Dispose();
            activeList = list;
            toolbar = new GComponent { sortingOrder = 12 };
            screen.AddChild(toolbar);
            for (int i = 0; i < Labels.Length; i++) AddButton(i);
        }
        LayoutToolbar(screen, list);
        UpdateStripes(screen, list, cards, priority);
    }

    private static void LayoutToolbar(GComponent screen, GList list)
    {
        var leftCard = Bootstrap.Read(Bootstrap.Read(screen, "com_player_1"), "com_PlayerLabel") as GComponent;
        var rightCard = Bootstrap.Read(Bootstrap.Read(screen, "com_player_4"), "com_PlayerLabel") as GComponent;
        var margin = 12f;
        var leftEdge = leftCard == null ? screen.width * .135f : Position(screen, leftCard).x;
        var rightEdge = rightCard == null ? screen.width * .865f :
            screen.GlobalToLocal(rightCard.LocalToGlobal(new Vector2(rightCard.width, 0))).x;
        var leftWidth = Math.Max(1, Math.Min(226, leftEdge - 2 * margin));
        var rightWidth = Math.Max(1, Math.Min(226, screen.width - rightEdge - 2 * margin));
        var height = 76f;
        var step = height + 16;
        var y = Math.Max(130, screen.height * .18f);
        toolbar.SetXY(0, 0);
        toolbar.SetSize(screen.width, screen.height);
        int[] left = { 0, 1, 2, 3, 8, 9 };
        int[] right = { 4, 5, 6, 7 };
        Place(left, Math.Max(margin, leftEdge - margin - leftWidth), leftWidth);
        Place(right, rightEdge + margin, rightWidth);

        void Place(int[] indices, float x, float width)
        {
            for (int row = 0; row < indices.Length; row++)
            {
                var index = indices[row];
                var item = (GComponent)toolbar.GetChildAt(index);
                var graph = (GGraph)item.GetChildAt(0);
                var label = (GTextField)item.GetChildAt(1);
                item.SetXY(x, y + row * step);
                if (item.width != width || item.height != height)
                {
                    item.SetSize(width, height);
                    graph.SetSize(width, height);
                    var fill = index == 8 ? new Color(.58f,.06f,.41f) :
                        index == 9 ? new Color(.24f,.32f,.13f) : new Color(.15f,.13f,.25f);
                    var line = index == 8 ? new Color(1f,.59f,.83f) :
                        index == 9 ? new Color(.71f,.92f,.43f) : new Color(.76f,.69f,.93f);
                    graph.shape.DrawRoundRect(2, line, fill, 12, 12, 12, 12);
                    var format = label.textFormat;
                    format.size = 30;
                    format.bold = true;
                    label.textFormat = format;
                    label.autoSize = AutoSizeType.Shrink;
                    label.SetSize(width - 12, height);
                    label.align = AlignType.Center;
                    label.verticalAlign = VertAlignType.Middle;
                    label.SetXY(6, 0);
                }
            }
        }
    }

    private static void ExportRoster(int[] roster)
    {
        File.WriteAllLines(Path.Combine(Application.persistentDataPath, "AstralParty.CharacterRoster.txt"),
            roster.Select(id =>
            {
                try
                {
                    var nickname = CharacterHandle.GetCharacterNickName(id);
                    var name = CharacterHandle.GetCharacterName(id);
                    return id + "=" + nickname + (name == nickname ? "" : " (" + name + ")");
                }
                catch { return id.ToString(); }
            }));
    }

    private static void LoadSettings()
    {
        if (settingsLoaded) return;
        var path = Path.Combine(Application.persistentDataPath, "AstralParty.CharacterSelect.cfg");
        if (!File.Exists(path))
        {
            File.WriteAllLines(path, Keys.Select(key => key + "="));
        }
        foreach (var line in File.ReadAllLines(path))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
            Settings[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
        }
        settingsLoaded = true;
    }

    private static string Setting(string key, string fallback) =>
        Settings.TryGetValue(key, out var value) ? value : fallback;

    private static IEnumerable<int> ParseIds(string value) =>
        value.Split(',').Select(token => int.TryParse(token, out var id) ? id : 0).Where(id => id > 0);

    private static int Id(object card) => Convert.ToInt32(Bootstrap.Read(card, "HeroId"));

    private static bool Matches(GObject cell, int id) =>
        cell.GetType().Name == "UIRoomHero_Button_SelectHero_2" &&
        (cell.data?.GetType().Name != "HeroCardData" || Id(cell.data) == id);

    private static void Reorder(object panel, GList list, IList cards, int[] current, int[] desired)
    {
        bool virtualList = Bootstrap.Read(list, "_virtual") is true;
        var cells = Bootstrap.Read(panel, "allHeroItem") as IList;
        if (virtualList ? list.numItems != current.Length :
            cells == null || cells.Count < current.Length || list.numChildren < current.Length) return;
        var cardSnapshot = cards.Cast<object>().ToArray();
        var cellSnapshot = new GObject[current.Length];
        for (int index = 0; !virtualList && index < current.Length; index++)
        {
            var cell = list.GetChildAt(index);
            if (!ReferenceEquals(cell, cells[index]) || !Matches(cell, current[index])) return;
            cellSnapshot[index] = cell;
        }

        try { Apply(desired); }
        catch { Apply(current); throw; }

        void Apply(int[] order)
        {
            for (int index = 0; index < order.Length; index++)
            {
                var source = Array.IndexOf(current, order[index]);
                cards[index] = cardSnapshot[source];
                if (virtualList) continue;
                cells[index] = cellSnapshot[source];
                list.SetChildIndex(cellSnapshot[source], index);
            }
            if (virtualList) list.RefreshVirtualList();
        }
    }

    private static string LocalizedButton(GTextField label, int index, string font = null)
    {
        if (index < Keys.Length)
        {
            var custom = Setting("Label." + Keys[index], "").Trim();
            if (custom.Length != 0) return Bootstrap.Localize(label, custom, Labels[index], font);
        }
        if (index < 4) return Labels[index];
        var translations = new[] { "딜러", "서포터", "카드특화", "탱커", "랜덤" };
        if (index < 9) return Bootstrap.Localize(label, translations[index - 4], Labels[index], font);
        return Bootstrap.Localize(label, mode == 2 ? "기본" : mode == 1 ? "역할순" : "자리순",
            mode == 2 ? "Default" : mode == 1 ? "Role" : "Seat", font);
    }

    private static void AddButton(int index)
    {
        var item = new GComponent { opaque = true };
        var background = new GGraph { touchable = false };
        item.AddChild(background);
        var label = Bootstrap.Label("", 16);
        var caption = LocalizedButton(label, index);
        label.text = caption;
        item.AddChild(label);
        item.onClick.Add(new EventCallback0(() =>
        {
            try
            {
                if (index < 9) Pick(index);
                else
                {
                    mode = (mode + 1) % 3;
                    PlayerPrefs.SetInt(Prefix + "Mode", mode);
                    PlayerPrefs.Save();
                    label.text = LocalizedButton(label, 9);
                }
            }
            catch (Exception error) { Debug.LogError("[AstralParty.Android] " + error); }
        }));
        toolbar.AddChild(item);
    }

    private static void Pick(int tag)
    {
        var panel = activePanel;
        var list = activeList;
        var cards = Bootstrap.Read(panel, "HeroCardDatas") as IList;
        var root = Bootstrap.Read(panel, "UIObject");
        if (cards == null || !(Bootstrap.Read(root, "btn_SureHero") is GObject confirm) ||
            !confirm.onStage || !confirm.touchable) return;
        var chosen = panel.GetType().GetMethod("GetHeroHasChoice",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int) }, null);
        if (chosen == null) return;
        var banned = Bootstrap.Read(panel, "banCharacterIds") as IEnumerable;
        var bannedIds = banned == null ? new HashSet<int>() : new HashSet<int>(banned.Cast<object>().Select(value => Convert.ToInt32(value)));
        var available = cards.Cast<object>().Where(card => Bootstrap.Read(card, "_IsHas") is true)
            .Select(Id).Where(id => !bannedIds.Contains(id) && chosen.Invoke(panel, new object[] { id }) is false);
        var picked = tags.Pick(tag < Keys.Length ? (HeroTag?)tag : null, available);
        if (!picked.HasValue) return;
        int index = Array.IndexOf(cards.Cast<object>().Select(Id).ToArray(), picked.Value);
        if (index < 0) return;
        if (Bootstrap.Read(list, "_virtual") is true)
        {
            list.ScrollToView(index, false, false);
            index = list.ItemIndexToChildIndex(index);
        }
        else
        {
            var cells = Bootstrap.Read(panel, "allHeroItem") as IList;
            if (cells == null || index >= cells.Count || index >= list.numChildren ||
                !ReferenceEquals(cells[index], list.GetChildAt(index))) return;
        }
        if (index < 0 || index >= list.numChildren) return;
        var row = list.GetChildAt(index);
        if (row.visible && row.touchable && Matches(row, picked.Value)) row.onClick.Call();
    }
}
