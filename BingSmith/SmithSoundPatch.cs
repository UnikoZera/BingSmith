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
    internal const double MaxVolume = 5.0;
    internal const double PitchRange = 24.0;

    private static AudioStreamMP3? _bingFixSound;
    private static AudioStreamMP3? _bingSound;
    private static AudioEffectPitchShift? _pitchEffect;
    private static int _pitchBusIndex = -1;
    private static string _selectedSound = DefaultSound;
    private static double _pitchSemitones;
    private static double _volume = 1.0;

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
        set
        {
            _pitchSemitones = Math.Clamp(value, -PitchRange, PitchRange);
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
            Play(manager, volume * (float)Volume);
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
            VolumeLinear = Math.Clamp(volume, 0f, (float)MaxVolume),
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
    private const double DefaultVolume = 1.0;
    private const string DefaultSound = "bing-fix.mp3";
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
            var enabled = ModSettingsBindings.Callback<bool>(ModId, "enabled", () => BingSmithAudio.Enabled,
                value => BingSmithAudio.Enabled = value, Save);
            var sound = ModSettingsBindings.Callback<string>(ModId, "sound", () => BingSmithAudio.SelectedSound,
                value => BingSmithAudio.SelectedSound = value, Save);
            var volume = ModSettingsBindings.Callback<double>(ModId, "volume", () => BingSmithAudio.Volume,
                value => BingSmithAudio.Volume = value, Save);
            var pitch = ModSettingsBindings.Callback<double>(ModId, "pitch", () => BingSmithAudio.PitchSemitones,
                value => BingSmithAudio.PitchSemitones = value, Save);

            page.WithTitle(ModSettingsText.Literal("Bing Smith"))
                .WithDescription(Text("设置锻造音效、音量和音调。", "Configure the Smith sound, volume, and pitch."))
                .WithModDisplayName(ModSettingsText.Literal("Bing Smith"))
                .AddSection("audio", section =>
                {
                    section.WithTitle(Text("音效控制", "Sound controls"))
                        .AddCustom("controls", Text("播放设置", "Playback settings"),
                            _ => CreateSettingsControl(enabled, sound, volume, pitch));
                });
        });
    }

    private static Control CreateSettingsControl(
        IModSettingsValueBinding<bool> enabled,
        IModSettingsValueBinding<string> sound,
        IModSettingsValueBinding<double> volume,
        IModSettingsValueBinding<double> pitch)
    {
        var layout = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        var enabledToggle = new CheckButton
        {
            Text = IsChinese() ? "启用替换音效" : "Enable replacement sound",
            ButtonPressed = enabled.Read()
        };
        enabledToggle.Toggled += value =>
        {
            enabled.Write(value);
            enabled.Save();
        };
        layout.AddChild(enabledToggle);
        layout.AddChild(new HSeparator());

        var soundSelector = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        soundSelector.AddItem(IsChinese() ? "Bing Fix（默认音效）" : "Bing Fix (default sound)");
        soundSelector.AddItem("Bing");
        soundSelector.Selected = sound.Read() == "bing.mp3" ? 1 : 0;
        soundSelector.ItemSelected += index =>
        {
            sound.Write(index == 1 ? "bing.mp3" : DefaultSound);
            sound.Save();
        };
        layout.AddChild(CreateLabeledRow(IsChinese() ? "音效" : "Sound", soundSelector));

        var volumeSlider = new HSlider
        {
            MinValue = 0,
            MaxValue = BingSmithAudio.MaxVolume * 100,
            Step = 1,
            Value = volume.Read() * 100,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var volumeInput = new SpinBox
        {
            MinValue = 0,
            MaxValue = BingSmithAudio.MaxVolume * 100,
            Step = 1,
            Value = volume.Read() * 100,
            Suffix = "%",
            CustomMinimumSize = new Vector2(100, 0)
        };
        LinkVolumeControls(volume, volumeSlider, volumeInput);
        layout.AddChild(CreateLabeledRow(IsChinese() ? "音量（0–500%）" : "Volume (0–500%)", volumeSlider, volumeInput));

        var pitchSlider = new HSlider
        {
            MinValue = -BingSmithAudio.PitchRange,
            MaxValue = BingSmithAudio.PitchRange,
            Step = 0.1,
            Value = pitch.Read(),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var pitchInput = new SpinBox
        {
            MinValue = -BingSmithAudio.PitchRange,
            MaxValue = BingSmithAudio.PitchRange,
            Step = 0.1,
            Value = pitch.Read(),
            CustomArrowStep = 1,
            Suffix = IsChinese() ? " 半音" : " st",
            CustomMinimumSize = new Vector2(100, 0)
        };
        LinkPitchControls(pitch, pitchSlider, pitchInput);
        layout.AddChild(CreateLabeledRow(IsChinese() ? "音调（-24 至 +24 半音）" : "Pitch (-24 to +24 semitones)", pitchSlider, pitchInput));

        layout.AddChild(new HSeparator());
        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End
        };
        var previewButton = new Button { Text = IsChinese() ? "试听当前设置" : "Preview current settings" };
        previewButton.Pressed += BingSmithAudio.PlayTest;
        var defaultButton = new Button { Text = IsChinese() ? "恢复默认" : "Restore defaults" };
        defaultButton.Pressed += () => RestoreDefaults(
            enabled, sound, volume, pitch, enabledToggle, soundSelector,
            volumeSlider, volumeInput, pitchSlider, pitchInput);
        buttons.AddChild(previewButton);
        buttons.AddChild(defaultButton);
        layout.AddChild(buttons);

        return layout;
    }

    private static Control CreateLabeledRow(string labelText, params Control[] controls)
    {
        var row = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(new Label { Text = labelText });
        var controlRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var control in controls)
            controlRow.AddChild(control);
        row.AddChild(controlRow);
        return row;
    }

    private static void LinkVolumeControls(
        IModSettingsValueBinding<double> binding,
        HSlider slider,
        SpinBox input)
    {
        slider.ValueChanged += value =>
        {
            binding.Write(value / 100.0);
            binding.Save();
            input.SetValueNoSignal(value);
        };
        input.ValueChanged += value =>
        {
            binding.Write(value / 100.0);
            binding.Save();
            slider.SetValueNoSignal(value);
        };
    }

    private static void LinkPitchControls(
        IModSettingsValueBinding<double> binding,
        HSlider slider,
        SpinBox input)
    {
        slider.ValueChanged += value =>
        {
            binding.Write(value);
            binding.Save();
            input.SetValueNoSignal(value);
        };
        input.ValueChanged += value =>
        {
            binding.Write(value);
            binding.Save();
            slider.SetValueNoSignal(value);
        };
    }

    private static void RestoreDefaults(
        IModSettingsValueBinding<bool> enabled,
        IModSettingsValueBinding<string> sound,
        IModSettingsValueBinding<double> volume,
        IModSettingsValueBinding<double> pitch,
        CheckButton enabledToggle,
        OptionButton soundSelector,
        HSlider volumeSlider,
        SpinBox volumeInput,
        HSlider pitchSlider,
        SpinBox pitchInput)
    {
        enabled.Write(true);
        sound.Write(DefaultSound);
        volume.Write(DefaultVolume);
        pitch.Write(0.0);
        enabled.Save();

        enabledToggle.SetPressedNoSignal(true);
        soundSelector.Selected = 0;
        volumeSlider.SetValueNoSignal(DefaultVolume * 100);
        volumeInput.SetValueNoSignal(DefaultVolume * 100);
        pitchSlider.SetValueNoSignal(0);
        pitchInput.SetValueNoSignal(0);
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
            if (values == null)
                return;

            BingSmithAudio.Enabled = values.Enabled;
            BingSmithAudio.Volume = values.Volume;
            BingSmithAudio.SelectedSound = values.Sound;
            BingSmithAudio.PitchSemitones = values.PitchSemitones;
        }
        catch
        {
            BingSmithAudio.Enabled = true;
            BingSmithAudio.Volume = DefaultVolume;
            BingSmithAudio.SelectedSound = DefaultSound;
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
        public double Volume { get; set; } = DefaultVolume;
        public string Sound { get; set; } = DefaultSound;
        public double PitchSemitones { get; set; }
    }
}
