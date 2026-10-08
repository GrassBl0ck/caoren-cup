using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private const int PlayerStatsColumnsPerPage = 4;
    private const int PlayerStatsRowCapacity = 10;
    private static readonly string[] PlayerStatsHeaders =
    {
        "受到伤害",
        "平均每回合",
        "受到道具伤害",
        "总共射击",
        "命中次数",
        "命中百分比",
    };

    private PlayerStatsTracker _playerStats = null!;

    private void RegisterPlayerStatsHooks()
    {
        _playerStats = new PlayerStatsTracker();
        _playerStats.Changed += RefreshOpenPlayerStatsMenus;
        RegisterEventHandler<EventRoundStart>(OnPlayerStatsRoundStart);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerStatsPlayerHurt);
        RegisterEventHandler<EventWeaponFire>(OnPlayerStatsWeaponFire);
    }

    private HookResult OnPlayerStatsRoundStart(EventRoundStart _, GameEventInfo __)
    {
        _playerStats.BeginRound(Utilities.GetPlayers()
            .Where(IsHuman)
            .Where(player => player.TeamNum is 2 or 3)
            .Select(player => new PlayerStatsParticipant(
                player.SteamID.ToString(), player.PlayerName, player.TeamNum)));
        return HookResult.Continue;
    }

    private HookResult OnPlayerStatsPlayerHurt(EventPlayerHurt @event, GameEventInfo _)
    {
        var victim = @event.Userid;
        if (victim == null || !IsHuman(victim)) return HookResult.Continue;

        var attacker = @event.Attacker;
        var attackerId = attacker != null && IsHuman(attacker) ? attacker.SteamID.ToString() : null;
        _playerStats.RegisterPlayer(
            victim.SteamID.ToString(), victim.PlayerName, victim.TeamNum);
        if (attacker != null && IsHuman(attacker))
        {
            _playerStats.RegisterPlayer(attacker.SteamID.ToString(), attacker.PlayerName, attacker.TeamNum);
        }

        _playerStats.RecordDamage(
            victim.SteamID.ToString(),
            @event.DmgHealth,
            @event.Weapon,
            attackerId,
            victim.TeamNum,
            attacker?.TeamNum ?? 0);
        return HookResult.Continue;
    }

    private HookResult OnPlayerStatsWeaponFire(EventWeaponFire @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player == null || !IsHuman(player)) return HookResult.Continue;

        _playerStats.RegisterPlayer(player.SteamID.ToString(), player.PlayerName, player.TeamNum);
        _playerStats.RecordShot(player.SteamID.ToString(), @event.Weapon);
        return HookResult.Continue;
    }

    private void ResetPlayerStatsForMap()
    {
        _playerStats.Reset();
        _personalStatsPages.Clear();
    }

    private void TrackDisconnectedPlayer(string steamId) => _playerStats.MarkDisconnected(steamId);

    private void RefreshOpenPlayerStatsMenus()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !IsHuman(player)) continue;
            if (_personalViews.GetValueOrDefault(player.SteamID.ToString()) == PersonalView.Stats)
            {
                RenderPersonalStats(player);
            }
        }
    }

    private void RenderPersonalStats(CCSPlayerController player)
    {
        SyncOnlinePlayerStats();
        var sid = player.SteamID.ToString();
        var rows = _playerStats.Snapshot();
        var pageCount = GetPlayerStatsPageCount();
        var page = Math.Clamp(_personalStatsPages.GetValueOrDefault(sid), 0, pageCount - 1);
        _personalStatsPages[sid] = page;
        var columnStart = page * PlayerStatsColumnsPerPage;

        _personalSurface.SetVariable(player, "player_stats_page", $"{page + 1}/{pageCount} 页 · {rows.Count} 名玩家");
        _personalSurface.SetClass(player, "player_stats_prev", "Hidden", page == 0);
        _personalSurface.SetClass(player, "player_stats_next", "Hidden", page >= pageCount - 1);

        for (var column = 0; column < PlayerStatsColumnsPerPage; column++)
        {
            var metricIndex = columnStart + column;
            var visible = metricIndex < PlayerStatsHeaders.Length;
            _personalSurface.SetVariable(player, $"player_stats_header{column}", visible ? PlayerStatsHeaders[metricIndex] : "");
            _personalSurface.SetClass(player, $"player_stats_header_{column}", "Hidden", !visible);
        }

        for (var rowIndex = 0; rowIndex < PlayerStatsRowCapacity; rowIndex++)
        {
            var row = rowIndex < rows.Count ? rows[rowIndex] : null;
            _personalSurface.SetClass(player, $"player_stats_row_{rowIndex}", "Hidden", row == null);
            if (row == null) continue;

            _personalSurface.SetVariable(player, $"player_stats_name{rowIndex}", FormatPlayerStatsName(row));
            for (var column = 0; column < PlayerStatsColumnsPerPage; column++)
            {
                var metricIndex = columnStart + column;
                _personalSurface.SetVariable(
                    player,
                    $"player_stats_value{rowIndex}_{column}",
                    metricIndex < PlayerStatsHeaders.Length ? FormatPlayerStatsValue(row, metricIndex) : "");
            }
        }
    }

    private void SyncOnlinePlayerStats()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsHuman(player)) continue;
            _playerStats.RegisterPlayer(
                player.SteamID.ToString(), player.PlayerName, player.TeamNum, notify: false);
        }
    }

    private static int GetPlayerStatsPageCount() =>
        (PlayerStatsHeaders.Length + PlayerStatsColumnsPerPage - 1) / PlayerStatsColumnsPerPage;

    private static string FormatPlayerStatsName(PlayerStatsRow row)
    {
        var team = row.Team switch
        {
            2 => "T",
            3 => "CT",
            1 => "观战",
            _ => "",
        };
        var suffix = row.Connected ? string.Empty : " · 离线";
        return string.IsNullOrEmpty(team) ? row.Name + suffix : $"{team} · {row.Name}{suffix}";
    }

    private static string FormatPlayerStatsValue(PlayerStatsRow row, int metricIndex) => metricIndex switch
    {
        0 => row.TotalDamage.ToString(),
        1 => row.FormatAverageDamage(),
        2 => row.UtilityDamage.ToString(),
        3 => row.Shots.ToString(),
        4 => row.Hits.ToString(),
        5 => row.FormatHitPercentage(),
        _ => string.Empty,
    };
}
