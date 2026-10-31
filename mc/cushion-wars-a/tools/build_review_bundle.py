#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""候验包：业务JAR、辅助包、候选地图、预览及实测说明；不自动安装。"""
import hashlib
import json
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
def main():
    entries={}
    def add(source,target):entries[target]=(ROOT/source).read_bytes()
    add("dist/CushionWarsProbe-0.1.0.jar","plugins/CushionWarsProbe-0.1.0.jar")
    add("dist/cushionwars-a-datapack-0.1.0-static.zip","maps/cushion-wars/world/datapacks/cushionwars-a-datapack-0.1.0-static.zip")
    for p in (ROOT/"deploy/server/cw-entry").rglob("*"):
        if p.is_file():entries["maps/cushion-wars/world/datapacks/cw-entry/"+p.relative_to(ROOT/"deploy/server/cw-entry").as_posix()]=p.read_bytes()
    for source,target in [
        ("handoffs/TASK-CW-A-002-report.md","docs/implementation-report.md"),
        ("docs/13-试玩修复与本地验收.md","docs/local-validation.md"),
        ("docs/14-高原景观生成方案.md","docs/landscape-plan.md"),
        ("docs/15-Linux隔离验收方案.md","docs/linux-isolated-test-plan.md"),
        ("docs/16-第二轮试玩修订与上线方案.md","docs/feedback-r2-and-deployment-plan.md"),
        ("checks/CW-A-002-verification.json","verification.json"),
        ("checks/feedback-r2/results.json","evidence/current-feedback-results.json"),
        ("checks/feedback-r2/cleanup.json","evidence/current-cleanup.json"),
        ("checks/feedback-r2/evidence/proof.json","evidence/current-proof.json"),
        ("checks/linux-isolated/smoke-results.json","evidence/previous-round-smoke-results.json"),
        ("checks/linux-isolated/import-results.json","evidence/previous-round-import-results.json"),
        ("dist/cushion-wars-a-highland-candidate-20261031.zip","candidates/highland-world.zip"),
        ("dist/highland-preview.png","candidates/highland-preview.png")]:add(source,target)
    with zipfile.ZipFile(ROOT/"dist/CushionWarsProbe-0.1.0.jar") as z:
        assert not any("SmokeProbe" in n or "ImportProbe" in n or "FeedbackProbe" in n or "LandscapeBuilder" in n for n in z.namelist())
    entries["SHA256.json"]=(json.dumps({n:hashlib.sha256(data).hexdigest() for n,data in sorted(entries.items())},indent=2)+"\n").encode()
    output=ROOT/"dist/cushion-wars-a-20261031-local-review.zip"
    with zipfile.ZipFile(output,"w",zipfile.ZIP_DEFLATED) as z:
        for n,data in sorted(entries.items()):
            i=zipfile.ZipInfo(n,(2026,1,1,0,0,0));i.compress_type=zipfile.ZIP_DEFLATED;i.external_attr=0o644<<16
            z.writestr(i,data)
    with zipfile.ZipFile(output) as z:
        assert z.testzip() is None
        manifest=json.loads(z.read("SHA256.json"))
        assert all(hashlib.sha256(z.read(n)).hexdigest()==s for n,s in manifest.items())
    print(output)
    print(hashlib.sha256(output.read_bytes()).hexdigest())
if __name__=="__main__":main()
