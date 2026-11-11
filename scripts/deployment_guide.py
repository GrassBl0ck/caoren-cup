"""Validate and collect explicitly selected public deployment documentation."""

from pathlib import Path, PurePosixPath
from urllib.parse import quote, unquote, urlsplit
import argparse
import json
import posixpath
import re
import unicodedata


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = 'scripts/deployment-guide-manifest.json'
LINK = re.compile(r'(!?\[[^\]\n]*\]\()(<[^>\n]+>|[^\s)\n]+)((?:[ \t]+"[^"\n]*")?\))')
FENCE = re.compile(r'^\s*(`{3,}|~{3,})')
VERSION = re.compile(r'v\d+\.\d+\.\d+')


def repository_path(root, relative):
    """Allow relative links inside the repository, rejecting symlink traversal."""
    if PurePosixPath(relative).is_absolute() or '\\' in relative or ':' in relative:
        raise ValueError(f'Invalid repository path: {relative}')
    candidate = root / relative
    resolved = candidate.resolve()
    if not resolved.is_relative_to(root.resolve()):
        raise ValueError(f'Path escapes repository: {relative}')
    for parent in [candidate, *candidate.parents]:
        if parent == root:
            break
        if parent.is_symlink():
            raise ValueError(f'Symlink in documentation path: {relative}')
    if not candidate.exists():
        raise ValueError(f'Missing documentation target: {relative}')
    return resolved


def public_markdown(root, relative):
    parts = PurePosixPath(relative).parts
    if (not parts or '..' in parts or relative.endswith('/')
            or PurePosixPath(relative).suffix != '.md'
            or any(part.startswith('.') or part.startswith('backup')
                   or part in {'runtime', 'archive', 'AGENTS.md'} for part in parts)):
        raise ValueError(f'Not a public Markdown entry: {relative}')
    path = repository_path(root, relative)
    if not path.is_file():
        raise ValueError(f'Expected Markdown file: {relative}')
    return path


def read_manifest(root):
    manifest = json.loads(repository_path(root, MANIFEST).read_text(encoding='utf-8'))
    files = manifest['files']
    installs = manifest['installationSources']
    if (manifest['schemaVersion'] != 1 or not files or not installs
            or len(files) != len(set(files)) or len(installs) != len(set(installs))
            or manifest['entryPoint'] not in files):
        raise ValueError('Invalid deployment documentation manifest')
    for relative in [*files, *installs]:
        public_markdown(root, relative)
    return manifest


def markdown_lines(text):
    """Expose only prose to link rewriting and heading validation."""
    fence = None
    for line in text.splitlines(keepends=True):
        marker = FENCE.match(line)
        if marker:
            value = marker.group(1)
            if fence is None:
                fence = value
            elif value[0] == fence[0] and len(value) >= len(fence):
                fence = None
            yield line, False
        else:
            yield line, fence is None
    if fence is not None:
        raise ValueError('Unclosed Markdown code fence')


def resolve_link(root, source, target):
    value = target[1:-1] if target.startswith('<') else target
    parsed = urlsplit(value)
    if parsed.scheme in {'http', 'https', 'mailto'}:
        return None
    if parsed.scheme or parsed.netloc or parsed.query:
        raise ValueError(f'Unsupported local documentation link: {value}')
    relative = posixpath.normpath(posixpath.join(posixpath.dirname(source), unquote(parsed.path)))
    if not parsed.path:
        relative = source
    path = repository_path(root, relative)
    return path.relative_to(root.resolve()).as_posix(), parsed.fragment


def render_markdown(root, source, destination, version, selected):
    if not VERSION.fullmatch(version):
        raise ValueError('Use a concrete vX.Y.Z release version')
    text = public_markdown(root, source).read_text(encoding='utf-8-sig')

    def replace(match):
        target = match.group(2)
        resolved = resolve_link(root, source, target)
        if resolved is None or target.startswith('#'):
            return match.group(0)
        relative, fragment = resolved
        if relative in selected:
            link = posixpath.relpath('guide/' + relative, posixpath.dirname(destination) or '.')
            link = quote(link, safe='/-_.~')
        else:
            kind = 'tree' if (root / relative).is_dir() else 'blob'
            link = f'https://github.com/GrassBl0ck/caoren-cup/{kind}/{version}/' + quote(relative, safe='/')
        if fragment:
            link += '#' + fragment
        return match.group(1) + link + match.group(3)

    return ''.join(LINK.sub(replace, line) if prose else line
                   for line, prose in markdown_lines(text))


def documentation_outputs(root, version, installation_source):
    manifest = read_manifest(root)
    if installation_source not in manifest['installationSources']:
        raise ValueError('Installation source is not explicitly selected')
    selected = set(manifest['files'])
    outputs = {
        'guide/' + source: render_markdown(root, source, 'guide/' + source, version, selected)
        for source in manifest['files']
    }
    outputs['INSTALL.md'] = render_markdown(root, installation_source, 'INSTALL.md', version, selected)
    return outputs


def copy_deployment_guide(root, stage, version, installation_source):
    # Resolve all sources and links before changing the stage.
    outputs = documentation_outputs(root, version, installation_source)
    destinations = {name: stage / name for name in outputs}
    for name, destination in destinations.items():
        if destination.exists():
            raise FileExistsError(name)
        if not destination.resolve().is_relative_to(stage.resolve()):
            raise ValueError(f'Documentation output escapes stage: {name}')
    for name, destination in destinations.items():
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(outputs[name], encoding='utf-8', newline='\n')


def heading_anchors(text):
    anchors = set()
    counts = {}
    for line, prose in markdown_lines(text):
        match = re.match(r'^#{1,6}\s+(.+?)\s*#*\s*$', line) if prose else None
        if match is None:
            continue
        heading = match.group(1).lower().replace('`', '')
        slug = ''.join(char for char in heading
                       if char in '-_ ' or unicodedata.category(char)[0] in {'L', 'N', 'M'})
        slug = slug.replace(' ', '-')
        count = counts.get(slug, 0)
        counts[slug] = count + 1
        anchors.add(slug + (f'-{count}' if count else ''))
    return anchors


def check_documentation(root):
    manifest = read_manifest(root)
    discovered = {path.relative_to(root).as_posix() for path in (root / 'docs/deployment').glob('*.md')}
    if not discovered.issubset(set(manifest['files'])):
        raise ValueError('Deployment chapter missing from explicit manifest')
    checked = 0
    for source in dict.fromkeys([*manifest['files'], *manifest['installationSources']]):
        text = public_markdown(root, source).read_text(encoding='utf-8-sig')
        for line, prose in markdown_lines(text):
            if not prose:
                continue
            for match in LINK.finditer(line):
                resolved = resolve_link(root, source, match.group(2))
                if resolved is None:
                    continue
                relative, fragment = resolved
                if fragment and relative.endswith('.md'):
                    target = (root / relative).read_text(encoding='utf-8-sig')
                    if unquote(fragment) not in heading_anchors(target):
                        raise ValueError(f'Missing heading: {source} -> {match.group(2)}')
                checked += 1
        render_markdown(root, source, 'guide/' + source, 'v0.0.0', set(manifest['files']))
    return checked


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', required=True)
    parser.parse_args()
    count = check_documentation(ROOT)
    print(f'PASS: deployment Markdown, UTF-8, explicit selection, {count} local links and headings')
