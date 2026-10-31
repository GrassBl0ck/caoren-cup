"""Verify imported source snapshots without contacting servers or changing files."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parent

def main():
    manifest = json.loads((ROOT / 'source-manifest.json').read_text(encoding='utf-8-sig'))
    checked = set()
    for entry in manifest['files']:
        file = (ROOT / entry['path']).resolve()
        if not file.is_relative_to(ROOT) or entry['path'] in checked:
            raise ValueError('Invalid or duplicate source path')
        checked.add(entry['path'])
        if not file.is_file() or hashlib.sha256(file.read_bytes()).hexdigest() != entry['sha256']:
            raise ValueError('Source snapshot changed: ' + entry['path'])
    for relative in ['cushion-wars-a/LICENSE', 'cross-era-pvp/LICENSE', 'third-party/old-combat-mechanics/LICENCE', 'minigames/source/LISENCE']:
        if not (ROOT / relative).is_file():
            raise ValueError('Missing component license: ' + relative)
    changes = sum('upstreamSha256' in entry for entry in manifest['files'])
    print(f'PASS: {len(checked)} imported source files and component licenses verified; {changes} documented integration change(s).')
    print('Map/world/audio assets, runtime data and dependency JARs are managed separately.')

if __name__ == '__main__':
    main()
