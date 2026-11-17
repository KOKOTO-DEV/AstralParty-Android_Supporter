using System;
using System.Reflection;
using FairyGUI;
using UnityEngine;

namespace AstralParty.Android;

public static class Bootstrap
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static FieldInfo uiInstance;
    private static float nextUpdate;
    private static string bindingError, selectionError, statsError;

    public static void Tick()
    {
        if (Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + .15f;
        object panel;
        GComponent screen;
        try
        {
            panel = Read(ExistingUiManager(), "currentPanel");
            screen = Read(panel, "UIObject") as GComponent;
        }
        catch (Exception error) { Report(ref bindingError, error); return; }
        try { CharacterSelection.Update(panel, screen); CharacterSelection.UpdateEditor(panel, screen); selectionError = null; }
        catch (Exception error) { Report(ref selectionError, error); }
        try { RoomStats.UpdateLeaveNavigation(panel); RoomStats.Update(screen); statsError = null; }
        catch (Exception error) { Report(ref statsError, error); }
    }

    private static void Report(ref string previous, Exception error)
    {
        if (previous == error.Message) return;
        previous = error.Message;
        Debug.LogError("[AstralParty.Android] " + error);
    }

    internal static object ExistingUiManager() => ExistingInstance(typeof(UI.UIManager), ref uiInstance);

    internal static object ExistingInstance(Type type, ref FieldInfo field)
    {
        for (var current = type; field == null && current != null; current = current.BaseType)
            field = current.GetField("_inst", BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        return field?.GetValue(null);
    }

    internal static object Read(object target, string member)
    {
        for (var type = target?.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(member, Members | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(target);
            var property = type.GetProperty(member, Members | BindingFlags.DeclaredOnly);
            if (property != null) return property.GetValue(target);
        }
        return null;
    }

    internal static bool Visible(GComponent component) =>
        component != null && !component.isDisposed && component.visible && component.onStage;

    internal static string KoreanFont;
    internal static string Localize(GTextField label, string korean, string fallback, string preferred = null)
    {
        bool hasHangul = false;
        foreach (char c in korean) if (c >= '가' && c <= '힣') { hasHangul = true; break; }
        if (!hasHangul) return korean;
        foreach (var name in new[] { preferred, KoreanFont, label.textFormat.font, UIConfig.defaultFont })
        {
            if (string.IsNullOrEmpty(name)) continue;
            var font = FontManager.GetFont(name);
            if (font == null) continue;
            bool supports = true;
            foreach (char c in korean)
                if (c >= '가' && c <= '힣' && !font.HasCharacter(c)) { supports = false; break; }
            if (!supports) continue;
            var style = label.textFormat;
            style.font = name;
            label.textFormat = style;
            return korean;
        }
        return fallback;
    }

    internal static GTextField Label(string text, int size)
    {
        var label = new GTextField { text = text, touchable = false, autoSize = AutoSizeType.Both };
        var format = label.textFormat;
        format.size = size;
        format.color = Color.white;
        label.textFormat = format;
        return label;
    }
}
