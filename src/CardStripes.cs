using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FairyGUI;
using UnityEngine;

namespace AstralParty.Android;
internal static partial class CharacterSelection
{
    private static readonly List<GGraph> stripes = new List<GGraph>();
    private static readonly Color[] stripeColors = { new Color(.96f,.27f,.33f), new Color(.42f,.81f,.24f), new Color(.25f,.62f,.98f), new Color(.98f,.71f,.22f) };

    private static void ClearStripes()
    {
        foreach (var stripe in stripes) if (!stripe.isDisposed) stripe.Dispose();
        stripes.Clear();
    }

    private static void UpdateStripes(GComponent screen, GList list, IList cards, HeroTag[] priority)
    {
        if (stripes.Any(graph => graph.isDisposed || graph.parent != screen)) ClearStripes();
        var roster = cards.Cast<object>().Select(Id).ToArray();
        var slots = tags.ColorSlots(roster, priority);
        var drawn = 0;
        var virtualList = Bootstrap.Read(list, "_virtual") is true;
        for (int i = 0; i < list.numChildren; i++)
        {
            var cell = list.GetChildAt(i);
            var index = virtualList ? list.ChildIndexToItemIndex(i) : i;
            if (!cell.visible || !cell.onStage || index < 0 || index >= roster.Length ||
                !Matches(cell, roster[index]) || !slots.TryGetValue(roster[index], out var slot)) continue;
            if (drawn == stripes.Count)
            {
                var graph = new GGraph { touchable = false, sortingOrder = 9 };
                screen.AddChild(graph);
                stripes.Add(graph);
            }
            var stripe = stripes[drawn++];
            var start = Position(screen, cell);
            var end = screen.GlobalToLocal(cell.LocalToGlobal(new Vector2(cell.width, cell.height)));
            var width = end.x - start.x;
            var stripeWidth = Math.Max(1, width * .70f);
            var center = start.x + width * .55f;
            stripe.DrawRect(stripeWidth, 4, 0, Color.clear, stripeColors[slot]);
            stripe.SetXY(center - stripeWidth / 2, end.y - 5);
            stripe.visible = true;
        }
        for (int i = drawn; i < stripes.Count; i++) stripes[i].visible = false;
    }
}
