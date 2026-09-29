using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;
using CaorenCup.Features;

namespace CaorenCup.Core;

/// <summary>拆分第一阶段的最小核心插件；玩法及旧命令仍在原插件中。</summary>
public sealed partial class CaorenCupCorePlugin : BasePlugin, ICaorenCupCoreApi
{
    public override string ModuleName => "CaorenCup Core";
    public override string ModuleVersion => "1.10.0";
    public override string ModuleAuthor => "Graslock + AI";
    public int ContractVersion => CaorenCupCoreAccess.CurrentContractVersion;
    private CaorenCupConfigStore _configStore = null!;
    public CaorenCupConfig Config => _configStore.Config;
    public ManagedCvarScope ManagedCvars { get; private set; } = new();
    public string LegacyConfigPath => _configStore.LegacyConfigPath;
    public string ModulesDirectory => _configStore.ModulesDirectory;
    private PlayerPreferenceStore _preferences = null!;

    public override void Load(bool hotReload)
    {
        string groupDirectory = Directory.GetParent(ModuleDirectory)?.FullName ?? ModuleDirectory;
        _configStore = new CaorenCupConfigStore(groupDirectory);
        CaorenCupRuntimeState? retained = hotReload
            ? CaorenCupCoreAccess.GetRetainedRuntimeState()
            : null;
        if (retained is null)
        {
            try
            {
                _configStore.Load();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CaorenCupCore] Fatal config load error; using defaults: {ex.Message}");
                _configStore.UseDefaults();
            }
            CaorenCupCoreAccess.RetainRuntimeState(
                new CaorenCupRuntimeState(Config, ManagedCvars));
        }
        else
        {
            _configStore.AttachExisting(retained.Config);
            ManagedCvars = retained.ManagedCvars;
        }
        CaorenCupCoreAccess.Publish(this);
        _preferences = new PlayerPreferenceStore(Path.Combine(ModuleDirectory, "data", "player-preferences.json"));
        CaorenCupPreferencesAccess.Publish(_preferences);
        RegisterCoreCommands();
    }

    public override void Unload(bool hotReload)
    {
        CaorenCupPreferencesAccess.Withdraw(_preferences);
        CaorenCupCoreAccess.Withdraw(this);
    }

    public void SaveConfig()
    {
        try
        {
            _configStore.Save();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CaorenCupCore] Failed to save config: {ex.Message}");
        }
    }
}
