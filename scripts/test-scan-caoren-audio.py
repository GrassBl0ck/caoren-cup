import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
import sys
from unittest.mock import patch
from uuid import uuid4

sys.dont_write_bytecode = True
spec=importlib.util.spec_from_file_location('scanner',Path(__file__).with_name('scan-caoren-audio.py'))
scanner=importlib.util.module_from_spec(spec); spec.loader.exec_module(scanner)

class ScannerTests(unittest.TestCase):
    def fixture(self):
        root=scanner.REPO/'release-build/ingamemenu-audio/scanner-tests'/uuid4().hex
        root.mkdir(parents=True)
        return root
    def test_wrong_extension_content_is_rejected_without_decoder(self):
        root=self.fixture(); audio=root/'fake.ogg'; audio.write_bytes(b'RIFFfake')
        with self.assertRaisesRegex(ValueError,'OGG 容器'): scanner.inspect_audio(audio,'missing','missing')
    def test_success_requires_full_decode_and_existing_edits_survive(self):
        root=self.fixture(); source=root/'sources'; source.mkdir(); (source/'a.ogg').write_bytes(b'OggSfixture')
        catalog=root/'edited.json'
        custom=[{'Id':'music.custom','SourceFile':'a.ogg','DisplayName':'已编辑名称','DefaultVolume':0.37,'Channel':'Music','Loop':True}]
        catalog.write_text(json.dumps(custom,ensure_ascii=False),encoding='utf-8')
        meta={'streams':[{'codec_type':'audio','codec_name':'vorbis','channels':2,'sample_rate':'48000'}],'format':{'duration':'12.5'}}
        with patch.object(scanner.subprocess,'run',side_effect=[SimpleNamespace(returncode=0,stdout=json.dumps(meta),stderr=''),SimpleNamespace(returncode=0,stdout='',stderr='')]) as run:
            report=scanner.scan(source,catalog,root/'out','ffprobe','ffmpeg')
            self.assertTrue(report['Success']); self.assertEqual(2,run.call_count)
            self.assertIn('-xerror',run.call_args.args[0])
        self.assertEqual(custom,json.loads((root/'out/audio-events.draft.json').read_text(encoding='utf-8')))
        self.assertEqual(custom,json.loads(catalog.read_text(encoding='utf-8')))
    def test_decoder_failure_does_not_register_a_bad_file(self):
        root=self.fixture(); (root/'bad.ogg').write_bytes(b'OggSfixture')
        meta={'streams':[{'codec_type':'audio'}],'format':{'duration':'1'}}
        with patch.object(scanner.subprocess,'run',side_effect=[SimpleNamespace(returncode=0,stdout=json.dumps(meta),stderr=''),SimpleNamespace(returncode=1,stdout='',stderr='corrupt')]):
            result=scanner.scan(root,root/'missing.json',root/'out','ffprobe','ffmpeg')
        self.assertFalse(result['Success']); self.assertEqual([],json.loads((root/'out/audio-events.draft.json').read_text()))
    def test_path_escape_and_reused_output_are_rejected(self):
        root=self.fixture()
        with self.assertRaises(ValueError): scanner.inside(root/'../../../../../../outside',root)
        with self.assertRaises(ValueError): scanner.scan(root,root/'catalog.json',root)
    def test_missing_registered_source_is_reported(self):
        root=self.fixture(); path=root/'catalog.json'; path.write_text('[{"Id":"custom.a","SourceFile":"missing.ogg"}]')
        report=scanner.scan(root,path,root/'out')
        self.assertFalse(report['Success']); self.assertIn('不存在',report['Sources'][0]['Error'])
if __name__=='__main__': unittest.main()
