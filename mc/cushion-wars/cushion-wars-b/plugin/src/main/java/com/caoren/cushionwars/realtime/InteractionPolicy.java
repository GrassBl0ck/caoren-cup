package com.caoren.cushionwars.realtime;

/** Native item use never grants permission to also operate the clicked block. */
final class InteractionPolicy {
    private InteractionPolicy() { }
    static boolean nativeItem(String role) {
        return MatchItems.ROLE_BOW.equals(role) || MatchItems.ROLE_HEAL.equals(role)
                || MatchItems.ROLE_SPLASH.equals(role);
    }
}
