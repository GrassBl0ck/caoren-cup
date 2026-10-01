# README 中的 v1.3 历史更新记录

> 本页保留旧 README 中的历史记录，描述当时版本，不是当前登录或部署教程。
> 当前玩家使用账号密码登录；游戏码只用于开户或恢复，成功验证后立即失效，玩家还需明确加入本场比赛。


## v1.3.2 维护说明

本版本主要是网页端维护更新，不改变游戏插件本体命令签名，也不改变桥接插件真实配置。

- 网页端：未启用 CaorenCup 修改时，隐藏修改模块配置区，只保留状态提示，避免页面误导管理员。
- 网页端：拆分 `match-options` 路由，减少 `web-command-center/src/server.ts` 体积，后续继续按路由模块拆分。
- 部署注意：本版本仍需保护服务器真实 `.env`、`ecosystem.config.cjs` 和 `caoren_config.json`，不要打进公开 Release 包。

## v1.3.3 game-code-login

- Added a web login entry for CS2 players.
- The login page can show bridge-plugin online status and open the configured Steam connect URL.
- Players can type `!cclogin` in the CS2 server to receive a temporary web login code.
- The same web input accepts a game login code, the admin password, or `spec`.
- Required web environment variables:
  - `GAME_SERVER_CONNECT_URL=steam://connect/<ip>:<port>`
  - `GAME_LOGIN_CODE_TTL_SECONDS=900`
  - `PLUGIN_ONLINE_TTL_MS=15000`
- Deploying the bridge plugin DLL requires a full CS2 server restart.

## v1.3.4 web Steam-code login

- Web login is now game-code first. The public login area keeps only one clickable button: **连接至草人杯服务器**. The status light beside it reflects whether the bridge plugin heartbeat is currently online.
- Configure `GAME_SERVER_CONNECT_URL` as `steam://connect/<ip>:<port>`. If only `<ip>:<port>` or `connect <ip>:<port>` is provided, the backend normalizes it to `steam://connect/...` for the browser. This Steam protocol URL lets Steam/CS2 handle the cases where the game or Steam is not already open.
- Players join the CS2 server, type `!cclogin` or `!cccode`, then enter the returned code in the web input and press Enter. The code can be reused for web reconnects until it expires, the web process restarts, or the player requests a new code.
- The web player name comes from the CS2 player name sent by the bridge plugin, so the lobby displays the player's Steam nickname instead of a manually typed web nickname.
- The same input accepts the admin password. A matching admin password joins the lobby directly as Admin.
- Default `GAME_LOGIN_CODE_TTL_SECONDS` is now `21600` seconds. It can still be overridden in the web environment.
- Deploying the bridge plugin DLL requires a full CS2 server restart.

## v1.3.5 web login usability hotfix

- The game-code login input now has an explicit **加入大厅** button. Pressing Enter still works, but players no longer need to remember the keyboard shortcut.
- The connection button remains **连接至草人杯服务器** and still uses `GAME_SERVER_CONNECT_URL`.
- If local PowerShell tests show Chinese names as `????`, send the JSON request body as UTF-8 bytes or use Unicode escapes. The CS2 bridge plugin sends JSON as UTF-8 through `PostAsJsonAsync`, so real Steam/CS2 nicknames should be preserved.

## v1.3.6 game login code visibility hotfix

- The CS2 bridge plugin now prints the web login code inside a clearly separated chat block so players do not miss the code.
- After `!cclogin` or `!cccode`, the plugin also shows a center-screen reminder with the login code and the instruction to return to the web page.
- The login behavior is unchanged: the code can still be reused for web reconnects until it expires, the web process restarts, or the player requests a new code.
- This is a bridge-plugin-only change. Deploying the updated DLL still requires a full CS2 server restart.

[返回项目首页](../../README.md)
