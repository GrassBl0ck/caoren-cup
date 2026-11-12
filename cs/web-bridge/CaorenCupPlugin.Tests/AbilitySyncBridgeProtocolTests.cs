using System.Text.Json;
using CaorenCupPlugin;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class AbilitySyncBridgeProtocolTests
{
    [Fact]
    public void Builds_fixed_begin_seat_commit_commands_without_accepting_server_text()
    {
        var command = new PluginCommand
        {
            Id = "queue-1",
            Type = AbilitySyncProtocol.WebCommandType,
            Payload = JsonSerializer.SerializeToElement(new
            {
                protocolVersion = 1,
                syncId = "sync-1",
                matchId = "match-1",
                revision = 1,
                catalogVersion = "ability-catalog-v1",
                abilityModeEnabled = true,
                bannedAbilityIds = new[] { "witch" },
                contentDigest = new string('a', 64),
                seats = new[]
                {
                    new { playerId = "A1", steamId = "76561198000000001", rosterTeam = "A", initialSide = "CT", abilityId = "medic" },
                    new { playerId = "B1", steamId = "76561198000000002", rosterTeam = "B", initialSide = "T", abilityId = "tank" },
                },
            }),
        };

        var commands = AbilitySyncProtocol.BuildInternalCommands(command);

        Assert.Equal(4, commands.Count);
        Assert.Equal("css_ability_sync_begin", commands[0].Name);
        Assert.Equal("css_ability_sync_seat", commands[1].Name);
        Assert.Equal("css_ability_sync_seat", commands[2].Name);
        Assert.Equal("css_ability_sync_commit", commands[3].Name);
        Assert.All(commands, item => Assert.DoesNotContain("EXECUTE_SERVER_COMMAND", item.Name));
        Assert.All(commands, item => Assert.DoesNotContain("\n", item.Argument));
    }

    [Fact]
    public void Rejects_non_structured_or_incomplete_web_commands()
    {
        var command = new PluginCommand
        {
            Id = "queue-2",
            Type = AbilitySyncProtocol.WebCommandType,
            Payload = JsonSerializer.SerializeToElement(new { syncId = "sync-1" }),
        };

        Assert.Throws<AbilitySyncProtocolException>(() => AbilitySyncProtocol.BuildInternalCommands(command));
    }
}
