using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Features;

public class PresetFeature : ICaorenFeature
{
    public string FeatureName => "Grass Presets";

    private CaorenCupPlugin _plugin = null!;
    private PresetSettings _settings = new();
    private PresetLibrary _library = new();
    private readonly Dictionary<string, GrassPreset> _presets = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly HashSet<string> AlwaysBlockedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "alias",
        "bind",
        "css_plugins",
        "exec",
        "exec_async",
        "quit",
        "exit",
        "_restart"
    };

    private static readonly HashSet<string> RestrictedOnlyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "sv_cheats",
        "host_timescale",
        "ent_fire",
        "plant_bomb",
        "map_setbombradius",
        "subclass_change"
    };

    private static readonly HashSet<string> ExactAllowedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "say",
        "css_say",
        "css_give",
        "css_slay",
        "bot_kick",
        "bot_add_t",
        "bot_add_ct",
        "bot_all_weapons",
        "bot_allow_grenades",
        "bot_allow_machine_guns",
        "bot_allow_pistols",
        "bot_allow_rifles",
        "bot_allow_rogues",
        "bot_allow_shotguns",
        "bot_allow_snipers",
        "bot_allow_sub_machine_guns",
        "bot_randombuy",
        "custom_bot_difficulty",
        "bot_difficulty"
    };

    private static readonly string[] AllowedPrefixes =
    {
        "mp_",
        "sv_",
        "cash_",
        "ammo_",
        "inferno_",
        "ff_",
        "healthshot_",
        "weapon_",
        "ragdoll_",
        "molotov_",
        "cl_",
        "bot_"
    };

    private static readonly string[] ResetBaseCommands =
    {
        "mp_damage_scale_t_body 1",
        "mp_damage_scale_t_head 1",
        "mp_damage_scale_ct_body 1",
        "mp_damage_scale_ct_head 1",
        "mp_damage_vampiric_amount 0",
        "mp_global_damage_per_second 0",
        "mp_weapon_self_inflict_amount 0",
        "mp_t_default_primary weapon_glock",
        "mp_ct_default_primary weapon_usp_silencer",
        "mp_t_default_secondary weapon_glock",
        "mp_ct_default_secondary weapon_usp_silencer",
        "mp_t_default_grenades 1",
        "mp_ct_default_grenades 1",
        "mp_c4timer 40",
        "mp_plant_c4_anywhere 0",
        "mp_buy_anywhere 0",
        "mp_buytime 35",
        "mp_free_armor 0",
        "mp_weapons_allow_heavy -1",
        "mp_weapons_allow_pistols -1",
        "mp_weapons_allow_rifles -1",
        "mp_weapons_allow_smgs -1",
        "mp_weapons_allow_zeus 5",
        "mp_teammates_are_enemies 0",
        "mp_friendlyfire 1",
        "sv_gravity 800",
        "sv_falldamage_scale 1",
        "sv_jump_impulse 301.9933",
        "sv_infinite_ammo 0",
        "sv_disable_radar 0",
        "sv_bounce 0",
        "sv_friction 5.2",
        "sv_accelerate 5.5",
        "sv_maxspeed 320",
        "sv_enablebunnyhopping 0",
        "sv_autobunnyhopping 0",
        "sv_staminajumpcost 0.08",
        "sv_staminalandcost 0.05",
        "sv_staminamax 80",
        "sv_staminarecoveryrate 60",
        "inferno_damage 40",
        "inferno_damage_ct 40",
        "sv_hegrenade_damage_multiplier 1",
        "sv_hegrenade_radius_multiplier 1",
        "host_timescale 1"
    };

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;
        ReloadPresets();

        plugin.AddCommand("css_preset", "Grass 经典玩法预设: /preset list|info|reset|reload|<name>", OnCommandPreset);
        plugin.AddCommand("preset", "Grass 经典玩法预设: preset list|info|reset|reload|<name>", OnCommandPreset);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        config.Preset ??= new PresetSettings();
        _settings = config.Preset;
        EnsureSettings();
    }

    public void OnUnload()
    {
        _presets.Clear();
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        _plugin.SaveConfig();
    }

    public string GetHelpEntry() =>
        $" {ChatColors.Green}/preset list|info|reset|<name>{ChatColors.Default} grass 经典玩法预设";

    public string GetStatusInfo() =>
        $" {ChatColors.Olive}Preset{ChatColors.Default}: {_settings.Enabled} | presets: {_presets.Count}";

    public string? GetPublicConfigInfo() => _settings.Enabled ? $"Grass 预设库已加载 {_presets.Count} 项" : null;

    public string GetFeatureDescription() =>
        "Grass Presets 将早期 grass/*.cfg 整理为分级预设。playable 预设可由管理员执行，restricted 仅 root，archived 只展示不执行。";

    private void OnCommandPreset(CCSPlayerController? player, CommandInfo info)
    {
        if (!_settings.Enabled)
        {
            Reply(player, "Preset 模块当前已禁用。");
            return;
        }

        if (info.ArgCount < 2)
        {
            PrintUsage(player);
            return;
        }

        string arg = info.GetArg(1).Trim();
        string key = arg.ToLowerInvariant();

        switch (key)
        {
            case "list":
                PrintList(player, info.ArgCount >= 3 && info.GetArg(2).Equals("all", StringComparison.OrdinalIgnoreCase));
                return;
            case "info":
                if (info.ArgCount < 3)
                {
                    Reply(player, "用法: /preset info <name>");
                    return;
                }

                PrintInfo(player, info.GetArg(2));
                return;
            case "reset":
                if (!HasPermission(player, _settings.DefaultPlayablePermission))
                {
                    Reply(player, "你没有权限重置预设。");
                    return;
                }

                _plugin.ManagedCvars.ResetAll();
                ExecuteResetBase();
                CaorenCupUtils.PrintToChatAll($"{ChatColors.Green}[草人杯]{ChatColors.Default} 已执行 preset reset-base。");
                return;
            case "reload":
                if (!HasPermission(player, "@css/root"))
                {
                    Reply(player, "只有 @css/root 可以重载预设库。");
                    return;
                }

                ReloadPresets();
                Reply(player, $"预设库已重载: {_presets.Count} 项。");
                return;
            default:
                ApplyPreset(player, arg);
                return;
        }
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        Reply(player, "用法: /preset list [all] | /preset info <name> | /preset reset | /preset <name>");
    }

    private void PrintList(CCSPlayerController? player, bool includeAll)
    {
        var visible = _presets.Values
            .Where(p => includeAll || p.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Reply(player, $"=== Grass Presets: {visible.Count}/{_presets.Count} 项 ===");

        foreach (var group in visible
                     .OrderBy(p => p.Category)
                     .ThenBy(p => p.Name)
                     .GroupBy(p => p.Category))
        {
            string names = string.Join(", ", group.Select(FormatPresetListName));
            Reply(player, $"{group.Key}: {names}");
        }
    }

    private void PrintInfo(CCSPlayerController? player, string name)
    {
        if (!_presets.TryGetValue(name, out GrassPreset? preset))
        {
            Reply(player, $"找不到预设: {name}");
            return;
        }

        Reply(player, $"=== preset: {preset.Name} ===");
        Reply(player, $"标题: {preset.Title}");
        Reply(player, $"分类: {preset.Category} | 状态: {preset.Status} | 等级: {preset.Classification}/{preset.RiskLevel}");
        Reply(player, $"来源: {preset.Source}");
        Reply(player, $"说明: {preset.Description}");
        if (IsMigrated(preset))
        {
            Reply(player, $"已迁移: {preset.Replacement}");
        }
        Reply(player, $"命令数: {preset.Commands.Count} | 权限: {GetRequiredPermission(preset)}");

        if (preset.Notes.Count > 0)
        {
            Reply(player, $"备注: {string.Join("；", preset.Notes)}");
        }

        foreach (string command in preset.Commands.Take(8))
        {
            Reply(player, $" - {command}");
        }

        if (preset.Commands.Count > 8)
        {
            Reply(player, $" ... 另有 {preset.Commands.Count - 8} 条命令");
        }
    }

    private void ApplyPreset(CCSPlayerController? player, string name)
    {
        if (!_presets.TryGetValue(name, out GrassPreset? preset))
        {
            Reply(player, $"找不到预设: {name}。用 /preset list 查看。");
            return;
        }

        if (IsArchived(preset))
        {
            Reply(player, $"预设 {preset.Name} 是 archived，只能查看，不能执行。");
            return;
        }

        if (IsMigrated(preset))
        {
            Reply(player, $"预设 {preset.Name} 已迁移到 Feature，不再通过 /preset 执行。请使用: {preset.Replacement}");
            return;
        }

        string requiredPermission = GetRequiredPermission(preset);
        if (!HasPermission(player, requiredPermission))
        {
            Reply(player, $"你没有权限执行 {preset.Name}。需要权限: {requiredPermission}");
            return;
        }

        if (preset.Commands.Count == 0)
        {
            Reply(player, $"预设 {preset.Name} 没有可执行命令。");
            return;
        }

        if (preset.Commands.Count > _settings.MaxCommandsPerPreset)
        {
            Reply(player, $"预设 {preset.Name} 命令数过多 ({preset.Commands.Count})，已拒绝执行。");
            return;
        }

        List<string> rejected = new();
        foreach (string command in preset.Commands)
        {
            if (!IsCommandAllowed(command, preset, out string reason))
            {
                rejected.Add($"{command} ({reason})");
            }
        }

        if (rejected.Count > 0)
        {
            Reply(player, $"预设 {preset.Name} 含有不允许执行的命令，已拒绝。");
            foreach (string item in rejected.Take(5))
            {
                Reply(player, $" - {item}");
            }
            return;
        }

        if (_settings.ApplyResetBeforePreset && preset.ResetBeforeApply)
        {
            ExecuteResetBase();
        }

        ExecuteCommands(preset.Commands, preset.Name);
        CaorenCupUtils.PrintToChatAll($"{ChatColors.Green}[草人杯]{ChatColors.Default} 已应用预设: {ChatColors.Green}{preset.Name}{ChatColors.Default} ({preset.Title})");
    }

    private void ExecuteResetBase()
    {
        ExecuteCommands(ResetBaseCommands, "reset-base");
    }

    private void ExecuteCommands(IEnumerable<string> commands, string source)
    {
        foreach (string command in commands)
        {
            string trimmed = command.Trim();
            if (trimmed.Length == 0) continue;

            if (_settings.LogExecutedCommands)
            {
                Console.WriteLine($"[CaorenCup][Preset] {source}: {trimmed}");
            }

            Server.ExecuteCommand(trimmed);
        }
    }

    private void ReloadPresets()
    {
        EnsureSettings();
        _presets.Clear();

        string path = Path.Combine(_plugin.ModuleDirectory, "module-configs", _settings.PresetFileName);
        if (!File.Exists(path))
        {
            Console.WriteLine($"[CaorenCup][Preset] Preset file not found: {path}");
            _library = new PresetLibrary();
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            _library = JsonSerializer.Deserialize<PresetLibrary>(json, JsonOptions) ?? new PresetLibrary();

            foreach (GrassPreset preset in _library.Presets)
            {
                if (string.IsNullOrWhiteSpace(preset.Name)) continue;
                _presets[preset.Name.Trim()] = preset.Normalize();
            }

            Console.WriteLine($"[CaorenCup][Preset] Loaded {_presets.Count} presets from {path}");
        }
        catch (Exception ex)
        {
            _library = new PresetLibrary();
            Console.WriteLine($"[CaorenCup][Preset] Failed to load presets: {ex.Message}");
        }
    }

    private bool IsCommandAllowed(string command, GrassPreset preset, out string reason)
    {
        reason = string.Empty;
        string token = GetCommandToken(command);

        if (string.IsNullOrWhiteSpace(token))
        {
            reason = "empty";
            return false;
        }

        if (command.Contains(';') || command.Contains('\r') || command.Contains('\n'))
        {
            reason = "contains command separator";
            return false;
        }

        if (AlwaysBlockedCommands.Contains(token))
        {
            reason = "blocked command";
            return false;
        }

        if (RestrictedOnlyCommands.Contains(token))
        {
            if (!IsRestricted(preset))
            {
                reason = "restricted command in non-restricted preset";
                return false;
            }

            return true;
        }

        if (ExactAllowedCommands.Contains(token))
        {
            return true;
        }

        if (AllowedPrefixes.Any(prefix => token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        reason = "not whitelisted";
        return false;
    }

    private string GetRequiredPermission(GrassPreset preset)
    {
        if (!string.IsNullOrWhiteSpace(preset.Permission))
        {
            return preset.Permission;
        }

        return IsRestricted(preset)
            ? _settings.DefaultRestrictedPermission
            : _settings.DefaultPlayablePermission;
    }

    private static bool IsRestricted(GrassPreset preset) =>
        preset.Status.Equals("restricted", StringComparison.OrdinalIgnoreCase) ||
        preset.Classification.Equals("restricted", StringComparison.OrdinalIgnoreCase) ||
        preset.RiskLevel.Equals("restricted", StringComparison.OrdinalIgnoreCase) ||
        preset.RiskLevel.Equals("high", StringComparison.OrdinalIgnoreCase);

    private static bool IsArchived(GrassPreset preset) =>
        preset.Status.Equals("archived", StringComparison.OrdinalIgnoreCase) ||
        preset.Classification.Equals("archived", StringComparison.OrdinalIgnoreCase);

    private static bool IsMigrated(GrassPreset preset) =>
        preset.Status.Equals("migrated", StringComparison.OrdinalIgnoreCase);

    private static string FormatPresetListName(GrassPreset preset)
    {
        string status = preset.Status.Equals("active", StringComparison.OrdinalIgnoreCase) ? preset.Classification : preset.Status;
        return $"{preset.Name}[{status}]";
    }

    private static string GetCommandToken(string command)
    {
        string trimmed = command.TrimStart();
        if (trimmed.Length == 0) return string.Empty;

        int index = trimmed.IndexOfAny(new[] { ' ', '\t' });
        return (index < 0 ? trimmed : trimmed[..index]).Trim('"').ToLowerInvariant();
    }

    private bool HasPermission(CCSPlayerController? player, string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return true;
        }

        return player == null || AdminManager.PlayerHasPermissions(player, permission);
    }

    private void Reply(CCSPlayerController? player, string message)
    {
        if (player == null)
        {
            Console.WriteLine($"[CaorenCup][Preset] {message}");
        }
        else
        {
            CaorenCupUtils.PrintToChat(player, message);
        }
    }

    private void EnsureSettings()
    {
        _settings ??= new PresetSettings();
        if (string.IsNullOrWhiteSpace(_settings.PresetFileName)) _settings.PresetFileName = "presets.grass.json";
        if (string.IsNullOrWhiteSpace(_settings.DefaultPlayablePermission)) _settings.DefaultPlayablePermission = "@css/changemap";
        if (string.IsNullOrWhiteSpace(_settings.DefaultRestrictedPermission)) _settings.DefaultRestrictedPermission = "@css/root";
        if (_settings.MaxCommandsPerPreset <= 0) _settings.MaxCommandsPerPreset = 200;
    }

    private sealed class PresetLibrary
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("generatedFrom")]
        public string GeneratedFrom { get; set; } = string.Empty;

        [JsonPropertyName("defaultPlayablePermission")]
        public string DefaultPlayablePermission { get; set; } = "@css/changemap";

        [JsonPropertyName("defaultRestrictedPermission")]
        public string DefaultRestrictedPermission { get; set; } = "@css/root";

        [JsonPropertyName("presets")]
        public List<GrassPreset> Presets { get; set; } = new();
    }

    private sealed class GrassPreset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = "misc";

        [JsonPropertyName("classification")]
        public string Classification { get; set; } = "playable";

        [JsonPropertyName("riskLevel")]
        public string RiskLevel { get; set; } = "normal";

        [JsonPropertyName("permission")]
        public string Permission { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("replacement")]
        public string Replacement { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("resetBeforeApply")]
        public bool ResetBeforeApply { get; set; } = true;

        [JsonPropertyName("commands")]
        public List<string> Commands { get; set; } = new();

        [JsonPropertyName("notes")]
        public List<string> Notes { get; set; } = new();

        public GrassPreset Normalize()
        {
            Name = Name.Trim();
            Title = string.IsNullOrWhiteSpace(Title) ? Name : Title.Trim();
            Category = string.IsNullOrWhiteSpace(Category) ? "misc" : Category.Trim();
            Classification = string.IsNullOrWhiteSpace(Classification) ? "playable" : Classification.Trim();
            RiskLevel = string.IsNullOrWhiteSpace(RiskLevel) ? "normal" : RiskLevel.Trim();
            Permission = Permission?.Trim() ?? string.Empty;
            Status = string.IsNullOrWhiteSpace(Status)
                ? (Classification.Equals("archived", StringComparison.OrdinalIgnoreCase) ||
                   RiskLevel.Equals("archived", StringComparison.OrdinalIgnoreCase)
                    ? "archived"
                    : "active")
                : Status.Trim();
            Replacement = Replacement?.Trim() ?? string.Empty;
            Description = Description?.Trim() ?? string.Empty;
            Source = Source?.Trim() ?? string.Empty;
            Commands = Commands?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList() ?? new List<string>();
            Notes = Notes?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList() ?? new List<string>();
            return this;
        }
    }
}
