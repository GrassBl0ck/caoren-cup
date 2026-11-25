using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Features.InGameMenu;

internal sealed record ShortcutItem(string Name, CsItem Id)
{
    internal string EntityName { get; } = EnumUtils.GetEnumMemberAttributeValue(Id)
        ?? throw new ArgumentException($"No entity name registered for {Id}.");
}
internal sealed record ShortcutItemGroup(string Name, string Slot, ShortcutItem[] Items);

internal static class ShortcutItems
{
    internal static readonly ShortcutItemGroup[] Groups =
    [
        new("步枪", "primary",
        [
            new("AK-47", CsItem.AK47),
            new("M4A1-S", CsItem.M4A1S),
            new("M4A4", CsItem.M4A1),
            new("加利尔 AR", CsItem.GalilAR),
            new("法玛斯", CsItem.Famas),
            new("AUG", CsItem.AUG),
            new("SG 553", CsItem.SG556)
        ]),
        new("狙击枪", "primary",
        [
            new("AWP", CsItem.AWP),
            new("SSG 08", CsItem.SSG08),
            new("SCAR-20", CsItem.SCAR20),
            new("G3SG1", CsItem.G3SG1)
        ]),
        new("冲锋枪", "primary",
        [
            new("MAC-10", CsItem.Mac10),
            new("MP9", CsItem.MP9),
            new("MP7", CsItem.MP7),
            new("MP5-SD", CsItem.MP5SD),
            new("UMP-45", CsItem.UMP45),
            new("P90", CsItem.P90),
            new("PP-野牛", CsItem.Bizon)
        ]),
        new("霰弹枪与机枪", "primary",
        [
            new("Nova", CsItem.Nova),
            new("XM1014", CsItem.XM1014),
            new("MAG-7", CsItem.MAG7),
            new("截短霰弹枪", CsItem.SawedOff),
            new("内格夫", CsItem.Negev),
            new("M249", CsItem.M249)
        ]),
        new("手枪", "secondary",
        [
            new("沙漠之鹰", CsItem.Deagle),
            new("格洛克 18", CsItem.Glock),
            new("USP-S", CsItem.USPS),
            new("P2000", CsItem.HKP2000),
            new("P250", CsItem.P250),
            new("双持贝瑞塔", CsItem.Elite),
            new("FN57", CsItem.FiveSeven),
            new("Tec-9", CsItem.Tec9),
            new("CZ75", CsItem.CZ),
            new("R8 左轮", CsItem.Revolver)
        ]),
        new("投掷物与其他", "",
        [
            new("高爆手雷", CsItem.HighExplosive),
            new("闪光弹", CsItem.Flashbang),
            new("烟雾弹", CsItem.Smoke),
            new("燃烧瓶", CsItem.Molotov),
            new("燃烧弹", CsItem.Incendiary),
            new("诱饵弹", CsItem.Decoy),
            new("治疗针", CsItem.Healthshot),
            new("电击枪", CsItem.Taser)
        ])
    ];
}
