using CaorenCup.Contracts;

namespace CaorenDuel;

/// <summary>
/// 把现有游戏内 Duel 运行时接入统一 Gamemode 选择器。
/// Duel 的具体回合、枪械、暂停和地图逻辑仍由 CaorenDuelPlugin 负责。
/// </summary>
public sealed class DuelGameModeProvider : ICaorenCupGameModeProvider
{
    public GameModeDescriptor Descriptor { get; } = new(
        "duel",
        "Duel（单挑）",
        "CaorenDuel",
        Array.Empty<string>(),
        null,
        15,
        Array.Empty<GameModeCvarDefinition>(),
        ConfigFileName: "default_duel.cfg");

    public GameModeResult ValidateStart(GameModeStartRequest request, GameModeRuntimeContext context) =>
        GameModeResult.Ok("Duel 使用现有 CaorenDuel 运行时，地图和玩家由 /duel 流程校验。");

    public GameModeResult OnWarmupStarted(GameModeRuntimeContext context) =>
        GameModeResult.Ok("Duel Warmup 已启动，等待统一倒计时结束。");

    public GameModeResult OnLiveStarted(GameModeRuntimeContext context)
    {
        // 15 秒倒计时结束后，复用 CaorenDuel 原有的玩家、队伍、回合和 CVar 逻辑。
        context.ExecuteServerCommand("css_duel start");
        return GameModeResult.Ok("Duel 已交给 CaorenDuel 运行时启动。");
    }

    public GameModeResult OnStopped(GameModeRuntimeContext context)
    {
        context.ExecuteServerCommand("css_duel stop");
        return GameModeResult.Ok("Duel 已停止。");
    }
}
