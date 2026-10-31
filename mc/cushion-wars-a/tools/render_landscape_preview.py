#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""由隔离服实际导出的方块高度/材质生成投影预览；不是游戏截图。"""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "checks/linux-isolated/surface-preview.json"
OUT = ROOT / "dist/highland-preview.png"
palette = {
    "WHITE_CONCRETE": "#e0e2e0", "BLACK_CONCRETE": "#323638",
    "GOLD_BLOCK": "#f4c947", "STONE_BRICKS": "#8d9393",
    "SMOOTH_STONE": "#b5bbbc", "GRASS_BLOCK": "#a0a760",
    "STONE": "#888b84", "WATER": "#608ea1", "SAND": "#d7c997",
    "DIRT": "#977853", "ACACIA_LOG": "#9c775b", "ACACIA_FENCE": "#ad7959",
    "ACACIA_LEAVES": "#7b944f", "LANTERN": "#edbc66"
}
def rgb(c): return tuple(int(c[i:i+2], 16) for i in (1,3,5))
def shade(c, f): return tuple(int(v*f) for v in c)
def main():
    d = json.loads(DATA.read_text(encoding="utf-8"))
    image = Image.new("RGB", (1600,1150), "#e9e6de")
    draw = ImageDraw.Draw(image)
    font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 24)
    big = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 36)
    small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 20)
    draw.text((52,32), "坐垫战争 · 高原场景候验预览", font=big, fill="#28363b")
    draw.text((54,90), "真实高度采样的柱体示意 · 非客户端截图 · 景观效果待真人确认", font=font, fill="#566268")
    draw.text((54,132), f"种子 20261031  |  中心 {d['centerX']}, {d['centerZ']}  |  平台 Y={d['groundY']}", font=small, fill="#566268")
    # 粗采样地形与逐格场地使用同一投影。只展示导出数据，不虚构未采样悬崖细节。
    def raw(x,y,z): return ((x-z)*4.35, (x+z)*2.05-(y-62)*3.25)
    projected=[raw(x,y,z) for x,y,z,m in d["cells"]]
    lo_x=min(p[0] for p in projected)-20; hi_x=max(p[0] for p in projected)+20
    lo_y=min(p[1] for p in projected)-20; hi_y=max(p[1] for p in projected)+20
    scale=min(1450/(hi_x-lo_x),790/(hi_y-lo_y))
    def point(x,y,z):
        px,py=raw(x,y,z)
        return (75+(px-lo_x)*scale,200+(py-lo_y)*scale)
    fine={(x,z) for x,y,z,m in d["cells"][1089:]}
    cells=[]
    for index, (x,y,z,m) in enumerate(d["cells"]):
        if index < 1089 and (x,z) in fine: continue
        cells.append((x,y,z,m,4 if index<1089 else 1))
    for x,y,z,m,size in sorted(cells, key=lambda a:(a[0]+a[2],a[1])):
        c=rgb(palette.get(m,"#aaa585"))
        a,b,corn,e=[point(px,y,pz) for px,pz in ((x,z),(x+size,z),(x+size,z+size),(x,z+size))]
        artificial=m in {"WHITE_CONCRETE","BLACK_CONCRETE","GOLD_BLOCK","STONE_BRICKS","SMOOTH_STONE","ACACIA_FENCE","ACACIA_LOG","ACACIA_LEAVES","LANTERN"}
        depth=(2 if m=="STONE_BRICKS" else 1) if artificial else max(1,y-60)
        pb=point(x+size,y-depth,z);pc=point(x+size,y-depth,z+size);pe=point(x,y-depth,z+size)
        draw.polygon([b,corn,pc,pb],fill=shade(c,.73))
        draw.polygon([e,corn,pc,pe],fill=shade(c,.87))
        draw.polygon([a,b,corn,e],fill=c)
        if size==1:draw.line([a,b,corn,e,a],fill=shade(c,.86),width=1)
    draw.rounded_rectangle((45,1025,1555,1115),radius=16,fill="#fbfaf5")
    draw.text((65,1040), "中央金色9格为占领目标；相思木候场台位于棋盘外。背景不垫在棋盘下，保留缩圈效果。",font=font,fill="#38464b")
    draw.text((65,1080), "预览以实际材质和高度采样绘制，坐垫实体、镜头、光照与远景需在游戏内验收。",font=small,fill="#697274")
    image.save(OUT)
    print(OUT)
if __name__=="__main__":main()
