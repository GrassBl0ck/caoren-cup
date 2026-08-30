# Plugin Web Bridge

`web-command-center/CaorenCupPlugin/` 是 CS2 与网页指挥台之间的桥接插件。

## 1. Purpose

桥接插件负责把 CS2 服务器中的比赛数据同步到网页后端。

同步内容包括：

- heartbeat：插件心跳
- snapshot：当前快照
- round_start：回合开始
- player_death：玩家死亡
- player_hurt：玩家受伤
- round_end：回合结束

## 2. Player Web Login

玩家统一使用草人杯账号和密码登录玩家中心。网页登录不会创建本场 Membership；玩家必须明确点击“加入本场比赛”。桥接插件在可信快照中读取完全一致的 `CCSPlayerController.SteamID`，只更新本场确认状态，不修改长期 SteamID 绑定。

新玩家开户或忘记账号、密码时，在游戏内使用唯一恢复命令：

```text
!cclogin
```

游戏码由插件使用可信 SteamID64 获取，只能成功消费一次；验证成功后立即失效，未使用时按配置的有效期过期。玩家在网页输入该码后完成开户，或进入账号恢复流程。

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

玩家账号绑定的 SteamID 是长期可信身份。账号或桌面设备登录只建立玩家中心会话，不创建当前场次 Membership；玩家明确点击“加入本场比赛”后，插件仍只能确认完全一致的绑定，不能改绑或合并其他身份。当前生产环境明确接受设备 Bearer Token 通过 HTTP 的窃取风险，轮换、短时票据和撤销不能替代 HTTPS。

## 7. v1.3.6 Login Code Display

`!cclogin` 成功后，插件会显示类似下面的聊天提示：

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
