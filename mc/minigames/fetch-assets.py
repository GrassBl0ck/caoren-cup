"""Fetch the pinned upstream map into a local ignored directory; never deploy it."""
from pathlib import Path, PurePosixPath
import argparse
import hashlib
import json
import os
import stat
import urllib.request
import uuid
import zipfile

ROOT = Path(__file__).resolve().parent

def sha256(file):
    digest = hashlib.sha256()
    with file.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--extract', action='store_true', help='extract into a new ignored world-template directory')
    args = parser.parse_args()
    info = json.loads((ROOT.parent / 'source-manifest.json').read_text(encoding='utf-8-sig'))['mapUpstream']
    assets = ROOT / '.assets'
    if assets.exists() and (assets.is_symlink() or not assets.resolve().is_relative_to(ROOT)):
        raise ValueError('Unsafe asset directory')
    assets.mkdir(exist_ok=True)
    archive = assets / ('Map-MiniGames-' + info['commit'] + '.zip')
    if archive.exists():
        if archive.is_symlink() or sha256(archive) != info['archiveSha256']:
            raise ValueError('Existing archive differs; refusing to overwrite it')
    else:
        temporary = assets / ('download-' + uuid.uuid4().hex + '.partial')
        request = urllib.request.Request('https://codeload.github.com/wifi-left/Map-MiniGames/zip/' + info['commit'], headers={'User-Agent':'CaorenCup-pinned-map-fetcher'})
        total = 0
        with urllib.request.urlopen(request, timeout=30) as response, temporary.open('xb') as output:
            for chunk in iter(lambda: response.read(1024 * 1024), b''):
                total += len(chunk)
                if total > info['archiveBytes']:
                    raise ValueError('Downloaded archive exceeds expected size; partial retained')
                output.write(chunk)
        if total != info['archiveBytes'] or sha256(temporary) != info['archiveSha256']:
            raise ValueError('Archive verification failed; partial retained, no extraction performed')
        # Atomic no-replace publication on NTFS/ext4: a concurrent fetch must not overwrite a file.
        os.link(temporary, archive)
        if temporary.parent.resolve() != assets.resolve() or temporary.is_symlink() or not temporary.name.startswith('download-'):
            raise ValueError('Unexpected temporary path; refusing cleanup')
        temporary.unlink()
    print('Verified pinned map archive:', archive.name)
    if not args.extract:
        return
    destination = assets / 'world-template'
    if destination.exists():
        raise ValueError('Extraction target exists; refusing to overwrite it')
    with zipfile.ZipFile(archive) as source:
        entries = []
        total_size = 0
        for item in source.infolist():
            parts = PurePosixPath(item.filename).parts
            if len(parts) < 2:
                continue
            if PurePosixPath(item.filename).is_absolute() or '..' in parts or '\\' in item.filename or stat.S_ISLNK(item.external_attr >> 16):
                raise ValueError('Unsafe upstream archive entry')
            relative = Path(*parts[1:])
            target = (destination / relative).resolve()
            if not target.is_relative_to(destination.resolve()):
                raise ValueError('Archive path escapes extraction target')
            total_size += item.file_size
            if total_size > 800 * 1024 * 1024:
                raise ValueError('Uncompressed resource inventory exceeded')
            entries.append((item, target))
        destination.mkdir()
        for item, target in entries:
            if item.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with source.open(item) as incoming, target.open('xb') as outgoing:
                    for chunk in iter(lambda: incoming.read(1024 * 1024), b''):
                        outgoing.write(chunk)
    print('Extracted a pristine upstream template into', destination)
    print('No production or player world was read or changed.')

if __name__ == '__main__':
    main()
