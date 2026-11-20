using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCupPlugin;

public partial class CaorenCupPlugin
{
    private const double BindingReminderInterval = 8;
    private const string BindingHudLayout = "panorama/layout/custom_game/caoren_binding_hud.xml";
    private readonly Dictionary<string, bool> _bindingStatus = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _nextBindingReminder = new(StringComparer.Ordinal);
    private string? _bindingReminderMatchId;
    private CCSCustomHudLayout? _bindingHud;

    private static string DescribeLobbyReminder(LobbyReminderMode mode) => mode switch
    {
        LobbyReminderMode.Off => "关闭",
        LobbyReminderMode.Remind => "软提醒",
        LobbyReminderMode.RequireBinding => "绑定后可以加入队伍",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private void SetLobbyReminderMode(LobbyReminderMode mode)
    {
        _lobbyReminderMode = mode;
        if (mode == LobbyReminderMode.RequireBinding)
        {
            TickBindingAdmission();
            RequestMatchStateHeartbeat();
            return;
        }

        RemoveBindingHud();
        EnforceTeamAssignments();
    }

    private void StartFormalBindingAdmission(string? matchId)
    {
        if (string.IsNullOrWhiteSpace(matchId) ||
            string.Equals(_bindingReminderMatchId, matchId, StringComparison.Ordinal)) return;
        _bindingReminderMatchId = matchId;
        SetLobbyReminderMode(LobbyReminderMode.RequireBinding);
    }

    private void FinishFormalBindingAdmission()
    {
        if (_bindingReminderMatchId is not null) SetLobbyReminderMode(LobbyReminderMode.Off);
    }

    private bool IsBindingAdmissionRequired(CCSPlayerController player) =>
        _lobbyReminderMode == LobbyReminderMode.RequireBinding &&
        (!_bindingStatus.TryGetValue(player.SteamID.ToString(), out var bound) || !bound);

    private void ApplyBindingStatus(Dictionary<string, bool>? checks)
    {
        if (checks is null) return;

        var connected = GetConnectedRealPlayerSteamIds();
        foreach (var cached in _bindingStatus.Keys.Where(id => !connected.Contains(id)).ToArray())
            _bindingStatus.Remove(cached);
        foreach (var (id, bound) in checks)
            if (connected.Contains(id)) _bindingStatus[id] = bound;
    }

    private void TickBindingAdmission()
    {
        if (_isUnloading || _lobbyReminderMode != LobbyReminderMode.RequireBinding) return;
        var bindingCompleted = false;
        foreach (var player in Utilities.GetPlayers().Where(IsRealPlayer))
        {
            var steamId = player.SteamID.ToString();
            if (!IsBindingAdmissionRequired(player))
            {
                if (_nextBindingReminder.Remove(steamId))
                {
                    SetBindingHudVisible(player, false);
                    CaorenCupChat.PrintToChat(player, "CS 游戏身份已确认。");
                    bindingCompleted = true;
                }
                continue;
            }

            if (player.Team != CsTeam.Spectator) player.ChangeTeam(CsTeam.Spectator);
            if (!_bindingStatus.ContainsKey(steamId)) continue;
            SetBindingHudVisible(player, true);
            var now = Server.EngineTime;
            if (now < _nextBindingReminder.GetValueOrDefault(steamId)) continue;
            _nextBindingReminder[steamId] = now + BindingReminderInterval;
            CaorenCupChat.PrintToChat(player, "绑定账号后即可加入队伍。输入 /ccbind 获取绑定码，按屏幕中央指引完成绑定。");
        }
        if (bindingCompleted) EnforceTeamAssignments();
    }

    private void SetBindingHudVisible(CCSPlayerController player, bool visible)
    {
        if (_bindingHud is not { IsValid: true })
        {
            if (!visible) return;
            _bindingHud = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
            if (_bindingHud is null) return;
            _bindingHud.StrLayout = BindingHudLayout;
            _bindingHud.DispatchSpawn();
        }
        _bindingHud.SetHasClassForPlayer(player, "binding_hud", "Hidden", !visible);
    }

    private void RemoveBindingHud()
    {
        if (_bindingHud is { IsValid: true }) _bindingHud.Remove();
        _bindingHud = null;
        _nextBindingReminder.Clear();
    }
}
