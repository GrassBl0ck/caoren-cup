// -----------------------------------------------------------------------------
// Third-party notice
// -----------------------------------------------------------------------------
// This module contains code, logic, or implementation ideas adapted from:
//
//   oqyh/cs2-ESP-Players-GoldKingZ
//
// Original author:
//   oqyh / GoldKingZ
//
// Permission:
//   Used/adapted with explicit permission from the original author.
//
// CaorenCup sincerely thanks the original author for their work and contribution
// to the CS2 plugin community.
// -----------------------------------------------------------------------------
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CaorenCup.Features;

public class EspFeature : ICaorenFeature
{
    public string FeatureName => "ESP 透视模块";

    private CaorenCupPlugin _plugin = null!;
    private CaorenCupConfig _config = null!;
    private Timer? _glowTimer;
    private bool _registered;
    private bool _initialized;
    private bool _disabledPersistedAutoStart;

    private class EspData
    {
        public CDynamicProp? ModelRelay { get; set; }
        public CDynamicProp? ModelGlow { get; set; }
        public string ModelName { get; set; } = string.Empty;
    }

    private readonly Dictionary<uint, EspData> _playerData = new();

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        _plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        _plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        _plugin.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        _plugin.RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        _registered = true;
        _initialized = true;
        if (_disabledPersistedAutoStart)
        {
            try { _plugin.SaveConfig(); } catch { }
            _disabledPersistedAutoStart = false;
        }
        ApplyConfigStatus();
        _plugin.AddCommand("css_esp", "控制 ESP 模块", OnEspCommand);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        if (config == null || config.Esp == null) return;
        _config = config;
        if (_config.Esp.Enabled)
        {
            _config.Esp.Enabled = false;
            _disabledPersistedAutoStart = true;
        }
    }

    public void OnUnload()
    {
        if (_config?.Esp != null) _config.Esp.Enabled = false;
        try { _plugin.SaveConfig(); } catch { }
        if (_glowTimer != null)
        {
            _glowTimer.Kill();
            _glowTimer = null;
        }
        ClearAllGlowEntities();
        if (_registered)
        {
            try { _plugin.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath); } catch { }
            try { _plugin.DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect); } catch { }
            try { _plugin.RemoveListener<Listeners.CheckTransmit>(OnCheckTransmit); } catch { }
            _registered = false;
        }
        _initialized = false;
    }

    public void SetEnabled(bool enabled)
    {
        if (_config == null || _config.Esp == null) return;

        _config.Esp.Enabled = enabled;
        ApplyConfigStatus();
        _plugin.SaveConfig();
    }

    private void ApplyConfigStatus()
    {
        if (_config == null || _config.Esp == null) return;
        if (!_initialized) return;

        if (_config.Esp.Enabled)
        {
            if (_glowTimer == null)
                _glowTimer = _plugin.AddTimer(1.0f, EspTimerTick, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }
        else
        {
            if (_glowTimer != null)
            {
                _glowTimer.Kill();
                _glowTimer = null;
            }
            ClearAllGlowEntities();
        }
    }

    private void OnEspCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (_config == null || _config.Esp == null) return;

        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "无权限执行此指令。");
            return;
        }

        int argCount = info.ArgCount;

        if (argCount == 1)
        {
            if (player != null)
            {
                CaorenCupUtils.PrintToChat(player, $"当前状态: \x04{GetStatusInfo()}");
                CaorenCupUtils.PrintToChat(player, "用法: \x04/esp <t/ct/all/0> [最远距离] [模式]");
                CaorenCupUtils.PrintToChat(player, "说明: \x01接 \x040\x01 为禁用模块。模式: 0=持续透视, 1=准星指着透。");
            }
            return;
        }

        string targetArg = info.GetArg(1).ToLower();

        if (targetArg == "0")
        {
            SetEnabled(false);
            if (player != null) CaorenCupUtils.PrintToChatAll("已 \x02禁用\x01 ESP 透视模块。");
            return;
        }

        if (targetArg != "t" && targetArg != "ct" && targetArg != "all")
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效阵营，请使用 t, ct, all，或使用 0 禁用。");
            return;
        }

        int range = _config.Esp.MaxRange;
        if (argCount >= 3) int.TryParse(info.GetArg(2), out range);

        int mode = _config.Esp.Mode;
        if (argCount >= 4) int.TryParse(info.GetArg(3), out mode);

        _config.Esp.Enabled = true;
        _config.Esp.Target = targetArg;
        _config.Esp.MaxRange = range;
        _config.Esp.Mode = mode;
        _plugin.SaveConfig();

        ClearAllGlowEntities();
        ApplyConfigStatus();

        string modeStr = mode == 0 ? "持续透视" : "准星指向";
        CaorenCupUtils.PrintToChatAll($"ESP 已开启 -> 透视阵营: {targetArg.ToUpper()} | 距离: {range} | 模式: {modeStr}");
    }

    public string GetHelpEntry() => "/esp - 全局 ESP 透视管理 (多参集成指令)";
    public string GetStatusInfo() => (_config?.Esp.Enabled ?? false) ? $"已开启 (透视者:{_config.Esp.Target.ToUpper()} 距离:{_config.Esp.MaxRange} 模式:{_config.Esp.Mode})" : "已禁用";
    public string? GetPublicConfigInfo() => (_config?.Esp.Enabled ?? false) ? $"ESP 透视: {_config.Esp.Target.ToUpper()} 开启 (距离:{_config.Esp.MaxRange})" : null;
    public string GetFeatureDescription() => "透视模块。在开启后，指定的阵营可以透过墙壁看到敌方发光的模型。";

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!_config.Esp.Enabled || player == null || !player.IsValid || !player.PawnIsAlive) return HookResult.Continue;
        SetGlowPlayer(player);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;
        RemoveGlowPlayer(player);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;
        RemoveGlowPlayer(player);
        return HookResult.Continue;
    }

    private void EspTimerTick()
    {
        if (!_config.Esp.Enabled) return;
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive || player.TeamNum < (int)CsTeam.Terrorist) continue;
            SetGlowPlayer(player);
        }
    }

    private void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (!_config.Esp.Enabled) return;

        foreach ((CCheckTransmitInfo info, CCSPlayerController? observer) in infoList)
        {
            if (observer == null || !observer.IsValid) continue;

            if (_config.Esp.DisableGlowOnGOTV && observer.IsHLTV)
            {
                RemoveAllGlowFromTransmit(info);
                continue;
            }

            bool hasPermission = _config.Esp.Target == "all" ||
                                (_config.Esp.Target == "t" && observer.TeamNum == (byte)CsTeam.Terrorist) ||
                                (_config.Esp.Target == "ct" && observer.TeamNum == (byte)CsTeam.CounterTerrorist);

            if (!hasPermission)
                RemoveAllGlowFromTransmit(info);
            else
            {
                foreach (var kvp in _playerData)
                {
                    var targetPlayer = Utilities.GetPlayerFromIndex((int)kvp.Key);
                    if (targetPlayer != null && targetPlayer.IsValid && targetPlayer.TeamNum == observer.TeamNum)
                    {
                        if (kvp.Value.ModelGlow != null && kvp.Value.ModelGlow.IsValid)
                            info.TransmitEntities.Remove(kvp.Value.ModelGlow);
                        if (kvp.Value.ModelRelay != null && kvp.Value.ModelRelay.IsValid)
                            info.TransmitEntities.Remove(kvp.Value.ModelRelay);
                    }
                }
            }
        }
    }

    private void RemoveAllGlowFromTransmit(CCheckTransmitInfo info)
    {
        foreach (var data in _playerData.Values)
        {
            if (data.ModelGlow != null && data.ModelGlow.IsValid) info.TransmitEntities.Remove(data.ModelGlow);
            if (data.ModelRelay != null && data.ModelRelay.IsValid) info.TransmitEntities.Remove(data.ModelRelay);
        }
    }

    private void SetGlowPlayer(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return;

        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid) return;

        var skeleton = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
        if (skeleton == null) return;

        string modelName = skeleton.ModelState.ModelName;
        if (string.IsNullOrEmpty(modelName)) return;

        uint index = player.Index;
        if (!_playerData.TryGetValue(index, out var data))
        {
            data = new EspData();
            _playerData[index] = data;
        }

        if (data.ModelRelay != null && data.ModelRelay.IsValid && data.ModelGlow != null && data.ModelGlow.IsValid)
        {
            if (data.ModelName != modelName)
            {
                data.ModelRelay.Remove();
                data.ModelGlow.Remove();
            }
            else return;
        }

        data.ModelName = modelName;

        data.ModelRelay = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (data.ModelRelay == null) return;

        data.ModelRelay.DispatchSpawn();
        data.ModelRelay.SetModel(modelName);
        data.ModelRelay.Spawnflags = 256u;
        data.ModelRelay.RenderMode = RenderMode_t.kRenderNone;

        data.ModelGlow = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (data.ModelGlow == null) return;

        data.ModelGlow.Render = Color.FromArgb(1, 0, 0, 0);
        data.ModelGlow.DispatchSpawn();
        data.ModelGlow.SetModel(modelName);
        data.ModelGlow.Spawnflags = 256u;

        if (player.TeamNum == (byte)CsTeam.CounterTerrorist)
            data.ModelGlow.Glow.GlowColorOverride = ParseColor(_config.Esp.GlowColorCT);
        else if (player.TeamNum == (byte)CsTeam.Terrorist)
            data.ModelGlow.Glow.GlowColorOverride = ParseColor(_config.Esp.GlowColorT);

        data.ModelGlow.Glow.GlowRange = _config.Esp.MaxRange;
        data.ModelGlow.Glow.GlowTeam = -1;
        data.ModelGlow.Glow.GlowType = _config.Esp.Mode == 1 ? 2 : 3;
        data.ModelGlow.Glow.GlowRangeMin = 100;

        data.ModelRelay.AcceptInput("FollowEntity", pawn, data.ModelRelay, "!activator");
        data.ModelGlow.AcceptInput("FollowEntity", data.ModelRelay, data.ModelGlow, "!activator");
    }

    private void RemoveGlowPlayer(CCSPlayerController? player)
    {
        if (player == null) return;

        if (_playerData.TryGetValue(player.Index, out var data))
        {
            if (data.ModelGlow != null && data.ModelGlow.IsValid) data.ModelGlow.Remove();
            if (data.ModelRelay != null && data.ModelRelay.IsValid) data.ModelRelay.Remove();
            _playerData.Remove(player.Index);
        }
    }

    private void ClearAllGlowEntities()
    {
        foreach (var data in _playerData.Values)
        {
            if (data.ModelGlow != null && data.ModelGlow.IsValid) data.ModelGlow.Remove();
            if (data.ModelRelay != null && data.ModelRelay.IsValid) data.ModelRelay.Remove();
        }
        _playerData.Clear();
    }

    private Color ParseColor(string rgba)
    {
        try
        {
            var parts = rgba.Split(',');
            if (parts.Length >= 4)
                return Color.FromArgb(
                    int.Parse(parts[3].Trim()), int.Parse(parts[0].Trim()),
                    int.Parse(parts[1].Trim()), int.Parse(parts[2].Trim())
                );
        }
        catch { }
        return Color.White;
    }
}
