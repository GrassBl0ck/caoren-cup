package com.caoren.cushionwars.game;

/** 一局的阶段。 */
public enum GamePhase {
    INTRO,
    /** 没有开局，玩家可以自由进出阵营。 */
    WAITING,
    /** 开局前 5 秒：锁定名单、入座、禁用道具。 */
    PREPARE,
    /** 某一方的行动阶段（最长 30 秒）。 */
    ACTION,
    /** 投射物结算（最多 5 秒）。 */
    RESOLVE,
    /** 结果已锁定，等待组织者收尾。 */
    ENDED
}
