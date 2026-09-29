using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.QOL.PlaySound;

public static class CaorenAudioPolicy
{
    public static float Volume(int personalVolume, float defaultVolume) =>
        Math.Clamp(personalVolume, 0, 100) / 100f * Math.Clamp(defaultVolume, 0, 1);
    // 不允许音频登记或 /ps 路径注入客户端命令。
    public static bool IsSafeSource(string source) => !string.IsNullOrWhiteSpace(source)
        && Regex.IsMatch(source, @"\A[A-Za-z0-9_./-]+\z") && !source.Contains("..") && !source.StartsWith('/')
        && !source.StartsWith('-');
    public static string ClientPlayCommand(string source, float volume)
    {
        if (!IsSafeSource(source) || !float.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentException("无效音频路径或音量。");
        return $"playvol \"{source}\" {volume.ToString("0.####", CultureInfo.InvariantCulture)}";
    }
}

/// <summary>短音频后端。长音频能力未经游戏宿主验证，明确拒绝而不是伪造播放状态。</summary>
public sealed class CaorenAudioService : ICaorenCupAudio, ICaorenCupAudioStatus
{
    private readonly Dictionary<string, CaorenAudioEvent> _events;
    private readonly IManagedAudioBackend? _managed;
    // 能力表示后端可调用，不代替该版本的游戏内验收。
    public CaorenAudioCapabilities Capabilities => _managed?.Ready == true ? new(true, true, true, true) : new(false, false, false, false);
    public IReadOnlyCollection<CaorenAudioEvent> Events => _events.Values;
    public CaorenAudioService(string? customCatalogPath = null, IManagedAudioBackend? managed = null)
    {
        _managed = managed;
        _events = BuiltInEvents(managed != null).ToDictionary(e => e.Id, StringComparer.Ordinal);
        if (customCatalogPath != null && File.Exists(customCatalogPath))
        {
            var custom = JsonSerializer.Deserialize<CaorenAudioEvent[]>(File.ReadAllText(customCatalogPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })
                ?? throw new InvalidDataException("音频清单为空。");
            foreach (var item in custom)
            {
                if (!Regex.IsMatch(item.Id, @"\A[a-z0-9]+(?:[._-][a-z0-9]+)*\z") || !CaorenAudioPolicy.IsSafeSource(item.Source)
                    || !float.IsFinite(item.DefaultVolume) || item.DefaultVolume is < 0 or > 1 || !Enum.IsDefined(item.Channel))
                    throw new InvalidDataException("无效音频登记：" + item.Id);
                if (!_events.TryAdd(item.Id, item)) throw new InvalidDataException("重复音频事件 ID：" + item.Id);
            }
        }
    }
    private static IEnumerable<CaorenAudioEvent> BuiltInEvents(bool managed)
    {
        yield return new("vote.started", "投票开始", managed ? "caorencup.ui.click" : "UIPanorama.submenu_select", true, managed ? .3f : 1);
        // 保留游戏主素材；通过原生事件启动参数控制比例，不使用 playvol。
        yield return new("menu.click", managed ? "菜单点击" : "菜单点击（比例音量未验收）", managed ? "caorencup.ui.click" : "UIPanorama.submenu_select", true, managed ? .3f : 1);
        yield return new("bridge.notice", "网页桥接通知", managed ? "caorencup.bridge.notice" : "training/bell_normal.vsnd_c", managed);
        yield return new("gameplay.bleed.pain", "持续失血痛叫", managed ? "caorencup.player.pain" : "sounds/player/damage1.vsnd", managed);
        yield return new("gameplay.smoke.pain", "烟雾痛叫", managed ? "caorencup.player.pain" : "sounds/player/damage1.vsnd", managed);
        yield return new("gameplay.blade.damage", "剑气追加伤害", "Player.Damage", true);
        yield return new("gameplay.magic.damage", "魔法追加伤害", "Player.Damage", true);
    }
    public CaorenAudioResult Play(string eventId, IReadOnlyList<CCSPlayerController>? recipients = null, CBaseEntity? source = null)
    {
        if (!_events.TryGetValue(eventId, out var audio)) return new(false, "未知音频事件：" + eventId);
        if (source != null && !source.IsValid) return new(false, "音频空间来源已失效。");
        if (audio.Channel != CaorenAudioChannel.Effect || audio.Loop)
            return _managed?.Ready == true ? _managed.Play(audio, recipients) : new(false, "原生受控后端未就绪，未播放长音频。");
        if (_managed?.Ready == true && _managed.HasAsset(audio.Id)) return _managed.Play(audio, recipients);
        if (audio.NativeEvent && _managed != null) return _managed.PlayNativeEffect(audio, recipients, source);
        return PlayEvent(audio, recipients, source);
    }
    public CaorenAudioResult PlayLegacyBroadcast(string source)
    {
        if (!CaorenAudioPolicy.IsSafeSource(source)) return new(false, "无效音频路径。");
        // 保留 /ps 旧的一次性播放入口，不能宣称具备长广播控制。
        return PlayEvent(new("legacy.ps", "旧 /ps 一次性播放", source), null, null);
    }
    private static CaorenAudioResult PlayEvent(CaorenAudioEvent audio, IReadOnlyList<CCSPlayerController>? recipients, CBaseEntity? source)
    {
        var prefs = CaorenCupPreferencesAccess.TryGet();
        if (prefs == null) return new(false, "Core 个人偏好服务不可用，声音未播放。");
        if (source != null && !source.IsValid) return new(false, "音频空间来源已失效。");
        var count = 0; var failures = 0;
        foreach (var player in (recipients ?? Utilities.GetPlayers()).DistinctBy(player => player.Slot))
        {
            if (!player.IsValid || player.IsBot || player.IsHLTV) continue;
            var volume = CaorenAudioPolicy.Volume(prefs.Get(player.SteamID.ToString()).Volume, audio.DefaultVolume);
            if (volume <= 0) continue;
            try
            {
                if (audio.NativeEvent) (source ?? player).EmitSound(audio.Source, new RecipientFilter(player), volume);
                else player.ExecuteClientCommand(CaorenAudioPolicy.ClientPlayCommand(audio.Source, volume));
                count++;
            }
            catch { failures++; }
        }
        return new(failures == 0, failures == 0 ? $"已发送播放请求，接收者 {count} 人；客户端资源与实际声音须游戏内验收。" : $"{failures} 名玩家发送失败。", count);
    }
    public CaorenAudioResult Control(CaorenAudioChannel channel, CaorenAudioControl operation) =>
        _managed?.Ready == true ? _managed.Control(channel, operation)
            : new(false, "受控音频后端未就绪。未改变播放状态。");
    public IReadOnlyList<CaorenAudioChannelStatus> GetChannels() => _managed?.GetChannels() ?? [];
}
