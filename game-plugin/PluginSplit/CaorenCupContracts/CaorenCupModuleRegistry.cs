namespace CaorenCup.Contracts;

/// <summary>独立插件向总菜单发布的状态与重置入口。</summary>
public sealed record CaorenCupModuleDescriptor(
    object Owner,
    string Key,
    string FeatureName,
    string TypeName,
    Func<string> Help,
    Func<string> Status,
    Func<string> Description,
    Func<string?> PublicConfig,
    Action? Reset);

public static class CaorenCupModuleRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CaorenCupModuleDescriptor> Modules =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(CaorenCupModuleDescriptor module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (string.IsNullOrWhiteSpace(module.Key))
            throw new ArgumentException("Module key is required.", nameof(module));

        lock (Gate)
        {
            if (Modules.TryGetValue(module.Key, out var existing) &&
                !ReferenceEquals(existing.Owner, module.Owner))
                throw new InvalidOperationException($"CaorenCup module key already registered: {module.Key}");
            Modules[module.Key] = module;
        }
    }

    public static void Unregister(object owner)
    {
        lock (Gate)
        {
            foreach (var key in Modules.Where(pair => ReferenceEquals(pair.Value.Owner, owner))
                         .Select(pair => pair.Key).ToArray())
                Modules.Remove(key);
        }
    }

    public static CaorenCupModuleDescriptor[] Snapshot()
    {
        lock (Gate)
            return Modules.Values.ToArray();
    }

    public static CaorenCupModuleDescriptor? Find(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return Snapshot().FirstOrDefault(module =>
            module.FeatureName.Contains(key, StringComparison.OrdinalIgnoreCase) ||
            module.TypeName.Contains(key, StringComparison.OrdinalIgnoreCase));
    }
}
