// 仅显式探针构建包含；普通玩家只能自愿 join/leave，不获得管理权限。
using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.QOL.PlaySound;

public sealed partial class CaorenCupPlaySoundPlugin
{
    private readonly AudioProbeGroupEnrollment _audioGroup = new();
    private readonly Dictionary<ulong, GroupVoice> _audioGroupVoices = new();
    private readonly Dictionary<uint, GroupVoice> _audioGroupSources = new();
    private readonly Dictionary<uint, ulong> _audioGroupGuidOwners = new();
    private sealed class GroupVoice(ulong steamId, CSoundEventEntity entity, uint hash, int session)
    {
        public ulong SteamId { get; } = steamId;
        public CSoundEventEntity Entity { get; } = entity;
        public uint Hash { get; } = hash;
        public int Session { get; } = session;
        public uint Guid { get; set; }
        public int Volume { get; set; } = -1;
    }

    private void RegisterAudioGroupProbe()
    {
        // 跨地图继续运行；OnMapEnd 会关闭名单，卸载由 BasePlugin 清理定时器。
        AddTimer(0.5f, ReconcileAudioGroup, TimerFlags.REPEAT);
        RegisterEventHandler<EventPlayerConnectFull>((@event, _) =>
        {
            var player = @event.Userid;
            if (player?.IsValid == true)
            {
                var steamId = player.SteamID;
                AddTimer(1f, () =>
                {
                    if (!_audioGroup.Contains(steamId)) return;
                    var connected = FindProbePlayer(steamId);
                    if (connected != null)
                    {
                        EnsureAudioGroupVoice(connected);
                        ProbeReply(connected, "已按本次测试的加入记录接入；实际进度仍需听音核对。");
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
            return HookResult.Continue;
        });
    }

    private static CCSPlayerController? FindProbePlayer(ulong steamId) => Utilities.GetPlayers()
        .FirstOrDefault(player => player.IsValid && !player.IsBot && !player.IsHLTV && player.SteamID == steamId);
    private static int ProbePlayerVolume(ulong steamId) =>
        CaorenCupPreferencesAccess.TryGet()?.Get(steamId.ToString()).Volume ?? 0;
    private static RecipientFilter ProbePlayerRecipients(ulong steamId) =>
        FindProbePlayer(steamId) is { } player ? new RecipientFilter(player) : new RecipientFilter();

    private bool TryPeerProbeCommand(CCSPlayerController? player, CommandInfo info, string operation)
    {
        if (!AudioProbeGroupPolicy.IsSelfParticipationOperation(operation)) return false;
        if (player?.IsValid != true || player.IsBot || player.IsHLTV || player.SteamID == 0)
        { ProbeReply(player, "join/leave 需由已连接的真人玩家输入。"); return true; }
        try
        {
            if (operation == "leave")
            {
                if (_audioGroup.Owner == player.SteamID)
                { ProbeReply(player, "管理员结束本次多人测试请用 /caudio_probe group off。"); return true; }
                _audioGroup.Leave(player.SteamID);
                StopAudioGroupVoice(player.SteamID);
                ProbeReply(player, "已退出音频试听，不再在本次重连接入。");
                return true;
            }
            if (!_audioGroup.IsOpen)
            { ProbeReply(player, "管理员尚未打开多人试听；不会给你播放声音。"); return true; }
            var position = ExpectedProbePosition();
            if (!_audioProbeLoop && position >= 30)
            { ProbeReply(player, "本次测试音频已结束，不补播。"); return true; }
            _audioGroup.Join(player.SteamID);
            EnsureAudioGroupVoice(player);
            ProbeReply(player, "已自愿加入本次音频试听；个人音量只影响自己。断线重连仍可接入，leave 可退出。");
        }
        catch (Exception error)
        {
            ProbeLog("group.error", new { Operation = operation, Error = error.Message });
            ProbeReply(player, "多人探针未通过：" + error.Message);
        }
        return true;
    }

    private void SetAudioGroup(CCSPlayerController player, CommandInfo info)
    {
        var mode = info.ArgCount == 3 ? info.GetArg(2).ToLowerInvariant() : "";
        if (mode == "off")
        {
            StopAudioProbe();
            ProbeReply(player, "本次多人测试已结束，所有测试声音已请求定向停止。");
            return;
        }
        if (mode != "on") { ProbeReply(player, "用法：/caudio_probe group on|off"); return; }
        if (_audioGroup.IsOpen) { ProbeReply(player, "本次多人试听已经打开。"); return; }
        if (_audioProbeSnapshot == null) { ProbeReply(player, "还没有启动消息，先等测试音响起再 group on。"); return; }
        if (!_audioProbeLoop && ExpectedProbePosition() >= 30)
        { ProbeReply(player, "音频已结束，先 stop，再 start loop12。"); return; }
        _audioGroup.Open(player.SteamID);
        // 原始实体保留为逻辑时钟／管理控制源；听音改为每人一个可隔离的实例。
        foreach (var guid in _audioProbeGuids) SendProbeGuidStop(guid, player.SteamID);
        ReconcileAudioGroup();
        ProbeReply(player, "多人试听已打开，只影响你和输入 join 的玩家；stop、断线或卸载会结束本次测试。");
    }

    private void ReconcileAudioGroup()
    {
        if (!_audioGroup.IsOpen) return;
        try
        {
            if (FindProbePlayer(_audioGroup.Owner) == null) { StopAudioProbe(); return; }
            foreach (var steamId in _audioGroup.Members)
            {
                var player = FindProbePlayer(steamId);
                if (player == null) { StopAudioGroupVoice(steamId); continue; }
                if (ProbePlayerVolume(steamId) <= 0 || (!_audioProbeLoop && ExpectedProbePosition() >= 30))
                { StopAudioGroupVoice(steamId); continue; }
                EnsureAudioGroupVoice(player);
                if (_audioGroupVoices.TryGetValue(steamId, out var voice) && voice.Guid != 0)
                {
                    var volume = ProbePlayerVolume(steamId);
                    if (volume == voice.Volume) continue;
                    using var message = UserMessage.FromPartialName("SosSetSoundEventParams");
                    message.SetUInt("soundevent_guid", voice.Guid);
                    message.SetBytes("packed_params", AudioProbeWireCodec.Volume(volume));
                    message.Send(new RecipientFilter(player));
                    voice.Volume = volume;
                    ProbeLog("group.volume", new { Slot = player.Slot, voice.Guid, Percent = volume });
                }
            }
        }
        catch (Exception error) { ProbeLog("group.error", new { Operation = "reconcile", Error = error.Message }); }
    }

    private void EnsureAudioGroupVoice(CCSPlayerController player)
    {
        if (!_audioGroup.Contains(player.SteamID) || _audioProbeEntity?.IsValid != true || _audioProbeSnapshot == null) return;
        var position = AudioProbeGroupPolicy.StartPosition(ExpectedProbePosition(), _audioProbeLoop,
            _audioProbePausedAt != null, ProbePlayerVolume(player.SteamID));
        if (position == null || _audioGroupVoices.ContainsKey(player.SteamID)) return;
        var entity = Utilities.CreateEntityByName<CSoundEventEntity>("snd_event_point")
            ?? throw new InvalidOperationException("无法创建参与者声音实体。");
        var voice = new GroupVoice(player.SteamID, entity, _audioProbeSnapshot.Hash, _audioProbeSession);
        _audioGroupVoices.Add(player.SteamID, voice);
        _audioGroupSources[entity.Index] = voice;
        try
        {
            entity.SoundName = _audioProbeEventName;
            var identity = entity.Entity ?? throw new InvalidOperationException("参与者声音实体没有 Identity。");
            identity.Name = $"caorencup_audio_group_{player.Slot}_{entity.Index}";
            entity.SourceEntityName = identity.Name;
            entity.StartOnSpawn = false; entity.StopOnNew = true; entity.SaveRestore = true; entity.EntityIndexSelection = 1;
            entity.DispatchSpawn();
            entity.AcceptInput("StartSound", player, player);
            AddTimer(1f, () =>
            {
                if (_audioProbeSession != voice.Session || !_audioGroupVoices.TryGetValue(voice.SteamId, out var current)
                    || !ReferenceEquals(current, voice) || voice.Guid != 0) return;
                StopAudioGroupVoice(voice.SteamId);
                ProbeLog("group.error", new { Operation = "attach", Error = "未捕获参与者启动消息", Slot = player.Slot });
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        catch { StopAudioGroupVoice(player.SteamID); throw; }
    }

    private bool TryGroupSoundStart(UserMessage message)
    {
        var source = (uint)message.ReadInt("source_entity_index");
        // 当前主实体优先走单人／管理源路径，避免实体索引重用时误认为旧参与者。
        if (_audioProbeEntity?.IsValid == true && source == _audioProbeEntity.Index) return false;
        if (!_audioGroupSources.TryGetValue(source, out var voice) || message.ReadUInt("soundevent_hash") != voice.Hash) return false;
        message.Recipients = new RecipientFilter(); // 自己管理的实例先默认无接收者
        var guid = message.ReadUInt("soundevent_guid");
        _audioGroupGuidOwners[guid] = voice.SteamId;
        if (!_audioGroup.Contains(voice.SteamId) || voice.Session != _audioProbeSession || !voice.Entity.IsValid
            || !_audioGroupVoices.TryGetValue(voice.SteamId, out var current) || !ReferenceEquals(current, voice)) return true;
        var player = FindProbePlayer(voice.SteamId);
        var volume = ProbePlayerVolume(voice.SteamId);
        var position = AudioProbeGroupPolicy.StartPosition(ExpectedProbePosition(), _audioProbeLoop, _audioProbePausedAt != null, volume);
        if (player == null || position == null)
        {
            Server.NextFrame(() => { if (_audioGroupVoices.GetValueOrDefault(voice.SteamId) == voice) StopAudioGroupVoice(voice.SteamId); });
            return true;
        }
        voice.Guid = guid; voice.Volume = volume;
        var fields = message.ReadBytes("packed_params");
        message.SetFloat("start_time", 0);
        message.SetBytes("packed_params", fields.Concat(AudioProbeWireCodec.ProbeStartOffset(position.Value))
            .Concat(AudioProbeWireCodec.Volume(volume)).ToArray());
        message.Recipients = new RecipientFilter(player);
        ProbeLog("group.start", new { Slot = player.Slot, Guid = guid, Source = source,
            ExpectedSeconds = position.Value, Volume = volume, Recipients = message.Recipients.Count });
        return true;
    }

    private bool TryGroupGuidMessage(UserMessage message)
    {
        var guid = message.ReadUInt("soundevent_guid");
        if (!_audioGroupGuidOwners.TryGetValue(guid, out var steamId)) return false;
        // 0% 或已经 leave 也必须收到旧实例停止消息，不能按新音量丢弃停止。
        message.Recipients = ProbePlayerRecipients(steamId);
        ProbeLog("group.guid-message", new { Guid = guid, Type = message.Type, Recipients = message.Recipients.Count });
        return true;
    }

    private void SendProbeGuidStop(uint guid, ulong steamId)
    {
        if (guid == 0) return;
        using var message = UserMessage.FromPartialName("SosStopSoundEvent");
        message.SetUInt("soundevent_guid", guid);
        message.Send(ProbePlayerRecipients(steamId));
    }

    private void StopAudioGroupVoice(ulong steamId)
    {
        if (!_audioGroupVoices.Remove(steamId, out var voice)) return;
        SendProbeGuidStop(voice.Guid, steamId);
        if (voice.Entity.IsValid) { voice.Entity.AcceptInput("StopSound"); voice.Entity.Remove(); }
        // Source/GUID 的所有权留作旧消息隔离；仅匹配草人杯这个事件的 hash。
    }

    private void ControlAudioGroupVoices(string input)
    {
        foreach (var voice in _audioGroupVoices.Values.ToArray())
            if (voice.Entity.IsValid) voice.Entity.AcceptInput(input);
    }
    private void CloseAudioGroup()
    {
        foreach (var steamId in _audioGroupVoices.Keys.ToArray()) StopAudioGroupVoice(steamId);
        _audioGroup.Close();
    }
    private void RestoreAudioGroup(ulong owner, ulong[] members)
    {
        _audioGroup.Open(owner);
        foreach (var member in members) _audioGroup.Join(member);
    }
}
