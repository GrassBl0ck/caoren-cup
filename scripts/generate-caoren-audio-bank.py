"""从已确认登记与扫描报告生成原生事件和时长清单，不修改源素材。"""
import argparse
import json
import math
from pathlib import Path
import re

HEADER = '<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->\n'

def generate(entries, scan):
    measurements = {item['SourceFile']: item for item in scan['Sources'] if item.get('Valid')}
    assets, runtime, events, stems = [], [], [], set()
    for entry in entries:
        if not entry.get('SourceFile'):
            runtime.append(dict(entry)); continue
        event_id = entry['Id']
        if not re.fullmatch(r'[a-z0-9]+(?:[._-][a-z0-9]+)*', event_id): raise ValueError('无效事件 ID。')
        stem = event_id.replace('.', '_')
        if stem in stems: raise ValueError('事件 ID 生成的资源文件名冲突。')
        stems.add(stem)
        duration = float(measurements[entry['SourceFile']]['DurationSeconds'])
        if not math.isfinite(duration) or duration <= 0: raise ValueError('缺少有效音频时长。')
        main = f'sounds/caorencup/{stem}.vsnd'
        alternate = f'sounds/caorencup/{stem}_once.vsnd' if entry['Loop'] else f'sounds/caorencup/{stem}_loop.vsnd'
        once, loop = (alternate, main) if entry['Loop'] else (main, alternate)
        name = 'caorencup.audio.' + event_id
        assets.append(dict(Id=event_id, DurationSeconds=duration, SoundEvent=name, LoopSoundEvent=name+'.loop',
            Resources=[once, loop], SourceFile=entry['SourceFile']))
        runtime.append(dict(Id=event_id, DisplayName=entry['DisplayName'], Source=name, NativeEvent=True,
            DefaultVolume=entry['DefaultVolume'], Channel=entry['Channel'], Loop=entry['Loop']))
        for event_name, resource in ((name, once),(name+'.loop',loop)):
            events.append(f'''    "{event_name}" =
    {{
        type = "csgo_mega"
        volume = 1.0
        pitch = 1.0
        occlusion_intensity = 0.0
        distance_effect_mix = 0.0
        restrict_source_reverb = true
        reverb_wet = 0.0
        use_distance_unfiltered_stereo_mapping_curve = false
        use_time_volume_mapping_curve = false
        position_relative_to_player = true
        vsnd_files_track_01 = "{resource}"
    }}''')
    # 直接引用游戏原生素材，不重新分发 Valve 音频文件。
    for name,resource,volume,pitch in [('caorencup.ui.click','sounds/ui/panorama/submenu_select_01.vsnd',.3,1.2),
        ('caorencup.bridge.notice','sounds/training/bell_normal.vsnd',1,1)]:
        events.append(f'''    "{name}" = {{ type = "csgo_mega" volume = {volume} pitch = {pitch}
            mixgroup = "UI" position_relative_to_player = true reverb_wet = 0.0
            vsnd_files_track_01 = "{resource}" }}''')
    for percent in range(1,101):
        events.append(f'''    "caorencup.ui.hover{percent}" = {{ type = "csgo_mega" volume = {percent/1000:.3f}
            pitch = 1.4 mixgroup = "UI" position_relative_to_player = true reverb_wet = 0.0
            vsnd_files_track_01 = "sounds/ui/panorama/generic_scroll_01.vsnd" }}''')
    events.append('''    "caorencup.player.pain" = { type = "csgo_mega" volume = 1.0 pitch = 1.0
        position_relative_to_player = false vsnd_files_track_01 = "sounds/player/damage1.vsnd" }''')
    return runtime, assets, '\n'.join(events)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--catalog',required=True,type=Path)
    parser.add_argument('--scan',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    parser.add_argument('--additional',type=Path)
    args=parser.parse_args()
    args.output.mkdir() # 不复用输出目录
    entries=json.loads(args.catalog.read_text(encoding='utf-8-sig'))
    scan=json.loads(args.scan.read_text(encoding='utf-8-sig'))
    if not scan['Success']:raise ValueError('扫描报告未通过。')
    runtime,assets,body=generate(entries,scan)
    if args.additional:
        previous=args.additional.read_text(encoding='utf-8-sig')
        # 仅接受独立 KV3 根对象，在最外层末尾追加；保留旧探针事件。
        end=previous.rfind('}')
        if end<0 or previous[end+1:].strip():raise ValueError('附加事件文件的根对象无效。')
        for asset in assets:
            if f'"{asset["SoundEvent"]}"' in previous or f'"{asset["LoopSoundEvent"]}"' in previous:
                raise ValueError('自动事件与附加事件冲突。')
        bank=previous[:end]+body+'\n'+previous[end:]
    else:bank=HEADER+'{\n'+body+'\n}\n'
    for name,data in [('audio-events.json',runtime),('audio-assets.json',assets)]:
        (args.output/name).write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (args.output/'soundevents_addon.vsndevts').write_text(bank,encoding='utf-8')

if __name__=='__main__':main()
