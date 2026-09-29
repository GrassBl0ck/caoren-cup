using System.Text.Json;
using System.Text.RegularExpressions;
using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.QOL.PlaySound;

public sealed record CaorenAudioAsset(string Id, double DurationSeconds, string SoundEvent,
    string LoopSoundEvent, string[] Resources);

public sealed class AudioAssetLibrary
{
    private readonly Dictionary<string, CaorenAudioAsset> _assets = new(StringComparer.Ordinal);
    public IReadOnlyCollection<CaorenAudioAsset> Assets => _assets.Values;
    public AudioAssetLibrary(string? path)
    {
        if (path == null || !File.Exists(path)) return;
        var assets = JsonSerializer.Deserialize<CaorenAudioAsset[]>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("素材时长清单为空。");
        foreach (var asset in assets)
        {
            if (!Regex.IsMatch(asset.Id, @"\A[a-z0-9]+(?:[._-][a-z0-9]+)*\z")
                || !double.IsFinite(asset.DurationSeconds) || asset.DurationSeconds <= 0
                || !CaorenAudioPolicy.IsSafeSource(asset.SoundEvent) || !CaorenAudioPolicy.IsSafeSource(asset.LoopSoundEvent)
                || asset.Resources == null || asset.Resources.Length != 2
                || asset.Resources.Any(r => !CaorenAudioPolicy.IsSafeSource(r) || !r.EndsWith(".vsnd", StringComparison.Ordinal)))
                throw new InvalidDataException("无效素材时长或原生登记：" + asset.Id);
            if (!_assets.TryAdd(asset.Id, asset)) throw new InvalidDataException("重复素材登记：" + asset.Id);
        }
    }
    public CaorenAudioAsset? Find(string id) => _assets.GetValueOrDefault(id);
}

public interface IManagedAudioBackend
{
    bool Ready { get; }
    bool HasAsset(string id);
    CaorenAudioResult Play(CaorenAudioEvent audio, IReadOnlyList<CCSPlayerController>? recipients);
    CaorenAudioResult Control(CaorenAudioChannel channel, CaorenAudioControl operation);
    IReadOnlyList<CaorenAudioChannelStatus> GetChannels();
    CaorenAudioResult PlayNativeEffect(CaorenAudioEvent audio, IReadOnlyList<CCSPlayerController>? recipients, CBaseEntity? source);
}
