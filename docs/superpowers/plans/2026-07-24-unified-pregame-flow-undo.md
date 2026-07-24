# Unified Pregame Flow Undo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the existing web pregame undo stack so manual operations and every manual or automatic pregame phase transition can be safely undone in strict LIFO order.

**Architecture:** Extend the existing `flow-undo-manager.ts` as the only history owner. Phase transitions and same-phase administrator actions push typed checkpoints into the same in-memory stack; socket and UI consumers use one concurrency-checked status and request contract. Session persistence keeps the current game state but deliberately excludes undo history.

**Tech Stack:** TypeScript, Node.js, Socket.IO, browser JavaScript, Node test runner through `tsx`.

## Global Constraints

- Modify only `web-command-center/`, tests, and documentation; do not modify or build CS2 plugins or the desktop client.
- Keep all new and modified text files UTF-8.
- Snapshot only pregame flow fields; never restore credentials, memberships, online/connection state, administrator authority, or the player collection.
- Preserve later spectators; reject and clear invalid history when a required participant is missing or has become a spectator.
- Keep at most 50 in-memory entries and never restore undo history after a service restart.
- Clear history on `LiveGame`, lobby reset, termination, and automatic duel return to lobby.
- Do not add redo.

---

### Task 1: Rebase the feature onto the current main implementation

**Files:**
- Preserve: `docs/superpowers/specs/2026-07-24-unified-pregame-flow-undo-design.md`
- Preserve: `docs/superpowers/plans/2026-07-24-unified-pregame-flow-undo.md`
- Drop during rebase: the obsolete parallel `phase-rollback.ts` implementation from commit `891f422`

**Interfaces:**
- Consumes: `origin/main` and its existing `FlowUndoManager`.
- Produces: a clean `feat/phase-rollback` based on `origin/main` with documentation only before TDD begins.

- [ ] **Step 1: Verify the linked worktree and ignored backups**

Run: `git rev-parse --git-dir; git rev-parse --git-common-dir; git status --short --ignored`

Expected: linked worktree paths differ, branch is `feat/phase-rollback`, backup directories are ignored.

- [ ] **Step 2: Rebase documentation onto current main and skip the obsolete implementation commit**

Run: `git rebase --onto origin/main b30fe6c`, then use `git rebase --skip` only for commit `891f422`; continue the documentation commits normally.

Expected: `git log origin/main..HEAD` contains the approved design and this plan, but no `phase-rollback.ts` implementation.

- [ ] **Step 3: Run the existing undo baseline**

Run: `npm run typecheck; npm run test:flow-undo; npm run test:flow-undo-ui`

Expected: PASS before behavior changes.

### Task 2: Make the single history manager concurrency-safe and non-persistent

**Files:**
- Modify: `web-command-center/src/types.ts`
- Modify: `web-command-center/src/flow-undo-manager.ts`
- Modify: `web-command-center/src/flow-undo-manager.test.ts`
- Modify: `web-command-center/src/session-persistence.ts`
- Modify: `web-command-center/src/session-persistence.test.ts`

**Interfaces:**
- Consumes: existing `pushFlowUndoCheckpoint`, `discardFlowUndoCheckpoint`, `getFlowUndoStatus`.
- Produces: `FlowUndoRequest { expectedPhase, expectedHistoryDepth, expectedEntryId }`, status fields `historyDepth` and `targetPhase`, and `undoLatestFlowAction(session, actor, request)`.

- [ ] **Step 1: Add failing manager tests**

Add tests asserting stale `expectedPhase`, `expectedHistoryDepth`, or `expectedEntryId` cannot pop a second entry; a valid request pops exactly one entry; missing required participants clear the stack without modifying the session; later spectators and identity fields remain unchanged.

- [ ] **Step 2: Verify manager tests fail for the missing request contract**

Run: `npx tsx --test src/flow-undo-manager.test.ts`

Expected: FAIL because the existing undo API has no concurrency request and restores invalid snapshots.

- [ ] **Step 3: Implement the minimal manager contract**

Use the existing whitelist snapshot. Add participant dependencies captured from active roster/team/captain flow fields, validate them before restoration, and compare all three expected request fields before popping the stack. Return a private Chinese failure reason without mutating the session on validation failure.

- [ ] **Step 4: Verify manager tests pass**

Run: `npx tsx --test src/flow-undo-manager.test.ts`

Expected: PASS.

- [ ] **Step 5: Add a failing persistence test for restart behavior**

Assert `buildSessionSnapshotPayload()` contains no `flowUndo`, and restoring a saved session clears the process-local history.

- [ ] **Step 6: Verify the persistence test fails**

Run: `npx tsx --test src/session-persistence.test.ts`

Expected: FAIL because snapshot version 2 currently exports and imports `flowUndo`.

- [ ] **Step 7: Remove undo history from persistence and pass the test**

Keep session snapshot compatibility for versions 1 and 2, ignore any legacy `flowUndo` property, and always call `clearFlowUndoHistory()` after restoring.

Run: `npx tsx --test src/session-persistence.test.ts`

Expected: PASS.

### Task 3: Record every pregame phase transition in the unified stack

**Files:**
- Modify: `web-command-center/src/game-flow-manager.ts`
- Modify: `web-command-center/src/flow-runtime.test.ts`
- Modify: `web-command-center/src/flow-undo-integration.test.ts`

**Interfaces:**
- Consumes: `pushFlowUndoCheckpoint` and `discardFlowUndoCheckpoint`.
- Produces: phase-transition entries with action type `ADVANCE_PHASE`, actor metadata for administrator advancement, and system metadata for vote/timer advancement.

- [ ] **Step 1: Add failing transition tests**

Cover delayed roll completion, draft completion, map vote completion, side vote completion, administrator advancement, and duel `Lobby -> PreGameSetup`. Assert a checkpoint appears only when the phase actually changes.

- [ ] **Step 2: Verify transition tests fail**

Run: `npx tsx --test src/flow-runtime.test.ts src/flow-undo-integration.test.ts`

Expected: FAIL because automatic phase changes currently do not create checkpoints.

- [ ] **Step 3: Add checkpoint preparation and commit around `advancePhase`**

Capture before duel setup or other transition side effects. Commit after an immediate transition succeeds, or inside the delayed roll callback immediately before changing to `PlayerDraft`; discard/avoid entries when validation fails. Clear the unified history on entering `LiveGame`.

- [ ] **Step 4: Verify transition tests pass**

Run: `npx tsx --test src/flow-runtime.test.ts src/flow-undo-integration.test.ts`

Expected: PASS.

### Task 4: Restore timers safely after undo

**Files:**
- Modify: `web-command-center/src/game-flow-manager.ts`
- Modify: `web-command-center/src/flow-undo-integration.test.ts`

**Interfaces:**
- Consumes: restored session state from `undoLatestFlowAction`.
- Produces: `resumePregameFlowAfterUndo()` that starts a full draft/map-vote/side-vote countdown only for incomplete work.

- [ ] **Step 1: Add failing timer restoration tests**

Assert incomplete draft, map vote, and side vote retain existing actions and receive fresh deadlines; completed restored phases have no timer and do not auto-advance.

- [ ] **Step 2: Verify timer tests fail**

Run: `npx tsx --test src/flow-undo-integration.test.ts`

Expected: FAIL because current undo restoration neither consistently resets timers nor distinguishes complete phases.

- [ ] **Step 3: Implement timer restoration**

Clear all flow timers before restoring, then inspect draft progress, active vote completion, and side-vote completion. Call existing timer helpers only for incomplete work and leave completed phases paused.

- [ ] **Step 4: Verify timer tests pass**

Run: `npx tsx --test src/flow-undo-integration.test.ts`

Expected: PASS.

### Task 5: Unify socket authorization, status, and UI

**Files:**
- Modify: `web-command-center/src/socket-handlers.ts`
- Modify: `web-command-center/src/player-utils.ts`
- Modify: `web-command-center/src/flow-undo-visibility.test.ts`
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/public/js/lobby-app.js`
- Modify: `web-command-center/scripts/check-flow-undo-ui.cjs`

**Interfaces:**
- Consumes: concurrency-safe `FlowUndoStatus` and `FlowUndoRequest`.
- Produces: administrator-only state, `UNDO_FLOW_ACTION` request payload, one dynamic undo button, private failures, and public success notifications.

- [ ] **Step 1: Add failing socket/visibility tests**

Assert ordinary players receive no undo metadata, official administrators receive the full safe status, duel temporary administrators can undo only their own duel pregame transition, and duplicate socket requests cannot pop two entries.

- [ ] **Step 2: Verify socket tests fail**

Run: `npx tsx --test src/flow-undo-integration.test.ts src/flow-undo-visibility.test.ts`

Expected: FAIL for missing request validation and automatic-transition ownership.

- [ ] **Step 3: Implement socket behavior**

Validate permission first, pass all expected fields to the manager, clear old timers, restore timers after success, broadcast a short success notice, and emit detailed failure only to the requesting socket.

- [ ] **Step 4: Update the static UI checker first**

Require `expectedPhase`, `expectedHistoryDepth`, `expectedEntryId`, phase-aware button copy, confirmation text, and the duel temporary administrator entry.

- [ ] **Step 5: Verify the static UI checker fails**

Run: `npm run test:flow-undo-ui`

Expected: FAIL because the existing browser request lacks concurrency fields and dynamic phase rollback copy.

- [ ] **Step 6: Implement the unified button and request**

For `ADVANCE_PHASE`, render `回退到：<阶段名>`; otherwise render `撤销：<安全摘要>`. Disable while pending and send all concurrency fields. Reset pending state on the next game-state update.

- [ ] **Step 7: Verify UI and socket tests pass**

Run: `npm run test:flow-undo-ui; npx tsx --test src/flow-undo-integration.test.ts src/flow-undo-visibility.test.ts; node --check public/js/lobby-app.js`

Expected: PASS.

### Task 6: Full verification and branch handoff

**Files:**
- Verify only: all changed files and ignored backup/runtime paths.

**Interfaces:**
- Consumes: completed unified undo implementation.
- Produces: a reviewed commit and PR-ready branch.

- [ ] **Step 1: Run the complete requested suite**

Run: `npm run typecheck; npm run test:flow-undo; npm run test:flow-undo-ui; npm run test:fixed-member-ui; npm run test:lobby-identity; npm run test:match-command-policy; npm run test:caoren-modules; npm run test:postmatch-fun-stats; npm run test:update-announcements; npm run test:update-announcement-ui; npm run test:rules-content; npm run test:rules-guide; npm run test:rules-pdf; node --check public/js/lobby-app.js`

Expected: every command exits 0.

- [ ] **Step 2: Run local HTTP and duel-flow smoke checks**

Start the web server locally, verify `/` returns HTTP 200, and exercise `Lobby -> PreGameSetup -> Lobby` using the existing integration harness or browser if a signed-in session is required.

- [ ] **Step 3: Inspect scope and ignored files**

Run: `git diff --check; git status --short --ignored; git diff --cached --stat`

Expected: backups, `node_modules`, runtime files, logs, and private plugins are not staged; only web/documentation changes are included.

- [ ] **Step 4: Commit, push, and create a PR**

Commit with `feat: unify pregame flow undo`, push `feat/phase-rollback`, and create a PR against `main` with Chinese public-facing notes and verification evidence.
