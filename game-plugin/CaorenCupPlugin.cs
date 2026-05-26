using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using CaorenCup.Features;

namespace CaorenCup;

// ????????? IPluginConfig
public class CaorenCupPlugin : BasePlugin
{
    public override string ModuleName => "CaorenCup All-In-One";
    public override string ModuleVersion => "3.2.0"; // Manual Config Version
    public override string ModuleAuthor => "Graslock + AI";

    public CaorenCupConfig Config { get; set; } = new();
    public ManagedCvarScope ManagedCvars { get; } = new();

    private readonly List<ICaorenFeature> _features = new();
    private bool _allowPlayerNoclip = false;

    // ?????????????? DLL ??
    private string ConfigFilePath => Path.Combine(ModuleDirectory, "CaorenCup.json");

 private string ConfigModulesDirectory => Path.Combine(ModuleDirectory, "module-configs");

 private static readonly JsonSerializerOptions ConfigJsonOptions = new()
 {
  WriteIndented = true,
  PropertyNameCaseInsensitive = true,
  Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
 };
public override void Load(bool hotReload)
    {
        // 1. ?????? (?????????)
        LoadConfig();

        // 2. ??????
        _features.Add(new Features.BombQuizFeature());//1 ????
        _features.Add(new Features.FireHealFeature());//2 ????
        _features.Add(new Features.FOVFeature());//3 ????
        _features.Add(new Features.SkillPointsFeature());//4 ????
        _features.Add(new Features.KillHealFeature());//5 ????
        _features.Add(new Features.SimpleHpFeature());//6 ????
        _features.Add(new Features.BleedFeature());//7 ????
        // 8 ???? OMA ???????????????
        //features.Add(new Features.PlayerStatsFeature());//9 ?????????
        _features.Add(new Features.SmokeFeature());//10 ????
        _features.Add(new Features.MoneyFeature());//11 ????
        _features.Add(new Features.DamageFeature());//12 ????
        //_features.Add(new Features.C4DefuseFeature());//13 ????????????????
        //_features.Add(new Features.DeafenFeature());//14 ???? ????????????
        //_features.Add(new Features.GrenadeDropFeature());//15 ????????????????
        _features.Add(new Features.FriendlyFireFeature());//16 ????
        _features.Add(new Features.AliasFeature());//17 ????
        //_features.Add(new ForceRoundEndFeature());//18 ????????????????
        //_features.Add(new ScoreManagerFeature());//19 ???? ????????????
        _features.Add(new TaggingControlFeature());//20 ????
        _features.Add(new DoubleJumpFeature()); //21 ????
        _features.Add(new Features.EspFeature()); //22 ????
        _features.Add(new OneHpFeature()); //23 ????
        _features.Add(new IncDmgFeature());//24 ????
        _features.Add(new AmmoFeature());//25 ????
        _features.Add(new MagicFeature());//26 ????
        _features.Add(new KbFeature());//27 ????
        _features.Add(new BladeAuraFeature());// 28 ????
        _features.Add(new EcoGuessFeature()); // 29 ????
        _features.Add(new PlaySoundFeature());// 30 ????
        _features.Add(new ArmorFeature()); // 31 ???????
        _features.Add(new LhImmFeature()); //32 ????
        _features.Add(new WeaponSpeedFeature()); // 33 ??????
        _features.Add(new AccuracyFeature()); // 34 acc ??????????
        _features.Add(new RadarColorFeature()); // 35 ???/???????
        _features.Add(new LoadoutFeature()); // 36 ????/????
        _features.Add(new ModifierFeature()); // 37 ?? buff CVar ??
        _features.Add(new MovementRulesFeature()); // 38 ?????? CVar ??
        _features.Add(new PresetFeature()); // 39 grass ??????

        // 3. ??????????????? Init??? Alias ????? JSON ?????
        foreach (var feature in _features)
        {
            feature.OnConfigParsed(Config);
            feature.Init(this);
        }

        // 4. ???? (??? enable/disable)
        AddCommand("helpall", "????????", OnCommandHelp);
        AddCommand("help_plu", "??????", OnCommandHelpPlu);
        AddCommand("status", "??????", OnCommandStatus);
        AddCommand("reset_plu", "??", OnCommandReset);
        AddCommand("save_plu", "??", OnCommandSave);
        AddCommand("rules", "?????????", OnCommandRules);
        AddCommand("hpcap", "???????????: /hpcap <min> <max>", OnCommandHpCap);
        AddCommand("css_hpcap", "???????????: /hpcap <min> <max>", OnCommandHpCap);
        AddCommand("info", "????????: /info <???>", OnCommandInfo);
        AddCommand("info_cast", "?????????: /info_cast <??>", OnCommandInfoCast);
        AddCommand("sv_noclip", "???? noclip: /sv_noclip <1??/0??/status>", OnCommandSvNoclip);
        AddCommandListener("noclip", OnNoclipCommand, HookMode.Pre);

        Console.WriteLine($"[CaorenCup] ?????????????: {ConfigFilePath}");
    }

    private HookResult OnNoclipCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return HookResult.Continue;
        }

        if (_allowPlayerNoclip)
        {
            return HookResult.Continue;
        }

        CaorenCupUtils.PrintToChat(player, "?????? noclip????????");
        Console.WriteLine($"[CaorenCup] Blocked noclip from player {player.PlayerName} (slot {player.Slot}).");
        return HookResult.Handled;
    }

    private void OnCommandSvNoclip(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "??????? noclip ???");
            return;
        }

        if (info.ArgCount < 2)
        {
            ReplySvNoclipStatus(player);
            return;
        }

        string arg = info.GetArg(1).Trim().ToLowerInvariant();
        switch (arg)
        {
            case "1":
            case "on":
            case "true":
            case "enable":
                _allowPlayerNoclip = true;
                ReplySvNoclipChanged(player);
                break;
            case "0":
            case "off":
            case "false":
            case "disable":
                _allowPlayerNoclip = false;
                ReplySvNoclipChanged(player);
                break;
            case "status":
                ReplySvNoclipStatus(player);
                break;
            default:
                ReplySvNoclipUsage(player);
                break;
        }
    }

    private void ReplySvNoclipChanged(CCSPlayerController? player)
    {
        string state = _allowPlayerNoclip ? "??" : "??";
        string message = $"[???] ?? noclip ?????{state}?";

        if (player == null)
        {
            Console.WriteLine(message);
        }
        else
        {
            CaorenCupUtils.PrintToChatAll(message);
        }
    }

    private void ReplySvNoclipStatus(CCSPlayerController? player)
    {
        string state = _allowPlayerNoclip ? "??" : "??";
        string message = $"[???] ???? noclip?{state}????/sv_noclip <1??/0??/status>";

        if (player == null)
        {
            Console.WriteLine(message);
        }
        else
        {
            CaorenCupUtils.PrintToChat(player, message);
        }
    }

    private void ReplySvNoclipUsage(CCSPlayerController? player)
    {
        string message = "[???] ???/sv_noclip <1??/0??/status>";

        if (player == null)
        {
            Console.WriteLine(message);
        }
        else
        {
            CaorenCupUtils.PrintToChat(player, message);
        }
    }

    private void OnCommandInfoCast(CCSPlayerController? player, CommandInfo info)
    {
        // ??????????????
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "????????????");
            return;
        }

        if (info.ArgCount < 2)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "??: /info_cast <???>");
            return;
        }

        string key = info.GetArg(1).ToLower();
        var target = _features.FirstOrDefault(f =>
            f.FeatureName.ToLower().Contains(key) ||
            f.GetType().Name.ToLower().Contains(key));

        if (target != null)
        {
            string desc = target.GetFeatureDescription();

            // ????
            CaorenCupUtils.PrintToChatAll($" \x10========== [???] {target.FeatureName} ???? ==========\x01");
            foreach (var line in desc.Split('\n'))
            {
                CaorenCupUtils.PrintToChatAll(line);
            }
            CaorenCupUtils.PrintToChatAll(" \x10==================================================\x01");
        }
        else
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "???????");
        }
    }

    private void OnCommandInfo(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        // ?????????????
        if (info.ArgCount < 2)
        {
            CaorenCupUtils.PrintToChat(player, "??: /info <???>");
            CaorenCupUtils.PrintToChat(player, "??: /info bomb (??BombQuiz??)");
            CaorenCupUtils.PrintToChat(player, "????: " + string.Join(", ", _features.Select(f => f.GetType().Name.Replace("Feature", ""))));
            return;
        }

        string key = info.GetArg(1).ToLower();
        var target = _features.FirstOrDefault(f =>
            f.FeatureName.ToLower().Contains(key) ||
            f.GetType().Name.ToLower().Contains(key));

        if (target != null)
        {
            CaorenCupUtils.PrintToChat(player, $"\x10========== {target.FeatureName} ???? ==========\x01");
            // ??????
            string desc = target.GetFeatureDescription();
            foreach (var line in desc.Split('\n'))
            {
                CaorenCupUtils.PrintToChat(player, line);
            }
            CaorenCupUtils.PrintToChat(player, "\x10============================================\x01");
        }
        else
        {
            CaorenCupUtils.PrintToChat(player, "???????");
        }
    }

    // --- ??????????? ---
    private void OnCommandRules(CCSPlayerController? player, CommandInfo info)
    {
        // ??????
        List<string> rules = new List<string>();
        foreach (var feature in _features)
        {
            string? rule = feature.GetPublicConfigInfo();
            if (!string.IsNullOrEmpty(rule))
            {
                rules.Add(rule);
            }
        }

        if (Config.HpCap.Enabled)
        {
            rules.Add($"??????: ??????? {Config.HpCap.Max} HP???????? {Config.HpCap.Min} HP");
        }

        if (rules.Count == 0)
        {
            CaorenCupUtils.PrintToChatAll(" [???] ??????????????????????");
        }
        else
        {
            CaorenCupUtils.PrintToChatAll(" \x10========== [???] ?????? ==========\x01");
            foreach (var rule in rules)
            {
                CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}>{ChatColors.Default} {rule}");
            }
            CaorenCupUtils.PrintToChatAll(" \x10===========================================\x01");
        }
    }
 private void LoadConfig()
 {
  try
  {
   Config = new CaorenCupConfig();

   if (File.Exists(ConfigFilePath))
   {
    try
    {
     string json = File.ReadAllText(ConfigFilePath);
     var loaded = JsonSerializer.Deserialize<CaorenCupConfig>(json, ConfigJsonOptions);
     if (loaded != null)
     {
      Config = loaded;
      Console.WriteLine("[CaorenCup] Loaded legacy CaorenCup.json as migration seed.");
     }
    }
    catch (Exception ex)
    {
     Console.WriteLine($"[CaorenCup] Failed to read legacy CaorenCup.json, continuing with module configs: {ex.Message}");
    }
   }
   else
   {
    Console.WriteLine("[CaorenCup] Legacy CaorenCup.json not found. Using module configs or defaults.");
   }

   LoadModuleConfigFiles();
   EnsureConfigObjects();

   Console.WriteLine($"[CaorenCup] Config loaded. Module config directory: {ConfigModulesDirectory}");
  }
  catch (Exception ex)
  {
   Console.WriteLine($"[CaorenCup] Fatal config load error: {ex.Message}");
   Console.WriteLine("[CaorenCup] Falling back to default config.");
   Config = new CaorenCupConfig();
   EnsureConfigObjects();
  }
 }

 private void LoadModuleConfigFiles()
 {
  Directory.CreateDirectory(ConfigModulesDirectory);

  foreach (var item in GetModuleConfigItems())
  {
   object? currentValue = item.Property.GetValue(Config);
   if (currentValue == null)
   {
    currentValue = Activator.CreateInstance(item.Property.PropertyType);
    item.Property.SetValue(Config, currentValue);
   }

   if (!File.Exists(item.FilePath))
   {
    SaveModuleConfigFile(item.Property, item.JsonName, item.FilePath);
    Console.WriteLine($"[CaorenCup] Created module config: {Path.GetFileName(item.FilePath)}");
    continue;
   }

   try
   {
    string moduleJson = File.ReadAllText(item.FilePath);
    object? moduleValue = JsonSerializer.Deserialize(moduleJson, item.Property.PropertyType, ConfigJsonOptions);
    if (moduleValue != null)
    {
     item.Property.SetValue(Config, moduleValue);
     Console.WriteLine($"[CaorenCup] Loaded module config: {Path.GetFileName(item.FilePath)}");
    }
   }
   catch (Exception ex)
   {
    Console.WriteLine($"[CaorenCup] Failed to read module config, keeping legacy/default value: {Path.GetFileName(item.FilePath)} | {ex.Message}");
   }
  }
 }

 public void SaveConfig()
 {
  try
  {
   Directory.CreateDirectory(ConfigModulesDirectory);

   foreach (var item in GetModuleConfigItems())
   {
    SaveModuleConfigFile(item.Property, item.JsonName, item.FilePath);
   }

   Console.WriteLine($"[CaorenCup] Config saved by modules: {ConfigModulesDirectory}");
   Console.WriteLine("[CaorenCup] Legacy CaorenCup.json is only used as a migration seed. Runtime config is module-configs/*.json.");
  }
  catch (Exception ex)
  {
   Console.WriteLine($"[CaorenCup] Failed to save config: {ex.Message}");
  }
 }

 private void SaveModuleConfigFile(System.Reflection.PropertyInfo property, string jsonName, string filePath)
 {
  object? value = property.GetValue(Config);
  if (value == null)
  {
   value = Activator.CreateInstance(property.PropertyType);
   property.SetValue(Config, value);
  }

  string json = JsonSerializer.Serialize(value, property.PropertyType, ConfigJsonOptions);
  File.WriteAllText(filePath, json);
 }

 private IEnumerable<(System.Reflection.PropertyInfo Property, string JsonName, string FilePath)> GetModuleConfigItems()
 {
  var properties = typeof(CaorenCupConfig).GetProperties(
   System.Reflection.BindingFlags.Instance |
   System.Reflection.BindingFlags.Public |
   System.Reflection.BindingFlags.DeclaredOnly
  );

  foreach (var property in properties)
  {
   if (!property.CanRead || !property.CanWrite) continue;

   var jsonNameAttribute = property
    .GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
    .OfType<JsonPropertyNameAttribute>()
    .FirstOrDefault();

   string jsonName = jsonNameAttribute?.Name ?? property.Name;
   string safeFileName = MakeSafeConfigFileName(jsonName) + ".json";
   string filePath = Path.Combine(ConfigModulesDirectory, safeFileName);

   yield return (property, jsonName, filePath);
  }
 }

 private static string MakeSafeConfigFileName(string jsonName)
 {
  var invalidChars = Path.GetInvalidFileNameChars();
  return new string(jsonName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
 }

 private void EnsureConfigObjects()
 {
  foreach (var item in GetModuleConfigItems())
  {
   if (item.Property.GetValue(Config) != null) continue;

   object? value = Activator.CreateInstance(item.Property.PropertyType);
   if (value != null)
   {
    item.Property.SetValue(Config, value);
   }
  }
 }
 public override void Unload(bool hotReload)
    {
        foreach (var feature in _features) feature.OnUnload();
        _features.Clear();
    }

    // --- ???? ---

    private void OnCommandHelp(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        CaorenCupUtils.PrintToChat(player, "=== ??? ????? ===");

        // ???????
        player.PrintToChat($" {ChatColors.Green}/help_plu{ChatColors.Default} : >>> ?????????? <<<");

        player.PrintToChat("------------------------------");

        // ???????? (??? enable ? disable)
        player.PrintToChat($" {ChatColors.Green}/status{ChatColors.Default} : ????");
        player.PrintToChat($" {ChatColors.Green}/save_plu{ChatColors.Default} : ????");
        player.PrintToChat($" {ChatColors.Green}/reset_plu{ChatColors.Default} : ????");
        player.PrintToChat($" {ChatColors.Green}/hpcap <min> <max>{ChatColors.Default} : ???????????");
    }

    private void OnCommandHelpPlu(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        player.PrintToChat($" {ChatColors.Green}{CaorenCupUtils.Tag}=== ?????? ==={ChatColors.Default}");

        // ?????????????????
        foreach (var feature in _features)
        {
            player.PrintToChat(CaorenCupUtils.FormatHelpMenuLine(feature.GetHelpEntry()));
        }
    }

    private void OnCommandStatus(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        if (info.ArgCount > 1)
        {
            string key = info.GetArg(1).ToLower();
            var targetFeature = _features.FirstOrDefault(f => f.FeatureName.ToLower().Contains(key) || f.GetType().Name.ToLower().Contains(key));
            if (targetFeature != null) { CaorenCupUtils.PrintToChat(player, $"=== {targetFeature.FeatureName} ???? ==="); player.PrintToChat(targetFeature.GetStatusInfo()); }
            else { CaorenCupUtils.PrintToChat(player, "???????"); }
            return;
        }
        CaorenCupUtils.PrintToChat(player, "=== ???? (/status <??> ??) ===");
        string hpCapState = Config.HpCap.Enabled
            ? $"HpCap: {ChatColors.Green}??{ChatColors.Default} | ??:{Config.HpCap.Min}-{Config.HpCap.Max}"
            : $"HpCap: {ChatColors.Red}???{ChatColors.Default}";
        player.PrintToChat(hpCapState);
        foreach (var feature in _features) { string full = feature.GetStatusInfo(); player.PrintToChat(full.Split('|')[0]); }
    }

    [ConsoleCommand("css_reset_plu", "??")]
    public void OnConsoleReset(CCSPlayerController? p, CommandInfo i) { PerformReset(); if (p == null) Console.WriteLine("[CaorenCup] ????"); }
    private void OnCommandReset(CCSPlayerController? p, CommandInfo i)
    {
        if (p != null && !AdminManager.PlayerHasPermissions(p, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(p, "?????");
            return;
        }
        if (p != null) PerformReset();
    }

    private void PerformReset()
    {
        ManagedCvars.ResetAll();

        foreach (var f in _features)
        {
            if (f is Features.SkillPointsFeature) continue;
            if (f is Features.RadarColorFeature) continue;
            f.SetEnabled(false);
        }
        Config.HpCap.Enabled = false;
        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}[???]{ChatColors.Default} ?????????????");
        SaveConfig(); // ?????????????????
    }


    private void OnCommandHpCap(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "?????");
            return;
        }

        if (info.ArgCount == 1)
        {
            string state = Config.HpCap.Enabled
                ? $"?????????? {Config.HpCap.Max} HP??????? {Config.HpCap.Min} HP"
                : "???";

            if (player != null)
            {
                CaorenCupUtils.PrintToChat(player, $"?? /hpcap ???{state}");
                CaorenCupUtils.PrintToChat(player, "??: /hpcap <min> <max>??? /hpcap 1 150?/hpcap 0 ????");
            }
            else
            {
                Console.WriteLine($"[CaorenCup] ?? hpcap ???{state}");
            }
            return;
        }

        string firstArg = info.GetArg(1).ToLower();
        if (firstArg == "0" || firstArg == "off" || firstArg == "disable")
        {
            Config.HpCap.Enabled = false;
            SaveConfig();
            CaorenCupUtils.PrintToChatAll($" {ChatColors.Red}??????????{ChatColors.Default}");
            return;
        }

        if (info.ArgCount < 3)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "??: /hpcap <min> <max>??? /hpcap 1 150??? /hpcap 0 ???");
            return;
        }

        if (!int.TryParse(info.GetArg(1), out int min) || !int.TryParse(info.GetArg(2), out int max))
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "min/max ??????");
            return;
        }

        if (min < 0 || max < 1 || max < min)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "??????? min >= 0?max >= 1?? max >= min?");
            return;
        }

        Config.HpCap.Enabled = true;
        Config.HpCap.Min = min;
        Config.HpCap.Max = max;
        SaveConfig();

        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}??????????{ChatColors.Default}??????? {ChatColors.Green}{max}{ChatColors.Default} HP???????? {ChatColors.Green}{min}{ChatColors.Default} HP?");
    }

    private void OnCommandSave(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "?????");
            return;
        }
        SaveConfig();
        if (player != null) CaorenCupUtils.PrintToChat(player, "?????????????");
    }
}
