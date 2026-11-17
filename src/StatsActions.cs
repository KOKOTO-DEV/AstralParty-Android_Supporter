using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FairyGUI;
using GameLogic;
using UnityEngine;

namespace AstralParty.Android;

internal static partial class RoomStats
{
    private static readonly Dictionary<GTextField,GComponent> ActionBars = new Dictionary<GTextField,GComponent>();
    private static float nextAction;
    private static long leavingRoom;
    private static float leaveStarted, nextRoute;
    private static int routeAttempts;

    internal static void UpdateLeaveNavigation(object panel)
    {
        if (leavingRoom == 0) return;
        var name = panel?.GetType().Name;
        var now = Time.unscaledTime;
        if ((name != "RoomWaitPanel" && name != "RoomHeroPanel") || now - leaveStarted > 13)
        {
            leavingRoom = 0;
            return;
        }
        if (now < nextRoute || routeAttempts >= 4) return;
        var game = Bootstrap.ExistingInstance(typeof(GameLogicManager), ref gameField) as GameLogicManager;
        if (game == null) return;
        var roomLogic = Bootstrap.Read(game, "room") ?? Bootstrap.Read(game, "<room>k__BackingField");
        var controller = Bootstrap.Read(roomLogic, "roomController");
        var room = Bootstrap.Read(controller, "_LocalRoom");
        var roomId = Convert.ToInt64(Bootstrap.Read(room, "Id"));
        if (roomId > 0 && roomId != leavingRoom) { leavingRoom = 0; return; }
        var state = Bootstrap.Read(controller, "roomStateType");
        if (roomId == leavingRoom && (state == null || Convert.ToInt32(state) != 0)) return;

        nextRoute = now + .65f;
        var route = routeAttempts++;
        var ui = Bootstrap.ExistingUiManager();
        if (ui == null) return;
        var methods = ui.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var back = methods.FirstOrDefault(method =>
        {
            var args = method.GetParameters();
            return method.Name == "ReturnToLastPanel" && args.Length == 2 &&
                args[0].ParameterType.IsInstanceOfType(panel) && args[1].ParameterType == typeof(object[]);
        });
        if (route == 0 || (route == 1 && name == "RoomWaitPanel"))
        {
            if (back != null) back.Invoke(ui, new object[] { panel, Array.Empty<object>() });
            return;
        }
        var open = methods.FirstOrDefault(method =>
        {
            var args = method.GetParameters();
            return method.Name == "OpenPanel" && args.Length == 2 &&
                args[0].ParameterType.IsEnum && args[0].ParameterType.Name == "UIPanelType" &&
                args[1].ParameterType == typeof(object[]);
        });
        if (open == null) return;
        var type = open.GetParameters()[0].ParameterType;
        var candidates = route == 3
            ? new[] { "Main", "MainPanel", "MainMenu", "MainMenuPanel", "Home", "HomePanel" }
            : new[] { "RoomList", "RoomListPanel" };
        foreach (var candidate in candidates)
        {
            if (!Enum.IsDefined(type, candidate)) continue;
            open.Invoke(ui, new object[] { Enum.Parse(type, candidate), Array.Empty<object>() });
            return;
        }
    }

    private static GameLogicManager CurrentGame;
    private static System.Reflection.FieldInfo gameField;

    private static object LocalRoom(GameLogicManager game) =>
        Bootstrap.Read(Bootstrap.Read(Bootstrap.Read(game, "room"), "roomController"), "_LocalRoom");

    private static void UpdateActions(GComponent screen, GTextField label, long id, float x, float y, bool choosing, GTextField style)
    {
        CurrentGame = Bootstrap.ExistingInstance(typeof(GameLogicManager), ref gameField) as GameLogicManager;
        if (CurrentGame?.account == null) return;
        var local = LocalRoom(CurrentGame);
        var roomId = Convert.ToInt64(Bootstrap.Read(local, "Id"));
        if (roomId <= 0) return;
        var own = CurrentGame.account.GetPlayerID();
        var host = Convert.ToInt64(Bootstrap.Read(local, "MasterId")) == own;
        if (!ActionBars.TryGetValue(label, out var bar))
        {
            bar = new GComponent { sortingOrder = 14 };
            screen.AddChild(bar);
            ActionBars[label] = bar;
        }
        bar.visible = true;
        var actionWidth = choosing && id != own ? 86 : 116;
        bar.SetSize(id == own ? 118 : choosing ? 176 : 354, 30);
        bar.SetXY(x, y);
        if (bar.data is string key && key == id + ":" + roomId + ":" + host + ":" + choosing) return;
        bar.data = id + ":" + roomId + ":" + host + ":" + choosing;
        while (bar.numChildren > 0) bar.GetChildAt(0).Dispose();
        if (id == own)
        {
            AddAction(bar, host ? "[방 닫기]" : "[방 나가기]", () => Execute(label, roomId, id, "leave"), actionWidth, 30, style);
            return;
        }
        AddAction(bar, "[등록]", () => Execute(label, roomId, id, "friend"), actionWidth, 30, style);
        AddAction(bar, "[차단]", () => Execute(label, roomId, id, "block"), actionWidth, 30, style);
        if (host && !choosing) AddAction(bar, "[내보내기]", () => Execute(label, roomId, id, "kick"), actionWidth, 30, style);
    }

    private static void AddAction(GComponent bar, string caption, EventCallback0 action, float width = 194, float height = 55, GTextField style = null)
    {
        var button = new GComponent { opaque = true };
        button.SetXY(style == null ? 0 : bar.numChildren * (width + 2), style == null ? bar.numChildren * (height + 8) : 0);
        button.SetSize(width, height);
        var background = new GGraph { touchable = false };
        background.DrawRect(width, height, 0, Color.clear, style == null ? new Color(.14f,.11f,.23f,1) : Color.clear);
        button.AddChild(background);
        var text = Bootstrap.Label(caption, style == null ? 25 : 21);
        if (style != null) { var format = text.textFormat; format.font = style.textFormat.font; format.color = new Color(1f,.74f,.62f); text.textFormat = format; }
        if (style == null)
        {
            var korean = caption == "Stats: On" ? "통계: 표시" : caption == "Stats: Off" ? "통계: 숨김" : caption == "Real names" ? "캐릭터: 이름" : "캐릭터: 이명";
            text.text = Bootstrap.Localize(text, korean, caption);
        }
        if (style != null)
        {
            var fallback = caption == "[방 닫기]" ? "[Close]" : caption == "[방 나가기]" ? "[Leave]" :
                caption == "[등록]" ? "[Add]" : caption == "[차단]" ? "[Block]" : "[Kick]";
            text.text = Bootstrap.Localize(text, caption, fallback, style.textFormat.font);
        }
        var fontStyle = text.textFormat;
        fontStyle.bold = true;
        text.textFormat = fontStyle;
        text.autoSize = AutoSizeType.Shrink;
        text.SetSize(width - 8, height);
        text.align = AlignType.Center;
        text.verticalAlign = VertAlignType.Middle;
        text.SetXY(4, 0);
        button.AddChild(text);
        button.onClick.Add(action);
        bar.AddChild(button);
    }

    private static void Execute(GTextField label, long roomId, long target, string action)
    {
        try
        {
            if (Time.unscaledTime < nextAction || !label.visible || !label.onStage ||
                !PlayerIds.TryGetValue(label, out var current) || current != target) return;
            var panel = Bootstrap.Read(Bootstrap.ExistingUiManager(), "currentPanel");
            var panelName = panel?.GetType().Name;
            if (!ReferenceEquals(Bootstrap.Read(panel, "UIObject"), activeScreen) ||
                (panelName != "RoomWaitPanel" && panelName != "RoomHeroPanel")) return;
            var game = Bootstrap.ExistingInstance(typeof(GameLogicManager), ref gameField) as GameLogicManager;
            if (game?.account == null || Convert.ToInt64(Bootstrap.Read(LocalRoom(game), "Id")) != roomId) return;
            var own = game.account.GetPlayerID();
            if (action == "leave" && leavingRoom != 0) return;
            nextAction = Time.unscaledTime + 2;
            if (action == "leave" || action == "kick")
            {
                if ((action == "leave" || panelName == "RoomWaitPanel") && RoomActions.Invoke(action, roomId, own, target) && action == "leave")
                {
                    leavingRoom = roomId;
                    leaveStarted = Time.unscaledTime;
                    nextRoute = leaveStarted + .2f;
                    routeAttempts = 0;
                }
                return;
            }
            var friend = game.friend;
            if (own == target || friend == null || friend.IsBlack(target)) return;
            if (action == "friend" && !friend.IsFriend(target)) friend.RequestFriendApplyC2S(target);
            if (action == "block") friend.RequestFriendOpC2S(target, string.Empty, 2, null);
        }
        catch (Exception error) { Debug.LogError("[AstralParty.Android] " + error); }
    }
}
