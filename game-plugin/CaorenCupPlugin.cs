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

// 注意：这里不再继承 IPluginConfig
public class CaorenCupPlugin : BasePlugin
{
    public override string ModuleName => "CaorenCup All-In-One";
    public override string ModuleVersion => "3.2.0"; // Manual Config Version
    public override string ModuleAuthor => "Graslock + AI";

    public CaorenCupConfig Config { get; set; } = new();

    private readonly List<ICaorenFeature> _features = new();

    // 强制锁定配置文件路径：永远在 DLL 旁边
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
        // 1. 手动加载配置 (这是最关键的一步！)
        LoadConfig();

        // 2. 注册所有积木
        _features.Add(new Features.BombQuizFeature());//1 炸弹解密
        _features.Add(new Features.FireHealFeature());//2 火焰回血
        _features.Add(new Features.FOVFeature());//3 视野宽度
        _features.Add(new Features.SkillPointsFeature());//4 技能点数
        _features.Add(new Features.KillHealFeature());//5 击杀回血
        _features.Add(new Features.SimpleHpFeature());//6 查看伤害
        _features.Add(new Features.BleedFeature());//7 血量更改
        _features.Add(new Features.OMAFeature());//8 一人成军
        //features.Add(new Features.PlayerStatsFeature());//9 玩家数据（已弃用）
        _features.Add(new Features.SmokeFeature());//10 烟雾修改
        _features.Add(new Features.MoneyFeature());//11 货币战争
        _features.Add(new Features.DamageFeature());//12 伤害修改
        //_features.Add(new Features.C4DefuseFeature());//13 拆弹时间（未按预期生效，已弃用）
        //_features.Add(new Features.DeafenFeature());//14 致聋玩家 （未按预期生效，已弃用）
        //_features.Add(new Features.GrenadeDropFeature());//15 空投模块（未按预期生效，已弃用）
        _features.Add(new Features.FriendlyFireFeature());//16 友伤管理
        _features.Add(new Features.AliasFeature());//17 指令别名
        //_features.Add(new ForceRoundEndFeature());//18 强制结束（未按预期生效，已弃用）
        //_features.Add(new ScoreManagerFeature());//19 分数管理 （未按预期生效，已弃用）
        _features.Add(new TaggingControlFeature());//20 受击效果
        _features.Add(new DoubleJumpFeature()); //21 二次跳跃
        _features.Add(new Features.EspFeature()); //22 通透世界
        _features.Add(new OneHpFeature()); //23 致命奇迹
        _features.Add(new IncDmgFeature());//24 伤害函数
        _features.Add(new AmmoFeature());//25 子弹修改
        _features.Add(new MagicFeature());//26 魔法子弹
        _features.Add(new KbFeature());//27 击退功能
        _features.Add(new BladeAuraFeature());// 28 剑气挥砍
        _features.Add(new EcoGuessFeature()); // 29 经济猜测
        _features.Add(new PlaySoundFeature());// 30 自定音效
        _features.Add(new ArmorFeature()); // 31 防弹衣耐久控制
        _features.Add(new LhImmFeature()); //32 名刀无敌
        _features.Add(new WeaponSpeedFeature()); // 33 武器速度控制
        _features.Add(new AccuracyFeature()); // 34 acc 武器精准与后坐力控制

        // 3. 注入配置并初始化。先给配置，再 Init，保证 Alias 等模块能按 JSON 注册指令。
        foreach (var feature in _features)
        {
            feature.OnConfigParsed(Config);
            feature.Init(this);
        }

        // 4. 注册指令 (已弃用 enable/disable)
        AddCommand("helpall", "查看所有指令入口", OnCommandHelp);
        AddCommand("help_plu", "查看插件列表", OnCommandHelpPlu);
        AddCommand("status", "查看功能状态", OnCommandStatus);
        AddCommand("reset_plu", "重置", OnCommandReset);
        AddCommand("save_plu", "保存", OnCommandSave);
        AddCommand("rules", "显示当前服务器规则", OnCommandRules);
        AddCommand("hpcap", "设置模块血量全局上下限: /hpcap <min> <max>", OnCommandHpCap);
        AddCommand("css_hpcap", "设置模块血量全局上下限: /hpcap <min> <max>", OnCommandHpCap);
        AddCommand("info", "查看模块玩法说明: /info <模块名>", OnCommandInfo);
        AddCommand("info_cast", "向全服广播玩法说明: /info_cast <模块>", OnCommandInfoCast);

        Console.WriteLine($"[CaorenCup] 插件加载完成。配置文件路径: {ConfigFilePath}");
    }

    private void OnCommandInfoCast(CCSPlayerController? player, CommandInfo info)
    {
        // 权限检查：防止普通玩家乱刷屏
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "你没有权限执行全服广播。");
            return;
        }

        if (info.ArgCount < 2)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "用法: /info_cast <模块名>");
            return;
        }

        string key = info.GetArg(1).ToLower();
        var target = _features.FirstOrDefault(f =>
            f.FeatureName.ToLower().Contains(key) ||
            f.GetType().Name.ToLower().Contains(key));

        if (target != null)
        {
            string desc = target.GetFeatureDescription();

            // 全服广播
            CaorenCupUtils.PrintToChatAll($" \x10========== [草人杯] {target.FeatureName} 玩法介绍 ==========\x01");
            foreach (var line in desc.Split('\n'))
            {
                CaorenCupUtils.PrintToChatAll(line);
            }
            CaorenCupUtils.PrintToChatAll(" \x10==================================================\x01");
        }
        else
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "未找到该模块。");
        }
    }

    private void OnCommandInfo(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        // 如果不带参数，列出所有模块
        if (info.ArgCount < 2)
        {
            CaorenCupUtils.PrintToChat(player, "用法: /info <模块名>");
            CaorenCupUtils.PrintToChat(player, "例如: /info bomb (查看BombQuiz玩法)");
            CaorenCupUtils.PrintToChat(player, "可用模块: " + string.Join(", ", _features.Select(f => f.GetType().Name.Replace("Feature", ""))));
            return;
        }

        string key = info.GetArg(1).ToLower();
        var target = _features.FirstOrDefault(f =>
            f.FeatureName.ToLower().Contains(key) ||
            f.GetType().Name.ToLower().Contains(key));

        if (target != null)
        {
            CaorenCupUtils.PrintToChat(player, $"\x10========== {target.FeatureName} 玩法说明 ==========\x01");
            // 打印多行说明
            string desc = target.GetFeatureDescription();
            foreach (var line in desc.Split('\n'))
            {
                CaorenCupUtils.PrintToChat(player, line);
            }
            CaorenCupUtils.PrintToChat(player, "\x10============================================\x01");
        }
        else
        {
            CaorenCupUtils.PrintToChat(player, "未找到该模块。");
        }
    }

    // --- 完全手动的配置读写逻辑 ---
    private void OnCommandRules(CCSPlayerController? player, CommandInfo info)
    {
        // 构建规则列表
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
            rules.Add($"全局血量保护: 模块回血最高到 {Config.HpCap.Max} HP，模块扣血最低到 {Config.HpCap.Min} HP");
        }

        if (rules.Count == 0)
        {
            CaorenCupUtils.PrintToChatAll(" [草人杯] 当前服务器运行默认竞技规则，未开启特殊修改。");
        }
        else
        {
            CaorenCupUtils.PrintToChatAll(" \x10========== [草人杯] 当前特殊规则 ==========\x01");
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

    // --- 指令处理 ---

    private void OnCommandHelp(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        CaorenCupUtils.PrintToChat(player, "=== 草人杯 总指令菜单 ===");

        // 指引前往第二层
        player.PrintToChat($" {ChatColors.Green}/help_plu{ChatColors.Default} : >>> 查看所有功能插件列表 <<<");

        player.PrintToChat("------------------------------");

        // 显示全局管理指令 (去除了 enable 和 disable)
        player.PrintToChat($" {ChatColors.Green}/status{ChatColors.Default} : 查看状态");
        player.PrintToChat($" {ChatColors.Green}/save_plu{ChatColors.Default} : 保存配置");
        player.PrintToChat($" {ChatColors.Green}/reset_plu{ChatColors.Default} : 一键重置");
        player.PrintToChat($" {ChatColors.Green}/hpcap <min> <max>{ChatColors.Default} : 设置模块血量全局上下限");
    }

    private void OnCommandHelpPlu(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        player.PrintToChat($" {ChatColors.Green}{CaorenCupUtils.Tag}=== 功能模块列表 ==={ChatColors.Default}");

        // 只有在这里才遍历显示各个插件的入口
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
            if (targetFeature != null) { CaorenCupUtils.PrintToChat(player, $"=== {targetFeature.FeatureName} 详细状态 ==="); player.PrintToChat(targetFeature.GetStatusInfo()); }
            else { CaorenCupUtils.PrintToChat(player, "未找到该模块。"); }
            return;
        }
        CaorenCupUtils.PrintToChat(player, "=== 全局状态 (/status <模块> 详情) ===");
        string hpCapState = Config.HpCap.Enabled
            ? $"HpCap: {ChatColors.Green}启用{ChatColors.Default} | 范围:{Config.HpCap.Min}-{Config.HpCap.Max}"
            : $"HpCap: {ChatColors.Red}已禁用{ChatColors.Default}";
        player.PrintToChat(hpCapState);
        foreach (var feature in _features) { string full = feature.GetStatusInfo(); player.PrintToChat(full.Split('|')[0]); }
    }

    [ConsoleCommand("css_reset_plu", "重置")]
    public void OnConsoleReset(CCSPlayerController? p, CommandInfo i) { PerformReset(); if (p == null) Console.WriteLine("[CaorenCup] 已重置。"); }
    private void OnCommandReset(CCSPlayerController? p, CommandInfo i)
    {
        if (p != null && !AdminManager.PlayerHasPermissions(p, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(p, "无权操作。");
            return;
        }
        if (p != null) PerformReset();
    }

    private void PerformReset()
    {
        foreach (var f in _features)
        {
            if (f is Features.SkillPointsFeature) continue;
            f.SetEnabled(false);
        }
        Config.HpCap.Enabled = false;
        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} 所有功能已重置为竞技状态。");
        SaveConfig(); // 重置后自动保存，防止重启后又变回去
    }


    private void OnCommandHpCap(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "无权操作。");
            return;
        }

        if (info.ArgCount == 1)
        {
            string state = Config.HpCap.Enabled
                ? $"已启用：模块回血最高 {Config.HpCap.Max} HP，模块扣血最低 {Config.HpCap.Min} HP"
                : "已禁用";

            if (player != null)
            {
                CaorenCupUtils.PrintToChat(player, $"当前 /hpcap 状态：{state}");
                CaorenCupUtils.PrintToChat(player, "用法: /hpcap <min> <max>，例如 /hpcap 1 150；/hpcap 0 可禁用。");
            }
            else
            {
                Console.WriteLine($"[CaorenCup] 当前 hpcap 状态：{state}");
            }
            return;
        }

        string firstArg = info.GetArg(1).ToLower();
        if (firstArg == "0" || firstArg == "off" || firstArg == "disable")
        {
            Config.HpCap.Enabled = false;
            SaveConfig();
            CaorenCupUtils.PrintToChatAll($" {ChatColors.Red}全局血量保护已禁用。{ChatColors.Default}");
            return;
        }

        if (info.ArgCount < 3)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "用法: /hpcap <min> <max>，例如 /hpcap 1 150。输入 /hpcap 0 禁用。");
            return;
        }

        if (!int.TryParse(info.GetArg(1), out int min) || !int.TryParse(info.GetArg(2), out int max))
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "min/max 必须是整数。");
            return;
        }

        if (min < 0 || max < 1 || max < min)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "参数非法：要求 min >= 0，max >= 1，且 max >= min。");
            return;
        }

        Config.HpCap.Enabled = true;
        Config.HpCap.Min = min;
        Config.HpCap.Max = max;
        SaveConfig();

        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}全局血量保护已启用：{ChatColors.Default}模块回血最高到 {ChatColors.Green}{max}{ChatColors.Default} HP，模块扣血最低到 {ChatColors.Green}{min}{ChatColors.Default} HP。");
    }

    private void OnCommandSave(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "无权操作。");
            return;
        }
        SaveConfig();
        if (player != null) CaorenCupUtils.PrintToChat(player, "配置已强制保存到插件目录。");
    }
}
