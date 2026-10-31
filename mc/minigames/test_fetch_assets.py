"""Behavior checks for safe extraction. Uses small local ZIP fixtures, never the network."""
from pathlib import Path
import contextlib
import hashlib
import importlib.util
import io
import json
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

MODULE = Path(__file__).with_name('fetch-assets.py')
RUNTIME = Path(__file__).resolve().parent / '.runtime' / 'map-fetch-tests'

class FetchTests(unittest.TestCase):
    def fixture(self, entries):
        RUNTIME.mkdir(parents=True, exist_ok=True)
        root = Path(tempfile.mkdtemp(prefix='case-', dir=RUNTIME))
        module_root = root / 'module'
        assets = module_root / '.assets'
        assets.mkdir(parents=True)
        archive = assets / 'Map-MiniGames-fixture.zip'
        with zipfile.ZipFile(archive, 'w') as writer:
            for name, value in entries:
                writer.writestr(name, value)
        info = {'commit':'fixture', 'archiveSha256':hashlib.sha256(archive.read_bytes()).hexdigest(), 'archiveBytes':archive.stat().st_size}
        (root / 'source-manifest.json').write_text(json.dumps({'mapUpstream':info}), encoding='utf-8')
        spec = importlib.util.spec_from_file_location('map_asset_fixture', MODULE)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        module.ROOT = module_root
        return root, assets, module

    def run_extract(self, module):
        previous = sys.argv
        try:
            sys.argv = ['fetch-assets.py', '--extract']
            with contextlib.redirect_stdout(io.StringIO()):
                module.main()
        finally:
            sys.argv = previous

    def test_pristine_template_and_existing_target_are_not_overwritten(self):
        _, assets, module = self.fixture([('upstream/data/file.json', b'{"test":true}')])
        self.run_extract(module)
        file = assets / 'world-template/data/file.json'
        self.assertEqual(file.read_bytes(), b'{"test":true}')
        with self.assertRaisesRegex(ValueError, 'target exists'):
            self.run_extract(module)
        self.assertEqual(file.read_bytes(), b'{"test":true}')

    def test_traversal_is_rejected_before_extracting_any_files(self):
        root, assets, module = self.fixture([('upstream/../../escape.txt', b'no')])
        with self.assertRaisesRegex(ValueError, 'Unsafe'):
            self.run_extract(module)
        self.assertFalse((root / 'escape.txt').exists())
        self.assertFalse((assets / 'world-template').exists())

    def test_symbolic_links_are_rejected(self):
        link = zipfile.ZipInfo('upstream/link')
        link.create_system = 3
        link.external_attr = (stat.S_IFLNK | 0o777) << 16
        _, assets, module = self.fixture([(link, b'../../escape')])
        with self.assertRaisesRegex(ValueError, 'Unsafe'):
            self.run_extract(module)
        self.assertFalse((assets / 'world-template').exists())

    def test_mismatched_cached_archive_is_not_replaced_or_extracted(self):
        _, assets, module = self.fixture([('upstream/a.txt', b'original')])
        archive = assets / 'Map-MiniGames-fixture.zip'
        archive.write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError, 'refusing to overwrite'):
            self.run_extract(module)
        self.assertEqual(archive.read_bytes(), b'changed')
        self.assertFalse((assets / 'world-template').exists())

    def test_verified_download_publishes_exclusively_and_extracts_without_network(self):
        _, assets, module = self.fixture([('upstream/a.txt', b'verified fixture')])
        archive = assets / 'Map-MiniGames-fixture.zip'
        data = archive.read_bytes()
        archive.rename(assets / 'fixture-input.zip')
        with patch.object(module.urllib.request, 'urlopen', return_value=io.BytesIO(data)) as incoming:
            self.run_extract(module)
        self.assertEqual(incoming.call_count, 1)
        self.assertEqual(archive.read_bytes(), data)
        self.assertEqual((assets / 'world-template/a.txt').read_bytes(), b'verified fixture')
        self.assertEqual(list(assets.glob('download-*.partial')), [])

if __name__ == '__main__':
    unittest.main()
