using System.Text.Json;
using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private const int HeldMatchRows = 8;
    private readonly Dictionary<string, HeldMatchView> _heldMatchViews = new(StringComparer.Ordinal);
    private readonly CustomHudMenuSurface _heldMatchSurface = new("panorama/layout/custom_game/caoren_match_menu.xml");
    private string[] _presetAnnouncements = [];

    private enum HeldMatchPage { Home, Modes, Restore, Announcements, Confirm }

    private sealed class HeldMatchView
    {
        public HeldMatchPage Page { get; set; }
        public int PageIndex { get; set; }
        public int PageCount { get; set; } = 1;
        public IReadOnlyList<HeldMatchRow> Rows { get; set; } = [];
        public MatchAdminRequest? PendingAction { get; set; }
        public string? ConfirmationTitle { get; set; }
        public HeldMatchPage ConfirmationSource { get; set; }
    }

    private sealed record HeldMatchRow(string Label, Action? OnClick);

    private void RegisterHeldMatchMenu()
    {
        _presetAnnouncements = ReadPresetAnnouncements();
        AddCommand("css_crcadmin_bind", "显示比赛快捷菜单的客户端绑键指引", ShowHeldMatchBindingGuide);
        AddCommand("css_crcadmin_hold_open", "按住显示比赛快捷菜单", (player, _) =>
        {
            if (player is not { IsValid: true, IsBot: false, IsHLTV: false }
                || !_enabled || !AdminManager.PlayerHasPermissions(player, "@css/root")) return;
            var steamId = player.SteamID.ToString();
            if (_heldMatchViews.ContainsKey(steamId) || IsVoteDraftAwaitingInput(steamId)) return;

            ForgetCombat(player);
            ClosePlayerMenu(player);
            CloseVotePage(player);
            ClosePersonalMenu(player);
            _heldMatchSurface.EnsureEntity();
            if (!_heldMatchSurface.IsAlive)
            {
                CaorenCupChat.PrintToChat(player, "比赛快捷菜单资源未加载。");
                return;
            }
            _heldMatchViews.Add(steamId, new HeldMatchView());
            RenderHeldMatchMenu(player);
            _heldMatchSurface.SetClass(player, "dialog", "Hidden", false);
            _heldMatchSurface.SetInputCapture(player, true);
        });
        AddCommand("css_crcadmin_hold_close", "松开收起比赛快捷菜单", (player, _) =>
        {
            if (player is { IsValid: true }) CloseHeldMatchMenu(player.SteamID.ToString(), player);
        });
    }

    private static void ShowHeldMatchBindingGuide(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true, IsBot: false, IsHLTV: false }) return;
        if (!AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupChat.PrintToChat(player, "只有管理员可以使用比赛快捷菜单。");
            return;
        }

        var key = command.ArgCount == 1 ? "z" : command.GetArg(1).ToLowerInvariant();
        if (command.ArgCount > 2 || key.Length is < 1 or > 24 ||
            !key.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            CaorenCupChat.PrintToChat(player, "用法：/crcadmin_bind [键名]，例如 z、f8、mouse5。");
            return;
        }

        player.PrintToConsole("[草人杯] 比赛快捷菜单默认留空。先输入下方命令，查看该键原有的绑定：");
        player.PrintToConsole($"bind \"{key}\"");
        player.PrintToConsole("[草人杯] 选好空闲键或确认覆盖后，把下面三行逐条输入控制台：");
        player.PrintToConsole("alias \"+crc_admin_menu\" \"css_crcadmin_hold_open\"");
        player.PrintToConsole("alias \"-crc_admin_menu\" \"css_crcadmin_hold_close\"");
        player.PrintToConsole($"bind \"{key}\" \"+crc_admin_menu\"");
        player.PrintToConsole($"[草人杯] 要取消此键绑定，输入 unbind \"{key}\"。");
        player.PrintToConsole("[草人杯] 按住打开，松开关闭。若松手后光标仍在，可在控制台输入：");
        player.PrintToConsole("css_crcadmin_hold_close");
        CaorenCupChat.PrintToChat(player, "已把比赛快捷菜单的绑键指引发到你的 CS2 控制台。");
    }

    private string[] ReadPresetAnnouncements()
    {
        var path = Path.Combine(ModuleDirectory, "data", "admin-announcements.json");
        try
        {
            if (!File.Exists(path))
                return ["比赛即将开始，请做好准备。", "请回到游戏并加入指定队伍。", "比赛暂停，请等待管理员通知。"];

            return (JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [])
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => new string(text.Where(character => !char.IsControl(character)).Take(100).ToArray()).Trim())
                .Where(text => text.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.LogWarning(exception, "[草人杯] 无法加载比赛预设公告。");
            return [];
        }
    }

    private void CloseHeldMatchMenu(string steamId, CCSPlayerController? player)
    {
        if (!_heldMatchViews.Remove(steamId)) return;
        if (player is { IsValid: true })
        {
            _heldMatchSurface.SetClass(player, "dialog", "Hidden", true);
            _heldMatchSurface.ReleaseInput(player);
        }
        if (_heldMatchViews.Count == 0) _heldMatchSurface.RemoveEntity();
    }

    private void CloseAllHeldMatchMenus()
    {
        _heldMatchViews.Clear();
        _heldMatchSurface.RemoveEntity();
    }

    private bool HandleHeldMatchClick(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId)
    {
        if (!_heldMatchSurface.IsOurLayout(layout)) return false;
        var steamId = player.SteamID.ToString();
        if (!_heldMatchViews.TryGetValue(steamId, out var view)) return true;
        if (!AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CloseHeldMatchMenu(steamId, player);
            return true;
        }

        switch (buttonId)
        {
            case "quick_home": SetHeldMatchPage(view, HeldMatchPage.Home); break;
            case "quick_modes": SetHeldMatchPage(view, HeldMatchPage.Modes); break;
            case "quick_restore": SetHeldMatchPage(view, HeldMatchPage.Restore); break;
            case "quick_announcements": SetHeldMatchPage(view, HeldMatchPage.Announcements); break;
            case "quick_back":
                SetHeldMatchPage(view, view.Page == HeldMatchPage.Confirm ? view.ConfirmationSource : HeldMatchPage.Home);
                break;
            case "quick_previous":
                view.PageIndex = Math.Max(view.PageIndex - 1, 0);
                break;
            case "quick_next":
                view.PageIndex = Math.Min(view.PageIndex + 1, view.PageCount - 1);
                break;
            default:
                const string prefix = "quick_item_";
                if (buttonId.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(buttonId.AsSpan(prefix.Length), out var index)
                    && index >= 0 && index < view.Rows.Count)
                    view.Rows[index].OnClick?.Invoke();
                break;
        }
        RenderHeldMatchMenu(player);
        return true;
    }

    private void RenderHeldMatchMenu(CCSPlayerController player)
    {
        if (!_heldMatchViews.TryGetValue(player.SteamID.ToString(), out var view)) return;
        var controller = CaorenCupMatchAdministration.Current;
        var match = controller?.ReadMatchAdminState();

        void Execute(MatchAdminRequest request)
        {
            var latest = CaorenCupMatchAdministration.Current;
            if (latest is null)
            {
                CaorenCupChat.PrintToChat(player, "比赛控制暂不可用。");
                return;
            }
            var result = latest.ApplyMatchAdminAction(request);
            if (!result.Success) CaorenCupChat.PrintToChat(player, result.Message);
        }

        HeldMatchRow Navigate(string label, HeldMatchPage page, bool enabled = true) =>
            new(label, enabled ? () => SetHeldMatchPage(view, page) : null);

        HeldMatchRow Act(string label, MatchAdminAction action, bool enabled, int? round = null) =>
            new(label, enabled ? () => Execute(new MatchAdminRequest(action, match?.Stamp, Round: round)) : null);

        HeldMatchRow Confirm(string label, MatchAdminAction action, bool enabled, int? round = null) =>
            new(label, enabled ? () =>
            {
                var parent = view.Page;
                SetHeldMatchPage(view, HeldMatchPage.Confirm);
                view.ConfirmationSource = parent;
                view.ConfirmationTitle = label;
                view.PendingAction = new MatchAdminRequest(action, match?.Stamp, Round: round);
            } : null);

        IReadOnlyList<HeldMatchRow> allRows;
        string title;
        switch (view.Page)
        {
            case HeldMatchPage.Modes:
                title = "更改模式";
                allRows = match?.AvailableModes.Select(mode => new HeldMatchRow(mode.DisplayName, () =>
                {
                    Execute(new MatchAdminRequest(MatchAdminAction.SelectMode, match.Stamp, ModeId: mode.Id));
                    SetHeldMatchPage(view, HeldMatchPage.Home);
                })).ToArray() ?? [];
                break;
            case HeldMatchPage.Restore:
                title = "回溯到更早回合";
                allRows = match is null ? [] : match.RestorableRounds
                    .Where(round => round < match.Stamp.CurrentRound)
                    .OrderDescending()
                    .Select(round => Confirm($"第 {round} 回合", MatchAdminAction.RestoreRound, true, round))
                    .ToArray();
                break;
            case HeldMatchPage.Announcements:
                title = "预设公告";
                allRows = _presetAnnouncements.Select(message => new HeldMatchRow(message, () =>
                    CaorenCupChat.PrintToChatAll(message))).ToArray();
                break;
            case HeldMatchPage.Confirm:
                title = $"确认{view.ConfirmationTitle}";
                allRows = [new HeldMatchRow("确认执行", () =>
                {
                    if (view.PendingAction is { } request) Execute(request);
                    SetHeldMatchPage(view, view.ConfirmationSource);
                }), Navigate("取消", view.ConfirmationSource)];
                break;
            default:
                title = "比赛管理";
                var isPaused = match?.IsPaused == true;
                var currentRound = match?.Stamp.CurrentRound;
                allRows =
                [
                    Act("开始游戏", MatchAdminAction.Start, match?.CanStart == true),
                    Act(isPaused ? "继续游戏" : "暂停游戏", isPaused ? MatchAdminAction.Resume : MatchAdminAction.Pause, match?.CanPause == true),
                    Act("重打本回合", MatchAdminAction.RestoreRound, currentRound is { } round && match!.RestorableRounds.Contains(round), currentRound),
                    Confirm("停止游戏", MatchAdminAction.Stop, match?.CanStop == true),
                ];
                break;
        }

        view.PageCount = Math.Max(1, (allRows.Count + HeldMatchRows - 1) / HeldMatchRows);
        view.PageIndex = Math.Clamp(view.PageIndex, 0, view.PageCount - 1);
        view.Rows = allRows.Skip(view.PageIndex * HeldMatchRows).Take(HeldMatchRows).ToArray();
        var section = view.Page == HeldMatchPage.Confirm ? view.ConfirmationSource : view.Page;
        var heading = section switch { HeldMatchPage.Modes => "更改模式", HeldMatchPage.Restore => "回溯",
            HeldMatchPage.Announcements => "预设公告", _ => "比赛管理" };
        var path = section == HeldMatchPage.Home ? heading : "比赛管理 / " + heading;
        _heldMatchSurface.SetVariable(player, "quick_title", view.Page == HeldMatchPage.Confirm ? path + " / " + title : path);
        foreach (var (id, page) in new[] { ("quick_home", HeldMatchPage.Home), ("quick_modes", HeldMatchPage.Modes),
                     ("quick_restore", HeldMatchPage.Restore), ("quick_announcements", HeldMatchPage.Announcements) })
            _heldMatchSurface.SetClass(player, id, "SelectedTab", section == page);
        string status = match?.Status ?? "比赛控制暂不可用";
        if (allRows.Count == 0) status = view.Page switch
        {
            HeldMatchPage.Modes => "当前没有可切换的模式",
            HeldMatchPage.Restore => "当前没有可回溯的更早回合",
            HeldMatchPage.Announcements => "尚无预设公告",
            _ => status
        };
        _heldMatchSurface.SetVariable(player, "quick_status", status);
        _heldMatchSurface.SetVariable(player, "quick_page", view.PageCount > 1 ? $"{view.PageIndex + 1}/{view.PageCount}" : string.Empty);
        _heldMatchSurface.SetClass(player, "quick_back", "Hidden", view.Page == HeldMatchPage.Home);
        _heldMatchSurface.SetClass(player, "quick_previous", "Hidden", view.PageIndex == 0);
        _heldMatchSurface.SetClass(player, "quick_next", "Hidden", view.PageIndex + 1 >= view.PageCount);
        for (var index = 0; index < HeldMatchRows; index++)
        {
            var row = index < view.Rows.Count ? view.Rows[index] : null;
            var id = $"quick_item_{index}";
            _heldMatchSurface.SetClass(player, id, "Hidden", row is null);
            _heldMatchSurface.SetClass(player, id, "Unavailable", row?.OnClick is null);
            _heldMatchSurface.SetVariable(player, $"quick_label_{index}", row?.Label ?? string.Empty);
        }
    }

    private static void SetHeldMatchPage(HeldMatchView view, HeldMatchPage target)
    {
        view.Page = target;
        view.PageIndex = 0;
        view.PendingAction = null;
        view.ConfirmationTitle = null;
    }
}
