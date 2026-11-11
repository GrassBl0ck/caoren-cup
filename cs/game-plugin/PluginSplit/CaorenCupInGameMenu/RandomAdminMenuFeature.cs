using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private const int RandomAdminSlots = 6;
    private enum RandomAdminPage { Players, Buffs, Detail, Actions, Levels, Filter, Clear }
    private sealed class RandomAdminMenuState
    {
        public RandomAdminPage Page;
        public int Index;
        public string Target = "";
        public string Buff = "";
        public string Quality = "";
        public string Operation = "";
        public long Revision;
        // 点击按上一次实际显示的身份解析；列表刷新或离线不能把点击错配给另一位玩家。
        public string[] VisibleKeys = [];
    }

    private readonly Dictionary<string, RandomAdminMenuState> _randomAdminMenus = new();
    private double _nextRandomAdminRefresh;

    private RandomAdminMenuState RandomAdminState(string id)
    {
        if (!_randomAdminMenus.TryGetValue(id, out var state))
            _randomAdminMenus[id] = state = new();
        return state;
    }

    private bool HandleAdminRandomClick(CCSPlayerController player, string button)
    {
        var sid = player.SteamID.ToString();
        if (button == "admin_random_open")
        {
            if (IsVoteAdministrator(player) && _adminVoteViews.GetValueOrDefault(sid) == AdminVoteView.Home)
            {
                _randomAdminMenus[sid] = new();
                SetAdminVoteView(player, AdminVoteView.Random);
            }
            return true;
        }
        if (_adminVoteViews.GetValueOrDefault(sid) != AdminVoteView.Random) return false;
        if (!IsVoteAdministrator(player)) { ClosePlayerMenu(player); return true; }
        var state = RandomAdminState(sid);
        switch (button)
        {
            case "admin_random_back": BackAdminRandom(player); return true;
            case "admin_random_prev": state.Index = Math.Max(0, state.Index - 1); break;
            case "admin_random_next": state.Index++; break;
            case "admin_random_refresh": break;
            default:
                const string prefix = "admin_random_row_";
                if (!button.StartsWith(prefix, StringComparison.Ordinal)
                    || !int.TryParse(button.AsSpan(prefix.Length), out var slot)) return false;
                if (slot < 0 || slot >= state.VisibleKeys.Length) return true;
                var key = state.VisibleKeys[slot];
                if (state.Page == RandomAdminPage.Players)
                {
                    state.Target = key;
                    state.Page = RandomAdminPage.Buffs;
                }
                else if (state.Page == RandomAdminPage.Buffs)
                {
                    if (key == "@filter") state.Page = RandomAdminPage.Filter;
                    else if (key == "@clear") state.Page = RandomAdminPage.Clear;
                    else { state.Buff = key; state.Page = RandomAdminPage.Actions; }
                }
                else if (state.Page == RandomAdminPage.Filter)
                { state.Quality = key == "all" ? "" : key; state.Page = RandomAdminPage.Buffs; }
                else if (state.Page == RandomAdminPage.Actions)
                {
                    if (key == "detail") state.Page = RandomAdminPage.Detail;
                    else if (key is "give" or "set") { state.Operation = key; state.Page = RandomAdminPage.Levels; }
                    else ExecuteAdminRandom(player, state, key, state.Buff);
                }
                else if (state.Page == RandomAdminPage.Levels && int.TryParse(key, out var level))
                    ExecuteAdminRandom(player, state, state.Operation, state.Buff, level);
                else if (state.Page == RandomAdminPage.Clear)
                    ExecuteAdminRandom(player, state, key.EndsWith("clear", StringComparison.Ordinal) ? "clear" : "reset", "", team: key.StartsWith("team", StringComparison.Ordinal));
                else return true;
                state.Index = 0;
                break;
        }
        RenderAdminRandomMenu(player);
        return true;
    }

    private void BackAdminRandom(CCSPlayerController player)
    {
        var state = RandomAdminState(player.SteamID.ToString());
        if (state.Page == RandomAdminPage.Players) { SetAdminVoteView(player, AdminVoteView.Home); return; }
        state.Page = state.Page switch
        {
            RandomAdminPage.Detail or RandomAdminPage.Levels => RandomAdminPage.Actions,
            RandomAdminPage.Buffs => RandomAdminPage.Players,
            _ => RandomAdminPage.Buffs
        };
        state.Index = 0;
        RenderAdminRandomMenu(player);
    }

    private void RenderAdminRandomMenu(CCSPlayerController player, bool preserveSlots = false)
    {
        if (!IsHuman(player) || !IsVoteAdministrator(player)) { ClosePlayerMenu(player); return; }
        var state = RandomAdminState(player.SteamID.ToString());
        var view = CaorenCupRandomAccess.TryGet()?.GetAdminView(player);
        var target = view?.Players.FirstOrDefault(entry => entry.SteamId == state.Target);
        if ((state.Page != RandomAdminPage.Players && target is null) || view is null)
        { state.Page = RandomAdminPage.Players; state.Index = 0; preserveSlots = false; }
        var buff = view?.Catalog.FirstOrDefault(entry => entry.Key == state.Buff);
        if (target is not null) state.Revision = target.Revision;
        if (state.Page is RandomAdminPage.Detail or RandomAdminPage.Actions or RandomAdminPage.Levels && buff is null)
        { state.Page = RandomAdminPage.Buffs; state.Index = 0; preserveSlots = false; }

        _surface.SetVariable(player, "admin_random_state", view is null ? "随机乱斗状态暂不可用"
            : view.Running ? $"第 {view.Round}/24 回合 · 管理员测试" : "随机乱斗未运行");
        _surface.SetVariable(player, "admin_random_title", state.Page == RandomAdminPage.Players ? "随机乱斗 · 玩家状态" : $"随机乱斗 · {target!.Name}");
        _surface.SetVariable(player, "admin_random_summary", state.Page == RandomAdminPage.Players
            ? "在线玩家与本局保留的离线玩家" : $"{RandomAdminMenuPresentation.Presence(target!)} · 强化券 {target!.Coupons}");
        _surface.SetVariable(player, "admin_random_usage", state.Page == RandomAdminPage.Players ? "点击玩家选择卡牌或清空范围" : RandomAdminMenuPresentation.RoundUsage(target!));

        var rows = new List<(string Key, string Text, string Hint)>();
        if (state.Page == RandomAdminPage.Players && view is not null)
            rows.AddRange(view.Players.Select(entry => (entry.SteamId, RandomAdminMenuPresentation.PlayerRow(entry), RandomAdminMenuPresentation.Presence(entry))));
        else if (state.Page == RandomAdminPage.Buffs)
        {
            rows.Add(("@filter", "筛选品质 · " + (state.Quality.Length == 0 ? "全部" : state.Quality), "按品质筛选46项卡牌"));
            rows.Add(("@clear", "清空或重置个人 / 全队", "个人和全队操作分别列出"));
            rows.AddRange(view!.Catalog.Where(entry => state.Quality.Length == 0 || entry.Quality == state.Quality)
                .Select(entry => (entry.Key, RandomAdminMenuPresentation.BuffRow(entry) +
                    $" · 当前{target!.Owned.FirstOrDefault(owned => owned.Key == entry.Key)?.Level ?? 0}级", entry.Scope + " · 免费测试，沿用已用次数")));
        }
        else if (state.Page == RandomAdminPage.Filter)
            rows.AddRange(new[] { "all", "D", "C", "B", "A", "S" }.Select(key => (key, key == "all" ? "全部品质" : RandomMenuPresentation.QualityName(key), "点击应用筛选")));
        else if (state.Page == RandomAdminPage.Actions)
        {
            var scope = buff!.Scope == "团队" ? "全队共享" : "目标个人";
            rows.Add(("detail", "查看完整说明", buff.Name));
            rows.Add(("give", "给予 BUFF · 选择等级", scope + "，保留更高等级"));
            rows.Add(("set", "设置等级 · 可降低", scope + "，已有次数继续沿用"));
            rows.Add(("remove", "移除此 BUFF", scope + "，立即恢复相关属性"));
            rows.Add(("reset", "重置此 BUFF 的次数与冷却", scope + "，取消相关待执行任务"));
        }
        else if (state.Page == RandomAdminPage.Levels)
            rows.AddRange(Enumerable.Range(1, buff!.MaxLevel).Select(level => (level.ToString(), $"{buff.Name} · {level}级", buff.Scope + " · 免费测试")));
        else if (state.Page == RandomAdminPage.Clear)
        {
            rows.Add(("personal-clear", "清空目标个人 BUFF", "保留团队 BUFF"));
            rows.Add(("team-clear", "清空目标所属全队 BUFF", "影响该逻辑队伍的所有玩家"));
            rows.Add(("personal-reset", "重置目标全部个人次数与冷却", "保留所持卡牌和等级"));
            rows.Add(("team-reset", "重置全队共享次数与冷却", "影响该逻辑队伍的所有玩家"));
        }

        var detail = state.Page == RandomAdminPage.Detail;
        var pages = detail ? RandomMenuPresentation.DescriptionPages(buff!.DetailedDescription) : [];
        var total = detail ? pages.Count : Math.Max(1, (rows.Count + RandomAdminSlots - 1) / RandomAdminSlots);
        state.Index = Math.Clamp(state.Index, 0, total - 1);
        state.VisibleKeys = RandomAdminMenuPresentation.PageKeys(rows.Select(row => row.Key), state.VisibleKeys, state.Index, preserveSlots);
        var byKey = rows.ToDictionary(row => row.Key, StringComparer.Ordinal);
        for (var slot = 0; slot < RandomAdminSlots; slot++)
        {
            var key = slot < state.VisibleKeys.Length ? state.VisibleKeys[slot] : "";
            var exists = byKey.TryGetValue(key, out var row);
            _surface.SetVariable(player, "admin_random_row" + slot, exists ? row.Text : "");
            _surface.SetVariable(player, "admin_random_hint" + slot, exists ? row.Hint : "");
            _surface.SetClass(player, "admin_random_row_" + slot + "_host", "Hidden", !exists);
        }
        _surface.SetClass(player, "admin_random_detail", "Hidden", !detail);
        _surface.SetVariable(player, "admin_random_buff", detail ? RandomAdminMenuPresentation.BuffRow(buff!) : "");
        _surface.SetVariable(player, "admin_random_description", detail ? pages[state.Index] : "");
        _surface.SetVariable(player, "admin_random_page", !detail && rows.Count == 0 ? (state.Page == RandomAdminPage.Buffs ? "尚未选择 BUFF" : "暂无本局玩家状态")
            : $"第 {state.Index + 1}/{total} 页{(detail ? "" : $" · 共 {rows.Count} 项")}");
        _surface.SetClass(player, "admin_random_prev_host", "Hidden", state.Index == 0);
        _surface.SetClass(player, "admin_random_next_host", "Hidden", state.Index >= total - 1);
        _surface.SetClass(player, "admin_random_refresh_host", "Hidden", detail);
        _surface.SetVariable(player, "admin_random_back_label", state.Page == RandomAdminPage.Players ? "返回快捷指令" : state.Page == RandomAdminPage.Buffs ? "返回玩家列表" : "返回上一级");
    }

    private void ExecuteAdminRandom(CCSPlayerController administrator, RandomAdminMenuState state,
        string operation, string card, int level = 1, bool team = false)
    {
        var api = CaorenCupRandomAccess.TryGet();
        var result = api?.Debug(administrator, new(operation, state.Target, card, level, state.Revision, team));
        Chat(administrator.SteamID.ToString(), result?.Message ?? "随机乱斗服务暂不可用。");
        if (result?.Success == true) state.Page = RandomAdminPage.Buffs;
    }

    private void RefreshAdminRandomMenus()
    {
        if (VoteNow < _nextRandomAdminRefresh) return;
        _nextRandomAdminRefresh = VoteNow + 1;
        foreach (var player in VoteHumans())
            if (_navigator.HasSession(player.SteamID.ToString()) && _adminVoteViews.GetValueOrDefault(player.SteamID.ToString()) == AdminVoteView.Random)
                RenderAdminRandomMenu(player, preserveSlots: true);
    }
}
