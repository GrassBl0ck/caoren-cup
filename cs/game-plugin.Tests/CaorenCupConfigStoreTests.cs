using System.Text;
using System.Text.Json;
using CaorenCup.Core;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class CaorenCupConfigStoreTests
{
    [Fact]
    public void ModuleFilesOverrideLegacySeedWhileMissingModulesKeepLegacyValues()
    {
        using var fixture = new ConfigFixture();
        File.WriteAllText(
            Path.Combine(fixture.Path, "CaorenCup.json"),
            """{"PlaySound":{"Enabled":false},"FireHeal":{"Enabled":true}}""",
            Encoding.UTF8);
        string modules = Path.Combine(fixture.Path, "module-configs");
        Directory.CreateDirectory(modules);
        File.WriteAllText(
            Path.Combine(modules, "PlaySound.json"),
            """{"Enabled":true,"DefaultPrefix":"custom/","Aliases":{"test":"music/test"}}""",
            Encoding.UTF8);

        var store = new CaorenCupConfigStore(fixture.Path);
        store.Load();

        Assert.True(store.Config.PlaySound.Enabled);
        Assert.Equal("custom/", store.Config.PlaySound.DefaultPrefix);
        Assert.True(store.Config.FireHeal.Enabled);
        Assert.True(File.Exists(Path.Combine(modules, "FireHeal.json")));

        store.Config.PlaySound.Enabled = false;
        store.Save();
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(modules, "PlaySound.json")));
        Assert.False(saved.RootElement.GetProperty("Enabled").GetBoolean());
    }

    private sealed class ConfigFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            AppContext.BaseDirectory, "caorencup-config-fixture-" + Guid.NewGuid().ToString("N"));

        public ConfigFixture() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            string basePath = System.IO.Path.GetFullPath(AppContext.BaseDirectory);
            string target = System.IO.Path.GetFullPath(Path);
            if (!target.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Path.GetFileName(target).StartsWith("caorencup-config-fixture-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe config fixture cleanup path.");
            Directory.Delete(target, recursive: true);
        }
    }
}
