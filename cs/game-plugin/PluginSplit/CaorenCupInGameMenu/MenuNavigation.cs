using CounterStrikeSharp.API.Core;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private const int AncestorSlots = 6;
    private readonly Dictionary<string, string[]> _adminTrails = new();
    private readonly Dictionary<string, string[]> _playerTrails = new();

    private static bool IsQuickView(AdminVoteView view) => view is not
        (AdminVoteView.HelpList or AdminVoteView.HelpDetail or AdminVoteView.Settings);

    private static void PaintTrail(CCSPlayerController player, IMenuHudSurface surface, string[] labels)
    {
        // The current location is a label. Only ancestors get buttons.
        for (var index = 0; index < AncestorSlots; index++)
        {
            bool visible = index < labels.Length - 1;
            surface.SetClass(player, "trail_up_" + index, "Hidden", !visible);
            if (visible) surface.SetVariable(player, "trail_label" + index, labels[index]);
        }
        surface.SetVariable(player, "trail_current", labels[^1]);
    }

    private static bool TryAncestor(string button, string[] path, out int index)
    {
        const string prefix = "trail_up_";
        index = -1;
        return button.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(button.AsSpan(prefix.Length), out index) && index >= 0 && index < path.Length - 1;
    }

    private void RenderAdminNavigation(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString();
        var view = _adminVoteViews.GetValueOrDefault(sid);
        const string root = "快捷指令";
        string[] trail;
        string? tab = null;
        switch (view)
        {
            case AdminVoteView.Home: trail = [root]; break;
            case AdminVoteView.Broadcast: trail = [root, "全服广播"]; tab = "admin_broadcast_open"; break;
            case AdminVoteView.Shortcuts when _shortcuts.TryGetValue(sid, out var shortcuts):
                trail = [root, .. shortcuts.PathLabels];
                tab = shortcuts.Section switch { ShortcutSection.Equipment => "shortcut_equipment",
                    ShortcutSection.HealthCap => "shortcut_cap", _ => "shortcut_match" };
                break;
            case AdminVoteView.Management: trail = [root, "投票"]; tab = "admin_vote_open"; break;
            case AdminVoteView.Preview: trail = [root, "投票", "确认发起"]; tab = "admin_vote_open"; break;
            case AdminVoteView.CancelConfirmation: trail = [root, "投票", "确认取消"]; tab = "admin_vote_open"; break;
            case AdminVoteView.Audio: trail = [root, "音乐与音频"]; tab = "admin_audio_open"; break;
            case AdminVoteView.Ability:
                var ability = GetAbilityMenuState(sid);
                trail = ability.Mode == AbilityMenuMode.Players ? [root, "异能"] : [root, "异能", "为 " + ability.TargetName + " 选择职业"];
                tab = "admin_ability_open";
                break;
            case AdminVoteView.Random:
                var state = RandomAdminState(sid);
                var pages = new List<string> { root, "随机乱斗" };
                if (state.Page != RandomAdminPage.Players) pages.Add("玩家强化");
                if (state.Page is RandomAdminPage.Actions or RandomAdminPage.Detail or RandomAdminPage.Levels) pages.Add("卡牌操作");
                if (state.Page == RandomAdminPage.Filter) pages.Add("品质筛选");
                if (state.Page == RandomAdminPage.Clear) pages.Add("清空与重置");
                if (state.Page == RandomAdminPage.Detail) pages.Add("卡牌详情");
                if (state.Page == RandomAdminPage.Levels) pages.Add("选择等级");
                trail = pages.ToArray(); tab = "admin_random_open"; break;
            case AdminVoteView.HelpList: trail = ["指令帮助"]; break;
            case AdminVoteView.HelpDetail:
                trail = ["指令帮助", _helpDetails.TryGetValue(sid, out var detail) ? detail.Entry.Name : "指令详情"]; break;
            case AdminVoteView.Settings: trail = ["个人设置"]; break;
            default: trail = [root]; break;
        }
        if (_adminTrails.TryGetValue(sid, out var old) && old.SequenceEqual(trail)) return;
        _adminTrails[sid] = trail;
        PaintTrail(player, _surface, trail);
        _surface.SetClass(player, "admin_categories", "Hidden", !IsQuickView(view));
        _surface.SetClass(player, "admin_categories", "Directory", view == AdminVoteView.Home);
        _shortcuts.TryGetValue(sid, out var equipment);
        bool showOperations = view == AdminVoteView.Shortcuts && equipment?.Section == ShortcutSection.Equipment;
        _surface.SetClass(player, "equipment_operations", "Hidden", !showOperations);
        if (showOperations)
        {
            _surface.SetClass(player, "shortcut_give", "CaorenTabSelected", equipment!.Operation == EquipmentAction.Give);
            _surface.SetClass(player, "shortcut_health", "CaorenTabSelected", equipment.Operation == EquipmentAction.Health);
            _surface.SetClass(player, "shortcut_loadout", "CaorenTabSelected", equipment.Operation == EquipmentAction.Loadout);
        }
        foreach (var id in new[] { "shortcut_equipment", "shortcut_cap", "shortcut_match", "admin_broadcast_open",
                     "admin_vote_open", "admin_audio_open", "admin_ability_open", "admin_random_open" })
            _surface.SetClass(player, id, "CaorenTabSelected", id == tab);
    }

    private bool HandleAdminTrailClick(CCSPlayerController player, string button)
    {
        var sid = player.SteamID.ToString();
        if (!_adminTrails.TryGetValue(sid, out var trail) || !TryAncestor(button, trail, out var depth)) return false;
        var view = _adminVoteViews.GetValueOrDefault(sid);
        if (depth == 0 && IsQuickView(view)) SetAdminVoteView(player, AdminVoteView.Home);
        else if (view == AdminVoteView.Shortcuts && _shortcuts.TryGetValue(sid, out var menu))
        {
            if (menu.BackTo(depth - 1)) RenderShortcuts(player, menu);
        }
        else
            for (var n = trail.Length - 1; n > depth; n--) GoMenuBack(player);
        return true;
    }

    private void RenderPlayerNavigation(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString();
        string[] trail = _personalViews.GetValueOrDefault(sid) switch
        {
            PersonalView.Detail => ["玩家指令", _personalDetails.TryGetValue(sid, out var detail) ? detail.Entry.Name : "详情"],
            PersonalView.HudEditor => ["个人设置", "HUD 位置"],
            PersonalView.Settings => ["个人设置"],
            PersonalView.Stats => ["趣味数据"],
            PersonalView.Guide => ["打开指引"],
            PersonalView.Random when _randomMenus.TryGetValue(sid, out var random) => RandomTrail(random),
            _ => ["玩家指令"]
        };
        if (_playerTrails.TryGetValue(sid, out var old) && old.SequenceEqual(trail)) return;
        _playerTrails[sid] = trail;
        PaintTrail(player, _personalSurface, trail);
    }

    private static string[] RandomTrail(RandomMenuState state)
    {
        static string Name(RandomMenuPage page) => page switch
        { RandomMenuPage.Owned => "已拥有", RandomMenuPage.Overview => "概览", _ => "强化三选一" };
        return state.Page == RandomMenuPage.Detail ? ["随机乱斗", Name(state.DetailFrom), "卡牌详情"] : ["随机乱斗", Name(state.Page)];
    }

    private bool HandlePlayerTrailClick(CCSPlayerController player, string button)
    {
        var sid = player.SteamID.ToString();
        if (!_playerTrails.TryGetValue(sid, out var trail) || !TryAncestor(button, trail, out var depth)) return false;
        if (_personalViews.GetValueOrDefault(sid) == PersonalView.Random)
        {
            if (depth == 0) HandleRandomClick(player, "random_offers");
            else HandleRandomBack(player);
        }
        else HandlePersonalBack(player);
        return true;
    }
}
