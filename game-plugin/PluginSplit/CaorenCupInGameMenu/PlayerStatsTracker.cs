using System.Globalization;

namespace CaorenCup.Features.InGameMenu;

/// <summary>地图内玩家趣味数据的纯内存统计器，不依赖 CounterStrikeSharp，便于单元测试。</summary>
public sealed class PlayerStatsTracker
{
    private static readonly HashSet<string> BulletWeapons = new(StringComparer.OrdinalIgnoreCase)
    {
        "ak47", "aug", "awp", "bizon", "cz75a", "deagle", "elite", "famas",
        "fiveseven", "g3sg1", "galilar", "glock", "hkp2000", "m249", "m4a1",
        "m4a1_silencer", "mac10", "mag7", "mp5sd", "mp7", "mp9", "negev",
        "nova", "p250", "p90", "revolver", "sawedoff", "scar20", "sg556",
        "ssg08", "tec9", "ump45", "usp_silencer", "xm1014",
    };

    private static readonly HashSet<string> UtilityDamageWeapons = new(StringComparer.OrdinalIgnoreCase)
    {
        "hegrenade", "molotov", "incgrenade", "inferno", "smokegrenade",
        "flashbang", "decoy",
    };

    private readonly Dictionary<string, PlayerStatsState> _players = new(StringComparer.Ordinal);

    public event Action? Changed;

    public void RegisterPlayer(string steamId, string name, int team, bool connected = true, bool notify = true)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return;

        var changed = false;
        if (!_players.TryGetValue(steamId, out var state))
        {
            state = new PlayerStatsState(steamId);
            _players.Add(steamId, state);
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(state.Name, name, StringComparison.Ordinal))
        {
            state.Name = name;
            changed = true;
        }
        if (state.Team != team)
        {
            state.Team = team;
            changed = true;
        }
        if (state.Connected != connected)
        {
            state.Connected = connected;
            changed = true;
        }
        if (notify && changed) Changed?.Invoke();
    }

    public void MarkDisconnected(string steamId)
    {
        if (!_players.TryGetValue(steamId, out var state)) return;
        if (!state.Connected) return;
        state.Connected = false;
        Changed?.Invoke();
    }

    public void BeginRound(IEnumerable<PlayerStatsParticipant> participants)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var participant in participants)
        {
            if (!seen.Add(participant.SteamId)) continue;
            RegisterPlayer(participant.SteamId, participant.Name, participant.Team, connected: true);
            if (_players.TryGetValue(participant.SteamId, out var state)) state.RoundsParticipated++;
        }

        Changed?.Invoke();
    }

    public void RecordShot(string steamId, string? weapon)
    {
        if (!IsBulletWeapon(weapon)) return;
        var state = GetOrCreate(steamId);
        state.Shots++;
        Changed?.Invoke();
    }

    public void RecordDamage(
        string victimSteamId,
        int damage,
        string? weapon,
        string? attackerSteamId,
        int victimTeam,
        int attackerTeam)
    {
        if (damage <= 0 || string.IsNullOrWhiteSpace(victimSteamId)) return;

        var victim = GetOrCreate(victimSteamId);
        victim.TotalDamage += damage;
        if (IsUtilityDamageWeapon(weapon)) victim.UtilityDamage += damage;

        if (IsBulletWeapon(weapon)
            && !string.IsNullOrWhiteSpace(attackerSteamId)
            && !string.Equals(victimSteamId, attackerSteamId, StringComparison.Ordinal)
            && IsEnemyTeam(victimTeam, attackerTeam))
        {
            GetOrCreate(attackerSteamId).Hits++;
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<PlayerStatsRow> Snapshot() => _players.Values
        .OrderBy(state => TeamSortOrder(state.Team))
        .ThenBy(state => state.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(state => state.SteamId, StringComparer.Ordinal)
        .Select(state => new PlayerStatsRow(
            state.SteamId,
            string.IsNullOrWhiteSpace(state.Name) ? state.SteamId : state.Name,
            state.Team,
            state.Connected,
            state.TotalDamage,
            state.UtilityDamage,
            state.RoundsParticipated,
            state.Shots,
            state.Hits))
        .ToArray();

    public void Reset()
    {
        if (_players.Count == 0) return;
        _players.Clear();
        Changed?.Invoke();
    }

    public static bool IsBulletWeapon(string? weapon) => BulletWeapons.Contains(NormalizeWeapon(weapon));

    public static bool IsUtilityDamageWeapon(string? weapon) => UtilityDamageWeapons.Contains(NormalizeWeapon(weapon));

    private static string NormalizeWeapon(string? weapon)
    {
        var name = weapon?.Trim() ?? string.Empty;
        return name.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? name[7..] : name;
    }

    private PlayerStatsState GetOrCreate(string steamId)
    {
        if (!_players.TryGetValue(steamId, out var state))
        {
            state = new PlayerStatsState(steamId);
            _players.Add(steamId, state);
        }

        return state;
    }

    private static bool IsEnemyTeam(int victimTeam, int attackerTeam) =>
        victimTeam is 2 or 3 && attackerTeam is 2 or 3 && victimTeam != attackerTeam;

    private static int TeamSortOrder(int team) => team switch
    {
        1 => 0,
        2 => 1,
        3 => 2,
        _ => 3,
    };

    private sealed class PlayerStatsState(string steamId)
    {
        public string SteamId { get; } = steamId;
        public string Name { get; set; } = steamId;
        public int Team { get; set; }
        public bool Connected { get; set; }
        public int TotalDamage { get; set; }
        public int UtilityDamage { get; set; }
        public int RoundsParticipated { get; set; }
        public int Shots { get; set; }
        public int Hits { get; set; }
    }
}

public sealed record PlayerStatsParticipant(string SteamId, string Name, int Team);

public sealed record PlayerStatsRow(
    string SteamId,
    string Name,
    int Team,
    bool Connected,
    int TotalDamage,
    int UtilityDamage,
    int RoundsParticipated,
    int Shots,
    int Hits)
{
    public double? AverageDamagePerRound => RoundsParticipated > 0
        ? (double)TotalDamage / RoundsParticipated
        : null;

    public double? HitPercentage => Shots > 0
        ? (double)Hits * 100d / Shots
        : null;

    public string FormatAverageDamage() => AverageDamagePerRound?.ToString("F1", CultureInfo.InvariantCulture) ?? "—";

    public string FormatHitPercentage() => HitPercentage is { } value
        ? value.ToString("F1", CultureInfo.InvariantCulture) + "%"
        : "—";
}
