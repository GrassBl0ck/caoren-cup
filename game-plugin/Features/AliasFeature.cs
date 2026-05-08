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

        // ��̬ע�������ļ��������ָ��
        foreach (var kvp in _settings.CommandMap)
        {
            // === �ؼ�������հ����� ===
            string currentChatCmd = kvp.Key;
            string currentConsoleCmd = kvp.Value;

            plugin.AddCommand(currentChatCmd, $"ִ��: {currentConsoleCmd}", (player, info) =>
            {
                ExecuteAlias(player, currentChatCmd, currentConsoleCmd);
            });
        }

        // ע�����ָ��
        plugin.AddCommand("alias_list", "�г���ǰ���б���", OnCommandList);
        plugin.AddCommand("alias_reload", "��ʾ���ط���", OnCommandReload);
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

    public string GetHelpEntry() => $" {ChatColors.Green}/alias_list{ChatColors.Default} �鿴�Ѽ��صĿ��ָ��";

    public string GetStatusInfo() => $" {ChatColors.Olive}Alias{ChatColors.Default}: {_settings.Enabled} | ������: {_settings.CommandMap.Count}";

    public string GetFeatureDescription()
    {
        return " [ָ�����] �Զ�����ָ��ϵͳ��\n" +
               " ����ͨ�������ļ������ӵĿ���ָ̨��ӳ��Ϊ�򵥵��������";
    }

    // --- �����߼� ---

    private void OnCommandList(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        CaorenCupUtils.PrintToChat(player, $"=== ��ǰ���صı��� ({_settings.CommandMap.Count}��) ===");

        if (!_settings.Enabled)
        {
            CaorenCupUtils.PrintToChat(player, $"{ChatColors.Red}���棺Alias ģ�鵱ǰ���ڹر�״̬��");
        }

        foreach (var kvp in _settings.CommandMap)
        {
            // ��ӡ��ʽ�� p1 -> mp_pause_match...
            CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/{kvp.Key}{ChatColors.Default} -> {kvp.Value}");
        }

        if (_settings.CommandMap.Count == 0)
        {
            CaorenCupUtils.PrintToChat(player, $"{ChatColors.Red}�б�Ϊ�գ����� CaorenCup.json �� Alias.CommandMap ���á�");
        }
    }

    private void ExecuteAlias(CCSPlayerController? player, string chatKey, string consoleCmd)
    {
        if (!_settings.Enabled)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "Alias ģ���ѽ��á�");
            return;
        }

        // Ȩ�޼��
        if (player != null && !string.IsNullOrEmpty(_settings.Permission))
        {
            if (!AdminManager.PlayerHasPermissions(player, _settings.Permission))
            {
                CaorenCupUtils.PrintToChat(player, $"{ChatColors.Red}��û��Ȩ��ִ�д�ָ�");
                return;
            }
        }
        // ִ��
        Server.ExecuteCommand(consoleCmd);

        if (player != null)
        {
            CaorenCupUtils.PrintToChat(player, $"��ִ��: {ChatColors.Green}{chatKey}");
        }
    }

    private void OnCommandReload(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null) CaorenCupUtils.PrintToChat(player, "�޸� json ��������������Լ�����ָ�");
    }
}
