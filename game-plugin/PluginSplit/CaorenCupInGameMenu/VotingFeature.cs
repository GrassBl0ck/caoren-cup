using System.Diagnostics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private enum AdminVoteView { Home, Management, Preview, CancelConfirmation, HelpList, HelpDetail, Settings, Audio }
    private sealed record VotePageSession(Guid? VoteId, bool ResultOnly);
    private readonly VoteSystem _votes = new();
    private IMenuHudSurface _voteSurface = null!;
    private readonly Dictionary<string, VoteDraft> _voteDrafts = new();
    private readonly Dictionary<string, AdminVoteView> _adminVoteViews = new();
    private readonly Dictionary<string, Guid> _cancelVoteIds = new();
    private readonly Dictionary<string, VotePageSession> _votePages = new();
    private readonly Dictionary<string, string> _noticeSignatures = new();
    private Guid? _publishedVoteResult;
    private double _nextVoteRefresh;
    private static double VoteNow => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>仅输出结构化结果，当前没有注册任何禁言、踢人、换图等操作。</summary>
    public event Action<VoteResult>? VoteCompleted;

    private void RegisterVotingHooks()
    {
        _voteSurface = new CustomHudMenuSurface("panorama/layout/custom_game/caoren_vote.xml");
        RegisterPersonalMenu();
        AddCommand("css_crcvote", "打开投票页；/crcvote A 投票；/crcvote result 查看结果", OnVoteCommand);
        AddCommand("css_cv", "打开投票页；/cv A 投票；/cv result 查看结果", OnVoteCommand);
        AddCommand("css_crcvote_admin", "管理员投票管理：create/start/cancel/discard", OnAdminVoteCommand);
        AddCommand("css_crcmenu_back", "返回当前菜单上一级，根页面返回游戏", OnMenuBackCommand);
        AddCommand("css_crcmenu_exit", "关闭自己的草人杯菜单并恢复游戏操作", OnMenuExitCommand);
        // 使用两个不同委托，避免 CSS 按委托记录监听器时覆盖 say 的卸载记录。
        AddCommandListener("say", OnVoteDraftSay, HookMode.Pre);
        AddCommandListener("say_team", OnVoteDraftSayTeam, HookMode.Pre);
        RegisterListener<Listeners.OnTick>(VoteTick);
        RegisterListener<Listeners.OnTick>(RefreshAudioMenus);
        RegisterListener<Listeners.OnMapEnd>(() => CloseAllMenus(VoteEndReason.MapChanged));
    }

    private static bool IsVoteAdministrator(CCSPlayerController player) => AdminManager.PlayerHasPermissions(player, "@css/root");
    private static bool IsHuman(CCSPlayerController player) => player.IsValid && !player.IsBot && !player.IsHLTV && player.SteamID != 0;
    private static List<CCSPlayerController> VoteHumans() => Utilities.GetPlayers().Where(IsHuman).ToList();
    private bool IsVoteDraftAwaitingInput(string steamId) => _voteDrafts.TryGetValue(steamId, out var draft) && !draft.Ready;

    private void OnAdminVoteCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !IsHuman(player)) return;
        if (!_enabled || !IsVoteAdministrator(player)) { Chat(player.SteamID.ToString(), "投票管理需要 @css/root 权限，且菜单必须启用。"); return; }
        switch ((info.ArgCount > 1 ? info.GetArg(1) : string.Empty).ToLowerInvariant())
        {
            case "create": BeginVoteDraft(player); break;
            case "start": StartDraftVote(player); break;
            case "cancel": CancelCurrentVote(player, null); break;
            case "discard": _voteDrafts.Remove(player.SteamID.ToString()); Chat(player.SteamID.ToString(), "已丢弃自己的投票草稿。"); break;
            default:
                EnsureAdminMenu(player);
                SetAdminVoteView(player, AdminVoteView.Management);
                break;
        }
    }

    private void EnsureAdminMenu(CCSPlayerController player)
    {
        if (IsVoteDraftAwaitingInput(player.SteamID.ToString()))
        {
            Chat(player.SteamID.ToString(), "正在填写草稿，请先完成，或输入 cancel／/crcmenu_back 取消创建。");
            return;
        }
        if (!_navigator.HasSession(player.SteamID.ToString())) OpenMenu(player);
    }

    private bool HandleAdminVotingClick(CCSPlayerController player, string buttonId)
    {
        var sid = player.SteamID.ToString();
        var view = _adminVoteViews.GetValueOrDefault(sid);
        switch (buttonId)
        {
            case "admin_exit": ExitOwnMenus(player); return true;
            case "admin_back": GoMenuBack(player); return true;
            case "admin_home_tab": SetAdminVoteView(player, AdminVoteView.Home); return true;
            case "admin_vote_open":
                if (view == AdminVoteView.Home) SetAdminVoteView(player, AdminVoteView.Management);
                return true;
            case "vote_create":
                if (view == AdminVoteView.Management) BeginVoteDraft(player);
                return true;
            case "vote_discard":
                if (view == AdminVoteView.Preview) { _voteDrafts.Remove(sid); SetAdminVoteView(player, AdminVoteView.Management); }
                return true;
            case "vote_start":
                if (_adminVoteViews.GetValueOrDefault(sid) == AdminVoteView.Preview) StartDraftVote(player);
                return true;
            case "vote_cancel":
                if (view != AdminVoteView.Management) return true;
                _votes.Advance(VoteNow); ProcessVoteResult();
                if (_votes.Active is { } active)
                {
                    _cancelVoteIds[sid] = active.Id;
                    SetAdminVoteView(player, AdminVoteView.CancelConfirmation);
                }
                else Chat(sid, "当前没有进行中的投票。");
                return true;
            case "vote_cancel_confirm":
                if (_adminVoteViews.GetValueOrDefault(sid) == AdminVoteView.CancelConfirmation
                    && _cancelVoteIds.TryGetValue(sid, out var expected)) CancelCurrentVote(player, expected);
                return true;
            default: return false;
        }
    }

    private void SetAdminVoteView(CCSPlayerController player, AdminVoteView view)
    {
        var sid = player.SteamID.ToString();
        if (!_navigator.HasSession(sid) || !IsVoteAdministrator(player)) return;
        _adminVoteViews[sid] = view;
        _surface.SetClass(player, "admin_home", "Hidden", view != AdminVoteView.Home);
        _surface.SetClass(player, "admin_vote_management", "Hidden", view != AdminVoteView.Management);
        _surface.SetClass(player, "admin_vote_preview", "Hidden", view != AdminVoteView.Preview);
        _surface.SetClass(player, "admin_vote_cancel", "Hidden", view != AdminVoteView.CancelConfirmation);
        _surface.SetClass(player, "admin_help_list", "Hidden", view != AdminVoteView.HelpList);
        _surface.SetClass(player, "admin_help_detail", "Hidden", view != AdminVoteView.HelpDetail);
        _surface.SetClass(player, "admin_settings", "Hidden", view != AdminVoteView.Settings);
        _surface.SetClass(player, "admin_audio", "Hidden", view != AdminVoteView.Audio);
        _surface.SetVariable(player, "navsection", view is AdminVoteView.HelpList or AdminVoteView.HelpDetail ? "指令帮助" : view == AdminVoteView.Home ? "快捷指令" : view == AdminVoteView.Settings ? "个人设置" : "投票");
        _surface.SetVariable(player, "subsection", view switch { AdminVoteView.Home => "广播", AdminVoteView.Preview => "确认发起", AdminVoteView.CancelConfirmation => "确认取消", AdminVoteView.HelpList => "全部指令", AdminVoteView.HelpDetail => "指令详情", AdminVoteView.Settings => "外观与声音", AdminVoteView.Audio => "音乐与广播", _ => "管理" });
        _surface.SetClass(player, "admin_home_tab", "CaorenTabSelected", view is not (AdminVoteView.HelpList or AdminVoteView.HelpDetail or AdminVoteView.Settings));
        _surface.SetClass(player, "admin_help_open_tab", "CaorenTabSelected", view is AdminVoteView.HelpList or AdminVoteView.HelpDetail);
        _surface.SetClass(player, "admin_settings_open", "CaorenTabSelected", view == AdminVoteView.Settings);
        _surface.SetClass(player, "admin_back", "Hidden", view == AdminVoteView.Home);
        RenderAdminVoting(player);
        RenderCommandHelp(player);
        if (view == AdminVoteView.Settings) RenderPersonalSettings(player, _surface);
        if (view == AdminVoteView.Audio) RenderAudioMenu(player);
    }

    private void RenderAdminVoting(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString();
        var now = VoteNow;
        var active = _votes.Active;
        var state = active != null ? $"投票进行中 · 剩余 {Math.Max(0, (int)Math.Ceiling(active.Deadline - now))} 秒"
            : now < _votes.ResultVisibleUntil ? $"结果展示中 · {Math.Ceiling(_votes.ResultVisibleUntil - now)} 秒后可发起下一轮"
            : "当前没有进行中的投票";
        _surface.SetVariable(player, "vote_admin_state", state);
        _surface.SetVariable(player, "vote_admin_question", active?.Question ?? _votes.LastResult?.Question ?? "");
        _surface.SetVariable(player, "vote_cancel_question", active?.Question ?? "本轮投票已经结束");
        _surface.SetClass(player, "vote_cancel", "Hidden", active == null);
        var draft = _voteDrafts.GetValueOrDefault(sid);
        _surface.SetVariable(player, "vote_create_label", draft?.Ready == true ? "查看自己的草稿" : "创建投票草稿");
        _surface.SetVariable(player, "vote_draft_question", draft?.Question ?? "暂无草稿");
        for (var i = 0; i < 4; i++)
        {
            var text = draft != null && i < draft.Options.Count ? $"{(char)('A' + i)} · {draft.Options[i]}" : "";
            _surface.SetVariable(player, "vote_draft_option" + i, text);
            _surface.SetClass(player, "vote_draft_option" + i, "Hidden", text.Length == 0);
        }
    }

    private void BeginVoteDraft(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString();
        _votes.Advance(VoteNow); ProcessVoteResult();
        var status = _votes.GetStartStatus(VoteNow);
        if (status != VoteStartStatus.Started) { Chat(sid, StartStatusText(status)); return; }
        if (_voteDrafts.TryGetValue(sid, out var existing) && existing.Ready)
        {
            EnsureAdminMenu(player); SetAdminVoteView(player, AdminVoteView.Preview); return;
        }
        _voteDrafts[sid] = new VoteDraft();
        ClosePlayerMenu(player);
        CloseVotePage(player);
        Chat(sid, _voteDrafts[sid].Prompt);
    }

    private HookResult OnVoteDraftSay(CCSPlayerController? player, CommandInfo info) => HandleVoteDraftChat(player, info);
    private HookResult OnVoteDraftSayTeam(CCSPlayerController? player, CommandInfo info) => HandleVoteDraftChat(player, info);
    private HookResult HandleVoteDraftChat(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !IsHuman(player)) return HookResult.Continue;
        var sid = player.SteamID.ToString();
        if (!_voteDrafts.TryGetValue(sid, out var draft) || draft.Ready) return HookResult.Continue;
        var text = info.GetArg(1).Trim();
        // 聊天命令仍走自己的命令处理；普通文本在发送前拦截，草稿不广播。
        if (text.StartsWith('/') || text.StartsWith('!')) return HookResult.Continue;
        if (!_enabled || !IsVoteAdministrator(player))
        {
            _voteDrafts.Remove(sid); Chat(sid, "权限已变化，创建已取消。"); return HookResult.Handled;
        }
        if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            _voteDrafts.Remove(sid); Chat(sid, "已取消创建投票。"); return HookResult.Handled;
        }
        if (!draft.Accept(text, out var error)) { Chat(sid, error); Chat(sid, draft.Prompt); return HookResult.Handled; }
        if (draft.Ready)
        {
            Chat(sid, $"草稿：{draft.Question}；" + string.Join("；", draft.Options.Select((option, i) => $"{(char)('A' + i)}={option}")));
            Chat(sid, "请在预览页确认发起；聊天备用命令：/crcvote_admin start。");
            EnsureAdminMenu(player); SetAdminVoteView(player, AdminVoteView.Preview);
        }
        else Chat(sid, draft.Prompt);
        return HookResult.Handled;
    }

    private void StartDraftVote(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString();
        if (!IsVoteAdministrator(player) || !_enabled) { Chat(sid, "无权限发起投票。"); return; }
        if (!_voteDrafts.TryGetValue(sid, out var draft) || !draft.Ready) { Chat(sid, "请先创建完整的投票草稿。"); return; }
        _votes.Advance(VoteNow); ProcessVoteResult();
        var eligible = VoteHumans().Where(p => !IsVoteAdministrator(p)).Select(p => p.SteamID.ToString()).ToArray();
        var status = _votes.TryStart(draft.Question, draft.Options, eligible, VoteNow);
        if (status != VoteStartStatus.Started) { Chat(sid, StartStatusText(status)); return; }
        _voteDrafts.Remove(sid);
        CloseAllVotePages();
        _voteSurface.RemoveEntity(); // 每轮创建新布局实体，拒收旧布局的迟到点击。
        _noticeSignatures.Clear();
        foreach (var human in VoteHumans())
        {
            RenderVoteNotice(human, true);
            if (_votes.IsEligible(human.SteamID.ToString()) && !IsVoteAdministrator(human))
            {
                // 原生事件仅发给本玩家，不依赖该玩家是否已安装菜单 VPK。
                var audio = CaorenCup.Contracts.CaorenCupAudioAccess.Play("vote.started", [human]);
                if (!audio.Success) Logger.LogWarning("Vote prompt sound failed: {Message}", audio.Message);
                Chat(human.SteamID.ToString(), "新投票已开始，30 秒内输入 /crcvote 打开，或输入 /crcvote A（B/C/D）投票。");
            }
        }
        EnsureAdminMenu(player); SetAdminVoteView(player, AdminVoteView.Management);
    }

    private static string StartStatusText(VoteStartStatus status) => status switch
    {
        VoteStartStatus.AlreadyRunning => "当前已有投票，请等待它结束。",
        VoteStartStatus.ResultStillDisplayed => "上一轮结果仍在展示，请等待 10 秒展示结束。",
        VoteStartStatus.NoEligiblePlayers => "没有可投票的非管理员真人玩家，未发起投票。",
        VoteStartStatus.InvalidDefinition => "投票必须有问题和 2～4 个不同的非空选项。",
        _ => "投票已发起。",
    };

    private void CancelCurrentVote(CCSPlayerController player, Guid? expectedId)
    {
        var sid = player.SteamID.ToString();
        _votes.Advance(VoteNow); ProcessVoteResult();
        if (expectedId != null && _votes.Active?.Id != expectedId) { Chat(sid, "确认页对应的投票已经结束或更换，没有取消其他投票。"); }
        else if (!_votes.Cancel(VoteEndReason.AdministratorCancelled, VoteNow)) Chat(sid, "当前没有进行中的投票。");
        else ProcessVoteResult();
        _cancelVoteIds.Remove(sid);
        SetAdminVoteView(player, AdminVoteView.Management);
    }

    private void OnVoteCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !IsHuman(player)) return;
        if (!_enabled) { Chat(player.SteamID.ToString(), "草人杯菜单当前禁用。"); return; }
        _votes.Advance(VoteNow); ProcessVoteResult();
        var arg = info.ArgCount > 1 ? info.GetArg(1).Trim() : string.Empty;
        if (arg.Length == 0) { OpenVotePage(player, false); return; }
        if (arg.Equals("result", StringComparison.OrdinalIgnoreCase)) { OpenVotePage(player, true); return; }
        if (_votes.Active is not { } active) { Chat(player.SteamID.ToString(), "当前没有进行中的投票；/crcvote result 可查看上一轮结果。"); return; }
        CastOwnVote(player, active.Id, arg);
    }

    private void OpenVotePage(CCSPlayerController player, bool resultOnly, bool fromPersonal = false)
    {
        if (IsVoteDraftAwaitingInput(player.SteamID.ToString()))
        {
            Chat(player.SteamID.ToString(), "正在填写草稿，请先完成，或输入 cancel／/crcmenu_back 取消创建。");
            return;
        }
        ClosePersonalMenu(player, fromPersonal);
        ClosePlayerMenu(player);
        var sid = player.SteamID.ToString();
        if (fromPersonal) _voteFromPersonalMenu.Add(sid); else _voteFromPersonalMenu.Remove(sid);
        _voteSurface.EnsureEntity();
        if (!_voteSurface.IsAlive) { Chat(sid, "投票界面不可用，可用 /crcvote A（B/C/D）提交投票。"); return; }
        _votePages[sid] = new VotePageSession(resultOnly ? null : _votes.Active?.Id, resultOnly);
        _voteSurface.SetClass(player, "vote_player_tabs", "Hidden", !fromPersonal);
        _voteSurface.SetClass(player, "vote_back", "Hidden", !fromPersonal);
        _voteSurface.SetClass(player, "vote_standalone_header", "Hidden", fromPersonal);
        RenderVotePage(player);
        _voteSurface.SetClass(player, "vote_page", "Hidden", false);
        _voteSurface.SetInputCapture(player, true);
        ApplyPersonalAppearance(player, _voteSurface, "vote_page");
    }

    private void RenderVotePage(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString();
        if (!_votePages.TryGetValue(sid, out var session)) return;
        var active = !session.ResultOnly && session.VoteId == _votes.Active?.Id ? _votes.Active : null;
        var result = active == null ? _votes.LastResult : null;
        _voteSurface.SetVariable(player, "vote_page_title", "CaorenCup · 玩家投票");
        _voteSurface.SetVariable(player, "vote_question", active?.Question ?? result?.Question ?? "当前没有投票");
        var choice = active != null ? _votes.GetChoice(sid) : _votes.GetLastChoice(sid);
        var canVote = active != null && _votes.IsEligible(sid) && !IsVoteAdministrator(player) && choice == null;
        var status = active == null ? result != null ? ResultHeadline(result) : "管理员发起投票后，这里会显示选项。"
            : IsVoteAdministrator(player) ? "管理员可以管理和查看结果，不参与投票。"
            : !_votes.IsEligible(sid) ? "你不在本轮开始时的资格名单中。"
            : choice != null ? $"已投给 {choice}，提交后不能修改。" : "请选择一项；每人一票，提交后不能修改。";
        _voteSurface.SetVariable(player, "vote_page_status", status);
        for (var i = 0; i < 4; i++)
        {
            var option = active != null && i < active.Options.Count ? active.Options[i] : null;
            var counted = result != null && i < result.Options.Count ? result.Options[i] : null;
            var text = option != null ? $"{option.Id} · {option.Text}" : counted != null ? $"{counted.Id} · {counted.Text}\u00A0\u00A0\u00A0\u00A0{counted.Votes} 票" : "";
            _voteSurface.SetVariable(player, "vote_option" + i, text);
            _voteSurface.SetVariable(player, "vote_hint" + i, canVote ? "投给 " + (char)('A' + i) : choice != null ? "已投票，不能修改" : "仅查看");
            _voteSurface.SetClass(player, "vote_option_" + i, "Hidden", text.Length == 0);
            _voteSurface.SetClass(player, "vote_option_" + i, "NotVoteable", !canVote || option == null);
            _voteSurface.SetClass(player, "vote_option_" + i, "MyVote", choice == ((char)('A' + i)).ToString());
        }
    }

    private void HandleVoteHudClick(CCSPlayerController player, string buttonId)
    {
        var sid = player.SteamID.ToString();
        if (!_votePages.TryGetValue(sid, out var session)) return;
        if (buttonId == "vote_exit") { ExitOwnMenus(player); return; }
        if (buttonId == "vote_back") { ReturnFromVotePage(player); return; }
        if (_voteFromPersonalMenu.Contains(sid) && buttonId is "player_commands_open" or "player_settings_open" or "player_guide_open")
        {
            ReturnFromVotePage(player);
            HandlePersonalClick(player, buttonId);
            return;
        }
        if (!_enabled || session.ResultOnly || session.VoteId == null) return;
        const string prefix = "vote_option_";
        if (buttonId.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(buttonId.AsSpan(prefix.Length), out var index) && index is >= 0 and < 4)
            CastOwnVote(player, session.VoteId.Value, ((char)('A' + index)).ToString());
    }

    private void CastOwnVote(CCSPlayerController player, Guid voteId, string option)
    {
        var sid = player.SteamID.ToString();
        var status = _votes.Cast(voteId, sid, option, VoteNow, IsVoteAdministrator(player));
        ProcessVoteResult();
        Chat(sid, status switch
        {
            VoteCastStatus.Accepted => $"已投给 {option.Trim().ToUpperInvariant()}。",
            VoteCastStatus.AlreadyVoted => "你已经投过票，不能重复提交或改票。",
            VoteCastStatus.Administrator => "管理员不参加投票。",
            VoteCastStatus.NotEligible => "你不在本轮投票资格名单中。",
            VoteCastStatus.InvalidOption => "该选项不存在，请选择本轮的 A/B/C/D 选项。",
            _ => "本轮投票已经结束或更换，没有接收这张票。",
        });
        if (_votePages.ContainsKey(sid)) RenderVotePage(player);
    }

    private void VoteTick()
    {
        if (!_enabled) return;
        var now = VoteNow;
        if (now < _nextVoteRefresh) return;
        _nextVoteRefresh = now + 0.25;
        _votes.Advance(now); ProcessVoteResult();
        if (_votes.Active == null && now >= _votes.ResultVisibleUntil && _votePages.Count == 0 && _noticeSignatures.Count == 0) return;
        foreach (var player in VoteHumans())
        {
            RenderVoteNotice(player, false);
            if (_votePages.ContainsKey(player.SteamID.ToString())) RenderVotePage(player);
            if (_adminVoteViews.GetValueOrDefault(player.SteamID.ToString()) != AdminVoteView.Home)
            {
                if (IsVoteAdministrator(player)) RenderAdminVoting(player);
                else ClosePlayerMenu(player);
            }
        }
        if (_votes.Active == null && now >= _votes.ResultVisibleUntil) _noticeSignatures.Clear();
    }

    private void ProcessVoteResult()
    {
        if (_votes.LastResult is not { } result || _publishedVoteResult == result.VoteId) return;
        _publishedVoteResult = result.VoteId;
        CloseAllVotePages();
        _voteSurface.RemoveEntity();
        _noticeSignatures.Clear();
        foreach (var player in VoteHumans())
        {
            RenderVoteNotice(player, true);
            Chat(player.SteamID.ToString(), ResultHeadline(result) + "；/crcvote result 可查看完整结果。");
        }
        RaiseVoteCompleted(result);
    }

    private void RaiseVoteCompleted(VoteResult result)
    {
        if (VoteCompleted == null) return;
        foreach (Action<VoteResult> listener in VoteCompleted.GetInvocationList())
        {
            try { listener(result); }
            catch (Exception error) { Logger.LogError(error, "Vote result subscriber failed for {VoteId}", result.VoteId); }
        }
    }

    private void RenderVoteNotice(CCSPlayerController player, bool force)
    {
        var now = VoteNow;
        var sid = player.SteamID.ToString();
        var active = _votes.Active;
        var result = active == null && now < _votes.ResultVisibleUntil ? _votes.LastResult : null;
        if (active == null && result == null)
        {
            if (_noticeSignatures.Remove(sid)) _voteSurface.SetClass(player, "vote_notice", "Hidden", true);
            return;
        }
        var remaining = active != null ? Math.Max(0, (int)Math.Ceiling(active.Deadline - now)) : 0;
        var own = active != null ? _votes.GetChoice(sid) : _votes.GetLastChoice(sid);
        var eligible = active != null && _votes.IsEligible(sid) && !IsVoteAdministrator(player);
        var signature = $"{active?.Id}/{result?.VoteId}/{remaining}/{own}/{eligible}";
        if (!force && _noticeSignatures.GetValueOrDefault(sid) == signature) return;
        _voteSurface.EnsureEntity();
        if (!_voteSurface.IsAlive) return;
        _noticeSignatures[sid] = signature;
        _voteSurface.SetVariable(player, "notice_title", active != null ? $"投票进行中 · {remaining} 秒" : "投票结束");
        _voteSurface.SetVariable(player, "notice_question", active?.Question ?? result!.Question);
        _voteSurface.SetVariable(player, "notice_options", active != null
            ? string.Join("\n", active.Options.Select(option => $"{option.Id} · {option.Text}"))
            : string.Join("\n", result!.Options.Select(option => $"{option.Id} · {option.Text}")));
        _voteSurface.SetVariable(player, "notice_status", active != null ? own != null ? $"已投给 {own}" : eligible ? "/cv 打开投票，或 /cv A" : "本轮仅查看，不能投票" : ResultHeadline(result!));
        _voteSurface.SetVariable(player, "notice_counts", result == null ? "" : string.Join("　", result.Options.Select(option => $"{option.Id}: {option.Votes} 票")));
        _voteSurface.SetVariable(player, "notice_participation", result == null ? "" : $"参与人数：{result.ParticipatedPlayerCount}/{result.EligiblePlayerCount}");
        _voteSurface.SetClass(player, "vote_notice", "VoteWon", result?.Outcome == VoteOutcome.Winner);
        _voteSurface.SetClass(player, "vote_notice", "Hidden", false);
    }

    private static string ResultHeadline(VoteResult result) => result.Outcome switch
    {
        VoteOutcome.Winner => $"胜出：{result.WinnerOptionId} · {result.Options.First(option => option.Id == result.WinnerOptionId).Text}",
        VoteOutcome.Tied => "结果并列，无唯一胜出选项",
        VoteOutcome.NoVotes => "无人投票，本轮无效",
        _ => "投票已取消，无胜出选项",
    };

    private void CloseVotePage(CCSPlayerController player)
    {
        _votePages.Remove(player.SteamID.ToString());
        if (_voteSurface == null) return;
        _voteSurface.SetClass(player, "vote_page", "Hidden", true);
        _voteSurface.ReleaseInput(player);
    }

    private void CloseAllVotePages()
    {
        foreach (var player in VoteHumans()) CloseVotePage(player);
        _votePages.Clear();
    }

    private void OnMenuBackCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && IsHuman(player)) GoMenuBack(player);
    }
    private void OnMenuExitCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && IsHuman(player)) ExitOwnMenus(player);
    }
    private void GoMenuBack(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString();
        if (HandlePersonalBack(player)) return;
        if (_votePages.ContainsKey(sid)) { ReturnFromVotePage(player); return; }
        if (_voteDrafts.TryGetValue(sid, out var draft) && !draft.Ready)
        {
            _voteDrafts.Remove(sid); Chat(sid, "已取消创建投票。");
            if (IsVoteAdministrator(player) && _enabled) { EnsureAdminMenu(player); SetAdminVoteView(player, AdminVoteView.Management); }
            return;
        }
        if (!_navigator.HasSession(sid)) return;
        if (!IsVoteAdministrator(player)) { ClosePlayerMenu(player); return; }
        switch (_adminVoteViews.GetValueOrDefault(sid))
        {
            case AdminVoteView.HelpDetail: SetAdminVoteView(player, AdminVoteView.HelpList); break;
            case AdminVoteView.HelpList: SetAdminVoteView(player, AdminVoteView.Home); break;
            case AdminVoteView.Settings: SetAdminVoteView(player, AdminVoteView.Home); break;
            case AdminVoteView.Audio: SetAdminVoteView(player, AdminVoteView.Home); break;
            case AdminVoteView.Preview:
            case AdminVoteView.CancelConfirmation: SetAdminVoteView(player, AdminVoteView.Management); break;
            case AdminVoteView.Management: SetAdminVoteView(player, AdminVoteView.Home); break;
            default: ClosePlayerMenu(player); break;
        }
    }
    private void ExitOwnMenus(CCSPlayerController player)
    {
        _voteDrafts.Remove(player.SteamID.ToString());
        _cancelVoteIds.Remove(player.SteamID.ToString());
        _voteFromPersonalMenu.Remove(player.SteamID.ToString());
        ClosePlayerMenu(player); CloseVotePage(player);
        ClosePersonalMenu(player);
    }

    private void OnVotingPlayerDisconnected(string steamId)
    {
        foreach (var key in _appearanceValues.Keys.Where(key => key.SteamId == steamId).ToArray()) _appearanceValues.Remove(key);
        foreach (var key in _helpRenderedRows.Keys.Where(key => key.SteamId == steamId).ToArray()) _helpRenderedRows.Remove(key);
        // 资格和已投票留在状态机中，重连仍按相同 SteamID 识别。
        _voteDrafts.Remove(steamId); _votePages.Remove(steamId);
        ForgetCommandHelp(steamId);
        _personalViews.Remove(steamId);
        _personalPages.Remove(steamId); _personalDetails.Remove(steamId); _voteFromPersonalMenu.Remove(steamId);
        _adminVoteViews.Remove(steamId); _cancelVoteIds.Remove(steamId); _noticeSignatures.Remove(steamId);
    }

    private void ResetVoting(VoteEndReason reason)
    {
        _helpRenderedRows.Clear();
        var result = _votes.Reset(reason, VoteNow);
        CloseAllVotePages();
        _voteSurface?.RemoveEntity();
        _voteDrafts.Clear(); _adminVoteViews.Clear(); _cancelVoteIds.Clear(); _noticeSignatures.Clear();
        _helpListPages.Clear(); _helpDetails.Clear();
        _personalViews.Clear(); _personalPages.Clear(); _personalDetails.Clear(); _voteFromPersonalMenu.Clear(); _personalSurface?.RemoveEntity();
        _publishedVoteResult = null;
        if (result != null) RaiseVoteCompleted(result);
    }
}
