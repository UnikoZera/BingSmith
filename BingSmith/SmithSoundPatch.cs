using System.Diagnostics;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;

namespace BingSmith;

internal static class BingSmithAudio
{
    private static AudioStreamMP3? _sound;

    internal static bool Enabled { get; set; } = true;
    internal static double Volume { get; set; } = 1.0;

    internal static bool TryPlay(NDebugAudioManager manager, string streamName, float volume, ref int result)
    {
        if (streamName != "card_smith.mp3" || !Enabled || NRestSiteRoom.Instance?.IsInsideTree() != true || !CalledFromSmithVfx())
            return true;

        try
        {
            Play(manager, (float)(volume * Volume));
            result = 0;
            return false;
        }
        catch (Exception exception)
        {
            GD.PushError($"BingSmith could not play replacement audio: {exception}");
            return true;
        }
    }

    internal static void PlayTest()
    {
        try
        {
            if (NDebugAudioManager.Instance != null)
                Play(NDebugAudioManager.Instance, (float)Volume);
        }
        catch (Exception exception)
        {
            GD.PushError($"BingSmith could not play test audio: {exception}");
        }
    }

    private static void Play(NDebugAudioManager manager, float volume)
    {
        _sound ??= LoadSound();
        var player = new AudioStreamPlayer
        {
            Stream = _sound,
            VolumeLinear = Math.Clamp(volume, 0f, 2f),
            Bus = "SFX"
        };
        player.Finished += player.QueueFree;
        manager.AddChild(player);
        player.Play();
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

    private static AudioStreamMP3 LoadSound()
    {
        using var stream = typeof(BingSmithAudio).Assembly
            .GetManifestResourceStream("BingSmith.bing-bing-bing.mp3")
            ?? throw new InvalidOperationException("Embedded audio is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return AudioStreamMP3.LoadFromBuffer(buffer.ToArray())
            ?? throw new InvalidOperationException("Godot could not decode the MP3 file.");
    }
}

[HarmonyPatch(typeof(NDebugAudioManager), nameof(NDebugAudioManager.Play))]
internal static class ReplacementAudioPatch
{
    private static bool Prefix(NDebugAudioManager __instance, string streamName, float volume, ref int __result) =>
        BingSmithAudio.TryPlay(__instance, streamName, volume, ref __result);
}

[ModInitializer("Initialize")]
internal static class BingSmithSettings
{
    private const string ModId = "BingSmith";
    private static readonly string ConfigPath = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "SlayTheSpire2", "BingSmith", "settings.json");

    internal static void Initialize()
    {
        Load();
        new Harmony("UnikoZera.BingSmith").PatchAll(typeof(BingSmithSettings).Assembly);
        ModSettingsRegistry.Register(ModId, page =>
        {
            page.WithTitle(ModSettingsText.Literal("Bing Smith"))
                .WithDescription(Text("锻造音效", "Smith sound"))
                .WithModDisplayName(ModSettingsText.Literal("Bing Smith"))
                .AddSection("audio", section =>
                {
                    var enabled = ModSettingsBindings.Callback<bool>(ModId, "enabled", () => BingSmithAudio.Enabled,
                        value => BingSmithAudio.Enabled = value, Save);
                    var volume = ModSettingsBindings.Callback<double>(ModId, "volume", () => BingSmithAudio.Volume,
                        value => BingSmithAudio.Volume = Math.Clamp(value, 0.0, 2.0), Save);
                    section.WithTitle(Text("音效", "Audio"))
                        .AddToggle("enabled", Text("启用替换音效", "Enable replacement sound"), enabled)
                        .AddCustom("volume", Text("音量", "Volume"), _ => CreateVolumeControl(volume));
                });
        });
    }

    private static Control CreateVolumeControl(IModSettingsValueBinding<double> binding)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 2,
            Step = 0.05,
            Value = binding.Read(),
            CustomMinimumSize = new Vector2(180, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var valueLabel = new Label { Text = $"{slider.Value * 100:0}%", CustomMinimumSize = new Vector2(56, 0) };
        var testButton = new Button { Text = IsChinese() ? "试听" : "Test" };
        slider.ValueChanged += value =>
        {
            binding.Write(value);
            binding.Save();
            valueLabel.Text = $"{value * 100:0}%";
        };
        testButton.Pressed += BingSmithAudio.PlayTest;
        row.AddChild(slider);
        row.AddChild(valueLabel);
        row.AddChild(testButton);
        return row;
    }

    private static ModSettingsText Text(string chinese, string english) =>
        ModSettingsText.Dynamic(() => IsChinese() ? chinese : english);

    private static bool IsChinese() => TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return;
            var values = System.Text.Json.JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(ConfigPath));
            if (values != null)
            {
                BingSmithAudio.Enabled = values.Enabled;
                BingSmithAudio.Volume = Math.Clamp(values.Volume, 0.0, 2.0);
            }
        }
        catch
        {
            BingSmithAudio.Enabled = true;
            BingSmithAudio.Volume = 1.0;
        }
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(new SettingsFile
        {
            Enabled = BingSmithAudio.Enabled,
            Volume = BingSmithAudio.Volume
        }));
    }

    private sealed class SettingsFile
    {
        public bool Enabled { get; set; } = true;
        public double Volume { get; set; } = 1.0;
    }
}
