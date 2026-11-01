"""Verify imported source snapshots without contacting servers or changing files."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parent

def main():
    manifest = json.loads((ROOT / 'source-manifest.json').read_text(encoding='utf-8-sig'))
    layout = json.loads((ROOT / 'source-layout.json').read_text(encoding='utf-8'))
    def current_path(original):
        if original in layout['paths']:
            return layout['paths'][original]
        for prefix, replacement in layout['prefixes'].items():
            if original.startswith(prefix):
                return replacement + original[len(prefix):]
        return original
    checked = set()
    for entry in manifest['files']:
        file = (ROOT / current_path(entry['path'])).resolve()
        if not file.is_relative_to(ROOT) or file in checked:
            raise ValueError('Invalid or duplicate source path')
        checked.add(file)
        if not file.is_file() or hashlib.sha256(file.read_bytes()).hexdigest() != layout['integrationChanges'].get(entry['path'], {}).get('sha256', entry['sha256']):
            raise ValueError('Source snapshot changed: ' + entry['path'])
    for relative in ['cushion-wars-a/LICENSE', 'cross-era-pvp/LICENSE', 'third-party/old-combat-mechanics/LICENCE', 'minigames/source/LISENCE']:
        if not (ROOT / current_path(relative)).is_file():
            raise ValueError('Missing component license: ' + relative)
    print(f"Layout/reference edits verified: {len(layout['integrationChanges'])}; original manifest preserved.")
    changes = sum('upstreamSha256' in entry for entry in manifest['files'])
    print(f'PASS: {len(checked)} imported source files and component licenses verified; {changes} documented integration change(s).')
    print('Map/world/audio assets, runtime data and dependency JARs are managed separately.')

if __name__ == '__main__':
    main()
