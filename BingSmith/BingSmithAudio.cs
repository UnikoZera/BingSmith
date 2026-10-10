using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
namespace BingSmith;

internal readonly record struct PlayerAudioOptions(bool Enabled, string Sound, double PitchSemitones);

internal static class BingSmithAudio
{
    internal const string DefaultSound = "bing-fix.mp3";
    private const string AlternateSound = "bing.mp3";
    internal const double DefaultVolume = 2.0;
    internal const double MaxVolume = 5.0;
    internal const double PitchRange = 24.0;

    private static readonly Dictionary<ulong, (string BusName, AudioEffectPitchShift Effect)> PitchBuses = new();
    private static AudioStreamMP3? _bingFixSound;
    private static AudioStreamMP3? _bingSound;
    private static string _selectedSound = DefaultSound;
    private static double _pitchSemitones;
    private static double _volume = DefaultVolume;

    internal static bool Enabled { get; set; } = true;

    internal static double Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0, MaxVolume);
    }

    internal static string SelectedSound
    {
        get => _selectedSound;
        set => _selectedSound = value == AlternateSound ? AlternateSound : DefaultSound;
    }

    internal static double PitchSemitones
    {
        get => _pitchSemitones;
        set => _pitchSemitones = Math.Clamp(value, -PitchRange, PitchRange);
    }

    internal static PlayerAudioOptions LocalOptions => new(Enabled, SelectedSound, PitchSemitones);

    internal static void Preload()
    {
        PreloadSound(DefaultSound);
        PreloadSound(AlternateSound);
    }

    internal static int PlayFromSmithVfx(
        NDebugAudioManager? manager,
        string streamName,
        float originalVolume,
        PitchVariance variance,
        NCardSmithVfx smithVfx)
    {
        if (manager == null)
            return 0;

        if (streamName != NCardSmithVfx.smithSfx || NRestSiteRoom.Instance?.IsInsideTree() != true)
            return manager.Play(streamName, originalVolume, variance);

        var playerId = SmithVfxOwners.GetOwnerId(smithVfx) ?? BingSmithNetwork.LocalPlayerId;
        var options = playerId is ulong id ? BingSmithNetwork.GetOptionsFor(id) : LocalOptions;
        if (!options.Enabled)
            return manager.Play(streamName, originalVolume, variance);

        try
        {
            Play(manager, options, originalVolume * (float)Volume, playerId);
            return 0;
        }
        catch (Exception exception)
        {
            GD.PushError($"BingSmith could not play replacement audio: {exception}");
            return manager.Play(streamName, originalVolume, variance);
        }
    }

    internal static void PlayTest()
    {
        try
        {
            if (NDebugAudioManager.Instance != null)
                Play(NDebugAudioManager.Instance, LocalOptions, (float)Volume, BingSmithNetwork.LocalPlayerId);
        }
        catch (Exception exception)
        {
            GD.PushError($"BingSmith could not play test audio: {exception}");
        }
    }

    private static void Play(NDebugAudioManager manager, PlayerAudioOptions options, float volume, ulong? playerId)
    {
        var player = new AudioStreamPlayer
        {
            Stream = GetSound(options.Sound),
            VolumeLinear = Math.Clamp(volume, 0f, (float)MaxVolume),
            Bus = GetPitchBus(playerId ?? 0, options.PitchSemitones)
        };
        player.Finished += player.QueueFree;
        manager.AddChild(player);
        player.Play();
    }

    private static string GetPitchBus(ulong playerId, double pitchSemitones)
    {
        var busName = $"BingSmithPitch_{playerId:X16}";
        try
        {
            if (!PitchBuses.TryGetValue(playerId, out var bus))
            {
                var busIndex = AudioServer.GetBusIndex(busName);
                if (busIndex < 0)
                {
                    busIndex = AudioServer.BusCount;
                    AudioServer.AddBus(busIndex);
                    AudioServer.SetBusName(busIndex, busName);
                    AudioServer.SetBusSend(busIndex, AudioServer.GetBusIndex("SFX") >= 0 ? "SFX" : "Master");
                }

                var effect = new AudioEffectPitchShift();
                AudioServer.AddBusEffect(busIndex, effect);
                bus = (busName, effect);
                PitchBuses[playerId] = bus;
            }

            bus.Effect.PitchScale = (float)Math.Pow(2.0, pitchSemitones / 12.0);
            return bus.BusName;
        }
        catch (Exception exception)
        {
            GD.PushError($"BingSmith could not configure pitch shifting for player {playerId}: {exception}");
            return AudioServer.GetBusIndex("SFX") >= 0 ? "SFX" : "Master";
        }
    }

    private static void PreloadSound(string soundName)
    {
        try
        {
            if (soundName == DefaultSound)
                _bingFixSound ??= LoadSound(soundName);
            else
                _bingSound ??= LoadSound(soundName);
        }
        catch (Exception exception)
        {
            GD.PushError($"BingSmith could not preload {soundName}: {exception}");
        }
    }

    private static AudioStreamMP3 GetSound(string soundName)
    {
        if (soundName == AlternateSound)
            return _bingSound ??= LoadSound(AlternateSound);
        return _bingFixSound ??= LoadSound(DefaultSound);
    }

    private static AudioStreamMP3 LoadSound(string soundName)
    {
        using var stream = typeof(BingSmithAudio).Assembly
            .GetManifestResourceStream($"BingSmith.{soundName}")
            ?? throw new InvalidOperationException($"Embedded audio resource {soundName} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return AudioStreamMP3.LoadFromBuffer(buffer.ToArray())
            ?? throw new InvalidOperationException($"Godot could not decode {soundName}.");
    }
}
