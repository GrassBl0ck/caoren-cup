"""只扫描、解码校验和生成草稿；不修改已编辑清单，不启动构建。"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import subprocess
import sys
from datetime import datetime, timezone

REPO = Path(__file__).resolve().parents[1]

def inside(path, root):
    resolved = Path(path).resolve()
    if not resolved.is_relative_to(Path(root).resolve()):
        raise ValueError(f'路径超出指定目录：{resolved}')
    return resolved

def read_catalog(path):
    if not path.exists(): return []
    value = json.loads(path.read_text(encoding='utf-8-sig'))
    if not isinstance(value, list): raise ValueError('音频登记清单必须是数组。')
    ids, sources = set(), set()
    for event in value:
        if not event.get('Id') or event['Id'] in ids: raise ValueError('缺少或重复事件 ID。')
        ids.add(event['Id'])
        if 'SourceFile' in event:
            if event['SourceFile'] in sources: raise ValueError('源文件重复登记。')
            sources.add(event['SourceFile'])
    return value

def inspect_audio(path, ffprobe, ffmpeg):
    if path.suffix.lower() != '.ogg': raise ValueError('源文件必须使用 OGG。')
    with path.open('rb') as stream:
        if stream.read(4) != b'OggS': raise ValueError('文件内容不是 OGG 容器。')
    result = subprocess.run([ffprobe, '-v', 'error', '-show_streams', '-show_format', '-of', 'json', str(path)],
        capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=60)
    if result.returncode: raise ValueError(result.stderr.strip() or 'ffprobe 失败。')
    info = json.loads(result.stdout)
    audio = [stream for stream in info.get('streams', []) if stream.get('codec_type') == 'audio']
    if len(audio) != 1: raise ValueError('需要恰好一个音轨。')
    duration = float(info.get('format', {}).get('duration', 0))
    if not math.isfinite(duration) or duration <= 0: raise ValueError('音频时长无效。')
    # 实际解码整个文件，不能只凭扩展名或元数据判断。
    decode = subprocess.run([ffmpeg, '-v', 'error', '-xerror', '-i', str(path), '-map', '0:a:0', '-f', 'null', '-'],
        capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=max(60, duration * 2))
    if decode.returncode: raise ValueError(decode.stderr.strip() or '全量解码失败。')
    return {'DurationSeconds': duration, 'Codec': audio[0].get('codec_name'),
        'Channels': audio[0].get('channels'), 'SampleRate': audio[0].get('sample_rate'),
        'Sha256': hashlib.sha256(path.read_bytes()).hexdigest()}

def scan(source_root, catalog_path, output_root, ffprobe=None, ffmpeg=None):
    source_root = inside(source_root, REPO)
    catalog_path = inside(catalog_path, REPO)
    output_root = inside(output_root, REPO)
    if output_root.exists(): raise ValueError('扫描输出目录已存在，停止以免覆盖草稿。')
    existing = read_catalog(catalog_path)
    draft = [dict(event) for event in existing]
    by_source = {event['SourceFile']: event for event in draft if 'SourceFile' in event}
    by_id = {event['Id'] for event in draft}
    files = sorted(source_root.rglob('*')) if source_root.exists() else []
    files = [file for file in files if file.is_file() and file.suffix.lower() == '.ogg']
    ffprobe = ffprobe or shutil.which('ffprobe')
    ffmpeg = ffmpeg or shutil.which('ffmpeg')
    reports = []
    for file in files:
        relative = file.relative_to(source_root).as_posix()
        try:
            inside(file, source_root)
            if not ffprobe or not ffmpeg: raise ValueError('缺少 ffprobe／ffmpeg；未进行实际解码。')
            metadata = inspect_audio(file, ffprobe, ffmpeg)
            if relative not in by_source:
                event_id = 'custom.' + hashlib.sha256(relative.encode('utf-8')).hexdigest()[:12]
                if event_id in by_id: raise ValueError('新事件 ID 与现有清单冲突，需要人工处理。')
                by_id.add(event_id)
                draft.append({'Id':event_id, 'DisplayName':file.stem, 'SourceFile':relative,
                    'Source':f'sounds/caorencup/{event_id.replace(".", "_")}.vsnd_c',
                    'NativeEvent':False, 'Category':'custom', 'Channel':'Effect', 'DefaultVolume':1.0, 'Loop':False})
            reports.append({'SourceFile':relative, 'Valid':True, **metadata})
        except Exception as error:
            reports.append({'SourceFile':relative, 'Valid':False, 'Error':str(error)})
    for relative in by_source:
        try:
            file = inside(source_root / relative, source_root)
            if not file.is_file(): raise ValueError('已有登记引用的源文件不存在。')
        except Exception as error:
            reports.append({'SourceFile':relative, 'Valid':False, 'Error':str(error)})
    output_root.mkdir(parents=True)
    (output_root / 'audio-events.draft.json').write_text(json.dumps(draft,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    report={'Success':all(item['Valid'] for item in reports), 'Sources':reports,
        'ExistingCatalogPreserved':str(catalog_path), 'NoAudioInputs':not bool(files), 'Built':False}
    (output_root / 'scan-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return report

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',type=Path,default=REPO/'game-plugin/Features/InGameMenu/audio-source')
    parser.add_argument('--catalog',type=Path,default=REPO/'game-plugin/Features/InGameMenu/audio-events.json')
    parser.add_argument('--output',type=Path,default=REPO/'release-build/ingamemenu-audio'/('scan-'+datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S-%f')))
    parser.add_argument('--ffprobe'); parser.add_argument('--ffmpeg')
    args=parser.parse_args()
    try:
        report=scan(args.source,args.catalog,args.output,args.ffprobe,args.ffmpeg)
        print(json.dumps({'Output':str(args.output),'Success':report['Success'],'NoAudioInputs':report['NoAudioInputs']},ensure_ascii=False))
        return 0 if report['Success'] else 1
    except Exception as error:
        print(str(error),file=sys.stderr); return 1
if __name__ == '__main__': sys.exit(main())
