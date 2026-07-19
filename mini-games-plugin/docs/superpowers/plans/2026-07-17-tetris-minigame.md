# CS2MiniGames Tetris Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a GPL-3.0 CounterStrikeSharp plugin named `CS2MiniGames` whose first game is a fully tested single-player 10×20 Tetris implementation with SRS rotation, hold, ghost piece, CenterHtml rendering, safe player lifecycle handling, and a SQLite leaderboard.

**Architecture:** Keep deterministic Tetris rules in API-independent classes under `Tetris/Core`, then adapt them to CounterStrikeSharp through a small session manager, input tracker, movement guard, renderer, repository, and plugin entry point. Register one global `OnTick`; every player session owns its own timers, bag, board, and input state. Tests reference the plugin project but exercise pure rule classes without live CS2 objects.

**Tech Stack:** C# 12, .NET 8, CounterStrikeSharp.API 1.0.367, Microsoft.Data.Sqlite 8.0.29, xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 2.8.2, Windows PowerShell 5.1+

## Global Constraints

- Work only in `D:\OpenSourcework\CS2MiniGames`; do not modify `caoren-cup-open-source`, `CS2Snake`, server files, or applications.
- Use branch `feat/tetris-mvp`; never implement directly on `main`.
- Before modifying an existing file, copy it into `backup_before_tetris_mvp_20260717/<task-name>/`; exclude `/backup_before_*/` through `.git/info/exclude`; never stage backups.
- Use UTF-8 for all source, project, JSON, Markdown, and PowerShell files.
- Runtime output must be `CS2MiniGames.dll`; namespace root is `CS2MiniGames`.
- License is GPL-3.0. Copy the canonical `LICENSE` text from the local GPL-3.0 CS2Snake repository; do not invent or abbreviate it.
- Pin `CounterStrikeSharp.API` to 1.0.367 to match the current Caoren Cup server plugins; pin `Microsoft.Data.Sqlite` to 8.0.29.
- Visible board is 10×20 with 2 hidden spawn rows; board size, hidden rows, 24px cell class, colors, and score table are not configurable.
- Commands are `css_tetris`, `css_toptetris`, `css_tetrishelp`, and `css_minigames`.
- All real players may start; Tab, player spawn, round start, disconnect, map end, and plugin unload close the session; closing is idempotent.
- Controls are A/D move, S soft drop, Space hard drop, E clockwise, R counterclockwise or restart after game over, W hold, Tab exit.
- Do not implement multiplayer attacks, garbage lines, T-Spin, Combo, Back-to-Back, Snake migration, Connect4, Breakout, or web administration.
- Do not deploy, upload, push, create a GitHub repository, or create a Release without separate user approval.

---

## File Structure

```text
CS2MiniGames.sln
LICENSE
.gitignore
src/CS2MiniGames/
  CS2MiniGames.csproj
  CS2MiniGamesPlugin.cs
  MiniGamesConfig.cs
  Framework/
    IMiniGameSession.cs
    MiniGameAction.cs
    MiniGameManager.cs
    PlayerInputTracker.cs
    PlayerMovementGuard.cs
  Persistence/
    LeaderboardEntry.cs
    LeaderboardRepository.cs
  Tetris/
    TetrisSession.cs
    TetrisRenderer.cs
    Core/
      ActivePiece.cs
      Cell.cs
      IPieceSource.cs
      RotationState.cs
      SevenBagRandomizer.cs
      TetrominoCatalog.cs
      TetrominoType.cs
      TetrisBoard.cs
      TetrisGameState.cs
      TetrisRotationSystem.cs
      TetrisScoring.cs
tests/CS2MiniGames.Tests/
  CS2MiniGames.Tests.csproj
  SevenBagRandomizerTests.cs
  TetrominoCatalogTests.cs
  TetrisBoardTests.cs
  TetrisRotationSystemTests.cs
  TetrisScoringTests.cs
  TetrisGameStateTests.cs
  TetrisRendererTests.cs
  TetrisSessionTests.cs
  PlayerInputTrackerTests.cs
  MiniGameManagerTests.cs
  LeaderboardRepositoryTests.cs
README.md
```

### Task 1: Bootstrap the licensed solution and test harness

**Files:**
- Create: `.gitignore`
- Create: `LICENSE`
- Create: `CS2MiniGames.sln`
- Create: `src/CS2MiniGames/CS2MiniGames.csproj`
- Create: `tests/CS2MiniGames.Tests/CS2MiniGames.Tests.csproj`
- Create: `src/CS2MiniGames/Tetris/Core/TetrominoType.cs`
- Create: `tests/CS2MiniGames.Tests/TetrominoCatalogTests.cs`
- Local-only: `.git/info/exclude`

**Interfaces:**
- Produces: `enum TetrominoType { I, O, T, S, Z, J, L }`; a buildable .NET 8 solution; an xUnit test project referencing the plugin project.

- [ ] **Step 1: Create the feature branch and local backup exclusion**

Run:

```powershell
git status --short --branch
git switch -c feat/tetris-mvp
Copy-Item -LiteralPath '.git\info\exclude' -Destination '.git\info\exclude.bak-20260717-tetris-mvp'
```

Use `apply_patch` to append this line to `.git/info/exclude`:

```text
/backup_before_*/
```

Expected: branch is `feat/tetris-mvp`; `.git/info/exclude` has a byte-for-byte backup inside `.git/info`; working tree contains only the already committed design and plan documents.

- [ ] **Step 2: Add repository metadata and project files**

Copy the unmodified GPL text:

```powershell
Copy-Item -LiteralPath 'D:\OpenSourcework\CS2Snake\LICENSE' -Destination '.\LICENSE'
```

Create `.gitignore` with:

```gitignore
bin/
obj/
.vs/
TestResults/
*.user
*.suo
*.log
*.bak
*.bak-*
backup_*/
backup-*/
backup_before_*/
release-build/
release-output/
*.zip
*.db
*.db-shm
*.db-wal
```

Run:

```powershell
dotnet new sln -n CS2MiniGames --format sln
dotnet new classlib -n CS2MiniGames -o '.\src\CS2MiniGames' --framework net8.0
dotnet new xunit -n CS2MiniGames.Tests -o '.\tests\CS2MiniGames.Tests' --framework net8.0
New-Item -ItemType Directory -Force '.\backup_before_tetris_mvp_20260717\task01' | Out-Null
Copy-Item '.\CS2MiniGames.sln' '.\backup_before_tetris_mvp_20260717\task01\CS2MiniGames.sln'
Copy-Item '.\src\CS2MiniGames\CS2MiniGames.csproj' '.\backup_before_tetris_mvp_20260717\task01\CS2MiniGames.csproj'
Copy-Item '.\src\CS2MiniGames\Class1.cs' '.\backup_before_tetris_mvp_20260717\task01\Class1.cs'
Copy-Item '.\tests\CS2MiniGames.Tests\CS2MiniGames.Tests.csproj' '.\backup_before_tetris_mvp_20260717\task01\CS2MiniGames.Tests.csproj'
Copy-Item '.\tests\CS2MiniGames.Tests\UnitTest1.cs' '.\backup_before_tetris_mvp_20260717\task01\UnitTest1.cs'
dotnet sln '.\CS2MiniGames.sln' add '.\src\CS2MiniGames\CS2MiniGames.csproj'
dotnet sln '.\CS2MiniGames.sln' add '.\tests\CS2MiniGames.Tests\CS2MiniGames.Tests.csproj'
dotnet add '.\tests\CS2MiniGames.Tests\CS2MiniGames.Tests.csproj' reference '.\src\CS2MiniGames\CS2MiniGames.csproj'
dotnet add '.\src\CS2MiniGames\CS2MiniGames.csproj' package CounterStrikeSharp.API --version 1.0.367
dotnet add '.\src\CS2MiniGames\CS2MiniGames.csproj' package Microsoft.Data.Sqlite --version 8.0.29
dotnet add '.\tests\CS2MiniGames.Tests\CS2MiniGames.Tests.csproj' package Microsoft.NET.Test.Sdk --version 17.14.1
dotnet add '.\tests\CS2MiniGames.Tests\CS2MiniGames.Tests.csproj' package xunit --version 2.9.3
dotnet add '.\tests\CS2MiniGames.Tests\CS2MiniGames.Tests.csproj' package xunit.runner.visualstudio --version 2.8.2
```

Delete generated `Class1.cs` and `UnitTest1.cs` with `apply_patch`. Set the plugin project properties to:

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <AssemblyName>CS2MiniGames</AssemblyName>
  <RootNamespace>CS2MiniGames</RootNamespace>
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>
```

- [ ] **Step 3: Write the first failing domain test**

Create `tests/CS2MiniGames.Tests/TetrominoCatalogTests.cs`:

```csharp
using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class TetrominoCatalogTests
{
    [Fact]
    public void DefinesExactlySevenTetrominoTypes()
    {
        Assert.Equal(7, Enum.GetValues<TetrominoType>().Length);
    }
}
```

Run:

```powershell
dotnet test '.\CS2MiniGames.sln' --no-restore
```

Expected: FAIL to compile because `CS2MiniGames.Tetris.Core.TetrominoType` does not exist.

- [ ] **Step 4: Add the minimal domain enum and verify GREEN**

Create `src/CS2MiniGames/Tetris/Core/TetrominoType.cs`:

```csharp
namespace CS2MiniGames.Tetris.Core;

public enum TetrominoType
{
    I,
    O,
    T,
    S,
    Z,
    J,
    L
}
```

Run:

```powershell
dotnet test '.\CS2MiniGames.sln'
```

Expected: 1 test passed, 0 failed.

- [ ] **Step 5: Commit the bootstrap**

```powershell
git status --short --ignored
git add .gitignore LICENSE CS2MiniGames.sln src tests
git diff --cached --stat
git diff --cached --check
git commit -m 'chore: bootstrap CS2MiniGames solution'
```

Expected: backup/build/test output remains ignored; commit contains project metadata, pinned packages, enum, and one passing test.

### Task 2: Define tetromino geometry and deterministic seven-bag generation

**Files:**
- Create: `src/CS2MiniGames/Tetris/Core/Cell.cs`
- Create: `src/CS2MiniGames/Tetris/Core/RotationState.cs`
- Create: `src/CS2MiniGames/Tetris/Core/ActivePiece.cs`
- Create: `src/CS2MiniGames/Tetris/Core/IPieceSource.cs`
- Create: `src/CS2MiniGames/Tetris/Core/TetrominoCatalog.cs`
- Create: `src/CS2MiniGames/Tetris/Core/SevenBagRandomizer.cs`
- Modify: `tests/CS2MiniGames.Tests/TetrominoCatalogTests.cs`
- Create: `tests/CS2MiniGames.Tests/SevenBagRandomizerTests.cs`

**Interfaces:**
- Produces: `Cell(int X,int Y)`; `RotationState`; `ActivePiece`; `TetrominoCatalog.GetCells`; resettable `IPieceSource`; `SevenBagRandomizer.Next()`.

- [ ] **Step 1: Back up the existing catalog test and write RED tests**

```powershell
New-Item -ItemType Directory -Force '.\backup_before_tetris_mvp_20260717\task02' | Out-Null
Copy-Item '.\tests\CS2MiniGames.Tests\TetrominoCatalogTests.cs' '.\backup_before_tetris_mvp_20260717\task02\TetrominoCatalogTests.cs'
```

Extend `TetrominoCatalogTests` with this exact parameterized test and add a separate assertion that the four O states are sequence-equal:

```csharp
[Theory]
[InlineData(TetrominoType.I)]
[InlineData(TetrominoType.O)]
[InlineData(TetrominoType.T)]
[InlineData(TetrominoType.S)]
[InlineData(TetrominoType.Z)]
[InlineData(TetrominoType.J)]
[InlineData(TetrominoType.L)]
public void EveryRotationHasFourUniqueCells(TetrominoType type)
{
    foreach (var rotation in Enum.GetValues<RotationState>())
    {
        var cells = TetrominoCatalog.GetCells(type, rotation);
        Assert.Equal(4, cells.Count);
        Assert.Equal(4, cells.Distinct().Count());
    }
}

[Fact]
public void ISpawnUsesStandardHorizontalCoordinates()
{
    Assert.Equal(
        new[] { new Cell(0, 1), new Cell(1, 1), new Cell(2, 1), new Cell(3, 1) },
        TetrominoCatalog.GetCells(TetrominoType.I, RotationState.Spawn));
}
```

Create `SevenBagRandomizerTests` that calls `Next()` fourteen times, splits the result into two groups of seven, and asserts each group ordered by enum value equals `Enum.GetValues<TetrominoType>()`. Inject `Random(12345)` so the shuffle is deterministic.

Use these exact public calls:

```csharp
var cells = TetrominoCatalog.GetCells(TetrominoType.I, RotationState.Spawn);
IPieceSource bag = new SevenBagRandomizer(new Random(12345));
TetrominoType next = bag.Next();
bag.Reset();
```

Run the two test classes and expect compile failures for the missing types.

- [ ] **Step 2: Implement the geometry contracts**

Create:

```csharp
namespace CS2MiniGames.Tetris.Core;

public readonly record struct Cell(int X, int Y);

public enum RotationState { Spawn, Right, Reverse, Left }

public readonly record struct ActivePiece(
    TetrominoType Type,
    RotationState Rotation,
    int X,
    int Y)
{
    public ActivePiece Move(int dx, int dy) => this with { X = X + dx, Y = Y + dy };
    public ActivePiece RotateTo(RotationState rotation) => this with { Rotation = rotation };
}

public interface IPieceSource
{
    TetrominoType Next();
    void Reset();
}
```

Implement `TetrominoCatalog` as a read-only dictionary keyed by `(TetrominoType, RotationState)`. Copy the exact coordinates from Appendix A of this plan. `GetCells` returns `IReadOnlyList<Cell>` backed by arrays that are never returned as a mutable type.

Implement `SevenBagRandomizer : IPieceSource` with a private queue. When empty, copy all enum values, Fisher-Yates shuffle them with the injected `Random`, then enqueue all seven. `Next()` dequeues one value. `Reset()` clears the queue so the next `Next()` creates a new complete shuffled bag.

- [ ] **Step 3: Verify and commit**

```powershell
dotnet test '.\CS2MiniGames.sln' --filter 'FullyQualifiedName~TetrominoCatalogTests|FullyQualifiedName~SevenBagRandomizerTests'
dotnet test '.\CS2MiniGames.sln'
git add src/CS2MiniGames/Tetris/Core tests/CS2MiniGames.Tests
git diff --cached --check
git commit -m 'feat: add tetromino catalog and seven bag'
```

Expected: all geometry and bag tests pass; no backup is staged.

### Task 3: Implement board collision, locking, line clearing, and SRS wall kicks

**Files:**
- Create: `src/CS2MiniGames/Tetris/Core/TetrisBoard.cs`
- Create: `src/CS2MiniGames/Tetris/Core/TetrisRotationSystem.cs`
- Create: `tests/CS2MiniGames.Tests/TetrisBoardTests.cs`
- Create: `tests/CS2MiniGames.Tests/TetrisRotationSystemTests.cs`

**Interfaces:**
- Produces: `TetrisBoard.CanPlace`, `Lock`, `ClearFullLines`, `GetCell`; `TetrisRotationSystem.TryRotate`.

- [ ] **Step 1: Write failing board tests**

Cover these exact cases: board constants are width 10, total height 22, hidden rows 2; cells outside x=0..9 or y=0..21 cannot be placed; overlapping locked cells cannot be placed; locking writes all four cells; clearing one and four full rows compacts rows downward and returns the cleared count.

Use this API:

```csharp
var board = new TetrisBoard();
bool allowed = board.CanPlace(piece);
board.Lock(piece);
int cleared = board.ClearFullLines();
TetrominoType? cell = board.GetCell(x, y);
```

Run the test class; expect compile failure because `TetrisBoard` is missing.

- [ ] **Step 2: Implement the specified board behavior**

Use `TetrominoType?[,] _cells = new TetrominoType?[22,10]`. Translate local tetromino cells using `piece.X + cell.X` and `piece.Y + cell.Y`. Reject any translated coordinate outside the array. `ClearFullLines` scans bottom-up; for every full row, copy all rows above down by one, clear row 0, increment the count, and re-check the same y index.

- [ ] **Step 3: Write failing SRS tests**

Tests must assert: unobstructed clockwise and counterclockwise rotations keep the origin; JLSTZ performs the documented wall kick at the left wall; I uses its independent kick table; O rotation succeeds without moving occupied cells; rotation fails and returns the original piece when all five kick targets collide.

Use:

```csharp
bool rotated = TetrisRotationSystem.TryRotate(
    board,
    piece,
    clockwise: true,
    out ActivePiece result);
```

- [ ] **Step 4: Implement exact SRS transition tables**

Create dictionaries for all eight directed transitions for JLSTZ and I using Appendix B exactly. Offsets use board coordinates where positive y points downward. Iterate offsets in the listed order and return the first candidate accepted by `board.CanPlace`; O only changes the rotation state and keeps its origin.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet test '.\CS2MiniGames.sln' --filter 'FullyQualifiedName~TetrisBoardTests|FullyQualifiedName~TetrisRotationSystemTests'
dotnet test '.\CS2MiniGames.sln'
git add src/CS2MiniGames/Tetris/Core tests/CS2MiniGames.Tests
git diff --cached --check
git commit -m 'feat: add tetris board and SRS rotation'
```

### Task 4: Implement scoring, gravity, hold, ghost, lock delay, and complete game state

**Files:**
- Create: `src/CS2MiniGames/Tetris/Core/TetrisScoring.cs`
- Create: `src/CS2MiniGames/Tetris/Core/TetrisGameOptions.cs`
- Create: `src/CS2MiniGames/Tetris/Core/TetrisGameState.cs`
- Create: `tests/CS2MiniGames.Tests/TetrisScoringTests.cs`
- Create: `tests/CS2MiniGames.Tests/TetrisGameStateTests.cs`

**Interfaces:**
- Produces: immutable options; scoring functions; a complete deterministic `TetrisGameState` API consumed by the renderer and live session.

- [ ] **Step 1: Write failing scoring tests**

Test `ScoreForLines(1..4, level)` equals 100/300/500/800 times level; unsupported counts return 0; `LevelForLines` starts at 1 and rises every configured 10 lines; `FallInterval` follows `max(80, round(800 * 0.85^(level-1)))` and never falls below 80ms.

Define calls exactly as:

```csharp
TetrisScoring.ScoreForLines(clearedLines, currentLevel);
TetrisScoring.LevelForLines(totalLines, linesPerLevel);
TetrisScoring.FallInterval(level, initialMs, minimumMs);
```

- [ ] **Step 2: Implement scoring and options**

Create the options contract exactly as:

```csharp
public sealed record TetrisGameOptions(
    int InitialFallIntervalMs = 800,
    int MinimumFallIntervalMs = 80,
    int LockDelayMs = 500,
    int MaxLockResets = 15,
    int HorizontalRepeatDelayMs = 150,
    int HorizontalRepeatIntervalMs = 50,
    int LinesPerLevel = 10);
```

Implement the three pure scoring functions with argument validation for non-positive level/interval values.

- [ ] **Step 3: Write failing game-state tests**

Create a test-only `StubPieceSource : IPieceSource` backed by a queue and a recorded reset count. Cover: standard spawn at x=3/y=0; left/right collision; soft drop adds 1 per moved cell; hard drop adds 2 per moved cell and locks immediately; ghost equals the last valid downward position; empty hold takes the next piece; non-empty hold swaps and resets rotation/position; hold cannot repeat before lock; lock clears rows and adds score; level updates; grounded piece locks after 500ms; successful grounded movement resets lock time at most 15 times; blocked spawn sets `IsGameOver`; `Restart()` clears score/board/hold, calls `IPieceSource.Reset()` once, and starts a fresh sequence.

Use this public API:

```csharp
var game = new TetrisGameState(options, pieceSource);
game.MoveLeft(); game.MoveRight(); game.SoftDrop(); game.HardDrop();
game.RotateClockwise(); game.RotateCounterClockwise(); game.Hold();
game.Advance(TimeSpan delta);
game.Restart();
ActivePiece ghost = game.GhostPiece;
```

- [ ] **Step 4: Implement `TetrisGameState`**

Keep `Board`, `ActivePiece`, `HoldPiece`, `NextPiece`, `Score`, `TotalLines`, `Level`, and `IsGameOver` readable. Maintain gravity and lock accumulators per instance. Successful grounded move/rotation resets lock time only while `lockResetCount < MaxLockResets`. Hard drop repeatedly moves down, adds `2 * distance`, then calls a single private `LockActivePiece`. Soft drop moves once and adds 1 only on success. Locking writes the piece, clears lines, scores using the pre-clear level, updates total lines/level, resets hold availability, and spawns the next piece. `Restart` replaces the board and all counters and requests a fresh active/next sequence from the supplied source.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet test '.\CS2MiniGames.sln' --filter 'FullyQualifiedName~TetrisScoringTests|FullyQualifiedName~TetrisGameStateTests'
dotnet test '.\CS2MiniGames.sln'
git add src/CS2MiniGames/Tetris/Core tests/CS2MiniGames.Tests
git diff --cached --check
git commit -m 'feat: add complete tetris game state'
```

### Task 5: Render the CenterHtml layout

**Files:**
- Create: `src/CS2MiniGames/Tetris/TetrisRenderer.cs`
- Create: `tests/CS2MiniGames.Tests/TetrisRendererTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<string> RenderBoardRows(TetrisGameState game)` and `string Render(TetrisGameState game)`.

- [ ] **Step 1: Write failing renderer tests**

Assert exactly 20 visible rows, exactly 10 cell `<font>` elements per row, hidden rows absent, active piece overrides ghost, locked cells override ghost, all seven color names appear for controlled boards, and output contains title/score/level/lines plus Hold and Next previews. Game-over output includes `Game Over`, `[R]`, and `[Tab]`.

- [ ] **Step 2: Implement the renderer**

Use `fontSize-l` for board cells and `fontSize-s` for previews. Use colors: I `Cyan`, O `Gold`, T `MediumPurple`, S `LimeGreen`, Z `Red`, J `DodgerBlue`, L `Orange`, ghost `Gray`, empty `DimGray`. Build a composite cell map in assignment order ghost→locked→active so locked cells replace ghost cells and the active piece replaces both. Render internal rows y=2..21 only. Join board rows with `<br>` below the title/status/preview block.

- [ ] **Step 3: Verify and commit**

```powershell
dotnet test '.\CS2MiniGames.sln' --filter 'FullyQualifiedName~TetrisRendererTests'
dotnet test '.\CS2MiniGames.sln'
git add src/CS2MiniGames/Tetris/TetrisRenderer.cs tests/CS2MiniGames.Tests/TetrisRendererTests.cs
git diff --cached --check
git commit -m 'feat: render tetris CenterHtml'
```

### Task 6: Add reusable input tracking and single-session management

**Files:**
- Create: `src/CS2MiniGames/Framework/MiniGameAction.cs`
- Create: `src/CS2MiniGames/Framework/IMiniGameSession.cs`
- Create: `src/CS2MiniGames/Framework/PlayerInputTracker.cs`
- Create: `src/CS2MiniGames/Framework/MiniGameManager.cs`
- Create: `tests/CS2MiniGames.Tests/PlayerInputTrackerTests.cs`
- Create: `tests/CS2MiniGames.Tests/MiniGameManagerTests.cs`

**Interfaces:**
- Produces: action mapping/repeat behavior; idempotent per-slot session lifecycle.

- [ ] **Step 1: Write failing input tests**

Map CounterStrikeSharp buttons exactly: Moveleft→MoveLeft, Moveright→MoveRight, Back→SoftDrop, Jump→HardDrop, Use→RotateClockwise, Reload→RotateCounterClockwise, Forward→Hold, Scoreboard→Exit. Edge actions fire once per press. Held A/D fires immediately, then at 150ms, then every 50ms. Held S emits SoftDrop immediately and every 50ms. Simultaneous opposite horizontal directions emit neither action. R remains `RotateCounterClockwise`; game-over restart is interpreted by `TetrisSession`, not the tracker.

- [ ] **Step 2: Implement the tracker and contracts**

Define `MiniGameAction` values `MoveLeft,MoveRight,SoftDrop,HardDrop,RotateClockwise,RotateCounterClockwise,Hold,Exit`. Define `IMiniGameSession` with `int PlayerSlot`, `bool IsClosed`, `void HandleActions(IReadOnlyCollection<MiniGameAction>)`, `void Update(TimeSpan)`, `string Render()`, and `void Close()`. `PlayerInputTracker.Read(PlayerButtons current, TimeSpan now)` returns actions and stores prior buttons plus independent repeat timestamps.

- [ ] **Step 3: Write failing manager tests and implement**

Test `TryStart` rejects a second session for the same slot, `TryGet` returns the active session, `Close(slot)` calls session close exactly once, `CloseAll` is idempotent, and `UpdateAll` isolates an exception from one session while updating the others through an injected error callback.

Implement with `Dictionary<int, IMiniGameSession>` and snapshot `.Values.ToArray()` before iteration so closing during update is safe.

- [ ] **Step 4: Verify and commit**

```powershell
dotnet test '.\CS2MiniGames.sln' --filter 'FullyQualifiedName~PlayerInputTrackerTests|FullyQualifiedName~MiniGameManagerTests'
dotnet test '.\CS2MiniGames.sln'
git add src/CS2MiniGames/Framework tests/CS2MiniGames.Tests
git diff --cached --check
git commit -m 'feat: add reusable minigame session framework'
```

### Task 7: Add SQLite leaderboard persistence

**Files:**
- Create: `src/CS2MiniGames/Persistence/LeaderboardEntry.cs`
- Create: `src/CS2MiniGames/Persistence/LeaderboardRepository.cs`
- Create: `tests/CS2MiniGames.Tests/LeaderboardRepositoryTests.cs`

**Interfaces:**
- Produces: initialize, save-if-higher, top-ten, and per-player lookup APIs.

- [ ] **Step 1: Write failing repository tests**

Use a unique temporary directory per test and delete it in `Dispose`. Test schema creation, first insert, lower/equal score ignored, higher score replaces score/lines/level/name, game keys remain isolated, top results order by score desc then lines desc then updated time asc, limit is 10, and missing player returns null.

Use:

```csharp
using var repo = new LeaderboardRepository(databasePath);
repo.Initialize();
repo.SaveIfHigher(entry);
IReadOnlyList<LeaderboardEntry> top = repo.GetTop("tetris", 10);
LeaderboardEntry? own = repo.GetPlayerBest("tetris", steamId);
```

- [ ] **Step 2: Implement repository and schema**

Create table `leaderboard(game TEXT NOT NULL, steam_id TEXT NOT NULL, player_name TEXT NOT NULL, score INTEGER NOT NULL, lines INTEGER NOT NULL, level INTEGER NOT NULL, updated_utc TEXT NOT NULL, PRIMARY KEY(game,steam_id))`. Use parameterized SQL only. Use `INSERT ... ON CONFLICT(game,steam_id) DO UPDATE ... WHERE excluded.score > leaderboard.score`. Store SteamID64 as invariant decimal text and UTC timestamps as ISO-8601 round-trip strings.

- [ ] **Step 3: Verify and commit**

```powershell
dotnet test '.\CS2MiniGames.sln' --filter 'FullyQualifiedName~LeaderboardRepositoryTests'
dotnet test '.\CS2MiniGames.sln'
git add src/CS2MiniGames/Persistence tests/CS2MiniGames.Tests/LeaderboardRepositoryTests.cs
git diff --cached --check
git commit -m 'feat: persist minigame leaderboards'
```

### Task 8: Integrate Tetris sessions with CounterStrikeSharp lifecycle and commands

**Files:**
- Create: `src/CS2MiniGames/MiniGamesConfig.cs`
- Create: `src/CS2MiniGames/Framework/PlayerMovementGuard.cs`
- Create: `src/CS2MiniGames/Tetris/TetrisSession.cs`
- Create: `src/CS2MiniGames/CS2MiniGamesPlugin.cs`
- Create: `tests/CS2MiniGames.Tests/MiniGamesConfigTests.cs`
- Create: `tests/CS2MiniGames.Tests/TetrisSessionTests.cs`

**Interfaces:**
- Consumes: all prior core/framework/renderer/repository APIs.
- Produces: loadable plugin with commands, one global tick, safe freeze/restore, lifecycle cleanup, and highscore display.

- [ ] **Step 1: Write failing config tests**

Test defaults `800,80,500,15,150,50,10`. Test clamps: initial 100–5000; minimum 20–1000 and no greater than initial; lock 100–2000; resets 0–50; repeat delay 50–1000; repeat interval 20–500; lines 1–50. Expose `MiniGamesConfig Normalize(Action<string> warn)` returning a normalized copy and one warning per corrected field.

- [ ] **Step 2: Write failing session tests and implement `TetrisSession`**

Construct `TetrisSession` with `playerSlot`, `TetrisGameState`, `TetrisRenderer`, `Action<TetrisResult> saveResult`, and `Action closePlayerBinding`. It must not hold `CCSPlayerController`, database connections, or Pawn handles. Define `TetrisResult(int Score,int Lines,int Level)`.

Tests assert: actions map to the correct game methods; when game over, RotateCounterClockwise invokes `Restart` instead of rotation; Exit calls `Close`; two `Close()` calls invoke `closePlayerBinding` once; crossing into game over invokes `saveResult` exactly once; later updates do not save again; restart allows the next game over to save once again.

`Update` advances only its own game. `Render` delegates to the renderer. `HandleActions` ignores gameplay actions after close. This keeps gameplay/lifecycle behavior testable without live CS2 objects.

- [ ] **Step 3: Implement safe movement guarding**

`Freeze(CCSPlayerController)` checks controller/pawn validity, sets `MoveType = MOVETYPE_NONE`, sets `m_nActualMoveType` to 0, and marks `m_MoveType` changed. `Restore` resolves the controller's current Pawn at call time, sets `MOVETYPE_WALK`, actual move type 2, and marks state changed. Both methods return without throwing for null/invalid Pawn and are idempotent.

- [ ] **Step 4: Implement plugin entry and commands**

`CS2MiniGamesPlugin : BasePlugin, IPluginConfig<MiniGamesConfig>` reports module name `CS2 Mini Games`, version `0.1.0`, and GPL-compatible attribution in README. In `Load`, normalize config, initialize `minigames.db`, create manager, register one `Listeners.OnTick`, and register player spawn, round start, disconnect, and map-end cleanup. Maintain a per-slot runtime binding containing the command caller's `CCSPlayerController` and `PlayerInputTracker`; player/Pawn objects never enter the pure Tetris rules. When restoring after spawn or round transition, resolve the current controller with `Utilities.GetPlayers().FirstOrDefault(p => p.Slot == slot)` instead of reusing an old Pawn. In `Unload`, remove listeners/handlers where required, call `CloseAll`, restore current valid Pawns through close callbacks, clear runtime bindings, and dispose repository.

Commands:

```csharp
[ConsoleCommand("css_tetris")]
[ConsoleCommand("css_toptetris")]
[ConsoleCommand("css_tetrishelp")]
[ConsoleCommand("css_minigames")]
```

Reject null console callers and bots for gameplay. `css_tetris` creates a session only when the slot is free, creates its runtime binding, freezes the player, and prints controls once. Bind `saveResult` to a repository call using the starting player's SteamID and current display name; bind `closePlayerBinding` to remove the runtime binding and restore the slot's current valid Pawn. `css_toptetris` prints top 10 plus the caller's best. Spawn and round start close relevant/all sessions before gameplay continues. `OnTick` uses a `Stopwatch` for monotonic frame elapsed time, reads each valid binding's buttons through its tracker, sends actions, advances the session, and calls `PrintToCenterHtml` with rendered HTML. Wrap each player's input/update/render block in its own `try/catch`, log the slot and exception, close only that failed session, and continue updating other players.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet test '.\CS2MiniGames.sln' --filter 'FullyQualifiedName~MiniGamesConfigTests|FullyQualifiedName~TetrisSessionTests'
dotnet test '.\CS2MiniGames.sln'
dotnet build '.\CS2MiniGames.sln' -c Release
git add src/CS2MiniGames tests/CS2MiniGames.Tests/MiniGamesConfigTests.cs tests/CS2MiniGames.Tests/TetrisSessionTests.cs
git diff --cached --check
git commit -m 'feat: integrate tetris CounterStrikeSharp plugin'
```

Expected: all tests pass; Release build has 0 errors; any warnings are reviewed and fixed rather than accepted without explanation.

### Task 9: Document, package-check, and perform final local verification

**Files:**
- Create: `README.md`
- Create: `scripts/Verify-Package.ps1`
- Create: `tests/CS2MiniGames.Tests/PluginMetadataTests.cs`

**Interfaces:**
- Produces: operator/player documentation and a repeatable local verification gate; no server mutation.

- [ ] **Step 1: Write failing metadata/package checks**

Add a metadata test asserting module name `CS2 Mini Games`, version `0.1.0`, and the four command names through reflection. Create `scripts/Verify-Package.ps1` that accepts the Release output directory, requires `CS2MiniGames.dll`, `CS2MiniGames.deps.json`, `Microsoft.Data.Sqlite.dll`, and `runtimes/linux-x64/native/libe_sqlite3.so`, and fails if it finds `.db`, `.log`, `.bak`, backup directories, `TestResults`, or user configuration.

- [ ] **Step 2: Write README and pass checks**

README must contain: purpose; GPL-3.0 notice; .NET 8/CounterStrikeSharp compatibility; build command; plugin directory layout; first-install dependency note; config path; command list; exact controls; all-player freeze warning; automatic exit lifecycle; database path; local test command; and a statement that server deployment is not performed by the build.

Run:

```powershell
dotnet test '.\CS2MiniGames.sln'
dotnet build '.\CS2MiniGames.sln' -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File '.\scripts\Verify-Package.ps1' -OutputPath '.\src\CS2MiniGames\bin\Release\net8.0'
```

Expected: all tests pass, build exits 0 with 0 errors, package verifier prints a PASS line.

- [ ] **Step 3: Review tracked/ignored scope and commit**

```powershell
git diff --check
git status --short --ignored
git add README.md scripts tests/CS2MiniGames.Tests/PluginMetadataTests.cs
git status --short --ignored
git diff --cached --stat
git diff --cached --check
git commit -m 'docs: add tetris build and test guidance'
```

Expected: no `bin/`, `obj/`, database, backup, log, test results, or local config is staged.

- [ ] **Step 4: Fresh verification before completion**

```powershell
dotnet test '.\CS2MiniGames.sln' --no-restore
dotnet build '.\CS2MiniGames.sln' -c Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File '.\scripts\Verify-Package.ps1' -OutputPath '.\src\CS2MiniGames\bin\Release\net8.0'
git status --short --branch
git log --oneline --decorate -12
```

Expected: tests report 0 failed; build reports 0 errors; package verifier passes; working tree is clean on `feat/tetris-mvp`.

- [ ] **Step 5: Record manual CS2 checks without deploying**

Report these as pending user-authorized checks:

```text
1. !tetris opens a 10×20 board without clipping at the target resolution.
2. A/D/S/Space/E/R/W/Tab match the approved controls.
3. SRS kicks, hold-once, ghost, soft/hard drop, scoring, levels, and lock delay feel correct.
4. Spawn and round start close the game and restore movement.
5. Two or more players can play with independent speed, bag, and board state.
6. !toptetris persists only higher scores in minigames.db.
7. Hot reload and unload do not leave players frozen.
```

Do not package for production or touch the server until the user explicitly approves deployment.

## Appendix A: Exact tetromino local coordinates

Coordinates are `(x,y)` in a 4×4 local box, with positive y downward. `S`, `R`, `2`, and `L` mean Spawn, Right, Reverse, and Left.

```text
I S: (0,1) (1,1) (2,1) (3,1)
I R: (2,0) (2,1) (2,2) (2,3)
I 2: (0,2) (1,2) (2,2) (3,2)
I L: (1,0) (1,1) (1,2) (1,3)

O S/R/2/L: (1,0) (2,0) (1,1) (2,1)

T S: (1,0) (0,1) (1,1) (2,1)
T R: (1,0) (1,1) (2,1) (1,2)
T 2: (0,1) (1,1) (2,1) (1,2)
T L: (1,0) (0,1) (1,1) (1,2)

J S: (0,0) (0,1) (1,1) (2,1)
J R: (1,0) (2,0) (1,1) (1,2)
J 2: (0,1) (1,1) (2,1) (2,2)
J L: (1,0) (1,1) (0,2) (1,2)

L S: (2,0) (0,1) (1,1) (2,1)
L R: (1,0) (1,1) (1,2) (2,2)
L 2: (0,1) (1,1) (2,1) (0,2)
L L: (0,0) (1,0) (1,1) (1,2)

S S: (1,0) (2,0) (0,1) (1,1)
S R: (1,0) (1,1) (2,1) (2,2)
S 2: (1,1) (2,1) (0,2) (1,2)
S L: (0,0) (0,1) (1,1) (1,2)

Z S: (0,0) (1,0) (1,1) (2,1)
Z R: (2,0) (1,1) (2,1) (1,2)
Z 2: (0,1) (1,1) (1,2) (2,2)
Z L: (1,0) (0,1) (1,1) (0,2)
```

## Appendix B: Exact SRS kick offsets

Offsets are `(dx,dy)` in board coordinates with positive y downward. Try each list from left to right.

```text
JLSTZ S->R: (0,0) (-1,0) (-1,-1) (0,2) (-1,2)
JLSTZ R->S: (0,0) (1,0) (1,1) (0,-2) (1,-2)
JLSTZ R->2: (0,0) (1,0) (1,1) (0,-2) (1,-2)
JLSTZ 2->R: (0,0) (-1,0) (-1,-1) (0,2) (-1,2)
JLSTZ 2->L: (0,0) (1,0) (1,-1) (0,2) (1,2)
JLSTZ L->2: (0,0) (-1,0) (-1,1) (0,-2) (-1,-2)
JLSTZ L->S: (0,0) (-1,0) (-1,1) (0,-2) (-1,-2)
JLSTZ S->L: (0,0) (1,0) (1,-1) (0,2) (1,2)

I S->R: (0,0) (-2,0) (1,0) (-2,1) (1,-2)
I R->S: (0,0) (2,0) (-1,0) (2,-1) (-1,2)
I R->2: (0,0) (-1,0) (2,0) (-1,-2) (2,1)
I 2->R: (0,0) (1,0) (-2,0) (1,2) (-2,-1)
I 2->L: (0,0) (2,0) (-1,0) (2,-1) (-1,2)
I L->2: (0,0) (-2,0) (1,0) (-2,1) (1,-2)
I L->S: (0,0) (1,0) (-2,0) (1,2) (-2,-1)
I S->L: (0,0) (-1,0) (2,0) (-1,-2) (2,1)
```
