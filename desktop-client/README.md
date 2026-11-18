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
CaorenCupClient-桌面客户端-v2.2.4.exe
```

第一版未配置代码签名，Windows 可能显示未知发布者提示。

## 玩家使用

1. 打开客户端，在首页用邮箱验证码注册，之后使用邮箱和密码登录。
2. 登录后可从右上角“个人资料”管理密码及 CS 游戏身份。参加 CS 比赛前，在游戏服务器输入 `/cclogin`，把一次性码填写到资料页完成身份绑定。
3. “记住此设备”会加密保存设备凭据；本次账户重置后旧设备登录将失效，需要重新注册并登录。
4. 进入 CS 板块后点击“加入本场比赛”；按钮不可用时查看旁边的具体原因。
5. 管理员通过独立 `/admin` 入口使用管理员密码，不需要玩家账户。
6. “连接 CS2”通过客户端主进程交给 Steam 打开；启动失败可复制服务器命令到 CS2 控制台。

## 注意

- 第一版只支持 Windows。
- 客户端不会访问 Steam Community、Steam Web API 或 OpenID，也不会读取本机 Steam 密码、Cookie 或 Steam 令牌。
- 客户端不读取本机 Steam 账号。玩家身份中的 SteamID64 由 CS2 桥接插件可信上报，并继续用于本场确认和换肤权限。
- 完整设备令牌只留在主进程；页面只接收短时单次玩家中心引导票据。
- 默认网站为 `https://caorencup.icu/`。设备凭证在公网只通过 HTTPS 传输；HTTP 仅允许本机开发地址。网页不再自行保存密码，浏览器会话保持登录，客户端设备凭据仍由主进程加密保存。
- `nodeIntegration` 保持关闭，`contextIsolation` 和 sandbox 保持开启；文件与凭据能力只通过固定 preload/IPC 提供。
- `steam://connect/...` 会交给系统打开，用于连接 CS2 服务器。
- 普通“退出玩家中心”只清除网页 Cookie；桌面端“退出账号并忘记此设备”会尝试撤销当前设备令牌并清除本机 `safeStorage` 凭据。

## 验证

```powershell
npm run check
npm test
```
