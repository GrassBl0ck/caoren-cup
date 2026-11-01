package com.caoren.cushionwars.realtime.rules;

import java.util.function.IntConsumer;

/** One shared boundary for timer callbacks and event callbacks. A tick closes only on entry to the next tick. */
public final class MatchClock {
    private final MatchEngine engine;
    private final long matchId;
    private int opened = -1;

    public MatchClock(MatchEngine engine, long matchId) {
        this.engine = engine;
        this.matchId = matchId;
    }

    public boolean enter(long eventMatchId, int now, IntConsumer onOpen) {
        if (eventMatchId != matchId || now < 0 || now < opened || !engine.isRunning()) return false;
        while (opened < now && engine.isRunning()) {
            if (opened >= 0 && engine.tickEnd(opened)) return false;
            engine.tickStart(++opened);
            if (!engine.isRunning()) return false;
            onOpen.accept(opened);
        }
        return engine.isRunning() && now < Timeline.DEADLINE_TICK;
    }
}
