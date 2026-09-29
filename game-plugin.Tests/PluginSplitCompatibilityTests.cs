using System.Reflection;
using CaorenCup.Contracts;
using CaorenCup.Features;
using CounterStrikeSharp.API.Core.Hosting;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using McMaster.NETCore.Plugins;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class PluginSplitCompatibilityTests
{
    [Fact]
    public void CoreLoadedInSeparateAssemblyContextSharesTheContractType()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "CaorenCupCore.dll");
        Assert.True(File.Exists(path));
        using var loader = PluginLoader.CreateFromAssemblyFile(
            path,
            [typeof(IPlugin), typeof(ICaorenCupCoreApi), typeof(CaorenCupCoreAccess),
                typeof(PluginCapability<>)],
            options => options.PreferSharedTypes = true);
        var assembly = loader.LoadDefaultAssembly();
        var pluginType = assembly.GetType("CaorenCup.Core.CaorenCupCorePlugin", throwOnError: true)!;
        Assert.True(typeof(ICaorenCupCoreApi).IsAssignableFrom(pluginType));

        // BasePlugin 的构造函数需要游戏宿主；这里只检验跨加载上下文的类型身份。
        var core = (ICaorenCupCoreApi)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(pluginType);
        try
        {
            CaorenCupCoreAccess.Publish(core);
            Assert.Same(core, CaorenCupCoreAccess.TryGet());
        }
        finally
        {
            CaorenCupCoreAccess.Withdraw(core);
        }
    }

    [Fact]
    public void CapabilityCanBeAbsentPublishedWithdrawnAndReplaced()
    {
        Assert.Null(CaorenCupCoreAccess.TryGet());

        var first = new FakeCore();
        CaorenCupCoreAccess.Publish(first);
        Assert.Same(first, CaorenCupCoreAccess.TryGet());

        CaorenCupCoreAccess.Withdraw(first);
        Assert.Null(CaorenCupCoreAccess.TryGet());

        var next = new FakeCore();
        CaorenCupCoreAccess.Publish(next);
        Assert.Same(next, CaorenCupCoreAccess.TryGet());
        CaorenCupCoreAccess.Withdraw(next);
        Assert.Null(CaorenCupCoreAccess.TryGet());
    }

    [Fact]
    public void CounterStrikeSharpDiscoversOnlyMatchingDllsAndStopsBelowPlugins()
    {
        using var fixture = new PluginTreeFixture();
        fixture.Create("CaorenCup/CaorenCupCore/CaorenCupCore.dll");
        fixture.Create("CaorenCup/CaorenCupQOLs/CaorenCupQOL_Alias/CaorenCupQOL_Alias.dll");
        fixture.Create("CaorenCup/CaorenCupGamemodes/CaorenCupGamemode_RUSH/CaorenCupGamemode_RUSH.dll");
        fixture.Create("CaorenCup/Loose.dll");
        fixture.Create("Parent/Parent.dll");
        fixture.Create("Parent/Hidden/Hidden.dll");

        var manager = new PluginManager(new FakeHost(fixture.Path), null!, null!, null!, null!);
        var method = typeof(PluginManager).GetMethod(
            "GetPluginsAssemblyPaths", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var discovered = ((string[])method.Invoke(manager, null)!)
            .Select(path => Path.GetRelativePath(fixture.Path, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(4, discovered.Count);
        Assert.Contains("CaorenCup/CaorenCupCore/CaorenCupCore.dll", discovered);
        Assert.Contains("CaorenCup/CaorenCupQOLs/CaorenCupQOL_Alias/CaorenCupQOL_Alias.dll", discovered);
        Assert.Contains("CaorenCup/CaorenCupGamemodes/CaorenCupGamemode_RUSH/CaorenCupGamemode_RUSH.dll", discovered);
        Assert.Contains("Parent/Parent.dll", discovered);
        Assert.DoesNotContain("CaorenCup/Loose.dll", discovered);
        Assert.DoesNotContain("Parent/Hidden/Hidden.dll", discovered);
    }

    private sealed class FakeCore : ICaorenCupCoreApi
    {
        public int ContractVersion => CaorenCupCoreAccess.CurrentContractVersion;
        public CaorenCupConfig Config { get; } = new();
        public ManagedCvarScope ManagedCvars { get; } = new();
        public string LegacyConfigPath => string.Empty;
        public string ModulesDirectory => string.Empty;
        public void SaveConfig() { }
    }

    private sealed class FakeHost(string pluginPath) : IScriptHostConfiguration
    {
        public string RootPath => pluginPath;
        public string PluginPath => pluginPath;
        public string SharedPath => pluginPath;
        public string ConfigsPath => pluginPath;
        public string GameDataPath => pluginPath;
        public string LanguagePath => pluginPath;
    }

    private sealed class PluginTreeFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            AppContext.BaseDirectory, "caorencup-plugin-fixture-" + Guid.NewGuid().ToString("N"));

        public PluginTreeFixture() => Directory.CreateDirectory(Path);

        public void Create(string relative)
        {
            var file = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, []);
        }

        public void Dispose()
        {
            var basePath = System.IO.Path.GetFullPath(AppContext.BaseDirectory);
            var target = System.IO.Path.GetFullPath(Path);
            if (!target.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Path.GetFileName(target).StartsWith("caorencup-plugin-fixture-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe plugin fixture cleanup path.");
            Directory.Delete(target, recursive: true);
        }
    }
}
