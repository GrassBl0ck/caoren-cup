# Plugin Web Bridge

`web-command-center/CaorenCupPlugin/` 是 CS2 与网页指挥台之间的桥接插件。

## 1. Purpose

桥接插件负责把 CS2 服务器中的比赛数据同步到网页后端。

同步内容包括：

- heartbeat：插件心跳
- bind：玩家绑定
- snapshot：当前快照
- round_start：回合开始
- player_death：玩家死亡
- player_hurt：玩家受伤
- round_end：回合结束

## 2. Player Binding

网页端生成绑定码后，玩家在 CS2 聊天中输入：

```text
!ccbind 1234
```

插件会把玩家 SteamID64 与网页玩家身份绑定。

## 3. Config

复制示例配置：

```bash
cp caoren_config.example.json caoren_config.json
```

配置示例：

```json
{
  "CommandCenterBaseUrl": "http://127.0.0.1:3000",
  "PluginToken": "CHANGE_ME_TO_THE_SAME_VALUE_AS_BACKEND_PLUGIN_TOKEN",
  "HeartbeatSeconds": 30,
  "EnableDebugLog": false
}
```

`PluginToken` 必须与网页端 `PLUGIN_TOKEN` 一致。

## 4. Security

不要把 `caoren_config.json` 提交到 GitHub。仓库里只保留 `caoren_config.example.json`。
