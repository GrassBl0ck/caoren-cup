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

## 2. Player Web Login

当前推荐流程是游戏内取码登录：

```text
!cclogin
```

或：

```text
!cccode
```

插件会把玩家 SteamID64 和游戏内昵称发送到网页端，网页端返回一个可重复使用的网页登录码。插件会在聊天框用分隔线突出显示这个码，并同时在屏幕中央提示一次。玩家把这个码输入网页后点击“加入大厅”或按 Enter 即可进入大厅；网页掉线后可用同一个码恢复，直到码过期、网页进程重启或玩家重新获取新码。

旧的绑定命令仍保留兼容：

```text
!ccbind 1234
```

但普通玩家进入大厅不再需要先手动填写网页昵称或绑定码。

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

网页登录还需要在网页端环境变量中配置服务器连接地址：

```bash
GAME_SERVER_CONNECT_URL=steam://connect/<ip>:<port>
# 可选：默认 21600 秒
GAME_LOGIN_CODE_TTL_SECONDS=21600
# 可选：默认 15000 毫秒
PLUGIN_ONLINE_TTL_MS=15000
```

## 4. Security

不要把 `caoren_config.json` 提交到 GitHub。仓库里只保留 `caoren_config.example.json`。

## 7. v1.3.6 Login Code Display

`!cclogin` / `!cccode` 成功后，插件会显示类似下面的聊天提示：

```text
[草人杯] =================================
[草人杯]  你的网页登录码： DTK8XY
[草人杯]  请立即回网页输入这个码进入大厅
[草人杯]  网页掉线后也可继续使用这个码恢复
[草人杯]  有效期：约 6 小时；重新获取新码后旧码失效
[草人杯] =================================
```

同时会显示中央提示：

```text
网页登录码：DTK8XY
请回网页输入
```

这只是提示增强，不改变网页码生成、有效期、恢复登录或管理员登录逻辑。
