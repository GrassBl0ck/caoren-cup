import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import wave

spec = importlib.util.spec_from_file_location('loop_marker', Path(__file__).with_name('mark-caoren-audio-loop.py'))
marker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(marker)


class LoopMarkerTests(unittest.TestCase):
    def setUp(self):
        # Keep fixtures within the authorized worktree.
        root = Path(__file__).resolve().parents[1] / 'release-build/ingamemenu-audio/loop-marker-tests'
        root.mkdir(parents=True, exist_ok=True)
        self.directory = Path(tempfile.mkdtemp(dir=root))
        self.source, self.output = self.directory / 'source.wav', self.directory / 'loop.wav'
        with wave.open(str(self.source), 'wb') as wav:
            wav.setparams((2, 2, 48000, 0, 'NONE', 'not compressed'))
            wav.writeframes(b'\0' * 480 * 4)

    def test_audio_is_preserved_and_cues_cover_whole_file(self):
        original = self.source.read_bytes()
        result = marker.mark_loop(self.source, self.output)
        self.assertEqual(original, self.source.read_bytes())
        with wave.open(str(self.source)) as a, wave.open(str(self.output)) as b:
            self.assertEqual(a.readframes(480), b.readframes(480))
        raw = self.output.read_bytes()
        offset = raw.index(b'cue ') + 8
        self.assertEqual(2, struct.unpack_from('<I', raw, offset)[0])
        self.assertEqual((0, 480), tuple(struct.unpack_from('<I', raw, offset + 4 + i * 24 + 20)[0] for i in range(2)))
        self.assertEqual(480, result['LoopEnd'])
        self.assertEqual(len(raw) - 8, struct.unpack_from('<I', raw, 4)[0])

    def test_cannot_overwrite_input_or_existing_output(self):
        for output in (self.source, self.output):
            if output != self.source:
                output.write_bytes(b'keep')
            before = output.read_bytes()
            with self.assertRaises(ValueError): marker.mark_loop(self.source, output)
            self.assertEqual(before, output.read_bytes())

    def test_existing_markers_require_review(self):
        marker.mark_loop(self.source, self.output)
        with self.assertRaises(ValueError): marker.mark_loop(self.output, self.directory / 'second.wav')

    def test_corruption_does_not_create_output(self):
        valid = self.source.read_bytes()
        for data in (b'fake', valid[:-1], valid + b'cue ', valid[:4] + b'\0' * 4 + valid[8:]):
            self.source.write_bytes(data)
            with self.assertRaises(ValueError): marker.mark_loop(self.source, self.output)
            self.assertFalse(self.output.exists())


if __name__ == '__main__': unittest.main()
