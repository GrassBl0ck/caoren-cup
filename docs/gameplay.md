# Gameplay Overview

草人杯系统包含网页赛事流程和 CS2 插件玩法。

## 1. Web Match Flow

网页指挥台的大致流程：

```text
Lobby
CaptainSelection
Roll
PlayerDraft
MapBan
SidePick
PreGameSetup
LiveGame
PostGameAccusation
Scoreboard
```

含义：

1. 大厅等待玩家加入。
2. 管理员选择或确认队长。
3. 队长 Roll 点决定先后手。
4. 队长蛇形选人。
5. 地图 Ban/Pick。
6. 选择 CT/T 阵营。
7. 管理员分配身份。
8. 正式比赛，桥接插件同步比分和战绩。
9. 赛后指认卧底。
10. 结算页面统计分数。

## 2. Roles

支持的身份：

- Soldier：士兵
- Undercover：卧底
- Detective：侦探

## 3. Undercover Task Board

卧底任务为九宫格任务板，任务状态包括：

- Incomplete：未完成
- Partial：部分完成
- Complete：完成
- Abandoned：放弃

系统支持任务提示、任务替换、N 值任务和连线计分。

## 4. Game Plugin Features

`game-plugin/Features/` 中包含多个 CS2 娱乐玩法模块，例如：

- 击杀回血
- 持续流血/回血
- 火焰回血或增伤
- 二段跳
- 伤害倍率
- 友伤倍率
- FOV 调整
- ESP 发光
- 弹药/道具概率保留
- 武器速度控制
- 经济倍率
- 非对称一人成军模式

具体指令和配置请查看对应 Feature 源码与 `CaorenCupConfig.cs`。
