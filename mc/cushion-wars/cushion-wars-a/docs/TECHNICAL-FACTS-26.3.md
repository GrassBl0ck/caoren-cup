# Minecraft Java 26.3：事实、证据与 P0 关卡

日期：2026-10-25。事实依据为此前原稿审计材料、本轮只读复核和官方发布资料。未启动游戏或服务端。静态参数不等于严格事件拦截能力。

## 1. 已核对的发布包身份

| 项目 | 值 |
|---|---|
| 版本 | Minecraft Java 26.3 |
| 发布类型 | release，stable=true |
| 客户端字节数 | 41,483,720 |
| 客户端 SHA-1 | e877b6a07acd633fb3bb475002175cec036e7b87 |
| 原包 version.json：world_version | 5023 |
| protocol_version | 777 |
| 数据包版本 | 121.0 |
| 资源包版本 | 97.1；本项目不要求资源包 |
| Java 主版本 | 25 |
| 服务端元数据 SHA-1 | 33680f5f2ac32864d6d7cf5e56a705fdb3e05f4c |
| 服务端元数据字节数 | 62,294,556 |

本轮曾重新计算现有客户端 SHA-1，与上述值一致。服务端信息来自元数据，**本轮没有下载或启动服务端，也未对本地服务端文件校验**。

官方来源：

- [26.3 发布说明](https://www.minecraft.net/en-us/article/minecraft-java-edition-26-3)
- [版本元数据](https://piston-meta.mojang.com/v1/packages/61fff1316bd50312624a858bbb5eb18fa45208ed/26.3.json)
- [客户端发布包](https://piston-data.mojang.com/v1/objects/e877b6a07acd633fb3bb475002175cec036e7b87/client.jar)
- [服务端发布包](https://piston-data.mojang.com/v1/objects/33680f5f2ac32864d6d7cf5e56a705fdb3e05f4c/server.jar)

不要只把 pack_format 改成某个旧整数就宣称兼容。按 26.3 实际元数据结构与加载器验证命令、标签、目录单复数、物品组件、属性、实体 NBT 和 pack.mcmeta。

## 2. 本包附带的静态证据

[版本元数据快照](../references/vanilla-audit/version-metadata.json) 及以下 javap 输出来自此前原包核验，均以来源清单中的 SHA-256 固定：

| 材料 | 使用时关注 |
|---|---|
| [Cushion](../references/vanilla-audit/Cushion.javap.txt) | interact、乘坐、乘客移除、受损、保存与实体形状 |
| [CushionItem](../references/vanilla-audit/CushionItem.javap.txt) | 放置、冒险模式限制、支撑面、消耗与事件时机 |
| [BowItem](../references/vanilla-audit/BowItem.javap.txt) | use、releaseUsing、getPowerForTime |
| [ProjectileWeaponItem](../references/vanilla-audit/ProjectileWeaponItem.javap.txt) | 箭种查找、发射和耐久/弹药 |
| [Player](../references/vanilla-audit/Player.javap.txt) | getProjectile、wantsToStopRiding、rideTick |
| [ThrownSplashPotion](../references/vanilla-audit/ThrownSplashPotion.javap.txt) | 原版碰撞、候选范围和瞬时效果 |
| [ProjectileUtil](../references/vanilla-audit/ProjectileUtil.javap.txt) | 投射物碰撞和动态容差 |
| [Consumables](../references/vanilla-audit/Consumables.javap.txt) | 默认饮用配置 |
| [Consumable](../references/vanilla-audit/Consumable.javap.txt) | consumeTicks、完成时机和使用效果 |

这些不是完整游戏源码。本包没有附带 client.jar、server.jar 或 Java 运行时，也没有假定 DeepSeek 的机器已安装它们。缺失的方法证据要回到可信原包核对，不能凭经验补结论。

## 3. 已核准参数及不能推出的结论

| 事实 | 可以作为实现输入 | 不能据此宣称 |
|---|---|---|
| 26.3 原生坐垫可放置/乘坐、水平对齐、无碰撞、支撑消失会破坏 | 使用原生坐垫的基础能力 | 可以按队伍/AP 提前阻止乘坐或下座 |
| 默认方块/实体交互属性 4.5/3.0 | 检查原版距离属性 | 所有方向都能移动固定 3 格 |
| 箭先副手，再主手，再库存索引 | 初始普通箭放副手 | 普通箭类型天然优先于药箭 |
| 满蓄力 20 tick、最早合法发射 3 tick | 线性玩法伤害的输入参数 | 原版弹速伤害曲线就是线性的 |
| 治疗 I 4 HP、治疗 II 完整 8 HP、饮用 32 tick | 治疗数值及时间 | 任意落点都给每个玩家 8 HP |
| 无限默认对普通箭免耗 | 普通箭重复、药箭有限 | 弓原版本来不消耗耐久 |

## 4. 已知原生路径与拦截风险

现有 Cushion.interact 静态输出显示普通乘坐分支会检查次要使用、是否已有乘客，再尝试 startRiding；该方法本身不认识本游戏的队伍、阶段或 AP。

Player.wantsToStopRiding 返回潜行状态，rideTick 中存在 stopRiding 路径。不能因为数据包每 tick 都能重新挂载，就认定“主动下座完全无效”已经达标。

这两点只说明必须研究真实拦截时机，不构成“纯数据包必定做不到”的终局结论。若借助辅助实体、物品组件、触发器或其他原版能力处理，应验证合法原生交互、坐姿眼高、输入优先、遮挡和箭路均未改变。

限定观战、禁止物资转交、真实蓄弓取消和同批伤亡同样不能仅凭每 tick 扫描能力判定通过。

## 5. 喷溅机制不能简化

核验清单记录的原版静态处理为：

1. 将药水碰撞箱移动到命中位置。
2. 候选框 X/Z 扩 4、Y 扩 2。
3. 目标碰撞箱按药水年龄对应容差扩张；容差为 (年龄 tick - 2)/20 限制在 0..0.3。
4. 以药水碰撞箱到扩张后目标碰撞箱最近距离 d 计算，仅 d < 4 处理，强度 1-d/4。
5. 治疗 II 标称恢复 int(8×强度+0.5)，最终不超过当前生命上限。

此处是审计摘要，场景实测仍需验证。不能将其替换成“脚部坐标的四格球”后称为保留原版机制。

## 6. P0 必须给出的结论

| 关卡 | 必须回答的问题 | 证据不足时 |
|---|---|---|
| G01 输入和 AP | 能否在真实输入路径保证失败无有效结果、无资源损失？ | 阻塞完整操作系统 |
| G02 乘坐 | 能否限制敌座、有人座、主动下座，并保留真实眼高/距离？ | 阻塞移动系统 |
| G03 门与放置 | 能否保持输入优先、占格、支撑和阶段门禁？ | 阻塞部署系统 |
| G04 箭矢 | 能否逐箭独立计伤、友方含本人穿箭、坐垫穿箭？ | 阻塞战斗系统 |
| G05 治疗顺序 | 能否保留原版治疗并做到死亡优先、跨阶段饮用和截止？ | 阻塞战斗/阶段结算 |
| G06 观战与断线 | 能否禁止有效自由侦察，且重连不能回场？ | 阻塞生命周期 |
| G07 信息隔离 | 能否暂时全显己方、只按条件显示敌方血量而无其他泄露？ | 阻塞正式 UI |
| G08 计时与收尾 | 能否按统一 tick 截止、去重和限定范围清理？ | 阻塞正式回合与安全重置 |

原型报告对每关只能填写：PASS、FAIL、NOT_RUN、BLOCKED；另列证据充分性。未跑、仅检查命令语法、只设置最终分数或只用虚拟实体，不得填写真实交互 PASS。

必要能力没有直接证据时不能整体验证通过。可交付已完成原型和可复现失败，询问用户后续方向；不得自动降级或加依赖。

## 7. 需要用户决定与需要技术验证的区别

- 原包里某个参数、事件能否取消、命令实际语法、眼高或碰撞结果：由执行者查证/测试，不能让用户猜。
- 只能通过改变输入、原生物件、信息范围或安装架构才能实现：整理事实和选项后由用户决定。
- 未明确的拆除交互、服务端重启中途恢复等，若确实影响可观察行为：不得假定。
- 已确认不限时、额外固定 6 HP、门不设额外场上上限：不因个人偏好或推测平衡而重新设计。

原始审计来源路径及各副本哈希见 [SOURCE-MANIFEST](../checks/SOURCE-MANIFEST.json)。静态几何连通与原版真实交互路线分别验收。
