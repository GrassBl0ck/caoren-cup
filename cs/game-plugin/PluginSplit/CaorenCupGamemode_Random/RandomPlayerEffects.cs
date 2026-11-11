using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.Gamemodes.Random;

public sealed partial class CaorenCupRandomPlugin
{
    public CaorenCupPlayerEffects? GetEffects(CCSPlayerController player)
    {
        if (!IsRunning || !_match.RoundOpen || !Participant(player)) return null;
        TrackIdentity(player);
        var id = Id(player);
        var fireLevel = _match.Level(id, "fire_resist");
        var switchLevel = _match.Level(id, "fast_switch");
        var triggerLevel = _match.Level(id, "fast_trigger");
        var tagLevel = _match.Level(id, "anti_tag");
        var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value?.DesignerName;
        // 排除栓动狙击、泵动霰弹枪及非枪械。
        var fireSpeed = weapon is "weapon_awp" or "weapon_ssg08" or "weapon_nova" or "weapon_sawedoff" ||
            !CaorenCupDamageSources.IsGun(weapon) ? 100 : triggerLevel switch { 1 => 108, 2 => 112, _ => 100 };
        return new(id, _match.Round, _match.Level(id, "extra_jump"), _match.ExtraJumpsUsed(id),
            FireScale: fireLevel switch { 1 => 0.75f, 2 => 0.6f, _ => 1 },
            SwitchSpeedPercent: switchLevel switch { 1 => 100 / 0.85f, 2 => 100 / 0.75f, _ => 100 },
            FireSpeedPercent: fireSpeed,
            MovePenaltyMultiplier: _match.Level(id, "mobile_accuracy") switch { 1 => 0.85f, 2 => 0.75f, _ => 1 },
            RecoilMultiplier: _match.Level(id, "recoil_control") switch { 1 => 0.85f, 2 => 0.75f, _ => 1 },
            TagReduction: tagLevel switch { 1 => 0.25f, 2 => 0.4f, _ => 0 });
    }

    public void ReportExtraJumpsUsed(CCSPlayerController player, int used)
    {
        if (Participant(player)) _match.SetExtraJumpsUsed(Id(player), used);
    }
}
