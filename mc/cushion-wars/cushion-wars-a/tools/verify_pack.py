#!/usr/bin/env python3
"""Check retained rule/menu/P0 packs; retired board functions must not be referenced.

This checks source structure and references, not command execution on a server.
"""
from pathlib import Path
import json
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
PACKS = {
    'cw-entry': ROOT / 'deploy/server/cw-entry',
    'cw-maps-menu': ROOT / 'deploy/server/cw-maps-menu',
    'cw-p0-probes': ROOT / 'prototypes/round-a-p0',
}
FUNCTION = re.compile(r'\bfunction\s+([a-z0-9_.-]+):([a-z0-9_./-]+)')


def verify():
    errors = []
    registry = {'function': set(), 'dialog': set()}
    documents = []
    for name, folder in PACKS.items():
        try:
            meta = json.loads((folder / 'pack.mcmeta').read_text(encoding='utf-8'))['pack']
            if not (meta.get('min_format') == meta.get('max_format') == 121
                    or meta.get('pack_format') == 121):
                errors.append(f'{name}: expected data pack format 121')
        except (OSError, ValueError, KeyError) as error:
            errors.append(f'{name}: invalid metadata: {error}')
            continue
        if not (folder / 'data').is_dir():
            errors.append(f'{name}: missing data directory')
            continue
        for file in sorted((folder / 'data').rglob('*')):
            if not file.is_file():
                continue
            relative = file.relative_to(folder / 'data')
            parts = relative.parts
            if file.suffix not in {'.json', '.mcfunction'}:
                continue
            try:
                raw = file.read_bytes()
                text = raw.decode('utf-8')
                if raw.startswith(b'\xef\xbb\xbf'):
                    errors.append(f'{name}/{relative}: unexpected UTF-8 BOM')
                payload = json.loads(text) if file.suffix == '.json' else None
            except (OSError, ValueError) as error:
                errors.append(f'{name}/{relative}: {error}')
                continue
            if 'cushionwars_a:' in text:
                errors.append(f'{name}/{relative}: retired board namespace referenced')
            documents.append((name, relative, text, payload))
            if len(parts) >= 3 and parts[1] in registry:
                registry[parts[1]].add(parts[0] + ':' + '/'.join(parts[2:]).rsplit('.', 1)[0])
    for name, relative, text, payload in documents:
        for namespace, function in FUNCTION.findall(text):
            target = namespace + ':' + function
            if target not in registry['function']:
                errors.append(f'{name}/{relative}: missing function {target}')
        parts = relative.parts
        if len(parts) >= 4 and parts[1] == 'tags' and parts[2] in registry:
            for entry in payload.get('values', []):
                target = entry if isinstance(entry, str) else entry['id']
                required = True if isinstance(entry, str) else entry.get('required', True)
                if required and target not in registry[parts[2]]:
                    errors.append(f'{name}/{relative}: unresolved tag entry {target}')
    # Retiring the old pack must keep the Esc menu registered in its replacement.
    tag = PACKS['cw-maps-menu'] / 'data/minecraft/tags/dialog/pause_screen_additions.json'
    try:
        if 'cw_menu:menu' not in json.loads(tag.read_text(encoding='utf-8'))['values']:
            errors.append('Esc menu tag is not registered')
    except (OSError, ValueError, KeyError) as error:
        errors.append(f'Esc menu tag: {error}')
    for relative in ('src/round-a', 'deploy/server/cw-autoverify', 'deploy/server/cw-maptest'):
        if (ROOT / relative).exists():
            errors.append(f'Retired pack restored into active tree: {relative}')
    return documents, errors


def main():
    documents, errors = verify()
    output = ROOT / 'checks/PACK-VERIFY.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({'scope': 'Retained rule/menu/P0 pack structure and references; no live server validation',
                                 'packs': list(PACKS), 'checkedFiles': len(documents), 'errors': errors},
                                ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
    for error in errors:
        print('FAIL: ' + error)
    if not errors:
        print(f'PASS: {len(PACKS)} retained packs, {len(documents)} files, function/tag references and Esc menu registration')
    return bool(errors)


if __name__ == '__main__':
    sys.exit(main())
