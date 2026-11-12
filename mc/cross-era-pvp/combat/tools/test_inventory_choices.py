"""[草人杯] MIT. Synthetic data only; retain all test material under runtime."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from inventory_choices import decode_ocm_bson, inventory_choices

TEMP_ROOT = None
P1 = "aaaaaaaa-0000-0000-0000-000000000001"
P2 = "bbbbbbbb-0000-0000-0000-000000000002"
W1 = "cccccccc-0000-0000-0000-000000000003"
W2 = "dddddddd-0000-0000-0000-000000000004"


def bson_pairs(pairs):
    elements = bytearray()
    for key, value in pairs:
        name = key.encode("utf-8") + b"\0"
        if isinstance(value, dict):
            elements += b"\x03" + name + bson_pairs(value.items())
        else:
            raw = value.encode("utf-8") + b"\0"
            elements += b"\x02" + name + struct.pack("<i", len(raw)) + raw
    return struct.pack("<i", len(elements) + 5) + elements + b"\0"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class InventoryTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix=self._testMethodName + "-", dir=TEMP_ROOT))
        self.source = self.root / "players.bson"
        self.path = self.root / "manifest.json"
        self.data = {P1: {"modesetByWorld": {W1: "cross-era-old", W2: "cross-era-modern"}},
                     P2: {"modesetByWorld": {W1: "cross-era-modern"}}}
        self.manifest = dict(schemaVersion=1, purpose="SYNTHETIC_FIXTURE",
            source=dict(path="players.bson", sha256="", ocmVersion="2.8.0",
                ocmJarSha256="a" * 64, configSha256="b" * 64,
                snapshotState="SYNTHETIC", capturedAt="2026-11-05T01:40:00+08:00"),
            profiles=dict(old="cross-era-old", modern="cross-era-modern"),
            worlds=[dict(uuid=W1, name="synthetic-arena"), dict(uuid=W2, name="synthetic-second")])
        self.save_data()

    def save_data(self, raw=None):
        self.source.write_bytes(bson_pairs(self.data.items()) if raw is None else raw)
        self.manifest["source"]["sha256"] = sha(self.source)
        self.save_manifest()

    def save_manifest(self):
        self.path.write_text(json.dumps(self.manifest, ensure_ascii=False), encoding="utf-8")

    def report(self):
        self.save_manifest()
        return inventory_choices(self.path)

    def codes(self, report):
        return {x["code"] for x in report["diagnostics"]}

    def test_two_players_multiple_worlds_keep_every_saved_choice(self):
        before = {p.name: sha(p) for p in self.root.iterdir()}
        report = inventory_choices(self.path)
        self.assertEqual(report["inventoryStatus"], "COMPLETE")
        self.assertEqual(report["counts"], dict(players=2, choices=3, old=1, modern=2, unknown=0, noSavedChoice=0))
        self.assertEqual([(x["playerId"], x["worldId"], x["profile"]) for x in report["choices"]],
            [(P1, W1, "old"), (P1, W2, "modern"), (P2, W1, "modern")])
        self.assertFalse(report["migrationExecuted"])
        self.assertFalse(report["migrationReady"])
        self.assertEqual(report["userAcceptance"], "PENDING")
        self.assertEqual(report["captureAuthenticity"], "NOT_VERIFIED")
        self.assertEqual(report["evidenceEligibility"], "NOT_ELIGIBLE_SYNTHETIC")
        self.assertEqual(before, {p.name: sha(p) for p in self.root.iterdir()})

    def test_empty_document_invents_no_players_or_defaults(self):
        self.data = {}
        self.save_data()
        report = self.report()
        self.assertEqual(report["inventoryStatus"], "COMPLETE")
        self.assertEqual(report["choices"], [])
        self.assertEqual(report["counts"]["players"], 0)

    def test_empty_player_choices_are_explicit_and_not_modern(self):
        self.data = {P1: {"modesetByWorld": {}}}
        self.save_data()
        report = self.report()
        self.assertEqual(report["choices"], [])
        self.assertEqual(report["counts"]["noSavedChoice"], 1)
        self.assertIn("NO_SAVED_SELECTION", self.codes(report))

    def test_missing_field_is_review_required(self):
        self.data = {P1: {}}
        self.save_data()
        report = self.report()
        self.assertEqual(report["inventoryStatus"], "REVIEW_REQUIRED")
        self.assertIn("MODESET_FIELD_MISSING", self.codes(report))

    def test_unknown_preset_is_preserved_without_guess(self):
        self.data[P1]["modesetByWorld"][W1] = "Cross-Era-Old"
        self.save_data()
        report = self.report()
        self.assertIn("UNKNOWN_MODESET", self.codes(report))
        self.assertEqual(report["choices"][0]["rawModeset"], "Cross-Era-Old")
        self.assertIsNone(report["choices"][0]["profile"])
        self.assertEqual(report["inventoryStatus"], "REVIEW_REQUIRED")

    def test_unknown_world_is_preserved_without_name_guess(self):
        self.manifest["worlds"] = []
        report = self.report()
        self.assertEqual(len(report["choices"]), 3)
        self.assertIsNone(report["choices"][0]["worldName"])
        self.assertIn("WORLD_UNMAPPED", self.codes(report))

    def test_uuid_case_is_normalised_but_raw_value_is_retained(self):
        self.data = {P1.upper(): {"modesetByWorld": {W1.upper(): "cross-era-old"}}}
        self.save_data()
        choice = self.report()["choices"][0]
        self.assertEqual(choice["playerId"], P1)
        self.assertEqual(choice["rawPlayerId"], P1.upper())
        self.assertEqual(choice["worldId"], W1)
        self.assertEqual(choice["rawWorldId"], W1.upper())

    def test_uuid_alias_collision_is_never_silently_merged(self):
        self.data[P1.upper()] = {"modesetByWorld": {W1: "cross-era-modern"}}
        self.save_data()
        report = self.report()
        self.assertIn("PLAYER_UUID_COLLISION", self.codes(report))
        self.assertEqual(report["counts"]["players"], 3)
        self.assertEqual(report["inventoryStatus"], "REVIEW_REQUIRED")

    def test_world_uuid_collision_and_invalid_uuid_are_blocked(self):
        self.data[P1]["modesetByWorld"][W1.upper()] = "cross-era-modern"
        self.data["bad-uuid"] = {"modesetByWorld": {"bad-world": "cross-era-old"}}
        self.save_data()
        report = self.report()
        self.assertTrue({"WORLD_UUID_COLLISION", "UUID_INVALID"}.issubset(self.codes(report)))
        self.assertEqual(report["counts"]["choices"], 5)

    def test_unknown_player_field_is_reported(self):
        self.data[P1]["futureField"] = "must not silently discard"
        self.save_data()
        self.assertIn("UNKNOWN_PLAYER_FIELD", self.codes(self.report()))

    def test_bad_shape_is_not_treated_as_empty(self):
        for data in ({P1: "bad"}, {P1: {"modesetByWorld": "bad"}},
                     {P1: {"modesetByWorld": {W1: {}}}}):
            with self.subTest(data=data):
                self.data = data
                self.save_data()
                self.assertEqual(self.report()["inventoryStatus"], "FORMAT_ERROR")

    def test_hash_mismatch_rejects_source(self):
        self.manifest["source"]["sha256"] = "0" * 64
        report = self.report()
        self.assertIn("HASH_MISMATCH", self.codes(report))
        self.assertEqual(report["choices"], [])

    def test_source_paths_are_local_and_contained(self):
        for value in ("../players.bson", "C:/players.bson", "//host/share/players.bson",
                      "\\\\host\\share\\players.bson", "players.bson:secret", "NUL", "CON.txt"):
            with self.subTest(value=value):
                self.manifest["source"]["path"] = value
                self.assertIn("SOURCE_PATH_INVALID", self.codes(self.report()))

    def test_manifest_unc_is_rejected_before_file_io(self):
        with patch.object(Path, "read_bytes", side_effect=AssertionError("must not read")):
            report = inventory_choices(Path("//host/share/manifest.json"))
        self.assertEqual(report["inventoryStatus"], "FORMAT_ERROR")

    def test_manifest_duplicate_keys_and_nonfinite_numbers_fail(self):
        for content in ('{"schemaVersion":1,"schemaVersion":1}', '{"v":NaN}', '{"v":1e400}'):
            self.path.write_text(content, encoding="utf-8")
            self.assertEqual(inventory_choices(self.path)["inventoryStatus"], "FORMAT_ERROR")

    def test_version_types_and_unknown_schema_fail(self):
        for key, value in (("schemaVersion", True), ("schemaVersion", 2), ("purpose", "GUESS")):
            with self.subTest(key=key, value=value):
                old = self.manifest[key]
                self.manifest[key] = value
                self.assertEqual(self.report()["inventoryStatus"], "FORMAT_ERROR")
                self.manifest[key] = old
        self.manifest["source"]["ocmVersion"] = "future"
        self.assertIn("OCM_VERSION_UNSUPPORTED", self.codes(self.report()))

    def test_profile_mapping_must_be_explicit_distinct_strings(self):
        for profiles in ({}, {"old": "x", "modern": "x"}, {"old": 1, "modern": "x"},
                         {"old": "x", "modern": "y", "other": "z"}):
            self.manifest["profiles"] = profiles
            self.assertEqual(self.report()["inventoryStatus"], "FORMAT_ERROR")

    def test_world_catalog_conflicts_fail(self):
        self.manifest["worlds"].append(dict(uuid=W1.upper(), name="different"))
        self.assertIn("WORLD_CATALOG_CONFLICT", self.codes(self.report()))

    def test_live_or_unknown_snapshot_cannot_be_ready(self):
        self.manifest["purpose"] = "REAL_COPY"
        for state in ("LIVE_COPY", "UNKNOWN"):
            self.manifest["source"]["snapshotState"] = state
            report = self.report()
            self.assertIn("SNAPSHOT_NOT_CLOSED", self.codes(report))
            self.assertEqual(report["inventoryStatus"], "REVIEW_REQUIRED")
            self.assertFalse(report["migrationReady"])

    def test_closed_real_copy_still_requires_manual_review(self):
        self.manifest["purpose"] = "REAL_COPY"
        self.manifest["source"]["snapshotState"] = "CLOSED_COPY"
        report = self.report()
        self.assertEqual(report["inventoryStatus"], "COMPLETE")
        self.assertEqual(report["evidenceEligibility"], "MANUAL_REVIEW_REQUIRED")
        self.assertFalse(report["migrationReady"])

    def test_purpose_snapshot_conflict_is_invalid(self):
        self.manifest["purpose"] = "REAL_COPY"
        self.assertIn("PROVENANCE_CONFLICT", self.codes(self.report()))

    def test_timestamp_and_build_hash_must_be_explicit(self):
        for key, value in (("capturedAt", "2026-11-05"), ("ocmJarSha256", None), ("configSha256", "bad")):
            old = self.manifest["source"][key]
            self.manifest["source"][key] = value
            self.assertEqual(self.report()["inventoryStatus"], "FORMAT_ERROR")
            self.manifest["source"][key] = old

    def test_known_bson_spec_bytes_decode_independently_of_encoder(self):
        # BSON specification's simple hello/world shape: total 22 bytes.
        raw = bytes.fromhex("160000000268656c6c6f0006000000776f726c640000")
        self.assertEqual(decode_ocm_bson(raw), {"hello": "world"})
        self.assertEqual(decode_ocm_bson(bytes.fromhex("0500000000")), {})

    def test_duplicate_bson_keys_fail_even_when_values_equal(self):
        self.save_data(bson_pairs([(P1, {}), (P1, {})]))
        self.assertIn("BSON_INVALID", self.codes(self.report()))

    def test_truncated_corrupt_or_trailing_bson_fails(self):
        good = self.source.read_bytes()
        for raw in (b"", good[:-1], good + b"\0", b"\xff\xff\xff\xff\0",
                    b"\x04\0\0\0\0", good[:-1] + b"x"):
            with self.subTest(raw=raw[:5]):
                self.save_data(raw)
                self.assertEqual(self.report()["inventoryStatus"], "FORMAT_ERROR")

    def test_unsupported_bson_type_never_skips_a_value(self):
        self.save_data(bytes.fromhex("0800000008780000"))  # Boolean is outside the supported schema.
        self.assertIn("BSON_UNSUPPORTED_TYPE", self.codes(self.report()))

    def test_invalid_utf8_and_string_terminator_fail(self):
        for raw in (bytes.fromhex("0e00000002780002000000ff0000"),
                    bytes.fromhex("0e00000002780002000000617800")):
            self.save_data(raw)
            self.assertIn("BSON_INVALID", self.codes(self.report()))

    def test_nested_length_cannot_escape_parent(self):
        raw = bytearray(bson_pairs([("x", {})]))
        struct.pack_into("<i", raw, 7, 100)
        self.save_data(bytes(raw))
        self.assertIn("BSON_INVALID", self.codes(self.report()))

    def test_depth_and_size_limits_are_enforced(self):
        data = {}
        for _ in range(12):
            data = {"nested": data}
        self.save_data(bson_pairs(data.items()))
        self.assertIn("BSON_LIMIT", self.codes(self.report()))
        with patch("inventory_choices.MAX_SOURCE_BYTES", 8):
            self.assertIn("SOURCE_TOO_LARGE", self.codes(self.report()))

    def test_modified_input_invalidates_result(self):
        with patch("inventory_choices.file_hash", return_value="0" * 64):
            report = self.report()
        self.assertEqual(report["inventoryStatus"], "FORMAT_ERROR")
        self.assertIn("INPUT_CHANGED", self.codes(report))

    def test_manifest_surrogate_and_unknown_field_are_rejected(self):
        self.path.write_text('{"value":"\\ud800"}', encoding="utf-8")
        self.assertEqual(inventory_choices(self.path)["inventoryStatus"], "FORMAT_ERROR")
        self.manifest["extra"] = "must not be ignored"
        self.assertEqual(self.report()["inventoryStatus"], "FORMAT_ERROR")

    def test_diagnostic_limit_retains_review_status_and_all_rows(self):
        self.manifest["worlds"] = []
        self.data = {f"{i:08x}-0000-0000-0000-000000000001": {"modesetByWorld": {W1: "unknown"}}
                     for i in range(110)}
        self.save_data()
        report = self.report()
        self.assertEqual(report["inventoryStatus"], "REVIEW_REQUIRED")
        self.assertEqual(len(report["diagnostics"]), 200)
        self.assertEqual(report["omittedDiagnostics"], 20)
        self.assertEqual(len(report["choices"]), 110)

    def test_element_and_string_limit_reject_whole_document(self):
        with patch("inventory_choices.MAX_ELEMENTS", 1):
            self.assertIn("BSON_LIMIT", self.codes(self.report()))
        self.data = {P1: {"modesetByWorld": {W1: "x" * 150000}}}
        self.save_data()
        self.assertIn("BSON_LIMIT", self.codes(self.report()))

    def test_symlink_escape_uses_resolved_containment_check(self):
        # Mock resolution so this is deterministic without Windows symlink privilege.
        # This does not claim a real junction/symlink was created.
        from inventory_choices import contained_source
        with patch("inventory_choices.resolve_local", return_value=self.root.parent / "outside.bson"):
            with self.assertRaises(ValueError):
                contained_source(self.root, "link/players.bson")

    def test_missing_source_and_directory_source_are_errors(self):
        for value in ("missing.bson", "."):
            self.manifest["source"]["path"] = value
            self.assertEqual(self.report()["inventoryStatus"], "FORMAT_ERROR")

    def cli(self, output):
        self.save_manifest()
        return subprocess.run([sys.executable, "-B", str(Path(__file__).with_name("inventory_choices.py")),
            "--manifest", str(self.path), "--output", str(output)], capture_output=True, text=True, encoding="utf-8")

    def test_cli_writes_new_report_without_printing_player_ids(self):
        result = self.cli(self.root / "report.json")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotIn(P1, result.stdout + result.stderr)
        self.assertEqual(json.loads((self.root / "report.json").read_text(encoding="utf-8"))["counts"]["choices"], 3)

    def test_cli_rejects_overwrite_and_outside_output(self):
        before = sha(self.source)
        for output in (self.source, self.path, self.root.parent / "must-not-create.json"):
            self.assertEqual(self.cli(output).returncode, 2)
        self.assertEqual(sha(self.source), before)
        self.assertFalse((self.root.parent / "must-not-create.json").exists())

    def test_cli_exit_codes_for_review_and_format_error(self):
        self.manifest["worlds"] = []
        self.assertEqual(self.cli(self.root / "review.json").returncode, 1)
        self.manifest["schemaVersion"] = 2
        self.assertEqual(self.cli(self.root / "invalid.json").returncode, 2)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--temp-root", required=True, type=Path)
    args, remaining = parser.parse_known_args()
    TEMP_ROOT = args.temp_root.resolve()
    if not TEMP_ROOT.is_relative_to(Path(__file__).resolve().parents[4] / "runtime"):
        parser.error("[草人杯] 测试材料必须保存在本工作树runtime内。")
    TEMP_ROOT.mkdir(parents=True, exist_ok=True)
    unittest.main(argv=[sys.argv[0], *remaining])
