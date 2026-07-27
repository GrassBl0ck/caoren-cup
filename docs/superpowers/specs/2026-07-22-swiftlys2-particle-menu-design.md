# 草人杯 SwiftlyS2 粒子菜单本地测试设计

## 状态与目标

本设计替代已废弃的 CounterStrikeSharp-only 粒子菜单方案。目标是在新版 SwiftlyS2 上制作本地、私有的两层可点击测试菜单，同时保持现有 CaorenCup CounterStrikeSharp 插件不迁移、不改动、不接入菜单业务。

第一阶段只证明四件事：SwiftlyS2 与 CounterStrikeSharp 可以隔离共存；菜单可使用原始 UserCmd 输入；菜单期间不会开火；菜单在所有退出路径都能恢复玩家状态。

## 项目归属

私有菜单项目位于：

```text
D:\OpenSourcework\caoren-particle-menu-swiftlys2\
```

该目录是公开仓库 `D:\OpenSourcework\caoren-cup-open-source\` 的同级目录，不属于该仓库。不得把私有菜单源码复制、移动、作为子模块添加或提交到公开仓库。

第一阶段不创建 GitHub Release，不增加现有三个公开 Release ZIP，也不改变 CaorenCup 的部署包。

## 架构边界

```text
CaorenCup / CounterStrikeSharp
  ├─ 现有娱乐玩法、网页桥接和比赛逻辑
  ├─ 不引用 SwiftlyS2
  └─ 不处理粒子菜单

CaorenParticleMenu / SwiftlyS2
  ├─ ProcessUserCmd 前置输入处理
  ├─ 视角锁、虚拟光标、攻击/副攻击/使用/切枪拦截
  ├─ 两层测试菜单和粒子渲染
  ├─ 玩家会话与退出清理
  └─ 共存诊断日志
```

SwiftlyS2 菜单插件不得直接调用 CaorenCup 的 C# 对象、配置或业务方法。第一阶段只有固定菜单；后续如需调用业务，必须单独设计带玩家身份校验的跨框架桥接。

## 私有项目结构

```text
caoren-particle-menu-swiftlys2/
├─ src/CaorenParticleMenu/
│  ├─ MenuPlugin/
│  ├─ InputController/
│  ├─ MenuSession/
│  ├─ MenuNavigation/
│  ├─ ParticleRenderer/
│  └─ CompatibilityProbe/
├─ config/particle-menu.json
├─ tests/
├─ docs/coexistence-test-plan.md
├─ docs/rollback.md
└─ scripts/build-local.ps1
```

最终使用的 SwiftlyS2 版本、官方模板、托管插件构建目标和精确目录结构必须在创建本地项目时按照该版本官方文档锁定；不得从旧 Swiftly Lua 文档推断。

## 第一阶段菜单

```text
测试主菜单
├─ 打开测试子页面
└─ 关闭

测试子页面
├─ 返回主菜单
└─ 关闭
```

每名玩家只有一个活动菜单会话和一个当前页面。切页时销毁旧页面粒子后创建新页面粒子，不保留不可见父页面。

## 输入与清理契约

菜单活动时，SwiftlyS2 的 `ProcessUserCmd` 前置处理必须：读取鼠标位移维护虚拟光标；锁住视角；清除攻击、副攻击、使用和切枪输入；仅用攻击上升沿确认一次菜单项。

请求关闭后，插件继续清除攻击，直到从原始 UserCmd 确认左键已松开，才解除输入处理。玩家死亡、断线、换图、插件停止、渲染异常或菜单异常都必须走同一清理入口：删除粒子、清空会话、恢复视角与移动输入。

## 共存测试与回滚

旧 Swiftly Lua 的历史兼容性结论不能外推到新版 SwiftlyS2；新版没有对 CounterStrikeSharp 的官方兼容承诺。因此共存状态只能通过隔离测试服验证，不能预先承诺稳定。

依次验证：仅 SwiftlyS2；仅 CounterStrikeSharp/CaorenCup；两者同时运行的两种加载顺序；完整重启；连续换图；玩家连接、断线和死亡；网页桥接命令；菜单的打开、确认、返回和关闭；数小时稳定运行及崩溃转储。

失败回滚只停止加载 SwiftlyS2 菜单插件，保留 CounterStrikeSharp 和 CaorenCup 原状。第一阶段菜单不得写入草人杯配置、比赛数据或网页端状态，因此不需要业务数据回滚。

## 资源路线与验收

逻辑原型先使用 CS2 原生粒子。自定义粒子、材质和图标将在逻辑稳定后打进独立 Workshop Addon，并通过 MultiAddonManager 的服务器侧额外挂载模式在任意地图提供给玩家。

安装 SwiftlyS2、安装 MultiAddonManager、创建或上传 Workshop 内容、修改服务器配置、测试服部署和正式服部署均不属于本设计授权范围；每一项操作前必须单独获得用户明确同意。

第一阶段验收标准：

1. 主菜单可打开、进入子页、返回和关闭；
2. 菜单期间没有开火事件、弹药变化、伤害、枪声、弹孔或投掷物；
3. 按住左键关闭时，松开前没有开火，松开后下一次全新按下才允许开火；
4. 死亡、断线、换图、插件停止时没有视角锁、输入锁或粒子残留；
5. SwiftlyS2 与 CounterStrikeSharp/CaorenCup 同时运行时，现有草人杯和网页桥接正常；
6. 第一阶段没有真实业务动作和跨框架调用。
