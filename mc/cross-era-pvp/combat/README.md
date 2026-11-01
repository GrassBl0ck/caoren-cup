# 跨时代战斗原型 0.1.0-dev

状态：实验，待真实客户端及原版对照验收。目标为现代客户端上的 1.8.9 / 26.3 两套近战规则。
本插件管理 OCM 的命名规则与观察日志；OCM v2.8.0 负责战斗。本轮不包含正式小游戏、旧客户端接入、地图切换或自研伤害重算。

## 构建

需要 JDK 25。将官方 OCM v2.8.0 独立 JAR 放入 `libs/OldCombatMechanics.jar`，或传 `-PocmJar=绝对路径`。
可从本仓库 `mc/third-party/old-combat-mechanics/` 的固定源码构建；不要拿其他版本替代。

```powershell
.\gradlew.bat test jar
# 可选：.\gradlew.bat test jar -PocmJar=D:/path/OldCombatMechanics.jar
```

Linux 首先赋予 `gradlew` 执行权限，然后使用 `./gradlew test jar`。
产物为 `build/libs/CrossEraCombat-0.1.0-dev.jar`。依赖缓存、libs、世界、日志与构建产物不提交。
第三方边界见 [THIRD-PARTY.md](THIRD-PARTY.md)。测试需要同一仓库中的固定 OCM 配置原件作分类对照；本工程不会修改它。

## 实验配置与管理

默认 `experimental-enabled: false`、`allowed-worlds: []`，没有启用世界或战斗接管。
只在获准的新实验环境安装，保留正版验证；不要将 OCM 或本插件直接放入现有正式服的共享 plugins。

1. 在获准的实验服务器准备一个专用世界；示例名为 `cross-era-test`。本文不自动建世界或发放物品。
2. 备份原有配置与数据，停服后安装两个独立 JAR。OCM 不打入自研插件。
3. 仅在新实验环境以 `examples/ocm-config.yml` 配置 OCM，以 `examples/combat-config.yml` 配置本插件。两个世界名称须一致。
4. 完整启动，先检查无配置/API错误，再用管理员命令核对模块；不使用 `/reload`。

所有下列命令都要求 `cross-era.combat.admin`（默认 OP），只接受在线玩家；反馈仅执行者可见，绿色 `[草人杯]`。

```text
/cecombat status
/cecombat status <在线玩家>
/cecombat profile <在线玩家> <old|modern>
```

选择通过 OCM 正常 API 按 UUID、世界保存，重连后保留。没有另一份自研玩家规则表。
新玩家遵守 OCM 的现代默认。任意模块 overrides 会导致切换拒绝；本插件不会清空其他插件的覆盖。
模块审计检查旧预设仅启用本轮七项、现代全部关闭；这只是配置一致性，不是原版公式验收。
如果 OCM 已保存选择但模块不符，命令会明确报告不一致，不能将其当作成功的战斗验证。后端调用失败时用 status 核对实际保存值，避免假定外部 API 是可回滚事务。

配置字段在完整重启时读取：实验开关为布尔值；世界为无重复的精确名称列表；后端固定 `ocm`；预设为互不相同的小写标识；日志队列为 16—65536 的整数，默认 2048。
缺依赖、配置非法、默认顺序错误或 API 不符时停止本插件；不替服务器安装、停用 OCM，也不退回另一种结算实现。

## 日志与接口

`plugins/CrossEraCombat/events.jsonl` 为 UTF-8 JSONL，schemaVersion=1。
事件包括 startup、damage_event、health_followup、velocity_event、ocm_modeset_change、player_state、shutdown。
UUID、两方规则、模块状态、装备耐久、位置速度、冷却及受伤状态在主线程采样；后台只接收字符串，250ms 轮询、最长约500ms批量刷出，无游戏对象异步访问。

- `eventBaseDamage` / `eventFinalDamage` 是 MONITOR 事件数值，不等于实际扣血。
- `health_followup.netHealthDelta` 是到下一次采样的净生命变化，可能混入其他攻击、治疗或复活，不自动归因于单次命中。
- `attackCooldownObserved` 是采样时公开 API 返回值，不保证是攻击前冷却；暴击相关字段记录条件，不伪称原生暴击判定。
- 速度事件不自动归因于某次命中；合成 Bukkit 事件也无法仅凭观察判断为真实输入。
- 队列满会拒绝记录并限频告警，不阻塞攻击。status 显示 accepted/rejected/errors；关闭最多等待5秒收尾。出现丢记录、I/O错误或收尾超时，本轮证据不能视为完整。
- 记录为私有研究数据，不进 Git 或发行包；不采集账号凭据、昵称、聊天、物品自定义文本或网站会话。

启用成功时通过 Bukkit Services 注册 `com.caoren.crossera.core.CombatService`；其公开 `profile`、`select`、`routing` 均须在服务端主线程使用。
`routing` 分别返回攻击者与受击者身份，不直接计算伤害。调用方须检查 `verifiedModules`、overrides 和异常，不能从规则名称推断还原已通过。
后续自研实现替换后端，未来小游戏不依赖 OCM 内部包。

## 停用和验收

停用前在允许世界把实验玩家切回 modern，并核对 status；停服、备份 OCM 玩家数据后移除测试组件。
只移除管理插件不会撤销 OCM 已保存的模式。未上线的实验玩家仍可能保留 old，恢复使用前逐个核对；不按空资料清玩家数据。
插件本身不发物品、不传送、不创建方块，也不重算伤害或施加速度。OCM 自己的属性和耐久行为见 [模块审计](../docs/stage04/OCM-AUDIT.md)。

原版对照步骤和未验证项见 [阶段04验收表](../docs/stage04/ACCEPTANCE.md)。构建/单测通过不能将整个阶段04标记完成。
