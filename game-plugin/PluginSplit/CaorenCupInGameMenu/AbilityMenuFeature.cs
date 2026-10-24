using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.Features.InGameMenu;

/// <summary>
/// 管理员菜单的异能职业分配页:在线玩家列表 → 职业列表。
/// 布局是静态行,因此用固定 6 个占位行 + 变量填充实现数据驱动,未使用的行整行隐藏。
/// </summary>
public sealed partial class CaorenCupInGameMenuPlugin
{
    private const int AbilityMenuSlots = 6;

    private enum AbilityMenuMode { Players, Roles }

    private sealed class AbilityMenuState
    {
        public AbilityMenuMode Mode = AbilityMenuMode.Players;
        public int Page;
        public string TargetSteamId = string.Empty;
        public string TargetName = string.Empty;
    }

    /// <summary>一行的完整文案、悬停提示与点击后要下发的数据。Row 是按钮上直接可见的文字。</summary>
    private readonly record struct AbilityMenuRow(string Key, string Name, string Row, string Hint, string Button, string AbilityId);

    private readonly Dictionary<string, AbilityMenuState> _abilityMenuStates = new();
    private double _nextAbilityRefresh;

    private AbilityMenuState GetAbilityMenuState(string steamId)
    {
        if (!_abilityMenuStates.TryGetValue(steamId, out var state))
        {
            state = new AbilityMenuState();
            _abilityMenuStates[steamId] = state;
        }
        return state;
    }

    private static void ResetAbilityMenuState(AbilityMenuState state)
    {
        state.Mode = AbilityMenuMode.Players;
        state.Page = 0;
        state.TargetSteamId = string.Empty;
        state.TargetName = string.Empty;
    }

    private bool HandleAdminAbilityClick(CCSPlayerController player, string buttonId)
    {
        var sid = player.SteamID.ToString();
        var view = _adminVoteViews.GetValueOrDefault(sid);
        if (buttonId == "admin_ability_open")
        {
            if (view == AdminVoteView.Home)
            {
                ResetAbilityMenuState(GetAbilityMenuState(sid));
                SetAdminVoteView(player, AdminVoteView.Ability);
            }
            return true;
        }
        if (view != AdminVoteView.Ability) return false;
        if (!IsVoteAdministrator(player)) return true;

        var state = GetAbilityMenuState(sid);
        switch (buttonId)
        {
            case "ability_back":
                if (state.Mode == AbilityMenuMode.Roles)
                {
                    ResetAbilityMenuState(state);
                    RenderAbilityMenu(player);
                }
                else SetAdminVoteView(player, AdminVoteView.Home);
                return true;
            case "ability_prev":
                state.Page = Math.Max(0, state.Page - 1);
                RenderAbilityMenu(player);
                return true;
            case "ability_next":
                state.Page++;
                RenderAbilityMenu(player);
                return true;
            case "ability_clear":
                if (state.Mode != AbilityMenuMode.Players) return true;
                ExecAsPlayer(sid, "css_ability_roles_clear");
                RenderAbilityMenu(player);
                return true;
        }

        if (!buttonId.StartsWith("ability_row_", StringComparison.Ordinal)
            || !int.TryParse(buttonId.AsSpan(12), out var slot)) return false;

        var rows = BuildAbilityRows(state);
        var offset = state.Page * AbilityMenuSlots;
        if (slot < 0 || offset + slot >= rows.Count) return true;
        var row = rows[offset + slot];

        if (state.Mode == AbilityMenuMode.Players)
        {
            state.Mode = AbilityMenuMode.Roles;
            state.Page = 0;
            state.TargetSteamId = row.Key;
            state.TargetName = row.Name;
            RenderAbilityMenu(player);
            return true;
        }

        ExecAsPlayer(sid, $"css_ability_role {state.TargetSteamId} {row.AbilityId}");
        ResetAbilityMenuState(state);
        RenderAbilityMenu(player);
        return true;
    }

    private List<AbilityMenuRow> BuildAbilityRows(AbilityMenuState state)
    {
        var ability = CaorenCupAbilityAccess.TryGet();
        if (ability is null) return [];

        if (state.Mode == AbilityMenuMode.Players)
        {
            // 行内直接显示当前职业的显示名，不再依赖需要鼠标悬停才会出现的提示。
            var nameById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var role in ability.Roles)
                nameById[role.AbilityId] = role.DisplayName;

            var rows = new List<AbilityMenuRow>();
            foreach (var seat in ability.OnlinePlayers)
            {
                var hasRole = !string.IsNullOrEmpty(seat.AbilityId);
                var roleName = hasRole
                    ? nameById.GetValueOrDefault(seat.AbilityId, seat.AbilityId)
                    : "无";
                var displayName = seat.IsBot ? $"{seat.PlayerName} [BOT]" : seat.PlayerName;
                // 命令统一用 #userId 定位：bot 名字可能带空格，按名字下发会被命令参数拆分。
                var commandTarget = seat.UserId > 0 ? $"#{seat.UserId}" : seat.SteamId;
                rows.Add(new AbilityMenuRow(
                    commandTarget,
                    displayName,
                    $"{displayName} · {roleName} · {(hasRole ? "更改" : "分配")}",
                    hasRole ? $"当前职业：{roleName} · 点击后可更改" : "还没有分配职业 · 点击后选择",
                    hasRole ? "更改" : "分配",
                    seat.AbilityId));
            }
            return rows;
        }

        return ability.Roles
            .Where(role => role.Enabled)
            .OrderBy(role => role.AbilityId, StringComparer.Ordinal)
            .Select(role => new AbilityMenuRow(
                role.AbilityId,
                role.DisplayName,
                $"{role.DisplayName} · 选择",
                $"职业 ID：{role.AbilityId}",
                "选择",
                role.AbilityId))
            .ToList();
    }

    private void RenderAbilityMenu(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString();
        var state = GetAbilityMenuState(sid);
        var ability = CaorenCupAbilityAccess.TryGet();

        _surface.SetVariable(player, "ability_state", ability is null
            ? "CaorenCupGamemode_Ability 未加载,无法分配职业"
            : ability.StatusText);

        var rows = BuildAbilityRows(state);
        var pageCount = Math.Max(1, (rows.Count + AbilityMenuSlots - 1) / AbilityMenuSlots);
        state.Page = Math.Clamp(state.Page, 0, pageCount - 1);
        var offset = state.Page * AbilityMenuSlots;

        var header = state.Mode == AbilityMenuMode.Players
            ? "异能职业分配"
            : $"职业 · {state.TargetName}";
        var empty = state.Mode == AbilityMenuMode.Players
            ? "当前没有可分配的在线玩家(观察位不计入)"
            : "没有已放行的职业,请先在 ability-roles.json 中开启";
        _surface.SetVariable(player, "ability_page", rows.Count == 0
            ? empty
            : $"{header} · 第 {state.Page + 1}/{pageCount} 页 · 共 {rows.Count} 项");

        for (var i = 0; i < AbilityMenuSlots; i++)
        {
            var index = offset + i;
            var has = index < rows.Count;
            _surface.SetVariable(player, "ability_row" + i, has ? rows[index].Row : "");
            _surface.SetVariable(player, "ability_hint" + i, has ? rows[index].Hint : "");
            _surface.SetClass(player, "ability_row_" + i, "Hidden", !has);
        }

        _surface.SetClass(player, "ability_prev_host", "Hidden", state.Page == 0);
        _surface.SetClass(player, "ability_next_host", "Hidden", state.Page >= pageCount - 1);
        _surface.SetClass(player, "ability_clear_host", "Hidden", state.Mode != AbilityMenuMode.Players || ability is null);
        _surface.SetClass(player, "ability_back_host", "Hidden", state.Mode != AbilityMenuMode.Roles);
        _surface.SetVariable(player, "ability_back_label", "返回玩家列表");
    }

    /// <summary>在线玩家或职业列表变化时刷新当前停留在异能页的管理员;玩家列表每 1 秒最多刷新一次。</summary>
    private void RefreshAbilityMenus()
    {
        if (VoteNow < _nextAbilityRefresh) return;
        _nextAbilityRefresh = VoteNow + 1.0;
        foreach (var player in VoteHumans())
        {
            var sid = player.SteamID.ToString();
            if (_adminVoteViews.GetValueOrDefault(sid) != AdminVoteView.Ability) continue;
            if (!IsVoteAdministrator(player)) continue;
            RenderAbilityMenu(player);
        }
    }
}
