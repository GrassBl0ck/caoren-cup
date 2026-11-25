using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private readonly Dictionary<string, AdminShortcutMenu> _shortcuts = new();

    private void RefreshShortcutMatches()
    {
        if (_shortcuts.Count == 0) return;
        foreach (var (sid, menu) in _shortcuts.ToArray())
        {
            var player = FindPlayer(sid);
            if (player == null) { _shortcuts.Remove(sid); continue; }
            if (!IsVoteAdministrator(player)) { ClosePlayerMenu(player); continue; }
            if (menu.RefreshMatchIfChanged()) RenderShortcuts(player, menu);
        }
    }

    private sealed class ShortcutHost(CCSPlayerController admin) : IAdminShortcutHost
    {
        public IReadOnlyList<ShortcutPlayer> Players() => VoteHumans()
            .Where(player => player.UserId.HasValue)
            .OrderBy(player => player.PlayerName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(player => player.UserId)
            .Select(player => new ShortcutPlayer(player.SteamID, player.UserId!.Value, player.PlayerName))
            .ToArray();

        public HpCapSettings? HpCap() => CaorenCupCoreAccess.TryGet()?.Config.HpCap;

        public IReadOnlyList<string> Rules() => HpCap() is { } limits
            ? CaorenCupModuleRegistry.CurrentRules(limits)
                .Select(rule => new string(rule.Where(c => !char.IsControl(c)).ToArray())).ToArray()
            : ["规则服务暂不可用"];

        public MatchAdminState? Match() => CaorenCupMatchAdministration.Current?.ReadMatchAdminState();

        public void Pause(MatchAdminRequest request)
        {
            var result = CaorenCupMatchAdministration.Current?.ApplyMatchAdminAction(request);
            if (result?.Success != true) Notify(result?.Message ?? "比赛控制暂不可用。");
        }

        public void Run(string command) => admin.ExecuteClientCommandFromServer(command);
        public void Notify(string message) => CaorenCupChat.PrintToChat(admin, message);
    }

    // OnHudClicked has already verified the active layout, the session and @css/root.
    private bool HandleShortcutClick(CCSPlayerController player, string button)
    {
        if (!button.StartsWith("shortcut_", StringComparison.Ordinal)) return false;
        var sid = player.SteamID.ToString();
        var view = _adminVoteViews.GetValueOrDefault(sid);
        if (IsQuickView(view))
        {
            ShortcutSection? section = button switch
            {
                "shortcut_equipment" => ShortcutSection.Equipment,
                "shortcut_cap" => ShortcutSection.HealthCap,
                "shortcut_match" => ShortcutSection.Match,
                _ => null
            };
            if (section.HasValue)
            {
                var menu = new AdminShortcutMenu(new ShortcutHost(player), section.Value);
                _shortcuts[sid] = menu;
                SetAdminVoteView(player, AdminVoteView.Shortcuts);
                RenderShortcuts(player, menu);
                return true;
            }
        }

        if (view != AdminVoteView.Shortcuts || !_shortcuts.TryGetValue(sid, out var current)) return true;
        if (button == "shortcut_give") current.OpenOperation(EquipmentAction.Give);
        else if (button == "shortcut_health") current.OpenOperation(EquipmentAction.Health);
        else if (button == "shortcut_loadout") current.OpenOperation(EquipmentAction.Loadout);
        else if (button == "shortcut_prev") current.TurnPage(-1);
        else if (button == "shortcut_next") current.TurnPage(1);
        else if (button == "shortcut_refresh") current.Refresh();
        else if (button.StartsWith("shortcut_key_", StringComparison.Ordinal)) current.Key(button["shortcut_key_".Length..]);
        else if (button.StartsWith("shortcut_row_", StringComparison.Ordinal) &&
                 int.TryParse(button.AsSpan("shortcut_row_".Length), out var slot)) current.Select(slot);

        // Reset can synchronously close menus through the module registry.
        if (_shortcuts.ContainsKey(sid)) RenderShortcuts(player, current);
        return true;
    }

    private void RenderShortcuts(CCSPlayerController player, AdminShortcutMenu menu)
    {
        var page = menu.Page;
        RenderAdminNavigation(player);
        _surface.SetVariable(player, "shortcut_detail", page.Detail);
        _surface.SetClass(player, "shortcut_keypad", "Hidden", page.Number == null);
        _surface.SetClass(player, "shortcut_prev", "Hidden", menu.Index == 0);
        _surface.SetClass(player, "shortcut_next", "Hidden", menu.Index + 1 >= menu.Pages);
        bool categoryOnly = menu.Section == ShortcutSection.Equipment && menu.Operation == null;
        _surface.SetClass(player, "shortcut_refresh", "Hidden", page.Number != null || categoryOnly);
        _surface.SetClass(player, "shortcut_summary", "Hidden", categoryOnly);
        _surface.SetVariable(player, "shortcut_page", menu.Pages > 1 ? $"第 {menu.Index + 1}/{menu.Pages} 页" : "");
        var visible = categoryOnly ? Array.Empty<ShortcutRow>() : menu.VisibleRows.ToArray();
        for (var i = 0; i < AdminShortcutMenu.Slots; i++)
        {
            var row = visible.ElementAtOrDefault(i);
            _surface.SetClass(player, $"shortcut_row_{i}", "Hidden", row == null);
            var label = row is { Select: null, Hint: null } ? row.Label + "（不可用）" : row?.Label;
            _surface.SetVariable(player, $"shortcut_label{i}", label ?? "");
            _surface.SetVariable(player, $"shortcut_hint{i}", row?.Hint ?? label ?? "");
        }
    }
}
