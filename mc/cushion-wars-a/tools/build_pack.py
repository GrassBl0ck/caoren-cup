#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""构建交付 ZIP（S 级，纯文件操作，不启动游戏）。

产物：
  dist/cushionwars-a-datapack-<版本>.zip        正式包（可直接放进 world/datapacks）
  dist/cushionwars-a-p0-probes-<版本>.zip       P0 探针包（隔离使用）
  dist/SHA256SUMS.txt                          两个 ZIP 的 SHA-256
  dist/BUILD-MANIFEST.json                     每个 ZIP 内文件清单、字节数与哈希

为保证可复现：ZIP 内时间戳固定为 2026-01-01 00:00:00，条目顺序按路径排序。
"""

from __future__ import annotations

import hashlib
import json
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = "0.1.0-static"
DIST = ROOT / "dist"
FIXED_TIME = (2026, 1, 1, 0, 0, 0)

TARGETS = {
    "cushionwars-a-datapack": ROOT / "src" / "round-a",
    "cushionwars-a-p0-probes": ROOT / "prototypes" / "round-a-p0",
}
EXCLUDE_SUFFIX = (".pyc", ".zip", ".log")
EXCLUDE_PARTS = ("__pycache__", ".git", "backups")

# 正式包必须存在的入口，防止把空目录打成“成品”。
REQUIRED_ENTRIES = {
    "cushionwars-a-datapack": ("pack.mcmeta", "data/cushionwars_a/tags/function/load.json",
                               "data/cushionwars_a/function/arena/init.mcfunction",
                               "data/cushionwars_a/function/arena/prepare.mcfunction",
                               "data/cushionwars_a/function/arena/gen/n4/prepare.mcfunction",
                               "data/cushionwars_a/function/arena/gen/n8/prepare.mcfunction",
                               "data/cushionwars_a/function/arena/gen/common/clear_max.mcfunction"),
    "cushionwars-a-p0-probes": ("pack.mcmeta", "data/cw_p0/tags/function/load.json",
                                "data/cw_p0/function/probe/load.mcfunction"),
}


def collect(root: Path) -> list[Path]:
    files = []
    for path in sorted(root.rglob("*")):
        if not path.is_file():
            continue
        if path.suffix in EXCLUDE_SUFFIX:
            continue
        if any(part in EXCLUDE_PARTS for part in path.parts):
            continue
        files.append(path)
    return files


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def build(name: str, root: Path) -> dict:
    files = collect(root)
    names = [p.relative_to(root).as_posix() for p in files]
    missing = [entry for entry in REQUIRED_ENTRIES[name] if entry not in names]
    if missing:
        raise SystemExit(f"{name} 缺少必需入口：{missing}")

    out_path = DIST / f"{name}-{VERSION}.zip"
    DIST.mkdir(parents=True, exist_ok=True)
    manifest_entries = []
    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for path, arcname in zip(files, names):
            data = path.read_bytes()
            if arcname.endswith((".mcfunction", ".json", ".mcmeta", ".md")):
                text = data.decode("utf-8")
                data = text.replace("\r\n", "\n").encode("utf-8")
            info = zipfile.ZipInfo(arcname, date_time=FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, data)
            manifest_entries.append({"path": arcname, "bytes": len(data), "sha256": sha256(data)})

    payload = out_path.read_bytes()
    return {
        "zip": out_path.name,
        "bytes": len(payload),
        "sha256": sha256(payload),
        "file_count": len(manifest_entries),
        "files": manifest_entries,
    }


def main() -> int:
    results = {}
    for name, root in TARGETS.items():
        results[name] = build(name, root)

    sums = "".join(f"{info['sha256']}  {info['zip']}\n" for info in results.values())
    (DIST / "SHA256SUMS.txt").write_text(sums, encoding="utf-8", newline="\n")

    manifest = {
        "schema": "cushion-wars/build-manifest@1",
        "version": VERSION,
        "note": "纯静态交付：未启动 Minecraft、服务端或 Java。ZIP 仅含数据包文本，不含世界存档。",
        "builds": results,
    }
    (DIST / "BUILD-MANIFEST.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
    )

    for name, info in results.items():
        print(f"{info['zip']}  {info['bytes']} 字节  {info['file_count']} 个文件  {info['sha256'][:16]}…")
    print(f"校验清单：{(DIST / 'SHA256SUMS.txt').relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
