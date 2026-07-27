# 固定成员 SteamID64 + 独立密码登录设计

日期：2026-07-12

## 1. 目标与约束

本功能在现有大厅身份系统上增加由管理员预设的固定成员账户。固定成员使用 SteamID64 和独立密码进入当前大厅，不需要邀请码、游戏内确认码、`!cclogin` 或 `!ccbind`。CS2 桥接插件只使用服务器可信的 `CCSPlayerController.SteamID` 对当前 Session 的长期成员进行静默确认，绝不重新绑定或合并身份。

临时玩家继续使用邀请码、昵称和 SteamID64 进入临时大厅。临时玩家提交的 SteamID64 是不可信声明，只用于当前大厅提醒匹配和现有 CS2 确认挑战，不能直接建立长期绑定。

当前生产环境为 HTTP。固定成员密码仅在以下两类操作中允许通过 HTTP 传输：

- 管理员创建固定账户或重置固定成员密码；
- 固定成员使用 SteamID64 和密码登录。

该例外不适用于设备令牌、轮换令牌或自动登录。现有 HTTPS 限制必须继续生效，固定密码登录不得签发或返回设备令牌。

## 2. 模块边界

- `src/identity/identity-types.ts`：定义 schema v2、固定账户凭据、Identity 和 Membership 数据结构。
- `src/identity/identity-store.ts`：负责 v1 到 v2 迁移、主文件与 previous 恢复、原子持久化，不处理密码业务。
- `src/identity/password-auth.ts`：负责密码校验、scrypt 派生、恒定时间比较和登录失败限流。
- `src/identity/identity-service.ts`：负责固定账户管理、密码验证、当前 Session Membership 创建或复用，以及现有确认逻辑。
- `src/identity/auth-routes.ts`：提供固定成员登录和管理员固定账户写操作的 HTTP 接口。
- `src/socket-handlers.ts`：消费固定成员登录票据，签发管理员单次操作票据，并提供脱敏管理状态。
- `public/index.html`、`public/js/game-code-login.js`、`public/css/app.css`：提供固定成员登录、临时玩家登录和管理员账户管理界面。
- `src/plugin-api.ts`：通过现有插件认证接口提供当前 Session 已入场 SteamID 集合及状态时间。
- `CaorenCupPlugin`：定期同步网页状态，每秒执行提醒判定，不修改绑定、队伍锁或比赛逻辑。
- `CaorenCupPlugin.Tests`：测试独立的提醒判定纯逻辑，不依赖运行中的 CS2 服务器。

`desktop-client/` 不需要修改。网页变化会由现有 Electron 壳直接加载。

## 3. 数据结构

身份库升级为 schema v2。固定账户直接属于长期 Identity：

```ts
interface IdentityRecord {
    identityId: string;
    displayName: string;
    steamId?: string;
    fixedAccount?: {
        enabled: boolean;
        password: {
            algorithm: 'scrypt';
            salt: string;
            hash: string;
            params: {
                N: number;
                r: number;
                p: number;
                keyLength: number;
                maxmem: number;
            };
            updatedAt: number;
        };
    };
    createdAt: number;
    updatedAt: number;
}
```

密码参数固定为：

- `N = 16384`
- `r = 8`
- `p = 1`
- `keyLength = 64`
- 32 字节随机盐
- 盐和哈希使用 base64url 编码

密码长度为 8 到 128 个字符，允许中文、字母、数字和符号。服务端使用异步 `crypto.scrypt`，比较前检查 Buffer 长度，再调用 `crypto.timingSafeEqual`。持久化数据中不保存明文密码。

现有 `LobbyMembershipRecord` 不增加账户密码字段。固定成员登录创建的本场投影使用：

- `identityLevel = 'longTerm'`
- `claimedSteamId = identity.steamId`
- `confirmationState = 'pending'`

桥接插件看到完全相同的可信 SteamID 后，将该 Membership 更新为 `confirmed`。未看到时保持 `pending`，不推测不一致。

## 4. 迁移与恢复

- v1 主文件正常：验证现有集合后迁移为 v2，保留全部 Identity、Membership 和 DeviceToken。
- 现有 Identity 默认没有 `fixedAccount`，不会自动创建密码。
- v2 文件正常：直接读取。
- 主文件损坏：继续尝试 `identity-store.previous.json`；previous 为 v1 时也执行迁移。
- 未知 schemaVersion 或主副本均无效：启动明确失败，不创建空身份库。
- 首次持久化 v2 前，当前主文件仍按现有机制复制为 previous。

迁移和恢复测试必须验证原有长期身份、成员和设备令牌未丢失。

## 5. 固定账户管理

管理员可以：

- 输入 SteamID64、昵称和初始密码创建固定账户；
- 修改昵称；
- 重置密码；
- 启用或禁用账户；
- 查看完整 SteamID64、密码更新时间、本场在线状态和本场确认状态。

SteamID64 去除首尾空格后必须严格匹配 `^7656119\d{10}$`。系统不自动删除中间字符。

如果 SteamID 已属于现有长期 Identity，管理员操作会更新该 Identity 的昵称并设置密码，不创建重复 Identity。Identity 库允许昵称重复，但同一 Session 内活动成员昵称必须唯一。

修改昵称会更新长期 Identity。该成员已在当前大厅时，也同步更新当前 Membership 和网页玩家名称；发生本场昵称冲突时拒绝修改。历史 Session 的 Membership 昵称不回写。

重置密码会原子替换整组盐、哈希、参数和更新时间，并清除该账户的登录失败计数。当前已登录连接保持有效。

禁用账户会立即阻止后续登录。如果该身份位于当前大厅，当前 Membership 会被标记 blocked，玩家会被移出网页大厅。重新启用只恢复账户资格，不解除当前 Session 的 blocked 状态；新 Session 才能重新进入。

管理状态和所有响应不得包含密码、盐、哈希或 scrypt 参数。

## 6. 登录接口与票据

固定成员登录接口：

```text
POST /api/fixed-member-auth/login
Body: { steamId, password }
成功: { success, socketTicket, socketTicketExpiresAt }
```

成功后，前端发送：

```text
FIXED_MEMBER_SOCKET_LOGIN { ticket }
```

票据有效期 30 秒、只能消费一次，并绑定当前 Session Membership。它不是设备令牌，不持久化，也不能用于设备自动登录。

失败响应区分：

- `account_not_found`
- `password_incorrect`
- `account_disabled`
- `blocked_for_session`
- `nickname_in_use`
- `rate_limited`

同一来源地址或同一 SteamID64 在 10 分钟内连续失败 10 次后暂停 15 分钟。成功登录后清除对应失败记录。限流状态只保存在内存中，服务重启后重置。

密码验证成功后：

- 当前 Session 有 active Membership：复用；
- 当前 Session 有 blocked Membership：拒绝；
- 当前 Session 没有 Membership：创建新的长期 Membership；
- 同场昵称冲突：拒绝进入并提示联系管理员修改昵称。

管理员密码写操作不把固定成员密码放入 Socket。管理员 Socket 先签发 30 秒单次管理票据，票据绑定具体操作和目标，再由前端调用专用 HTTP 写接口。管理票据只发送给已认证管理员连接，消费后立即失效。

## 7. 临时玩家与兼容流程

临时玩家登录事件调整为：

```text
LOBBY_INVITE_LOGIN {
    inviteCode,
    nickname,
    claimedSteamId
}
```

SteamID64 为必填项并执行严格格式校验。它是不可信声明：

- 可让插件判断该 SteamID 已进入当前网页大厅，从而停止提醒；
- 可继续生成现有 CS2 确认挑战；
- 只有服务器可信 SteamID 和确认码均匹配时，才能提升为长期身份；
- 不匹配时保持原有安全行为，不改绑、不合并。

以下流程必须保持：

- 邀请码和昵称进入临时大厅；
- Steam 确认码；
- `!cclogin`、`!cccode`、`!ccbind`；
- 旧游戏码恢复；
- 管理员密码登录；
- HTTP 环境下设备令牌签发、轮换和自动登录继续禁用。

## 8. CS2 自动确认与提醒

自动确认只处理当前 Session 中未 blocked 的长期 Membership。插件快照中的可信 SteamID 与 Identity 已绑定 SteamID 完全一致时，更新本场 `confirmationState`、`trustedSteamId` 和 `confirmedAt`。它不修改 `Identity.steamId`。

如果固定成员绑定的 SteamID 没有出现在可信快照中，网页显示“尚未检测到该固定账户的 SteamID”，保持 `pending`。系统不会根据其他在线玩家、昵称或人数推测 mismatch。

插件认证状态接口增加：

```text
GET /api/plugin/state
{
    sessionId,
    generatedAt,
    lobbySteamIds: string[]
}
```

`lobbySteamIds` 只包含当前 Session 中未离开、未 blocked 的 Membership 声明 SteamID。该字段仅通过插件 Token 保护的接口返回，不进入公开状态或 Socket 广播。

插件每 5 秒刷新一次网页状态，成功后记录本地时间。状态超过 15 秒视为过期。每秒提醒判定要求：

- 已经至少成功同步一次；
- 缓存未过期；
- 玩家是真实有效玩家，不是 Bot 或 HLTV；
- 玩家 SteamID64 格式有效；
- SteamID 不在 `lobbySteamIds` 中。

满足条件时在聊天区域显示：

```text
[草人杯] 请先打开草人杯客户端，使用 SteamID64 + 密码或邀请码进入大厅。
```

玩家进入网页大厅后，下一次成功状态刷新即停止提示。同步失败时可暂时保留缓存，但缓存过期后停止所有提醒。插件卸载、换图和玩家断开时清理提醒状态。提醒逻辑不执行踢人、观察者隔离、jointeam 限制或服务器密码操作。

## 9. 前端设计

登录页增加主入口“固定成员”：

- SteamID64 输入框；
- 密码输入框；
- 登录按钮；
- 固定错误提示区域。

“临时参赛者”保留邀请码和昵称，并增加 SteamID64 输入框。旧游戏码与管理员登录继续作为次级入口。设备自动登录区域继续显示 HTTP 不可用状态，不因固定密码登录而开放。

管理员现有身份面板增加固定账户表单和账户列表。密码保存后立即清空输入框，列表只显示密码更新时间。重置密码需要明确确认，不提供查看原密码功能。

## 10. 测试

TypeScript 测试覆盖：

- 管理员预设固定账户；
- SteamID 唯一性和格式；
- scrypt 哈希持久化且无明文；
- 正确和错误密码；
- 禁用账户；
- 重置后旧密码失效、新密码生效；
- Membership 创建和复用；
- blocked 无法绕过；
- 服务重启后账户与哈希仍存在；
- 已有长期 Identity 设置密码不重复建档并更新昵称；
- 同 SteamID 自动确认；
- 不同 SteamID 不改绑；
- v1 到 v2 主文件和 previous 迁移；
- 未知或损坏 schema 不静默清空；
- 邀请码临时入场；
- `!cclogin` 和旧游戏码回退；
- 固定登录不签发或返回设备令牌；
- 管理员操作票据的权限、目标绑定和单次消费。

C# 纯逻辑测试覆盖：

- 真实玩家与 Bot/HLTV；
- 有效和无效 SteamID；
- 已入场和未入场；
- 从未成功同步；
- 缓存有效和缓存过期。

验证命令：

```powershell
cd D:\OpenSourcework\caoren-cup-open-source\web-command-center
npm run typecheck
npm run test:lobby-identity

cd D:\OpenSourcework\caoren-cup-open-source\web-command-center\CaorenCupPlugin.Tests
dotnet test

cd D:\OpenSourcework\caoren-cup-open-source\web-command-center\CaorenCupPlugin
dotnet build
```

`desktop-client/` 预计不修改。若实际发生修改，再运行 `npm run check` 和 `npm test`。

## 11. 生产风险

- HTTP 会暴露固定成员密码传输内容。部署 HTTPS 前应使用独立、不可复用到其他服务的密码。
- schema v2 部署前必须单独备份生产身份库及 previous 文件；网页端和桥接插件应在同一维护窗口更新。
- 旧桥接插件不会消费新的 `lobbySteamIds`，因此只更新网页端时不会产生提醒，但固定登录本身仍可工作。
- 只更新桥接插件而未更新网页端时，新状态字段不存在；插件必须按“未成功同步”处理并禁止提醒，不能向所有人刷屏。
- 密码限流位于单进程内存中；当前单实例部署有效，但未来多实例部署需要共享限流存储。

## 12. 部署边界

本次实施只修改本地仓库、运行本地测试，不连接服务器、不部署、不提交、不推送。

未来经用户单独授权后，部署应遵循：本地测试、推送 GitHub、本地生成三个规定名称的 zip、通过 WinSCP/SFTP 上传到服务器 `/tmp`、备份生产身份库和目标目录、分别覆盖网页端与桥接插件、重启对应服务并验证。不得默认在服务器执行 `git pull`。
