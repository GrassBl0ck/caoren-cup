package com.caoren.crossera.core;

public final class CombatFailure extends RuntimeException {
    public enum Code { DISABLED, UNAUTHORIZED, OUTSIDE_WORLD, INVALID_SETUP, BACKEND_UNAVAILABLE,
        PLAYER_UNAVAILABLE, OVERRIDE_CONFLICT, INVALID_PROFILE }
    private final Code code;
    public CombatFailure(Code code, String message) { super(message); this.code = code; }
    public Code code() { return code; }
}
