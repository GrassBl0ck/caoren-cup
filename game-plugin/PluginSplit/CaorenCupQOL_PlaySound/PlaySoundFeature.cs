using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using System;
using System.Collections.Generic;
using CaorenCup.Contracts;

namespace CaorenCup.QOL.PlaySound;

public sealed partial class CaorenCupPlaySoundPlugin : BasePlugin
{
    public override string ModuleName => "CaorenCup Play Sound";
    public override string ModuleVersion => "1.10.0";
    public override string ModuleAuthor => "Graslock + AI";

    private ICaorenCupCoreApi Core => CaorenCupCoreAccess.TryGet()
        ?? throw new InvalidOperationException("CaorenCupCore is unavailable.");
    private PlaySoundSettings _settings = null!;
    private CaorenAudioService? _audio;
    private NativeManagedAudioBackend? _nativeAudio;
    private AudioAssetLibrary? _audioAssets;

    public override void Load(bool hotReload)
    {
        // 指令已改成 css_ps，支持聊天栏 /ps
        AddCommand("css_ps", "全服播放指定音效", OnCommandPs);
        AddCommand("css_caudio", "统一音频管理（管理员，全服）", OnCommandAudio);
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (_audioAssets == null) return;
            manifest.AddResource("soundevents/soundevents_addon.vsndevts");
            foreach (var resource in _audioAssets.Assets.SelectMany(asset => asset.Resources).Distinct()) manifest.AddResource(resource);
        });
        RegisterListener<Listeners.OnMapEnd>(() => _nativeAudio?.MapEnd());
        AddTimer(0.1f, () => _nativeAudio?.Tick(), CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
#if CRC_AUDIO_PROBE
        RegisterAudioCapabilityProbe();
#endif
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _ = Core;
        _settings = Core.Config.PlaySound;
        try
        {
            _audioAssets = new AudioAssetLibrary(Path.Combine(ModuleDirectory, "audio-assets.json"));
            _nativeAudio = new NativeManagedAudioBackend(this, _audioAssets);
            _audio = new CaorenAudioService(Path.Combine(ModuleDirectory, "audio-events.json"), _nativeAudio);
        }
        catch (Exception ex)
        {
            _nativeAudio?.StopAll(); _nativeAudio = null; _audioAssets = null;
            Console.WriteLine("[CaorenCupPlaySound] 自定义清单无效，保留原文件，仅加载内置音频：" + ex.Message);
            _audio = new CaorenAudioService();
        }
        CaorenCupAudioAccess.Publish(_audio);

        if (_settings.Aliases == null)
            _settings.Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        else
            _settings.Aliases = new Dictionary<string, string>(_settings.Aliases, StringComparer.OrdinalIgnoreCase);

        CaorenCupModuleRegistry.Register(new CaorenCupModuleDescriptor(
            this, "playsound", "全服音效广播 (PlaySound)", "PlaySoundFeature",
            GetHelpEntry, GetStatusInfo, GetFeatureDescription, GetPublicConfigInfo,
            () => SetEnabled(false)));
    }

    public override void Unload(bool hotReload)
    {
#if CRC_AUDIO_PROBE
        StopAudioProbe();
#endif
        if (_audio != null) CaorenCupAudioAccess.Withdraw(_audio);
        _nativeAudio?.StopAll();
        CaorenCupModuleRegistry.Unregister(this);
    }

    private void OnCommandAudio(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        { CaorenCupChat.PrintToChat(player, "无权限执行此指令。"); return; }
        var operation = info.ArgCount > 1 ? info.GetArg(1).ToLowerInvariant() : "status";
        CaorenAudioResult result;
        if (_audio == null) result = new(false, "音频服务尚未加载。");
        else if (operation == "play" && info.ArgCount == 3) result = _audio.Play(info.GetArg(2));
        else if (operation == "status") result = new(true, "影响范围：全服；" + string.Join("；", _audio.GetChannels().Select(s =>
            $"{s.Channel}: {s.DisplayName} {s.State} {s.PositionSeconds:0.0}/{s.DurationSeconds:0.0} 秒"))
            + "；事件：" + string.Join(", ", _audio.Events.Select(e => e.Id)));
        else if (info.ArgCount == 3 && Enum.TryParse<CaorenAudioChannel>(info.GetArg(2), true, out var channel)
            && Enum.IsDefined(channel) && channel != CaorenAudioChannel.Effect)
        {
            var control = operation switch { "pause" => CaorenAudioControl.Pause, "resume" => CaorenAudioControl.Resume,
                "stop" => CaorenAudioControl.Stop, "loopon" => CaorenAudioControl.LoopOn, "loopoff" => CaorenAudioControl.LoopOff, _ => (CaorenAudioControl?)null };
            result = control != null ? _audio.Control(channel, control.Value) : new(false, "未知控制操作。");
        }
        else result = new(false, "用法：/caudio status；/caudio play <事件ID>；/caudio pause|resume|stop|loopon|loopoff music|broadcast。影响范围：全服。");
        if (player == null) Console.WriteLine("[CaorenCupAudio] " + result.Message);
        else CaorenCupChat.PrintToChat(player, result.Message);
    }

    private void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        Core.SaveConfig();
    }

    private void OnCommandPs(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupChat.PrintToChat(player, "无权限执行此指令。");
            return;
        }

        bool isChatCommand = info.CallingContext == CommandCallingContext.Chat;

        if (info.ArgCount == 1)
        {
            if (player != null)
            {
                if (isChatCommand)
                {
                    CaorenCupChat.PrintToChat(player, $"\u0004当前状态: \u0001{GetStatusInfo()}");
                    CaorenCupChat.PrintToChat(player, $"\u0004用法: \u0001/ps <文件名/别名>  \u0001(输入0禁用)");
                    CaorenCupChat.PrintToChat(player, $"\u0001详细帮助已打印到控制台~");
                }
                else
                {
                    player.PrintToConsole("========== [草人杯] 全服音效广播 ==========");
                    player.PrintToConsole($"当前状态: {GetStatusInfo()}");
                    player.PrintToConsole("用法: css_ps <文件名/别名/完整路径>");
                    player.PrintToConsole($"  默认前缀: {(_settings.DefaultPrefix == "" ? "无" : _settings.DefaultPrefix)}");
                    player.PrintToConsole("  可用别名: " + string.Join(", ", _settings.Aliases.Keys));
                    player.PrintToConsole("范例1(别名): css_ps win");
                    player.PrintToConsole("范例2(自动补全): css_ps test (将自动补全前缀和 .vsnd_c 后缀)");
                    player.PrintToConsole("==========================================");
                }
            }
            return;
        }

        string arg1 = info.GetArg(1);
        string lowerArg1 = arg1.ToLower();

        if (lowerArg1 == "0" || lowerArg1 == "off" || lowerArg1 == "false")
        {
            SetEnabled(false);
            CaorenCupChat.PrintToChatAll($"\u0002已禁用\u0001 全服音效广播。");
            return;
        }

        if (lowerArg1 == "1" || lowerArg1 == "on" || lowerArg1 == "true")
        {
            SetEnabled(true);
            CaorenCupChat.PrintToChatAll($"\u0004已启用\u0001 全服音效广播。");
            return;
        }

        if (!_settings.Enabled)
        {
            if (player != null) CaorenCupChat.PrintToChat(player, "该功能当前已被禁用，请先输入 /ps 1 开启。");
            return;
        }

        // --- 智能路径解析核心 ---
        string soundPath = arg1;

        // 1. 尝试匹配超级别名
        if (_settings.Aliases.TryGetValue(arg1, out string? aliasPath))
        {
            soundPath = aliasPath;
        }
        else
        {
            // 2. 若没有斜杠，代表只输入了文件名，尝试拼上默认前缀
            if (!soundPath.Contains('/') && !string.IsNullOrWhiteSpace(_settings.DefaultPrefix))
            {
                string prefix = _settings.DefaultPrefix.EndsWith("/") ? _settings.DefaultPrefix : _settings.DefaultPrefix + "/";
                soundPath = prefix + soundPath;
            }
        }

        // 3. 自动补齐 CS2 音频后缀
        if (!soundPath.EndsWith(".vsnd_c", StringComparison.OrdinalIgnoreCase) &&
            !soundPath.EndsWith(".vsnd", StringComparison.OrdinalIgnoreCase))
        {
            soundPath += ".vsnd_c";
        }
        // ------------------------

        var result = _audio?.PlayLegacyBroadcast(soundPath) ?? new CaorenAudioResult(false, "音频服务尚未加载。");
        if (player == null) Console.WriteLine("[草人杯] " + result.Message);

        if (player != null)
        {
            if (isChatCommand)
                CaorenCupChat.PrintToChat(player, result.Message);
            else
                player.PrintToConsole($"[草人杯] {result.Message}");
        }
    }

    // --- 接口实现 ---
    public string GetHelpEntry() => "/ps - (管理员) 兼容音效广播；/caudio - 统一音频事件与能力状态";

    public string GetStatusInfo() => (_settings.Enabled ? "旧 /ps 已开启" : "旧 /ps 已禁用") + " | 统一短音频服务 | 长音频控制未验证";

    public string? GetPublicConfigInfo() => null;

    public string GetFeatureDescription()
    {
        return "【统一音频】/ps 保留别名和路径补全，按每位玩家的草人杯音量发送一次性播放请求；/caudio 查询事件和能力。长音频控制尚未验证。";
    }
}
