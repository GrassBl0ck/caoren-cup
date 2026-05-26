using System.Runtime;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace CaorenCup;

public class CaorenCupConfig : BasePluginConfig
{
    // --- ?? 1: Bomb Quiz ---
    [JsonPropertyName("BombQuiz")]
    public BombQuizSettings BombQuiz { get; set; } = new BombQuizSettings();//bq

    // --- ?? 2: Fire Heal ---
    [JsonPropertyName("FireHeal")]
    public FireHealSettings FireHeal { get; set; } = new();//fireheal

    // --- ?? 3: FOV ---
    [JsonPropertyName("FOV")]
    public FOVSettings FOV { get; set; } = new();

    // --- ?? 4: Skill Points ---
    [JsonPropertyName("SkillPoints")]
    public SkillPointsSettings SkillPoints { get; set; } = new SkillPointsSettings();

    // --- ?? 5: Kill Heal ---
    [JsonPropertyName("KillHeal")]
    public KillHealSettings KillHeal { get; set; } = new KillHealSettings();

    // --- ?? 6: Simple HP ---
    [JsonPropertyName("SimpleHp")]
    public SimpleHpSettings SimpleHp { get; set; } = new();

    // --- ?? 7: Bleed (?????????) ---
    [JsonPropertyName("Bleed")]
    public BleedSettings Bleed { get; set; } = new();//bleed
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
    public PresetSettings Preset { get; set; } = new PresetSettings();

    [JsonPropertyName("HpCap")]
    public HpCapSettings HpCap { get; set; } = new HpCapSettings();
}

// ==========================================
// ???????????????
// ==========================================
public class HpCapSettings
{
    public bool Enabled { get; set; } = false;
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 100;
}

public class PresetSettings
{
    public bool Enabled { get; set; } = true;
    public string PresetFileName { get; set; } = "presets.grass.json";
    public string DefaultPlayablePermission { get; set; } = "@css/changemap";
    public string DefaultRestrictedPermission { get; set; } = "@css/root";
    public bool ApplyResetBeforePreset { get; set; } = true;
    public int MaxCommandsPerPreset { get; set; } = 200;
    public bool LogExecutedCommands { get; set; } = false;
}

public class PlaySoundSettings
{
    public bool Enabled { get; set; } = true;

    // ??????????????????? (?????)
    public string DefaultPrefix { get; set; } = "test_res_music/";

    // ?????????????????? JSON ????
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "win", "music/custom/victory_song" },
        { "lose", "music/custom/fail_song" },
        { "bell", "training/bell_normal" } // ????????????
    };
}
public class EcoGuessSettings
{
    public bool Enabled { get; set; } = false;
}
public class BombQuizSettings
{
    public bool Enabled { get; set; } = false;
    public int QuizType { get; set; } = 1; // ?????? 123 ???????
    public float OverrideTime { get; set; } = 0f;
    public float CtDelay { get; set; } = 0f;
    public float SurvivalTimeoutSeconds { get; set; } = 45f;
    public int TDrainDamage { get; set; } = 1;
}
public class FireHealSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Scale { get; set; } = -1.0f; // 0=??, 1=??, <0=??, >0=????
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
    public string AutoPauseCommand { get; set; } = "mp_pause_match"; // ??????????
}
public class KillHealSettings
{
    public bool Enabled { get; set; } = false;
    public int TargetMode { get; set; } = 0; // 0=All, 1=T, 2=CT, 3=VIP
    public int HealAmount { get; set; } = 25; // ?????
    public int MaxHealth { get; set; } = 100; // ???????? /kh ??? HpCap ????
    public int MinHealth { get; set; } = 1; // ???????? /kh ??? HpCap ????
    public string VipFlag { get; set; } = "@css/vip";
    public bool ShowMessage { get; set; } = true;
}


public class SimpleHpSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public bool MustBeDead { get; set; } = true; // 1=???????, 0=?????

    // ?????? (?????????? JSON ??)
    public bool ShowId { get; set; } = true;
    public bool ShowHits { get; set; } = true;
    public bool ShowDmg { get; set; } = true;
    public bool ShowHp { get; set; } = true;
    public bool ShowKilled { get; set; } = true;
}

// === ??? Bleed ??? ===
public class BleedSettings
{
    public bool Enabled { get; set; } = false;
    public string TargetTeam { get; set; } = "all"; // t, ct, all
    public float Interval { get; set; } = 1.0f;
    public int Amount { get; set; } = -1; // ?????????
    public int Limit { get; set; } = 1;   // ???????? /bleed ??? HpCap ?????
    public bool PlaySound { get; set; } = true; // ?????????
}

public class PlayerStatsSettings
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("EnableConsoleOutput")] public bool EnableConsoleOutput { get; set; } = true;
    [JsonPropertyName("UseDynamicEconomy")] public bool UseDynamicEconomy { get; set; } = true;

    // ???? (??????????)
    [JsonPropertyName("Weight_Kill")] public float WeightKill { get; set; } = 0.20f;
    [JsonPropertyName("Weight_Survival")] public float WeightSurvival { get; set; } = 0.15f;
    [JsonPropertyName("Weight_Damage")] public float WeightDamage { get; set; } = 0.15f;
    [JsonPropertyName("Weight_KAST")] public float WeightKAST { get; set; } = 0.15f;
    [JsonPropertyName("Weight_Impact")] public float WeightImpact { get; set; } = 0.35f;

    // ???
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
    public float Duration { get; set; } = -1.0f; // -1??????
    public int HealthChangePerSecond { get; set; } = 0; // ???????????

    // ???????
    public float BaseRadius { get; set; } = 144.0f;
    public bool PlaySound { get; set; } = true;
}
public class MoneySettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Multiplier { get; set; } = 1.0f; // ????
    public bool EnableRoundBonus { get; set; } = false; // ????????
}
public class DamageSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Multiplier { get; set; } = 1.0f; // ????
    public int Cap { get; set; } = 0; // ?????????? (0????)
    public float CapWindow { get; set; } = 1.0f; // ????(?)
}
public class C4DefuseSettings
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;

    // ????????? (?? 10.0s)
    [JsonPropertyName("TimeNoKit")] public float TimeNoKit { get; set; } = 10.0f;

    // ????????? (?? 5.0s)
    [JsonPropertyName("TimeWithKit")] public float TimeWithKit { get; set; } = 5.0f;
}
public class DeafenSettings
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;

    // 0=??, 1=??, 2=T, 3=CT
    [JsonPropertyName("TargetTeam")] public int TargetTeam { get; set; } = 0;
}
public class GrenadeDropSettings
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;

    // 0=??, 1=??, 2=T, 3=CT
    [JsonPropertyName("TargetTeam")] public int TargetTeam { get; set; } = 0;

    // ???? (?)
    [JsonPropertyName("Interval")] public float Interval { get; set; } = 10.0f;

    // ????: hegrenade, molotov, flashbang, smokegrenade, decoy, random
    [JsonPropertyName("GrenadeType")] public string GrenadeType { get; set; } = "hegrenade";
}
public class FriendlyFireSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Multiplier { get; set; } = 0.33f; // ?????????????0??
    public bool AllowKill { get; set; } = true;    // true=???, false=?1???
}

public class AliasSettings
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    // ?????????????????
    // ?????????????? ""??????????????????
    [JsonPropertyName("Permission")]
    public string Permission { get; set; } = "@css/changemap";

    // ????
    // key ????????????? /?!?css_?
    // value ???????????????? /?!?. ????????
    // ??????? /p1 -> ???????? mp_pause_match
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
    public float RestartDelay { get; set; } = 7.0f; // ?????????????
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
    public bool Enabled { get; set; } = false; // ???????????
    public string Target { get; set; } = "all"; // ??: "all", "t", "ct"
    public int MaxJumps { get; set; } = 2; // ??????
    public float Velocity { get; set; } = 300.0f; // ???????
    public bool AllowInstantJump { get; set; } = false; // ????????????? (false?????????)
}
public class EspSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // ???????: t, ct, all
    public int MaxRange { get; set; } = 5000;
    public int Mode { get; set; } = 0; // 0=????, 1=??????

    // ????????????
    public bool DisableGlowOnGOTV { get; set; } = true;
    public string GlowColorCT { get; set; } = "0, 190, 255, 255";
    public string GlowColorT { get; set; } = "243, 0, 93, 255";
}
public class IncDmgSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // ??: t, ct, all
    public float Rate { get; set; } = 0.01f;    // ??????????????
}
public class OneHpSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public int Mode { get; set; } = 1;          // ??: 1=??, 2=??
    public float Arg1 { get; set; } = 0f;       // ?1: ???? / ????
    public float Arg2 { get; set; } = 3f;       // ?2: ???? / ????
    public float Arg3 { get; set; } = 100f;     // ?3: ????
    public float Arg4 { get; set; } = 2f;       // ?4: ??????
}
public class AmmoSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float BulletChance { get; set; } = 50f;  // ??????? (0-100)
    public float GrenadeChance { get; set; } = 30f; // ??????? (0-100)
}
public class MagicSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // t, ct, all
    public float Radius { get; set; } = 40f;    // ???? (?? 20~80)
    public int Damage { get; set; } = 25;       // ???????????
}
public class KbSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all"; // ?????????: t, ct, all
    public float Horizontal { get; set; } = 400f; // ???????
    public float Vertical { get; set; } = 250f;   // ??????
    public bool Friendly { get; set; } = false;   // ?????(???)??
    public float Multiplier { get; set; } = 2.0f; // ??????
}
public class AuraSettings
{
    public bool Enabled { get; set; } = false;
    public string Target { get; set; } = "all";
    public bool AllowKb { get; set; } = false;
    public float DecayRate { get; set; } = 0.5f;
    // ??????????????
    public int MinDamage { get; set; } = 15;
}
