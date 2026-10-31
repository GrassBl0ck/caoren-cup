using System.Text.Json;

namespace CaorenCup.Gamemodes.Ability;

/// <summary>管理员手动分配的职业，按 SteamID 保存，重启后仍可使用。</summary>
public sealed class AbilitySeatAssignmentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<string, string> _assignments = new(StringComparer.Ordinal);

    public AbilitySeatAssignmentStore(string path) => _path = path;

    public IReadOnlyDictionary<string, string> Snapshot => _assignments;

    public void Load()
    {
        _assignments.Clear();
        if (!File.Exists(_path)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path), JsonOptions);
            if (loaded is null) return;
            foreach (var pair in loaded)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;
                _assignments[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[草人杯] 异能职业分配文件读取失败：{ex.Message}");
        }
    }

    public bool TryGet(string steamId, out string abilityId) => _assignments.TryGetValue(steamId, out abilityId!);

    public void Set(string steamId, string abilityId)
    {
        _assignments[steamId] = abilityId;
        Save();
    }

    public bool Remove(string steamId)
    {
        var removed = _assignments.Remove(steamId);
        if (removed) Save();
        return removed;
    }

    public void Clear()
    {
        _assignments.Clear();
        Save();
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(_assignments, JsonOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[草人杯] 异能职业分配文件写入失败：{ex.Message}");
        }
    }
}
