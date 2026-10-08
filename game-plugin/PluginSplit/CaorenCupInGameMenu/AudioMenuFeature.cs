using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private readonly Dictionary<string, int> _audioMenuPages = new();
    private readonly Dictionary<string, string[]> _audioMenuIds = new();
    private double _nextAudioRefresh;
    private bool HandleAdminAudioClick(CCSPlayerController player, string id)
    {
        if (id == "admin_audio_open") { SetAdminVoteView(player, AdminVoteView.Audio); return true; }
        if (_adminVoteViews.GetValueOrDefault(player.SteamID.ToString()) != AdminVoteView.Audio) return false;
        if (!IsVoteAdministrator(player)) return true;
        var service = CaorenCupAudioAccess.TryGet();
        if (service == null) { Chat(player.SteamID.ToString(), "音频服务暂不可用。"); return true; }
        var sid = player.SteamID.ToString();
        if (id is "audio_prev" or "audio_next")
        {
            _audioMenuPages[sid] = Math.Max(0, _audioMenuPages.GetValueOrDefault(sid) + (id == "audio_next" ? 1 : -1));
            RenderAudioMenu(player); return true;
        }
        if (id.StartsWith("audio_item_", StringComparison.Ordinal) && int.TryParse(id.AsSpan(11), out var slot))
        {
            var ids = _audioMenuIds.GetValueOrDefault(sid) ?? [];
            if (slot >= 0 && slot < ids.Length)
            {
                var result = service.Play(ids[slot]); Chat(sid, result.Message);
                RenderAudioMenu(player);
            }
            return true;
        }
        var parts = id.Split('_');
        if (parts.Length != 3 || parts[0] != "audio") return false;
        var channel = parts[1] == "music" ? CaorenAudioChannel.Music : parts[1] == "broadcast" ? CaorenAudioChannel.Broadcast : CaorenAudioChannel.Effect;
        if (channel == CaorenAudioChannel.Effect) return true;
        CaorenAudioControl? operation = parts[2] switch { "pause" => CaorenAudioControl.Pause,
            "resume" => CaorenAudioControl.Resume, "stop" => CaorenAudioControl.Stop, "loop" => CaorenAudioControl.LoopOn, _ => null };
        if (parts[2] == "loop" && service is ICaorenCupAudioStatus status
            && status.GetChannels().FirstOrDefault(s => s.Channel == channel)?.LoopEnabled == true) operation = CaorenAudioControl.LoopOff;
        if (operation != null) Chat(sid, service.Control(channel, operation.Value).Message);
        RenderAudioMenu(player);
        return true;
    }
    private void RefreshAudioMenus()
    {
        if (VoteNow < _nextAudioRefresh) return;
        _nextAudioRefresh = VoteNow + 0.25;
        foreach (var player in VoteHumans())
            if (_adminVoteViews.GetValueOrDefault(player.SteamID.ToString()) == AdminVoteView.Audio && IsVoteAdministrator(player)) RenderAudioMenu(player);
    }
    private void RenderAudioMenu(CCSPlayerController player)
    {
        var service = CaorenCupAudioAccess.TryGet();
        var states = (service as ICaorenCupAudioStatus)?.GetChannels() ?? [];
        foreach (var (channel, prefix) in new[] { (CaorenAudioChannel.Music, "music"), (CaorenAudioChannel.Broadcast, "broadcast") })
        {
            var state = states.FirstOrDefault(s => s.Channel == channel);
            var text = state?.State switch { "Playing" => "播放中", "Paused" => "已暂停", "Ended" => "已结束", "Stopped" => "已停止", _ => "未播放" };
            _surface.SetVariable(player, "audio_" + prefix + "_state", state?.EventId == null ? text
                : $"{state.DisplayName} · {text} · {state.PositionSeconds:0.0}/{state.DurationSeconds:0.0} 秒 · {state.Recipients} 人");
            _surface.SetVariable(player, "audio_" + prefix + "_loop_label", state?.LoopEnabled == true ? "关闭循环" : "开启循环");
            _surface.SetVariable(player, "audio_" + prefix + "_cycle", state?.FinishingCycle == true ? "当前轮播完后停止" : "");
        }
        var entries = (service as ICaorenCupAudioStatus)?.Events.OrderBy(e => e.Channel).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray() ?? [];
        var sid = player.SteamID.ToString(); var count = Math.Max(1, (entries.Length + 5) / 6);
        var page = Math.Clamp(_audioMenuPages.GetValueOrDefault(sid), 0, count - 1); _audioMenuPages[sid] = page;
        var visible = entries.Skip(page * 6).Take(6).ToArray(); _audioMenuIds[sid] = visible.Select(e => e.Id).ToArray();
        _surface.SetVariable(player, "audio_library_page", $"{page + 1}/{count} 页 · {entries.Length} 项 · 播放会替换同通道当前音频");
        _surface.SetClass(player, "audio_prev_host", "Hidden", page == 0);
        _surface.SetClass(player, "audio_next_host", "Hidden", page == count - 1);
        for (var i = 0; i < 6; i++)
        {
            var item = i < visible.Length ? visible[i] : null;
            var channel = item?.Channel switch { CaorenAudioChannel.Music => "音乐", CaorenAudioChannel.Broadcast => "广播", _ => "短提示" };
            _surface.SetVariable(player, "audio_name" + i, item == null ? "" : $"{item.DisplayName} · {channel}");
            _surface.SetClass(player, "audio_item_" + i + "_host", "Hidden", item == null);
        }
    }
}
