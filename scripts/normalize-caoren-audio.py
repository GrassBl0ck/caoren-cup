"""生成独立响度试听产物；原音频及当前构建默认值保持不变。"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import subprocess
import sys

REPO=Path(__file__).resolve().parents[1]

def inside(path):
    resolved=Path(path).resolve()
    if not resolved.is_relative_to(REPO): raise ValueError('输入／输出路径必须位于当前工作树内。')
    return resolved

def run(command):
    result=subprocess.run(command,capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=180)
    if result.returncode: raise ValueError(result.stderr[-4000:] or '音频处理失败。')
    return result

def measurement(ffmpeg,path,target,peak):
    result=run([ffmpeg,'-hide_banner','-nostats','-i',str(path),'-af',
        f'loudnorm=I={target}:TP={peak}:LRA=7:print_format=json','-f','null','-'])
    matches=re.findall(r'\{\s*"input_i".*?\}',result.stderr,re.S)
    if not matches: raise ValueError('没有取得 loudnorm 测量结果。')
    value=json.loads(matches[-1])
    if not all(math.isfinite(float(value[key])) for key in ['input_i','input_tp','input_lra','input_thresh']):
        raise ValueError('无法对无有效响度的音频进行归一化。')
    return value,result.stderr

def duration(ffprobe,path):
    result=run([ffprobe,'-v','error','-show_entries','format=duration','-of','json',str(path)])
    return float(json.loads(result.stdout)['format']['duration'])

def normalize(input_path,output_root,targets,peak,ffmpeg,ffprobe):
    input_path=inside(input_path); output_root=inside(output_root)
    if output_root.exists(): raise ValueError('输出目录已存在，停止以免覆盖旧产物。')
    if not targets or any(not math.isfinite(t) or not -70<=t<=-5 for t in targets): raise ValueError('响度目标超出 loudnorm 范围。')
    if not math.isfinite(peak) or not -9<=peak<=0: raise ValueError('真峰值目标超出 loudnorm 范围。')
    if len(set(targets))!=len(targets): raise ValueError('响度目标重复。')
    output_root.mkdir(parents=True)
    source_hash=hashlib.sha256(input_path.read_bytes()).hexdigest()
    # 先统一到最终编译时使用的 48kHz／双声道，再测响度，避免声道转换改变结果。
    reference=output_root/'reference-stereo.wav'
    run([ffmpeg,'-v','error','-i',str(input_path),'-map','0:a:0','-ar','48000','-ac','2','-c:a','pcm_s16le','-n',str(reference)])
    reference_duration=duration(ffprobe,reference)
    report={'Source':str(input_path),'SourceSha256':source_hash,'TruePeakTarget':peak,'DefaultProfileChanged':False,'Variants':[]}
    for target in targets:
        slug='lufs'+str(abs(target)).replace('.','_')
        stats,log=measurement(ffmpeg,reference,target,peak)
        (output_root/(slug+'-pass1.txt')).write_text(log,encoding='utf-8')
        filter_text=(f'loudnorm=I={target}:TP={peak}:LRA=7:linear=true:'
            f'measured_I={stats["input_i"]}:measured_TP={stats["input_tp"]}:'
            f'measured_LRA={stats["input_lra"]}:measured_thresh={stats["input_thresh"]}:'
            f'offset={stats["target_offset"]}:print_format=json')
        wav=output_root/(slug+'.wav')
        result=run([ffmpeg,'-hide_banner','-nostats','-i',str(reference),'-af',filter_text,
            '-ar','48000','-ac','2','-c:a','pcm_s16le','-n',str(wav)])
        (output_root/(slug+'-pass2.txt')).write_text(result.stderr,encoding='utf-8')
        ogg=output_root/(slug+'.ogg')
        run([ffmpeg,'-v','error','-i',str(wav),'-c:a','libvorbis','-q:a','6','-n',str(ogg)])
        final,final_log=measurement(ffmpeg,ogg,target,peak)
        (output_root/(slug+'-encoded-measurement.txt')).write_text(final_log,encoding='utf-8')
        final_duration=duration(ffprobe,ogg)
        error=abs(float(final['input_i'])-target)
        if error>0.5 or float(final['input_tp'])>peak+0.1 or abs(final_duration-reference_duration)>0.02:
            raise ValueError(f'{slug} 输出未达到响度／峰值／时长验收要求。')
        report['Variants'].append({'TargetLUFS':target,'MeasuredLUFS':float(final['input_i']),
            'TruePeakDBTP':float(final['input_tp']),'LoudnessRangeLU':float(final['input_lra']),
            'DurationSeconds':final_duration,'GainFromReferenceLU':target-float(stats['input_i']),
            'Wav':str(wav),'Ogg':str(ogg)})
    if hashlib.sha256(input_path.read_bytes()).hexdigest()!=source_hash: raise ValueError('原音频意外变化。')
    (output_root/'normalization-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return report

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input',type=Path,required=True); parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--targets',type=float,nargs='+',default=[-18,-16]); parser.add_argument('--peak',type=float,default=-1.5)
    parser.add_argument('--ffmpeg',required=True); parser.add_argument('--ffprobe',required=True)
    args=parser.parse_args()
    try:
        print(json.dumps(normalize(args.input,args.output,args.targets,args.peak,args.ffmpeg,args.ffprobe),ensure_ascii=False,indent=2)); return 0
    except Exception as error: print(str(error),file=sys.stderr); return 1
if __name__=='__main__': sys.exit(main())
