using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;

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

/// <summary>
/// Carries a player's replacement choice and pitch to the other clients. Listener volume is deliberately local.
/// </summary>
public sealed class BingSmithAudioSettingsMessage : INetMessage
{
    public ulong PlayerId { get; set; }
    public bool Enabled { get; set; }
    public bool UseAlternateSound { get; set; }
    public double PitchSemitones { get; set; }

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(PlayerId);
        writer.WriteBool(Enabled);
        writer.WriteBool(UseAlternateSound);
        writer.WriteDouble(PitchSemitones);
    }

    public void Deserialize(PacketReader reader)
    {
        PlayerId = reader.ReadULong();
        Enabled = reader.ReadBool();
        UseAlternateSound = reader.ReadBool();
        PitchSemitones = reader.ReadDouble();
    }
}

/// <summary>Requests the current player settings from the host, including settings announced before this client joined.</summary>
public sealed class BingSmithAudioSettingsRequestMessage : INetMessage
{
    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer) { }
    public void Deserialize(PacketReader reader) { }
}

internal static class BingSmithNetwork
{
    private static readonly Dictionary<ulong, PlayerAudioOptions> OtherPlayers = new();
    private static INetGameService? _service;
    private static bool _announced;
    private static bool _requestedSnapshot;

    internal static ulong? LocalPlayerId => _service?.NetId;

    internal static void Attach(INetGameService service)
    {
        if (ReferenceEquals(service, _service))
        {
            TryAnnounce();
            return;
        }

        if (_service != null)
        {
            _service.UnregisterMessageHandler<BingSmithAudioSettingsMessage>(OnSettingsReceived);
            _service.UnregisterMessageHandler<BingSmithAudioSettingsRequestMessage>(OnSettingsRequested);
            _service.Disconnected -= OnDisconnected;
        }

        _service = service;
        _announced = false;
        _requestedSnapshot = false;
        OtherPlayers.Clear();
        service.RegisterMessageHandler<BingSmithAudioSettingsMessage>(OnSettingsReceived);
        service.RegisterMessageHandler<BingSmithAudioSettingsRequestMessage>(OnSettingsRequested);
        service.Disconnected += OnDisconnected;
        TryAnnounce();
    }

    internal static PlayerAudioOptions GetOptionsFor(ulong playerId)
    {
        if (_service?.NetId == playerId)
            return BingSmithAudio.LocalOptions;
        return OtherPlayers.TryGetValue(playerId, out var options)
            ? options
            : new PlayerAudioOptions(true, BingSmithAudio.DefaultSound, 0.0);
    }

    internal static void PublishLocalSettings()
    {
        TryAnnounce(forceUpdate: true);
    }

    private static void TryAnnounce(bool forceUpdate = false)
    {
        if (_service == null || !_service.IsConnected || _service.Type is not (NetGameType.Host or NetGameType.Client))
            return;

        if (forceUpdate || !_announced)
        {
            var options = BingSmithAudio.LocalOptions;
            OtherPlayers[_service.NetId] = options;
            _service.SendMessage(ToMessage(_service.NetId, options));
            _announced = true;
        }

        if (_service.Type == NetGameType.Client && !_requestedSnapshot)
        {
            _service.SendMessage(new BingSmithAudioSettingsRequestMessage());
            _requestedSnapshot = true;
        }
    }

    private static BingSmithAudioSettingsMessage ToMessage(ulong playerId, PlayerAudioOptions options) => new()
    {
        PlayerId = playerId,
        Enabled = options.Enabled,
        UseAlternateSound = options.Sound == "bing.mp3",
        PitchSemitones = options.PitchSemitones
    };

    private static void OnSettingsReceived(BingSmithAudioSettingsMessage message, ulong senderId)
    {
        if (_service == null)
            return;

        if (_service.Type == NetGameType.Host && senderId != message.PlayerId)
            return;

        OtherPlayers[message.PlayerId] = new PlayerAudioOptions(
            message.Enabled,
            message.UseAlternateSound ? "bing.mp3" : BingSmithAudio.DefaultSound,
            Math.Clamp(message.PitchSemitones, -BingSmithAudio.PitchRange, BingSmithAudio.PitchRange));
    }

    private static void OnSettingsRequested(BingSmithAudioSettingsRequestMessage _, ulong senderId)
    {
        if (_service?.Type != NetGameType.Host)
            return;

        foreach (var (playerId, options) in OtherPlayers.ToArray())
            _service.SendMessage(ToMessage(playerId, options), senderId);
    }

    private static void OnDisconnected(MegaCrit.Sts2.Core.Entities.Multiplayer.NetErrorInfo _)
    {
        _service = null;
        OtherPlayers.Clear();
        _announced = false;
        _requestedSnapshot = false;
    }
}

internal static class SmithVfxOwners
{
    private sealed record Owner(ulong PlayerId);
    private static readonly ConditionalWeakTable<NCardSmithVfx, Owner> Owners = new();
    private static readonly FieldInfo CardsField = AccessTools.Field(typeof(NCardSmithVfx), "_cards")!;

    internal static ulong? GetOwnerId(NCardSmithVfx smithVfx) =>
        Owners.TryGetValue(smithVfx, out var owner) ? owner.PlayerId : null;

    internal static void Associate(NCardSmithVfx? smithVfx, object[] arguments)
    {
        if (smithVfx == null || arguments.Length < 2 || arguments[1] is not true)
            return;

        try
        {
            ulong? playerId = arguments[0] switch
            {
                NCard card => card.Model?.Owner?.NetId,
                IEnumerable<CardModel> => ((IEnumerable<CardModel>?)CardsField.GetValue(smithVfx))?.FirstOrDefault()?.Owner?.NetId,
                _ => null
            };

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
internal static class SmithAnimationSoundPatch
{
    private static readonly MethodInfo AudioPlay = AccessTools.Method(
        typeof(NDebugAudioManager), nameof(NDebugAudioManager.Play),
        new[] { typeof(string), typeof(float), typeof(PitchVariance) })!;
    private static readonly MethodInfo ReplacementPlay = AccessTools.Method(
        typeof(BingSmithAudio), nameof(BingSmithAudio.PlayFromSmithVfx))!;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var animation in typeof(NCardSmithVfx).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                     .Where(method => method.Name == "PlayAnimation"))
        {
            var stateMachine = animation.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            var moveNext = stateMachine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (moveNext != null)
                yield return moveNext;
        }
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var vfxField = __originalMethod.DeclaringType?.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(field => field.FieldType == typeof(NCardSmithVfx));
        if (vfxField == null)
        {
            GD.PushError($"BingSmith could not find the owning VFX field in {__originalMethod.DeclaringType}.");
            return instructions;
        }

        var output = new List<CodeInstruction>();
        var replacementCount = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(AudioPlay))
            {
                var loadVfx = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0);
                loadVfx.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                output.Add(loadVfx);
                output.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Ldfld, vfxField));
                instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                instruction.operand = ReplacementPlay;
                replacementCount++;
            }

            output.Add(instruction);
        }

        if (replacementCount != 1)
            GD.PushError($"BingSmith expected one Smith sound call in {__originalMethod}, found {replacementCount}.");
        return output;
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
                return parameters.Length == 2 && parameters[1].ParameterType == typeof(bool) &&
                       (parameters[0].ParameterType == typeof(NCard) || parameters[0].ParameterType == typeof(IEnumerable<CardModel>));
            });

    [HarmonyPostfix]
    private static void Postfix(object[] __args, NCardSmithVfx? __result) => SmithVfxOwners.Associate(__result, __args);
}

[HarmonyPatch(typeof(RunManager), "InitializeShared")]
internal static class SmithNetworkInitializationPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunManager __instance) => BingSmithNetwork.Attach(__instance.NetService);
}

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
        volume.Write(BingSmithAudio.DefaultVolume);
        pitch.Write(0.0);
        enabled.Save();

        enabledToggle.SetPressedNoSignal(true);
        soundSelector.Selected = 0;
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
