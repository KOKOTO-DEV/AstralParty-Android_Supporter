using System;
using System.Reflection;
using System.Linq;
using GameLogic;
using Tools;

namespace AstralParty.Android;

/// <summary>
/// Calls the existing game's room methods, not an invented RPC command ID.
/// Fail closed if a game update removes the expected entry point. This code
/// runs in HybridCLR, where the game's room controller types are accessible.
/// </summary>
internal static class RoomActions
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] KickNames =
    {
        "RequestKickRoomPlayerC2S", "RequestKickPlayerC2S", "RequestKickOutPlayerC2S",
        "RequestRoomKickC2S", "RequestRemoveRoomPlayerC2S",
        "RequestKickRoomPlayer", "RequestKickPlayer", "KickRoomPlayer",
        "KickPlayer", "KickOutPlayer"
    };

    private static object? Member(object? target, string name)
    {
        if (target == null) return null;
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var prop = type.GetProperty(name, Instance);
            if (prop != null && prop.GetIndexParameters().Length == 0) return prop.GetValue(target);
            var field = type.GetField(name, Instance);
            if (field != null) return field.GetValue(target);
        }
        return null;
    }

    private static long Long(object? value)
    {
        return value is long id ? id : value is int n ? n : 0;
    }

    public static bool Invoke(string action, long currentRoomId, long accountId, long targetId)
    {
        var game = SimpleSingletonProvider<GameLogicManager>.inst;
        var roomLogic = Member(game, "room") ?? Member(game, "<room>k__BackingField");
        var controller = Member(roomLogic, "roomController");
        var room = Member(controller, "_LocalRoom");
        if (room == null || Long(Member(room, "Id")) != currentRoomId)
            throw new InvalidOperationException("Room action aborted: current room changed.");
        if (action == "leave")
        {
            if (targetId != accountId) return false;
        }
        else if (action == "kick")
        {
            // Check against the actual room model again at execution time, in
            // addition to the earlier UI/bridge ownership check.
            if (targetId == accountId || Long(Member(room, "MasterId")) != accountId) return false;
        }
        else return false;

        if (action == "leave")
        {
            if (targetId != accountId) return false;
            var exit = roomLogic.GetType().GetMethods(Instance).SingleOrDefault(method =>
            {
                var parameters = method.GetParameters();
                return method.Name == "RequestExitRoomC2S" && parameters.Length == 1 &&
                    parameters[0].ParameterType.FullName == "party.protocol.ExitRoomC2S+Types+ForceExitType";
            });
            if (exit == null) throw new MissingMethodException("RequestExitRoomC2S(ForceExitType)");
            var enumType = exit.GetParameters()[0].ParameterType;
            var values = Enum.GetNames(enumType).Select(name => new RoomExitEnumSelection.Value(
                name, Convert.ToInt32(Enum.Parse(enumType, name)))).ToArray();
            var mode = RoomExitEnumSelection.Choose(values, Long(Member(room, "MasterId")) == accountId);
            exit.Invoke(roomLogic, new[] { Enum.Parse(enumType, mode.Name) });
            return true;
        }

        var names = KickNames;
        if (TryInvoke(controller, names, action == "kick" ? targetId : (long?)null)
            || TryInvoke(roomLogic, names, action == "kick" ? targetId : (long?)null)) return true;
        throw new MissingMethodException("Game room " + action + " method is not compatible with this version. "
            + "No custom/guessed RPC was sent.");
    }

    private static bool TryInvoke(object? target, string[] methodNames, long? playerId)
    {
        if (target == null) return false;
        var type = target.GetType();
        foreach (var name in methodNames)
        {
            foreach (var method in type.GetMethods(Instance))
            {
                if (!string.Equals(method.Name, name, StringComparison.Ordinal) || method.IsGenericMethod) continue;
                var parameters = method.GetParameters();
                if (playerId == null && parameters.Length == 0)
                {
                    method.Invoke(target, null);
                    return true;
                }
                if (playerId != null && parameters.Length == 1 && parameters[0].ParameterType == typeof(long))
                {
                    method.Invoke(target, new object[] { playerId.Value });
                    return true;
                }
            }
        }
        return false;
    }
}
