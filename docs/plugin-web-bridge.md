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

新玩家现在先在桌面客户端输入本场邀请码和昵称，以临时参赛者进入大厅。客户端声明 SteamID 后，网页端会在桥接插件快照响应中返回一次性确认挑战；插件只向具有对应 `CCSPlayerController.SteamID` 的真实在线玩家显示 6 位确认码。玩家把该码输入客户端后，后端才建立永久 SteamID 绑定。

插件会在聊天框和屏幕中央显示类似：

```text
[草人杯]  本场 Steam 确认码： ABCD23
[草人杯]  请回到草人杯客户端输入，只需首次绑定时确认一次
```

原有游戏内取码继续作为恢复入口：

```text
!cclogin
```

或：

```text
!cccode
```

游戏码由插件使用可信 SteamID64 获取，可用于首次识别失败、换电脑、Steam 账号不一致和自动确认码未显示时的恢复。游戏码只能成功消费一次；验证成功后立即失效，未使用时则按配置的有效期过期。

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

客户端读取的 SteamID 不是可信身份。后端必须同时校验临时成员声明、插件可信 SteamID 和一次性确认码，不得仅因为某 SteamID 出现在在线快照中就自动合并身份。

## 7. v1.3.6 Login Code Display

`!cclogin` / `!cccode` 成功后，插件会显示类似下面的聊天提示：

```text
[草人杯] =================================
[草人杯]  你的网页登录码： DTK8XY
[草人杯]  请立即回网页输入这个码进入大厅
[草人杯]  这是单次登录码；网页验证成功后立即失效
[草人杯]  未使用时有效期：约 6 小时；重新获取新码后旧码失效
[草人杯] =================================
```

同时会显示中央提示：

```text
网页登录码：DTK8XY
请回网页输入
```

游戏码仍保留原有效期和管理员登录兼容，但玩家码在验证成功后会立即消费，不能重复用于签发设备凭据。
