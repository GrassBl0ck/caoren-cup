using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using CaorenCup.Contracts;

namespace CaorenCup.Core;

/// <summary>损坏或不可读的文件只读保护；内存变更可生效，禁止用默认值覆盖原文件。</summary>
public sealed class PlayerPreferenceStore : ICaorenCupPreferences
{
    private sealed record LegacyImportMarker(string Fingerprint, int Records);
    private readonly string _path;
    private readonly Dictionary<string, CaorenCupPreference> _values = new();
    public string? ReadError { get; }
    public PlayerPreferenceStore(string path)
    {
        _path = Path.GetFullPath(path);
        try
        {
            _values = JsonSerializer.Deserialize<Dictionary<string, CaorenCupPreference>>(File.ReadAllText(_path))
                ?? throw new JsonException("偏好文件不是有效的对象。");
            if (_values.Any(pair => pair.Value == null)) throw new JsonException("偏好记录包含 null。");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _values = new(); ReadError = ex.Message;
            Console.WriteLine($"[CaorenCupCore] 偏好读取失败，原文件进入只读保护：{ex.Message}");
        }
    }
    public CaorenCupPreference Get(string steamId) => (_values.GetValueOrDefault(steamId) ?? new()).Clamp();
    public void Set(string steamId, int? transparency = null, int? volume = null)
    {
        var old = Get(steamId);
        _values[steamId] = (old with { Transparency = transparency ?? old.Transparency, Volume = volume ?? old.Volume }).Clamp();
        Save(_values);
    }
    public void ImportLegacy(IReadOnlyDictionary<string, CaorenCupPreference> values)
    {
        if (ReadError != null) throw new IOException("偏好读取失败，禁止迁移覆盖原文件：" + ReadError);
        if (values.Count == 0) return;
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
            { SteamId = pair.Key, pair.Value.Clamp().Transparency, pair.Value.Clamp().Volume }))));
        var markerPath = _path + ".legacy-menu-import.json";
        LegacyImportMarker? marker = null;
        try
        {
            marker = JsonSerializer.Deserialize<LegacyImportMarker>(File.ReadAllText(markerPath))
                ?? throw new JsonException("迁移标记为空。");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        catch (JsonException ex) { throw new IOException("迁移标记损坏，停止迁移并保留原文件。", ex); }
        if (marker != null)
        {
            if (marker.Fingerprint != fingerprint || marker.Records != values.Count)
                throw new InvalidDataException("已迁移的旧菜单偏好后来发生变化，停止再次导入，等待人工检查。");
            if (!File.Exists(_path) || values.Keys.Any(key => !_values.ContainsKey(key)))
                throw new InvalidDataException("迁移标记存在，但 Core 偏好缺失，停止覆盖并保留旧数据。");
            return; // 已完成迁移，Core 中后续由玩家修改的值是当前值，不再与旧副本比较。
        }
        var conflicts = values.Where(pair => _values.TryGetValue(pair.Key, out var current)
            && (current.Clamp().Transparency != pair.Value.Clamp().Transparency || current.Clamp().Volume != pair.Value.Clamp().Volume))
            .Select(pair => pair.Key).ToArray();
        if (conflicts.Length != 0) throw new InvalidDataException("偏好迁移冲突，未导入任何记录：" + string.Join(", ", conflicts));
        var merged = new Dictionary<string, CaorenCupPreference>(_values);
        foreach (var pair in values) if (!merged.ContainsKey(pair.Key)) merged[pair.Key] = pair.Value.Clamp();
        if (merged.Count != _values.Count)
        {
            Save(merged); // 先保存成功再发布迁移结果，失败不污染已有缓存。
            foreach (var pair in merged) _values[pair.Key] = pair.Value;
        }
        WriteAtomic(markerPath, JsonSerializer.Serialize(new LegacyImportMarker(fingerprint, values.Count)));
    }
    private void Save(IReadOnlyDictionary<string, CaorenCupPreference> values)
    {
        if (ReadError != null) throw new IOException("当前设置仅在内存生效；禁止覆盖不可读的偏好文件：" + ReadError);
        WriteAtomic(_path, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Copy(path, path + ".bak-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff"));
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}
