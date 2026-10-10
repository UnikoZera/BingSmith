using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;
namespace BingSmith;

[ModInitializer("Initialize")]
internal static class BingSmithSettings
{
    private const string ModId = "BingSmith";
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
                value => BingSmithAudio.Enabled = value, SaveAndSync);
            var sound = ModSettingsBindings.Callback<string>(ModId, "sound", () => BingSmithAudio.SelectedSound,
                value => BingSmithAudio.SelectedSound = value, SaveAndSync);
            var volume = ModSettingsBindings.Callback<double>(ModId, "volume", () => BingSmithAudio.Volume,
                value => BingSmithAudio.Volume = value, Save);
            var pitch = ModSettingsBindings.Callback<double>(ModId, "pitch", () => BingSmithAudio.PitchSemitones,
                value => BingSmithAudio.PitchSemitones = value, SaveAndSync);

            page.WithTitle(ModSettingsText.Literal("Bing Smith"))
                .WithDescription(Text("自定义锻造音效、个人音量和音调；联机时同步每位玩家的音效选择与音调。", "Choose a Smith sound and personal volume; sound choice and pitch are shared in multiplayer."))
                .WithModDisplayName(ModSettingsText.Literal("Bing Smith"))
                .AddSection("audio", section =>
                {
                    section.WithTitle(Text("音效设置", "Sound settings"))
                        .AddCustom("controls", Text("播放选项", "Playback options"),
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
        var layout = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

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

        var soundGroup = new ButtonGroup { AllowUnpress = false };
        var bingFixButton = new Button
        {
            Text = IsChinese() ? "Bing Fix（默认音效）" : "Bing Fix (default sound)",
            ToggleMode = true,
            ButtonGroup = soundGroup,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var bingButton = new Button
        {
            Text = "Bing",
            ToggleMode = true,
            ButtonGroup = soundGroup,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var useBing = sound.Read() == "bing.mp3";
        bingFixButton.ButtonPressed = !useBing;
        bingButton.ButtonPressed = useBing;
        bingFixButton.Pressed += () =>
        {
            sound.Write(DefaultSound);
            sound.Save();
        };
        bingButton.Pressed += () =>
        {
            sound.Write("bing.mp3");
            sound.Save();
        };
        layout.AddChild(CreateLabeledRow(IsChinese() ? "音效" : "Sound", bingFixButton, bingButton));

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
        layout.AddChild(CreateLabeledRow(IsChinese() ? "本地音量（0–500%）" : "Local volume (0–500%)", volumeSlider, volumeInput));

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
        layout.AddChild(CreateLabeledRow(IsChinese() ? "音调（−24 至 +24 半音）" : "Pitch (−24 to +24 semitones)", pitchSlider, pitchInput));

        layout.AddChild(new HSeparator());
        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End
        };
        var previewButton = new Button { Text = IsChinese() ? "试听当前音效" : "Preview current sound" };
        previewButton.Pressed += BingSmithAudio.PlayTest;
        var defaultButton = new Button { Text = IsChinese() ? "恢复默认" : "Restore defaults" };
        defaultButton.Pressed += () => RestoreDefaults(
            enabled, sound, volume, pitch, enabledToggle, bingFixButton, bingButton,
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
        Button bingFixButton,
        Button bingButton,
        HSlider volumeSlider,
        SpinBox volumeInput,
        HSlider pitchSlider,
        SpinBox pitchInput)
    {
        enabled.Write(true);
        sound.Write(DefaultSound);
        volume.Write(BingSmithAudio.DefaultVolume);
        pitch.Write(0.0);
        enabled.Save();

        enabledToggle.SetPressedNoSignal(true);
        bingFixButton.SetPressedNoSignal(true);
        bingButton.SetPressedNoSignal(false);
        volumeSlider.SetValueNoSignal(BingSmithAudio.DefaultVolume * 100);
        volumeInput.SetValueNoSignal(BingSmithAudio.DefaultVolume * 100);
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
            BingSmithAudio.Volume = BingSmithAudio.DefaultVolume;
            BingSmithAudio.SelectedSound = DefaultSound;
            BingSmithAudio.PitchSemitones = 0.0;
        }
    }

    private static void SaveAndSync()
    {
        Save();
        BingSmithNetwork.PublishLocalSettings();
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
        public double Volume { get; set; } = BingSmithAudio.DefaultVolume;
        public string Sound { get; set; } = DefaultSound;
        public double PitchSemitones { get; set; }
    }
}
