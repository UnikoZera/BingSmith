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
    private const string DefaultSound = "bing-fix.mp3";
    private const string AlternateSound = "bing.mp3";
    private const string PitchBusName = "BingSmithPitchShift";

    private static AudioStreamMP3? _bingFixSound;
    private static AudioStreamMP3? _bingSound;
    private static AudioEffectPitchShift? _pitchEffect;
    private static int _pitchBusIndex = -1;
    private static string _selectedSound = DefaultSound;
    private static double _pitchSemitones;

    internal static bool Enabled { get; set; } = true;
    internal static double Volume { get; set; } = 1.0;

    internal static string SelectedSound
    {
        get => _selectedSound;
        set => _selectedSound = value == AlternateSound ? AlternateSound : DefaultSound;
    }

    internal static double PitchSemitones
    {
        get => _pitchSemitones;
        set
        {
            _pitchSemitones = Math.Clamp(value, -12.0, 12.0);
            if (_pitchEffect != null)
                _pitchEffect.PitchScale = (float)Math.Pow(2.0, _pitchSemitones / 12.0);
        }
    }

    internal static void Preload()
    {
        PreloadSound(DefaultSound);
        PreloadSound(AlternateSound);
        ConfigurePitchBus();
    }

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
        var player = new AudioStreamPlayer
        {
            Stream = GetSelectedSound(),
            VolumeLinear = Math.Clamp(volume, 0f, 2f),
            Bus = _pitchBusIndex >= 0 ? PitchBusName : "SFX"
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

    private static AudioStreamMP3 GetSelectedSound()
    {
        if (SelectedSound == AlternateSound)
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

    private static void ConfigurePitchBus()
    {
        try
        {
            _pitchBusIndex = AudioServer.GetBusIndex(PitchBusName);
            if (_pitchBusIndex < 0)
            {
                _pitchBusIndex = AudioServer.BusCount;
                AudioServer.AddBus(_pitchBusIndex);
                AudioServer.SetBusName(_pitchBusIndex, PitchBusName);
                AudioServer.SetBusSend(_pitchBusIndex, AudioServer.GetBusIndex("SFX") >= 0 ? "SFX" : "Master");
            }

            _pitchEffect = new AudioEffectPitchShift { PitchScale = 1f };
            AudioServer.AddBusEffect(_pitchBusIndex, _pitchEffect);
            PitchSemitones = _pitchSemitones;
        }
        catch (Exception exception)
        {
            _pitchBusIndex = -1;
            _pitchEffect = null;
            GD.PushError($"BingSmith could not configure pitch shifting: {exception}");
        }
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
        BingSmithAudio.Preload();
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
                    var sound = ModSettingsBindings.Callback<string>(ModId, "sound", () => BingSmithAudio.SelectedSound,
                        value => BingSmithAudio.SelectedSound = value, Save);
                    var volume = ModSettingsBindings.Callback<double>(ModId, "volume", () => BingSmithAudio.Volume,
                        value => BingSmithAudio.Volume = Math.Clamp(value, 0.0, 2.0), Save);
                    var pitch = ModSettingsBindings.Callback<double>(ModId, "pitch", () => BingSmithAudio.PitchSemitones,
                        value => BingSmithAudio.PitchSemitones = value, Save);

                    section.WithTitle(Text("音效", "Audio"))
                        .AddToggle("enabled", Text("启用替换音效", "Enable replacement sound"), enabled)
                        .AddCustom("sound", Text("音效文件", "Sound"), _ => CreateSoundControl(sound))
                        .AddCustom("volume", Text("音量", "Volume"), _ => CreateVolumeControl(volume))
                        .AddCustom("pitch", Text("音调", "Pitch"), _ => CreatePitchControl(pitch));
                });
        });
    }

    private static Control CreateSoundControl(IModSettingsValueBinding<string> binding)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var selector = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        selector.AddItem(IsChinese() ? "Bing Fix（默认）" : "Bing Fix (default)");
        selector.AddItem("Bing");
        selector.Selected = binding.Read() == "bing.mp3" ? 1 : 0;
        selector.ItemSelected += index =>
        {
            binding.Write(index == 1 ? "bing.mp3" : "bing-fix.mp3");
            binding.Save();
        };

        var testButton = new Button { Text = IsChinese() ? "试听" : "Test" };
        testButton.Pressed += BingSmithAudio.PlayTest;
        row.AddChild(selector);
        row.AddChild(testButton);
        return row;
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
        slider.ValueChanged += value =>
        {
            binding.Write(value);
            binding.Save();
            valueLabel.Text = $"{value * 100:0}%";
        };
        row.AddChild(slider);
        row.AddChild(valueLabel);
        return row;
    }

    private static Control CreatePitchControl(IModSettingsValueBinding<double> binding)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var slider = new HSlider
        {
            MinValue = -12,
            MaxValue = 12,
            Step = 1,
            Value = binding.Read(),
            CustomMinimumSize = new Vector2(180, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var valueLabel = new Label { Text = FormatPitch(slider.Value), CustomMinimumSize = new Vector2(64, 0) };
        slider.ValueChanged += value =>
        {
            binding.Write(value);
            binding.Save();
            valueLabel.Text = FormatPitch(value);
        };
        row.AddChild(slider);
        row.AddChild(valueLabel);
        return row;
    }

    private static string FormatPitch(double semitones) => IsChinese()
        ? $"{semitones:+0;-0;0} 半音"
        : $"{semitones:+0;-0;0} st";

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
            if (values == null)
                return;

            BingSmithAudio.Enabled = values.Enabled;
            BingSmithAudio.Volume = Math.Clamp(values.Volume, 0.0, 2.0);
            BingSmithAudio.SelectedSound = values.Sound;
            BingSmithAudio.PitchSemitones = values.PitchSemitones;
        }
        catch
        {
            BingSmithAudio.Enabled = true;
            BingSmithAudio.Volume = 1.0;
            BingSmithAudio.SelectedSound = "bing-fix.mp3";
            BingSmithAudio.PitchSemitones = 0.0;
        }
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(new SettingsFile
        {
            Enabled = BingSmithAudio.Enabled,
            Volume = BingSmithAudio.Volume,
            Sound = BingSmithAudio.SelectedSound,
            PitchSemitones = BingSmithAudio.PitchSemitones
        }));
    }

    private sealed class SettingsFile
    {
        public bool Enabled { get; set; } = true;
        public double Volume { get; set; } = 1.0;
        public string Sound { get; set; } = "bing-fix.mp3";
        public double PitchSemitones { get; set; }
    }
}
