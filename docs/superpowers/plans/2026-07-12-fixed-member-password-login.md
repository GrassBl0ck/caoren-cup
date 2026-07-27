# Fixed Member SteamID64 Password Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Subagents are not authorized for this workspace. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add administrator-managed fixed member accounts that log in with SteamID64 and a scrypt password, create a current-session membership, receive trusted CS2 presence confirmation, and coexist with the existing temporary and legacy login paths.

**Architecture:** Extend identity-store schema v1 to v2 with an optional fixed-account credential on long-term identities. Verify passwords through a dedicated HTTP route that returns only a 30-second one-time Socket ticket; perform administrator password writes through one-time administrator action tickets. Expose a plugin-authenticated current-lobby SteamID set and let the bridge plugin run a fail-closed, cache-aware reminder predicate once per second.

**Tech Stack:** Node.js 20, TypeScript, Express, Socket.IO, Node `crypto.scrypt`, Node test runner, .NET 8, CounterStrikeSharp, xUnit.

## Global Constraints

- Work only in `D:\OpenSourcework\caoren-cup-open-source`; do not connect to or modify the server.
- Do not run `git pull`, switch branches, overwrite user changes, commit, push, tag, package, or deploy.
- Treat `.github/workflows/ci.yml`, `desktop-client/package.json`, `desktop-client/package-lock.json`, `bot-improver-controller/`, `c4-effect-test-plugin*/`, `scripts/package-caoren-bot-controller.ps1`, and all unrelated untracked files as protected user work.
- Before modifying an existing file, copy it into an ignored `backup_before_fixed_member_password_YYYYMMDD-HHMMSS/` directory and verify the directory is ignored.
- Keep all edited text UTF-8.
- Do not add a native Node dependency. Passwords use asynchronous `crypto.scrypt`, random salt, and `timingSafeEqual`.
- Fixed passwords may traverse HTTP only for administrator create/reset and member login. Device enrollment, device tokens, rotation, and automatic login remain HTTPS-only.
- Never put plaintext passwords, password hashes, salts, scrypt parameters, bearer tickets, or request bodies in logs, Socket broadcasts, public state, errors, examples, or backups intended for publication.
- Temporary invitation login, Steam confirmation code, `!cclogin`, `!cccode`, `!ccbind`, old game-code recovery, and administrator password login must remain available.
- The approved design is `docs/superpowers/specs/2026-07-12-fixed-member-password-login-design.md`.

---

### Task 1: Backup, Password Primitive, and Schema v2 Migration

**Files:**
- Create: `web-command-center/src/identity/password-auth.ts`
- Create: `web-command-center/src/identity/password-auth.test.ts`
- Modify: `web-command-center/src/identity/identity-types.ts`
- Modify: `web-command-center/src/identity/identity-store.ts`
- Modify: `web-command-center/src/identity/identity-store.test.ts`

**Interfaces:**
- Produces: `PasswordCredential`, `validateFixedMemberPassword(password)`, `hashFixedMemberPassword(password, options?)`, `verifyFixedMemberPassword(password, credential)`, and `FixedMemberLoginGuard`.
- Produces: `IdentityStoreDataV1`, `IdentityStoreData` with `schemaVersion: 2`, and a loader that accepts v1/v2 but rejects unknown versions.

- [ ] **Step 1: Create one ignored backup directory and copy every existing file listed in this plan before its first edit**

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "backup_before_fixed_member_password_$stamp"
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item web-command-center/src/identity/identity-types.ts $backup/
Copy-Item web-command-center/src/identity/identity-store.ts $backup/
Copy-Item web-command-center/src/identity/identity-store.test.ts $backup/
# Copy the remaining existing files immediately before later tasks edit them.
git check-ignore $backup
```

Expected: `git check-ignore` prints the backup directory path.

- [ ] **Step 2: Write failing password tests**

```ts
test('scrypt credential verifies the right password and stores no plaintext', async () => {
    const credential = await hashFixedMemberPassword('固定成员Pass123');
    assert.equal(await verifyFixedMemberPassword('固定成员Pass123', credential), true);
    assert.equal(await verifyFixedMemberPassword('wrong-password', credential), false);
    assert.equal(JSON.stringify(credential).includes('固定成员Pass123'), false);
});

test('password validation accepts 8-128 characters and rejects outside bounds', () => {
    assert.equal(validateFixedMemberPassword('12345678'), '12345678');
    assert.throws(() => validateFixedMemberPassword('1234567'), /password_invalid/);
    assert.throws(() => validateFixedMemberPassword('x'.repeat(129)), /password_invalid/);
});

test('login guard blocks the tenth failure for source or SteamID for fifteen minutes', () => {
    const guard = new FixedMemberLoginGuard({ now: () => now });
    for (let attempt = 1; attempt < 10; attempt++) assert.equal(guard.recordFailure(['ip:1', 'steam:76561198000000001']).blocked, false);
    assert.equal(guard.recordFailure(['ip:1', 'steam:76561198000000001']).blocked, true);
});
```

- [ ] **Step 3: Run password tests and confirm the missing module failure**

Run: `cd web-command-center; npx tsx --test src/identity/password-auth.test.ts`

Expected: FAIL because `password-auth.ts` does not exist.

- [ ] **Step 4: Implement the password primitive and in-memory guard**

```ts
export interface PasswordCredential {
    algorithm: 'scrypt';
    salt: string;
    hash: string;
    params: { N: number; r: number; p: number; keyLength: number; maxmem: number };
    updatedAt: number;
}

export const DEFAULT_SCRYPT_PARAMS = {
    N: 16_384,
    r: 8,
    p: 1,
    keyLength: 64,
    maxmem: 64 * 1024 * 1024,
} as const;
```

Use `promisify(crypto.scrypt)`, a 32-byte random salt, base64url storage, and `timingSafeEqual` only after equal Buffer length is confirmed. The guard maintains separate 10-minute counters for every supplied key, blocks for 15 minutes on failure 10, and clears all supplied keys on success.

- [ ] **Step 5: Write failing migration tests**

```ts
test('schema v1 migrates to v2 without losing identities memberships or device tokens', async () => {
    fs.writeFileSync(file, JSON.stringify(v1Fixture), 'utf8');
    const store = new IdentityStore(file);
    await store.load();
    const data = store.snapshot();
    assert.equal(data.schemaVersion, 2);
    assert.deepEqual(data.identities, v1Fixture.identities);
    assert.deepEqual(data.memberships, v1Fixture.memberships);
    assert.deepEqual(data.deviceTokens, v1Fixture.deviceTokens);
});

test('unknown schema does not produce an empty store', async () => {
    fs.writeFileSync(file, JSON.stringify({ schemaVersion: 99 }), 'utf8');
    await assert.rejects(new IdentityStore(file).load(), /identity store schema is unsupported/);
});
```

Add a second fixture where the primary is corrupt and previous is a valid v1 file.

- [ ] **Step 6: Run migration tests and confirm failure on schema v1/v2 expectations**

Run: `cd web-command-center; npx tsx --test src/identity/identity-store.test.ts`

Expected: FAIL because the store still only accepts and emits schema v1.

- [ ] **Step 7: Implement strict v1-to-v2 migration**

Add optional `fixedAccount: { enabled: boolean; password: PasswordCredential }` to `IdentityRecord`. Parse JSON as unknown, validate the required top-level collections, migrate v1 by cloning all three collections and setting `schemaVersion: 2`, accept v2, and throw for every other version. Preserve the current primary/previous fallback and atomic write behavior.

- [ ] **Step 8: Run focused tests**

Run: `cd web-command-center; npx tsx --test src/identity/password-auth.test.ts src/identity/identity-store.test.ts`

Expected: all tests PASS.

### Task 2: Fixed Account Service and Session Membership Semantics

**Files:**
- Modify: `web-command-center/src/identity/identity-service.ts`
- Modify: `web-command-center/src/identity/identity-service.test.ts`
- Modify: `web-command-center/src/identity/session-integration.ts`

**Interfaces:**
- Consumes: `PasswordCredential`, password hash/verify functions, schema v2 store.
- Produces: `createOrUpdateFixedAccount`, `renameFixedAccount`, `resetFixedAccountPassword`, `setFixedAccountEnabled`, `authenticateFixedAccount`, `listFixedAccounts`, and `listLobbySteamIds`.
- Produces: `removeIdentityFromSession(session, identityId)` for immediate disable handling.

- [ ] **Step 1: Back up the three existing files**

Copy them into the Task 1 backup directory before editing.

- [ ] **Step 2: Add failing service tests for provisioning and persistence**

```ts
const created = await service.createOrUpdateFixedAccount({
    steamId: '76561198000000040', nickname: '固定成员甲', password: 'initial-pass',
});
assert.equal(created.identity.fixedAccount?.enabled, true);
assert.equal(JSON.stringify(store.snapshot()).includes('initial-pass'), false);

const existing = await service.confirmTrustedIdentity(temp.membershipId, '76561198000000041', 'Old Name');
const updated = await service.createOrUpdateFixedAccount({
    steamId: '76561198000000041', nickname: '管理员昵称', password: 'another-pass',
});
assert.equal(updated.identity.identityId, existing.identity?.identityId);
assert.equal(Object.values(store.snapshot().identities).length, identityCountBefore);
```

Also assert strict SteamID format, duplicate Steam identity prevention, and reload persistence.

- [ ] **Step 3: Add failing authentication, reset, disable, and membership tests**

Cover correct/wrong password, active Membership reuse, a new Session producing a new pending Membership, blocked rejection, disabled rejection, old password failing after reset, new password succeeding, nickname collision, and account re-enable not clearing the blocked Membership.

- [ ] **Step 4: Run focused service tests and verify the missing methods fail**

Run: `cd web-command-center; npx tsx --test src/identity/identity-service.test.ts`

Expected: FAIL with missing fixed-account service methods.

- [ ] **Step 5: Implement service methods with strict return unions**

```ts
type FixedAccountLoginResult =
    | { ok: true; identity: IdentityRecord; membership: LobbyMembershipRecord }
    | { ok: false; reason: 'account_not_found' | 'password_incorrect' | 'account_disabled' | 'blocked_for_session' | 'nickname_in_use' };
```

All identity and membership mutations occur through `IdentityStore.mutate`. Password hashing happens before the mutation, and the mutation rechecks SteamID and nickname collisions. Fixed login never calls `issueDeviceToken` and never creates an enrollment ticket.

- [ ] **Step 6: Implement session synchronization helpers**

`removeIdentityFromSession` removes the matching non-admin player from `players`, `playerOrder`, both roster arrays, captain slots, and accusations, then returns the removed player for a private kick message. Nickname changes update the current Membership and matching session Player only after collision checks.

- [ ] **Step 7: Run service and legacy identity tests**

Run: `cd web-command-center; npx tsx --test src/identity/*.test.ts`

Expected: all tests PASS.

### Task 3: One-Time HTTP Login and Administrator Action Tickets

**Files:**
- Modify: `web-command-center/src/identity/auth-core.ts`
- Modify: `web-command-center/src/identity/identity-runtime.ts`
- Modify: `web-command-center/src/identity/auth-routes.ts`
- Create: `web-command-center/src/identity/fixed-member-auth.test.ts`
- Modify: `web-command-center/src/server.ts`

**Interfaces:**
- Produces: `POST /api/fixed-member-auth/login`.
- Produces administrator routes for create/update, rename, reset, and enabled state, protected by one-time `FixedAccountAdminTicket` bearer tokens.
- Produces `fixedMemberSocketTickets` and `fixedAccountAdminTickets` ephemeral stores.

- [ ] **Step 1: Back up all existing files in this task**

Copy them into the ignored backup directory.

- [ ] **Step 2: Write failing HTTP integration tests using an ephemeral Express server and Node fetch**

Test exact errors `account_not_found`, `password_incorrect`, `account_disabled`, `blocked_for_session`, `nickname_in_use`, and `rate_limited`. Assert a successful payload contains only `success`, `socketTicket`, and `socketTicketExpiresAt`, and recursively assert it contains no `password`, `hash`, `salt`, `deviceToken`, or `rotation` key.

- [ ] **Step 3: Write failing administrator ticket tests**

Create a ticket payload containing `sessionId`, `adminPlayerId`, `operation`, and the bound `identityId` or `steamId`. Assert wrong operation/target fails, first matching use succeeds, and replay fails.

- [ ] **Step 4: Run the HTTP tests and confirm route failures**

Run: `cd web-command-center; npx tsx --test src/identity/fixed-member-auth.test.ts`

Expected: FAIL because the routes and ticket stores do not exist.

- [ ] **Step 5: Implement fixed member login route**

Use strict trimmed SteamID validation, raw password validation, request IP plus SteamID limit keys, and `authenticateFixedAccount`. On success issue a 30-second one-time ticket `{membershipId, sessionId}`. Do not wrap this route in `requireSecureDeviceAuth`; keep every existing device route unchanged.

- [ ] **Step 6: Implement administrator HTTP routes**

Use one-time bearer tickets bound to these operation names:

```ts
type FixedAccountAdminOperation = 'create' | 'rename' | 'reset_password' | 'set_enabled';
```

Consume and validate the ticket before invoking the service. Never echo password input. Inject callbacks from `server.ts` for `broadcastState` and immediate player removal after disable; register these routes after Socket.IO and broadcast helpers exist.

- [ ] **Step 7: Run HTTP and type tests**

Run: `cd web-command-center; npx tsx --test src/identity/fixed-member-auth.test.ts; npm run typecheck`

Expected: all tests PASS and TypeScript emits no errors.

### Task 4: Socket Login, Admin Status, Temporary Claim, and Plugin State

**Files:**
- Modify: `web-command-center/src/types.ts`
- Modify: `web-command-center/src/socket-handlers.ts`
- Modify: `web-command-center/src/plugin-api.ts`
- Create: `web-command-center/src/identity/fixed-member-socket.test.ts`
- Modify: `web-command-center/src/v1333-game-login.test.ts`

**Interfaces:**
- Consumes: fixed-member Socket and administrator ticket stores.
- Produces: `WsEvents.FIXED_MEMBER_SOCKET_LOGIN` and the admin ticket response through `IDENTITY_ADMIN_ACTION`.
- Produces plugin state fields `generatedAt` and `lobbySteamIds`.

- [ ] **Step 1: Back up existing files in this task**

Copy them into the ignored backup directory.

- [ ] **Step 2: Write failing Socket tests**

Test that a valid fixed ticket establishes identity once, a replay fails, a blocked Membership fails, and no `DEVICE_ENROLLMENT_READY` event is emitted. Test `ISSUE_FIXED_ACCOUNT_TICKET` rejects non-admin sockets and sends the ticket only to the requesting admin socket.

- [ ] **Step 3: Write failing temporary invitation regression test**

Emit `LOBBY_INVITE_LOGIN` with invitation, nickname, and `76561198000000050`; assert a temporary pending Membership with that claimed SteamID is attached. Assert invalid SteamID is rejected. Keep the existing `v1333-game-login.test.ts` assertions for one-time `!cclogin` recovery and no rebind.

- [ ] **Step 4: Run Socket tests and confirm failures**

Run: `cd web-command-center; npx tsx --test src/identity/fixed-member-socket.test.ts src/v1333-game-login.test.ts`

Expected: new tests FAIL; legacy tests still PASS.

- [ ] **Step 5: Implement fixed ticket consumption and admin ticket issuance**

Use the existing `establishSocketIdentity` path. Validate ticket Session, Membership active state, and fixed account enabled state before attaching. Admin status returns full SteamID, display name, enabled state, password `updatedAt`, current online state, and current confirmation state, but never the stored credential.

- [ ] **Step 6: Change invitation login to strict `claimedSteamId` input**

Pass `{steamId: claimedSteamId}` to `createTemporaryMembership`. Do not use the HTTPS-gated desktop Steam claim route for this input and do not promote it without the existing plugin challenge.

- [ ] **Step 7: Add plugin state fields**

Return `generatedAt: Date.now()` and `lobbySteamIds: lobbyIdentityService.listLobbySteamIds(session.sessionId)`. The service list excludes left and blocked Memberships and is only exposed on `/api/plugin/state`, which already requires `PLUGIN_TOKEN`.

- [ ] **Step 8: Run the lobby identity suite**

Run: `cd web-command-center; npm run test:lobby-identity; npm run typecheck`

Expected: all tests PASS.

### Task 5: Login and Administrator Web UI

**Files:**
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/public/js/game-code-login.js`
- Modify: `web-command-center/public/css/app.css`

**Interfaces:**
- Consumes: fixed login HTTP route, fixed Socket event, admin ticket flow, and sanitized admin status.
- Produces: fixed-member form, temporary SteamID input, fixed account create/reset/rename/enable controls.

- [ ] **Step 1: Back up the three existing frontend files**

Copy them into the ignored backup directory.

- [ ] **Step 2: Add the fixed-member form and temporary SteamID field**

Use stable grid layouts with labels for SteamID64 and password. Use `autocomplete="current-password"` for member login and `autocomplete="new-password"` for administrator create/reset. Keep legacy game-code/admin login in `<details>`.

- [ ] **Step 3: Implement fixed login behavior**

POST `{steamId, password}` with `cache: 'no-store'`; clear the password input in `finally`; map the six approved error codes to distinct Chinese messages; emit `FIXED_MEMBER_SOCKET_LOGIN` only after success. Do not persist the password in localStorage, sessionStorage, globals, desktop IPC, or DOM attributes.

- [ ] **Step 4: Implement administrator one-time ticket flow**

For each write action, request a ticket through the authenticated admin Socket, then POST/PATCH the password or account change through HTTP. Clear create/reset password fields immediately after completion. Render full SteamID, enabled state, password update time, current online state, and confirmation state. Never render credential internals.

- [ ] **Step 5: Add responsive styles**

Keep card radius at 6px, avoid nested cards, use a compact account table with horizontally scrollable overflow on small screens, and ensure the longest error labels wrap. Preserve the existing color system and dark theme overrides.

- [ ] **Step 6: Run static verification**

Run: `cd web-command-center; npm run typecheck; rg -n "localStorage|sessionStorage|deviceToken|password" public/js/game-code-login.js`

Expected: typecheck PASS; every password match is limited to input handling and request construction, with no persistence or logging.

### Task 6: Bridge Plugin Cache and Reminder Predicate

**Files:**
- Create: `web-command-center/CaorenCupPlugin/LobbyReminderPolicy.cs`
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs`
- Create: `web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj`
- Create: `web-command-center/CaorenCupPlugin.Tests/LobbyReminderPolicyTests.cs`

**Interfaces:**
- Produces: `LobbyReminderPolicy.ShouldRemind(bool isRealPlayer, string steamId, bool hasSuccessfulSync, DateTimeOffset lastSuccessfulSync, DateTimeOffset now, TimeSpan maxAge, IReadOnlySet<string> lobbySteamIds)`.
- Consumes: `/api/plugin/state` fields `sessionId`, `generatedAt`, and `lobbySteamIds`.

- [ ] **Step 1: Back up `CaorenCupPlugin.cs`**

Copy it into the ignored backup directory. New files have no original to back up.

- [ ] **Step 2: Create failing xUnit predicate tests**

```csharp
[Fact]
public void Fresh_state_reminds_real_unregistered_player() =>
    Assert.True(LobbyReminderPolicy.ShouldRemind(true, ValidSteamId, true, Now, Now, TimeSpan.FromSeconds(15), Empty));

[Theory]
[InlineData(false, "76561198000000060")]
[InlineData(true, "BOT")]
[InlineData(true, "0")]
public void Invalid_or_non_real_players_are_not_reminded(bool real, string steamId) =>
    Assert.False(LobbyReminderPolicy.ShouldRemind(real, steamId, true, Now, Now, TimeSpan.FromSeconds(15), Empty));
```

Also test registered SteamID, never-synced state, exactly expired state, and stale state.

- [ ] **Step 3: Run tests and verify missing policy failure**

Run: `cd web-command-center/CaorenCupPlugin.Tests; dotnet test`

Expected: FAIL because `LobbyReminderPolicy` does not exist.

- [ ] **Step 4: Implement the pure predicate**

Validate SteamID using exact `^7656119\d{10}$`. Return false unless every freshness and real-player condition is satisfied and the ID is absent from the set.

- [ ] **Step 5: Add fail-closed periodic state refresh**

Add a 5-second map-change-safe timer for `RefreshWebStateAsync`, a one-second map-change-safe reminder timer, a successful-sync timestamp, and a lobby SteamID HashSet. Only replace the set and timestamp after a complete successful response containing `lobbySteamIds`; missing fields count as failed sync. Use a synchronization guard to prevent overlapping refreshes.

- [ ] **Step 6: Add reminder delivery and cleanup**

For each `Utilities.GetPlayers()` entry passing `IsRealPlayer`, call the predicate and print exactly one chat message per one-second tick when true. Clear lobby cache, successful-sync state, and per-player reminder state on unload and map transition; disconnected players naturally disappear from enumeration. Do not modify `jointeam`, team assignment, challenge display, match events, or statistics.

- [ ] **Step 7: Run plugin tests and build**

Run: `cd web-command-center/CaorenCupPlugin.Tests; dotnet test`

Run: `cd ../CaorenCupPlugin; dotnet build`

Expected: tests PASS and plugin build succeeds with zero errors.

### Task 7: Documentation, Security Scan, and Full Regression

**Files:**
- Modify: `docs/deployment.md`
- Modify: `docs/plugin-web-bridge.md`
- Modify: `README.md`
- Modify: `web-command-center/package.json` only if the new tests are outside the existing `src/identity/*.test.ts` glob.

**Interfaces:**
- Documents schema v2 backup risk, HTTP password exception, fixed account flow, reminder freshness behavior, and unchanged device-token HTTPS restriction.

- [ ] **Step 1: Back up every existing documentation/package file before editing**

Copy it into the ignored backup directory.

- [ ] **Step 2: Update public documentation without private deployment paths**

Describe user-visible fixed login and temporary login behavior, schema v2 migration, generic identity-store backup requirement, and bridge plugin compatibility. Do not include server IPs, `/root/...`, `/opt/...`, `/tmp`, WinSCP steps, PM2 process details, production config paths, or passwords.

- [ ] **Step 3: Run complete required verification**

```powershell
cd D:\OpenSourcework\caoren-cup-open-source\web-command-center
npm run typecheck
npm run test:lobby-identity

cd D:\OpenSourcework\caoren-cup-open-source\web-command-center\CaorenCupPlugin.Tests
dotnet test

cd D:\OpenSourcework\caoren-cup-open-source\web-command-center\CaorenCupPlugin
dotnet build
```

Expected: every command exits 0.

- [ ] **Step 4: Scan for credential leakage and forbidden artifacts**

```powershell
rg -n --hidden -S "console\.(log|warn|error).*password|Logger\..*password|deviceToken.*fixed|fixed.*deviceToken" web-command-center/src web-command-center/public web-command-center/CaorenCupPlugin
git status --short --ignored
git diff --check
git diff --stat
```

Expected: no password logging or fixed-login device-token path; backups, `bin/`, and `obj/` are ignored; no whitespace errors.

- [ ] **Step 5: Review only task-owned diffs**

Confirm `.github/workflows/ci.yml`, both desktop package files, private Bot files, test plugins, and unrelated untracked files are byte-for-byte untouched. Do not stage or commit anything.

- [ ] **Step 6: Report completion**

Report modified files, schema v2 migration, endpoint and Socket behavior, all test outputs, remaining HTTP and deployment risks, and the future local-package/SFTP deployment sequence. Explicitly state that no server, commit, push, tag, Release, or deployment action occurred.
