using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CounterStrikeSharp.API.Modules.Timers;
using CaorenCup.Contracts;

namespace CaorenCup.Features.InGameMenu;

/// <summary>
/// CS2 风格管理员菜单、普通玩家投票页与左侧通知。
/// 管理入口 /crcmenu_admin（root），玩家入口 /crcmenu，公共返回/退出命令仅操作自己的界面。
/// </summary>
public sealed partial class CaorenCupInGameMenuPlugin : BasePlugin, IMenuHostActions
{
    private const float ChatRelayTimeoutSeconds = 60f;

    public override string ModuleName => "CaorenCup In Game Menu";
    public override string ModuleVersion => "1.10.0";
    public override string ModuleAuthor => "Graslock + AI";

    private IMenuHudSurface _surface = null!;
    private MenuNavigator _navigator = null!;

    private bool _enabled = true;

    private sealed record ChatRelayEntry(string Prompt, System.Action<string> OnText, Timer Timer);

    private readonly System.Collections.Generic.Dictionary<string, ChatRelayEntry> _chatRelays = new();

    public string GetHelpEntry() => _enabled
        ? "/crcmenu_admin 管理菜单(root)；/crcmenu 玩家菜单；/crcvote A 投票；/crcmenu_back 返回"
        : "/crcmenu_admin - 管理员游戏内菜单(当前禁用)";

    public string GetStatusInfo() =>
        (_enabled ? "已开启" : "已禁用")
        + $" | 会话:{_navigator?.Sessions.Count ?? 0}"
        + $" | HUD实体:{(_surface?.IsAlive == true ? "存活" : "无")}"
        + $" | 聊天接力等待:{_chatRelays.Count}";

    public string? GetPublicConfigInfo() => null;

    public string GetFeatureDescription() => "CS2 风格管理菜单、玩家投票页及左侧结果提示；投票目前只公布结果，不执行后续操作。";

    private void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled) CloseAllMenus(VoteEndReason.Disabled);
    }

    public override void Load(bool hotReload)
    {
        _surface = new CustomHudMenuSurface();
        _navigator = new MenuNavigator();
        _commandHelp = CommandHelpCatalog.Load();
        new AdminMenuCatalog(this, _navigator).RegisterMinimalAdminPage();

        AddCommand("css_crcmenu_admin", "打开管理员游戏内菜单", OnMenuCommand);
        AddCommand("css_ca", "打开管理员游戏内菜单", OnMenuCommand);
        RegisterVotingHooks();
        RegisterListener<Listeners.OnCustomHudClicked>(OnHudClicked);
        RegisterListener<Listeners.OnMapStart>(_ => CloseAllMenus());
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerChat>(OnPlayerChat);
        CaorenCupModuleRegistry.Register(new CaorenCupModuleDescriptor(
            this, "ingamemenu", "管理员游戏内菜单 (InGameMenu)", "InGameMenuFeature",
            GetHelpEntry, GetStatusInfo, GetFeatureDescription, GetPublicConfigInfo,
            () => SetEnabled(false)));
    }

    public override void Unload(bool hotReload)
    {
        CloseAllMenus(VoteEndReason.PluginUnloaded);
        CaorenCupModuleRegistry.Unregister(this);
    }

    // === 命令入口 ===

    private void OnMenuCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;

        if (!_enabled)
        {
            CaorenCupChat.PrintToChat(player, "管理员菜单当前禁用。");
            return;
        }

        if (!AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupChat.PrintToChat(player, "无权限使用管理员菜单。");
            return;
        }

        if (IsVoteDraftAwaitingInput(player.SteamID.ToString()))
        {
            Chat(player.SteamID.ToString(), "正在填写投票草稿，请先完成，或输入 cancel／/crcmenu_back 取消创建。");
            return;
        }

        if (_navigator.HasSession(player.SteamID.ToString()))
        {
            ClosePlayerMenu(player);
            return;
        }

        OpenMenu(player);
    }

    private void OpenMenu(CCSPlayerController player)
    {
        ClosePersonalMenu(player);
        CloseVotePage(player);
        var steamId = player.SteamID.ToString();
        _surface.EnsureEntity();
        if (!_surface.IsAlive)
        {
            CaorenCupChat.PrintToChat(player, "菜单面板实体创建失败(服务器缺少菜单资源或 CSSharp 版本过低)。");
            return;
        }

        if (_navigator.Open(steamId, "root"))
        {
            RenderPlayer(player);
            SetAdminVoteView(player, AdminVoteView.Home);
            _surface.SetClass(player, "dialog", "Hidden", false);
            _surface.SetInputCapture(player, true);
            ApplyPersonalAppearance(player, _surface, "dialog");
        }
    }

    // === HUD 点击 ===

    private void OnHudClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId)
    {
        if (!player.IsValid) return;
        if (CustomHudMenuSurface.IsBusinessButton(buttonId) &&
            ((_personalSurface.IsOurLayout(layout) && _personalViews.ContainsKey(player.SteamID.ToString()))
            || (_voteSurface.IsOurLayout(layout) && _votePages.ContainsKey(player.SteamID.ToString()))
            || (_surface.IsOurLayout(layout) && _navigator.HasSession(player.SteamID.ToString()) && AdminManager.PlayerHasPermissions(player, "@css/root"))))
            CaorenCup.Contracts.CaorenCupAudioAccess.Play("menu.click", [player]);
        if (_personalSurface.IsOurLayout(layout)) { HandlePersonalClick(player, buttonId); return; }
        if (_voteSurface.IsOurLayout(layout))
        {
            HandleVoteHudClick(player, buttonId);
            return;
        }
        if (!_surface.IsOurLayout(layout)) return;

        var steamId = player.SteamID.ToString();
        var isRoot = AdminManager.PlayerHasPermissions(player, "@css/root");
        if (!isRoot)
        {
            ClosePlayerMenu(player);
            CaorenCupChat.PrintToChat(player, "无权限使用管理员菜单。");
            return;
        }
        if (_navigator.HasSession(steamId) && HandleAdminVotingClick(player, buttonId)) return;
        if (_navigator.HasSession(steamId) && HandleAdminAudioClick(player, buttonId)) return;
        if (_navigator.HasSession(steamId) && HandleCommandHelpClick(player, buttonId)) return;
        if (_navigator.HasSession(steamId) && HandleAdminSettingsClick(player, buttonId)) return;
        if (_adminVoteViews.GetValueOrDefault(steamId) != AdminVoteView.Home) return;
        var action = _navigator.HandleClick(steamId, buttonId, isRoot);

        if (action == MenuClickAction.None && !_navigator.HasSession(steamId))
        {
            ClosePlayerMenu(player);
            return;
        }

        switch (action)
        {
            case MenuClickAction.Close:
                ClosePlayerMenu(player);
                break;
            case MenuClickAction.Rerender:
                RenderPlayer(player);
                break;
        }
    }

    private void RenderPlayer(CCSPlayerController player)
    {
        var steamId = player.SteamID.ToString();
        var isRoot = AdminManager.PlayerHasPermissions(player, "@css/root");
        var render = _navigator.BuildRender(steamId, isRoot);
        if (render == null) return;

        _surface.SetVariable(player, "title", render.Title);
        _surface.SetVariable(player, "status", render.Status);
        _surface.SetVariable(player, "btn0", render.SlotTitles[0]);
        _surface.SetVariable(player, "navsection", "快捷指令");
        _surface.SetVariable(player, "subsection", "广播");
    }


    // === 生命周期清理 ===

    private void ClosePlayerMenu(CCSPlayerController player)
    {
        _navigator.Close(player.SteamID.ToString());
        _adminVoteViews.Remove(player.SteamID.ToString());
        ForgetCommandHelp(player.SteamID.ToString());
        _surface.SetClass(player, "dialog", "Hidden", true);
        _surface.ReleaseInput(player);
        if (_navigator.Sessions.Count == 0)
        {
            _surface.RemoveEntity();
            ClearHelpRenderCache(_surface);
        }
    }

    private void CloseAllMenus(VoteEndReason reason = VoteEndReason.MapChanged)
    {
        ResetVoting(reason);
        _navigator.CloseAll();
        _surface.RemoveEntity();
        CancelAllChatRelays();
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        var steamId = @event.Xuid != 0 ? @event.Xuid.ToString()
            : player?.IsValid == true ? player.SteamID.ToString() : string.Empty;
        if (steamId.Length == 0) return HookResult.Continue;
        OnVotingPlayerDisconnected(steamId);
        _navigator.Close(steamId);
        if (player?.IsValid == true)
        {
            _surface.SetClass(player, "dialog", "Hidden", true);
            _surface.ReleaseInput(player);
            CloseVotePage(player);
            ClosePersonalMenu(player);
        }
        if (_navigator.Sessions.Count == 0)
        {
            _surface.RemoveEntity();
            ClearHelpRenderCache(_surface);
        }

        if (_chatRelays.Remove(steamId, out var relay))
        {
            relay.Timer.Kill();
        }

        return HookResult.Continue;
    }

    private void OnMapStart(string mapName) => CloseAllMenus();

    // === 聊天接力输入 ===

    private HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        if (_chatRelays.Count == 0) return HookResult.Continue;

        var speaker = Utilities.GetPlayerFromUserid(@event.Userid);
        if (speaker == null || !speaker.IsValid) return HookResult.Continue;

        var steamId = speaker.SteamID.ToString();
        if (!_chatRelays.Remove(steamId, out var relay)) return HookResult.Continue;

        relay.Timer.Kill();
        var text = (@event.Text ?? string.Empty).Trim();
        if (text.Length == 0) return HookResult.Continue;

        if (text.Equals("cancel", System.StringComparison.OrdinalIgnoreCase))
        {
            CaorenCupChat.PrintToChat(speaker, "已取消输入。");
            return HookResult.Handled;
        }

        relay.OnText(text);
        return HookResult.Handled;
    }

    // === IMenuHostActions ===

    public void ExecAsPlayer(string steamId, string command)
    {
        var player = FindPlayer(steamId);
        if (player == null) return;
        player.ExecuteClientCommandFromServer(command);
    }

    public void ExecAsServer(string command) => Server.ExecuteCommand(command);

    public void BeginChatRelay(string steamId, string prompt, System.Action<string> onText)
    {
        var player = FindPlayer(steamId);
        if (player == null) return;

        if (_chatRelays.Remove(steamId, out var old))
        {
            old.Timer.Kill();
        }

        // 输入捕获可能占掉聊天键,接力期间先收起菜单。
        ClosePlayerMenu(player);
        CaorenCupChat.PrintToChat(player, prompt);

        var timer = AddTimer(ChatRelayTimeoutSeconds, () =>
        {
            if (_chatRelays.Remove(steamId))
            {
                var p = FindPlayer(steamId);
                if (p != null) CaorenCupChat.PrintToChat(p, "输入超时,已取消。");
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);

        _chatRelays[steamId] = new ChatRelayEntry(prompt, onText, timer);
    }

    public void Chat(string steamId, string message)
    {
        var player = FindPlayer(steamId);
        if (player != null) CaorenCupChat.PrintToChat(player, message);
    }

    private void CancelAllChatRelays()
    {
        foreach (var relay in _chatRelays.Values)
        {
            relay.Timer.Kill();
        }
        _chatRelays.Clear();
    }

    private CCSPlayerController? FindPlayer(string steamId) =>
        Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.SteamID.ToString() == steamId);
}
