using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;

namespace CaorenCup.Features;

public class ArmorFeature : ICaorenFeature
{
    public string FeatureName => "Armor (防弹衣耐久控制)";

    private CaorenCupPlugin _plugin = null!;

    // 独立运行时配置：不写入 CaorenCup.json，重启后默认关闭
    private readonly ArmorRuntimeSettings _settings = new();

    private sealed class ArmorRuntimeSettings
    {
        public bool Enabled { get; set; } = false;
        public string Target { get; set; } = "all"; // t, ct, all
        public int Value { get; set; } = 100;
    }

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // /armor 会走 css_armor；额外注册 armor 是为了兼容控制台直接输入 armor
        plugin.AddCommand("css_armor", "设置购买防弹衣后的耐久: /armor <t/ct/all/0> <Value>", OnArmorCommand);
        plugin.AddCommand("armor", "设置购买防弹衣后的耐久: /armor <t/ct/all/0> <Value>", OnArmorCommand);

        // 必须用 Post：等游戏先完成购买、先把护甲设成默认 100，再由本模块覆盖成自定义值
        plugin.RegisterEventHandler<EventItemPurchase>(OnItemPurchase, HookMode.Post);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        // 独立版不依赖总配置
    }

    public void OnUnload()
    {
        // 不修改现有玩家护甲，只停止后续干预
        _settings.Enabled = false;
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
    }

    private void OnArmorCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "你没有权限使用此指令。");
            return;
        }

        if (info.ArgCount == 1)
        {
            PrintUsage(player);
            return;
        }

        string targetArg = info.GetArg(1).Trim().ToLowerInvariant();

        if (targetArg == "0" || targetArg == "off" || targetArg == "disable")
        {
            _settings.Enabled = false;
            CaorenCupUtils.PrintToChatAll($"{ChatColors.Green}Armor 防弹衣耐久控制{ChatColors.Default} 已禁用，后续购买防弹衣恢复游戏默认耐久。");
            return;
        }

        if (targetArg != "t" && targetArg != "ct" && targetArg != "all")
        {
            Reply(player, "无效目标，请使用 t / ct / all / 0。");
            return;
        }

        if (info.ArgCount < 3)
        {
            PrintUsage(player);
            return;
        }

        if (!int.TryParse(info.GetArg(2), out int armorValue))
        {
            Reply(player, "无效的护甲耐久数值。");
            return;
        }

        // 允许 0，表示买了防弹衣也变成 0 护甲。
        // 上限给到 1000，避免误输入特别大的数把属性写爆。
        int clampedValue = Math.Clamp(armorValue, 0, 1000);

        _settings.Enabled = true;
        _settings.Target = targetArg;
        _settings.Value = clampedValue;

        string targetName = GetTargetDisplayName(targetArg);

        CaorenCupUtils.PrintToChatAll(
            $"{ChatColors.Green}Armor 防弹衣耐久控制已启用！{ChatColors.Default} " +
            $"目标:{ChatColors.Green}{targetName}{ChatColors.Default} | " +
            $"购买防弹衣后耐久:{ChatColors.Green}{clampedValue}{ChatColors.Default}"
        );

        if (armorValue != clampedValue)
        {
            Reply(player, $"输入值 {armorValue} 已被限制为 {clampedValue}。");
        }
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        string status = GetStatusInfo();

        Reply(player, "=== Armor 防弹衣耐久控制 ===");
        Reply(player, $"{ChatColors.Green}/armor 0{ChatColors.Default} : 关闭模块，恢复游戏默认购买护甲逻辑");
        Reply(player, $"{ChatColors.Green}/armor <t/ct/all> <Value>{ChatColors.Default}");
        Reply(player, $"示例: {ChatColors.Green}/armor t 50{ChatColors.Default}  T 方购买防弹衣后护甲耐久变为 50");
        Reply(player, $"示例: {ChatColors.Green}/armor ct 150{ChatColors.Default} CT 方购买防弹衣后护甲耐久变为 150");
        Reply(player, $"示例: {ChatColors.Green}/armor all 0{ChatColors.Default} 所有人买甲后护甲耐久变为 0");
        Reply(player, $"当前状态: {status}");
    }

    private HookResult OnItemPurchase(EventItemPurchase @event, GameEventInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;
        if (!player.PawnIsAlive) return HookResult.Continue;

        if (!IsTarget(player)) return HookResult.Continue;

        string weapon = (@event.Weapon ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsArmorPurchase(weapon)) return HookResult.Continue;

        // 多延迟几次是为了防止购买事件后游戏本身又把 ArmorValue 覆盖回 100。
        ApplyArmor(player);
        _plugin.AddTimer(0.05f, () => ApplyArmor(player));
        _plugin.AddTimer(0.20f, () => ApplyArmor(player));

        return HookResult.Continue;
    }

    private bool IsArmorPurchase(string weapon)
    {
        // 常见事件值：
        // item_kevlar       = 防弹衣
        // item_assaultsuit  = 防弹衣 + 头盔
        // 不处理 item_helmet，因为它只买头盔，不应强行改护甲耐久。
        if (weapon == "item_kevlar" || weapon == "kevlar") return true;
        if (weapon == "item_assaultsuit" || weapon == "assaultsuit") return true;

        // 兼容不同版本事件名
        if (weapon.Contains("kevlar")) return true;
        if (weapon.Contains("assaultsuit")) return true;
        if (weapon.Contains("vest")) return true;

        return false;
    }

    private void ApplyArmor(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive) return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        pawn.ArmorValue = _settings.Value;

        // 尽量同步到客户端 HUD。不同 CSS 版本 schema 名可能不同，所以做容错。
        try
        {
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
        }
        catch
        {
            try
            {
                Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_ArmorValue");
            }
            catch
            {
                // 某些版本不需要手动 SetStateChanged，直接写 ArmorValue 也会生效。
            }
        }
    }

    private bool IsTarget(CCSPlayerController player)
    {
        if (_settings.Target == "all") return true;

        if (_settings.Target == "t" &&
            player.TeamNum == (byte)CsTeam.Terrorist)
            return true;

        if (_settings.Target == "ct" &&
            player.TeamNum == (byte)CsTeam.CounterTerrorist)
            return true;

        return false;
    }

    private string GetTargetDisplayName(string target)
    {
        return target switch
        {
            "t" => "T方",
            "ct" => "CT方",
            "all" => "全体玩家",
            _ => target.ToUpperInvariant()
        };
    }

    private void Reply(CCSPlayerController? player, string message)
    {
        if (player != null && player.IsValid)
            CaorenCupUtils.PrintToChat(player, message);
        else
            Console.WriteLine($"[草人杯] {message}");
    }

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/armor{ChatColors.Default} : 设置购买防弹衣后的护甲耐久";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled)
            return $"Armor: {ChatColors.Red}已禁用{ChatColors.Default}";

        return $"Armor: {ChatColors.Green}已启用{ChatColors.Default} | 目标:{_settings.Target.ToUpper()} | 买甲后护甲:{_settings.Value}";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled) return null;
        return $"[防弹衣耐久] {GetTargetDisplayName(_settings.Target)}购买防弹衣后护甲耐久 = {_settings.Value}";
    }

    public string GetFeatureDescription()
    {
        return " [Armor] 防弹衣耐久控制模块。\n" +
               " 管理员可用 /armor <t/ct/all/0> <Value> 控制某一方购买防弹衣后的护甲耐久。\n" +
               " 例如 /armor t 50 表示 T 方购买防弹衣后护甲耐久固定为 50。\n" +
               " 例如 /armor ct 150 表示 CT 方购买防弹衣后护甲耐久固定为 150。\n" +
               " /armor 0 可关闭模块，关闭后恢复游戏默认买甲逻辑。";
    }
}