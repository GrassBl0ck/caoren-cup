package com.caoren.cushionwars.realtime.rules;

/** 单名参赛者的纯规则状态。位置是棋盘相对格坐标。 */
public final class PlayerState {
    public static final int INITIAL_CUSHIONS = 3;
    public static final int MAX_CUSHIONS = 3;
    public static final int INITIAL_DOORS = 0;
    public static final int MAX_DOORS = 2;
    public static final int INITIAL_TIPPED_ARROWS = 2;
    public static final int INITIAL_HEALING = 1;
    public static final int INITIAL_SPLASH = 1;
    public static final int INITIAL_HEALTH = 40;

    private final String id;
    private final String name;
    private Side side;
    private int spawnIndex;
    private int x;
    private int z;
    private boolean alive = true;
    private boolean connected = true;
    private boolean spectator;
    private double health = INITIAL_HEALTH;
    private double maxHealth = INITIAL_HEALTH;
    private final ApPool ap = new ApPool();
    private int cushions = INITIAL_CUSHIONS;
    private int doors = INITIAL_DOORS;
    private int tippedArrows = INITIAL_TIPPED_ARROWS;
    private int healingPotions = INITIAL_HEALING;
    private int splashPotions = INITIAL_SPLASH;
    private int lastSwitchTick = -10_000;
    private int seatSwitchSuccesses;

    record ActionState(int x, int z, int cushions, int doors, int tipped, int heal, int splash,
                       int lastSwitch, int switches, ApPool.Snapshot ap) { }

    ActionState actionState() {
        return new ActionState(x, z, cushions, doors, tippedArrows, healingPotions, splashPotions,
                lastSwitchTick, seatSwitchSuccesses, ap.snapshot());
    }

    void restoreAction(ActionState s) {
        x = s.x(); z = s.z(); cushions = s.cushions(); doors = s.doors();
        tippedArrows = s.tipped(); healingPotions = s.heal(); splashPotions = s.splash();
        lastSwitchTick = s.lastSwitch(); seatSwitchSuccesses = s.switches(); ap.restore(s.ap());
    }

    public PlayerState(String id, String name, Side side) {
        this.id = id;
        this.name = name;
        this.side = side;
    }

    public String id() {
        return id;
    }

    public String name() {
        return name;
    }

    public Side side() {
        return side;
    }

    void setSide(Side side) {
        this.side = side;
    }

    public int spawnIndex() {
        return spawnIndex;
    }

    void setSpawnIndex(int spawnIndex) {
        this.spawnIndex = spawnIndex;
    }

    public int x() {
        return x;
    }

    public int z() {
        return z;
    }

    public GridPos position() {
        return new GridPos(x, z);
    }

    void setPosition(int x, int z) {
        this.x = x;
        this.z = z;
    }

    public boolean alive() {
        return alive;
    }

    void setAlive(boolean alive) {
        this.alive = alive;
    }

    public boolean connected() {
        return connected;
    }

    void setConnected(boolean connected) {
        this.connected = connected;
    }

    public boolean spectator() {
        return spectator;
    }

    void setSpectator(boolean spectator) {
        this.spectator = spectator;
    }

    public double health() {
        return health;
    }

    void setHealth(double health) {
        this.health = health;
    }

    public double maxHealth() {
        return maxHealth;
    }

    void setMaxHealth(double maxHealth) {
        this.maxHealth = maxHealth;
    }

    public ApPool ap() {
        return ap;
    }

    public int cushions() {
        return cushions;
    }

    void setCushions(int cushions) {
        this.cushions = cushions;
    }

    public int doors() {
        return doors;
    }

    void setDoors(int doors) {
        this.doors = doors;
    }

    public int tippedArrows() {
        return tippedArrows;
    }

    void setTippedArrows(int tippedArrows) {
        this.tippedArrows = tippedArrows;
    }

    public int healingPotions() {
        return healingPotions;
    }

    void setHealingPotions(int healingPotions) {
        this.healingPotions = healingPotions;
    }

    public int splashPotions() {
        return splashPotions;
    }

    void setSplashPotions(int splashPotions) {
        this.splashPotions = splashPotions;
    }

    public int lastSwitchTick() {
        return lastSwitchTick;
    }

    void setLastSwitchTick(int lastSwitchTick) {
        this.lastSwitchTick = lastSwitchTick;
    }

    public int seatSwitchSuccesses() {
        return seatSwitchSuccesses;
    }

    void countSeatSwitch() {
        seatSwitchSuccesses++;
    }

    /** 当前生命扣减；不处理死亡判定，由同一批次统一处理。 */
    void damage(double amount) {
        health = Math.max(0, health - amount);
    }

    /** 治疗存活者，受当前最大生命约束。 */
    void heal(double amount) {
        if (!alive) {
            return;
        }
        health = Math.min(maxHealth, health + amount);
    }

    void resetForMatch() {
        alive = true;
        spectator = false;
        health = INITIAL_HEALTH;
        maxHealth = INITIAL_HEALTH;
        cushions = INITIAL_CUSHIONS;
        doors = INITIAL_DOORS;
        tippedArrows = INITIAL_TIPPED_ARROWS;
        healingPotions = INITIAL_HEALING;
        splashPotions = INITIAL_SPLASH;
        lastSwitchTick = -10_000;
        seatSwitchSuccesses = 0;
        ap.reset();
    }
}
