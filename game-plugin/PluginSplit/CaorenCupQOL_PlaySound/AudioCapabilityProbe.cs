// 仅显式启用的技术验证构建包含本文件。普通构建不注册探针指令。
using System.Globalization;
using System.Text.Json;
using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.QOL.PlaySound;

public sealed partial class CaorenCupPlaySoundPlugin
{
    private const string AudioProbeEvent = "caorencup.probe.timeline";
    private CSoundEventEntity? _audioProbeEntity;
    private ulong _audioProbeSteamId;
    private readonly HashSet<uint> _audioProbeGuids = new();
    private bool _audioProbeHooksReady;
    private bool _audioProbeStarting;
    private ProbeStartSnapshot? _audioProbeSnapshot;
    private float _audioProbeStartedAt;
    private float _audioProbePausedSeconds;
    private float? _audioProbePausedAt;
    private bool _audioProbeLoop;
    private bool _audioProbeReceiveResetPending;
    private int _audioProbeSession;
    private string _audioProbeEventName = "";
    private bool? _audioProbeResetOffset;
    private uint _audioProbeResetOldGuid;
    private int _audioProbeLastMenuVolume = -1;
    private sealed record ProbeStartSnapshot(uint Guid, uint Hash, int Source, int Seed, byte[] Params);

    private void RegisterAudioCapabilityProbe()
    {
        AddCommand("css_caudio_probe", "仅开发构建：root 管理，玩家 join/leave 自愿试听", OnAudioCapabilityProbe);
        RegisterAudioGroupProbe();
        AddTimer(0.25f, ReconcileSoloProbeVolume, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            manifest.AddResource("soundevents/soundevents_addon.vsndevts");
            manifest.AddResource("sounds/caorencup/probe_timeline.vsnd");
            manifest.AddResource("sounds/caorencup/probe_lufs18.vsnd");
            manifest.AddResource("sounds/caorencup/probe_lufs16.vsnd");
            manifest.AddResource("sounds/caorencup/probe_lufs12.vsnd");
            manifest.AddResource("sounds/caorencup/probe_lufs10.vsnd");
            manifest.AddResource("sounds/caorencup/probe_loop12.vsnd");
        });
        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            StopAudioProbe();
            _audioGroupSources.Clear();
            _audioGroupGuidOwners.Clear();
        });
        RegisterEventHandler<EventPlayerDisconnect>((@event, _) =>
        {
            if (@event.Xuid != 0 && @event.Xuid == _audioProbeSteamId) StopAudioProbe();
            else if (@event.Xuid != 0) StopAudioGroupVoice(@event.Xuid);
            return HookResult.Continue;
        });
    }

    private void PrepareAudioProbeHooks()
    {
        if (_audioProbeHooksReady) return;
        var names = new[] { "SosStartSoundEvent", "SosStopSoundEvent", "SosSetSoundEventParams" };
        var ids = names.Select(UserMessage.FindIdByName).ToArray();
        for (var index = 0; index < names.Length; index++)
        {
            using var message = UserMessage.FromId(ids[index]);
            ProbeLog("message.schema", new { Name = message.Name, Type = message.Type, Id = message.Id, Fields = message.DebugString });
        }
        HookUserMessage(ids[0], OnProbeSoundStart, HookMode.Pre);
        HookUserMessage(ids[1], OnProbeSoundStop, HookMode.Pre);
        HookUserMessage(ids[2], OnProbeSoundParams, HookMode.Pre);
        _audioProbeHooksReady = true;
    }

    private HookResult OnProbeSoundStart(UserMessage message)
    {
        if (TryGroupSoundStart(message)) return HookResult.Continue;
        if (_audioProbeEntity?.IsValid != true || (!_audioProbeStarting && message.ReadInt("source_entity_index") != (int)_audioProbeEntity.Index))
            return HookResult.Continue;
        var guid = message.ReadUInt("soundevent_guid");
        if (_audioGroup.IsOpen) message.Recipients = new RecipientFilter();
        else FilterProbeRecipients(message, respectMute: true);
        if (_audioProbeResetOffset is { } usePlaybackOffset)
        {
            if (guid == 0 || guid == _audioProbeResetOldGuid)
            {
                message.Recipients = new RecipientFilter();
                ProbeLog("receiver-reset.error", new { Error = "引擎未产生新的有效 GUID", OldGuid = _audioProbeResetOldGuid, NewGuid = guid });
                _audioProbeReceiveResetPending = false;
                _audioProbeResetOffset = null;
                return HookResult.Continue;
            }
            var expected = ExpectedProbePosition();
            // 从头对照已通过。csgo_mega 将 public.delay 接入 vmix_voice_start，
            // 原生随机起始时间也使用负 delay；其精确进度仍须实机验证。
            message.SetFloat("start_time", 0f);
            if (usePlaybackOffset)
            {
                var fields = message.ReadBytes("packed_params");
                message.SetBytes("packed_params", fields.Concat(AudioProbeWireCodec.ProbeStartOffset(expected)).ToArray());
            }
            ProbeLog("receiver-reset.sent", new { Backend = "native-fresh-guid",
                Mode = usePlaybackOffset ? "negative-delay" : "from-start-control", OldGuid = _audioProbeResetOldGuid,
                NewGuid = guid, ExpectedSeconds = expected, WireStartTime = 0f,
                ParamsHex = Convert.ToHexString(message.ReadBytes("packed_params")), GameTime = Server.CurrentTime });
            _audioProbeReceiveResetPending = false;
            _audioProbeResetOffset = null;
        }
        _audioProbeGuids.Add(guid);
        if (!_audioGroup.IsOpen)
        {
            var menuVolume = ProbePlayerVolume(_audioProbeSteamId);
            var fields = message.ReadBytes("packed_params");
            message.SetBytes("packed_params", fields.Concat(AudioProbeWireCodec.Volume(menuVolume)).ToArray());
            _audioProbeLastMenuVolume = menuVolume;
        }
        // StartSound 可以在后续帧才产生消息；必须在已确认的探针源匹配后
        // 保存第一条启动消息，不能仅依赖同步调用期间的 _audioProbeStarting。
        _audioProbeSnapshot ??= new(guid, message.ReadUInt("soundevent_hash"), message.ReadInt("source_entity_index"),
            message.ReadInt("seed"), message.ReadBytes("packed_params"));
        ProbeLog("sound.start", new { Guid = guid, Source = message.ReadInt("source_entity_index"),
            Hash = message.ReadUInt("soundevent_hash"), Seed = message.ReadInt("seed"),
            StartTime = message.ReadFloat("start_time"), GameTime = Server.CurrentTime, ParamsHex = Convert.ToHexString(message.ReadBytes("packed_params")),
            Recipients = message.Recipients.Count });
        return HookResult.Continue;
    }
    private HookResult OnProbeSoundStop(UserMessage message)
    {
        if (TryGroupGuidMessage(message)) return HookResult.Continue;
        var guid = message.ReadUInt("soundevent_guid");
        if (!_audioProbeGuids.Contains(guid)) return HookResult.Continue;
        FilterProbeRecipients(message);
        ProbeLog("sound.stop", new { Guid = guid, GameTime = Server.CurrentTime, Recipients = message.Recipients.Count });
        return HookResult.Continue;
    }
    private HookResult OnProbeSoundParams(UserMessage message)
    {
        if (TryGroupGuidMessage(message)) return HookResult.Continue;
        var guid = message.ReadUInt("soundevent_guid");
        if (!_audioProbeGuids.Contains(guid)) return HookResult.Continue;
        FilterProbeRecipients(message);
        ProbeLog("sound.params", new { Guid = guid, GameTime = Server.CurrentTime, ParamsHex = Convert.ToHexString(message.ReadBytes("packed_params")) });
        return HookResult.Continue;
    }
    private void FilterProbeRecipients(UserMessage message, bool respectMute = false)
    {
        var player = Utilities.GetPlayers().FirstOrDefault(player => player.IsValid && player.SteamID == _audioProbeSteamId && !player.IsBot);
        var volume = player == null ? 0 : CaorenCupPreferencesAccess.TryGet()?.Get(player.SteamID.ToString()).Volume ?? 0;
        message.Recipients = player != null && (!respectMute || volume > 0) ? new RecipientFilter(player) : new RecipientFilter();
    }

    private void OnAudioCapabilityProbe(CCSPlayerController? player, CommandInfo info)
    {
        var operation = info.ArgCount > 1 ? info.GetArg(1).ToLowerInvariant() : "status";
        if (TryPeerProbeCommand(player, info, operation)) return;
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        { CaorenCupChat.PrintToChat(player, "音频技术验证需要 root 权限。"); return; }
        try
        {
            if (operation == "inspect")
            {
                PrepareAudioProbeHooks();
                ProbeReply(player, "协议消息可创建；单人验收见记录，多人同步仍需真人测试。");
                return;
            }
            if (operation == "status") { ProbeState("status"); ProbeReply(player, "探针状态已打印到服务器日志。"); return; }
            if (player == null) { ProbeReply(null, "播放/控制探针需由已连接的 root 玩家调用，仅影响自己。"); return; }
            if (operation is "start" or "replace")
            {
                var groupMembers = operation == "replace" && _audioGroup.IsOpen ? _audioGroup.Members.ToArray() : null;
                var variant = info.ArgCount > 2 ? info.GetArg(2).ToLowerInvariant() : "original";
                var eventName = variant switch { "original" => AudioProbeEvent,
                    "18" => "caorencup.probe.lufs18", "16" => "caorencup.probe.lufs16",
                    "12" => "caorencup.probe.lufs12", "10" => "caorencup.probe.lufs10",
                    "loop12" => "caorencup.probe.loop12", _ => null };
                if (eventName == null) { ProbeReply(player, "试听用法：/caudio_probe start|replace original|18|16|12|10|loop12；数字表示目标负 LUFS，不是音量百分比。"); return; }
                var volume = CaorenCupPreferencesAccess.TryGet()?.Get(player.SteamID.ToString()).Volume ?? 0;
                if (volume == 0) { ProbeReply(player, "个人音量为 0，探针不播放。请手动调高后再测。"); return; }
                if (_audioProbeEntity?.IsValid == true)
                {
                    if (operation != "replace" || _audioProbeSteamId != player.SteamID)
                    { ProbeReply(player, "已有探针在运行，先由原操作者停止；只允许替换自己的探针。"); return; }
                    StopAudioProbe();
                }
                else if (operation == "replace")
                { ProbeReply(player, "没有可替换的探针，先 start。"); return; }
                PrepareAudioProbeHooks();
                _audioProbeSteamId = player.SteamID;
                _audioProbeGuids.Clear();
                _audioProbeSnapshot = null;
                _audioProbeLastMenuVolume = volume;
                _audioProbeSession++;
                _audioProbeStartedAt = Server.CurrentTime;
                _audioProbePausedSeconds = 0;
                _audioProbePausedAt = null;
                _audioProbeLoop = variant == "loop12";
                _audioProbeEventName = eventName;
                if (groupMembers != null) RestoreAudioGroup(player.SteamID, groupMembers);
                var probeEntity = CreateAudioProbeEntity(player, eventName);
                _audioProbeStarting = true;
                try { probeEntity.AcceptInput("StartSound", player, player); }
                finally { _audioProbeStarting = false; }
                ReconcileAudioGroup();
                ProbeState("start.requested");
                ProbeReply(player, _audioGroup.IsOpen ? $"已请求替换为 {variant}，影响本次已加入的试听玩家。"
                    : $"已请求播放 {variant} 版本，只发给自己；响度默认值仍未修改。");
                ProbeLog("variant", new { Operation = operation, Variant = variant, Event = eventName });
                return;
            }
            if (_audioProbeEntity?.IsValid != true || _audioProbeSteamId != player.SteamID)
            { ProbeReply(player, "没有由你启动的有效探针。"); return; }
            if (_audioProbeReceiveResetPending && operation != "stop")
            { ProbeReply(player, "接收端重建实验还在等待发送，稍后再操作；stop 随时可用。"); return; }
            switch (operation)
            {
                case "group": SetAudioGroup(player, info); return;
                case "pause":
                    _audioProbeEntity.AcceptInput("PauseSound", player, player);
                    _audioProbePausedAt ??= Server.CurrentTime;
                    ControlAudioGroupVoices("PauseSound");
                    break;
                case "resume":
                    _audioProbeEntity.AcceptInput("UnPauseSound", player, player);
                    if (_audioProbePausedAt is { } pausedAt) _audioProbePausedSeconds += Server.CurrentTime - pausedAt;
                    _audioProbePausedAt = null;
                    ControlAudioGroupVoices("UnPauseSound");
                    ReconcileAudioGroup();
                    break;
                case "stop": StopAudioProbe(); break;
                case "replay":
                case "resync":
                    if (_audioGroup.IsOpen)
                    { ProbeReply(player, "多人测试用 join、重连和个人设置验证接入；单人 replay/resync 请先 group off。"); return; }
                    // replay 是明确从头播放的对照，不是暂停恢复或同步。
                    // resync 使用原生算子的负 delay 假设；是否 seek 必须另行听音。
                    if (_audioProbePausedAt != null || _audioProbeSnapshot is not { } snapshot)
                    { ProbeReply(player, "进度接入实验需要未暂停、已捕获起始消息的探针。"); return; }
                    var expected = ExpectedProbePosition();
                    if (!_audioProbeLoop && expected >= 30)
                    { ProbeReply(player, "测试音频已经结束，不能补播。"); return; }
                    if (ProbePlayerVolume(player.SteamID) <= 0)
                    { ProbeReply(player, "个人音量为 0，接入实验不播放；不会改变你的设置。"); return; }
                    ResetProbeReceiver(player, snapshot, operation == "resync");
                    ProbeReply(player, operation == "replay"
                        ? "接收端从头重建对照：预期回到第一档低音。此操作不是暂停恢复或同步。"
                        : $"负 delay 接入实验：预期约 {expected:0.00} 秒；接在哪档、从头或无声仍需听音确认。");
                    return;
                case "gain":
                    if (_audioGroup.IsOpen)
                    { ProbeReply(player, "多人试听请在各自个人设置调整音量，gain 只用于单人探针。"); return; }
                    if (info.ArgCount != 3 || !int.TryParse(info.GetArg(2), NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
                        || percent is < 0 or > 100)
                    { ProbeReply(player, "用法：/caudio_probe gain 0～100；只实验当前自己的探针音量，不保存个人设置。"); return; }
                    if (_audioProbeGuids.Count == 0)
                    { ProbeReply(player, "没有捕获到探针 GUID，未修改参数。"); return; }
                    var packedVolume = AudioProbeWireCodec.Volume(percent);
                    foreach (var guid in _audioProbeGuids)
                    {
                        using var parameter = UserMessage.FromPartialName("SosSetSoundEventParams");
                        parameter.SetUInt("soundevent_guid", guid);
                        parameter.SetBytes("packed_params", packedVolume);
                        parameter.Send(new RecipientFilter(player));
                    }
                    ProbeLog("gain.requested", new { Percent = percent, Parameter = "public.volume", Backend = "direct-message",
                        ParamsHex = Convert.ToHexString(packedVolume), Guids = _audioProbeGuids.ToArray(), GameTime = Server.CurrentTime });
                    break;
                case "guidstop":
                    if (_audioGroup.IsOpen)
                    { ProbeReply(player, "多人试听的定向停止使用 stop 或 group off。"); return; }
                    foreach (var guid in _audioProbeGuids)
                    {
                        using var message = UserMessage.FromPartialName("SosStopSoundEvent");
                        message.SetUInt("soundevent_guid", guid);
                        message.Send(new RecipientFilter(player));
                    }
                    break;
                default: ProbeReply(player, "用法：/caudio_probe start|replace|pause|resume|stop|group on|group off；玩家 join|leave；单人 gain|replay|resync"); return;
            }
            ProbeState(operation + ".requested");
            ProbeReply(player, "已提交探针控制请求；是否实际生效需听音与日志共同确认。");
        }
        catch (Exception error)
        {
            ProbeLog("error", new { Operation = operation, Error = error.Message });
            if (operation is "start" or "replace") StopAudioProbe();
            ProbeReply(player, "探针未通过：" + error.Message);
        }
    }

    private void StopAudioProbe()
    {
        CloseAudioGroup();
        if (_audioProbeEntity?.IsValid == true)
        {
            _audioProbeEntity.AcceptInput("StopSound");
            _audioProbeEntity.Remove();
        }
        _audioProbeEntity = null;
        _audioProbeSteamId = 0;
        _audioProbeGuids.Clear();
        _audioProbeSnapshot = null;
        _audioProbePausedAt = null;
        _audioProbeSession++;
        _audioProbeReceiveResetPending = false;
        _audioProbeResetOffset = null;
        _audioProbeEventName = "";
        _audioProbeLastMenuVolume = -1;
    }
    private CSoundEventEntity CreateAudioProbeEntity(CCSPlayerController player, string eventName)
    {
        var entity = Utilities.CreateEntityByName<CSoundEventEntity>("snd_event_point")
            ?? throw new InvalidOperationException("无法创建 snd_event_point。");
        _audioProbeEntity = entity;
        entity.SoundName = eventName;
        var identity = entity.Entity ?? throw new InvalidOperationException("音频探针实体没有有效 Identity。");
        var sourceName = $"caorencup_audio_probe_{player.Slot}_{entity.Index}";
        identity.Name = sourceName;
        entity.SourceEntityName = sourceName;
        entity.StartOnSpawn = false;
        entity.StopOnNew = true;
        entity.SaveRestore = true;
        entity.EntityIndexSelection = 1;
        entity.DispatchSpawn();
        return entity;
    }
    private void ResetProbeReceiver(CCSPlayerController player, ProbeStartSnapshot snapshot, bool usePlaybackOffset)
    {
        using (var stop = UserMessage.FromPartialName("SosStopSoundEvent"))
        {
            stop.SetUInt("soundevent_guid", snapshot.Guid);
            stop.Send(new RecipientFilter(player));
        }
        if (_audioProbeEntity?.IsValid == true)
        {
            _audioProbeEntity.AcceptInput("StopSound");
            _audioProbeEntity.Remove();
        }
        _audioProbeEntity = null;
        _audioProbeSnapshot = null;
        _audioProbeReceiveResetPending = true;
        _audioProbeResetOldGuid = snapshot.Guid;
        _audioProbeResetOffset = usePlaybackOffset;
        var session = _audioProbeSession;
        // 由引擎生成新 GUID，避免复用旧 GUID 的接收端缓存；保留原逻辑时钟。
        AddTimer(0.15f, () =>
        {
            if (session != _audioProbeSession) return; // stop/替换/断线后不得补发
            try
            {
                if (!player.IsValid || player.SteamID != _audioProbeSteamId)
                { StopAudioProbe(); return; }
                if (ProbePlayerVolume(player.SteamID) <= 0)
                { StopAudioProbe(); return; }
                var expected = ExpectedProbePosition();
                if (!_audioProbeLoop && expected >= 30) { StopAudioProbe(); return; }
                var entity = CreateAudioProbeEntity(player, _audioProbeEventName);
                _audioProbeStarting = true;
                try { entity.AcceptInput("StartSound", player, player); }
                finally { _audioProbeStarting = false; }
                AddTimer(1f, () =>
                {
                    if (session != _audioProbeSession || !_audioProbeReceiveResetPending) return;
                    ProbeLog("receiver-reset.error", new { Error = "未捕获新实例的启动消息" });
                    StopAudioProbe();
                    if (player.IsValid) ProbeReply(player, "未捕获新声音实例，实验失败并已清理。请不要重复同一请求。");
                }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            }
            catch (Exception error)
            {
                ProbeLog("receiver-reset.error", new { Error = error.Message });
                StopAudioProbe();
                if (player.IsValid) ProbeReply(player, "新实例实验失败：" + error.Message);
            }
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
    }
    // 这是服务端时钟推算值，不是已验证的引擎当前进度。
    private float ExpectedProbePosition()
    {
        var seconds = Math.Max(0, (_audioProbePausedAt ?? Server.CurrentTime) - _audioProbeStartedAt - _audioProbePausedSeconds);
        return _audioProbeLoop ? seconds % 30 : seconds;
    }
    private void ReconcileSoloProbeVolume()
    {
        if (_audioGroup.IsOpen || _audioProbeEntity?.IsValid != true || _audioProbeSnapshot == null
            || _audioProbeReceiveResetPending || _audioProbeSteamId == 0) return;
        if (!_audioProbeLoop && ExpectedProbePosition() >= 30) return;
        var player = FindProbePlayer(_audioProbeSteamId);
        if (player == null) return;
        try
        {
            var volume = ProbePlayerVolume(_audioProbeSteamId);
            if (volume == _audioProbeLastMenuVolume) return;
            using var message = UserMessage.FromPartialName("SosSetSoundEventParams");
            message.SetUInt("soundevent_guid", _audioProbeSnapshot.Guid);
            message.SetBytes("packed_params", AudioProbeWireCodec.Volume(volume));
            message.Send(new RecipientFilter(player));
            _audioProbeLastMenuVolume = volume;
            ProbeLog("menu-volume.applied", new { Percent = volume, _audioProbeSnapshot.Guid, GameTime = Server.CurrentTime });
        }
        catch (Exception error) { ProbeLog("menu-volume.error", new { Error = error.Message }); }
    }
    private void ProbeState(string operation)
    {
        ProbeLog("entity.state", new { Operation = operation, Valid = _audioProbeEntity?.IsValid == true,
            SavedIsPlaying = _audioProbeEntity?.IsValid == true ? _audioProbeEntity.SavedIsPlaying : (bool?)null,
            SavedElapsedTime = _audioProbeEntity?.IsValid == true ? _audioProbeEntity.SavedElapsedTime : (float?)null,
            Guids = _audioProbeGuids.ToArray(), GameTime = Server.CurrentTime,
            ExpectedSeconds = _audioProbeEntity?.IsValid == true ? ExpectedProbePosition() : (float?)null,
            HasStartSnapshot = _audioProbeSnapshot != null, ReceiveResetPending = _audioProbeReceiveResetPending,
            GroupOpen = _audioGroup.IsOpen, GroupMembers = _audioGroup.Members.Count,
            GroupVoices = _audioGroupVoices.Count, LastAppliedMenuVolume = _audioProbeLastMenuVolume,
            ProbeRevision = "menu-volume-v1" });
    }
    private static void ProbeLog(string action, object data) => Console.WriteLine("[CaorenAudioProbe] " + action + " " + JsonSerializer.Serialize(data));
    private static void ProbeReply(CCSPlayerController? player, string message)
    {
        if (player == null) Console.WriteLine("[CaorenAudioProbe] " + message);
        else CaorenCupChat.PrintToChat(player, message);
    }
}
