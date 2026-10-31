using System.Net;
using System.Numerics;

namespace Caoren.AbilityMode;

public sealed record CommanderHudResult(string Text, int VisibleEnemyCount, bool IsUltimateView);

public sealed class CommanderRoleHandler
{
    public const string UltimateStateKey = "指挥官主动";

    public AbilityEffectResult ActivateUltimate(
        AbilitySeatState seat,
        DateTimeOffset now,
        bool hasAliveEnemies = true)
    {
        seat.RecoverableUntil[UltimateStateKey] = now.AddSeconds(10);
        return AbilityEffectResult.Succeeded(emptyCast: !hasAliveEnemies);
    }

    public CommanderHudResult BuildHud(
        AbilityRolePlayer commander,
        IEnumerable<AbilityRolePlayer> players,
        DateTimeOffset now)
    {
        if (!commander.IsAlive || commander.Seat.AbilityId != "commander")
            return new(string.Empty, 0, false);
        var enemies = players
            .Where(player => player.IsAlive && player.Seat.RosterTeam != commander.Seat.RosterTeam)
            .Select(player => new
            {
                Player = player,
                Distance = RoundDistance(Vector3.Distance(commander.Position, player.Position)),
            })
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Player.Seat.SeatId, StringComparer.Ordinal)
            .ToArray();
        var active = commander.Seat.RecoverableUntil.TryGetValue(UltimateStateKey, out var until) && until > now;
        if (active)
        {
            var text = string.Join("<br>", enemies.Select(item =>
                $"{WebUtility.HtmlEncode(item.Player.DisplayName)}：{item.Distance} 游戏单位"));
            return new(text, enemies.Length, true);
        }
        if (enemies.Length == 0) return new("附近没有存活敌人", 0, false);
        return new($"最近敌人距离：{enemies[0].Distance} 游戏单位", 1, false);
    }

    public void Clear(AbilitySeatState seat) => seat.RecoverableUntil.Remove(UltimateStateKey);

    private static int RoundDistance(float distance) =>
        Math.Max(0, (int)Math.Round(distance, MidpointRounding.AwayFromZero));
}
