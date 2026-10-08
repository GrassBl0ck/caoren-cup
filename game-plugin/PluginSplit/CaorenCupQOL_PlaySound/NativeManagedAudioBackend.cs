using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.QOL.PlaySound;

/// <summary>每位听众有独立实例，音乐／广播各有服务端时间状态，不绑定管理员连接。</summary>
public sealed class NativeManagedAudioBackend : IManagedAudioBackend
{
    private readonly BasePlugin _plugin;
    private readonly AudioAssetLibrary _library;
    private readonly Dictionary<CaorenAudioChannel, Session> _channels = new();
    private readonly List<Session> _effects = new();
    private readonly Dictionary<uint, Voice> _sources = new();
    private readonly Dictionary<uint, ulong> _guidOwners = new();
    private readonly Dictionary<uint, double> _retiredSources = new();
    private readonly Dictionary<uint, double> _retiredGuids = new();
    private bool _hooks;
    private long _sequence;
    private sealed record NativeEffectRequest(uint Hash, ulong SteamId, float Volume, double Deadline);
    private readonly Dictionary<uint, NativeEffectRequest> _nativeEffects = new();
    private NativeEffectRequest? _emittingEffect;
    private sealed class Session(CaorenAudioEvent audio, CaorenAudioAsset asset, double now, HashSet<ulong>? audience)
    {
        public long Id { get; set; }
        public CaorenAudioEvent Audio { get; } = audio;
        public CaorenAudioAsset Asset { get; } = asset;
        public AudioPlaybackTimeline Timeline { get; } = new(asset.DurationSeconds, now, audio.Loop);
        public HashSet<ulong>? Audience { get; } = audience;
        public Dictionary<ulong, Voice> Voices { get; } = new();
        public bool Contains(ulong id) => Audience == null || Audience.Contains(id);
    }
    private sealed class Voice(Session session, ulong steamId, CSoundEventEntity entity, uint hash)
    {
        public Session Session { get; } = session;
        public ulong SteamId { get; } = steamId;
        public CSoundEventEntity Entity { get; } = entity;
        public uint Source { get; } = entity.Index;
        public uint Hash { get; } = hash;
        public uint Guid { get; set; }
        public float Volume { get; set; } = -1;
    }
    public bool Ready => _library.Assets.Count > 0;
    public bool HasAsset(string id) => _library.Find(id) != null;
    public NativeManagedAudioBackend(BasePlugin plugin, AudioAssetLibrary library) { _plugin = plugin; _library = library; }
    private static bool Human(CCSPlayerController player) => player.IsValid && !player.IsBot && !player.IsHLTV && player.SteamID != 0;
    private static CCSPlayerController? Player(ulong id) => Utilities.GetPlayers().FirstOrDefault(p => Human(p) && p.SteamID == id);
    private static RecipientFilter Recipients(ulong id) => Player(id) is { } player ? new(player) : new();
    private static float Volume(Session session, ulong id) => CaorenAudioPolicy.Volume(
        CaorenCupPreferencesAccess.TryGet()?.Get(id.ToString()).Volume ?? 0, session.Audio.DefaultVolume);
    private IEnumerable<Session> Sessions => _channels.Values.Concat(_effects).ToArray();

    private void PrepareHooks()
    {
        if (_hooks) return;
        _plugin.HookUserMessage(UserMessage.FindIdByName("SosStartSoundEvent"), StartMessage, HookMode.Pre);
        _plugin.HookUserMessage(UserMessage.FindIdByName("SosStopSoundEvent"), GuidMessage, HookMode.Pre);
        _plugin.HookUserMessage(UserMessage.FindIdByName("SosSetSoundEventParams"), GuidMessage, HookMode.Pre);
        _hooks = true;
    }
    public CaorenAudioResult Play(CaorenAudioEvent audio, IReadOnlyList<CCSPlayerController>? recipients)
    {
        var asset = _library.Find(audio.Id);
        if (asset == null) return new(false, "缺少该音频的原生事件／时长登记，未播放。");
        if (audio.Channel == CaorenAudioChannel.Effect && audio.Loop) return new(false, "循环音频请登记到音乐或广播通道。");
        PrepareHooks();
        HashSet<ulong>? audience = recipients?.Where(Human).Select(p => p.SteamID).ToHashSet();
        // 短音效不补播给后来加入者。
        if (audio.Channel == CaorenAudioChannel.Effect && audience == null)
            audience = Utilities.GetPlayers().Where(p => Human(p) && CaorenCupPreferencesAccess.TryGet()?.Get(p.SteamID.ToString()).Volume > 0).Select(p => p.SteamID).ToHashSet();
        var session = new Session(audio, asset, Server.CurrentTime, audience) { Id = ++_sequence };
        if (audio.Channel == CaorenAudioChannel.Effect) _effects.Add(session);
        else
        {
            if (_channels.TryGetValue(audio.Channel, out var old)) StopSession(old);
            _channels[audio.Channel] = session;
        }
        try { Reconcile(session); }
        catch { StopSession(session); throw; }
        return new(true, $"已请求播放：{audio.DisplayName}；通道 {audio.Channel}，音量只影响各自听众。", session.Voices.Count);
    }
    public CaorenAudioResult PlayNativeEffect(CaorenAudioEvent audio, IReadOnlyList<CCSPlayerController>? recipients, CBaseEntity? source)
    {
        PrepareHooks();
        var count = 0;
        foreach (var player in (recipients ?? Utilities.GetPlayers()).Where(Human).DistinctBy(p => p.SteamID))
        {
            var volume = CaorenAudioPolicy.Volume(CaorenCupPreferencesAccess.TryGet()?.Get(player.SteamID.ToString()).Volume ?? 0, audio.DefaultVolume);
            if (volume <= 0) continue;
            var request = new NativeEffectRequest(AudioProbeWireCodec.EventHash(audio.Source), player.SteamID, volume, Server.CurrentTime + 2);
            _emittingEffect = request;
            try
            {
                var guid = (source ?? player).EmitSound(audio.Source, new RecipientFilter(player), 1);
                if (guid != 0) _nativeEffects[guid] = request;
                count++;
            }
            finally { _emittingEffect = null; }
        }
        return new(true, "已请求按个人音量播放原生提示音；实际比例须客户端验收。", count);
    }
    public CaorenAudioResult Control(CaorenAudioChannel channel, CaorenAudioControl operation)
    {
        if (channel == CaorenAudioChannel.Effect || !_channels.TryGetValue(channel, out var session))
            return new(false, "该通道没有可控制的音乐／广播。");
        var now = (double)Server.CurrentTime;
        if (operation == CaorenAudioControl.Stop)
        { StopSession(session); return new(true, "已定向停止该通道，正常游戏声音不受影响。"); }
        if (session.Timeline.Snapshot(now).State is AudioPlaybackState.Ended or AudioPlaybackState.Stopped)
            return new(false, "该音频已经结束；重新播放会从头开始。");
        switch (operation)
        {
            case CaorenAudioControl.Pause:
                if (!session.Timeline.Pause(now)) return new(false, "该通道已暂停。");
                foreach (var voice in session.Voices.Values) if (voice.Entity.IsValid) voice.Entity.AcceptInput("PauseSound");
                return new(true, "已暂停该通道，播放进度冻结。");
            case CaorenAudioControl.Resume:
                if (!session.Timeline.Resume(now)) return new(false, "该通道没有暂停。");
                foreach (var voice in session.Voices.Values) if (voice.Entity.IsValid) voice.Entity.AcceptInput("UnPauseSound");
                Reconcile(session);
                return new(true, "已从原进度恢复该通道。");
            case CaorenAudioControl.LoopOn:
            case CaorenAudioControl.LoopOff:
                var enabled = operation == CaorenAudioControl.LoopOn;
                if (session.Timeline.LoopEnabled == enabled) return new(true, enabled ? "循环已开启。" : "循环已关闭。");
                session.Timeline.SetLoop(enabled, now);
                // 更换有／无 WAV 循环标记的资源，负 delay 保留当前音段。
                // 暂停时先保持静音，resume 后重新接入，不用重播冒充暂停。
                foreach (var id in session.Voices.Keys.ToArray()) StopVoice(session, id);
                Reconcile(session);
                return new(true, enabled ? "已开启整段循环，接续当前进度。" : "循环已关闭，当前一轮播完后停止。");
            default: return new(false, "未知音频控制。");
        }
    }
    public IReadOnlyList<CaorenAudioChannelStatus> GetChannels() => new[] { CaorenAudioChannel.Music, CaorenAudioChannel.Broadcast }
        .Select(channel =>
        {
            if (!_channels.TryGetValue(channel, out var session)) return new CaorenAudioChannelStatus(channel, null, "", "Idle", 0, 0, false, false, 0);
            var snapshot = session.Timeline.Snapshot(Server.CurrentTime);
            return new CaorenAudioChannelStatus(channel, session.Audio.Id, session.Audio.DisplayName, snapshot.State.ToString(),
                snapshot.PositionSeconds, session.Asset.DurationSeconds, snapshot.LoopEnabled,
                snapshot.FinishingCycle,
                session.Voices.Values.Count(v => v.Guid != 0));
        }).ToArray();

    public void Tick()
    {
        try
        {
            foreach (var session in Sessions) Reconcile(session);
            foreach (var source in _retiredSources.Where(p => p.Value < Server.CurrentTime).Select(p => p.Key).ToArray())
            { _retiredSources.Remove(source); if (_sources.TryGetValue(source, out var voice) && !voice.Entity.IsValid) _sources.Remove(source); }
            foreach (var guid in _retiredGuids.Where(p => p.Value < Server.CurrentTime).Select(p => p.Key).ToArray())
            { _retiredGuids.Remove(guid); _guidOwners.Remove(guid); }
            foreach (var guid in _nativeEffects.Where(pair => pair.Value.Deadline < Server.CurrentTime).Select(pair => pair.Key).ToArray())
                _nativeEffects.Remove(guid);
            _effects.RemoveAll(s => s.Timeline.Snapshot(Server.CurrentTime).State is AudioPlaybackState.Ended or AudioPlaybackState.Stopped);
        }
        catch (Exception error) { Console.WriteLine("[CaorenCupAudio] tick failed: " + error.Message); }
    }
    private void Reconcile(Session session)
    {
        var state = session.Timeline.Snapshot(Server.CurrentTime);
        if (state.State is AudioPlaybackState.Ended or AudioPlaybackState.Stopped)
        { foreach (var id in session.Voices.Keys.ToArray()) StopVoice(session, id); return; }
        foreach (var id in session.Voices.Keys.ToArray())
            if (Player(id) == null || Volume(session, id) <= 0) StopVoice(session, id);
        foreach (var player in Utilities.GetPlayers().Where(Human))
        {
            if (!session.Contains(player.SteamID) || Volume(session, player.SteamID) <= 0) continue;
            if (!session.Voices.TryGetValue(player.SteamID, out var voice))
            {
                if (state.State == AudioPlaybackState.Playing) StartVoice(session, player);
                continue;
            }
            var volume = Volume(session, player.SteamID);
            if (voice.Guid == 0 || volume == voice.Volume) continue;
            using var message = UserMessage.FromPartialName("SosSetSoundEventParams");
            message.SetUInt("soundevent_guid", voice.Guid);
            message.SetBytes("packed_params", AudioProbeWireCodec.VolumeScalar(volume));
            message.Send(new RecipientFilter(player));
            voice.Volume = volume;
        }
    }
    private void StartVoice(Session session, CCSPlayerController player)
    {
        var name = session.Timeline.LoopEnabled ? session.Asset.LoopSoundEvent : session.Asset.SoundEvent;
        var entity = Utilities.CreateEntityByName<CSoundEventEntity>("snd_event_point") ?? throw new InvalidOperationException("声音实例创建失败。");
        var voice = new Voice(session, player.SteamID, entity, AudioProbeWireCodec.EventHash(name));
        session.Voices.Add(player.SteamID, voice);
        _sources[entity.Index] = voice;
        _retiredSources.Remove(entity.Index);
        try
        {
            entity.SoundName = name;
            var identity = entity.Entity ?? throw new InvalidOperationException("声音实例缺少 Identity。");
            identity.Name = $"caoren_audio_{session.Id}_{player.Slot}_{entity.Index}";
            entity.SourceEntityName = identity.Name;
            entity.StartOnSpawn = false; entity.StopOnNew = true; entity.SaveRestore = true; entity.EntityIndexSelection = 1;
            entity.DispatchSpawn(); entity.AcceptInput("StartSound", player, player);
            _plugin.AddTimer(1f, () =>
            {
                if (session.Voices.GetValueOrDefault(voice.SteamId) == voice && voice.Guid == 0)
                { StopVoice(session, voice.SteamId); Console.WriteLine("[CaorenCupAudio] 声音未捕获启动 GUID，已清理实例。"); }
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }
        catch { StopVoice(session, player.SteamID); throw; }
    }
    private HookResult StartMessage(UserMessage message)
    {
        var incomingGuid = message.ReadUInt("soundevent_guid");
        var hash = message.ReadUInt("soundevent_hash");
        var effect = _nativeEffects.GetValueOrDefault(incomingGuid);
        var knownGuid = effect != null;
        if (effect == null && _emittingEffect?.Hash == hash) effect = _emittingEffect;
        if (effect != null && (knownGuid || effect.Hash == hash))
        {
            message.SetBytes("packed_params", message.ReadBytes("packed_params").Concat(AudioProbeWireCodec.VolumeScalar(effect.Volume)).ToArray());
            message.Recipients = Recipients(effect.SteamId);
            _nativeEffects.Remove(incomingGuid);
            return HookResult.Continue;
        }
        var source = (uint)message.ReadInt("source_entity_index");
        if (!_sources.TryGetValue(source, out var voice) || message.ReadUInt("soundevent_hash") != voice.Hash) return HookResult.Continue;
        message.Recipients = new RecipientFilter();
        var guid = message.ReadUInt("soundevent_guid");
        _guidOwners[guid] = voice.SteamId;
        var session = voice.Session;
        var state = session.Timeline.Snapshot(Server.CurrentTime);
        var player = Player(voice.SteamId);
        var volume = Volume(session, voice.SteamId);
        if (!voice.Entity.IsValid || session.Voices.GetValueOrDefault(voice.SteamId) != voice || player == null
            || state.State != AudioPlaybackState.Playing || volume <= 0) return HookResult.Continue;
        voice.Guid = guid; voice.Volume = volume;
        message.SetFloat("start_time", 0);
        message.SetBytes("packed_params", message.ReadBytes("packed_params")
            .Concat(AudioProbeWireCodec.PlaybackOffset(state.PositionSeconds)).Concat(AudioProbeWireCodec.VolumeScalar(volume)).ToArray());
        message.Recipients = new RecipientFilter(player);
        Console.WriteLine($"[CaorenCupAudio] start channel={session.Audio.Channel} event={session.Audio.Id} guid={guid} slot={player.Slot} position={state.PositionSeconds:0.000} volume={volume:0.000}");
        return HookResult.Continue;
    }
    private HookResult GuidMessage(UserMessage message)
    {
        if (_guidOwners.TryGetValue(message.ReadUInt("soundevent_guid"), out var id)) message.Recipients = Recipients(id);
        return HookResult.Continue;
    }
    private void StopVoice(Session session, ulong id)
    {
        if (!session.Voices.Remove(id, out var voice)) return;
        if (voice.Guid != 0)
        {
            using var message = UserMessage.FromPartialName("SosStopSoundEvent");
            message.SetUInt("soundevent_guid", voice.Guid); message.Send(Recipients(id));
        }
        if (voice.Entity.IsValid) { voice.Entity.AcceptInput("StopSound"); voice.Entity.Remove(); }
        _retiredSources[voice.Source] = Server.CurrentTime + 2;
        if (voice.Guid != 0) _retiredGuids[voice.Guid] = Server.CurrentTime + 2;
    }
    private void StopSession(Session session)
    {
        session.Timeline.Stop(Server.CurrentTime);
        foreach (var id in session.Voices.Keys.ToArray()) StopVoice(session, id);
    }
    public void StopAll()
    {
        foreach (var session in Sessions) StopSession(session);
        _channels.Clear(); _effects.Clear();
    }
    public void MapEnd() { StopAll(); _sources.Clear(); _guidOwners.Clear(); _retiredSources.Clear(); _retiredGuids.Clear(); _nativeEffects.Clear(); }
}
