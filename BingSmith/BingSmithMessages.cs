using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
namespace BingSmith;

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

