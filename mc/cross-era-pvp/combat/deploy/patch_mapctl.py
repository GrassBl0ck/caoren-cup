"""Render a narrow mapctl delta; preserve the imported launcher snapshot unchanged."""
import argparse
import hashlib
from pathlib import Path

ORIGINAL_SHA256 = '5b896fca0fd84db1b83927c79c4512cfac0241674d3c00e4cbf951072fa2d4fa'
ANCHOR = '    exec "$JAVA_BIN" -Xms1G -Xmx3G -Dfile.encoding=UTF-8 \\\n'
WORLD = '        --world "$WORLD_DIR_NAME"\n'
PRELUDE = '''    # [草人杯] Only load the selected map's additional plugin directory.
    local plugin_args=()
    if [ -d "$MAPS/$id/plugins" ]; then
        plugin_args=(--add-plugin-dir "$MAPS/$id/plugins")
    fi
'''
PATCHED_WORLD = '        --world "$WORLD_DIR_NAME" "${plugin_args[@]}"\n'

def render(original: str) -> str:
    if PRELUDE in original:
        restored = original.replace(PRELUDE, '', 1).replace(PATCHED_WORLD, WORLD, 1)
        if hashlib.sha256(restored.encode('utf-8')).hexdigest() == ORIGINAL_SHA256:
            return original
        raise ValueError('Existing launcher delta differs from the recorded version')
    if hashlib.sha256(original.encode('utf-8')).hexdigest() != ORIGINAL_SHA256:
        raise ValueError('Launcher changed; inspect its actual delta before patching')
    if original.count(ANCHOR) != 1 or original.count(WORLD) != 1:
        raise ValueError('Ambiguous launcher anchors')
    return original.replace(ANCHOR, PRELUDE + ANCHOR, 1).replace(WORLD, PATCHED_WORLD, 1)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    if args.output.exists():
        parser.error('Output exists; preserve it before rendering another candidate')
    text = render(args.input.read_text(encoding='utf-8'))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(text, encoding='utf-8', newline='\n')
