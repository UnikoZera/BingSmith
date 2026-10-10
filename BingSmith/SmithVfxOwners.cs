using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
namespace BingSmith;

internal static class SmithVfxOwners
{
    private sealed record Owner(ulong PlayerId);
    private static readonly ConditionalWeakTable<NCardSmithVfx, Owner> Owners = new();
    private static readonly FieldInfo CardsField = AccessTools.Field(typeof(NCardSmithVfx), "_cards")!;
    [ThreadStatic]
    private static ulong? _remoteSmithOwnerId;

    internal static ulong? GetOwnerId(NCardSmithVfx smithVfx) =>
        Owners.TryGetValue(smithVfx, out var owner) ? owner.PlayerId : null;

    internal static ulong? SetRemoteSmithOwner(ulong? playerId)
    {
        var previous = _remoteSmithOwnerId;
        _remoteSmithOwnerId = playerId;
        return previous;
    }

    internal static void Associate(NCardSmithVfx? smithVfx, object[] arguments)
    {
        if (smithVfx == null)
            return;

        try
        {
            ulong? playerId;
            if (arguments.Length == 0)
            {
                // Remote Smith animations use Create() without a card argument, so their
                // owner must come from the rest-site callback that created the VFX.
                playerId = _remoteSmithOwnerId;
            }
            else
            {
                if (arguments.Length < 2 || arguments[1] is not true)
                    return;

                playerId = arguments[0] switch
                {
                    NCard card => card.Model?.Owner?.NetId,
                    IEnumerable<CardModel> => ((IEnumerable<CardModel>?)CardsField.GetValue(smithVfx))?.FirstOrDefault()?.Owner?.NetId,
                    _ => null
                };
            }

            if (playerId is ulong id)
            {
                Owners.Remove(smithVfx);
                Owners.Add(smithVfx, new Owner(id));
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"BingSmith could not identify the player for a Smith animation: {exception.Message}");
        }
    }
}

[HarmonyPatch]
internal static class SmithVfxOwnerPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        typeof(NCardSmithVfx).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "Create")
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 0 ||
                       (parameters.Length == 2 && parameters[1].ParameterType == typeof(bool) &&
                        (parameters[0].ParameterType == typeof(NCard) || parameters[0].ParameterType == typeof(IEnumerable<CardModel>)));
            });

    [HarmonyPostfix]
    private static void Postfix(object[] __args, NCardSmithVfx? __result) => SmithVfxOwners.Associate(__result, __args);
}

/// <summary>
/// A non-owning client creates a parameterless Smith VFX for another player's action.
/// Keep that player ID in scope while the game creates the VFX so its sound uses the
/// acting player's synchronized sound and pitch settings.
/// </summary>
[HarmonyPatch(typeof(NRestSiteRoom), "OnAfterPlayerSelectedRestSiteOption")]
internal static class SmithRemoteVfxOwnerScopePatch
{
    [HarmonyPrefix]
    private static void Prefix(object[] __args, out ulong? __state)
    {
        ulong? playerId = __args.Length >= 3 &&
                          __args[0] is MegaCrit.Sts2.Core.Entities.RestSite.SmithRestSiteOption &&
                          __args[1] is true &&
                          __args[2] is ulong actingPlayerId &&
                          BingSmithNetwork.LocalPlayerId is ulong localPlayerId &&
                          actingPlayerId != localPlayerId
            ? actingPlayerId
            : null;

        __state = SmithVfxOwners.SetRemoteSmithOwner(playerId);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, ulong? __state)
    {
        SmithVfxOwners.SetRemoteSmithOwner(__state);
        return __exception;
    }
}
