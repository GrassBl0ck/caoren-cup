using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using System;
using System.Collections.Generic;

namespace CaorenCup.Features;

public class PlaySoundFeature : ICaorenFeature
{
    public string FeatureName => "全服音效广播 (PlaySound)";

    private CaorenCupPlugin _plugin = null!;
    private PlaySoundSettings _settings = null!;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 指令已改成 css_ps，支持聊天栏 /ps 和 !ps
        _plugin.AddCommand("css_ps", "全服播放指定音效", OnCommandPs);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _settings = config.PlaySound;

        if (_settings.Aliases == null)
            _settings.Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        else
            _settings.Aliases = new Dictionary<string, string>(_settings.Aliases, StringComparer.OrdinalIgnoreCase);
    }

    public void OnUnload() { }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        _plugin.SaveConfig();
    }

    private void OnCommandPs(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "无权限执行此指令。");
            return;
        }

        bool isChatCommand = info.CallingContext == CommandCallingContext.Chat;

        if (info.ArgCount == 1)
        {
            if (player != null)
            {
                if (isChatCommand)
                {
                    CaorenCupUtils.PrintToChat(player, $"\u0004当前状态: \u0001{GetStatusInfo()}");
                    CaorenCupUtils.PrintToChat(player, $"\u0004用法: \u0001/ps <文件名/别名>  \u0001(输入0禁用)");
                    CaorenCupUtils.PrintToChat(player, $"\u0001详细帮助已打印到控制台~");
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
            CaorenCupUtils.PrintToChatAll($"\u0002已禁用\u0001 全服音效广播。");
            return;
        }

        if (lowerArg1 == "1" || lowerArg1 == "on" || lowerArg1 == "true")
        {
            SetEnabled(true);
            CaorenCupUtils.PrintToChatAll($"\u0004已启用\u0001 全服音效广播。");
            return;
        }

        if (!_settings.Enabled)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "该功能当前已被禁用，请先输入 /ps 1 开启。");
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

        foreach (var p in Utilities.GetPlayers())
        {
            if (p != null && p.IsValid && !p.IsBot)
            {
                p.ExecuteClientCommand($"play \"{soundPath}\"");
            }
        }

        if (player != null)
        {
            if (isChatCommand)
                CaorenCupUtils.PrintToChat(player, $"已向全服播放音效: \u0004{soundPath}");
            else
                player.PrintToConsole($"[草人杯] 成功向全服发送播放指令: {soundPath}");
        }
    }

    // --- 接口实现 ---
    public string GetHelpEntry() => "/ps - (管理员) 向全服播放指定路径或别名的音效";

    public string GetStatusInfo() => _settings.Enabled ? "已开启" : "已禁用";

    public string? GetPublicConfigInfo() => null;

    public string GetFeatureDescription()
    {
        return "【全服音效广播】管理员专属工具，支持别名映射和自动补全，一键强制全服播放音频。";
    }
}