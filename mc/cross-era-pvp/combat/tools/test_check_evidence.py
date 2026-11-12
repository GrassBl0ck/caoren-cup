"""Synthetic fixtures only. All generated files remain in the supplied runtime directory."""
import argparse
import copy
import hashlib
import json
import os
import stat
from types import SimpleNamespace
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from check_evidence import check_evidence, resolve_local

TEMP_ROOT = None
STAMP = "2026-11-05T00:00:00+08:00"
UUID_A = "00000000-0000-0000-0000-000000000001"
UUID_B = "00000000-0000-0000-0000-000000000002"
WORLD = "00000000-0000-0000-0000-000000000003"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def record(kind, tick, **fields):
    return dict(schemaVersion=1, kind=kind, timestamp=STAMP, tick=tick, **fields)


def player(uid, profile):
    return dict(uuid=uid, world="synthetic-world", worldId=WORLD, profile=profile,
                modeset="ce-" + profile, stored=True, overrides=False, verifiedModules=True,
                effectiveModules={"synthetic-module": profile == "old"},
                position=dict(x=0, y=200, z=0, yaw=0, pitch=0), velocity=dict(x=0, y=0, z=0),
                health=20, absorption=0, noDamageTicks=0, maximumNoDamageTicks=20,
                lastDamage=0, attackCooldownObserved=1, sprinting=False, onGround=True,
                fallDistance=0, inWater=False, climbing=False, insideVehicle=False,
                blindness=False, effects=[],
                mainHand=dict(material="DIAMOND_SWORD", amount=1, enchanted=False, durabilityDamage=0),
                armourBootsToHelmet=[dict(material="AIR", amount=0) for _ in range(4)])


def session_records():
    return [
        record("startup", 0, pluginVersion="0.1.0-dev", serverVersion="synthetic-26.3",
               javaVersion="25", backend="OCM 2.8.0", fidelityAccepted=False),
        record("damage_event", 10, eventId=1, cause="ENTITY_ATTACK", cancelled=False,
               samplePhase="MONITOR_EVENT_VALUES_NOT_ACTUAL_HEALTH_LOSS",
               inputProvenance="BUKKIT_EVENT_NATIVE_OR_SYNTHETIC_UNDETERMINED",
               eventBaseDamage=3, eventFinalDamage=3,
               attacker=player(UUID_A, "old"), victim=player(UUID_B, "modern")),
        record("health_followup", 11, eventId=1, windowTicks=1,
               isolatedHitGuaranteed=False, sameConnectionAndWorld=True,
               healthBeforeEventCommit=20, healthAtSample=17, netHealthDelta=3,
               netAbsorptionDelta=0, deadAtSample=False, velocityAtSample=dict(x=0, y=0, z=0)),
        record("velocity_event", 11, player=UUID_B, cancelled=False,
               eventVelocity=dict(x=0, y=0, z=0), causallyAttributedToHit=False),
        record("shutdown", 20, journalBeforeDrain=dict(accepted=4, rejected=0, errors=0,
               queued=0, writtenToBuffer=4, workerAlive=True))]


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix=self._testMethodName + "-", dir=TEMP_ROOT)).resolve()
        self.assertTrue(self.root.is_relative_to(TEMP_ROOT))
        self.events = session_records()
        self.manifest = {
            "schemaVersion": 1, "runId": "synthetic-only", "purpose": "SYNTHETIC_FIXTURE",
            "environment": dict(serverVersion="synthetic-26.3", clientVersion="26.3", javaVersion="25",
                candidateCommit="a" * 40, serverJarSha256="a" * 64, combatJarSha256="b" * 64,
                ocmJarSha256="c" * 64, configSha256="d" * 64),
            "files": [], "sessions": [{"journal": "events", "session": 1,
                "drain": {"file": "console", "line": 1}}],
            "cases": [{"caseId": "C04-OM-N-S01", "execution": "RECORDED",
                "inputSource": "SYNTHETIC_EVENT", "observedVerdict": "PENDING",
                "inputRefs": ["input"], "contextRefs": ["context"], "observationRefs": ["observations"],
                "baselineRefs": ["old-baseline", "modern-baseline"],
                "criteriaRefs": ["criteria"], "criteriaConfirmed": True,
                "samples": [{"journal": "events", "session": 1, "eventId": 1, "isolation": "ISOLATED"}]}]}
        for name, kind, extra in [
            ("input", "input", {}), ("context", "context", {}),
            ("observations", "observation", {}), ("criteria", "criteria", {}),
            ("old-baseline", "baseline", dict(version="1.8.9", provenance="SYNTHETIC_EVENT")),
            ("modern-baseline", "baseline", dict(version="26.3", provenance="SYNTHETIC_EVENT"))]:
            self.file(name, kind, "Synthetic fixture only; no gameplay evidence.\n", **extra)
        self.save_events()
        self.file("console", "console", self.drain_line(5))
        self.save()

    def file(self, name, kind, text, **extra):
        path = self.root / (name + (".jsonl" if kind == "journal" else ".txt"))
        path.write_text(text, encoding="utf-8")
        entry = dict(id=name, kind=kind, path=path.name, sha256=digest(path), **extra)
        self.manifest["files"] = [x for x in self.manifest["files"] if x["id"] != name] + [entry]
        return path

    def save_events(self):
        self.file("events", "journal", "".join(json.dumps(x, ensure_ascii=False) + "\n" for x in self.events))

    @staticmethod
    def drain_line(count, **changes):
        stats = dict(accepted=count, rejected=0, errors=0, queued=0, writtenToBuffer=count, workerAlive=False)
        stats.update(changes)
        values = ", ".join(f"{k}={str(v).lower()}" for k, v in stats.items())
        return "[INFO] [CrossEraCombat] [草人杯] 日志收尾完成：{" + values + "}\n"

    def save(self):
        self.path = self.root / "manifest.json"
        self.path.write_text(json.dumps(self.manifest, ensure_ascii=False, indent=2), encoding="utf-8")
        return self.path

    def check(self):
        result = check_evidence(self.save())
        self.assertEqual(result["gameplayVerdict"], "NOT_EVALUATED")
        self.assertEqual(result["userAcceptance"], "PENDING")
        return result

    def codes(self):
        return {item["code"] for item in self.check()["issues"]}

    def test_complete_synthetic_fixture_is_structure_only(self):
        before = {p.name: digest(p) for p in self.root.iterdir()}
        result = self.check()
        self.assertEqual(result["integrityStatus"], "COMPLETE")
        self.assertEqual(result["humanEvidenceStatus"], "NOT_ELIGIBLE_SYNTHETIC")
        self.assertEqual(result["captureAuthenticity"], "NOT_VERIFIED")
        self.assertEqual(before, {p.name: digest(p) for p in self.root.iterdir()})

    def guarded_events(self):
        self.events[0]["followupPolicy"] = "connection-world-life-v1"
        self.events[1]["victimObservationEpoch"] = 7
        self.events[2].update(victimObservationEpoch=7, sameObservationEpoch=True, observationInvalidation="NONE")

    def test_guarded_complete_fixture_still_does_not_claim_gameplay_acceptance(self):
        self.guarded_events()
        self.save_events()
        self.assertEqual(self.check()["integrityStatus"], "COMPLETE")
        self.assertNotIn("LEGACY_LIFECYCLE_GUARD_ABSENT", self.codes())

    def test_legacy_capture_is_identified_without_inventing_lifecycle_guard(self):
        self.assertIn("LEGACY_LIFECYCLE_GUARD_ABSENT", self.codes())
        self.assertEqual(self.check()["integrityStatus"], "COMPLETE")

    def test_invalidated_lifecycle_has_no_health_or_velocity_measurements(self):
        self.guarded_events()
        self.events[2] = record("health_followup", 11, eventId=1, windowTicks=1,
            isolatedHitGuaranteed=False, sameConnectionAndWorld=True,
            victimObservationEpoch=7, sameObservationEpoch=False, observationInvalidation="RESPAWN")
        self.save_events()
        self.assertIn("LIFECYCLE_CHANGED", self.codes())
        self.assertNotIn("FIELD_INVALID", self.codes())
        self.assertEqual(self.check()["integrityStatus"], "INCOMPLETE")

    def test_invalidated_window_cannot_supply_health_delta_as_if_same_life(self):
        self.guarded_events()
        self.events[2].update(sameObservationEpoch=False, observationInvalidation="DEATH")
        self.save_events()
        self.assertIn("INVALIDATED_MEASUREMENTS", self.codes())
        self.assertEqual(self.check()["integrityStatus"], "FORMAT_ERROR")

    def test_followup_epoch_cannot_be_borrowed_from_new_life(self):
        self.guarded_events()
        self.events[2]["victimObservationEpoch"] = 8
        self.save_events()
        self.assertIn("OBSERVATION_EPOCH_MISMATCH", self.codes())

    def test_unknown_followup_policy_is_rejected(self):
        for policy in ("future-policy", None, []):
            self.events[0]["followupPolicy"] = policy
            self.save_events()
            self.assertIn("FOLLOWUP_POLICY_UNSUPPORTED", self.codes())

    def test_guard_fields_without_startup_declaration_are_rejected(self):
        self.guarded_events()
        del self.events[0]["followupPolicy"]
        self.save_events()
        self.assertIn("UNDECLARED_FOLLOWUP_POLICY", self.codes())

    def test_guard_reason_must_agree_with_validity_and_be_known(self):
        self.guarded_events()
        for state, reason in ((True, "DEATH"), (False, "NONE"), (False, "unknown"), (True, [])):
            self.events[2].update(sameObservationEpoch=state, observationInvalidation=reason)
            self.save_events()
            self.assertIn("LIFECYCLE_STATE_INVALID", self.codes())

    def test_guard_requires_positive_epoch_and_explicit_boolean(self):
        self.guarded_events()
        self.events[1]["victimObservationEpoch"] = True
        self.events[2].pop("sameObservationEpoch")
        self.save_events()
        self.assertIn("FIELD_INVALID", self.codes())

    def test_not_run_cannot_claim_pass_or_recorded_samples(self):
        self.manifest["cases"][0].update(execution="NOT_RUN", observedVerdict="PASS")
        self.assertIn("CASE_NOT_RUN", self.codes())
        self.assertIn("INVALID_EXECUTION_CLAIM", self.codes())

    def test_missing_baselines_and_unconfirmed_criteria_stay_incomplete(self):
        self.manifest["cases"][0].update(baselineRefs=[], criteriaConfirmed=False)
        codes = self.codes()
        self.assertIn("BASELINE_MISSING", codes)
        self.assertIn("CRITERIA_UNCONFIRMED", codes)

    def test_mixed_direction_requires_both_baseline_versions(self):
        self.manifest["cases"][0]["baselineRefs"] = ["old-baseline"]
        self.assertIn("BASELINE_MISSING", self.codes())

    def test_nonplayer_input_cannot_be_real_capture(self):
        self.manifest["purpose"] = "REAL_CAPTURE"
        self.assertIn("NONPLAYER_INPUT", self.codes())
        self.assertIn("BASELINE_PROVENANCE", self.codes())

    def test_hash_mismatch_is_not_complete(self):
        (self.root / "input.txt").write_text("changed", encoding="utf-8")
        self.assertIn("HASH_MISMATCH", self.codes())

    def test_missing_followup_is_reported(self):
        self.events = [e for e in self.events if e["kind"] != "health_followup"]
        self.save_events()
        self.assertIn("FOLLOWUP_MISSING", self.codes())

    def test_duplicate_event_id_in_same_session_is_reported(self):
        self.events.insert(2, copy.deepcopy(self.events[1]))
        self.save_events()
        self.assertIn("DUPLICATE_EVENT", self.codes())

    def test_same_event_id_after_restart_is_not_joined_to_previous_session(self):
        self.events += session_records()
        self.save_events()
        self.file("console", "console", self.drain_line(5) * 2)
        self.manifest["sessions"].append(dict(journal="events", session=2, drain=dict(file="console", line=2)))
        self.manifest["cases"][0]["samples"].append(dict(journal="events", session=2, eventId=1, isolation="ISOLATED"))
        self.assertEqual(self.check()["integrityStatus"], "COMPLETE")

    def test_truncated_jsonl_has_line_diagnostic(self):
        self.file("events", "journal", '{"schemaVersion":1,\n')
        result = self.check()
        self.assertEqual(result["integrityStatus"], "FORMAT_ERROR")
        self.assertIn("JSON_INVALID", {x["code"] for x in result["issues"]})

    def test_unknown_journal_schema_is_not_silently_accepted(self):
        self.events[1]["schemaVersion"] = 2
        self.save_events()
        self.assertIn("SCHEMA_UNSUPPORTED", self.codes())

    def test_changed_connection_is_not_usable_health_sample(self):
        self.events[2] = record("health_followup", 11, eventId=1, windowTicks=1,
                               isolatedHitGuaranteed=False, sameConnectionAndWorld=False)
        self.save_events()
        self.assertIn("CONNECTION_CHANGED", self.codes())

    def test_death_window_is_not_isolated_hit(self):
        self.events[2]["deadAtSample"] = True
        self.save_events()
        self.assertIn("DEATH_WINDOW", self.codes())

    def test_final_drain_errors_and_record_loss_are_visible(self):
        self.file("console", "console", self.drain_line(5, rejected=1, errors=1))
        self.assertIn("JOURNAL_LOSS", self.codes())

    def test_shutdown_snapshot_does_not_substitute_for_final_drain(self):
        self.manifest["sessions"][0].pop("drain")
        self.assertIn("DRAIN_MISSING", self.codes())

    def test_missing_shutdown_is_incomplete(self):
        self.events.pop()
        self.save_events()
        self.assertIn("SHUTDOWN_MISSING", self.codes())

    def test_duplicate_json_keys_are_rejected(self):
        self.path.write_text('{"schemaVersion":1,"schemaVersion":1}', encoding="utf-8")
        self.assertEqual(check_evidence(self.path)["integrityStatus"], "FORMAT_ERROR")

    def test_nonfinite_numbers_and_boolean_schema_are_rejected(self):
        for replacement in ('NaN', 'Infinity', '1e400', 'true'):
            with self.subTest(replacement=replacement):
                self.file("events", "journal", '{"schemaVersion":' + replacement + '}\n')
                self.assertEqual(self.check()["integrityStatus"], "FORMAT_ERROR")

    def test_missing_event_id_and_orphan_followup_are_reported(self):
        self.events[1].pop("eventId")
        self.save_events()
        self.assertIn("EVENT_ID_INVALID", self.codes())
        self.assertIn("ORPHAN_FOLLOWUP", self.codes())

    def test_malformed_shapes_never_crash(self):
        original = copy.deepcopy(self.manifest)
        for field in ("schemaVersion", "purpose", "environment", "files", "sessions", "cases"):
            for invalid in (None, [], True, 7):
                with self.subTest(field=field, invalid=invalid):
                    self.manifest = copy.deepcopy(original)
                    self.manifest[field] = invalid
                    self.assertNotEqual(self.check()["integrityStatus"], "COMPLETE")

    def test_nested_bad_types_are_diagnostics_not_tracebacks(self):
        for field in ("journal", "session", "eventId", "isolation"):
            for invalid in ({}, [], True, None):
                with self.subTest(field=field, invalid=invalid):
                    sample = dict(journal="events", session=1, eventId=1, isolation="ISOLATED")
                    sample[field] = invalid
                    self.manifest["cases"][0]["samples"] = [sample]
                    self.assertNotEqual(self.check()["integrityStatus"], "COMPLETE")

    def test_path_traversal_absolute_and_unc_references_are_rejected_before_open(self):
        file_entry = next(f for f in self.manifest["files"] if f["id"] == "input")
        for path in ("../outside.txt", "..\\outside.txt", "C:\\outside.txt", "\\\\server\\share\\secret"):
            with self.subTest(path=path):
                file_entry["path"] = path
                self.assertIn("PATH_ESCAPE", self.codes())

    def test_symlink_escape_is_rejected_when_supported(self):
        outside = self.root.parent / (self.root.name + "-outside.txt")
        outside.write_text("synthetic", encoding="utf-8")
        link = self.root / "escaped.txt"
        try:
            link.symlink_to(outside)
        except (OSError, NotImplementedError):
            self.skipTest("宿主不允许创建符号链接；未执行此项，不计通过")
        entry = next(f for f in self.manifest["files"] if f["id"] == "input")
        entry.update(path=link.name, sha256=digest(outside))
        self.assertIn("PATH_ESCAPE", self.codes())

    def test_unc_manifest_is_rejected_without_reading_it(self):
        with patch.object(Path, "read_bytes", side_effect=AssertionError("must not read UNC")), \
             patch.object(Path, "stat", side_effect=AssertionError("must not stat UNC")), \
             patch.object(Path, "resolve", side_effect=AssertionError("must not resolve UNC")):
            result = check_evidence(Path("//not-a-real-host/share/manifest.json"))
        self.assertEqual(result["integrityStatus"], "FORMAT_ERROR")

    def test_declared_environment_must_match_startup_record(self):
        self.manifest["environment"]["serverVersion"] = "different-server"
        self.assertIn("ENVIRONMENT_MISMATCH", self.codes())

    def test_wrong_armour_and_wrong_direction_are_reported(self):
        self.manifest["cases"][0]["caseId"] = "C04-MO-D-S01"
        self.assertIn("EQUIPMENT_MISMATCH", self.codes())
        self.assertIn("PROFILE_MISMATCH", self.codes())

    def test_effects_absorption_and_missing_weapon_durability_are_not_in_scope(self):
        self.events[1]["attacker"]["effects"] = [{"type": "synthetic-strength", "amplifier": 1, "ticks": 10}]
        self.events[1]["victim"]["absorption"] = 2
        self.events[1]["attacker"]["mainHand"].pop("durabilityDamage")
        self.save_events()
        self.assertIn("INPUT_CONDITIONS_MISMATCH", self.codes())
        self.assertIn("EQUIPMENT_MISMATCH", self.codes())

    def test_attacker_and_victim_must_be_distinct_and_in_same_world(self):
        self.events[1]["victim"].update(uuid=UUID_A, worldId=UUID_B)
        self.save_events()
        self.assertIn("PLAYER_PAIR_INVALID", self.codes())

    def test_windows_streams_and_device_paths_are_rejected(self):
        entry = next(f for f in self.manifest["files"] if f["id"] == "input")
        for path in ("input.txt:private", "NUL", "CON"):
            with self.subTest(path=path):
                entry["path"] = path
                self.assertIn("PATH_ESCAPE", self.codes())

    def test_observed_verdict_is_validated_but_not_adopted(self):
        self.manifest["cases"][0]["observedVerdict"] = "ACCEPTED"
        self.assertIn("VERDICT_INVALID", self.codes())

    def test_duplicate_drain_line_cannot_prove_two_sessions(self):
        self.events += session_records()
        self.save_events()
        self.manifest["sessions"].append(dict(journal="events", session=2, drain=dict(file="console", line=1)))
        self.assertIn("DRAIN_REUSED", self.codes())

    def test_busy_writer_and_wrong_record_counts_are_not_complete(self):
        self.file("console", "console", self.drain_line(99, queued=1, workerAlive=True))
        self.assertIn("DRAIN_INCOMPLETE", self.codes())
        self.assertIn("RECORD_COUNT_MISMATCH", self.codes())

    def test_duplicate_console_counter_is_format_error(self):
        self.file("console", "console", self.drain_line(5).replace("accepted=5", "accepted=5, accepted=5"))
        self.assertIn("DRAIN_INVALID", self.codes())

    def test_followup_never_borrows_hit_from_previous_session(self):
        self.events += [session_records()[0], session_records()[2], session_records()[-1]]
        self.save_events()
        self.assertIn("ORPHAN_FOLLOWUP", self.codes())

    def test_single_hit_overlap_is_detected_without_damage_calculation(self):
        other = copy.deepcopy(self.events[1]); other["eventId"] = 2
        follow = copy.deepcopy(self.events[2]); follow["eventId"] = 2
        self.events.insert(2, other); self.events.insert(4, follow)
        self.events[-1]["journalBeforeDrain"].update(accepted=6, writtenToBuffer=6)
        self.save_events(); self.file("console", "console", self.drain_line(7))
        self.assertIn("OVERLAPPING_HITS", self.codes())
        case = self.manifest["cases"][0]
        case["caseId"] = "C04-OM-N-S02"; case["samples"][0]["isolation"] = "SEQUENCE"
        result = self.check()
        self.assertEqual(result["integrityStatus"], "COMPLETE")
        self.assertIn("SEQUENCE_NOT_ATTRIBUTED", {i["code"] for i in result["issues"]})

    def test_no_event_observation_requires_explicit_independent_evidence(self):
        self.events = [self.events[0], self.events[-1]]
        self.events[-1]["journalBeforeDrain"].update(accepted=1, writtenToBuffer=1)
        self.save_events(); self.file("console", "console", self.drain_line(2))
        self.manifest["cases"][0]["samples"] = []
        self.assertIn("SAMPLES_MISSING", self.codes())
        self.manifest["cases"][0]["noEventReason"] = "synthetic no-event observation; reason not inferred"
        self.assertEqual(self.check()["integrityStatus"], "COMPLETE")

    def test_change_during_read_is_detected(self):
        import check_evidence as module
        original_hash = module.file_hash
        count = 0
        def changing_hash(path):
            nonlocal count
            if path.name == "input.txt":
                count += 1
                if count > 1:
                    return "0" * 64
            return original_hash(path)
        with patch.object(module, "file_hash", side_effect=changing_hash):
            self.assertIn("INPUT_CHANGED", self.codes())

    def cli(self, *args):
        return subprocess.run([sys.executable, "-B", str(Path(__file__).with_name("check_evidence.py")),
                               "--manifest", str(self.save()), *args],
                              capture_output=True, text=True, encoding="utf-8",
                              env={**os.environ, "PYTHONIOENCODING": "utf-8"}, timeout=20)

    def test_cli_creates_only_explicit_new_report_and_preserves_input_bytes(self):
        before = {p.name: digest(p) for p in self.root.iterdir()}
        output = self.root / "report.json"
        result = self.cli("--output", str(output))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("[草人杯]", result.stderr)
        report = json.loads(output.read_text(encoding="utf-8"))
        self.assertEqual(report["gameplayVerdict"], "NOT_EVALUATED")
        self.assertEqual(before, {p.name: digest(p) for p in self.root.iterdir() if p != output})

    def test_cli_exit_codes_distinguish_incomplete_and_invalid(self):
        self.manifest["cases"][0]["baselineRefs"] = []
        result = self.cli()
        self.assertEqual(result.returncode, 1)
        self.assertEqual(json.loads(result.stdout)["integrityStatus"], "INCOMPLETE")
        self.manifest["schemaVersion"] = 2
        result = self.cli()
        self.assertEqual(result.returncode, 2)
        self.assertEqual(json.loads(result.stdout)["integrityStatus"], "FORMAT_ERROR")

    def test_cli_will_not_overwrite_input_existing_output_or_escape_root(self):
        before = {p.name: digest(p) for p in self.root.iterdir()}
        for output in (self.path, self.root / "input.txt", self.root.parent / "must-not-create.json"):
            with self.subTest(output=output.name):
                result = self.cli("--output", str(output))
                self.assertEqual(result.returncode, 2)
        self.assertEqual(before, {p.name: digest(p) for p in self.root.iterdir()})
        self.assertFalse((self.root.parent / "must-not-create.json").exists())

    def test_cli_uses_utf8_even_with_legacy_redirected_stream_encoding(self):
        self.save()
        env = dict(os.environ, PYTHONUTF8="0", PYTHONIOENCODING="cp936")
        result = subprocess.run([sys.executable, "-B", str(Path(__file__).with_name("check_evidence.py")),
            "--manifest", str(self.path)], capture_output=True, env=env)
        self.assertEqual(result.returncode, 0)
        self.assertIn("[草人杯]", result.stderr.decode("utf-8"))
        self.assertEqual(json.loads(result.stdout.decode("utf-8"))["integrityStatus"], "COMPLETE")

    def test_cli_argument_errors_are_utf8_under_legacy_encoding(self):
        env = dict(os.environ, PYTHONUTF8="0", PYTHONIOENCODING="cp936")
        result = subprocess.run([sys.executable, "-B", str(Path(__file__).with_name("check_evidence.py"))],
            capture_output=True, env=env)
        self.assertEqual(result.returncode, 2)
        self.assertIn("[草人杯]", result.stderr.decode("utf-8"))

    def test_symbolic_link_metadata_is_rejected_before_target_resolution(self):
        # Deterministic metadata stub; actual link creation remains a separate skipped test.
        metadata = SimpleNamespace(st_mode=stat.S_IFLNK, st_file_attributes=0)
        with patch.object(Path, "lstat", return_value=metadata), patch.object(
                Path, "resolve", side_effect=AssertionError("must not resolve link target")):
            with self.assertRaises(ValueError):
                resolve_local(self.path)

    def test_windows_reparse_metadata_is_rejected_before_target_resolution(self):
        # Includes junction/cloud/network reparse targets; does not create a real junction.
        metadata = SimpleNamespace(st_mode=stat.S_IFDIR, st_file_attributes=0x400)
        with patch.object(Path, "lstat", return_value=metadata), patch.object(
                Path, "resolve", side_effect=AssertionError("must not resolve reparse target")):
            with self.assertRaises(ValueError):
                resolve_local(self.path)


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--temp-root", required=True, type=Path)
    args, remaining = parser.parse_known_args()
    TEMP_ROOT = args.temp_root.resolve()
    workspace = Path(__file__).resolve().parents[4]
    if not TEMP_ROOT.is_relative_to(workspace / "runtime"):
        parser.error("[草人杯] 测试临时目录必须位于本工作树runtime内。")
    TEMP_ROOT.mkdir(parents=True, exist_ok=True)
    unittest.main(argv=[sys.argv[0], *remaining])
