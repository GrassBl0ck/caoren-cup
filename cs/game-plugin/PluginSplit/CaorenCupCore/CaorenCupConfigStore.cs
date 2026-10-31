using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaorenCup.Core;

/// <summary>保留旧 CaorenCup.json 种子和 module-configs 覆盖顺序。</summary>
public sealed class CaorenCupConfigStore(string groupDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public CaorenCupConfig Config { get; private set; } = new();
    public string LegacyConfigPath => Path.Combine(groupDirectory, "CaorenCup.json");
    public string ModulesDirectory => Path.Combine(groupDirectory, "module-configs");

    public void AttachExisting(CaorenCupConfig config) =>
        Config = config ?? throw new ArgumentNullException(nameof(config));

    public void UseDefaults() => Config = new CaorenCupConfig();

    public void Load()
    {
        Config = new CaorenCupConfig();
        if (File.Exists(LegacyConfigPath))
        {
            try
            {
                Config = JsonSerializer.Deserialize<CaorenCupConfig>(
                    File.ReadAllText(LegacyConfigPath, Encoding.UTF8), JsonOptions) ?? Config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CaorenCupCore] Legacy config read failed: {ex.Message}");
            }
        }

        Directory.CreateDirectory(ModulesDirectory);
        foreach (var item in ConfigItems())
        {
            if (!File.Exists(item.Path))
            {
                WriteItem(item.Property, item.Path);
                continue;
            }

            try
            {
                object? value = JsonSerializer.Deserialize(
                    File.ReadAllText(item.Path, Encoding.UTF8), item.Property.PropertyType, JsonOptions);
                if (value is not null) item.Property.SetValue(Config, value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CaorenCupCore] Module config read failed: {item.Path} | {ex.Message}");
            }
        }
        EnsureConfigObjects();
    }

    public void Save()
    {
        Directory.CreateDirectory(ModulesDirectory);
        EnsureConfigObjects();
        foreach (var item in ConfigItems())
            WriteItem(item.Property, item.Path);
    }

    private void EnsureConfigObjects()
    {
        foreach (var item in ConfigItems())
        {
            if (item.Property.GetValue(Config) is null)
                item.Property.SetValue(Config, Activator.CreateInstance(item.Property.PropertyType));
        }
    }

    private void WriteItem(PropertyInfo property, string path)
    {
        object? value = property.GetValue(Config);
        if (value is null)
        {
            value = Activator.CreateInstance(property.PropertyType);
            property.SetValue(Config, value);
        }
        File.WriteAllText(path,
            JsonSerializer.Serialize(value, property.PropertyType, JsonOptions), new UTF8Encoding(false));
    }

    private IEnumerable<(PropertyInfo Property, string Path)> ConfigItems()
    {
        foreach (var property in typeof(CaorenCupConfig).GetProperties(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (!property.CanRead || !property.CanWrite) continue;
            string jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
            string safeName = new(jsonName.Select(ch =>
                Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            yield return (property, Path.Combine(ModulesDirectory, safeName + ".json"));
        }
    }
}
