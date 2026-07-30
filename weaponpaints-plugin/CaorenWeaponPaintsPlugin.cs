using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;

namespace CaorenCup.WeaponPaints;

[MinimumApiVersion(338)]
public partial class CaorenWeaponPaintsPlugin : BasePlugin, IPluginConfig<CaorenWeaponPaintsConfig>
{
    public const string ReleaseVersion = "1.9.0";

    public override string ModuleName => "CaorenWeaponPaints";
    public override string ModuleAuthor => "Nereziel, daffyy & Caoren Cup contributors";
    public override string ModuleDescription => "草人杯独立枪皮与饰品菜单插件";
    public override string ModuleVersion => ReleaseVersion;

    public CaorenWeaponPaintsConfig Config { get; set; } = new();

    public void OnConfigParsed(CaorenWeaponPaintsConfig config)
    {
        Config = config;
        SetInstance(this, config);
    }

    public override void Load(bool hotReload)
    {
        SetInstance(this, Config);
        StartRuntime(hotReload);
    }

    public override void Unload(bool hotReload)
    {
        StopRuntime();
        ClearRuntimeState();
    }
}
