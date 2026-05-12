// -----------------------------------------------------------------------------
// Third-party notice
// -----------------------------------------------------------------------------
// This module contains code, logic, or implementation ideas adapted from:
//
//   fidarit/cs2-DoubleJump
//
// Original author:
//   fidarit
//
// License:
//   MIT License
//
// Related notice files:
//   THIRD_PARTY_NOTICES.md
//   licenses/cs2-DoubleJump-LICENSE.txt
//
// The original copyright and license notice are preserved according to the
// MIT License. CaorenCup sincerely thanks the original author for their work.
// -----------------------------------------------------------------------------
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;

namespace CaorenCup.Features
{
    // 用于记录每个玩家状态的内部类（替代原 UserInfo）
    internal class PlayerJumpState
    {
        public PlayerButtons PrevButtons { get; set; }
        public PlayerFlags PrevFlags { get; set; }
        public int JumpsCount { get; set; }
        // 保留玩家个人关闭二段跳的功能，可通过其他途径触发，默认开启
        public bool PersonalEnabled { get; set; } = true;
    }

    public class DoubleJumpFeature : ICaorenFeature
    {
        public string FeatureName => "DoubleJump";

        private CaorenCupPlugin _plugin = null!;
        private CaorenCupConfig _config = null!;
        private DoubleJumpSettings _settings => _config.DoubleJump;

        // 使用 Slot 数组代替 Dictionary 极大提升 OnTick 性能
        private readonly PlayerJumpState[] _playerStates = new PlayerJumpState[65];

        public void Init(CaorenCupPlugin plugin)
        {
            _plugin = plugin;

            // 初始化状态数组
            for (int i = 0; i < _playerStates.Length; i++)
            {
                _playerStates[i] = new PlayerJumpState();
            }

            // 注册 OnTick 监听
            _plugin.RegisterListener<Listeners.OnTick>(OnTick);
            // 注册玩家断开连接清理状态
            _plugin.RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
            {
                var player = @event.Userid;
                if (player != null && player.IsValid)
                {
                    _playerStates[player.Slot] = new PlayerJumpState();
                }
                return HookResult.Continue;
            });

            // 注册多端集成指令
            _plugin.AddCommand("css_dj", "设置二段跳 [0关闭] 或 [t/ct/all] [跳跃次数] [向上推力]", Command_DJ);
        }

        public void OnConfigParsed(CaorenCupConfig config)
        {
            _config = config;
        }

        public void OnUnload()
        {
            // 如果有 Timer 需要在这里清理，本模块无 Timer
        }

        public void SetEnabled(bool enabled)
        {
            _settings.Enabled = enabled;
            _plugin.SaveConfig();
        }

        public string GetHelpEntry()
        {
            return "css_dj (或/dj) - 配置二段跳模块";
        }

        public string GetStatusInfo()
        {
            if (!_settings.Enabled) return "DoubleJump: \x02已禁用\x01";
            return $"DoubleJump: \x04已开启\x01 (目标:{_settings.Target.ToUpper()}, 次数:{_settings.MaxJumps}, 力度:{_settings.Velocity})";
        }
        public string? GetPublicConfigInfo()
        {
            if (!_settings.Enabled) return null;
            string targetStr = _settings.Target == "all" ? "所有人" : (_settings.Target == "t" ? "T阵营" : "CT阵营");
            return $"二段跳已开启 ({targetStr})，最大支持 {_settings.MaxJumps} 段跳。";
        }

        public string GetFeatureDescription()
        {
            return "允许玩家在空中进行额外的跳跃。输入 css_dj 可以调整对应队伍的跳跃次数和跳跃高度。";
        }

        // ==================== 指令系统 ====================
        private void Command_DJ(CCSPlayerController? player, CommandInfo info)
        {
            if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
            {
                CaorenCupUtils.PrintToChat(player, "你没有权限使用此指令。");
                return;
            }

            // 1. 检查是否是关闭指令 (css_dj 0)
            if (info.ArgCount >= 2 && info.GetArg(1) == "0")
            {
                SetEnabled(false);
                string msg = "DoubleJump: \x02已禁用\x01";
                if (player != null) CaorenCupUtils.PrintToChat(player, msg);
                else info.ReplyToCommand($"[草人杯] {msg}");
                return;
            }

            // 2. 帮助菜单展示 (完全匹配截图排版)
            if (info.ArgCount < 3)
            {
                if (player != null)
                {
                    CaorenCupUtils.PrintToChat(player, "=== DoubleJump 指令说明 ===");
                    CaorenCupUtils.PrintToChat(player, " \x04/dj 0\x01 : 一键禁用");
                    CaorenCupUtils.PrintToChat(player, " \x04/dj <t/ct/all> <跳跃次数> <高度力度> <上升期起跳(true/false)>\x01");
                    CaorenCupUtils.PrintToChat(player, "示例: /dj t 2 300 true (T阵营2段跳，力度300，允许上升期连跳)");
                    CaorenCupUtils.PrintToChat(player, "示例: /dj all 3 250 false (全员3段跳，力度250，必须下落才能连跳)");
                    CaorenCupUtils.PrintToChat(player, $"当前状态: {GetStatusInfo()}");
                }
                else
                {
                    // 控制台输出备用格式
                    info.ReplyToCommand("=== DoubleJump 指令说明 ===");
                    info.ReplyToCommand(" css_dj 0 : 一键禁用");
                    info.ReplyToCommand(" css_dj <t/ct/all> <跳跃次数> <高度力度> <上升期起跳(true/false)>");
                    info.ReplyToCommand("示例: css_dj t 2 300 true (T阵营2段跳，力度300，允许上升期连跳)");
                    info.ReplyToCommand($"当前状态: {GetStatusInfo().Replace("\x04", "").Replace("\x02", "").Replace("\x01", "")}"); // 控制台去除颜色代码
                }
                return;
            }

            // 3. 解析集成指令 (代码保持不变)
            string target = info.GetArg(1).ToLower();
            if (target != "all" && target != "t" && target != "ct")
            {
                target = "all";
            }

            if (!int.TryParse(info.GetArg(2), out int jumps)) jumps = 2;

            float velocity = 300.0f;
            if (info.ArgCount >= 4) float.TryParse(info.GetArg(3), out velocity);

            bool allowInstant = false;
            if (info.ArgCount >= 5) bool.TryParse(info.GetArg(4), out allowInstant);

            // 4. 修改配置并保存 (代码保持不变)
            _settings.Enabled = true;
            _settings.Target = target;
            _settings.MaxJumps = jumps;
            _settings.Velocity = velocity;
            _settings.AllowInstantJump = allowInstant;

            _plugin.SaveConfig();

            string reply = $"DoubleJump: \x04已更新\x01! 目标:\x04{target.ToUpper()}\x01 次数:\x04{jumps}\x01 力度:\x04{velocity}\x01";
            if (player != null) CaorenCupUtils.PrintToChat(player, reply);
            else info.ReplyToCommand($"[草人杯] {reply}");
        }

        // ==================== 核心逻辑 ====================
        private void OnTick()
        {
            if (!_settings.Enabled || _settings.MaxJumps <= 1)
                return;

            foreach (var player in Utilities.GetPlayers())
            {
                // 校验玩家有效性 (替代 PlayerControllerEx.IsValid)
                if (player == null || !player.IsValid || player.IsBot || !player.PawnIsAlive)
                    continue;

                // 校验目标队伍
                var team = player.TeamNum; // 1=Spec, 2=T, 3=CT
                if (_settings.Target == "t" && team != 2) continue;
                if (_settings.Target == "ct" && team != 3) continue;

                ProcessPlayerJump(player);
            }
        }

        private void ProcessPlayerJump(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null) return;

            var state = _playerStates[player.Slot];
            if (!state.PersonalEnabled) return;

            var currentFlags = (PlayerFlags)playerPawn.Flags;
            var currentButtons = player.Buttons;

            var wasGrounded = (state.PrevFlags & PlayerFlags.FL_ONGROUND) != 0;
            var isGrounded = (currentFlags & PlayerFlags.FL_ONGROUND) != 0;

            var jumpWasPressed = (state.PrevButtons & PlayerButtons.Jump) != 0;
            var jumpIsPressed = (currentButtons & PlayerButtons.Jump) != 0;

            // 地面状态重置跳跃次数
            if (isGrounded)
                state.JumpsCount = 0;
            else if (state.JumpsCount < 1)
                state.JumpsCount = 1; // 走到边缘掉下去算作第1次跳跃

            // 触发二段跳条件判定
            if (!jumpWasPressed && jumpIsPressed &&
                !wasGrounded && !isGrounded &&
                state.JumpsCount < _settings.MaxJumps)
            {
                state.JumpsCount++;

                // 判断是否允许上升期跳跃，或者当前 Z 轴速度小于 0（正在下落）
                if (_settings.AllowInstantJump || playerPawn.AbsVelocity.Z < 0)
                {
                    // 施加推力 (替代 PlayerPawnEx.ForceJump)
                    playerPawn.AbsVelocity.Z = _settings.Velocity;
                }
            }

            state.PrevFlags = currentFlags;
            state.PrevButtons = currentButtons;
        }
    }
}
