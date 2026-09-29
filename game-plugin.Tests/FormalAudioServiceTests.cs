using CaorenCup.Contracts;
using CaorenCup.QOL.PlaySound;
using CounterStrikeSharp.API.Core;
using System.Text.Json;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class FormalAudioServiceTests
{
    private sealed class Backend : IManagedAudioBackend
    {
        public bool Ready => true;
        public string? Played;
        public CaorenAudioChannel? Controlled;
        public bool HasAsset(string id) => id == "music.demo";
        public CaorenAudioResult Play(CaorenAudioEvent audio, IReadOnlyList<CCSPlayerController>? recipients) { Played=audio.Id;return new(true,"ok"); }
        public CaorenAudioResult Control(CaorenAudioChannel channel, CaorenAudioControl operation) { Controlled=channel;return new(true,"ok"); }
        public IReadOnlyList<CaorenAudioChannelStatus> GetChannels() => [];
        public CaorenAudioResult PlayNativeEffect(CaorenAudioEvent audio,IReadOnlyList<CCSPlayerController>? recipients,CBaseEntity? source) { Played=audio.Id;return new(true,"ok"); }
    }
    [Fact]
    public void Existing_protocol_events_and_controls_route_to_managed_backend()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"formal-audio-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"events.json");
        File.WriteAllText(path,JsonSerializer.Serialize(new[]{new CaorenAudioEvent("music.demo","音乐","caorencup.audio.music.demo",true,1,CaorenAudioChannel.Music)}));
        var backend=new Backend();var service=new CaorenAudioService(path,backend);
        Assert.True(service.Play("music.demo").Success);Assert.Equal("music.demo",backend.Played);
        Assert.True(service.Control(CaorenAudioChannel.Music,CaorenAudioControl.LoopOff).Success);
        Assert.Equal(CaorenAudioChannel.Music,backend.Controlled);Assert.True(service.Capabilities.Seek);
        Assert.True(service.Play("menu.click").Success);Assert.Equal("menu.click",backend.Played);
    }
    [Fact]
    public void Missing_native_metadata_keeps_backend_unavailable()
    {
        Assert.Empty(new AudioAssetLibrary(null).Assets);
        Assert.False(new CaorenAudioService().Capabilities.TargetedStop);
        Assert.False(new CaorenAudioService().Control(CaorenAudioChannel.Music,CaorenAudioControl.Resume).Success);
    }
    [Fact]
    public void Event_hash_matches_the_captured_native_probe_message() =>
        Assert.Equal(4130914104u,AudioProbeWireCodec.EventHash("caorencup.probe.lufs12"));
    [Fact]
    public void Arbitrary_duration_offsets_use_negative_delay_and_reject_nonfinite()
    {
        Assert.Equal(-123.5f,System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(AudioProbeWireCodec.PlaybackOffset(123.5).AsSpan(7)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AudioProbeWireCodec.PlaybackOffset(double.NaN));
    }
}
