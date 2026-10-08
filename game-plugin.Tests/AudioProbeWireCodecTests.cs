using System.Buffers.Binary;
using CaorenCup.QOL.PlaySound;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AudioProbeWireCodecTests
{
    [Theory]
    [InlineData("public.volume", 0xbd6054e9u)] // 作者公开的参数键
    [InlineData("public.position", 0x5a7cce4du)] // 实际 CS2 启动消息中的键
    public void Hash_matches_known_protocol_keys(string name, uint expected) =>
        Assert.Equal(expected, AudioProbeWireCodec.ParameterHash(name));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void Volume_packet_has_exact_key_type_length_and_little_endian_value(int percent)
    {
        var packet = AudioProbeWireCodec.Volume(percent);
        Assert.Equal(11, packet.Length);
        Assert.Equal(0xbd6054e9u, BinaryPrimitives.ReadUInt32LittleEndian(packet));
        Assert.Equal(8, packet[4]);
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(5)));
        Assert.Equal(percent / 100f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(7)));
        Assert.Single(AudioProbeWireCodec.Inspect(packet));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Invalid_percent_cannot_produce_a_packet(int percent) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioProbeWireCodec.Volume(percent));

    [Fact]
    public void Captured_native_position_packet_parses_without_changing_its_bytes()
    {
        const string captured = "4DCE7C5A0A0C00000000000000000000000000";
        var packet = Convert.FromHexString(captured);
        var field = Assert.Single(AudioProbeWireCodec.Inspect(packet));
        Assert.Equal(AudioProbeWireCodec.ParameterHash("public.position"), field.Hash);
        Assert.Equal(10, field.Type);
        Assert.Equal(12, field.Bytes);
        Assert.Equal(captured, Convert.ToHexString(packet));
        Assert.Equal(2, AudioProbeWireCodec.Inspect(packet.Concat(AudioProbeWireCodec.Volume(10)).ToArray()).Count);
    }

    [Fact]
    public void Truncated_packets_are_rejected()
    {
        var packet = AudioProbeWireCodec.Volume(50);
        for (var length = 1; length < packet.Length; length++)
            Assert.Throws<InvalidDataException>(() => AudioProbeWireCodec.Inspect(packet[..length]));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(13f)]
    [InlineData(29.5f)]
    public void Probe_offset_encodes_negative_delay_without_altering_other_fields(float seconds)
    {
        var packet = AudioProbeWireCodec.ProbeStartOffset(seconds);
        Assert.Equal(AudioProbeWireCodec.ParameterHash("public.delay"), BinaryPrimitives.ReadUInt32LittleEndian(packet));
        Assert.Equal(8, packet[4]);
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(5)));
        Assert.Equal(-seconds, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(7)));
        var position = Convert.FromHexString("4DCE7C5A0A0C00000000000000000000000000");
        Assert.Equal(2, AudioProbeWireCodec.Inspect(position.Concat(packet).ToArray()).Count);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(30f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Invalid_or_finished_probe_offsets_are_rejected(float seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioProbeWireCodec.ProbeStartOffset(seconds));
}
