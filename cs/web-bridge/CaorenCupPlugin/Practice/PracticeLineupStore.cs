using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;

namespace CaorenCupPlugin.Practice;

public sealed record PracticeLineup(
    string Id,
    string Name,
    string? Description,
    string Grenade,
    float X,
    float Y,
    float Z,
    float Pitch,
    float Yaw);

public sealed record PracticeLineupFile(
    int SchemaVersion,
    string SteamId,
    string MapName,
    IReadOnlyList<PracticeLineup> Lineups);

public sealed class PracticeLineupStore
{
    private static readonly Regex SteamIdPattern = new("^7656119[0-9]{10}$", RegexOptions.CultureInvariant);
    private static readonly Regex MapPattern = new("^de_[a-zA-Z0-9_]+$", RegexOptions.CultureInvariant);
    private static readonly Regex LineupIdPattern = new("^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly string _root;

    public PracticeLineupStore(string root) => _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));

    public async Task<IReadOnlyList<PracticeLineup>> ListAsync(string steamId, string mapName)
    {
        var path = FilePath(steamId, mapName);
        var primary = await ReadAsync(path, steamId, mapName);
        if (primary is not null) return primary.Lineups;
        var previous = await ReadAsync(PreviousPath(path), steamId, mapName);
        return previous?.Lineups ?? [];
    }

    public async Task SaveAsync(string steamId, string mapName, PracticeLineup lineup)
    {
        ValidateLineup(lineup);
        var path = FilePath(steamId, mapName);
        var lineups = (await ListAsync(steamId, mapName)).ToList();
        var index = lineups.FindIndex(item => item.Id == lineup.Id);
        if (index >= 0) lineups[index] = lineup;
        else lineups.Add(lineup);
        await WriteAsync(path, new PracticeLineupFile(1, steamId, mapName, lineups));
    }

    public async Task<bool> DeleteAsync(string steamId, string mapName, string lineupId)
    {
        if (!LineupIdPattern.IsMatch(lineupId)) throw new ArgumentException("Lineup ID is invalid.", nameof(lineupId));
        var path = FilePath(steamId, mapName);
        var lineups = (await ListAsync(steamId, mapName)).ToList();
        var removed = lineups.RemoveAll(item => item.Id == lineupId) > 0;
        if (removed) await WriteAsync(path, new PracticeLineupFile(1, steamId, mapName, lineups));
        return removed;
    }

    private string FilePath(string steamId, string mapName)
    {
        if (!SteamIdPattern.IsMatch(steamId)) throw new ArgumentException("SteamID64 is invalid.", nameof(steamId));
        if (!MapPattern.IsMatch(mapName)) throw new ArgumentException("Map name is invalid.", nameof(mapName));
        return Path.Combine(_root, steamId, mapName + ".json");
    }

    private static string PreviousPath(string path) => path[..^5] + ".previous.json";

    private static async Task<PracticeLineupFile?> ReadAsync(string path, string steamId, string mapName)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<PracticeLineupFile>(await File.ReadAllTextAsync(path, Encoding.UTF8), JsonOptions);
            return data is { SchemaVersion: 1 } && data.SteamId == steamId && data.MapName == mapName ? data : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteAsync(string path, PracticeLineupFile data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(data, JsonOptions), new UTF8Encoding(false));
            if (File.Exists(path)) File.Copy(path, PreviousPath(path), overwrite: true);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void ValidateLineup(PracticeLineup lineup)
    {
        if (!LineupIdPattern.IsMatch(lineup.Id)
            || string.IsNullOrWhiteSpace(lineup.Name)
            || lineup.Name.Length > 64
            || lineup.Description?.Length > 240
            || !Regex.IsMatch(lineup.Grenade, "^[a-z_]{2,32}$"))
            throw new ArgumentException("Practice lineup is invalid.", nameof(lineup));
    }
}
