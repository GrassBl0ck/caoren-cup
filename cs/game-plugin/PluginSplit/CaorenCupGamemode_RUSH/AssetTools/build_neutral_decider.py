import argparse
import hashlib
from pathlib import Path
import struct


EXPECTED_SOURCE_SHA256 = "d38bcfac1db90565647e1a4bd2c1b9e49564a94d5fd06e15466105c44d692f98"
parser = argparse.ArgumentParser(description="Build the 7:7 neutral Convoy script from an installed CS2 resource.")
parser.add_argument("original_vjs_c", type=Path)
parser.add_argument("output_vjs_c", type=Path)
parser.add_argument("--source-output", type=Path, help="Optional local review copy; do not add Valve source to Git.")
args = parser.parse_args()

compiled = args.original_vjs_c.read_bytes()
actual_hash = hashlib.sha256(compiled).hexdigest()
if actual_hash != EXPECTED_SOURCE_SHA256:
    raise ValueError(f"Unexpected CS2 script SHA-256: {actual_hash}")
if struct.unpack_from("<I", compiled, 0)[0] != len(compiled):
    raise ValueError("Invalid Source 2 resource length")

block_count = struct.unpack_from("<I", compiled, 12)[0]
data_start = data_size_offset = data_length = None
for index in range(block_count):
    block = 16 + index * 12
    if compiled[block:block + 4] == b"DATA":
        data_size_offset = block + 8
        data_start = block + 4 + struct.unpack_from("<I", compiled, block + 4)[0]
        data_length = struct.unpack_from("<I", compiled, data_size_offset)[0]
        break
if data_start is None or data_size_offset is None or data_length is None:
    raise ValueError("DATA block not found")
if data_start + data_length != len(compiled):
    raise ValueError("Unexpected resource layout; review the new CS2 script before patching")

original = compiled[data_start:].decode("utf-8").replace("\r\n", "\n")
modified = original


def replace_once(before: str, after: str) -> None:
    global modified
    count = modified.count(before)
    if count != 1:
        raise ValueError(f"Expected one replacement, found {count}: {before[:80]!r}")
    modified = modified.replace(before, after, 1)


replace_once(
    "\tResetGameState();\n});",
    "\tResetGameState();\n"
    "\tInstance.Msg('[CaorenCup RUSH] neutral decider script loaded');\n"
    "});",
)
replace_once(
    "\t_lastRoundWinner = args.winningTeam;",
    "\t_lastRoundWinner = args.winningTeam;\n"
    "\tif (_roomIds[_currentRoomIndex] == DECIDER_ROOM_ID\n"
    "\t\t&& (_teamWins[TEAM_T] >= ROUNDS_TO_WIN || _teamWins[TEAM_CT] >= ROUNDS_TO_WIN)) {\n"
    "\t\tEndMatch(args.winningTeam);\n"
    "\t\treturn;\n"
    "\t}",
)
replace_once(
    "\t_roomIds[roomIndex] = DECIDER_ROOM_ID;",
    "\t_roomIds[roomIndex] = DECIDER_ROOM_ID;\n"
    "\tif (_teamWins[TEAM_T] == ROUNDS_TO_WIN - 1\n"
    "\t\t&& _teamWins[TEAM_CT] == ROUNDS_TO_WIN - 1) {\n"
    "\t\tSetRoomControl(roomIndex, TEAM_NONE, false, false);\n"
    "\t\t// Extend before round-end processing, then extend again after each draw.\n"
    "\t\tInstance.ServerCommand(`mp_maxrounds ${Instance.GetRoundsPlayed() + 3}`);\n"
    "\t}",
)
replace_once(
    "const TEAM_ANTENNA_SKINS = {",
    "const NEUTRAL_DECIDER_WHITE = { r: 255, g: 255, b: 255 };\n"
    "function TowerColorForRoom(roomIndex, team) {\n"
    "\treturn team == TEAM_NONE && _roomIds[roomIndex] == DECIDER_ROOM_ID\n"
    "\t\t? NEUTRAL_DECIDER_WHITE\n"
    "\t\t: (TEAM_COLORS[team] ?? TEAM_COLORS[TEAM_NONE]);\n"
    "}\n\n"
    "const TEAM_ANTENNA_SKINS = {",
)
replace_once(
    "\tconst teamColor = TEAM_COLORS[team];",
    "\tconst teamColor = TowerColorForRoom(roomIndex, team);",
)
replace_once(
    "\tconst glowColor = TEAM_COLORS[team] ?? TEAM_COLORS[TEAM_NONE];",
    "\tconst glowColor = TowerColorForRoom(_currentRoomIndex, team);",
)
replace_once(
    "\tconst owningTeam = _roomStates[_currentRoomIndex];\n"
    "\tif (!END_ROUND_ON_TEAM_ELIMINATION && IsTeamEliminated(_roomStates[_currentRoomIndex]) && !_countdownActive)",
    "\tconst owningTeam = _roomStates[_currentRoomIndex];\n"
    "\tconst tWiped = IsTeamEliminated(TEAM_T);\n"
    "\tconst ctWiped = IsTeamEliminated(TEAM_CT);\n"
    "\tconst wipedTeamForCountdown = owningTeam == TEAM_NONE\n"
    "\t\t&& _roomIds[_currentRoomIndex] == DECIDER_ROOM_ID\n"
    "\t\t? (tWiped != ctWiped ? (tWiped ? TEAM_T : TEAM_CT) : TEAM_NONE)\n"
    "\t\t: owningTeam;\n"
    "\tif (!END_ROUND_ON_TEAM_ELIMINATION && wipedTeamForCountdown != TEAM_NONE\n"
    "\t\t&& IsTeamEliminated(wipedTeamForCountdown) && !_countdownActive)",
)
replace_once(
    "_legitimateKillsPerTeamThisRound[OtherTeam(owningTeam)] && _legitimateKillsPerTeamThisRound[OtherTeam(owningTeam)] > 0",
    "_legitimateKillsPerTeamThisRound[OtherTeam(wipedTeamForCountdown)] && _legitimateKillsPerTeamThisRound[OtherTeam(wipedTeamForCountdown)] > 0",
)
replace_once(
    "\t\tdefault: return CSRadarColor.GRAY;",
    "\t\tdefault: return team == TEAM_NONE\n"
    "\t\t\t&& _roomIds[_currentRoomIndex] == DECIDER_ROOM_ID\n"
    "\t\t\t? CSRadarColor.WHITE : CSRadarColor.GRAY;",
)
replace_once(
    "\t\tconst spectatorString = _roomStates[_currentRoomIndex] == TEAM_T ? \"#rush_countdown_capture_ct\" : \"#rush_countdown_capture_t\";",
    "\t\tconst spectatorString = _roomStates[_currentRoomIndex] == TEAM_NONE\n"
    "\t\t\t? \"#rush_countdown_capture\"\n"
    "\t\t\t: (_roomStates[_currentRoomIndex] == TEAM_T\n"
    "\t\t\t\t? \"#rush_countdown_capture_ct\" : \"#rush_countdown_capture_t\");",
)
replace_once(
    "\tconst teamNum = controller.GetTeamNumber();\n\n\tconst attackText = \"#rush_hint_enemy_tower\";",
    "\tconst teamNum = controller.GetTeamNumber();\n\n"
    "\tif (teamInControl == TEAM_NONE) {\n"
    "\t\t_uiEntity.SetHasClassForPlayer(controller.GetPlayerSlot(), 'rush_attack_defend', 'ct', false);\n"
    "\t\t_uiEntity.SetHasClassForPlayer(controller.GetPlayerSlot(), 'rush_attack_defend', 't', false);\n"
    "\t\t_uiEntity.SetHasClassForPlayer(controller.GetPlayerSlot(), 'rush_attack_defend', 'attack', false);\n"
    "\t\t_uiEntity.SetHasClassForPlayer(controller.GetPlayerSlot(), 'rush_attack_defend', 'defend', false);\n"
    "\t\t_uiEntity.SetDialogVariableStringForPlayer(controller.GetPlayerSlot(), 'rush_attack_defend', 'attack_defend', '#rush_countdown_capture');\n"
    "\t\treturn;\n"
    "\t}\n\n"
    "\tconst attackText = \"#rush_hint_enemy_tower\";",
)

source_bytes = modified.encode("utf-8")
rebuilt = bytearray(compiled[:data_start] + source_bytes)
struct.pack_into("<I", rebuilt, 0, len(rebuilt))
struct.pack_into("<I", rebuilt, data_size_offset, len(source_bytes))
if not args.output_vjs_c.parent.is_dir() or args.output_vjs_c.exists():
    raise ValueError("Output directory must exist and output file must not already exist")
args.output_vjs_c.write_bytes(rebuilt)
if args.source_output:
    if not args.source_output.parent.is_dir() or args.source_output.exists():
        raise ValueError("Source output directory must exist and file must not already exist")
    args.source_output.write_bytes(source_bytes)
print(f"Original source: {len(original.encode('utf-8'))} bytes")
print(f"Modified source: {len(source_bytes)} bytes")
print(f"Modified resource: {len(rebuilt)} bytes")
print(f"Modified SHA-256: {hashlib.sha256(rebuilt).hexdigest()}")
