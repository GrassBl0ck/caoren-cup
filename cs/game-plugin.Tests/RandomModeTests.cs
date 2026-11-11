using CaorenCup.Contracts;
using CaorenCup.Gamemodes.Random;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class RandomModeTests
{
    private static RandomMatch Match() { var m = new RandomMatch(RandomCatalog.Cards.Where(c => c.Quality == "D").Select(c => c with { Cost = 1 }).ToArray(), roundCoupons: 1); m.Start(); m.BeginRound(1, ["p"]); return m; }
    private static void Buy(RandomMatch m, string key) => Assert.True(m.Purchase("p", key, m.View("p", true)!.Revision, true).Success);
    [Fact] public void Duplicate_round_and_reconnect_do_not_mint_coupons_and_half_keeps_upgrades()
    {
        var m = Match(); Buy(m, RandomCatalog.Reload);
        Assert.False(m.BeginRound(1, ["p"])); Assert.Equal(0, m.View("p", true)!.Coupons);
        Assert.True(m.EndRound(1)); Assert.False(m.EndRound(1));
        for (var n = 2; n <= 13; n++) { Assert.True(m.BeginRound(n, ["p"])); if(n < 13) m.EndRound(n); }
        var v = m.View("p", true)!; Assert.Equal(12, v.Coupons); Assert.Single(v.Owned); Assert.Equal(1, v.Owned[0].Level);
        Assert.Equal(0, m.View("late", true)!.Coupons);
        for (var n = 14; n <= 24; n++) { m.EndRound(n-1); Assert.True(m.BeginRound(n, ["p"])); }
        Assert.False(m.BeginRound(25, ["p"])); m.EndRound(24); Assert.False(m.View("p", true)!.CanPurchase);
    }
    [Fact] public void Purchase_is_one_per_round_and_stale_transaction_cannot_charge_again()
    {
        var m = Match(); var v = m.View("p", true)!;
        Assert.False(m.Purchase("p", "bogus", v.Revision, true).Success);
        Assert.False(m.Purchase("p", RandomCatalog.Reload, v.Revision, false).Success);
        Assert.True(m.Purchase("p", RandomCatalog.Reload, v.Revision, true).Success);
        Assert.False(m.Purchase("p", RandomCatalog.Medkit, v.Revision, true).Success);
        Assert.False(m.Purchase("p", RandomCatalog.Medkit, m.View("p", true)!.Revision, true).Success);
        Assert.Equal(0, m.View("p", true)!.Coupons);
    }
    [Fact] public void Exchange_is_atomic_limited_and_unused_allowance_does_not_accumulate()
    {
        var m = Match(); int money=999;
        bool Debit(int n) { if(money<n)return false; money-=n; return true; }
        var rev=m.View("p", true)!.Revision;
        Assert.False(m.Exchange("p", rev, true, Debit).Success); Assert.Equal(1,m.View("p",true)!.Coupons);
        money=3000; Assert.True(m.Exchange("p",rev,true,Debit).Success);
        Assert.False(m.Exchange("p",m.View("p",true)!.Revision,true,Debit).Success); Assert.Equal(2000,money);
        m.EndRound(1); m.BeginRound(2,["p"]); m.EndRound(2); m.BeginRound(3,["p"]);
        Assert.True(m.Exchange("p",m.View("p",true)!.Revision,true,Debit).Success);
        Assert.False(m.Exchange("p",m.View("p",true)!.Revision,true,Debit).Success); Assert.Equal(1000,money);
    }
    [Fact] public void Reroll_never_overlaps_and_small_pool_does_not_charge()
    {
        var m=Match(); Assert.False(m.View("p",true)!.CanReroll);
        Assert.False(m.Reroll("p",m.View("p",true)!.Revision,true).Success); Assert.Equal(1,m.View("p",true)!.Coupons);
        var cards=Enumerable.Range(1,6).Select(i=>new BuffDefinition("k"+i,"D-"+i,"卡"+i,"防御","简","详",1)).ToArray();
        m=new RandomMatch(cards, roundCoupons: 1);m.Start();m.BeginRound(1,["p"]);var before=m.View("p",true)!;
        Assert.True(m.Reroll("p",before.Revision,true).Success);var after=m.View("p",true)!;
        Assert.Empty(before.Offers.Select(x=>x.Key).Intersect(after.Offers.Select(x=>x.Key)));Assert.Equal(0,after.Coupons);
        Assert.False(m.Reroll("p",after.Revision,true).Success);
    }
    [Fact] public void Unique_and_max_level_leave_pool_and_overview_counts_distinct_cards()
    {
        var m=Match();Buy(m,RandomCatalog.Ghost);m.EndRound(1);m.BeginRound(2,["p"]);
        Assert.Equal(2,m.View("p",true)!.Offers.Count);
        for(int round=2;round<=4;round++){Buy(m,RandomCatalog.Reload);m.EndRound(round);m.BeginRound(round+1,["p"]);}
        var v=m.View("p",true)!;Assert.Single(v.Offers);Assert.Equal(2,v.Owned.Count);
        Assert.Equal(3,v.Owned.Single(c=>c.Key==RandomCatalog.Reload).Level);
        Assert.Equal(25,m.ReloadArmor("p",0,true));
    }
    [Fact] public void Reload_requires_real_transfer_and_consumes_first_even_at_full_armor()
    {
        var m=Match();Buy(m,RandomCatalog.Reload);
        Assert.Equal(5,m.ReloadArmor("p",5,false));Assert.Equal(100,m.ReloadArmor("p",100,true));Assert.Equal(5,m.ReloadArmor("p",5,true));
        m.EndRound(1);m.BeginRound(2,["p"]);Assert.Equal(100,m.ReloadArmor("p",95,true));
        var o=new ReloadObservation(10,90);Assert.False(o.Observe(10,90,true));Assert.True(o.Observe(30,70,false));
        o=new(10,90);Assert.False(o.Observe(10,90,true));Assert.False(o.Observe(10,90,false));Assert.True(o.Finished);Assert.False(o.Observe(30,70,false));
        o=new(10,90);Assert.False(o.Observe(30,90,true)); // scripted clip refill
        o=new(10,90);Assert.False(o.Observe(30,70,false)); // no native reload state
    }
    [Theory] [InlineData(49,true,64)] [InlineData(50,true,50)] [InlineData(0,true,0)] [InlineData(49,false,49)]
    public void Medkit_respects_source_alive_and_threshold(int hp,bool enemy,int expected)
    {var m=Match();Buy(m,RandomCatalog.Medkit);Assert.Equal(expected,m.HealAfterDamage("p",hp,100,enemy));}
    [Fact] public void Medkit_caps_health_and_upgrade_does_not_reset_used_charge()
    {var m=Match();Buy(m,RandomCatalog.Medkit);Assert.Equal(55,m.HealAfterDamage("p",49,55,true));Assert.Equal(10,m.HealAfterDamage("p",10,100,true));}
    [Fact] public void Ghost_requires_enemy_once_per_round_and_old_views_cannot_buy_after_restart()
    {
        var m=Match();Buy(m,RandomCatalog.Ghost);Assert.False(m.GhostOnDeath("p",false));Assert.True(m.GhostOnDeath("p",true));Assert.False(m.GhostOnDeath("p",true));
        var rev=m.View("p",true)!.Revision;m.Stop();Assert.Null(m.View("p",true));Assert.False(m.GhostOnDeath("p",true));
        m.Start();m.BeginRound(1,["p"]);Assert.False(m.Purchase("p",RandomCatalog.Reload,rev,true).Success);
    }
    [Fact] public void Fixed_rules_cannot_be_overridden_and_provider_hooks_are_complete()
    {
        int starts=0,stops=0;var p=new RandomGameModeProvider(()=>GameModeResult.Ok("ok"),()=>starts++,()=>stops++);
        var d=p.Descriptor;Assert.Equal("24",d.Cvars.Single(x=>x.Name=="mp_maxrounds").DefaultValue);
        Assert.False(GameModeCvarValidator.Validate(d,new Dictionary<string,string>{{"mp_maxrounds","13"}},true).Success);
        Assert.False(GameModeCvarValidator.Validate(d,new Dictionary<string,string>{{"mp_halftime","0"}},false).Success);
        var commands=new List<string>();var context=new GameModeRuntimeContext{ExecuteServerCommand=commands.Add,GetCurrentMap=()=>"custom",Notify=_=>{}};
        p.OnWarmupStarted(context);p.OnLiveStarted(context);p.OnStopped(context);Assert.Equal(1,starts);Assert.Equal(2,stops);Assert.Contains("mp_warmup_end",commands);
    }
    [Fact] public void Existing_overcap_attributes_are_never_reduced_and_performance_does_not_mint_coupons()
    {
        var m=Match();m.Record("p","kill");m.Record("p","damage",500);m.Record("p","plant");
        Assert.Equal(1,m.View("p",true)!.Coupons);Buy(m,RandomCatalog.Reload);
        Assert.Equal(150,m.ReloadArmor("p",150,true));
        m.EndRound(1);m.BeginRound(2,["p"]);Buy(m,RandomCatalog.Medkit);
        Assert.Equal(40,m.HealAfterDamage("p",40,0,true)); // invalid cap must not consume a charge
        Assert.Equal(55,m.HealAfterDamage("p",40,100,true));
    }
}
