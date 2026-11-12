package cn.caoren.spiritsky;
/** Runs with plain Java; assertions target rule boundaries and resource exploits, not implementation strings. */
public final class RuleTests {
    static int checks;
    static void eq(double expected,double actual){
        checks++;
        if(Math.abs(expected-actual)>1e-8)throw new AssertionError(expected+" != "+actual);
    }
    static void yes(boolean condition){
        checks++;
        if(!condition)throw new AssertionError("condition failed");
    }
    public static void main(String[] args){
        eq(1,Rules.arrowDecay(0));
        eq(.955,Rules.arrowDecay(9));
        eq(.9,Rules.arrowDecay(10));
        eq(.71,Rules.arrowDecay(29));
        eq(.7,Rules.arrowDecay(30));
        eq(.7,Rules.arrowDecay(500));
        eq(8,Rules.arrowRaw(1,0));
        eq(4,Rules.arrowRaw(.5,0));
        eq(.36,Rules.buildingDamage(Rules.arrowRaw(1,10),true,true,false,true));
        eq(3.6,Rules.buildingDamage(7.2,true,true,true,true));
        eq(3.6,Rules.buildingDamage(7.2,false,true,false,true));
        eq(7.2,Rules.buildingDamage(7.2,false,true,true,true));
        eq(2,Rules.buildingDamage(2,true,false,false,false));
        eq(125,Rules.nextHealBand(124,500));
        eq(125,Rules.nextHealBand(125,500));
        eq(250,Rules.nextHealBand(125.01,500));
        eq(500,Rules.nextHealBand(499,500));
        eq(500,Rules.nextHealBand(500,500));
        for(int i=0;i<=4;i++){
            long t=i*3600;
            eq(i,Rules.stage(t));
            eq(200+15*i,Rules.playerRespawn(t));
            eq(240+20*i,Rules.shipRespawn(t));
            eq(1+.75*i,Rules.healthFactor(t));
            eq(1+.5*i,Rules.damageFactor(t));
            eq(1+.25*i,Rules.speedFactor(t));
            if(i>0)eq(i-1,Rules.stage(t-1));
        }
        eq(4,Rules.stage(Long.MAX_VALUE));
        eq(48,12*Rules.healthFactor(14400));
        eq(32,8*Rules.healthFactor(14400));
        eq(6,2*Rules.damageFactor(14400));
        eq(4.4,2.2*Rules.speedFactor(14400));
        eq(5.612*1.5,Rules.flightSpeed(9.999,false,false));
        eq(5.612,Rules.flightSpeed(10,false,false));
        eq(5.612*.7,Rules.flightSpeed(20,false,false));
        eq(5.612*.7*.8*1.3,Rules.flightSpeed(30,true,true));
        eq(.01625,Rules.verticalAcceleration(1,.05));
        eq(.008125,Rules.verticalAcceleration(.5,.05));
        eq(0,Rules.verticalAcceleration(0,.05));
        eq(-.01625,Rules.verticalAcceleration(-1,.05));
        yes(Rules.result(0,0).equals("DRAW"));
        yes(Rules.result(0,1).equals("BLUE"));
        yes(Rules.result(1,0).equals("RED"));
        yes(Rules.result(1,1).equals("NONE"));
        for(Rules.Stock type:Rules.Stock.values()){
            Rules.Supply stock=new Rules.Supply(type);
            stock.tick(10000);
            eq(type.cap,stock.count);
            stock.spend(10000);
            eq(type.cap-1,stock.count);
            stock.tick(10000+type.period-1);
            eq(type.cap-1,stock.count);
            stock.tick(10000+type.period);
            eq(type.cap,stock.count);
            stock.spend(20000);
            Rules.Supply copy=stock.copy();
            eq(stock.count,copy.count);
            eq(stock.due,copy.due);
            stock.fill();
            eq(type.cap,stock.count);
            eq(type.cap-1,copy.count);
            stock.spend(30000);
            stock.tick(30000+type.period-1);
            eq(type.cap-1,stock.count);
        }
        eq(1,java.util.Arrays.stream(Rules.Stock.values()).filter(s->Rules.uses(Rules.Role.DEFENDER,s)).count());
        eq(3,java.util.Arrays.stream(Rules.Stock.values()).filter(s->Rules.uses(Rules.Role.WITCH,s)).count());
        Rules.configure(java.util.Map.of("arrow-damage",10,"arrow-cap",3,"arrow-ticks",40));
        eq(10,Rules.arrowRaw(1,0));
        Rules.Supply configured=new Rules.Supply(Rules.Stock.ARROW);
        eq(3,configured.count);
        configured.spend(0);
        configured.tick(39);
        eq(2,configured.count);
        configured.tick(40);
        eq(3,configured.count);
        for(var bad:java.util.List.of(java.util.Map.of("arrow-cap",0),java.util.Map.of("arrow-ticks",1.5),java.util.Map.of("base-hp",Double.NaN),java.util.Map.of("typo",1),java.util.Map.of("base-hp","500"))){
            boolean rejected=false;
            try{
                Rules.configure(bad);
            }
            catch(IllegalArgumentException expected){
                rejected=true;
            }
            yes(rejected);
            eq(10,Rules.arrowRaw(1,0));
        }
        Rules.configure(java.util.Map.of());
        eq(8,Rules.arrowRaw(1,0));
        System.out.println("RULE_TESTS_PASS checks="+checks);
    }
}
