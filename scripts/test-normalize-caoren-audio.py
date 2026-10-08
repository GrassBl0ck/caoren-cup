import importlib.util
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
from uuid import uuid4

sys.dont_write_bytecode=True
spec=importlib.util.spec_from_file_location('normalizer',Path(__file__).with_name('normalize-caoren-audio.py'))
normalizer=importlib.util.module_from_spec(spec); spec.loader.exec_module(normalizer)

class NormalizerTests(unittest.TestCase):
    def fixture(self):
        root=normalizer.REPO/'release-build/ingamemenu-audio/normalizer-tests'/uuid4().hex
        root.mkdir(parents=True)
        source=root/'source.ogg'; source.write_bytes(b'unchanged-fixture')
        return root,source
    def test_invalid_targets_are_rejected_before_any_process_or_output(self):
        for targets,peak in [([-71],-1.5),([-4],-1.5),([float('nan')],-1.5),([-18,-18],-1.5),([-18],1),([-18],float('inf'))]:
            root,source=self.fixture()
            with patch.object(normalizer.subprocess,'run') as run:
                with self.assertRaises(ValueError): normalizer.normalize(source,root/'output',targets,peak,'ffmpeg','ffprobe')
                run.assert_not_called()
            self.assertFalse((root/'output').exists()); self.assertEqual(b'unchanged-fixture',source.read_bytes())
    def test_output_reuse_does_not_overwrite_existing_audio(self):
        root,source=self.fixture(); output=root/'output'; output.mkdir(); audio=output/'original.wav'; audio.write_bytes(b'preserved')
        with self.assertRaises(ValueError): normalizer.normalize(source,output,[-18],-1.5,'ffmpeg','ffprobe')
        self.assertEqual(b'preserved',audio.read_bytes())
    def test_paths_cannot_escape_worktree(self):
        with self.assertRaises(ValueError): normalizer.inside(normalizer.REPO.parent/'outside.ogg')
    def test_silence_and_nonfinite_measurements_are_not_reported_as_normalized(self):
        result=type('Result',(),{'stderr':'{"input_i":"-inf","input_tp":"-inf","input_lra":"0","input_thresh":"-70"}'})()
        with patch.object(normalizer,'run',return_value=result):
            with self.assertRaises(ValueError): normalizer.measurement('ffmpeg',Path('silent.wav'),-18,-1.5)

if __name__=='__main__': unittest.main()
