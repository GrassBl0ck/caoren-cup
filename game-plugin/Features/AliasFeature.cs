using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CaorenCup;

namespace CaorenCup.Features;

public class AliasFeature : ICaorenFeature
{
    public string FeatureName => "Command Alias";

    private AliasSettings _settings = new();
    private CaorenCupPlugin _plugin = null!;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 动态注册配置文件里的所有指令
        foreach (var kvp in _settings.CommandMap)
        {
            // === 关键：解决闭包陷阱 ===
            string currentChatCmd = kvp.Key;
            string currentConsoleCmd = kvp.Value;

            plugin.AddCommand(currentChatCmd, $"执行: {currentConsoleCmd}", (player, info) =>
            {
                ExecuteAlias(player, currentChatCmd, currentConsoleCmd);
            });
        }

        // 注册调试指令
        plugin.AddCommand("alias_list", "列出当前所有别名", OnCommandList);
        plugin.AddCommand("alias_reload", "提示重载方法", OnCommandReload);
    }

    public void OnConfigParsed(CaorenCup.CaorenCupConfig config)
    {
        _settings = config.Alias;
    }

    public void OnUnload() { }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        _plugin.SaveConfig();
    }

    public string? GetPublicConfigInfo() => null;

    public string GetHelpEntry() => $" {ChatColors.Green}/alias_list{ChatColors.Default} 查看已加载的快捷指令";

    public string GetStatusInfo() => $" {ChatColors.Olive}Alias{ChatColors.Default}: {_settings.Enabled} | 加载数: {_settings.CommandMap.Count}";

    public string GetFeatureDescription()
    {
        return " [指令别名] 自定义快捷指令系统。\n" +
               " 允许通过配置文件将复杂的控制台指令映射为简单的聊天命令。";
    }

    // --- 核心逻辑 ---

    private void OnCommandList(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        CaorenCupUtils.PrintToChat(player, $"=== 当前加载的别名 ({_settings.CommandMap.Count}个) ===");

        if (!_settings.Enabled)
        {
            CaorenCupUtils.PrintToChat(player, $"{ChatColors.Red}警告：Alias 模块当前处于关闭状态！");
        }

        foreach (var kvp in _settings.CommandMap)
        {
            // 打印格式： p1 -> mp_pause_match...
            CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/{kvp.Key}{ChatColors.Default} -> {kvp.Value}");
        }

        if (_settings.CommandMap.Count == 0)
        {
            CaorenCupUtils.PrintToChat(player, $"{ChatColors.Red}列表为空！请检查 CaorenCup.json 的 Alias.CommandMap 配置。");
        }
    }

    private void ExecuteAlias(CCSPlayerController? player, string chatKey, string consoleCmd)
    {
        if (!_settings.Enabled)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "Alias 模块已禁用。");
            return;
        }

        // 权限检查
        if (player != null && !string.IsNullOrEmpty(_settings.Permission))
        {
            if (!AdminManager.PlayerHasPermissions(player, _settings.Permission))
            {
                CaorenCupUtils.PrintToChat(player, $"{ChatColors.Red}你没有权限执行此指令。");
                return;
            }
        }
        w
        // 执行
        Server.ExecuteCommand(consoleCmd);

        if (player != null)
        {
            CaorenCupUtils.PrintToChat(player, $"已执行: {ChatColors.Green}{chatKey}");
        }
    }

    private void OnCommandReload(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null) CaorenCupUtils.PrintToChat(player, "修改 json 后，请重启服务器以加载新指令。");
    }
}