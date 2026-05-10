using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CaorenCup;

namespace CaorenCup.Features;

public class AliasFeature : ICaorenFeature
{
    public string FeatureName => "Command Alias";

    private const string ArgsPlaceholder = "{args}";

    private CaorenCupPlugin _plugin = null!;
    private AliasSettings _settings = new();

    private readonly Dictionary<string, CommandInfo.CommandCallback> _registeredAliasCommands =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ReservedAliasNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "alias_list",
        "alias_reload"
    };

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;
        EnsureSettings();

        RegisterAliasCommands();

        // 聊天栏输入 /alias_list 或 !alias_list
        plugin.AddCommand("css_alias_list", "列出当前 Alias 控制台指令别名", OnCommandList);

        // 聊天栏输入 /alias_reload 或 !alias_reload
        // 注意：这个只重载 Alias 配置并重新注册 Alias 指令，不会重载其它功能模块。
        plugin.AddCommand("css_alias_reload", "从 CaorenCup.json 重载 Alias 控制台指令别名", OnCommandReload);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        config.Alias ??= new AliasSettings();
        _settings = config.Alias;
        EnsureSettings();
    }

    public void OnUnload()
    {
        UnregisterAliasCommands();
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;

        if (_plugin.Config.Alias != _settings)
        {
            _plugin.Config.Alias = _settings;
        }

        _plugin.SaveConfig();
    }

    public string? GetPublicConfigInfo() => null;

    public string GetHelpEntry() =>
        $" {ChatColors.Green}/alias_list{ChatColors.Default} 查看已加载的控制台指令别名";

    public string GetStatusInfo() =>
        $" {ChatColors.Olive}Alias{ChatColors.Default}: {_settings.Enabled} | 别名数: {_registeredAliasCommands.Count}/{_settings.CommandMap.Count}";

    public string GetFeatureDescription()
    {
        return " [指令别名] JSON 控制台指令别名系统。\n"
             + " 管理员在聊天栏输入 /别名 或 !别名 后，插件会以服务器控制台身份执行 CaorenCup.json 中配置的目标控制台命令。";
    }

    private void RegisterAliasCommands()
    {
        EnsureSettings();
        UnregisterAliasCommands();

        foreach (var kvp in _settings.CommandMap)
        {
            string aliasKey = NormalizeAliasKey(kvp.Key);
            string targetCommand = (kvp.Value ?? string.Empty).Trim();

            if (!IsSafeAliasKey(aliasKey))
            {
                Console.WriteLine($"[CaorenCup][Alias] 跳过非法别名: '{kvp.Key}'。别名只能包含英文字母、数字和下划线。");
                continue;
            }

            if (ReservedAliasNames.Contains(aliasKey))
            {
                Console.WriteLine($"[CaorenCup][Alias] 跳过保留别名: '{aliasKey}'。");
                continue;
            }

            if (string.IsNullOrWhiteSpace(targetCommand))
            {
                Console.WriteLine($"[CaorenCup][Alias] 跳过空目标命令: /{aliasKey}");
                continue;
            }

            string registeredCommand = $"css_{aliasKey}";

            if (_registeredAliasCommands.ContainsKey(registeredCommand))
            {
                Console.WriteLine($"[CaorenCup][Alias] 跳过重复别名: /{aliasKey}");
                continue;
            }

            CommandInfo.CommandCallback callback = (player, info) =>
            {
                ExecuteAlias(player, info, aliasKey, targetCommand);
            };

            _plugin.AddCommand(
                registeredCommand,
                $"Alias: /{aliasKey} -> {targetCommand}",
                callback
            );

            _registeredAliasCommands[registeredCommand] = callback;

            if (LooksLikeChatCommand(targetCommand))
            {
                Console.WriteLine($"[CaorenCup][Alias] 警告: /{aliasKey} 的目标命令像聊天指令，不像控制台命令: {targetCommand}");
                Console.WriteLine($"[CaorenCup][Alias]      目标应写成 mp_pause_match、mp_unpause_match、css_xxx 等服务器控制台可执行命令。");
            }
        }

        Console.WriteLine($"[CaorenCup][Alias] 已注册 {_registeredAliasCommands.Count} 个聊天别名。");
    }

    private void UnregisterAliasCommands()
    {
        if (_registeredAliasCommands.Count == 0)
        {
            return;
        }

        foreach (var item in _registeredAliasCommands)
        {
            try
            {
                _plugin.RemoveCommand(item.Key, item.Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CaorenCup][Alias] 移除别名命令失败: {item.Key} | {ex.Message}");
            }
        }

        _registeredAliasCommands.Clear();
    }

    private void ExecuteAlias(CCSPlayerController? player, CommandInfo info, string aliasKey, string commandTemplate)
    {
        if (!_settings.Enabled)
        {
            Reply(info, "Alias 模块当前已禁用。");
            return;
        }

        if (player != null && !HasAliasPermission(player))
        {
            Reply(info, $"{ChatColors.Red}你没有权限执行此 Alias 指令。");
            return;
        }

        if (LooksLikeChatCommand(commandTemplate))
        {
            Reply(
                info,
                $"{ChatColors.Red}Alias 配置错误：目标命令必须是服务器控制台命令，不能以 /、! 或 . 开头。请把 /{aliasKey} 的目标改成例如 mp_pause_match 或 css_xxx。"
            );

            Console.WriteLine($"[CaorenCup][Alias] 已拒绝聊天风格目标命令: /{aliasKey} -> {commandTemplate}");
            return;
        }

        string finalCommand = BuildConsoleCommand(commandTemplate, info, out bool usedArgsPlaceholder);

        if (string.IsNullOrWhiteSpace(finalCommand))
        {
            Reply(info, $"{ChatColors.Red}Alias 配置错误：/{aliasKey} 最终生成的控制台命令为空。");
            return;
        }

        if (usedArgsPlaceholder && ContainsCommandSeparator(info.ArgString))
        {
            Reply(info, $"{ChatColors.Red}Alias 参数中包含不安全的命令分隔符，已拒绝执行。");
            Console.WriteLine($"[CaorenCup][Alias] 已拒绝包含分隔符的参数: /{aliasKey} args='{info.ArgString}'");
            return;
        }

        try
        {
            Server.ExecuteCommand(finalCommand);

            Reply(info, $"已以服务器控制台身份执行别名: {ChatColors.Green}/{aliasKey}{ChatColors.Default}");

            string source = player == null
                ? "SERVER"
                : $"{player.PlayerName} / {player.SteamID}";

            Console.WriteLine($"[CaorenCup][Alias] {source} 执行 /{aliasKey} -> {finalCommand}");
        }
        catch (Exception ex)
        {
            Reply(info, $"{ChatColors.Red}Alias 执行失败：{ex.Message}");
            Console.WriteLine($"[CaorenCup][Alias] 执行失败: /{aliasKey} -> {finalCommand} | {ex}");
        }
    }

    private void OnCommandList(CCSPlayerController? player, CommandInfo info)
    {
        EnsureSettings();

        string state = _settings.Enabled
            ? $"{ChatColors.Green}已启用"
            : $"{ChatColors.Red}已禁用";

        string permission = string.IsNullOrWhiteSpace(_settings.Permission)
            ? "无"
            : _settings.Permission;

        Reply(info, $"=== Alias 控制台指令别名：{_registeredAliasCommands.Count}/{_settings.CommandMap.Count} ===");
        Reply(info, $"状态: {state}{ChatColors.Default} | 权限: {permission}");

        if (_settings.CommandMap.Count == 0)
        {
            Reply(info, $"{ChatColors.Red}列表为空，请检查 CaorenCup.json 的 Alias.CommandMap。");
            return;
        }

        foreach (var kvp in _settings.CommandMap)
        {
            string aliasKey = NormalizeAliasKey(kvp.Key);
            string targetCommand = (kvp.Value ?? string.Empty).Trim();

            string warning = LooksLikeChatCommand(targetCommand)
                ? $"{ChatColors.Red}  [目标不是控制台命令]"
                : string.Empty;

            Reply(info, $" {ChatColors.Green}/{aliasKey}{ChatColors.Default} -> {targetCommand}{warning}");
        }
    }

    private void OnCommandReload(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            Reply(info, $"{ChatColors.Red}只有 @css/root 可以重载 Alias 配置。");
            return;
        }

        if (TryReloadAliasConfig(out string message))
        {
            Reply(info, $"{ChatColors.Green}{message}");
        }
        else
        {
            Reply(info, $"{ChatColors.Red}{message}");
        }
    }

    private bool TryReloadAliasConfig(out string message)
    {
        try
        {
            string configPath = Path.Combine(_plugin.ModuleDirectory, "CaorenCup.json");

            if (!File.Exists(configPath))
            {
                message = $"找不到配置文件: {configPath}";
                return false;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            string json = File.ReadAllText(configPath);
            var loaded = JsonSerializer.Deserialize<CaorenCupConfig>(json, options);

            if (loaded?.Alias == null)
            {
                message = "配置文件中没有有效的 Alias 节点。";
                return false;
            }

            _plugin.Config.Alias = loaded.Alias;
            _settings = _plugin.Config.Alias;
            EnsureSettings();

            RegisterAliasCommands();

            message = $"Alias 已重载，当前注册 {_registeredAliasCommands.Count} 个聊天别名。";
            Console.WriteLine($"[CaorenCup][Alias] {message}");
            return true;
        }
        catch (Exception ex)
        {
            message = $"Alias 重载失败: {ex.Message}";
            Console.WriteLine($"[CaorenCup][Alias] {message}");
            return false;
        }
    }

    private void EnsureSettings()
    {
        _settings ??= new AliasSettings();

        _settings.Permission ??= string.Empty;

        _settings.CommandMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // JSON 反序列化出来的 Dictionary 默认区分大小写，这里统一成忽略大小写。
        _settings.CommandMap = new Dictionary<string, string>(
            _settings.CommandMap,
            StringComparer.OrdinalIgnoreCase
        );
    }

    private bool HasAliasPermission(CCSPlayerController player)
    {
        if (string.IsNullOrWhiteSpace(_settings.Permission))
        {
            return true;
        }

        return AdminManager.PlayerHasPermissions(player, _settings.Permission);
    }

    private static string BuildConsoleCommand(string template, CommandInfo info, out bool usedArgsPlaceholder)
    {
        usedArgsPlaceholder = template.Contains(ArgsPlaceholder, StringComparison.OrdinalIgnoreCase);

        if (!usedArgsPlaceholder)
        {
            return template.Trim();
        }

        string args = (info.ArgString ?? string.Empty).Trim();

        return template
            .Replace(ArgsPlaceholder, args, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static bool ContainsCommandSeparator(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.Contains(';') || value.Contains('\r') || value.Contains('\n');
    }

    private static bool LooksLikeChatCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        char first = command.TrimStart()[0];

        return first == '/' || first == '!' || first == '.';
    }

    private static string NormalizeAliasKey(string raw)
    {
        string key = (raw ?? string.Empty).Trim();

        while (key.StartsWith("/") || key.StartsWith("!") || key.StartsWith("."))
        {
            key = key[1..].TrimStart();
        }

        if (key.StartsWith("css_", StringComparison.OrdinalIgnoreCase))
        {
            key = key[4..];
        }

        return key.Trim().ToLowerInvariant();
    }

    private static bool IsSafeAliasKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        foreach (char c in key)
        {
            bool ok =
                c is >= 'a' and <= 'z'
                || c is >= 'A' and <= 'Z'
                || c is >= '0' and <= '9'
                || c == '_';

            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static void Reply(CommandInfo info, string message)
    {
        info.ReplyToCommand($" {ChatColors.Green}{CaorenCupUtils.Tag}{ChatColors.Default}{message}");
    }
}