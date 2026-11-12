# [草人杯] 离线证据完整性校验器

这是本地Python标准库工具，不是Minecraft插件。读取显式指定的证据副本，检查结构、哈希、关联和缺口；不连接服务器、不扫描其他目录、不改变原始文件，不计算伤害或决定玩法/用户验收。

当前支持[阶段04清单](../../docs/stage04/ACCEPTANCE.md)中的 `C04-<OO/MM/OM/MO>-<N/D>-<S01..S08/B05..B07>` 战斗条目。M/L生命周期、V原版采集仍按[人工结果模板](../../docs/stage04/RESULT-TEMPLATE.md)记录；原版数据作为版本明确的基准文件引用，不要求原版产生插件JSONL。

## 使用

需要Python 3.10及以上（本轮实际使用3.12.10），无第三方安装步骤。在工作树根执行：

```powershell
python -B mc/cross-era-pvp/combat/tools/check_evidence.py --manifest runtime/cross-era-stage04/<runId>/manifest.json --output runtime/cross-era-stage04/<runId>/report.json
```

先在获准的私有runtime运行目录准备清单和证据副本。报告父目录须已存在、目标须尚不存在且位于清单所在目录内；已有输出和任何原始文件均不能覆盖。省略 `--output` 时仅向终端输出JSON，简短中文状态写stderr并带`[草人杯]`前缀。

[manifest.example.json](manifest.example.json)是空白起点：NOT_RUN、PENDING、版本与哈希为null，没有造任何实测数值。复制后按实际证据填写；直接检查空白模板会报告缺失/格式问题，不应返回完整。实际UUID、控制台日志、录像及配置只放私有目录，不提交源码。

| 退出码 / integrityStatus | 含义 |
|---|---|
| 0 / COMPLETE | **本批所列条目的证据结构完整**，不是88格都测完或战斗通过 |
| 1 / INCOMPLETE | 未执行、缺基准/判据/收尾、来源不适用、哈希/关联/输入条件有缺口 |
| 2 / FORMAT_ERROR | JSON、schema、字段类型、路径等格式错误；CLI参数/输出保护也返回2 |

所有报告固定 `gameplayVerdict=NOT_EVALUATED`、`userAcceptance=PENDING`、`captureAuthenticity=NOT_VERIFIED`。合成夹具可结构完整，但固定标为 `NOT_ELIGIBLE_SYNTHETIC`；真实采集声明仍需人工审阅。报告不复制原始玩家快照或事件正文。

## 清单schemaVersion=1

此schema仅用于离线清单，未修改既有插件日志schemaVersion=1。

| 字段 | 类型 / 填写方式 |
|---|---|
| `runId` | 非空批次标识，不用“待填写”充当实测值 |
| `purpose` | `REAL_CAPTURE`或`SYNTHETIC_FIXTURE`；后者永远不能计入真人证据 |
| `environment` | `serverVersion`、`clientVersion`、`javaVersion`完整文本；服务器与Java版本须和各startup原文一致；`candidateCommit`为40位Git提交；四项`*Sha256`为64位哈希；未提交差异和完整配置文件集合另写context证据 |
| `files` | 显式证据文件列表，每项有`id`、`kind`、相对`path`、`sha256`；ID与实际路径均不能重复 |
| `sessions` | 每个日志startup会话对应一项，格式见下文；包括日志中的未选样本会话，不能漏掉失败会话 |
| `cases` | 非空所选用例列表；同一caseId只列一次，多次采样放在samples内；未列用例不被认为已覆盖 |

`files.kind`取 `journal`（本插件JSONL）、`console`（最终收尾证据）、`input`（点击/输入时间线或录像）、`context`（装备/场地/版本/测量条件）、`observation`（原始HP/耐久/轨迹观测）、`baseline`（原版基准）、`criteria`（已确认精度/容差/比较方法）。

baseline还须有 `version: "1.8.9"`或`"26.3"`、`provenance`来源。来源枚举为 `REAL_PLAYER`、`COMMAND`、`SYNTHETIC_EVENT`、`UNIT_STUB`；REAL_CAPTURE只接受REAL_PLAYER作为战斗及基准来源。OO引用1.8.9，MM引用26.3，OM/MO都须引用两版本基准。文件哈希和“已确认”声明只证明清单一致性，不验证原版身份、审批真实性、基准公式或容差是否合理。

environment中的构建/配置哈希只检查格式；实际文件哈希通过files对所提供副本核对。工具不从服务器取得文件，也不自动证明环境声明与生产文件对应，完整构建/配置来源仍须在context证据中审阅。

路径只接受清单目录内的相对普通文件；拒绝盘符/绝对路径、`..`、解析后越界的链接、Windows备用数据流/设备名。CLI清单/输出拒绝UNC网络及设备路径。清单上限2MiB，单个journal/console上限64MiB、单行2MiB；超限明确报错，不截断后继续判完整。其他证据流式算哈希，不自动读取或解析其正文。使用已停止写入的本地副本；检查前后重新算输入哈希，变化就报告不可采信。

## 会话与最终排空

每个JSONL文件内按startup出现顺序从1编号；唯一事件键为 **journal文件ID + session序号 + eventId**。重启后eventId重复合法，同会话伤害/后续采样重复、缺失、孤立或tick窗口不一致均有诊断。

以下只是结构示意，不是执行结果：

```json
{
  "journal": "events",
  "session": 1,
  "drain": {"file": "console", "line": 120}
}
```

`drain.line`为显式console文件的1基行号，必须包含本插件原文 ` [草人杯] 日志收尾完成：{...}`。工具读取accepted、rejected、errors、queued、writtenToBuffer、workerAlive，核对最终排空及会话记录总数；同一行不能为两个会话重复背书。未知格式不会猜测转换。

JSONL里的 `shutdown.journalBeforeDrain` 是关闭前快照，不能代替最终控制台结果。最终行与会话的对应关系由采集者声明，仍须人工核对时间和上下文。少shutdown、缺最后排空、拒绝记录、I/O错误、队列/worker未结束均不能判完整；也不因此自动判战斗错误。

## 用例与观察关联

RECORDED用例填写下列字段；NOT_RUN只需合法caseId、execution、PENDING和空samples，仍会得到未执行诊断。

| 字段 | 规则 |
|---|---|
| `caseId` / `execution` | 上述C04编号；`RECORDED`或`NOT_RUN` |
| `inputSource` | 来源枚举；声明REAL_PLAYER仍不是真人真实性证明 |
| `observedVerdict` | 可选，默认PENDING；人工填写PENDING/PASS/FAIL/INCONCLUSIVE，工具仅校验其字段，不采纳为自身结论 |
| `inputRefs` / `contextRefs` / `observationRefs` | 对应kind的非空文件ID数组，分别说明真实操作、条件、原始测量 |
| `baselineRefs` / `criteriaRefs` | 相应文件ID数组；OM/MO的criteria须说明已确认的攻防组合方法，工具不生成该方法 |
| `criteriaConfirmed` | 必须显式true才能结构完整；false或缺失诊断为待确认，不填默认容差 |
| `samples` | 数组，每项含journal、session、eventId、isolation；同用例不重复引用同一事件 |
| `noEventReason` | samples为空时必须说明无事件观察，仍须输入/观察引用；不是期望无伤害的断言 |

样本结构示意：

```json
{"journal": "events", "session": 1, "eventId": 1, "isolation": "ISOLATED"}
```

isolation为 `ISOLATED`、`SEQUENCE`、`UNKNOWN`或`CONTAMINATED`。后两项不能判完整；SEQUENCE只用于S02/S08/B05/B06/B07的连续输入记录，不把净HP分摊成逐击伤害。ISOLATED若在相同受击者的观察窗口发现额外伤害，会提示冲突；没有发现额外damage也不能排除治疗、复活或其他来源，必须人工核对。

工具检查事件字段、攻防身份、同世界两名玩家、无附魔钻石剑/耐久、N/D护甲、无药水/吸收生命的快照条件。起始20HP、实际冷却充满、输入方法、时序/击退精度和原版公式仍由原始观测及人工审阅判断，不从MONITOR字段猜测。

现有 `velocity_event` 没有eventId，工具不强制关联单次命中；`attackCooldownObserved`不保证攻击前值，暴击字段是条件观察；`netHealthDelta`不是逐击扣血。连接/世界变化、死亡窗口等会明确诊断。未观察到事件须保留独立输入和测量证据，不补造eventId。

04-005候选新增[生命周期保护能力](FOLLOWUP-PROTOCOL.md)。startup明确声明 `connection-world-life-v1` 后，工具核对观察代号、失效原因与后续测量抑制。历史日志没有声明时保留旧解析并提示能力缺失，不伪造历史保护效果。

## 自动验证

在本工作树runtime内选择**新的**测试目录，生成的假UUID、文件和报告均标记为合成夹具，保留供复核，不删除其他文件：

```powershell
python -B mc/cross-era-pvp/combat/tools/test_check_evidence.py --temp-root runtime/cross-era-stage04/<本次任务>/tests -v
```

已覆盖正常夹具、缺基准/判据、NOT_RUN伪装、跨会话ID、孤立/重复/缺失采样、日志损坏/数值类型、最终排空、输入变化、版本/装备/身份不符、连续窗口污染、路径和CLI覆盖保护，以及新增生命周期能力。04-002历史40项为39通过/1跳过；04-005扩展到49项，48通过/1跳过。创建真实符号链接的1项因宿主权限不足跳过，不算通过；实际分支待具备条件的环境验证，不调整系统权限绕行。

实际命令、初始失败与修正记录见交付报告（历史工作记录，未随包分发）。该工具完成不等于阶段04、OCM独立替换或真人验收完成。

新增协议及Java到Python实际交叉读取的时间/证据见04-005交付（历史工作记录，未随包分发）。

04-007交付核对再增加旧编码环境与路径预检回归，当前为53项中52通过/1跳过。CLI stdout/stderr明确使用UTF-8；普通本地路径在解析前逐组件检查，符号链接和Windows重解析点直接拒绝，包括清单、输入及输出路径。元数据替身测试不替代尚未执行的真实链接实验。当前结果见04-007交付（历史工作记录，未随包分发）。
