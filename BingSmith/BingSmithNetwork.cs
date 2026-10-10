using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
namespace BingSmith;

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

[HarmonyPatch(typeof(RunManager), "InitializeShared")]
internal static class SmithNetworkInitializationPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunManager __instance) => BingSmithNetwork.Attach(__instance.NetService);
}
