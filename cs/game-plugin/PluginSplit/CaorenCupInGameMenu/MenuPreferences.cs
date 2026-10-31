using System.Text.Json;
using CaorenCup.Contracts;

namespace CaorenCup.Features.InGameMenu;

public sealed record MenuPreference(int Transparency = 10, int Volume = 100);

/// <summary>菜单偏好适配器。正式运行由 Core 保存；旧数据保留并受迁移冲突保护。</summary>
public sealed class MenuPreferences
{
    private readonly Dictionary<string, MenuPreference> _values;
    private readonly string? _path;
    private readonly bool _useCore;
    private string? _readError;
    private ICaorenCupPreferences? _importedInto;

    public MenuPreferences(string? path = null, bool useCore = false)
    {
        _path = path;
        _useCore = useCore;
        _values = path != null ? Read(path) : new();
    }
    private Dictionary<string, MenuPreference> Read(string path)
    {
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, MenuPreference>>(File.ReadAllText(path))
                ?? throw new JsonException("偏好文件不是有效对象。");
            if (values.Any(pair => pair.Value == null)) throw new JsonException("偏好记录包含 null。");
            return values;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _readError = ex.Message;
            // 保留坏文件，避免个人偏好损坏导致整个菜单插件无法加载。
            Console.WriteLine("[CaorenCup InGameMenu] 个人偏好读取失败，本次使用默认值；原文件未删除：" + ex.Message);
            return new();
        }
    }
    public MenuPreference Get(string steamId)
    {
        if (_useCore && CaorenCupPreferencesAccess.TryGet() is { } core)
        {
            var value = core.Get(steamId);
            return new MenuPreference(value.Transparency, value.Volume);
        }
        var stored = _values.GetValueOrDefault(steamId) ?? new MenuPreference();
        return new MenuPreference(Math.Clamp(stored.Transparency, 0, 100), Math.Clamp(stored.Volume, 0, 100));
    }
    public void Set(string steamId, int? transparency = null, int? volume = null)
    {
        if (_useCore)
        {
            var core = CaorenCupPreferencesAccess.TryGet() ?? throw new IOException("Core 个人偏好服务不可用。");
            EnsureImported(core);
            core.Set(steamId, transparency, volume);
            return;
        }
        var old = Get(steamId);
        _values[steamId] = new MenuPreference(Math.Clamp(transparency ?? old.Transparency, 0, 100), Math.Clamp(volume ?? old.Volume, 0, 100));
        if (_path == null) return;
        if (_readError != null) throw new IOException("禁止覆盖不可读的偏好文件：" + _readError);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // 每次覆盖前保留前一份，运行数据不进入仓库。
        if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }
    public void EnsureImported(ICaorenCupPreferences core)
    {
        if (ReferenceEquals(_importedInto, core)) return;
        if (_readError != null) throw new IOException("旧菜单偏好不可读，迁移已停止：" + _readError);
        if (_path != null && File.Exists(_path))
            File.Copy(_path, _path + ".bak-migration-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff"));
        core.ImportLegacy(_values.ToDictionary(pair => pair.Key, pair => new CaorenCupPreference(pair.Value.Transparency, pair.Value.Volume)));
        _importedInto = core;
    }
}
