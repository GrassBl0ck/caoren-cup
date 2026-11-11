"""Regression tests for public documentation collection and portable links."""

from pathlib import Path
from urllib.parse import unquote, urlsplit
import hashlib
import json
import subprocess
import tempfile
import unittest

import deployment_guide as guide


ROOT = Path(__file__).resolve().parents[1]


class DeploymentGuideTests(unittest.TestCase):
    def setUp(self):
        build = ROOT / 'release-build'
        build.mkdir(exist_ok=True)
        self.root = Path(tempfile.mkdtemp(prefix='guide-test-', dir=build))
        self.files = ['docs/deployment/README.md', 'docs/deployment/next.md', 'mc/INSTALL.md']
        self.manifest = {
            'schemaVersion': 1,
            'entryPoint': self.files[0],
            'installationSources': ['docs/release-installation.md', 'mc/INSTALL.md'],
            'files': self.files,
        }
        self.put(guide.MANIFEST, json.dumps(self.manifest))
        self.put(self.files[0], '# 首页\n[下一步](next.md#安装)\n[MC](../../mc/INSTALL.md)\n'
                 '[源码](../../src/Game.cs#L1)\n[中文](<../../docs/shared/图 示.md>)\n'
                 '```text\n[示例](missing-example.md)\n```\n')
        self.put(self.files[1], '# 安装\n[自己](#安装)\n[网络](https://example.com/a)\n')
        self.put(self.files[2], '# MC\n[首页](../docs/deployment/README.md)\n')
        self.put('docs/release-installation.md', '# 安装\n[开始](deployment/README.md)\n')
        self.put('src/Game.cs', '// source file\n')
        self.put('docs/shared/图 示.md', '# 引用\n')
        self.put('.env', 'PRIVATE_FIXTURE_SECRET')

    def put(self, relative, text):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')
        return path

    def write_manifest(self):
        self.put(guide.MANIFEST, json.dumps(self.manifest))

    def test_portable_links_versioned_source_and_code_fences(self):
        outputs = guide.documentation_outputs(self.root, 'v9.8.7', 'docs/release-installation.md')
        self.assertIn('(guide/docs/deployment/README.md)', outputs['INSTALL.md'])
        homepage = outputs['guide/docs/deployment/README.md']
        self.assertIn('(next.md#安装)', homepage)
        self.assertIn('(../../mc/INSTALL.md)', homepage)
        self.assertIn('/blob/v9.8.7/src/Game.cs#L1', homepage)
        self.assertIn('/blob/v9.8.7/docs/shared/%E5%9B%BE%20%E7%A4%BA.md', homepage)
        self.assertIn('[示例](missing-example.md)', homepage)
        self.assertNotIn('PRIVATE_FIXTURE_SECRET', ''.join(outputs.values()))
        self.assertEqual(len(outputs), len(self.files) + 1)

    def test_mc_installation_uses_the_same_offline_entry(self):
        outputs = guide.documentation_outputs(self.root, 'v9.8.7', 'mc/INSTALL.md')
        self.assertIn('(guide/docs/deployment/README.md)', outputs['INSTALL.md'])

    def test_manifest_rejects_duplicates_private_paths_and_missing_entry(self):
        for relative in ['.env', 'docs/AGENTS.md', 'runtime/private.md', '../escape.md', 'src/Game.cs']:
            with self.subTest(relative=relative):
                self.manifest['files'] = [*self.files, relative]
                self.write_manifest()
                with self.assertRaises(ValueError):
                    guide.read_manifest(self.root)
        self.manifest['files'] = [*self.files, self.files[0]]
        self.write_manifest()
        with self.assertRaises(ValueError):
            guide.read_manifest(self.root)
        self.manifest['files'] = self.files[1:]
        self.write_manifest()
        with self.assertRaises(ValueError):
            guide.read_manifest(self.root)

    def test_invalid_link_fails_before_stage_creation(self):
        self.put(self.files[0], '# 首页\n[丢失](missing.md)\n')
        stage = self.root / 'stage'
        with self.assertRaises(ValueError):
            guide.copy_deployment_guide(self.root, stage, 'v9.8.7', 'mc/INSTALL.md')
        self.assertFalse(stage.exists())

    def test_existing_output_is_retained_without_partial_write(self):
        stage = self.root / 'stage'
        stage.mkdir()
        (stage / 'INSTALL.md').write_bytes(b'original')
        with self.assertRaises(FileExistsError):
            guide.copy_deployment_guide(self.root, stage, 'v9.8.7', 'mc/INSTALL.md')
        self.assertEqual((stage / 'INSTALL.md').read_bytes(), b'original')
        self.assertFalse((stage / 'guide').exists())

    def test_heading_and_outside_repository_links_are_checked(self):
        self.assertGreater(guide.check_documentation(self.root), 0)
        self.put(self.files[1], '# 安装\n[缺失](#不存在)\n')
        with self.assertRaises(ValueError):
            guide.check_documentation(self.root)
        self.put(self.files[1], '# 安装\n[越界](../../../escape.md)\n')
        with self.assertRaises(ValueError):
            guide.check_documentation(self.root)

    def test_unclosed_fences_and_invalid_versions_are_rejected(self):
        self.put(self.files[0], '# 首页\n```text\n')
        with self.assertRaises(ValueError):
            guide.documentation_outputs(self.root, 'v9.8.7', 'mc/INSTALL.md')
        with self.assertRaises(ValueError):
            guide.documentation_outputs(self.root, '../main', 'mc/INSTALL.md')

    def test_real_guide_links_remain_local_only_when_bundled(self):
        self.assertGreater(guide.check_documentation(ROOT), 0)
        for install in ['docs/release-installation.md', 'mc/INSTALL.md']:
            outputs = guide.documentation_outputs(ROOT, 'v9.8.7', install)
            for name, text in outputs.items():
                for line, prose in guide.markdown_lines(text):
                    if not prose:
                        continue
                    for match in guide.LINK.finditer(line):
                        parsed = urlsplit(match.group(2))
                        if parsed.scheme or not parsed.path:
                            continue
                        resolved = guide.posixpath.normpath(guide.posixpath.join(
                            guide.posixpath.dirname(name), unquote(parsed.path)))
                        self.assertIn(resolved, outputs, (name, match.group(2)))

    def test_powershell_and_python_collect_identical_real_documents(self):
        stage = self.root / 'powershell-stage'
        command = (
            "Import-Module './scripts/CaorenRelease.psm1' -Force; "
            "Copy-DeploymentGuide -RepoRoot (Get-Location).Path "
            f"-Stage '{stage.as_posix()}' -Version v9.8.7"
        )
        subprocess.run(['pwsh', '-NoProfile', '-Command', command], cwd=ROOT, check=True,
                       capture_output=True, text=True, encoding='utf-8')
        expected = guide.documentation_outputs(ROOT, 'v9.8.7', 'docs/release-installation.md')
        actual = {path.relative_to(stage).as_posix() for path in stage.rglob('*') if path.is_file()}
        self.assertEqual(actual, set(expected))
        for name, text in expected.items():
            self.assertEqual(hashlib.sha256((stage / name).read_bytes()).digest(),
                             hashlib.sha256(text.encode('utf-8')).digest(), name)


if __name__ == '__main__':
    unittest.main()
