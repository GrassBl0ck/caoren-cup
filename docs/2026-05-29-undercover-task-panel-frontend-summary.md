# 2026-05-29 卧底任务面板前端显示修复总结

## 处理范围

本次只处理网页端卧底任务面板的前端显示问题。

未修改：

- 统计后端逻辑
- CS2 娱乐插件本体
- 网页端服务器桥接插件

## 原始问题

1. 管理员编辑视角富文本正常，但玩家视角会把部分任务描述里的 HTML 原样显示，例如 `<b>副武器</b>` 没有加粗。
2. 玩家视角任务九宫格缩在左边，没有像管理员视角一样铺满容器。
3. 玩家点击锁定一个格子后，状态刷新会导致显示上的选中高亮和当前操作焦点丢失。
4. 玩家视角“我的任务操作记录”插在任务表和操作按钮中间，影响操作区连续性。

## 修改文件

- `web-command-center/public/js/lobby-app.js`
- `web-command-center/public/css/app.css`

## 修改前备份

按项目规则，修改前已备份目标文件：

- `web-command-center/public/js/lobby-app.js.bak-20260529-014128-undercover-panel`
- `web-command-center/public/css/app.css.bak-20260529-014128-undercover-panel`

备份文件按现有忽略规则不纳入 Git。

## 主要改动

### 1. 统一任务富文本渲染

新增安全任务富文本渲染函数，用于任务描述显示。

支持白名单标签：

- `<b>`
- `<strong>`
- `<u>`
- `<br>`

处理方式：

- 白名单标签保留显示效果。
- 其它 HTML 仍会转义，避免直接渲染不受控 HTML。
- 玩家任务格、管理员模板预览、备用任务预览使用同一套渲染逻辑。

### 2. 玩家九宫格铺满容器

调整 `.task-grid` 和 `.task-cell` 样式：

- 九宫格保持 3x3。
- 使用 `repeat(3, minmax(0, 1fr))` 三等分容器宽度。
- 去掉固定 170/180px 列宽带来的左侧挤压。
- 小屏断点也保持 3 列，避免变成 2 列或 1 列。

### 3. 刷新后恢复选中格子

新增 `applySelectedTaskCellState()`：

- 根据 `window.selectedCellId` 重新给对应格子加 `selected`。
- 同步恢复“当前操作焦点：格子 X”文字。
- 在玩家点击格子和 `renderLiveGame()` 重绘后都会调用。

这样玩家执行任务操作、页面收到状态刷新后，显示状态不会丢。

### 4. 调整操作记录位置

玩家视角中：

- 任务九宫格
- 操作按钮区域
- 我的任务操作记录

现在按这个顺序显示，操作记录不再插在任务表和按钮之间。

## 验证

已执行：

```powershell
cd D:\OpenSourcework\caoren-cup-open-source\web-command-center
npm run typecheck
```

结果：通过。

已启动本地开发服务并用浏览器检查：

```text
http://localhost:3000
```

浏览器验证覆盖：

- 管理员视角能看到卧底任务监控区。
- 玩家视角任务面板为 9 个格子。
- 九宫格为 3 列等宽布局。
- `<b>` / `<strong>` / `<br>` 被渲染为加粗和换行，不再原样显示。
- 点击 A1 后触发任务操作刷新，仍保留选中高亮。
- “当前操作焦点”刷新后仍显示 `【格子 A1】`。
- “我的任务操作记录”位于操作按钮区域下方。

## 截图说明

验证截图位于本地运行态目录：

- `web-command-center/runtime/admin-undercover-monitor.png`
- `web-command-center/runtime/player-undercover-panel.png`

其中早期管理员截图里任务文本出现问号，是验证时手写临时 `runtime/live-session-snapshot.json` 的中文测试数据在 Windows 终端写入时发生编码问题导致，不是前端渲染逻辑导致。

真实任务模板如果 UTF-8 内容正常，例如：

```html
<b>副武器</b><br>完成一次击杀
```

会按安全富文本逻辑显示为加粗“副武器”并换行。

## 注意事项

本次验证过程中为了构造本地页面状态，使用过 `web-command-center/runtime/live-session-snapshot.json` 临时运行态文件。该目录属于运行时数据，不应提交到 Git。

