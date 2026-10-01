using System.Diagnostics;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace BingSmith;

internal static class BingSmithAudio
{
    private static AudioStreamOggVorbis? _sound;

    internal static bool TryPlay(NDebugAudioManager manager, string streamName, float volume, ref int result)
    {
        if (streamName != "card_smith.mp3" || NRestSiteRoom.Instance?.IsInsideTree() != true || !CalledFromSmithVfx())
            return true;

        try
        {
            _sound ??= LoadSound();
            var player = new AudioStreamPlayer
            {
                Stream = _sound,
                VolumeLinear = volume,
                Bus = "SFX"
            };
            player.Finished += player.QueueFree;
            manager.AddChild(player);
            player.Play();
            result = 0;
            return false;
        }
        catch (Exception exception)
        {
            GD.PushError($"BingSmith could not play replacement audio: {exception}");
            return true;
        }
    }

    private static bool CalledFromSmithVfx()
    {
        var smithType = typeof(NCardSmithVfx);
        var frames = new StackTrace().GetFrames();
        if (frames == null)
            return false;

        foreach (var frame in frames)
        {
            var type = frame.GetMethod()?.DeclaringType;
            while (type != null)
            {
                if (type == smithType)
                    return true;
                type = type.DeclaringType;
            }
        }
        return false;
    }

    private static AudioStreamOggVorbis LoadSound()
    {
        using var stream = typeof(BingSmithAudio).Assembly
            .GetManifestResourceStream("BingSmith.bing-bing-bing.ogg")
            ?? throw new InvalidOperationException("Embedded audio is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return AudioStreamOggVorbis.LoadFromBuffer(buffer.ToArray())
            ?? throw new InvalidOperationException("Godot could not decode the OGG file.");
    }
}

[HarmonyPatch(typeof(NDebugAudioManager), nameof(NDebugAudioManager.Play))]
internal static class ReplacementAudioPatch
{
    private static bool Prefix(NDebugAudioManager __instance, string streamName, float volume, ref int __result) =>
        BingSmithAudio.TryPlay(__instance, streamName, volume, ref __result);
}
