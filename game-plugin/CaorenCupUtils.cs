// 文件名: CaorenCupUtils.cs
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text;

namespace CaorenCup;

public static class CaorenCupUtils
{
    public const string Tag = " [草人杯] ";

    // 统一发送消息到所有玩家
    public static void PrintToChatAll(string message)
    {
        Server.PrintToChatAll($" {ChatColors.Green}{Tag}{ChatColors.Default}{message}");
    }

    // 统一发送消息给指定玩家
    public static void PrintToChat(CCSPlayerController player, string message)
    {
        if (player.IsValid)
        {
            player.PrintToChat($" {ChatColors.Green}{Tag}{ChatColors.Default}{message}");
        }
    }

    // 统一目标解析器 (解析 0/1/2/3/ID/all/ct/t)
    // 返回: 目标玩家列表。如果列表为空，且 out description 为 "功能已关闭"，则说明用户输入了 0
    public static List<CCSPlayerController> GetTargetPlayers(string arg, out string description)
    {
        var targets = new List<CCSPlayerController>();
        arg = arg.ToLower().Trim();
        description = "未知目标";

        // 0: 关闭
        if (arg == "0")
        {
            description = "功能已关闭";
            return targets; // 空列表
        }

        var allPlayers = Utilities.GetPlayers();

        // 1 或 all: 所有玩家
        if (arg == "1" || arg == "all")
        {
            description = "所有玩家";
            return allPlayers.Where(p => p != null && p.IsValid && !p.IsBot).ToList();
        }

        // 2 或 ct: CT阵营
        if (arg == "2" || arg == "ct")
        {
            description = "CT方玩家";
            return allPlayers.Where(p => p != null && p.IsValid && !p.IsBot && p.TeamNum == (int)CsTeam.CounterTerrorist).ToList();
        }

        // 3 或 t: T阵营
        if (arg == "3" || arg == "t")
        {
            description = "T方玩家";
            return allPlayers.Where(p => p != null && p.IsValid && !p.IsBot && p.TeamNum == (int)CsTeam.Terrorist).ToList();
        }

        // 尝试解析特定 ID 或名字 (这里简单处理为 Slot 或 UserId 匹配，实际可视需求增强)
        // 简单起见，如果不是上述关键词，暂且认为是单人操作，尝试寻找匹配的名字
        var specificPlayer = allPlayers.FirstOrDefault(p => p.PlayerName.ToLower().Contains(arg));
        if (specificPlayer != null)
        {
            description = $"玩家 {specificPlayer.PlayerName}";
            targets.Add(specificPlayer);
            return targets;
        }

        // 如果都匹配不上，返回空
        description = "未找到目标";
        return targets;
    }

    // 格式化“更改”类消息
    // 例如：[草人杯] (绿色)成功更改 (默认)T方FOV至120
    public static string FormatChangeMessage(string action, string targetDesc, string value)
    {
        return $"{ChatColors.Green}{action} {ChatColors.Default}{targetDesc} {ChatColors.Green}{value}";
    }


    public static bool IsHpCapEnabled(CaorenCupPlugin plugin)
    {
        var cap = plugin.Config.HpCap;
        return cap.Enabled && cap.Max >= cap.Min;
    }

    public static int GetHpCapMin(CaorenCupPlugin plugin, int fallback = 1)
    {
        // /hpcap 开启后，全局下限直接接管各模块的扣血下限；未开启时才使用模块自己的 fallback。
        if (!IsHpCapEnabled(plugin)) return fallback;
        return plugin.Config.HpCap.Min;
    }

    public static int GetHpCapMax(CaorenCupPlugin plugin, int fallback)
    {
        // /hpcap 开启后，全局上限直接接管各模块的回血上限；未开启时才使用模块自己的 fallback。
        if (!IsHpCapEnabled(plugin)) return fallback;
        return plugin.Config.HpCap.Max;
    }

    public static int ClampModuleHealth(CaorenCupPlugin plugin, int currentHp, int desiredHp)
    {
        if (!IsHpCapEnabled(plugin)) return desiredHp;

        int min = plugin.Config.HpCap.Min;
        int max = plugin.Config.HpCap.Max;

        if (desiredHp > currentHp)
        {
            // 模块回血时，如果当前血量已经超过全局上限，不主动扣回，只阻止继续回血。
            if (currentHp >= max) return currentHp;
            return Math.Min(desiredHp, max);
        }

        if (desiredHp < currentHp)
        {
            // 模块扣血时，如果当前血量已经低于全局下限，不继续扣血。
            if (currentHp <= min) return currentHp;
            return Math.Max(desiredHp, min);
        }

        return desiredHp;
    }

    public static int ClampModuleSetHealth(CaorenCupPlugin plugin, int desiredHp)
    {
        if (!IsHpCapEnabled(plugin)) return desiredHp;
        int min = plugin.Config.HpCap.Min;
        int max = plugin.Config.HpCap.Max;
        return Math.Clamp(desiredHp, min, max);
    }

    private static void EnsureMaxHealthForModule(CaorenCupPlugin plugin, CCSPlayerPawn pawn, int finalHp)
    {
        if (!IsHpCapEnabled(plugin)) return;
        if (finalHp <= 0) return;

        // CS2/CounterStrikeSharp 某些环境下，Health 直接写到 100 以上会被 MaxHealth 限制或在下一帧回落。
        // 所以只要 /hpcap 开启，并且模块准备把血量设置到 MaxHealth 以上，就先抬高 MaxHealth。
        // 这里不主动降低 MaxHealth，避免和 OMA、复活等模块互相覆盖。
        int targetMaxHealth = Math.Max(finalHp, plugin.Config.HpCap.Max);
        if (pawn.MaxHealth < targetMaxHealth)
        {
            pawn.MaxHealth = targetMaxHealth;
            try { Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth"); } catch { }
        }
    }

    public static void SetModuleHealth(CaorenCupPlugin plugin, CCSPlayerPawn pawn, int finalHp)
    {
        if (pawn == null || !pawn.IsValid) return;
        EnsureMaxHealthForModule(plugin, pawn, finalHp);
        pawn.Health = finalHp;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
    }

    public static bool ApplyModuleHealth(CaorenCupPlugin plugin, CCSPlayerPawn pawn, int desiredHp)
    {
        if (pawn == null || !pawn.IsValid) return false;

        int currentHp = pawn.Health;
        int finalHp = ClampModuleHealth(plugin, currentHp, desiredHp);
        if (finalHp == currentHp) return false;

        SetModuleHealth(plugin, pawn, finalHp);
        return true;
    }

    public static bool ApplyModuleSetHealth(CaorenCupPlugin plugin, CCSPlayerPawn pawn, int desiredHp)
    {
        if (pawn == null || !pawn.IsValid) return false;

        int finalHp = ClampModuleSetHealth(plugin, desiredHp);
        if (finalHp == pawn.Health)
        {
            EnsureMaxHealthForModule(plugin, pawn, finalHp);
            return false;
        }

        SetModuleHealth(plugin, pawn, finalHp);
        return true;
    }

    public static float ClampModuleDamageByHpCap(CaorenCupPlugin plugin, CCSPlayerPawn pawn, float damage)
    {
        if (!IsHpCapEnabled(plugin) || damage <= 0) return damage;

        int min = plugin.Config.HpCap.Min;
        float maxDamage = pawn.Health - min;
        if (maxDamage <= 0) return 0;
        return Math.Min(damage, maxDamage);
    }

}