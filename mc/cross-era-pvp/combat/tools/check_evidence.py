"""[草人杯] MIT. Read-only local evidence checks, never combat calculations or acceptance."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path, PureWindowsPath
import re
import stat
import sys

MAX_MANIFEST_BYTES = 2 * 1024 * 1024
MAX_TEXT_BYTES = 64 * 1024 * 1024
MAX_LINE_BYTES = 2 * 1024 * 1024
CASE_ID = re.compile(r"C04-(OO|MM|OM|MO)-(N|D)-(S0[1-8]|B0[5-7])\Z")
IDENTIFIER = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,95}\Z")
SHA256 = re.compile(r"[a-fA-F0-9]{64}\Z")
UUID = re.compile(r"[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}\Z")
KINDS = {"journal", "console", "input", "context", "observation", "baseline", "criteria"}
EVENTS = {"startup", "shutdown", "damage_event", "health_followup", "velocity_event",
          "player_state", "ocm_modeset_change"}
SOURCES = {"REAL_PLAYER", "COMMAND", "SYNTHETIC_EVENT", "UNIT_STUB"}
COUNTERS = ("accepted", "rejected", "errors", "queued", "writtenToBuffer")
PENDING = {"TBD", "NOT_RUN", "UNKNOWN", "待填写", "待确认", "待采集"}
FOLLOWUP_POLICY = "connection-world-life-v1"
LIFECYCLE_REASONS = {"NONE", "JOIN", "QUIT", "WORLD_CHANGE", "DEATH", "RESPAWN"}
FOLLOWUP_MEASUREMENTS = {"healthBeforeEventCommit", "healthAtSample", "netHealthDelta",
                         "netAbsorptionDelta", "deadAtSample", "velocityAtSample"}


def nonempty(value):
    return isinstance(value, str) and bool(value.strip()) and value.strip() not in PENDING


def number(value):
    return type(value) in (int, float) and (type(value) is int or math.isfinite(value))


def integer(value, minimum=0):
    return type(value) is int and value >= minimum


def strict_json(text):
    def pairs(items):
        obj = {}
        for key, value in items:
            if key in obj:
                raise ValueError("duplicate key")
            obj[key] = value
        return obj

    def finite(value):
        result = float(value)
        if not math.isfinite(result):
            raise ValueError("non-finite number")
        return result

    def constant(_):
        raise ValueError("non-finite constant")

    return json.loads(text, object_pairs_hook=pairs, parse_float=finite, parse_constant=constant)


def file_hash(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def resolve_local(path):
    raw = str(path)
    windows_path = PureWindowsPath(raw)
    if raw.startswith(("\\\\", "//")) or windows_path.drive.startswith("\\\\"):
        raise ValueError("UNC/device paths are not local evidence paths")
    tail = raw[len(windows_path.drive):] if windows_path.drive else raw
    if ":" in tail or any(PureWindowsPath(part).is_reserved() for part in windows_path.parts):
        raise ValueError("device names and alternate streams are not evidence files")
    absolute = Path(path).absolute()
    component = Path(absolute.anchor)
    for part in absolute.parts[1:]:
        component /= part
        try:
            metadata = component.lstat()
        except FileNotFoundError:
            break  # A new output leaf is allowed; its parent is checked by the CLI.
        if stat.S_ISLNK(metadata.st_mode) or getattr(metadata, "st_file_attributes", 0) & 0x400:
            raise ValueError("symbolic links and Windows reparse points are not evidence paths")
    resolved = absolute.resolve()
    if str(resolved).startswith(("\\\\", "//")):
        raise ValueError("resolved path is not local")
    return resolved


class Checker:
    def __init__(self, path):
        self.issues = []
        self.omitted = 0
        self.severities = set()
        try:
            self.path = resolve_local(path)
            self.root = self.path.parent
        except (OSError, ValueError, RuntimeError):
            self.path = None
            self.root = None
            self.issue("MANIFEST_PATH_INVALID", "manifest", "只接收可解析的本地路径，不读取UNC/网络或设备路径。", "ERROR")
        self.files = {}
        self.texts = {}
        self.inputs = {}
        self.sessions = {}
        self.manifest = {}
        self.case_counts = {"RECORDED": 0, "NOT_RUN": 0}

    def issue(self, code, where, message, severity="MISSING"):
        self.severities.add(severity)
        if len(self.issues) < 200:
            self.issues.append(dict(code=code, location=where, message=message, severity=severity))
        else:
            self.omitted += 1

    def mapping(self, value, where):
        if not isinstance(value, dict):
            self.issue("TYPE_INVALID", where, "须为JSON对象。", "ERROR")
            return {}
        return value

    def sequence(self, value, where):
        if not isinstance(value, list):
            self.issue("TYPE_INVALID", where, "须为JSON数组。", "ERROR")
            return []
        return value

    def fields(self, obj, names, predicate, where):
        for name in names:
            if not predicate(obj.get(name)):
                self.issue("FIELD_INVALID", where + "." + name, "必填字段缺失或类型/取值不符。", "ERROR")

    def timestamp(self, value, where):
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
            if parsed.utcoffset() is None:
                raise ValueError("timezone required")
        except (ValueError, TypeError, AttributeError, OverflowError):
            self.issue("TIMESTAMP_INVALID", where, "时间必须为包含时区的ISO格式。", "ERROR")

    def parse(self, text, where):
        try:
            return strict_json(text)
        except (ValueError, RecursionError):
            self.issue("JSON_INVALID", where, "JSON损坏、重复键或含非有限数；未采信该记录。", "ERROR")
            return None

    def schema(self, obj, where):
        if type(obj.get("schemaVersion")) is not int or obj["schemaVersion"] != 1:
            self.issue("SCHEMA_UNSUPPORTED", where, "仅支持schemaVersion整数1，不猜测其他版本。", "ERROR")
            return False
        return True

    def load_manifest(self):
        try:
            if self.path.stat().st_size > MAX_MANIFEST_BYTES:
                self.issue("FILE_TOO_LARGE", "manifest", "清单超过2MiB读取上限。", "ERROR")
                return False
            data = self.path.read_bytes()
            self.inputs[self.path] = hashlib.sha256(data).hexdigest()
            parsed = self.parse(data.decode("utf-8-sig"), "manifest")
        except (OSError, UnicodeError):
            self.issue("MANIFEST_UNREADABLE", "manifest", "清单不可读取或不是UTF-8。", "ERROR")
            return False
        self.manifest = self.mapping(parsed, "manifest")
        return self.schema(self.manifest, "manifest")

    def local_path(self, value, where):
        if not isinstance(value, str) or not value or "\x00" in value:
            self.issue("PATH_INVALID", where, "证据路径须为非空相对路径。", "ERROR")
            return None
        relative = Path(value.replace("\\", "/"))
        if relative.is_absolute() or PureWindowsPath(value).drive or ".." in relative.parts or ":" in value or any(PureWindowsPath(part).is_reserved() for part in relative.parts):
            self.issue("PATH_ESCAPE", where, "只允许清单目录内的相对文件，不允许上级路径或盘符。", "ERROR")
            return None
        try:
            path = resolve_local(self.root / relative)
            if not path.is_relative_to(self.root):
                raise ValueError("outside evidence root")
            return path
        except (OSError, ValueError, RuntimeError):
            self.issue("PATH_ESCAPE", where, "解析后的证据路径越界或链接不可解析。", "ERROR")
            return None

    def read_files(self):
        seen_paths = {self.path}
        for index, raw in enumerate(self.sequence(self.manifest.get("files"), "files")):
            where = f"files[{index}]"
            item = self.mapping(raw, where)
            identifier = item.get("id")
            if not isinstance(identifier, str) or not IDENTIFIER.fullmatch(identifier):
                self.issue("FILE_ID_INVALID", where, "文件ID须为不重复的短英文标识。", "ERROR")
                continue
            if identifier in self.files:
                self.issue("DUPLICATE_FILE_ID", where, "文件ID重复。", "ERROR")
                continue
            kind = item.get("kind")
            if not isinstance(kind, str) or kind not in KINDS:
                self.issue("FILE_KIND_INVALID", where, "文件用途不受支持。", "ERROR")
                continue
            path = self.local_path(item.get("path"), where)
            if path is None:
                continue
            if path in seen_paths:
                self.issue("DUPLICATE_FILE_PATH", where, "同一文件不能用多个ID或用途重复登记。", "ERROR")
                continue
            seen_paths.add(path)
            entry = dict(item, resolved=path, usable=False)
            self.files[identifier] = entry
            expected = item.get("sha256")
            if not isinstance(expected, str) or not SHA256.fullmatch(expected):
                self.issue("HASH_INVALID", where, "必须填写64位SHA-256。", "ERROR")
                continue
            try:
                if not path.is_file():
                    self.issue("FILE_UNREADABLE", where, "证据必须是已存在的普通文件。")
                    continue
                actual = file_hash(path)
                self.inputs[path] = actual
                if actual != expected.lower():
                    self.issue("HASH_MISMATCH", where, "证据字节与清单哈希不一致。")
                    continue
                if kind in {"journal", "console"}:
                    if path.stat().st_size > MAX_TEXT_BYTES:
                        self.issue("FILE_TOO_LARGE", where, "单个日志超过64MiB读取上限；须明确分批方案。", "ERROR")
                        continue
                    data = path.read_bytes()
                    if hashlib.sha256(data).hexdigest() != actual:
                        self.issue("INPUT_CHANGED", where, "读取期间证据发生变化。")
                        continue
                    if any(len(line) > MAX_LINE_BYTES for line in data.splitlines()):
                        self.issue("LINE_TOO_LARGE", where, "单行超过2MiB上限。", "ERROR")
                        continue
                    self.texts[identifier] = data.decode("utf-8-sig").splitlines()
                entry["usable"] = True
            except UnicodeError:
                self.issue("UTF8_INVALID", where, "日志不是有效UTF-8。", "ERROR")
            except OSError:
                self.issue("FILE_UNREADABLE", where, "显式引用的文件不存在或不可读取。")

    def vector(self, value, where, position=False):
        obj = self.mapping(value, where)
        self.fields(obj, ("x", "y", "z", "yaw", "pitch") if position else ("x", "y", "z"), number, where)

    def player(self, value, where):
        obj = self.mapping(value, where)
        self.fields(obj, ("uuid", "worldId"), lambda v: isinstance(v, str) and bool(UUID.fullmatch(v)), where)
        self.fields(obj, ("world",), nonempty, where)
        if "profileError" in obj:
            self.issue("PROFILE_ERROR", where, "日志未取得有效规则身份。")
        else:
            self.fields(obj, ("profile",), lambda v: isinstance(v, str) and v in {"old", "modern"}, where)
            self.fields(obj, ("modeset",), nonempty, where)
            self.fields(obj, ("stored", "overrides", "verifiedModules"), lambda v: type(v) is bool, where)
            if obj.get("overrides") is not False or obj.get("verifiedModules") is not True:
                self.issue("MODULES_UNVERIFIED", where, "存在覆盖或模块审计未确认一致。")
            modules = self.mapping(obj.get("effectiveModules"), where + ".effectiveModules")
            if not modules or any(type(v) is not bool for v in modules.values()):
                self.issue("MODULES_INVALID", where, "缺有效模块快照。", "ERROR")
        self.vector(obj.get("position"), where + ".position", position=True)
        self.vector(obj.get("velocity"), where + ".velocity")
        self.fields(obj, ("health", "absorption", "lastDamage", "attackCooldownObserved", "fallDistance"), number, where)
        self.fields(obj, ("noDamageTicks", "maximumNoDamageTicks"), integer, where)
        self.fields(obj, ("sprinting", "onGround", "inWater", "climbing", "insideVehicle", "blindness"), lambda v: type(v) is bool, where)
        self.sequence(obj.get("effects"), where + ".effects")
        self.item(obj.get("mainHand"), where + ".mainHand")
        armour = self.sequence(obj.get("armourBootsToHelmet"), where + ".armourBootsToHelmet")
        if len(armour) != 4:
            self.issue("ARMOUR_INVALID", where, "必须记录四个护甲槽。", "ERROR")
        for index, item in enumerate(armour):
            self.item(item, f"{where}.armour[{index}]")

    def item(self, value, where):
        obj = self.mapping(value, where)
        self.fields(obj, ("material",), nonempty, where)
        self.fields(obj, ("amount",), integer, where)
        if obj.get("material") != "AIR":
            self.fields(obj, ("enchanted",), lambda v: type(v) is bool, where)
        if "durabilityDamage" in obj:
            self.fields(obj, ("durabilityDamage",), integer, where)

    def stats(self, value, where):
        obj = self.mapping(value, where)
        self.fields(obj, COUNTERS, integer, where)
        self.fields(obj, ("workerAlive",), lambda v: type(v) is bool, where)
        if any(obj.get(k) != 0 for k in ("rejected", "errors")):
            self.issue("JOURNAL_LOSS", where, "日志存在拒绝记录或写入错误，不能视为完整。")
        return obj

    def read_journals(self):
        for file_id, entry in self.files.items():
            if entry["kind"] != "journal" or not entry["usable"]:
                continue
            current = None
            ordinal = 0
            for line_no, line in enumerate(self.texts[file_id], 1):
                where = f"{file_id}:line[{line_no}]"
                obj = self.mapping(self.parse(line, where), where)
                if not self.schema(obj, where):
                    continue
                self.timestamp(obj.get("timestamp"), where + ".timestamp")
                self.fields(obj, ("tick",), integer, where)
                kind = obj.get("kind")
                if not isinstance(kind, str) or kind not in EVENTS:
                    self.issue("EVENT_KIND_INVALID", where, "不支持的事件种类，未猜测其含义。", "ERROR")
                    continue
                if kind == "startup":
                    ordinal += 1
                    current = dict(hits={}, followups={}, records=1, closed=False,
                                   where=where, lastTick=obj.get("tick"))
                    self.sessions[(file_id, ordinal)] = current
                    current["followupPolicy"] = obj.get("followupPolicy")
                    if "followupPolicy" not in obj:
                        self.issue("LEGACY_LIFECYCLE_GUARD_ABSENT", where,
                                   "历史会话未声明生命周期保护；同连接/世界不能排除死亡复活或快速切图往返，仍须外部证据。", "NOTE")
                    elif obj["followupPolicy"] != FOLLOWUP_POLICY:
                        self.issue("FOLLOWUP_POLICY_UNSUPPORTED", where,
                                   "未知后续采样能力，不猜测其字段和保护效果。", "ERROR")
                    self.fields(obj, ("pluginVersion", "serverVersion", "javaVersion", "backend"), nonempty, where)
                    env = self.manifest.get("environment")
                    if isinstance(env, dict) and any(obj.get(k) != env.get(k) for k in ("serverVersion", "javaVersion")):
                        self.issue("ENVIRONMENT_MISMATCH", where, "startup的完整服务器/Java版本与清单不一致。")
                    if obj.get("fidelityAccepted") is not False:
                        self.issue("FIDELITY_CLAIM", where, "startup不得充当还原验收通过证明。")
                    continue
                if current is None:
                    self.issue("SESSION_MISSING", where, "记录不在startup/shutdown会话内，不能跨会话关联。")
                    continue
                current["records"] += 1
                if integer(obj.get("tick")) and integer(current["lastTick"]) and obj["tick"] < current["lastTick"]:
                    self.issue("TICK_ORDER", where, "同会话tick逆序。")
                current["lastTick"] = obj.get("tick")
                guarded = current.get("followupPolicy") == FOLLOWUP_POLICY
                if not guarded and any(key in obj for key in ("victimObservationEpoch", "sameObservationEpoch", "observationInvalidation")):
                    self.issue("UNDECLARED_FOLLOWUP_POLICY", where, "记录使用生命周期字段，但startup没有声明相应能力。", "ERROR")
                if kind in {"damage_event", "health_followup"}:
                    event_id = obj.get("eventId")
                    if not integer(event_id, 1):
                        self.issue("EVENT_ID_INVALID", where, "必须有正整数eventId。", "ERROR")
                        continue
                    group = current["hits" if kind == "damage_event" else "followups"]
                    if event_id in group:
                        self.issue("DUPLICATE_EVENT", where, "同会话存在重复伤害或后续采样ID。")
                    else:
                        group[event_id] = (obj, where, line_no)
                if kind == "damage_event":
                    if guarded:
                        self.fields(obj, ("victimObservationEpoch",), lambda v: integer(v, 1), where)
                    self.fields(obj, ("cause",), nonempty, where)
                    self.fields(obj, ("cancelled",), lambda v: type(v) is bool, where)
                    self.fields(obj, ("eventBaseDamage", "eventFinalDamage"), number, where)
                    if obj.get("samplePhase") != "MONITOR_EVENT_VALUES_NOT_ACTUAL_HEALTH_LOSS" or obj.get("inputProvenance") != "BUKKIT_EVENT_NATIVE_OR_SYNTHETIC_UNDETERMINED":
                        self.issue("OBSERVATION_SEMANTICS", where, "事件来源/伤害采样声明不符合当前日志契约。", "ERROR")
                    self.player(obj.get("attacker"), where + ".attacker")
                    self.player(obj.get("victim"), where + ".victim")
                elif kind == "health_followup":
                    self.fields(obj, ("windowTicks",), integer, where)
                    self.fields(obj, ("sameConnectionAndWorld",), lambda v: type(v) is bool, where)
                    valid_life = True
                    if guarded:
                        self.fields(obj, ("victimObservationEpoch",), lambda v: integer(v, 1), where)
                        self.fields(obj, ("sameObservationEpoch",), lambda v: type(v) is bool, where)
                        valid_life = obj.get("sameObservationEpoch") is True
                        reason = obj.get("observationInvalidation")
                        if not isinstance(reason, str) or reason not in LIFECYCLE_REASONS or valid_life != (reason == "NONE"):
                            self.issue("LIFECYCLE_STATE_INVALID", where, "生命周期有效标志和失效原因缺失、不认识或互相矛盾。", "ERROR")
                        if not valid_life:
                            self.issue("LIFECYCLE_CHANGED", where, "窗口内发生加入/退出/死亡/复活/换世界；不能作为同一生命的净HP样本。")
                        if (not valid_life or obj.get("sameConnectionAndWorld") is not True) and FOLLOWUP_MEASUREMENTS.intersection(obj):
                            self.issue("INVALIDATED_MEASUREMENTS", where, "失效窗口仍携带HP/吸收/速度采样，违反已声明协议。", "ERROR")
                    if obj.get("isolatedHitGuaranteed") is not False:
                        self.issue("CAUSALITY_CLAIM", where, "现有日志不保证单次攻击归因。", "ERROR")
                    if obj.get("sameConnectionAndWorld") is not True:
                        self.issue("CONNECTION_CHANGED", where, "后续采样连接或世界已变，不能用作同一受击生命证据。")
                    elif valid_life:
                        self.fields(obj, ("healthBeforeEventCommit", "healthAtSample", "netHealthDelta", "netAbsorptionDelta"), number, where)
                        self.fields(obj, ("deadAtSample",), lambda v: type(v) is bool, where)
                        self.vector(obj.get("velocityAtSample"), where + ".velocityAtSample")
                        if obj.get("deadAtSample") is True:
                            self.issue("DEATH_WINDOW", where, "采样窗口包含死亡，不能作为隔离单击证据。")
                elif kind == "velocity_event":
                    self.fields(obj, ("player",), lambda v: isinstance(v, str) and bool(UUID.fullmatch(v)), where)
                    self.fields(obj, ("cancelled",), lambda v: type(v) is bool, where)
                    self.vector(obj.get("eventVelocity"), where + ".eventVelocity")
                    if obj.get("causallyAttributedToHit") is not False:
                        self.issue("CAUSALITY_CLAIM", where, "速度事件没有单击因果关联。", "ERROR")
                elif kind == "player_state":
                    self.fields(obj, ("reason",), nonempty, where)
                    self.player(obj.get("player"), where + ".player")
                elif kind == "ocm_modeset_change":
                    self.fields(obj, ("player", "worldId"), lambda v: isinstance(v, str) and bool(UUID.fullmatch(v)), where)
                    self.fields(obj, ("selected", "reason", "samplePhase"), nonempty, where)
                elif kind == "shutdown":
                    current["beforeDrain"] = self.stats(obj.get("journalBeforeDrain"), where)
                    if current["beforeDrain"].get("accepted") != current["records"] - 1:
                        self.issue("RECORD_COUNT_MISMATCH", where, "关闭前accepted与已提供会话记录数不符。")
                    current["closed"] = True
                    current = None
        for session in self.sessions.values():
            if not session["closed"]:
                self.issue("SHUTDOWN_MISSING", session["where"], "启动会话缺shutdown。")
            for event_id, (hit, where, line_no) in session["hits"].items():
                if event_id not in session["followups"]:
                    self.issue("FOLLOWUP_MISSING", where, "缺少同会话health_followup。")
                    continue
                follow, follow_where, follow_line = session["followups"][event_id]
                if session.get("followupPolicy") == FOLLOWUP_POLICY and hit.get("victimObservationEpoch") != follow.get("victimObservationEpoch"):
                    self.issue("OBSERVATION_EPOCH_MISMATCH", follow_where, "后续采样与原受击记录的观察代号不同，不跨生命关联。", "ERROR")
                if all(integer(v) for v in (hit.get("tick"), follow.get("tick"), follow.get("windowTicks"))):
                    if follow_line <= line_no or follow["tick"] - hit["tick"] != follow["windowTicks"]:
                        self.issue("FOLLOWUP_WINDOW", follow_where, "后续采样顺序或tick窗口与原事件不一致。")
            for event_id, (_, where, _) in session["followups"].items():
                if event_id not in session["hits"]:
                    self.issue("ORPHAN_FOLLOWUP", where, "后续采样找不到同会话伤害，不向其他会话寻找。")

    def reference(self, value, kind, where):
        if not isinstance(value, str) or value not in self.files:
            self.issue("REFERENCE_MISSING", where, "引用的文件ID未登记。")
            return None
        item = self.files[value]
        if item["kind"] != kind or not item["usable"]:
            self.issue("REFERENCE_UNUSABLE", where, "引用文件用途不符或字节未通过检查。")
            return None
        return item

    def refs(self, obj, field, kind, where):
        values = self.sequence(obj.get(field), where + "." + field)
        if not values:
            self.issue("REFERENCE_MISSING", where + "." + field, "缺少明确证据引用。")
        return [entry for value in values if (entry := self.reference(value, kind, where + "." + field))]

    def declared_sessions(self):
        seen = set()
        used_lines = set()
        for index, raw in enumerate(self.sequence(self.manifest.get("sessions"), "sessions")):
            where = f"sessions[{index}]"
            obj = self.mapping(raw, where)
            journal, ordinal = obj.get("journal"), obj.get("session")
            if not isinstance(journal, str) or not integer(ordinal, 1):
                self.issue("SESSION_REFERENCE_INVALID", where, "须指定journal文件ID与从1开始的startup序号。", "ERROR")
                continue
            key = (journal, ordinal)
            if key in seen:
                self.issue("DUPLICATE_SESSION", where, "会话声明重复。", "ERROR")
            seen.add(key)
            session = self.sessions.get(key)
            if session is None:
                self.issue("SESSION_REFERENCE_MISSING", where, "日志中没有指定会话。")
                continue
            drain = obj.get("drain")
            if not isinstance(drain, dict):
                self.issue("DRAIN_MISSING", where, "缺最终排空证据，shutdown快照不能替代。")
                continue
            evidence = self.reference(drain.get("file"), "console", where + ".drain")
            line_no = drain.get("line")
            if evidence is None or not integer(line_no, 1):
                self.issue("DRAIN_MISSING", where, "须明确指定控制台证据与正整数行号。")
                continue
            lines = self.texts[drain["file"]]
            line_key = (drain["file"], line_no)
            if line_key in used_lines:
                self.issue("DRAIN_REUSED", where, "同一排空行不能证明两个不同启动会话。")
            used_lines.add(line_key)
            line = lines[line_no - 1] if line_no <= len(lines) else ""
            match = re.search(r"\[草人杯\] 日志收尾完成：\{([^{}]*)\}\s*$", line)
            if not match:
                self.issue("DRAIN_MISSING", where, "指定行不是本插件的最终收尾完成计数。")
                continue
            stats = {}
            try:
                for pair in match[1].split(","):
                    key_name, raw_value = (v.strip() for v in pair.split("=", 1))
                    if key_name in stats:
                        raise ValueError("duplicate stat")
                    stats[key_name] = {"true": True, "false": False}[raw_value] if raw_value in {"true", "false"} else int(raw_value)
                self.stats(stats, where + ".drain")
            except (ValueError, KeyError):
                self.issue("DRAIN_INVALID", where, "最终计数格式损坏或重复。", "ERROR")
                continue
            if stats.get("workerAlive") is not False or stats.get("queued") != 0:
                self.issue("DRAIN_INCOMPLETE", where, "最终worker未停止或队列未排空。")
            if stats.get("accepted") != session["records"] or stats.get("writtenToBuffer") != session["records"]:
                self.issue("RECORD_COUNT_MISMATCH", where, "最终计数与提供的会话记录数不一致。")
        for key in self.sessions.keys() - seen:
            self.issue("DRAIN_MISSING", self.sessions[key]["where"], "日志中的会话没有独立声明最终排空证据。")
        if not self.sessions:
            self.issue("JOURNAL_EMPTY", "sessions", "没有可核对的startup会话。")

    def sample(self, raw, direction, armour, scenario, where):
        sample = self.mapping(raw, where)
        journal, ordinal, event_id = (sample.get(k) for k in ("journal", "session", "eventId"))
        if not isinstance(journal, str) or not integer(ordinal, 1) or not integer(event_id, 1):
            self.issue("SAMPLE_ID_INVALID", where, "样本须明确journal、startup序号和正整数eventId。", "ERROR")
            return
        session = self.sessions.get((journal, ordinal))
        hit_entry = session["hits"].get(event_id) if session else None
        if hit_entry is None:
            self.issue("SAMPLE_EVENT_MISSING", where, "指定会话没有该伤害记录，不跨会话补齐。")
            return
        hit = hit_entry[0]
        a, b = hit.get("attacker"), hit.get("victim")
        if not isinstance(a, dict) or not isinstance(b, dict):
            return  # Detailed field errors already recorded.
        expected = tuple("old" if letter == "O" else "modern" for letter in direction)
        if (a.get("profile"), b.get("profile")) != expected:
            self.issue("PROFILE_MISMATCH", where, "记录中的攻防身份与用例方向不符。")
        if a.get("uuid") == b.get("uuid") or a.get("worldId") != b.get("worldId") or a.get("world") != b.get("world"):
            self.issue("PLAYER_PAIR_INVALID", where, "需要同一世界中的两个不同玩家。")
        if a.get("effects") or b.get("effects") or a.get("absorption") != 0 or b.get("absorption") != 0:
            self.issue("INPUT_CONDITIONS_MISMATCH", where, "本轮无药水效果/吸收生命的输入条件不满足。")
        hand = a.get("mainHand")
        if not isinstance(hand, dict) or hand.get("material") != "DIAMOND_SWORD" or hand.get("enchanted") is not False or hand.get("amount") != 1 or not integer(hand.get("durabilityDamage")):
            self.issue("EQUIPMENT_MISMATCH", where, "攻击者不是本清单的无附魔钻石剑输入。")
        expected_armour = ["AIR"] * 4 if armour == "N" else ["DIAMOND_BOOTS", "DIAMOND_LEGGINGS", "DIAMOND_CHESTPLATE", "DIAMOND_HELMET"]
        actual_armour = b.get("armourBootsToHelmet")
        if not isinstance(actual_armour, list) or len(actual_armour) != 4 or any(
                not isinstance(item, dict) or item.get("material") != material
                or item.get("amount") != (0 if material == "AIR" else 1)
                or (material != "AIR" and (item.get("enchanted") is not False or not integer(item.get("durabilityDamage"))))
                for item, material in zip(actual_armour, expected_armour)):
            self.issue("EQUIPMENT_MISMATCH", where, "受击者护甲/耐久记录与N/D用例不符。")
        if hit.get("cause") != "ENTITY_ATTACK":
            self.issue("INPUT_CAUSE_MISMATCH", where, "记录不是当前清单的直接近战原因。")
        if hit.get("cancelled") is True:
            self.issue("CANCELLED_EVENT", where, "事件被取消；只保留观察，不推断实际扣血。", "NOTE")
        isolation = sample.get("isolation")
        if not isinstance(isolation, str) or isolation not in {"ISOLATED", "SEQUENCE", "UNKNOWN", "CONTAMINATED"}:
            self.issue("ISOLATION_INVALID", where, "须标明单击/连续序列/未知/污染。", "ERROR")
        elif isolation in {"UNKNOWN", "CONTAMINATED"}:
            self.issue("ISOLATION_UNCONFIRMED", where, "输入归因未知或已污染，需补证据。")
        elif isolation == "SEQUENCE":
            if scenario not in {"S02", "S08", "B05", "B06", "B07"}:
                self.issue("ISOLATION_UNCONFIRMED", where, "此单击用例不能用未隔离的连续序列代替。")
            self.issue("SEQUENCE_NOT_ATTRIBUTED", where, "连续攻击仅保留序列观察，净HP不分摊成逐击伤害。", "NOTE")
        elif event_id in session["followups"]:
            follow = session["followups"][event_id][0]
            start, end = hit.get("tick"), follow.get("tick")
            if integer(start) and integer(end):
                for other_id, (other, _, _) in session["hits"].items():
                    victim = other.get("victim")
                    if other_id != event_id and isinstance(victim, dict) and victim.get("uuid") == b.get("uuid") and integer(other.get("tick")) and start <= other["tick"] <= end:
                        self.issue("OVERLAPPING_HITS", where, "单击采样窗口有同一受击者的其他伤害，不能独立归因。")
                        break

    def cases(self):
        cases = self.sequence(self.manifest.get("cases"), "cases")
        if not cases:
            self.issue("CASES_EMPTY", "cases", "没有选定用例；不能以空清单表示资料完整。")
        seen = set()
        for index, raw in enumerate(cases):
            where = f"cases[{index}]"
            obj = self.mapping(raw, where)
            case_id = obj.get("caseId")
            match = CASE_ID.fullmatch(case_id) if isinstance(case_id, str) else None
            if not match:
                self.issue("CASE_ID_INVALID", where, "本工具仅支持C04四方向、N/D和S01–S08/B05–B07。", "ERROR")
                continue
            if case_id in seen:
                self.issue("DUPLICATE_CASE", where, "同一用例请合并到samples，不能重复计数。", "ERROR")
            seen.add(case_id)
            execution = obj.get("execution")
            if not isinstance(execution, str) or execution not in self.case_counts:
                self.issue("EXECUTION_INVALID", where, "执行状态须为NOT_RUN或RECORDED。", "ERROR")
                continue
            self.case_counts[execution] += 1
            verdict = obj.get("observedVerdict", "PENDING")
            if not isinstance(verdict, str) or verdict not in {"PENDING", "PASS", "FAIL", "INCONCLUSIVE"}:
                self.issue("VERDICT_INVALID", where, "人工观察结论字段无效；工具不会采纳它作为自身判定。", "ERROR")
            if execution == "NOT_RUN":
                self.issue("CASE_NOT_RUN", where, "未执行用例仍需实际证据。")
                if obj.get("samples") or obj.get("observedVerdict", "PENDING") != "PENDING":
                    self.issue("INVALID_EXECUTION_CLAIM", where, "NOT_RUN不能同时声明实测样本或判定。", "ERROR")
                continue
            source = obj.get("inputSource")
            if not isinstance(source, str) or source not in SOURCES:
                self.issue("SOURCE_INVALID", where, "输入来源缺失或不受支持。", "ERROR")
            if self.manifest.get("purpose") == "REAL_CAPTURE" and source != "REAL_PLAYER":
                self.issue("NONPLAYER_INPUT", where, "命令/合成事件/桩输入不能成为真人PvP结果。")
            for field, kind in (("inputRefs", "input"), ("contextRefs", "context"),
                                ("observationRefs", "observation"), ("criteriaRefs", "criteria")):
                self.refs(obj, field, kind, where)
            baselines = self.refs(obj, "baselineRefs", "baseline", where)
            required = {"1.8.9"} if match[1] == "OO" else {"26.3"} if match[1] == "MM" else {"1.8.9", "26.3"}
            versions = {b.get("version") for b in baselines if isinstance(b.get("version"), str)}
            if not required.issubset(versions):
                self.issue("BASELINE_MISSING", where, "缺对应版本基准；混版必须同时引用两版本依据。")
            for baseline in baselines:
                provenance = baseline.get("provenance")
                if not isinstance(provenance, str) or provenance not in SOURCES or (self.manifest.get("purpose") == "REAL_CAPTURE" and provenance != "REAL_PLAYER"):
                    self.issue("BASELINE_PROVENANCE", where, "基准来源不明或不能用于真人对照。")
            if obj.get("criteriaConfirmed") is not True:
                self.issue("CRITERIA_UNCONFIRMED", where, "数值精度、容差或混版组合判据待确认；工具不会填默认值。")
            samples = self.sequence(obj.get("samples"), where + ".samples")
            if not samples:
                if not nonempty(obj.get("noEventReason")):
                    self.issue("SAMPLES_MISSING", where, "无事件须提供独立观察证据和说明，不能自动推断没有攻击。")
                else:
                    self.issue("NO_EVENT_NOT_ATTRIBUTED", where, "只记录无事件观察，不判断原因或战斗行为正确性。", "NOTE")
            sample_keys = set()
            for sample_index, sample in enumerate(samples):
                self.sample(sample, match[1], match[2], match[3], f"{where}.samples[{sample_index}]")
                if isinstance(sample, dict):
                    key = tuple(sample.get(k) for k in ("journal", "session", "eventId"))
                    if isinstance(key[0], str) and integer(key[1], 1) and integer(key[2], 1):
                        if key in sample_keys:
                            self.issue("DUPLICATE_SAMPLE", where, "同一用例重复引用同一事件，不算独立采样。")
                        sample_keys.add(key)

    def run(self):
        if self.path is not None and self.load_manifest():
            self.fields(self.manifest, ("runId",), nonempty, "manifest")
            purpose = self.manifest.get("purpose")
            if not isinstance(purpose, str) or purpose not in {"REAL_CAPTURE", "SYNTHETIC_FIXTURE"}:
                self.issue("PURPOSE_INVALID", "manifest", "须明确真实采集或合成测试夹具。", "ERROR")
            env = self.mapping(self.manifest.get("environment"), "environment")
            self.fields(env, ("serverVersion", "clientVersion", "javaVersion"), nonempty, "environment")
            self.fields(env, ("candidateCommit",), lambda v: isinstance(v, str) and bool(re.fullmatch(r"[a-fA-F0-9]{40}", v)), "environment")
            self.fields(env, ("serverJarSha256", "combatJarSha256", "ocmJarSha256", "configSha256"), lambda v: isinstance(v, str) and bool(SHA256.fullmatch(v)), "environment")
            self.read_files()
            self.read_journals()
            self.declared_sessions()
            self.cases()
        for path, original_hash in self.inputs.items():
            try:
                if file_hash(path) == original_hash:
                    continue
            except OSError:
                pass
            self.issue("INPUT_CHANGED", "inputs", "检查期间原始输入变化，请保留稳定副本后重试。")
        status = "FORMAT_ERROR" if "ERROR" in self.severities else "INCOMPLETE" if "MISSING" in self.severities else "COMPLETE"
        labels = {"FORMAT_ERROR": "格式错误", "INCOMPLETE": "证据不完整", "COMPLETE": "证据结构完整"}
        return dict(schemaVersion=1, tool="cross-era-evidence-checker", label="[草人杯] " + labels[status],
                    checkedUtc=datetime.now(timezone.utc).isoformat(), integrityStatus=status,
                    scope="SELECTED_CASES_STRUCTURE_ONLY", gameplayVerdict="NOT_EVALUATED",
                    userAcceptance="PENDING", captureAuthenticity="NOT_VERIFIED",
                    humanEvidenceStatus="NOT_ELIGIBLE_SYNTHETIC" if self.manifest.get("purpose") == "SYNTHETIC_FIXTURE" else "MANUAL_REVIEW_REQUIRED",
                    caseCounts=self.case_counts, sessions=len(self.sessions), issues=self.issues,
                    omittedIssues=self.omitted,
                    inputHashes={str(p.relative_to(self.root)): h for p, h in self.inputs.items()},
                    limitations=["声明及文件哈希不能证明真人来源，仍须人工审阅原始证据。",
                                 "结构完整不代表88格全部执行、公式正确或用户验收。",
                                 "事件值、净HP、速度和暴击条件均不自动证明单击因果。"])


def check_evidence(manifest_path: Path) -> dict:
    """Read only explicitly named inputs. Does not write a report or change acceptance."""
    return Checker(manifest_path).run()


class CliParser(argparse.ArgumentParser):
    def error(self, message):
        self.exit(2, "[草人杯] 参数/输出错误：" + message + "\n")


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    parser = CliParser(description="[草人杯] 离线证据结构核对；不判战斗还原，不改输入。")
    parser.add_argument("--manifest", type=Path, required=True, help="私有runtime内的JSON清单")
    parser.add_argument("--output", type=Path, help="可选：清单目录内尚不存在的JSON报告；默认输出到终端")
    args = parser.parse_args(argv)
    try:
        root = resolve_local(args.manifest).parent
        if args.output is not None:
            output = resolve_local(args.output)
            if not output.is_relative_to(root) or not output.parent.is_dir() or output.exists():
                parser.error("报告须位于清单目录内、父目录已存在且文件尚不存在；不覆盖旧文件。")
    except (OSError, ValueError, RuntimeError):
        parser.error("输入和输出必须为普通本地路径；拒绝网络、设备、符号链接和重解析点。")
    result = check_evidence(args.manifest)
    rendered = json.dumps(result, ensure_ascii=False, allow_nan=False, indent=2) + "\n"
    if args.output is None:
        print(rendered, end="")
    else:
        try:
            with output.open("x", encoding="utf-8", newline="\n") as stream:
                stream.write(rendered)
        except OSError:
            parser.error("无法独占创建报告；没有覆盖已有文件。")
    print(result["label"] + "；玩法未判定，用户验收仍待确认。", file=sys.stderr)
    return {"COMPLETE": 0, "INCOMPLETE": 1, "FORMAT_ERROR": 2}[result["integrityStatus"]]


if __name__ == "__main__":
    raise SystemExit(main())
