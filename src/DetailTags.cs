using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using FairyGUI;
using UnityEngine;
using Tools;

namespace AstralParty.Android;

internal static partial class CharacterSelection
{
    private static GComponent editor;
    private static int editedHero;
    private static Dictionary<string,int> localizedNames;

    internal static void UpdateEditor(object panel, GComponent screen)
    {
        if (panel?.GetType().Name != "HeroPanel" || !Bootstrap.Visible(screen))
        {
            RemoveEditor();
            return;
        }
        LoadSettings();
        if (localizedNames == null)
        {
            localizedNames = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            foreach (int id in Enumerable.Range(101, 30).Concat(Enumerable.Range(301, 6)))
                try
                {
                    var name = CharacterHandle.GetCharacterName(id);
                    if (!string.IsNullOrWhiteSpace(name)) localizedNames[name.Trim()] = id;
                }
                catch { }
        }
        var title = DetailLabels(screen, 0).Where(label =>
            localizedNames.ContainsKey(label.text.Trim()) && Position(screen, label).x >= screen.width * .40f)
            .OrderBy(label => Position(screen, label).y).FirstOrDefault();
        if (title == null)
        {
            RemoveEditor();
            return;
        }
        var idForTitle = localizedNames[title.text.Trim()];
        if (editor != null && !editor.isDisposed && editor.parent == screen && editedHero == idForTitle) return;
        RemoveEditor();
        editedHero = idForTitle;
        editor = new GComponent { sortingOrder = 20 };
        editor.SetSize(186, 8 * 80);
        editor.SetXY(Math.Max(0, screen.width * .526f - editor.width + 2), screen.height * .17f);
        screen.AddChild(editor);
        for (int index = 0; index < Keys.Length; index++)
        {
            var key = Keys[index];
            var label = Bootstrap.Label("", 32);
            var style = label.textFormat;
            label.textFormat = style;
            var item = new GComponent { opaque = true };
            var graph = new GGraph { touchable = false };
            var caption = LocalizedButton(label, index, title.textFormat.font);
            void Paint()
            {
                bool enabled = ParseIds(Setting(key, "")).Contains(idForTitle);
                var fill = enabled ? new Color(.57f,.98f,0f) : new Color(1f,.31f,.67f);
                graph.SetSize(186, 67);
                graph.shape.DrawRoundRect(2, new Color(.13f,.15f,.20f), fill, 15, 0, 15, 0);
                var format = label.textFormat;
                format.color = new Color(.13f,.15f,.20f);
                format.bold = true;
                label.textFormat = format;
                label.text = caption;
            }
            item.SetSize(186, 67);
            item.SetXY(0, index * 80);
            label.autoSize = AutoSizeType.Shrink;
            label.SetSize(186, 67);
            label.align = AlignType.Center;
            label.verticalAlign = VertAlignType.Middle;
            item.AddChild(graph);
            item.AddChild(label);
            item.onClick.Add(new EventCallback0(() =>
            {
                try
                {
                    var ids = new HashSet<int>(ParseIds(Setting(key, "")));
                    if (!ids.Remove(idForTitle)) ids.Add(idForTitle);
                    var value = string.Join(",", ids.OrderBy(id => id));
                    SaveTag(key, value);
                    Settings[key] = value;
                    Paint();
                }
                catch (Exception error) { Debug.LogError("[AstralParty.Android] " + error); }
            }));
            Paint();
            editor.AddChild(item);
        }
    }

    private static void SaveTag(string key, string value)
    {
        var path = Path.Combine(Application.persistentDataPath, "AstralParty.CharacterSelect.cfg");
        var lines = File.ReadAllLines(path).ToList();
        var index = lines.FindLastIndex(line =>
        {
            var separator = line.IndexOf('=');
            return separator > 0 && line.Substring(0, separator).Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
        });
        var entry = key + "=" + value;
        if (index < 0) lines.Add(entry);
        else lines[index] = entry;
        var staged = path + ".tmp";
        File.WriteAllLines(staged, lines);
        try { File.Replace(staged, path, null); }
        catch (PlatformNotSupportedException) { File.Copy(staged, path, true); }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    private static void RemoveEditor()
    {
        if (editor != null && !editor.isDisposed) editor.Dispose();
        editor = null;
    }

    private static Vector2 Position(GComponent screen, GObject node) =>
        screen.GlobalToLocal(node.LocalToGlobal(Vector2.zero));

    private static IEnumerable<GTextField> DetailLabels(GObject node, int depth)
    {
        if (!node.visible || !node.onStage || ReferenceEquals(node, editor)) yield break;
        if (node is GTextField label && !string.IsNullOrWhiteSpace(label.text)) yield return label;
        if (depth >= 5 || node is GList || !(node is GComponent group)) yield break;
        for (int i = 0; i < Math.Min(group.numChildren, 64); i++)
            foreach (var child in DetailLabels(group.GetChildAt(i), depth + 1)) yield return child;
    }
}
