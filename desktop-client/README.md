# 草人杯桌面客户端

这是草人杯玩家中心 Windows 客户端。它打开已部署的网页指挥台，并在主进程中使用 Electron `safeStorage` 保存设备登录凭据。

## 开发预览

```powershell
cd D:\OpenSourcework\caoren-cup-open-source\desktop-client
npm install
$env:CAOREN_COMMAND_CENTER_URL="http://127.0.0.1:3000"
npm run dev
```

本目录的 `.npmrc` 已配置 Electron 相关二进制镜像，用来减少国内网络下载超时。

如果没有设置 `CAOREN_COMMAND_CENTER_URL`，客户端会显示“未配置草人杯指挥台地址”，不会打开错误网页。

## 正式打包

打包前把 [src/client-config.js](src/client-config.js) 里的 `COMMAND_CENTER_URL` 改成线上指挥台地址，例如：

```js
module.exports = {
  COMMAND_CENTER_URL: 'https://你的草人杯指挥台地址'
};
```

然后执行：

```powershell
npm run package:win
```

开发时仍可用 `CAOREN_COMMAND_CENTER_URL` 临时覆盖配置，不需要修改文件。

产物在 `desktop-client/dist/`，默认文件名类似：

```text
CaorenCupClient-桌面客户端-v1.0.0.exe
```

第一版未配置代码签名，Windows 可能显示未知发布者提示。

## 玩家使用

1. 打开 `CaorenCupClient-桌面客户端-vX.X.X.exe`。
2. 使用草人杯账号和密码登录玩家中心；没有账号或忘记凭据时，先在 CS2 服务器输入 `!cclogin` 获取一次性游戏码。
3. 回到玩家中心输入游戏码完成开户或恢复。SteamID64 由服务器插件可信读取，不作为登录方式。
4. 可主动勾选“记住此设备”。设备凭据由主进程通过 `safeStorage` 加密保存，以后打开客户端可自动进入玩家中心。
5. 登录玩家中心不会自动参赛；仍需手动点击“加入本场比赛”。

`!cclogin` 是新玩家开户和忘记账号或密码时的唯一恢复入口。

管理员使用同一个客户端，在输入框里输入管理员密码进入管理界面。

## 注意

- 第一版只支持 Windows。
- 客户端不会访问 Steam Community、Steam Web API 或 OpenID，也不会读取或上传密码、Cookie、Steam 令牌。
- 客户端不读取本机 Steam 账号。玩家身份中的 SteamID64 由 CS2 桥接插件可信上报，并继续用于本场确认和换肤权限。
- 完整设备令牌只留在主进程；页面只接收短时单次玩家中心引导票据。
- 当前生产站点明确允许设备 Bearer Token 通过 HTTP 自动登录。HTTP 不提供传输加密，局域网或链路攻击者可能窃取长期设备令牌；成功即轮换、短时单次引导票据、速率限制、审计和撤销只能降低泄露后果，不能替代 HTTPS。
- `nodeIntegration` 保持关闭，`contextIsolation` 和 sandbox 保持开启；文件与凭据能力只通过固定 preload/IPC 提供。
- `steam://connect/...` 会交给系统打开，用于连接 CS2 服务器。
- 普通“退出玩家中心”只清除网页 Cookie；桌面端“退出账号并忘记此设备”会尝试撤销当前设备令牌并清除本机 `safeStorage` 凭据。

## 验证

```powershell
npm run check
npm test
```
