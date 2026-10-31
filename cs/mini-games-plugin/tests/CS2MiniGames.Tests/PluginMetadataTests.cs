using System.Reflection;
using System.Runtime.CompilerServices;

namespace CS2MiniGames.Tests;

public sealed class PluginMetadataTests
{
    [Fact]
    public void PluginExposesApprovedMetadataAndCommands()
    {
        var pluginType = typeof(CS2MiniGamesPlugin);
        var plugin = (CS2MiniGamesPlugin)RuntimeHelpers.GetUninitializedObject(
            pluginType);
        var moduleName = pluginType
            .GetProperty(nameof(CS2MiniGamesPlugin.ModuleName))!
            .GetValue(plugin);
        var moduleVersion = pluginType
            .GetProperty(nameof(CS2MiniGamesPlugin.ModuleVersion))!
            .GetValue(plugin);

        Assert.Equal("CS2 Mini Games", moduleName);
        Assert.Equal("0.1.0", moduleVersion);

        var commandNames = pluginType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(method => method.CustomAttributes)
            .Where(attribute =>
                attribute.AttributeType.FullName ==
                "CounterStrikeSharp.API.Core.Attributes.Registration.ConsoleCommandAttribute")
            .Select(attribute => (string)attribute.ConstructorArguments[0].Value!)
            .ToArray();

        var expectedCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            "css_tetris",
            "css_toptetris",
            "css_tetrishelp",
            "css_minigames"
        };

        Assert.Equal(4, commandNames.Length);
        Assert.True(commandNames.ToHashSet(StringComparer.Ordinal).SetEquals(expectedCommands));
    }
}
