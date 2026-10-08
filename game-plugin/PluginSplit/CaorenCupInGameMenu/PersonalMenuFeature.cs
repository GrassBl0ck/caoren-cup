using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private enum PersonalView { Commands, Detail, Settings, Guide }
    private IMenuHudSurface _personalSurface = null!;
    private MenuPreferences _preferences = null!;
    private readonly Dictionary<(IMenuHudSurface Surface, string SteamId, string Panel), int> _menuSoundVolumes = new();
    private CommandHelpCatalog _playerCommands = null!;
    private readonly Dictionary<string, PersonalView> _personalViews = new();
    private readonly Dictionary<string, int> _personalPages = new();
    private readonly Dictionary<string, (CommandHelpEntry Entry, int Page)> _personalDetails = new();
    private readonly HashSet<string> _voteFromPersonalMenu = new();
    private readonly Dictionary<(IMenuHudSurface Surface, string SteamId, string Panel), int> _appearanceValues = new();

    private void RegisterPersonalMenu()
    {
        _personalSurface = new CustomHudMenuSurface("panorama/layout/custom_game/caoren_player_menu.xml");
        _preferences = new MenuPreferences(Path.Combine(ModuleDirectory, "data", "menu-user-preferences.json"), useCore: true);
        _playerCommands = CommandHelpCatalog.LoadPlayer();
        Console.WriteLine("[CaorenCupInGameMenu] 玩家对局指令目录已加载：" + _playerCommands.Entries.Count + " 项（" + string.Join("、", _playerCommands.Entries.Select(entry => entry.Name)) + "）。");
        AddCommand("css_crcmenu", "打开普通玩家菜单", OnPersonalMenuCommand);
        AddCommand("css_cm", "打开普通玩家菜单", OnPersonalMenuCommand);
    }
    public override void OnAllPluginsLoaded(bool hotReload)
    {
        if (CaorenCup.Contracts.CaorenCupPreferencesAccess.TryGet() is not { } core) return;
        try { _preferences.EnsureImported(core); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { Console.WriteLine("[CaorenCupInGameMenu] 个人偏好迁移停止，旧数据保留：" + ex.Message); }
    }
    private void OnPersonalMenuCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !IsHuman(player) || !_enabled) return;
        var sid = player.SteamID.ToString();
        if (_personalViews.ContainsKey(sid)) { ClosePersonalMenu(player); return; }
        if (IsVoteDraftAwaitingInput(sid)) { Chat(sid, "请先完成或取消投票草稿。"); return; }
        OpenPersonalMenu(player);
    }
    private void OpenPersonalMenu(CCSPlayerController player, bool preservePage = false)
    {
        var sid = player.SteamID.ToString();
        if (!preservePage) { _personalPages.Remove(sid); _personalDetails.Remove(sid); }
        ClosePlayerMenu(player); CloseVotePage(player);
        _personalSurface.EnsureEntity();
        if (!_personalSurface.IsAlive) { Chat(sid, "菜单界面不可用，投票可用 /crcvote A 提交。"); return; }
        SetPersonalView(player, PersonalView.Commands);
        _personalSurface.SetClass(player, "dialog", "Hidden", false);
        _personalSurface.SetInputCapture(player, true);
        ApplyPersonalAppearance(player, _personalSurface, "dialog");
    }
    private void SetPersonalView(CCSPlayerController player, PersonalView view)
    {
        var sid = player.SteamID.ToString(); _personalViews[sid] = view;
        _personalSurface.SetClass(player, "player_back", "Hidden", view == PersonalView.Commands);
        foreach (var pair in new[] { (PersonalView.Commands, "player_commands"),
            (PersonalView.Detail, "player_detail"), (PersonalView.Settings, "player_settings"), (PersonalView.Guide, "player_open_guide") })
            _personalSurface.SetClass(player, pair.Item2, "Hidden", view != pair.Item1);
        _personalSurface.SetClass(player, "player_commands_open", "CaorenTabSelected", view is PersonalView.Commands or PersonalView.Detail);
        _personalSurface.SetClass(player, "player_settings_open", "CaorenTabSelected", view == PersonalView.Settings);
        _personalSurface.SetClass(player, "player_guide_open", "CaorenTabSelected", view == PersonalView.Guide);
        _personalSurface.SetVariable(player, "player_section", view switch { PersonalView.Commands => "玩家指令", PersonalView.Detail => "指令详情", PersonalView.Settings => "个人设置", _ => "打开指引" });
        RenderPersonalView(player);
    }
    private void HandlePersonalClick(CCSPlayerController player, string id)
    {
        var sid = player.SteamID.ToString(); if (!_personalViews.TryGetValue(sid, out var view)) return;
        if (id == "player_exit") { ExitOwnMenus(player); return; }
        if (id == "player_back") { HandlePersonalBack(player); return; }
        if (id == "player_commands_open") { _personalDetails.Remove(sid); SetPersonalView(player, PersonalView.Commands); return; }
        if (id == "player_settings_open") { SetPersonalView(player, PersonalView.Settings); return; }
        if (id == "player_guide_open") { SetPersonalView(player, PersonalView.Guide); return; }
        if (id == "player_vote_open") { OpenVotePage(player, false, true); return; }
        if (view == PersonalView.Settings && HandlePreferenceClick(player, id, _personalSurface)) return;
        if (view == PersonalView.Commands)
        {
            if (id is "player_cmd_prev" or "player_cmd_next")
            {
                _personalPages[sid] = Math.Clamp(_personalPages.GetValueOrDefault(sid) + (id == "player_cmd_prev" ? -1 : 1), 0, _playerCommands.ListPageCount - 1);
                RenderPersonalView(player); return;
            }
            const string prefix = "player_cmd_";
            if (id.StartsWith(prefix) && int.TryParse(id.AsSpan(prefix.Length), out var i))
            {
                var page = _playerCommands.ListPage(_personalPages.GetValueOrDefault(sid));
                if (i >= 0 && i < page.Count) { _personalDetails[sid] = (page[i], 0); SetPersonalView(player, PersonalView.Detail); }
            }
        }
        else if (view == PersonalView.Detail && (id is "player_detail_prev" or "player_detail_next") && _personalDetails.TryGetValue(sid, out var detail))
        {
            var count = _playerCommands.DetailPages(detail.Entry).Count;
            _personalDetails[sid] = (detail.Entry, Math.Clamp(detail.Page + (id == "player_detail_prev" ? -1 : 1), 0, count - 1));
            RenderPersonalView(player);
        }
    }
    private void RenderPersonalView(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString(); var view = _personalViews.GetValueOrDefault(sid);
        if (view == PersonalView.Settings) { RenderPersonalSettings(player, _personalSurface); return; }
        if (view == PersonalView.Commands)
        {
            var index = _personalPages.GetValueOrDefault(sid); var page = _playerCommands.ListPage(index);
            _personalSurface.SetClass(player, "player_cmd_prev", "Hidden", index == 0);
            _personalSurface.SetClass(player, "player_cmd_next", "Hidden", index >= _playerCommands.ListPageCount - 1);
            _personalSurface.SetVariable(player, "player_commands_page", $"{index + 1}/{_playerCommands.ListPageCount} 页 · {_playerCommands.Entries.Count} 项");
            for (var i = 0; i < 8; i++)
            {
                var entry = i < page.Count ? page[i] : null;
                _personalSurface.SetVariable(player, "player_cmd_name" + i, entry?.Name ?? "");
                _personalSurface.SetVariable(player, "player_cmd_summary" + i, entry?.Summary ?? "");
                _personalSurface.SetClass(player, "player_cmd_" + i, "Hidden", entry == null);
            }
        }
        else if (view == PersonalView.Detail && _personalDetails.TryGetValue(sid, out var detail))
        {
            var pages = _playerCommands.DetailFragmentPages(detail.Entry); var count = pages.Count;
            _personalSurface.SetClass(player, "player_detail_prev", "Hidden", detail.Page == 0);
            _personalSurface.SetClass(player, "player_detail_next", "Hidden", detail.Page >= count - 1);
            _personalSurface.SetVariable(player, "player_detail_name", detail.Entry.Name);
            _personalSurface.SetVariable(player, "player_detail_summary", detail.Entry.Summary);
            _personalSurface.SetVariable(player, "player_detail_meta", $"模块：{detail.Entry.Module}　权限：{detail.Entry.Permission}");
            RenderHelpRows(player, _personalSurface, "player", pages[detail.Page]);
            _personalSurface.SetVariable(player, "player_detail_page", $"{detail.Page + 1}/{count} 页");
        }
    }
    private bool HandlePersonalBack(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString(); if (!_personalViews.TryGetValue(sid, out var view)) return false;
        if (view == PersonalView.Commands) ClosePersonalMenu(player);
        else SetPersonalView(player, PersonalView.Commands);
        return true;
    }
    private void ClosePersonalMenu(CCSPlayerController player, bool preservePage = false)
    {
        var sid = player.SteamID.ToString();
        _personalViews.Remove(sid);
        if (!preservePage) { _personalPages.Remove(sid); _personalDetails.Remove(sid); }
        if (_personalSurface == null) return;
        _personalSurface.SetClass(player, "dialog", "Hidden", true); _personalSurface.ReleaseInput(player);
        if (_personalViews.Count == 0) { _personalSurface.RemoveEntity(); ClearHelpRenderCache(_personalSurface); }
    }
    private void ReturnFromVotePage(CCSPlayerController player)
    {
        var fromPersonal = _voteFromPersonalMenu.Remove(player.SteamID.ToString());
        CloseVotePage(player);
        if (fromPersonal && _enabled) OpenPersonalMenu(player, true);
    }
    private bool HandleAdminSettingsClick(CCSPlayerController player, string id)
    {
        if (id == "admin_settings_open") { SetAdminVoteView(player, AdminVoteView.Settings); return true; }
        return _adminVoteViews.GetValueOrDefault(player.SteamID.ToString()) == AdminVoteView.Settings && HandlePreferenceClick(player, id, _surface);
    }
    private bool HandlePreferenceClick(CCSPlayerController player, string id, IMenuHudSurface surface)
    {
        var alpha = id.StartsWith("settings_alpha_", StringComparison.Ordinal);
        var volume = id.StartsWith("settings_volume_", StringComparison.Ordinal);
        if (!alpha && !volume) return false;
        var prefix = alpha ? "settings_alpha_" : "settings_volume_";
        var delta = id[prefix.Length..] switch { "minus5" => -5, "minus1" => -1, "plus1" => 1, "plus5" => 5, _ => 0 };
        if (delta == 0) return true;
        var current = _preferences.Get(player.SteamID.ToString());
        var value = alpha ? current.Transparency : current.Volume;
        if ((value == 0 && delta < 0) || (value == 100 && delta > 0)) return true;
        try { _preferences.Set(player.SteamID.ToString(), transparency: alpha ? current.Transparency + delta : null, volume: volume ? current.Volume + delta : null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { Chat(player.SteamID.ToString(), "设置未能持久保存，请通知管理员：" + ex.Message); }
        RenderPersonalSettings(player, surface);
        ApplyPersonalAppearance(player, surface, "dialog");
        return true;
    }
    private void RenderPersonalSettings(CCSPlayerController player, IMenuHudSurface surface)
    {
        var preference = _preferences.Get(player.SteamID.ToString());
        surface.SetVariable(player, "settings_alpha", preference.Transparency + "%");
        surface.SetVariable(player, "settings_volume", preference.Volume + "%");
        foreach (var prefix in new[] { "settings_alpha_", "settings_volume_" })
        {
            var value = prefix == "settings_alpha_" ? preference.Transparency : preference.Volume;
            surface.SetClass(player, prefix + "minus5", "AtBoundary", value == 0);
            surface.SetClass(player, prefix + "minus1", "AtBoundary", value == 0);
            surface.SetClass(player, prefix + "plus1", "AtBoundary", value == 100);
            surface.SetClass(player, prefix + "plus5", "AtBoundary", value == 100);
        }
    }
    private void ApplyPersonalAppearance(CCSPlayerController player, IMenuHudSurface surface, string panel)
    {
        var preference = _preferences.Get(player.SteamID.ToString());
        var key = (surface, player.SteamID.ToString(), panel);
        surface.SetClass(player, panel, "MenuSoundOn", preference.Volume > 0);
        if (_menuSoundVolumes.TryGetValue(key, out var previousVolume)) surface.SetClass(player, panel, "MenuVolume" + previousVolume, false);
        surface.SetClass(player, panel, "MenuVolume" + preference.Volume, true);
        _menuSoundVolumes[key] = preference.Volume;
        // 新布局默认 10%；只撤去默认值和上次值，避免每次刷新发送 101 次类更新。
        surface.SetClass(player, panel, "UserAlpha10", false);
        if (_appearanceValues.TryGetValue(key, out var previous) && previous != 10)
            surface.SetClass(player, panel, "UserAlpha" + previous, false);
        surface.SetClass(player, panel, "UserAlpha" + preference.Transparency, true);
        _appearanceValues[key] = preference.Transparency;
    }
}
