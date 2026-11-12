"""[草人杯] MIT. Read-only OCM choice inventory, never a migration writer.

The BSON document/string subset is independently implemented from BSON spec 1.1.
The expected shape is documented by the pinned OCM 2.8.0 storage classes.
"""
import argparse
from datetime import datetime
import hashlib
from pathlib import Path, PureWindowsPath
import json
import re
import struct
import sys

from check_evidence import file_hash, resolve_local, strict_json

MAX_MANIFEST_BYTES = 2 * 1024 * 1024
MAX_SOURCE_BYTES = 64 * 1024 * 1024
MAX_ELEMENTS = 200000
MAX_DEPTH = 8
MAX_STRING_BYTES = 128 * 1024
UUID_PATTERN = re.compile(r"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\Z")
HASH_PATTERN = re.compile(r"[0-9a-fA-F]{64}\Z")


class BsonError(ValueError):
    def __init__(self, code, offset):
        super().__init__(f"{code} at byte {offset}")
        self.code = code
        self.offset = offset


def decode_ocm_bson(data):
    """Decode only documents and strings; reject rather than skip other BSON types."""
    if not isinstance(data, bytes) or len(data) > MAX_SOURCE_BYTES:
        raise BsonError("BSON_LIMIT", 0)
    elements = 0

    def document(start, boundary, depth):
        nonlocal elements
        if depth > MAX_DEPTH:
            raise BsonError("BSON_LIMIT", start)
        if start + 4 > boundary:
            raise BsonError("BSON_INVALID", start)
        size = struct.unpack_from("<i", data, start)[0]
        end = start + size
        if size < 5 or end > boundary or data[end - 1] != 0:
            raise BsonError("BSON_INVALID", start)
        result = {}
        position = start + 4
        while position < end - 1:
            tag = data[position]
            position += 1
            key_end = data.find(b"\0", position, end - 1)
            if key_end < 0 or key_end - position > MAX_STRING_BYTES:
                raise BsonError("BSON_INVALID", position)
            try:
                key = data[position:key_end].decode("utf-8")
            except UnicodeError as error:
                raise BsonError("BSON_INVALID", position) from error
            if key in result:
                raise BsonError("BSON_INVALID", position)
            position = key_end + 1
            elements += 1
            if elements > MAX_ELEMENTS:
                raise BsonError("BSON_LIMIT", position)
            if tag == 3:
                value, position = document(position, end - 1, depth + 1)
            elif tag == 2:
                if position + 4 > end - 1:
                    raise BsonError("BSON_INVALID", position)
                length = struct.unpack_from("<i", data, position)[0]
                position += 4
                if length < 1 or position + length > end - 1:
                    raise BsonError("BSON_INVALID", position)
                if length > MAX_STRING_BYTES:
                    raise BsonError("BSON_LIMIT", position)
                if data[position + length - 1] != 0:
                    raise BsonError("BSON_INVALID", position)
                try:
                    value = data[position:position + length - 1].decode("utf-8")
                except UnicodeError as error:
                    raise BsonError("BSON_INVALID", position) from error
                position += length
            else:
                raise BsonError("BSON_UNSUPPORTED_TYPE", position - 1)
            result[key] = value
        if position != end - 1:
            raise BsonError("BSON_INVALID", position)
        return result, end

    result, end = document(0, len(data), 1)
    if end != len(data):
        raise BsonError("BSON_INVALID", end)
    return result


def contained_source(root, relative):
    if not isinstance(relative, str) or not relative or "\0" in relative:
        raise ValueError("relative file required")
    parts = Path(relative.replace("\\", "/"))
    if parts.is_absolute() or PureWindowsPath(relative).drive or ".." in parts.parts or ":" in relative:
        raise ValueError("relative file required")
    path = resolve_local(root / parts)
    if not path.is_relative_to(root) or path == root:
        raise ValueError("outside input directory")
    return path


def read_limited(path, limit):
    with path.open("rb") as stream:
        data = stream.read(limit + 1)
    if len(data) > limit:
        raise OverflowError("input limit exceeded")
    return data


class Inventory:
    def __init__(self):
        self.diagnostics = []
        self.omitted = 0
        self.severities = set()
        self.choices = []
        self.players = []
        self.inputs = {}
        self.purpose = None
        self.source = None
        self.profiles = {}
        self.worlds = {}

    def issue(self, code, location, message, severity="REVIEW"):
        self.severities.add(severity)
        if len(self.diagnostics) < 200:
            self.diagnostics.append(dict(code=code, location=location, message=message, severity=severity))
        else:
            self.omitted += 1

    def uuid(self, raw, location):
        if not isinstance(raw, str) or not UUID_PATTERN.fullmatch(raw):
            self.issue("UUID_INVALID", location, "UUID不符合完整格式；保留原值，禁止猜测或修复。")
            return None
        return raw.lower()

    def manifest(self, path):
        raw = read_limited(path, MAX_MANIFEST_BYTES)
        self.inputs[path] = hashlib.sha256(raw).hexdigest()
        obj = strict_json(raw.decode("utf-8-sig"))
        json.dumps(obj, ensure_ascii=False).encode("utf-8")  # Reject unpaired JSON surrogate escapes.
        if not isinstance(obj, dict):
            raise ValueError("manifest must be an object")
        if set(obj) != {"schemaVersion", "purpose", "source", "profiles", "worlds"}:
            raise ValueError("unknown or missing manifest field")
        if type(obj.get("schemaVersion")) is not int or obj["schemaVersion"] != 1:
            raise ValueError("unsupported schema")
        self.purpose = obj.get("purpose")
        if self.purpose not in ("SYNTHETIC_FIXTURE", "REAL_COPY"):
            raise ValueError("unsupported purpose")
        source = obj.get("source")
        if not isinstance(source, dict) or set(source) != {
                "path", "sha256", "ocmVersion", "ocmJarSha256", "configSha256", "snapshotState", "capturedAt"}:
            raise ValueError("invalid source fields")
        self.source = dict(source)
        if source.get("ocmVersion") != "2.8.0":
            self.issue("OCM_VERSION_UNSUPPORTED", "source.ocmVersion", "仅识别已审阅的2.8.0数据形状。", "ERROR")
        for key in ("sha256", "ocmJarSha256", "configSha256"):
            if not isinstance(source[key], str) or not HASH_PATTERN.fullmatch(source[key]):
                raise ValueError("explicit SHA-256 required")
        try:
            when = datetime.fromisoformat(source["capturedAt"].replace("Z", "+00:00"))
            if when.utcoffset() is None:
                raise ValueError("timezone required")
        except (ValueError, TypeError, AttributeError, OverflowError) as error:
            raise ValueError("capture timestamp with timezone required") from error
        state = source["snapshotState"]
        if state not in ("SYNTHETIC", "CLOSED_COPY", "LIVE_COPY", "UNKNOWN"):
            raise ValueError("invalid snapshot state")
        if (self.purpose == "SYNTHETIC_FIXTURE") != (state == "SYNTHETIC"):
            self.issue("PROVENANCE_CONFLICT", "source.snapshotState", "合成/真实来源声明与快照状态冲突。", "ERROR")
        elif self.purpose == "REAL_COPY" and state != "CLOSED_COPY":
            self.issue("SNAPSHOT_NOT_CLOSED", "source.snapshotState", "不是已关闭来源的副本，可能遗漏尚未保存的选择；须重新采集一致快照。")
        profiles = obj["profiles"]
        if not isinstance(profiles, dict) or set(profiles) != {"old", "modern"} or any(
                not isinstance(v, str) or not v or v != v.strip() or len(v) > 256 for v in profiles.values()):
            raise ValueError("explicit old and modern preset names required")
        if profiles["old"] == profiles["modern"]:
            raise ValueError("preset names must differ")
        self.profiles = {v: k for k, v in profiles.items()}
        worlds = obj["worlds"]
        if not isinstance(worlds, list) or len(worlds) > 20000:
            raise ValueError("invalid worlds catalog")
        names = set()
        for i, item in enumerate(worlds):
            if not isinstance(item, dict) or set(item) != {"uuid", "name"}:
                raise ValueError("invalid world record")
            uid = self.uuid(item["uuid"], f"worlds[{i}].uuid")
            name = item["name"]
            if uid is None or not isinstance(name, str) or not name or name != name.strip() or len(name) > 256:
                raise ValueError("invalid world identity")
            if uid in self.worlds or name in names:
                self.issue("WORLD_CATALOG_CONFLICT", f"worlds[{i}]", "世界UUID或名称重复；不选择其中一个映射。", "ERROR")
            else:
                self.worlds[uid] = name
                names.add(name)
        try:
            source_path = contained_source(path.parent, source["path"])
            if source_path == path:
                raise ValueError("manifest is not a BSON source")
        except (OSError, ValueError, RuntimeError):
            self.issue("SOURCE_PATH_INVALID", "source.path", "只允许清单目录内的普通本地文件，拒绝上级路径、网络/设备路径和数据流。", "ERROR")
            return None
        return source_path

    def inspect(self, document):
        seen_players = set()
        for pindex, (raw_player, player) in enumerate(document.items()):
            location = f"players[{pindex}]"
            uid = self.uuid(raw_player, location)
            if uid is not None:
                if uid in seen_players:
                    self.issue("PLAYER_UUID_COLLISION", location, "多个原始玩家键指向同一UUID；全部保留，迁移前必须处理。")
                seen_players.add(uid)
            entry = dict(playerId=uid, rawPlayerId=raw_player, savedChoices=None, unknownFields=[])
            self.players.append(entry)
            if not isinstance(player, dict):
                self.issue("PLAYER_SHAPE_INVALID", location, "玩家值必须为文档。", "ERROR")
                continue
            entry["unknownFields"] = sorted(set(player) - {"modesetByWorld"})
            if entry["unknownFields"]:
                self.issue("UNKNOWN_PLAYER_FIELD", location, "存在未识别的玩家字段；原始文件保留，不把这些字段丢弃后迁移。")
            if "modesetByWorld" not in player:
                self.issue("MODESET_FIELD_MISSING", location, "缺modesetByWorld字段；不能自动当作已保存modern。")
                continue
            saved = player["modesetByWorld"]
            if not isinstance(saved, dict):
                self.issue("MODESET_SHAPE_INVALID", location, "modesetByWorld必须为文档。", "ERROR")
                continue
            entry["savedChoices"] = len(saved)
            if not saved:
                self.issue("NO_SAVED_SELECTION", location, "该玩家没有持久化选择；有效默认行为仍依赖原配置。", "NOTE")
            seen_worlds = set()
            for windex, (raw_world, modeset) in enumerate(saved.items()):
                row_location = f"{location}.choices[{windex}]"
                world = self.uuid(raw_world, row_location)
                if world is not None:
                    if world in seen_worlds:
                        self.issue("WORLD_UUID_COLLISION", row_location, "同一玩家有多个键指向同一世界；不合并选择。")
                    seen_worlds.add(world)
                if not isinstance(modeset, str):
                    self.issue("MODESET_TYPE_INVALID", row_location, "预设值必须为字符串；未推断该选择。", "ERROR")
                    continue
                profile = self.profiles.get(modeset)
                name = self.worlds.get(world)
                self.choices.append(dict(playerId=uid, rawPlayerId=raw_player, worldId=world,
                    rawWorldId=raw_world, worldName=name, rawModeset=modeset, profile=profile))
                if profile is None:
                    self.issue("UNKNOWN_MODESET", row_location, "预设不在显式old/modern映射中；保留原名并阻塞迁移。")
                if name is None:
                    self.issue("WORLD_UNMAPPED", row_location, "世界UUID没有明确名称对应；保留原UUID，不猜当前世界。")

    def run(self, raw_path):
        try:
            path = resolve_local(raw_path)
        except (ValueError, OSError, RuntimeError):
            self.issue("MANIFEST_PATH_INVALID", "manifest", "清单必须是普通本地文件。", "ERROR")
            return self.finish()
        try:
            source_path = self.manifest(path)
        except (ValueError, OSError, UnicodeError, OverflowError, RecursionError):
            self.issue("MANIFEST_INVALID", "manifest", "清单无法读取、超限、不是严格UTF-8 JSON或缺必填字段。", "ERROR")
            source_path = None
        if source_path is not None and "ERROR" not in self.severities:
            try:
                data = read_limited(source_path, MAX_SOURCE_BYTES)
                digest = hashlib.sha256(data).hexdigest()
                self.inputs[source_path] = digest
                if digest.lower() != self.source["sha256"].lower():
                    self.issue("HASH_MISMATCH", "source.sha256", "实际文件SHA-256与清单不符；不解析该文件。", "ERROR")
                else:
                    self.inspect(decode_ocm_bson(data))
            except OverflowError:
                self.issue("SOURCE_TOO_LARGE", "source", "BSON超过64MiB上限。", "ERROR")
            except BsonError as error:
                self.issue(error.code, f"source:byte:{error.offset}", "BSON损坏、超限或含本工具未支持的类型；没有跳过该字段继续迁移。", "ERROR")
            except OSError:
                self.issue("SOURCE_UNREADABLE", "source", "无法读取明确指定的BSON文件。", "ERROR")
        for file, before in self.inputs.items():
            try:
                if file_hash(file) != before:
                    self.issue("INPUT_CHANGED", "source" if file != path else "manifest", "执行期间输入文件发生变化；该次清单不可用于迁移。", "ERROR")
            except OSError:
                self.issue("INPUT_CHANGED", "source" if file != path else "manifest", "执行后无法复核输入文件。", "ERROR")
        return self.finish()

    def finish(self):
        status = "FORMAT_ERROR" if "ERROR" in self.severities else (
            "REVIEW_REQUIRED" if "REVIEW" in self.severities else "COMPLETE")
        return dict(schemaVersion=1, inventoryStatus=status, scope="DECLARED_SNAPSHOT_ONLY",
            migrationExecuted=False, migrationReady=False, userAcceptance="PENDING",
            captureAuthenticity="NOT_VERIFIED", purpose=self.purpose,
            evidenceEligibility="NOT_ELIGIBLE_SYNTHETIC" if self.purpose == "SYNTHETIC_FIXTURE" else "MANUAL_REVIEW_REQUIRED",
            source=self.source, counts=dict(players=len(self.players), choices=len(self.choices),
                old=sum(x["profile"] == "old" for x in self.choices),
                modern=sum(x["profile"] == "modern" for x in self.choices),
                unknown=sum(x["profile"] is None for x in self.choices),
                noSavedChoice=sum(x["savedChoices"] == 0 for x in self.players)),
            players=self.players, choices=self.choices, diagnostics=self.diagnostics,
            omittedDiagnostics=self.omitted, inputHashes=[dict(name=p.name, sha256=h) for p, h in self.inputs.items()],
            limits=dict(maxSourceBytes=MAX_SOURCE_BYTES, maxDepth=MAX_DEPTH, maxElements=MAX_ELEMENTS),
            limitations=["Only persisted per-player, per-world modeset strings are inventoried.",
                "Overrides, effective defaults, world identity and provenance require separate review.",
                "Declared build/config hashes and profile mappings are not independently authenticated.",
                "No destination storage, migration, deletion, repair or gameplay acceptance is performed."])


def inventory_choices(path):
    return Inventory().run(path)


def main():
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="[草人杯] OCM选择只读清单；不执行迁移。")
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        manifest = resolve_local(args.manifest)
        output = resolve_local(args.output)
        if not output.is_relative_to(manifest.parent) or not output.parent.is_dir() or output.exists():
            raise ValueError("output must be a new file within the manifest directory")
        report = inventory_choices(manifest)
        with output.open("x", encoding="utf-8", newline="\n") as stream:
            json.dump(report, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
    except (OSError, ValueError, RuntimeError):
        print("[草人杯] 输出失败：必须是清单目录内的新文件，不能覆盖或越界。", file=sys.stderr)
        return 2
    print(f"[草人杯] {report['inventoryStatus']}; players={report['counts']['players']}; "
          f"choices={report['counts']['choices']}; migrationExecuted=false")
    return {"COMPLETE": 0, "REVIEW_REQUIRED": 1, "FORMAT_ERROR": 2}[report["inventoryStatus"]]


if __name__ == "__main__":
    sys.exit(main())
