using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using System;
using System.Linq;
using CounterStrikeSharp.API.Modules.Events;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Features;

public class BombQuizFeature : ICaorenFeature
{
    public string FeatureName => "黑客攻防(动态下包测验)";

    private CaorenCupPlugin _plugin = null!;
    private BombQuizSettings _settings = null!;
    private readonly Random _rng = new();

    // 状态管理
    private bool _quizActive = false;
    private bool _survivalPhase = false;
    private string _currentAnswer = "";
    private int _quizId = 0;

    private Timer? _answerTimer;
    private Timer? _survivalTimer;
    private Timer? _drainTimer;

    // 常量：表示 CT 延迟采用动态计算模式
    private const float AUTO_CT_DELAY = -999f;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;
        _plugin.AddCommand("css_bq", "黑客攻防设置", OnCommandBq);

        _plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        _plugin.RegisterEventHandler<EventBombPlanted>(OnBombPlanted);
        _plugin.RegisterEventHandler<EventPlayerChat>(OnPlayerChat);
        _plugin.RegisterEventHandler<EventBombDefused>(OnBombDefused);
        _plugin.RegisterEventHandler<EventBombExploded>(OnBombExploded);
        _plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _settings = config.BombQuiz;
        // 如果旧配置是0，你可能也想让它强制变自动，但这里我们保留用户的真实配置，指令控制时再覆盖
    }

    public void OnUnload() => ClearAllState();

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        if (!enabled) ClearAllState();
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnCommandBq(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "无权限执行此指令。");
            return;
        }

        bool isChatCommand = info.CallingContext == CommandCallingContext.Chat;

        if (info.ArgCount == 1)
        {
            if (player != null)
            {
                if (isChatCommand)
                {
                    CaorenCupUtils.PrintToChat(player, $"\u0004当前状态: \u0001{GetStatusInfo()}");
                    CaorenCupUtils.PrintToChat(player, $"\u0004用法: \u0001/bq <题型组合/0> [强制秒数/0] [CT延迟秒数]");
                    CaorenCupUtils.PrintToChat(player, $"\u0004题型: \u00011(两位+-), 2(两位*一位), 3(三位+-), 4(除法), 5(大除法), 6(两位*十几), 7(一位+一位)");
                    CaorenCupUtils.PrintToChat(player, $"\u0004组合: \u0001输入 147 即在 1,4,7 中随机抽取");
                }
                else
                {
                    player.PrintToConsole("========== [草人杯] 黑客攻防 (BombQuiz) ==========");
                    player.PrintToConsole($"当前状态: {GetStatusInfo()}");
                    player.PrintToConsole("用法: css_bq <题型组合/0> [强制秒数/0] [CT延迟秒数]");
                    player.PrintToConsole("  输入 0 直接禁用此模块。省略后续参数将自动恢复动态平衡。");
                    player.PrintToConsole("  题型: 1(两位+-), 2(两位*一位), 3(三位+-), 4(除法), 5(大除法), 6(两位*十几), 7(一位+一位)");
                    player.PrintToConsole("  组合: 输入 147 即在 1, 4, 7 中随机抽取题目");
                    player.PrintToConsole("范例: css_bq 147 (秒数和延迟全部随题型动态变化)");
                    player.PrintToConsole("范例: css_bq 147 10 2 (强制所有题型10秒，CT强制定死延迟2秒)");
                    player.PrintToConsole("==================================================");
                }
            }
            return;
        }

        string arg1 = info.GetArg(1).ToLower();

        if (arg1 == "0" || arg1 == "off" || arg1 == "false")
        {
            SetEnabled(false);
            CaorenCupUtils.PrintToChatAll($"\u0002已禁用\u0001 黑客攻防模块。");
            return;
        }

        string validTypes = new string(arg1.Where(c => c >= '1' && c <= '7').ToArray());
        if (string.IsNullOrEmpty(validTypes)) validTypes = "1";

        int type = int.Parse(validTypes);

        // 核心修复点：不继承旧设置。如果不填写，默认为自动模式。
        float overTime = 0f;
        float ctDelay = AUTO_CT_DELAY;

        if (info.ArgCount >= 3 && float.TryParse(info.GetArg(2), out float parsedOverTime)) overTime = parsedOverTime;
        if (info.ArgCount >= 4 && float.TryParse(info.GetArg(3), out float parsedCtDelay)) ctDelay = parsedCtDelay;

        _settings.Enabled = true;
        _settings.QuizType = type;
        _settings.OverrideTime = overTime;
        _settings.CtDelay = ctDelay;
        _plugin.SaveConfig();

        string ctDelayStr = ctDelay == AUTO_CT_DELAY ? "动态公式(0.1*时间+0.4)" : $"{ctDelay}s";
        string overTimeStr = overTime > 0 ? overTime + "s" : "跟随题型自动";

        CaorenCupUtils.PrintToChatAll($"黑客攻防已更新 -> 题库: \u0004{type}\u0001 | 时间: \u0004{overTimeStr}\u0001 | CT延迟: \u0004{ctDelayStr}\u0001");
    }

    // --- 动态题库与默认时间生成器 ---
    private void GenerateQuestion(out string text, out string ans, out float defaultTime)
    {
        string typeStr = _settings.QuizType.ToString();
        char chosenChar = typeStr[_rng.Next(typeStr.Length)];
        int actualType = chosenChar - '0';

        int a, b;
        switch (actualType)
        {
            case 1:
                a = _rng.Next(10, 100); b = _rng.Next(10, 100);
                if (a < b) (a, b) = (b, a);
                if (_rng.Next(2) == 0) { text = $"{a} + {b}"; ans = (a + b).ToString(); }
                else { text = $"{a} - {b}"; ans = (a - b).ToString(); }
                defaultTime = 4f; break;
            case 2:
                a = _rng.Next(10, 100); b = _rng.Next(2, 10);
                text = $"{a} * {b}"; ans = (a * b).ToString();
                defaultTime = 7f; break;
            case 3:
                a = _rng.Next(100, 1000); b = _rng.Next(100, 1000);
                if (a < b) (a, b) = (b, a);
                if (_rng.Next(2) == 0) { text = $"{a} + {b}"; ans = (a + b).ToString(); }
                else { text = $"{a} - {b}"; ans = (a - b).ToString(); }
                defaultTime = 9f; break;
            case 4:
                a = _rng.Next(10, 100); b = _rng.Next(2, 10);
                text = $"{a} / {b} (保留整数)"; ans = (a / b).ToString();
                defaultTime = 5f; break;
            case 5:
                a = _rng.Next(100, 1000); b = _rng.Next(2, 10);
                text = $"{a} / {b} (保留整数)"; ans = (a / b).ToString();
                defaultTime = 9f; break;
            case 6:
                a = _rng.Next(10, 100); b = _rng.Next(11, 20);
                text = $"{a} * {b}"; ans = (a * b).ToString();
                defaultTime = 12f; break;
            case 7:
                a = _rng.Next(1, 10); b = _rng.Next(1, 10);
                text = $"{a} + {b}"; ans = (a + b).ToString();
                defaultTime = 2.25f; break;
            default:
                text = "1 + 1"; ans = "2"; defaultTime = 5f; break;
        }
    }

    // --- 核心事件逻辑 ---
    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        ClearAllState();
        return HookResult.Continue;
    }

    private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        ClearAllState();
        _quizActive = true;
        int currentQuiz = ++_quizId;

        GenerateQuestion(out string qText, out _currentAnswer, out float defaultTime);

        // 核心公式：先计算出最终使用的答题时间 baseTime
        float baseTime = _settings.OverrideTime > 0 ? _settings.OverrideTime : defaultTime;

        // 如果 CT 延迟处于自动模式，则根据当前的 baseTime 动态计算：0.1倍答题时间 + 0.4秒
        float actualCtDelay = _settings.CtDelay == AUTO_CT_DELAY ? (0.1f * baseTime + 0.4f) : _settings.CtDelay;

        float delayT = actualCtDelay < 0 ? Math.Abs(actualCtDelay) : 0f;
        float delayCT = actualCtDelay > 0 ? actualCtDelay : 0f;

        if (delayT == 0) SendPromptToTeam(CsTeam.Terrorist, qText, baseTime);
        else _plugin.AddTimer(delayT, () => { if (_quizActive && _quizId == currentQuiz) SendPromptToTeam(CsTeam.Terrorist, qText, baseTime - delayT); });

        if (delayCT == 0) SendPromptToTeam(CsTeam.CounterTerrorist, qText, baseTime);
        else _plugin.AddTimer(delayCT, () => { if (_quizActive && _quizId == currentQuiz) SendPromptToTeam(CsTeam.CounterTerrorist, qText, baseTime - delayCT); });

        _answerTimer = _plugin.AddTimer(baseTime, () =>
        {
            if (!_quizActive || _quizId != currentQuiz) return;
            FailCurrentPlant("答题超时", true);
        });

        return HookResult.Continue;
    }

    private void SendPromptToTeam(CsTeam team, string question, float remainingTime)
    {
        if (remainingTime <= 0) return;

        string prefix = team == CsTeam.Terrorist ? "\u0007[C4加密]" : "\u000C[C4警报]";
        string action = team == CsTeam.Terrorist ? "全队请立刻输入答案维持C4" : "全队请立刻输入答案拦截C4";

        foreach (var p in Utilities.GetPlayers())
        {
            if (p != null && p.IsValid && p.TeamNum == (byte)team)
            {
                CaorenCupUtils.PrintToChat(p, $"{prefix} \u0001{action}: \u0004{question} = ? \u0001(限时 \u0002{Math.Round(remainingTime, 2)}秒\u0001)");
            }
        }
    }

    private HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        if (!_settings.Enabled || !_quizActive) return HookResult.Continue;

        var speaker = Utilities.GetPlayerFromUserid(@event.Userid);
        if (speaker == null || !speaker.IsValid) return HookResult.Continue;

        string text = (@event.Text ?? string.Empty).Trim();

        if (text == _currentAnswer)
        {
            _quizActive = false;
            _answerTimer?.Kill();
            _answerTimer = null;

            if (speaker.TeamNum == (byte)CsTeam.Terrorist)
            {
                CaorenCupUtils.PrintToChatAll($"\u0007[C4系统] \u0001T方成员 \u0004{speaker.PlayerName} \u0001率先完成加密！C4起爆程序正常运行！");
            }
            else if (speaker.TeamNum == (byte)CsTeam.CounterTerrorist)
            {
                CaorenCupUtils.PrintToChatAll($"\u000C[C4警报] \u0001CT方成员 \u0004{speaker.PlayerName} \u0001率先破解代码！C4已瘫痪！");
                FailCurrentPlant("CT反向黑客入侵", false);
            }
        }
        return HookResult.Continue;
    }

    private void FailCurrentPlant(string reason, bool isTimeout)
    {
        _quizActive = false;
        _survivalPhase = true;

        Server.ExecuteCommand("ent_remove planted_c4");

        if (isTimeout) CaorenCupUtils.PrintToChatAll($"\u0002[C4系统] 双方均未在限时内完成破解！C4系统死机！");

        CaorenCupUtils.PrintToChatAll($"\u0004[生存战触发] \u0001由于 {reason}，C4已被移除！");
        CaorenCupUtils.PrintToChatAll($"\u0004[生存战触发] \u0001杀光对方！CT将在 \u0002{_settings.SurvivalTimeoutSeconds}\u0001 秒后强制获胜。");

        HandleCtRevival();
        StartTDrainTimer();
        StartSurvivalTimeoutTimer(_settings.SurvivalTimeoutSeconds);
    }

    private void HandleCtRevival()
    {
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || p.TeamNum != (int)CsTeam.CounterTerrorist) continue;
            if (!p.PawnIsAlive) p.Respawn();
            if (p.PawnIsAlive && p.PlayerPawn.Value != null)
            {
                var pawn = p.PlayerPawn.Value;
                CaorenCupUtils.ApplyModuleSetHealth(_plugin, pawn, 100);
            }
        }
    }

    private void StartTDrainTimer()
    {
        if (!_survivalPhase || _settings.TDrainDamage <= 0) return;
        _drainTimer = _plugin.AddTimer(1.0f, () =>
        {
            if (!_survivalPhase) { _drainTimer?.Kill(); return; }
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && p.PawnIsAlive && p.TeamNum == (int)CsTeam.Terrorist && p.PlayerPawn.Value != null)
                {
                    var pawn = p.PlayerPawn.Value;
                    int minHp = CaorenCupUtils.GetHpCapMin(_plugin, 1);
                    if (pawn.Health > minHp)
                    {
                        CaorenCupUtils.ApplyModuleHealth(_plugin, pawn, Math.Max(minHp, pawn.Health - _settings.TDrainDamage));
                    }
                }
            }
        }, TimerFlags.REPEAT);
    }

    private void StartSurvivalTimeoutTimer(float seconds)
    {
        if (!_survivalPhase) return;
        _survivalTimer = _plugin.AddTimer(seconds, () =>
        {
            if (!_survivalPhase) return;
            bool tAlive = Utilities.GetPlayers().Any(p => p.TeamNum == (int)CsTeam.Terrorist && p.PawnIsAlive);
            bool ctAlive = Utilities.GetPlayers().Any(p => p.TeamNum == (int)CsTeam.CounterTerrorist && p.PawnIsAlive);

            if (tAlive && ctAlive)
            {
                CaorenCupUtils.PrintToChatAll($"\u0002生存战超时！T方任务失败，判定 CT 获胜。");
                Server.ExecuteCommand("css_slay @t");
                ClearAllState();
            }
        });
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!_survivalPhase) return HookResult.Continue;
        bool tAlive = Utilities.GetPlayers().Any(p => p.TeamNum == (int)CsTeam.Terrorist && p.PawnIsAlive);
        bool ctAlive = Utilities.GetPlayers().Any(p => p.TeamNum == (int)CsTeam.CounterTerrorist && p.PawnIsAlive);

        if (!tAlive && ctAlive)
        {
            CaorenCupUtils.PrintToChatAll($"\u000CCT 肃清了敌人，赢得了生存战！");
            Server.ExecuteCommand("terminate_round 0 2");
            ClearAllState();
        }
        else if (!ctAlive && tAlive)
        {
            CaorenCupUtils.PrintToChatAll($"\u0007T 肃清了敌人，赢得了生存战！");
            Server.ExecuteCommand("terminate_round 0 3");
            ClearAllState();
        }
        return HookResult.Continue;
    }

    private HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info) { ClearAllState(); return HookResult.Continue; }
    private HookResult OnBombExploded(EventBombExploded @event, GameEventInfo info) { ClearAllState(); return HookResult.Continue; }

    private void ClearAllState()
    {
        _quizActive = false;
        _survivalPhase = false;
        _currentAnswer = "";
        _quizId++;

        _answerTimer?.Kill(); _answerTimer = null;
        _survivalTimer?.Kill(); _survivalTimer = null;
        _drainTimer?.Kill(); _drainTimer = null;
    }

    // --- 接口实现 ---
    public string GetHelpEntry() => "/bq - 控制黑客攻防(下包测验/生存战)";

    public string GetStatusInfo() => _settings.Enabled ? $"已开启 (题库:{_settings.QuizType} 时间:{(_settings.OverrideTime > 0 ? _settings.OverrideTime + "s" : "自动")} CT延迟:{(_settings.CtDelay == AUTO_CT_DELAY ? "自动" : _settings.CtDelay + "s")})" : "已禁用";

    public string? GetPublicConfigInfo() => _settings.Enabled ? $"黑客攻防: 植入C4后，全员将参与代码破译竞速" : null;

    public string GetFeatureDescription()
    {
        if (!_settings.Enabled) return "C4炸弹起爆逻辑目前一切正常。";

        string ctInfo = _settings.CtDelay == AUTO_CT_DELAY ? "CT方收到题目的延迟时间将根据题目的长短\u0004动态调整\u0001。" :
                        (_settings.CtDelay > 0 ? $"CT方会比T方晚 \u0004{_settings.CtDelay}\u0001 秒收到题目，处于劣势。" :
                        (_settings.CtDelay < 0 ? $"CT方会比T方提前 \u0004{Math.Abs(_settings.CtDelay)}\u0001 秒收到题目，占尽先机！" : "双方将同时收到题目！"));

        return $"【黑客密码战】T方安放C4后，全员聊天频道会弹出一道随机密码题！任何人均可无限次作答。\n" +
               $"- 若\u0004T方全队\u0001率先答对：C4成功加密，正常进行起爆倒计时。\n" +
               $"- 若\u0004CT方全队\u0001率先答对：C4被反向入侵失效，直接进入生存战大乱斗！\n" +
               $"- 若\u0004超时无人答对\u0001：系统死机，C4失效，进入生存战。\n{ctInfo}";
    }
}