using CaorenCup.Contracts;
using CaorenCup.Features;
using CounterStrikeSharp.API.Core;

namespace CaorenCup;

/// <summary>草人杯特色玩法与通用规则的单个插件宿主。</summary>
public sealed class CaorenCupPlugin : BasePlugin
{
    public override string ModuleName => "CaorenCup Fun Commands";
    public override string ModuleVersion => "1.10.0";
    public override string ModuleAuthor => "Graslock + AI";

    private ICaorenCupCoreApi Core => CaorenCupCoreAccess.TryGet()
        ?? throw new InvalidOperationException("CaorenCupCore is unavailable.");
    private readonly List<ICaorenFeature> _features = new();

    public CaorenCupConfig Config => Core.Config;
    public ManagedCvarScope ManagedCvars => Core.ManagedCvars;
    public string LegacyConfigPath => Core.LegacyConfigPath;
    public string ConfigModulesDirectory => Core.ModulesDirectory;

    public void SaveConfig() => Core.SaveConfig();
    public void MeasurePerformance(string callbackName, Action callback) => callback();
    public T MeasurePerformance<T>(string callbackName, Func<T> callback) => callback();

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _ = Core;

        ICaorenFeature[] features =
        {
            new BombQuizFeature(),
            new FireHealFeature(),
            new FOVFeature(),
            new SkillPointsFeature(),
            new KillHealFeature(),
            new BleedFeature(),
            new SmokeFeature(),
            new MoneyFeature(),
            new DamageFeature(),
            new FriendlyFireFeature(),
            new TaggingControlFeature(),
            new DoubleJumpFeature(),
            new EspFeature(),
            new OneHpFeature(),
            new IncDmgFeature(),
            new AmmoFeature(),
            new MagicFeature(),
            new KbFeature(),
            new BladeAuraFeature(),
            new EcoGuessFeature(),
            new ArmorFeature(),
            new LhImmFeature(),
            new WeaponSpeedFeature(),
            new AccuracyFeature(),
            new LoadoutFeature(),
            new ModifierFeature(),
            new MovementRulesFeature(),
            new PresetFeature(),
        };

        foreach (var feature in features)
        {
            feature.OnConfigParsed(Config);
            feature.Init(this);
            _features.Add(feature);

            bool skipReset = feature is SkillPointsFeature;
            CaorenCupModuleRegistry.Register(new CaorenCupModuleDescriptor(
                this,
                feature.GetType().Name,
                feature.FeatureName,
                feature.GetType().Name,
                feature.GetHelpEntry,
                feature.GetStatusInfo,
                feature.GetFeatureDescription,
                feature.GetPublicConfigInfo,
                skipReset ? null : () => feature.SetEnabled(false)));
        }

        Console.WriteLine($"[CaorenCup] FunCommands loaded. Config seed: {LegacyConfigPath}");
    }

    public override void Unload(bool hotReload)
    {
        CaorenCupModuleRegistry.Unregister(this);
        foreach (var feature in _features)
            feature.OnUnload();
        _features.Clear();
    }
}
