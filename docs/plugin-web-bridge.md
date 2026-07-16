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

管理员可以预先创建固定成员账户，保存 SteamID64、昵称和 scrypt 密码凭据。固定成员使用 SteamID64 和密码进入当前大厅，不需要邀请码或游戏内确认操作。桥接插件在可信快照中看到完全相同的 `CCSPlayerController.SteamID` 后，只更新当前场次的确认状态，不修改长期 SteamID 绑定。

临时玩家继续使用邀请码和昵称，并同时提交 SteamID64 作为本场不可信声明。该声明只用于大厅提醒和现有确认挑战；只有服务器可信 SteamID 与确认码均匹配后，才会建立长期身份。

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

旧命令仅作为故障恢复入口，正常固定成员和临时玩家流程都不需要先填写网页绑定码。

桥接插件会定期读取当前网页大厅的 SteamID 集合。真实玩家连接 CS2、但其 SteamID 尚未进入当前网页大厅时，插件每秒在聊天区域提示先打开草人杯客户端。网页状态从未同步成功或缓存已过期时，插件停止所有此类提醒，避免网络故障时向全服误刷。Bot、HLTV 和无效玩家不会收到提醒。

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

固定成员的 SteamID 已由管理员预绑定。密码登录只创建或复用当前场次 Membership；插件只能确认完全一致的绑定，不能改绑或合并其他身份。固定密码登录不会签发设备令牌，设备自动登录仍要求 HTTPS/WSS。

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
