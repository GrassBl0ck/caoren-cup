# Plugin Web Bridge

`cs/web-bridge/CaorenCupPlugin/` 是 CS2 与网页指挥台之间的桥接插件。

## 1. Purpose

桥接插件负责把 CS2 服务器中的比赛数据同步到网页后端。

同步内容包括：

- heartbeat：插件心跳
- snapshot：当前快照
- round_start：回合开始
- player_death：玩家死亡
- player_hurt：玩家受伤
- round_end：回合结束

## 2. 玩家登录与 CS 身份绑定

玩家通过邮箱验证码注册，用邮箱和密码登录。在 CS 板块点击“加入本场比赛”才创建本场 Membership。桥接插件只用完全一致的 `CCSPlayerController.SteamID` 确认参赛身份。

首次参加 CS 比赛时，在游戏聊天栏输入以下命令：

```text
/ccbind
```

插件使用可信 SteamID64 生成一次性绑定码。玩家登录网站后，在“个人资料”的“CS 游戏身份”中填入绑定码。忘记密码可在首页用邮箱验证码重设。

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
GAME_SERVER_CONNECT_URL=steam://connect/play.caorencup.icu:27015
# 可选：默认 21600 秒
GAME_LOGIN_CODE_TTL_SECONDS=21600
# 可选：默认 15000 毫秒
PLUGIN_ONLINE_TTL_MS=15000
```

## 4. Security

不要把 `caoren_config.json` 提交到 GitHub。仓库里只保留 `caoren_config.example.json`。

玩家账号绑定的 SteamID 是长期可信身份。账号或桌面设备登录只建立玩家中心会话，不创建当前场次 Membership；玩家明确点击“加入本场比赛”后，插件仍只能确认完全一致的绑定，不能改绑或合并其他身份。生产环境设备凭证只允许通过 HTTPS 传输。同机游戏桥接继续使用 `http://127.0.0.1:3000` 内部接口，不传输玩家设备凭证；不要把内部端口开放给公网玩家。

## 7. CS 身份绑定码提示

`/ccbind` 成功后，插件会显示类似下面的聊天提示：

```text
[草人杯] =================================
[草人杯]  你的 CS 身份验证码： DTK8XY
[草人杯]  用邮箱登录草人杯网站，在“个人资料”的“CS 游戏身份”中输入此码绑定。
[草人杯]  此码仅能使用一次，验证成功后立即失效
[草人杯]  未使用时有效期：约 6 小时；重新获取新码后旧码失效
[草人杯] =================================
```

同时会显示中央提示：

```text
CS 身份验证码：DTK8XY
请在草人杯网站的个人资料页输入
```

绑定码使用后立即失效。未使用时按 `GAME_LOGIN_CODE_TTL_SECONDS` 设置的时长过期。
