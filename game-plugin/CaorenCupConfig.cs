using System.Runtime;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace CaorenCup;

public class CaorenCupConfig : BasePluginConfig
{
    // --- 模块 1: Bomb Quiz ---
    [JsonPropertyName("BombQuiz")]
    public BombQuizSettings BombQuiz { get; set; } = new BombQuizSettings();//bq

    // --- 模块 2: Fire Heal ---
    [JsonPropertyName("FireHeal")]
    public FireHealSettings FireHeal { get; set; } = new();//fireheal

    // --- 模块 3: FOV ---
    [JsonPropertyName("FOV")]
    public FOVSettings FOV { get; set; } = new();

    // --- 模块 4: Skill Points ---
    [JsonPropertyName("SkillPoints")]
    public SkillPointsSettings SkillPoints { get; set; } = new SkillPointsSettings();

    // --- 模块 5: Kill Heal ---
    [JsonPropertyName("KillHeal")]
    public KillHealSettings KillHeal { get; set; } = new KillHealSettings();

    // --- 模块 6: Simple HP ---
    [JsonPropertyName("SimpleHp")]
    public SimpleHpSettings SimpleHp { get; set; } = new();

    // --- 模块 7: Bleed (刚才报错就是缺这个) ---
    [JsonPropertyName("Bleed")]
    public BleedSettings Bleed { get; set; } = new();//bleed
    [JsonPropertyName("OneManArmy")]
    public OMASettings OneManArmy { get; set; } = new OMASettings();
    [JsonPropertyName("PlayerStats")]
    public PlayerStatsSettings PlayerStats { get; set; } = new PlayerStatsSettings();
    [JsonPropertyName("Smoke")]
    public SmokeSettings Smoke { get; set; } = new();
    [JsonPropertyName("Money")]
    public MoneySettings Money { get; set; } = new();

    [JsonPropertyName("Damage")]
    public DamageSettings Damage { get; set; } = new();
    [JsonPropertyName("C4Defuse")]
    public C4DefuseSettings C4Defuse { get; set; } = new C4DefuseSettings();
    [JsonPropertyName("Deafen")]
    public DeafenSettings Deafen { get; set; } = new DeafenSettings();
    [JsonPropertyName("GrenadeDrop")]
    public GrenadeDropSettings GrenadeDrop { get; set; } = new GrenadeDropSettings();
    [JsonPropertyName("FriendlyFire")]
    public FriendlyFireSettings FriendlyFire { get; set; } = new();//ffire
    [JsonPropertyName("Alias")]
    public AliasSettings Alias { get; set; } = new AliasSettings();
    [JsonPropertyName("FRE")]
    public ForceRoundEndSettings ForceRoundEnd { get; set; } = new ForceRoundEndSettings();
    public TaggingControlSettings TaggingControl { get; set; } = new TaggingControlSettings();
    public DoubleJumpSettings DoubleJump { get; set; } = new DoubleJumpSettings();
    public EspSettings Esp { get; set; } = new EspSettings();
    public IncDmgSettings IncDmg { get; set; } = new IncDmgSettings();
    public OneHpSettings OneHp { get; set; } = new OneHpSettings();
    public AmmoSettings Ammo { get; set; } = new AmmoSettings();
    public MagicSettings Magic { get; set; } = new MagicSettings();
    public KbSettings Kb { get; set; } = new KbSettings();
    public AuraSettings Aura { get; set; } = new AuraSettings();
    public EcoGuessSettings EcoGuess { get; set; } = new EcoGuessSettings();
    public PlaySoundSettings PlaySound { get; set; } = new PlaySoundSettings();
    public RadarColorSettings RadarColor { get; set; } = new RadarColorSettings();

    [JsonPropertyName("HpCap")]
    public HpCapSettings HpCap { get; set; } = new HpCapSettings();
}

// ==========================================
// 下面是各个模块的具体设置类定义
// ==========================================
public class HpCapSettings
{
    public bool Enabled { get; set; } = false;
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 100;
}

public class RadarColorSettings
{
    public bool Enabled { get; set; } = true;
    public bool ApplyOnRoundStart { get; set; } = true;
    public bool ApplyOnSpawn { get; set; } = true;
}

public class PlaySoundSettings
{
    public bool Enabled { get; set; } = true;

    // 如果你只输入文件名，会自动加上这个前缀 (留空则不加)
    public string DefaultPrefix { get; set; } = "test_res_music/";

    // 超级别名库，忽略大小写。你可以随时在 JSON 里加更多
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "win", "music/custom/victory_song" },
        { "lose", "music/custom/fail_song" },
        { "bell", "training/bell_normal" } // 甚至可以映射游戏自带音效
    };
}
public class EcoGuessSettings
{
    public bool Enabled { get; set; } = false;
}
public class BombQuizSettings
{
    public bool Enabled { get; set; } = false;
    public int QuizType { get; set; } = 1; // 现在允许填入 123 这样的组合数字
    public float OverrideTime { get; set; } = 0f;
    public float CtDelay { get; set; } = 0f;
    public float SurvivalTimeoutSeconds { get; set; } = 45f;
    public int TDrainDamage { get; set; } = 1;
}
public class FireHealSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Scale { get; set; } = -1.0f; // 0=免疫, 1=正常, <0=回血, >0=额外伤害
}

public class FOVSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public int FovValue { get; set; } = 90; // 30 - 150
}

public class SkillPointsSettings
{
    public bool Enabled { get; set; } = false;
    public string AutoPauseCommand { get; set; } = "mp_pause_match"; // 自动暂停指令，可留空
}
public class KillHealSettings
{
    public bool Enabled { get; set; } = false;
    public int TargetMode { get; set; } = 0; // 0=All, 1=T, 2=CT, 3=VIP
    public int HealAmount { get; set; } = 25; // 允许为负数
    public int MaxHealth { get; set; } = 100; // 兼容旧配置，当前 /kh 已改用 HpCap 控制上限
    public int MinHealth { get; set; } = 1; // 兼容旧配置，当前 /kh 已改用 HpCap 控制下限
    public string VipFlag { get; set; } = "@css/vip";
    public bool ShowMessage { get; set; } = true;
}


public class SimpleHpSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public bool MustBeDead { get; set; } = true; // 1=必须死亡才能查, 0=活着也能查

    // 显示细节配置 (无需指令修改，直接改 JSON 即可)
    public bool ShowId { get; set; } = true;
    public bool ShowHits { get; set; } = true;
    public bool ShowDmg { get; set; } = true;
    public bool ShowHp { get; set; } = true;
    public bool ShowKilled { get; set; } = true;
}

// === 新增的 Bleed 设置类 ===
public class BleedSettings
{
    public bool Enabled { get; set; } = false;
    public string TargetTeam { get; set; } = "all"; // t, ct, all
    public float Interval { get; set; } = 1.0f;
    public int Amount { get; set; } = -1; // 正为回血，负为扣血
    public int Limit { get; set; } = 1;   // 兼容旧配置，当前 /bleed 已改用 HpCap 控制上下限
    public bool PlaySound { get; set; } = true; // 保留原有的音效开关
}

public class OMASettings
{
    // Solo (独勇者) 属性
    [JsonPropertyName("SoloHealth")] public int SoloHealth { get; set; } = 500;
    [JsonPropertyName("SoloArmor")] public int SoloArmor { get; set; } = 200;
    [JsonPropertyName("SoloSpeed")] public float SoloSpeed { get; set; } = 1.2f;
    [JsonPropertyName("SoloDamage")] public float SoloDamage { get; set; } = 2.0f; // 造成伤害倍率
    [JsonPropertyName("SoloHeal")] public int SoloHeal { get; set; } = 50; // 击杀回血
    [JsonPropertyName("InfiniteAmmo")] public bool InfiniteAmmo { get; set; } = true;

    // Wallhack (雷达透视)
    [JsonPropertyName("WallhackEnabled")] public bool WallhackEnabled { get; set; } = false;
    [JsonPropertyName("WallhackDuration")] public float WallhackDuration { get; set; } = 3.0f;
    [JsonPropertyName("WallhackInterval")] public float WallhackInterval { get; set; } = 10.0f;

    // Team (挑战者团队) 属性
    [JsonPropertyName("TeamHealth")] public int TeamHealth { get; set; } = 100;
    [JsonPropertyName("TeamSpeed")] public float TeamSpeed { get; set; } = 1.0f;
    [JsonPropertyName("BanTeamAWP")] public bool BanTeamAWP { get; set; } = false;
    [JsonPropertyName("BanTeamHelm")] public bool BanTeamHelm { get; set; } = false;

    // Game (游戏规则)
    [JsonPropertyName("C4TimerSoloCT")] public int C4TimerSoloCT { get; set; } = 60;
    [JsonPropertyName("C4TimerDefault")] public int C4TimerDefault { get; set; } = 40;
    [JsonPropertyName("AutoSwap")] public bool AutoSwap { get; set; } = true;

    // 当前开启状态 (0=关, 2=T Solo, 3=CT Solo)
    // 注意：这个状态不需要持久化保存，每次重启默认为关即可，防止重启后直接进入变态模式
    // 但为了能在 Config 里改默认值，还是加上吧
    [JsonPropertyName("DefaultMode")] public int DefaultMode { get; set; } = 0;
}
public class PlayerStatsSettings
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("EnableConsoleOutput")] public bool EnableConsoleOutput { get; set; } = true;
    [JsonPropertyName("UseDynamicEconomy")] public bool UseDynamicEconomy { get; set; } = true;

    // 权重配置 (默认值保留原插件设定)
    [JsonPropertyName("Weight_Kill")] public float WeightKill { get; set; } = 0.20f;
    [JsonPropertyName("Weight_Survival")] public float WeightSurvival { get; set; } = 0.15f;
    [JsonPropertyName("Weight_Damage")] public float WeightDamage { get; set; } = 0.15f;
    [JsonPropertyName("Weight_KAST")] public float WeightKAST { get; set; } = 0.15f;
    [JsonPropertyName("Weight_Impact")] public float WeightImpact { get; set; } = 0.35f;

    // 基准线
    [JsonPropertyName("Baseline_KPR")] public float BaselineKPR { get; set; } = 0.70f;
    [JsonPropertyName("Baseline_Survival")] public float BaselineSurvival { get; set; } = 0.35f;
    [JsonPropertyName("Baseline_ADR")] public float BaselineADR { get; set; } = 78.0f;
    [JsonPropertyName("Baseline_KAST")] public float BaselineKAST { get; set; } = 0.72f;
    [JsonPropertyName("Baseline_Swing")] public float BaselineSwing { get; set; } = 0.03f;
}
public class SmokeSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Duration { get; set; } = -1.0f; // -1表示默认时长
    public int HealthChangePerSecond { get; set; } = 0; // 正数为回血，负数为扣血

    // 隐藏的固定配置
    public float BaseRadius { get; set; } = 144.0f;
    public bool PlaySound { get; set; } = true;
}
public class MoneySettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Multiplier { get; set; } = 1.0f; // 经济倍数
    public bool EnableRoundBonus { get; set; } = false; // 是否包含回合奖励
}
public class DamageSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Multiplier { get; set; } = 1.0f; // 易伤倍率
    public int Cap { get; set; } = 0; // 时间窗口内的伤害上限 (0为不限制)
    public float CapWindow { get; set; } = 1.0f; // 时间窗口(秒)
}
public class C4DefuseSettings
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;

    // 无钳子时的拆弹时间 (默认 10.0s)
    [JsonPropertyName("TimeNoKit")] public float TimeNoKit { get; set; } = 10.0f;

    // 有钳子时的拆弹时间 (默认 5.0s)
    [JsonPropertyName("TimeWithKit")] public float TimeWithKit { get; set; } = 5.0f;
}
public class DeafenSettings
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;

    // 0=关闭, 1=全员, 2=T, 3=CT
    [JsonPropertyName("TargetTeam")] public int TargetTeam { get; set; } = 0;
}
public class GrenadeDropSettings
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;

    // 0=关闭, 1=全员, 2=T, 3=CT
    [JsonPropertyName("TargetTeam")] public int TargetTeam { get; set; } = 0;

    // 生成间隔 (秒)
    [JsonPropertyName("Interval")] public float Interval { get; set; } = 10.0f;

    // 掉落类型: hegrenade, molotov, flashbang, smokegrenade, decoy, random
    [JsonPropertyName("GrenadeType")] public string GrenadeType { get; set; } = "hegrenade";
}
public class FriendlyFireSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Multiplier { get; set; } = 0.33f; // 倍数，正数伤害，负数回血，0无伤
    public bool AllowKill { get; set; } = true;    // true=可打死, false=留1血保护
}

public class AliasSettings
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    // 默认只有具备换图权限的管理员可用。
    // 如果希望普通玩家也能用，改成 ""，但只建议暴露绝对安全的控制台命令。
    [JsonPropertyName("Permission")]
    public string Permission { get; set; } = "@css/changemap";

    // 映射表：
    // key 是聊天栏输入的别名，不要写 /、!、css_。
    // value 是服务器控制台可执行命令，不要写 /、!、. 这种聊天触发符。
    // 示例：聊天输入 /p1 -> 服务器控制台执行 mp_pause_match
    [JsonPropertyName("CommandMap")]
    public Dictionary<string, string> CommandMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "p1", "mp_pause_match" },
        { "un", "mp_unpause_match" }
    };
}
public class ForceRoundEndSettings
{
    public bool Enabled { get; set; } = true;
    public float RestartDelay { get; set; } = 7.0f; // 结束回合后几秒开始下一回合
}
public class TaggingControlSettings
{
    public bool Enabled { get; set; } = false;
    public bool CustomT { get; set; } = false;
    public float ValueT { get; set; } = 1.0f;
    public bool CustomCT { get; set; } = false;
    public float ValueCT { get; set; } = 1.0f;
}
public class DoubleJumpSettings
{
    public bool Enabled { get; set; } = false; // 默认关闭，通过指令开启
    public string Target { get; set; } = "all"; // 可选: "all", "t", "ct"
    public int MaxJumps { get; set; } = 2; // 最大跳跃次数
    public float Velocity { get; set; } = 300.0f; // 二段跳向上推力
    public bool AllowInstantJump { get; set; } = false; // 是否允许在上升期直接二段跳 (false则必须等下落才能跳)
}
public class EspSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // 可见发光的阵营: t, ct, all
    public int MaxRange { get; set; } = 5000;
    public int Mode { get; set; } = 0; // 0=持续透视, 1=准星指着才透

    // 原插件的其他必要保留设置
    public bool DisableGlowOnGOTV { get; set; } = true;
    public string GlowColorCT { get; set; } = "0, 190, 255, 255";
    public string GlowColorT { get; set; } = "243, 0, 93, 255";
}
public class IncDmgSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // 可选: t, ct, all
    public float Rate { get; set; } = 0.01f;    // 基础倍率，正为增伤，负为减伤
}
public class OneHpSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public int Mode { get; set; } = 1;          // 模式: 1=转生, 2=自爆
    public float Arg1 { get; set; } = 0f;       // 值1: 转生位置 / 爆炸伤害
    public float Arg2 { get; set; } = 3f;       // 值2: 转生延迟 / 爆炸半径
    public float Arg3 { get; set; } = 100f;     // 值3: 转生血量
    public float Arg4 { get; set; } = 2f;       // 值4: 转生无敌时间
}
public class AmmoSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float BulletChance { get; set; } = 50f;  // 不消耗子弹概率 (0-100)
    public float GrenadeChance { get; set; } = 30f; // 不消耗道具概率 (0-100)
}
public class MagicSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Radius { get; set; } = 40f;    // 吸附半径 (建议 20~80)
    public int Damage { get; set; } = 25;       // 每次磁性吸附造成的伤害
}
public class KbSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // 拥有击退子弹的阵营: t, ct, all
    public float Horizontal { get; set; } = 400f; // 基础水平击退力
    public float Vertical { get; set; } = 250f;   // 基础垂直升力
    public bool Friendly { get; set; } = false;   // 是否对己方(含自己)生效
    public float Multiplier { get; set; } = 2.0f; // 伤害换算倍数
}
public class AuraSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all";
    public bool AllowKb { get; set; } = false;
    public float DecayRate { get; set; } = 0.5f;
    // 新增：剑气溃散的最低伤害阈值
    public int MinDamage { get; set; } = 15;
}
