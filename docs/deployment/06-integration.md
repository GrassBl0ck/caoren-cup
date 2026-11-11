# 网站与游戏联通

[返回目录](README.md) · [上一章](05-mc.md) · [下一章](07-networking.md)

## 1. 配置 CS 桥接

在服务端停服后，打开以下目录，复制示例配置为 `caoren_config.json`。

```text
cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/CaorenCupWebBridge/CaorenCupPlugin/
```

网站和 CS2 同机时填写以下 JSON，Token 与网站 `PLUGIN_TOKEN` 完全相同。

```json
{
  "CommandCenterBaseUrl": "http://127.0.0.1:3000",
  "PluginToken": "REPLACE_BRIDGE_TOKEN",
  "HeartbeatSeconds": 30,
  "EnableDebugLog": false
}
```

先启动网站，再重启 CS2。查看网站和插件日志，网页桥接状态应在数个心跳周期内显示在线。401 表示认证失败，连接拒绝通常表示地址、端口或网站进程有误。

分机时先在网站机器部署有效 HTTPS，把 `CommandCenterBaseUrl` 改为 `https://cup.example.com`。从 CS2 机器请求这个地址，应能完成 DNS、TLS 和 HTTP 连接。桥接与浏览器各自使用可达地址，填写 localhost 时目标永远是调用者自己。

## 2. 区分四种身份

| 身份 | 配置或验证位置 |
|---|---|
| 网站账户 | 邮箱注册、账户密码与恢复流程 |
| 网页管理员 | 网站 `ADMIN_PASSWORD`，按页面管理员入口使用 |
| CS 玩家身份 | 进入 CS2 后获取 `/cclogin` 一次性码，在网页账户设置中绑定或恢复 |
| CS 游戏管理员 | CSS 的 `configs/admins.json` 与 SteamID64 |

首次注册要确认邮件实际收到。登录网站后再进入 CS2，在游戏聊天使用 `/cclogin`，按网页提示完成身份验证，然后明确加入本场比赛。账号登录、加入场次、桥接确认游戏身份分别检查。

## 3. 最小联通测试

1. 打开网站首页，注册并登录自己的测试账户。
2. 进入 CS 页面，检查桥接在线状态，点击连接按钮应连接自己的游戏地址。
3. 游戏中取得一次性码并在网页使用，确认显示正确玩家身份。
4. 明确加入当前比赛，检查网页与游戏名单一致。
5. 管理员创建一个测试场次，核对所选模式，再做一个可恢复的赛前设置并查看游戏实际结果。
6. 用测试玩家完成一局，检查阶段推进、回合同步与赛后状态，再恢复测试设置。

每一步记录预期、实际与日志。网页显示操作成功后，继续核对游戏是否执行。测试完整比赛需要实际玩家配合，静态检查和桥接心跳各自只证明覆盖的部分。

## 4. MC 与网站

配置网站 `MC_SERVER_ADDRESS` 和 `MC_SERVER_VERSION` 后重启网页，检查 `/mc` 页面展示的地址和说明，再用正版 Java 客户端连接该地址。当前网站提供 MC 介绍与入服指引，MC 账号验证由游戏服务端承担。MC 玩法启停与自动战绩等能力按各玩法已有实现核对。

## 5. 联通故障顺序

先检查网站是否启动，再检查 CS 插件是否加载，然后从插件机器检查网络、HTTPS、Token，最后检查场次与身份。调试日志仅在排错时按需开启，分享日志前隐藏身份资料、邮箱和秘密值。
