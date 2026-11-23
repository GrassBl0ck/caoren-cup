"""Build a seekable image archive. Original URLs are the index keys."""
import hashlib
import io
import json
import os
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from PIL import Image

root, target = map(Path, sys.argv[1:])
types = {'.png':'image/png', '.webp':'image/webp', '.jpg':'image/jpeg', '.jpeg':'image/jpeg',
         '.gif':'image/gif', '.avif':'image/avif', '.svg':'image/svg+xml', '.ico':'image/x-icon'}
paths = sorted(p for group in ('assets', 'weaponpaints') for p in (root / group).rglob('*')
               if p.suffix.lower() in types and p.is_file())
if not all(any(p.is_relative_to(root / 'weaponpaints' / section) for p in paths) for section in ('base', 'stickers')):
    raise SystemExit('Missing weaponpaints image collections in ' + str(root))
target.mkdir(parents=True, exist_ok=True)
stamp = hashlib.sha256(b'webp-v3/base640q82m3/stickers256q80m4/assets1600q88m3/alpha100\n')
for p in paths:
    st = p.stat()
    stamp.update(f'{p.relative_to(root)}:{st.st_size}:{st.st_mtime_ns}\n'.encode())
digest = stamp.hexdigest()
index_file = target / 'index.json'
pack = target / 'images.pack'
if index_file.exists() and pack.exists():
    old = json.loads(index_file.read_text(encoding='utf8'))
    if old.get('source') == digest and old.get('size') == pack.stat().st_size:
        print(f'{len(paths)} local images ready')
        sys.exit(0)

def encode(p):
    raw = p.read_bytes()
    if p.suffix.lower() in ('.png', '.jpg', '.jpeg', '.webp'):
        with Image.open(io.BytesIO(raw)) as im:
            if not getattr(im, 'is_animated', False):
                section = p.relative_to(root).parts[:2]
                if section == ('weaponpaints', 'stickers'):
                    limit, quality, method = (256, 256), 80, 4
                elif section == ('weaponpaints', 'base'):
                    limit, quality, method = (640, 640), 82, 3
                else:
                    limit, quality, method = (1600, 1600), 88, 3
                original_size = im.size
                if im.mode not in ('RGB', 'RGBA'):
                    im = im.convert('RGBA' if im.has_transparency_data else 'RGB')
                im.thumbnail(limit, Image.Resampling.LANCZOS)
                buf = io.BytesIO()
                im.save(buf, format='WEBP', quality=quality, method=method, alpha_quality=100)
                if im.size != original_size or buf.tell() < len(raw):
                    return 'image/webp', buf.getvalue()
    return types[p.suffix.lower()], raw

index = {}
with ThreadPoolExecutor(max_workers=min(8, os.cpu_count() or 1)) as pool, (target / 'images.tmp').open('wb') as out:
    for i in range(0, len(paths), 32):
        batch = paths[i:i+32]
        for p, (mime, data) in zip(batch, pool.map(encode, batch)):
            index['/' + p.relative_to(root).as_posix()] = [out.tell(), len(data), mime]
            out.write(data)
    size = out.tell()
(target / 'index.tmp').write_text(json.dumps(dict(source=digest, size=size, images=index), separators=(',',':')), encoding='utf8')
os.replace(target / 'images.tmp', pack)
os.replace(target / 'index.tmp', index_file)
print(f'{len(paths)} local images, {size} bytes')
