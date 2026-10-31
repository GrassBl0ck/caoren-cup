namespace CaorenCup.WeaponPaints.Core;

// Controller/Pawn 使用包含序号的实体句柄，避免地址或槽位复用后误认旧玩家。
public readonly record struct WeaponRefreshIdentity(
    ulong SteamId, uint Controller, uint Pawn, int Team, int Generation);

public static class WeaponRefreshWorkflow
{
    public static bool CanApply(WeaponRefreshIdentity scheduled, WeaponRefreshIdentity current) =>
        scheduled.SteamId != 0 && scheduled.Controller is not (0 or uint.MaxValue) &&
        scheduled.Pawn is not (0 or uint.MaxValue) && scheduled.Team is 2 or 3 && scheduled == current;

    // 生产刷新和测试共用入口。只遍历当前装备，不创建、删除或选择武器。
    public static void Apply<T>(IEnumerable<T> weapons, Action<T> applySkin)
    {
        foreach (var weapon in weapons.ToArray()) applySkin(weapon);
    }
}