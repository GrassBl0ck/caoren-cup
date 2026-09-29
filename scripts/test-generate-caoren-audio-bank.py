import importlib.util
from pathlib import Path
import unittest
spec=importlib.util.spec_from_file_location('bank',Path(__file__).with_name('generate-caoren-audio-bank.py'))
bank=importlib.util.module_from_spec(spec);spec.loader.exec_module(bank)

class BankTests(unittest.TestCase):
    def entry(self,**changes):
        return dict(Id='music.demo',SourceFile='demo.ogg',DisplayName='音乐',Channel='Music',Loop=False,DefaultVolume=1,**changes)
    def scan(self,duration=30):return {'Sources':[dict(SourceFile='demo.ogg',Valid=True,DurationSeconds=duration)]}
    def test_stable_native_ids_duration_and_two_variants(self):
        runtime,assets,text=bank.generate([self.entry()],self.scan())
        self.assertTrue(runtime[0]['NativeEvent']);self.assertEqual('Music',runtime[0]['Channel'])
        self.assertEqual(30,assets[0]['DurationSeconds']);self.assertEqual(2,len(assets[0]['Resources']))
        self.assertIn('music_demo_loop.vsnd',text);self.assertIn('caorencup.audio.music.demo.loop',text)
    def test_existing_loop_source_keeps_original_loop_path(self):
        entry=self.entry();entry['Loop']=True
        runtime,assets,text=bank.generate([entry],self.scan())
        self.assertTrue(runtime[0]['Loop']);self.assertEqual('sounds/caorencup/music_demo_once.vsnd',assets[0]['Resources'][0])
    def test_flat_resource_path_collisions_are_rejected(self):
        a=self.entry();b=self.entry();b['Id']='music_demo'
        with self.assertRaises(ValueError):bank.generate([a,b],self.scan())
    def test_missing_or_invalid_duration_does_not_make_up_a_length(self):
        for value in [0,-1,float('nan'),float('inf')]:
            with self.assertRaises(ValueError):bank.generate([self.entry()],self.scan(value))
        with self.assertRaises(KeyError):bank.generate([self.entry()],{'Sources':[]})
    def test_existing_nonimported_events_are_preserved(self):
        entry=dict(Id='existing.event',Source='Player.Damage',NativeEvent=True,Loop=False,Channel='Effect',DefaultVolume=.5)
        runtime,assets,text=bank.generate([entry],{'Sources':[]})
        self.assertEqual([entry],runtime);self.assertEqual([],assets)
if __name__=='__main__':unittest.main()
