package com.caoren.cushionwars.game;
import com.caoren.cushionwars.probe.*;
import java.util.UUID;
import org.bukkit.configuration.file.YamlConfiguration;
public final class ConfigApTest{
    private static int checks;
    private static void expect(boolean ok,String message){checks++;if(!ok)throw new AssertionError(message);}
    public static void main(String[] args){
        var yaml=new YamlConfiguration();
        var original=MatchSettings.load(yaml);
        expect(original.personalAp()==2&&original.shared(Side.RED,4)==4,"原默认保持");
        yaml.set("rules.personal-ap",7);yaml.set("rules.red-shared-ap",3);
        var next=MatchSettings.load(yaml);
        expect(original.personalAp()==2,"当前局快照不随保存改变");
        expect(next.personalAp()==7&&next.shared(Side.RED,8)==3&&next.shared(Side.BLUE,8)==8,"下一局与自动共享");
        for(double bad:new double[]{Double.NaN,Double.POSITIVE_INFINITY,0,-1,2000}){
            boolean rejected=false;try{new MatchSettings(2,-1,-1,40,bad,30);}catch(IllegalArgumentException e){rejected=true;}
            expect(rejected,"非法生命拒绝："+bad);
        }
        boolean rejected=false;try{new MatchSettings(2,0,0,41,40,30);}catch(IllegalArgumentException e){rejected=true;}
        expect(rejected,"初始生命不超过上限");
        var state=new ProbeState(ProbeConfig.load(new YamlConfiguration()));
        UUID p=UUID.randomUUID(),q=UUID.randomUUID();state.setSide(p,Side.RED);state.setSide(q,Side.RED);
        state.setPersonalAp(p,0);state.setPersonalAp(q,0);state.setSharedAp(Side.RED,1);
        var debit=state.debit(p);
        expect(debit!=null&&!debit.personal()&&state.sharedAp(Side.RED)==0,"共享最后一点只扣一次");
        expect(state.debit(q)==null,"第二个竞争者失败不扣 AP");
        state.refund(debit);
        expect(state.sharedAp(Side.RED)==1&&state.personalAp(p)==0,"共享退回原池，不能生成个人 AP");
        state.setPersonalAp(p,1);debit=state.debit(p);
        expect(debit.personal()&&state.sharedAp(Side.RED)==1,"个人优先");
        state.refund(debit);expect(state.personalAp(p)==1&&state.sharedAp(Side.RED)==1,"个人退款不修改共享");
        expect(state.debit(UUID.randomUUID())==null,"未入队拒绝");
        for(int n=1;n<=8;n++)expect(BoardLayout.size(n)==4*n+15&&BoardLayout.spawnSlots(n).size()==2*n,"人数缩放 "+n);
        expect(BoardLayout.coverCells(4)==52,"4v4 内置掩体基准");
        System.out.println("PASS "+checks+" config/AP checks");
    }
}
