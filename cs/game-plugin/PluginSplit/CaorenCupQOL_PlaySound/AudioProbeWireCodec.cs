// 探针专用纯编码逻辑；不代表客户端已接受这些参数。
using System.Buffers.Binary;
using System.Text;

namespace CaorenCup.QOL.PlaySound;

public static class AudioProbeWireCodec
{
    // 协议研究来源记录在音频验证文档中；使用独立实现，不引入全局原生 Hook。
    public static uint ParameterHash(string name)
        => Hash(name, 0x31415926u);
    public static uint EventHash(string name) => Hash(name, 0x53524332u);
    private static uint Hash(string name, uint seed)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var bytes = Encoding.UTF8.GetBytes(name);
        unchecked
        {
            const uint mix = 0x5bd1e995;
            var hash = seed ^ (uint)bytes.Length;
            var position = 0;
            while (position + 4 <= bytes.Length)
            {
                var word = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, 4));
                word *= mix;
                word ^= word >> 24;
                word *= mix;
                hash = (hash * mix) ^ word;
                position += 4;
            }
            var remaining = bytes.Length - position;
            if (remaining >= 3) hash ^= (uint)bytes[position + 2] << 16;
            if (remaining >= 2) hash ^= (uint)bytes[position + 1] << 8;
            if (remaining >= 1)
            {
                hash ^= bytes[position];
                hash *= mix;
            }
            hash ^= hash >> 13;
            hash *= mix;
            return hash ^ (hash >> 15);
        }
    }

    public static byte[] Volume(int percent)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        return FloatParameter("public.volume", percent / 100f);
    }

    public static byte[] ProbeStartOffset(float seconds)
    {
        // 当前实验素材固定为 30 秒；到末尾不允许补播。
        if (!float.IsFinite(seconds) || seconds < 0 || seconds >= 30)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        return FloatParameter("public.delay", -seconds);
    }
    public static byte[] PlaybackOffset(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(seconds));
        return FloatParameter("public.delay", -(float)seconds);
    }
    public static byte[] VolumeScalar(float volume)
    {
        if (!float.IsFinite(volume) || volume is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(volume));
        return FloatParameter("public.volume", volume);
    }

    private static byte[] FloatParameter(string name, float value)
    {
        var packet = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, ParameterHash(name));
        packet[4] = 8; // float
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(5), 4);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(7), value);
        return packet;
    }

    public static IReadOnlyList<(uint Hash, byte Type, ushort Bytes)> Inspect(byte[] packet)
    {
        var fields = new List<(uint Hash, byte Type, ushort Bytes)>();
        var position = 0;
        while (position < packet.Length)
        {
            if (packet.Length - position < 7) throw new InvalidDataException("声音参数头被截断。");
            var hash = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(position, 4));
            var type = packet[position + 4];
            var length = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(position + 5, 2));
            if (length > packet.Length - position - 7) throw new InvalidDataException("声音参数内容被截断。");
            fields.Add((hash, type, length));
            position += 7 + length;
        }
        return fields;
    }
}
