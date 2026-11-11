"""Packaging behavior checks; fixtures stay in ignored release-build for review."""
from pathlib import Path
import importlib.util,json,tempfile,unittest,zipfile
import hashlib

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('mc_package',ROOT/'scripts/package-caoren-mc.py')
package=importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)

class McPackageTests(unittest.TestCase):
    def setUp(self):
        (ROOT/'release-build').mkdir(exist_ok=True)
        self.root=Path(tempfile.mkdtemp(prefix='mc-package-test-',dir=ROOT/'release-build'))
        assert self.root.resolve().is_relative_to((ROOT/'release-build').resolve())
    def pack_source(self):
        source=self.root/'datapack';source.mkdir()
        (source/'pack.mcmeta').write_text('{"pack":{"pack_format":98,"description":"fixture"}}',encoding='utf-8')
        fn=source/'data/example/function/load.mcfunction';fn.parent.mkdir(parents=True)
        fn.write_text('say fixture\n',encoding='utf-8')
        return source
    def test_datapack_has_installable_root_and_no_runtime(self):
        source=self.pack_source();cache=source/'__pycache__';cache.mkdir();(cache/'x.pyc').write_bytes(b'cache')
        output=self.root/'pack.zip';package.pack_datapack(source,output)
        with zipfile.ZipFile(output) as z:
            self.assertEqual(set(z.namelist()),{'pack.mcmeta','data/example/function/load.mcfunction'})
    def test_local_collaboration_instructions_are_not_distributed(self):
        source=self.pack_source()
        (source/'AGENTS.md').write_text('private local workflow',encoding='utf-8')
        nested=source/'docs';nested.mkdir()
        (nested/'AGENTS.md').write_text('private nested workflow',encoding='utf-8')
        (nested/'README.md').write_text('public game guide',encoding='utf-8')
        output=self.root/'pack.zip';package.pack_datapack(source,output)
        with zipfile.ZipFile(output) as z:
            self.assertIn('docs/README.md',z.namelist())
            self.assertFalse(any(Path(name).name=='AGENTS.md' for name in z.namelist()))
            self.assertFalse(any(b'private' in z.read(name) for name in z.namelist()))
    def test_missing_datapack_entry_fails_before_output(self):
        source=self.root/'empty';source.mkdir();output=self.root/'pack.zip'
        with self.assertRaises(ValueError): package.pack_datapack(source,output)
        self.assertFalse(output.exists())
    def test_existing_datapack_output_is_retained(self):
        source=self.pack_source();output=self.root/'pack.zip';output.write_bytes(b'original')
        with self.assertRaises(FileExistsError): package.pack_datapack(source,output)
        self.assertEqual(output.read_bytes(),b'original')
    def test_source_selection_preserves_wrappers_but_rejects_worlds_secrets_and_builds(self):
        for name in ['cushion-wars/cushion-wars-a/plugin/src/main/java/Game.java','third-party/old-combat-mechanics/gradle/wrapper/gradle-wrapper.jar','cushion-wars/cushion-wars-a/deploy/server/mapctl']:
            self.assertTrue(package.is_source_file(Path(name)),name)
        for name in ['minigames/source/datapacks/example/data/a/function/tick.mcfunction','minigames/fetch-assets.py','.local-maps/minigames/source/README.md','minigames/.assets/world/level.dat','cushion-wars/cushion-wars-a/plugin/lib/paper.jar','cross-era-pvp/stage03/build/libs/Probe.jar','server.properties','ops.json','.env','world/playerdata/a.dat']:
            self.assertFalse(package.is_source_file(Path(name)),name)
    def test_imported_sources_are_selected_after_directory_migration(self):
        manifest=json.loads((ROOT/'mc/source-manifest.json').read_text(encoding='utf-8-sig'))
        layout=json.loads((ROOT/'mc/source-layout.json').read_text(encoding='utf-8'))
        expected=package.imported_current_paths(manifest,layout)
        selected={p.relative_to(ROOT/'mc').as_posix() for p in package.source_files(ROOT/'mc')}
        self.assertTrue(expected.issubset(selected),sorted(expected-selected))
        excluded = {entry['path'] for entry in manifest['files']
                    if entry['path'] in layout.get('excludedPaths', {})
                    or any(entry['path'].startswith(prefix) for prefix in layout.get('excludedPrefixes', {}))}
        self.assertEqual(len(expected),len(manifest['files'])-len(excluded))
        self.assertFalse(any(name.startswith('minigames/') for name in expected | selected))
        missing=next(iter(expected))
        self.assertFalse(expected.issubset(selected-{missing}))
    def test_bundle_manifest_paths_hashes_and_exclusive_output(self):
        stage=self.root/'stage';stage.mkdir();(stage/'INSTALL.md').write_text('install\n',encoding='utf-8')
        target=self.root/'bundle.zip';package.write_bundle(stage,target,'v2.0.0',{'experimentalDefaultEnabled':False})
        with zipfile.ZipFile(target) as z:
            manifest=json.loads(z.read('package-manifest.json'))
            self.assertEqual(manifest['version'],'v2.0.0')
            self.assertEqual([f['path'] for f in manifest['files']],['INSTALL.md'])
            self.assertFalse(manifest['provenance']['experimentalDefaultEnabled'])
            self.assertNotIn('MapMiniGames', manifest['modules'])
        original=target.read_bytes()
        with self.assertRaises(FileExistsError): package.write_bundle(stage,target,'v2.0.0',{})
        self.assertEqual(target.read_bytes(),original)

    def test_deployment_guide_and_offline_entry_are_in_mc_bundle_manifest(self):
        stage = self.root / 'guide-stage'
        package.copy_deployment_guide(ROOT, stage, 'v9.8.7', 'mc/INSTALL.md')
        output = self.root / 'guide-bundle.zip'
        package.write_bundle(stage, output, 'v9.8.7', {})
        with zipfile.ZipFile(output) as archive:
            install = archive.read('INSTALL.md').decode('utf-8')
            self.assertIn('(guide/docs/deployment/README.md)', install)
            manifest = json.loads(archive.read('package-manifest.json'))
            rows = {row['path']: row for row in manifest['files']}
            name = 'guide/docs/deployment/README.md'
            self.assertEqual(rows[name]['sha256'], hashlib.sha256(archive.read(name)).hexdigest())
            self.assertEqual(rows[name]['size'], len(archive.read(name)))
            cs = archive.read('guide/docs/deployment/04-cs.md').decode('utf-8')
            self.assertIn('/blob/v9.8.7/cs/game-plugin/Features/HeadSizeFeature.cs', cs)

if __name__=='__main__': unittest.main()
