package cn.caoren.spiritsky;
/** Pure game rules; time is monotonically increasing server ticks (20 ticks = one game second). */
public final class Rules {
    private Rules() {
    }
    /** Loaded once before the game exists. Bounds guard configuration mistakes, not balance. */
    public enum Param {
        QUIET_TICKS(140,1,12000), SUPPLY_TICKS(60,1,12000), COUNTDOWN_TICKS(200,1,12000),
        BASE_HP(500,1,1024), STATION_HP(80,1,1024), SHIP_HP(128,1,1024),
        BASE_DAMAGE(6,.01,100), STATION_DAMAGE(3,.01,100),
        BASE_SHOT_TICKS(30,1,12000), STATION_SHOT_TICKS(20,1,12000),
        BASE_RANGE(30,1,100), STATION_RANGE(20,1,100), SUPPORT_RANGE(12,1,100), DEPLOY_RANGE(6,1,30),
        DEPLOY_TICKS(400,1,12000), WAVE_TICKS(600,1,12000), STAGE_TICKS(3600,1,72000),
        PLAYER_RESPAWN_TICKS(200,1,12000), PLAYER_RESPAWN_STEP_TICKS(15,1,12000),
        SHIP_RESPAWN_TICKS(240,1,12000), SHIP_RESPAWN_STEP_TICKS(20,1,12000),
        BASE_HEAL_FRACTION(.01,.001,1), STATION_HEAL_FRACTION(.005,.001,1),
        SHIP_HEAL_FRACTION(.1,.001,1), ARROW_DAMAGE(8,.01,100),
        BASE_ARROW_FACTOR(.5,.001,1), BASE_UNSUPPORTED_FACTOR(.1,.001,1), STATION_UNSUPPORTED_FACTOR(.5,.001,1),
        MINION_MELEE_HP(12,1,256), MINION_RANGED_HP(8,1,256), MINION_DAMAGE(2,.01,100), MINION_SPEED(2.2,.1,5),
        FLIGHT_LOW_FACTOR(1.5,.1,3), FLIGHT_MID_FACTOR(1,.1,3), FLIGHT_HIGH_FACTOR(.7,.1,3),
        FLIGHT_HURT_FACTOR(.8,.1,1), FLIGHT_BOOST_FACTOR(1.3,1,3),
        ARROW_CAP(8,1,64), ARROW_TICKS(60,1,12000), HEAL_CAP(2,1,64), HEAL_TICKS(120,1,12000),
        RESIST_CAP(2,1,64), RESIST_TICKS(120,1,12000), SPEED_CAP(1,1,64), SPEED_TICKS(200,1,12000),
        STATION_CAP(2,1,64), STATION_TICKS(800,1,12000), STRENGTH_CAP(1,1,64), STRENGTH_TICKS(1200,1,12000);
        public final double initial,min,max;
        private double value;
        Param(double initial,double min,double max){
            this.initial=initial;
            this.value=initial;
            this.min=min;
            this.max=max;
        }
        public double v(){
            return value;
        }
        public int i(){
            return (int)value;
        }
        public String key(){
            return name().toLowerCase(java.util.Locale.ROOT).replace('_','-');
        }
    }
    public static void configure(java.util.Map<String,?> values){
        java.util.Map<Param,Double> checked=new java.util.EnumMap<>(Param.class);
        for(String key:values.keySet()){
            Param p=java.util.Arrays.stream(Param.values()).filter(x->x.key().equals(key)).findFirst().orElseThrow(()->new IllegalArgumentException("Unknown rule: "+key));
            Object raw=values.get(key);
            if(!(raw instanceof Number number))throw new IllegalArgumentException("Rule must be numeric: "+key);
            double n=number.doubleValue();
            if(!Double.isFinite(n)||n<p.min||n>p.max||((p.name().endsWith("_TICKS")||p.name().endsWith("_CAP"))&&n!=Math.rint(n)))throw new IllegalArgumentException("Rule out of range: "+key);
            checked.put(p,n);
        }
        for(Param p:Param.values())p.value=checked.getOrDefault(p,p.initial);
    }
    public enum Side {
        RED, BLUE;
        public Side enemy() {
            return this == RED ? BLUE : RED;
        }
    }
    public enum Role {
        DIRECTOR, ARCHOR, WITCH, DEFENDER, BERSERKER
    }
    public enum State {
        LOBBY, COUNTDOWN, RUNNING, ENDING, FAULT
    }
    public enum Stock {
        ARROW(8,60), HEAL(2,120), RESIST(2,120), SPEED(1,200), STATION(2,800), STRENGTH(1,1200);
        public final int cap, period;
        Stock(int cap,int period) {
            this.cap=cap;
            this.period=period;
        }
        public int capacity(){
            return Param.valueOf(name()+"_CAP").i();
        }
        public int interval(){
            return Param.valueOf(name()+"_TICKS").i();
        }
    }
    public static boolean uses(Role role, Stock stock) {
        return switch(role) {
            case ARCHOR -> stock==Stock.ARROW;
            case WITCH -> stock==Stock.HEAL || stock==Stock.RESIST || stock==Stock.SPEED;
            case DEFENDER -> stock==Stock.STATION;
            case BERSERKER -> stock==Stock.STRENGTH;
            default -> false;
        }
        ;
    }
    public static int stage(long elapsed) {
        return (int)Math.max(0,Math.min(4,elapsed/Param.STAGE_TICKS.i()));
    }
    public static double arrowDecay(int t) {
        return t<10 ? 1-.005*Math.max(0,t) : t<30 ? .9-.01*(t-10) : .7;
    }
    public static double arrowRaw(double force,int ticks) {
        return Param.ARROW_DAMAGE.v()*Math.clamp(force,0,1)*arrowDecay(ticks);
    }
    public static double buildingDamage(double raw,boolean base,boolean arrow,boolean supported,boolean player) {
        return raw*(base&&arrow?Param.BASE_ARROW_FACTOR.v():1)*(player&&!supported?(base?Param.BASE_UNSUPPORTED_FACTOR.v():Param.STATION_UNSUPPORTED_FACTOR.v()):1);
    }
    public static double nextHealBand(double hp,double max) {
        double step=max/4;
        return Math.min(max,Math.ceil(hp/step)*step);
    }
    public static int playerRespawn(long elapsed) {
        return Param.PLAYER_RESPAWN_TICKS.i()+Param.PLAYER_RESPAWN_STEP_TICKS.i()*stage(elapsed);
    }
    public static int shipRespawn(long elapsed) {
        return Param.SHIP_RESPAWN_TICKS.i()+Param.SHIP_RESPAWN_STEP_TICKS.i()*stage(elapsed);
    }
    public static double healthFactor(long elapsed) {
        return 1+.75*stage(elapsed);
    }
    public static double damageFactor(long elapsed) {
        return 1+.5*stage(elapsed);
    }
    public static double speedFactor(long elapsed) {
        return 1+.25*stage(elapsed);
    }
    /** Vanilla ground MoveControl uses speed both as acceleration and input magnitude. */
    public static double mobSpeedAttribute(double blocksPerSecond){
        return Math.sqrt(blocksPerSecond/(20*.98/(1-.6*.91)));
    }
    public static double flightSpeed(double height,boolean hurt,boolean boost) {
        return 5.612*(height<10?Param.FLIGHT_LOW_FACTOR.v():height<20?Param.FLIGHT_MID_FACTOR.v():Param.FLIGHT_HIGH_FACTOR.v())*(hurt?Param.FLIGHT_HURT_FACTOR.v():1)*(boost?Param.FLIGHT_BOOST_FACTOR.v():1);
    }
    /** 26.3 HappyGhast#getRiddenInput and travelFlying; native drag remains in entity travel. */
    public static double verticalAcceleration(double input,double flyingAttribute){
        return input*3.9*flyingAttribute*flyingAttribute*5/3;
    }
    public static String result(double red,double blue) {
        return red<=0 && blue<=0 ? "DRAW" : red<=0 ? "BLUE" : blue<=0 ? "RED" : "NONE";
    }
    /** Full inventory never banks recovery ticks. Restoring a saved kit preserves deadlines. */
    public static final class Supply {
        public int count;
        public long due;
        public final Stock type;
        public Supply(Stock s) {
            type=s;
            count=s.capacity();
        }
        public void spend(long now) {
            if(count<=0) throw new IllegalStateException("empty");
            if(count==type.capacity())due=now+type.interval();
            count--;
        }
        public void tick(long now) {
            if(count<type.capacity() && now>=due){
                count++;
                due=now+type.interval();
            }
        }
        public void fill(){
            count=type.capacity();
            due=0;
        }
        public Supply copy(){
            Supply s=new Supply(type);
            s.count=count;
            s.due=due;
            return s;
        }
    }
}
