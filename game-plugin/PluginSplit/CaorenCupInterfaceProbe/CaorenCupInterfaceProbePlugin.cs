using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace CaorenCup.InterfaceProbe;

/// <summary>仅用于本地验证独立插件之间的共享接口，不进入发布包。</summary>
public sealed class CaorenCupInterfaceProbePlugin : BasePlugin
{
    public override string ModuleName => "CaorenCup Interface Probe (local only)";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "Graslock + AI";

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var core = CaorenCupCoreAccess.TryGet();
        if (core is null)
        {
            Logger.LogError("CaorenCup Core capability unavailable after plugin loading.");
            return;
        }

        Logger.LogInformation("CaorenCup Core capability resolved; contract version {Version}.",
            core.ContractVersion);
    }
}
