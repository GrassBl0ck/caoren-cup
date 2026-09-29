"""为内部 PCM WAV 副本添加整段循环 cue；不修改用户 OGG 或原 WAV。"""
import argparse
from pathlib import Path
import struct


def chunk(tag, data):
    return tag + struct.pack('<I', len(data)) + data + (b'\0' if len(data) % 2 else b'')


def mark_loop(source, output):
    source, output = Path(source), Path(output)
    if output.exists() or source.resolve() == output.resolve():
        raise ValueError('输出必须是一个尚不存在的新 WAV，禁止覆盖。')
    data = source.read_bytes()
    if len(data) < 12 or data[:4] != b'RIFF' or data[8:12] != b'WAVE':
        raise ValueError('需要 RIFF/WAVE 输入。')
    if struct.unpack_from('<I', data, 4)[0] != len(data) - 8:
        raise ValueError('RIFF 长度不匹配。')
    pos, fmt, audio_bytes = 12, None, None
    while pos < len(data):
        if pos + 8 > len(data):
            raise ValueError('WAV chunk 头被截断。')
        tag, length = struct.unpack_from('<4sI', data, pos)
        begin, end = pos + 8, pos + 8 + length
        if end + (length % 2) > len(data):
            raise ValueError('WAV chunk 数据被截断。')
        if tag == b'fmt ':
            if fmt is not None or length < 16:
                raise ValueError('无效或重复的 fmt chunk。')
            fmt = struct.unpack_from('<HHIIHH', data, begin)
        if tag == b'data':
            if audio_bytes is not None:
                raise ValueError('重复的 data chunk。')
            audio_bytes = length
        if tag in (b'cue ', b'smpl'):
            raise ValueError('输入已有循环或 cue 标记，需要人工检查。')
        pos = end + (length % 2)
    if fmt is None or audio_bytes is None:
        raise ValueError('缺少 fmt/data chunk。')
    encoding, channels, rate, byte_rate, align, bits = fmt
    if encoding != 1 or bits != 16 or channels not in (1, 2) or align != channels * 2 or byte_rate != rate * align or rate <= 0:
        raise ValueError('只接受内部转换得到的 16-bit PCM 单/双声道。')
    if audio_bytes == 0 or audio_bytes % align:
        raise ValueError('音频样本数无效。')
    samples = audio_bytes // align
    # 实际 resourcecompiler 实验确认：两个 cue 的 [0, samples] 边界被识别；
    # 无 label、start/end、Marker 1/2 均可。本工具使用便于检查的 start/end。
    cues = struct.pack('<I', 2) + b''.join(
        struct.pack('<II4sIII', index + 1, sample, b'data', 0, 0, sample)
        for index, sample in enumerate((0, samples)))
    labels = b'adtl' + b''.join(chunk(b'labl', struct.pack('<I', index + 1) + label + b'\0')
        for index, label in enumerate((b'start', b'end')))
    marked = data + chunk(b'cue ', cues) + chunk(b'LIST', labels)
    marked = marked[:4] + struct.pack('<I', len(marked) - 8) + marked[8:]
    # 'xb' prevents concurrent overwrites as well as repeated builds.
    with output.open('xb') as stream:
        stream.write(marked)
    return {'SampleRate': rate, 'Samples': samples, 'LoopStart': 0, 'LoopEnd': samples}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    print(mark_loop(args.input, args.output))
