# [草人杯] OCM选择只读迁移清单

`inventory_choices.py` 用于R02，只核对明确提供的副本；**不会执行迁移**。支持已审阅OCM 2.8.0的 `players.bson` 文档/字符串结构。Python 3.12、仅标准库；复用同目录证据工具的严格JSON、哈希与本地路径检查。自研部分MIT，第三方许可不变。

## 使用

从工作树根目录执行。先将示例复制到本任务私有runtime子目录，将已获准取得的离线副本放在同目录，按实际资料填全字段；文件不存在、哈希/时间未填时会返回错误，不能将模板当真实数据。

```powershell
python -B mc/cross-era-pvp/combat/tools/inventory_choices.py --manifest runtime/cross-era-stage04/<本轮目录>/manifest.json --output runtime/cross-era-stage04/<本轮目录>/inventory.json
python -B mc/cross-era-pvp/combat/tools/test_inventory_choices.py --temp-root runtime/cross-era-stage04/<本轮目录>/tests -v
```

输出必须是manifest目录内尚不存在的文件；目录须已存在。不覆盖、不创建父目录、不扫描服务器、不联网、不解密、不读取账号。报告包含玩家UUID和世界选择，应留在私有runtime，不提交或发到公开渠道。控制台只输出状态和数量。

## manifest字段

示例：[migration-manifest.example.json](migration-manifest.example.json)。所有字段必须明确，未知字段报错，避免拼错后被忽略。

| 字段 | 要求与作用 |
|---|---|
| schemaVersion | 整数1；布尔值/其他版本拒绝 |
| purpose | `REAL_COPY` 或 `SYNTHETIC_FIXTURE`；自报来源不是来源真实性证明 |
| source.path / sha256 | 同目录或其子目录相对路径、文件实际SHA-256；禁止越界、盘符、UNC、设备名、备用数据流；读取后及收尾均核对 |
| source.ocmVersion | `2.8.0`；其他版本需要先审阅对应源格式 |
| source.ocmJarSha256 / configSha256 | 原副本所对应构建/OCM配置的哈希声明；目前只查格式，不替代人工核对原文件与来源 |
| source.snapshotState | 合成为`SYNTHETIC`；真实副本为`CLOSED_COPY`、`LIVE_COPY`或`UNKNOWN`。后两项需要审阅，因可能遗漏未刷盘选择；`CLOSED_COPY`也只是声明 |
| source.capturedAt | 带时区的ISO时间；示例留空，不能猜采集时间 |
| profiles | 必须明确old和modern对应的不同原始预设名；大小写精确比较，不能从名字含old就猜规则；示例仅对应仓库配置 |
| worlds | `{uuid,name}`列表；UUID/名称重复拒绝。未列出的世界保留原UUID并标待审；名称/UUID真实性仍需采集证据 |

## 结果与退出码

| 退出码 / inventoryStatus | 含义 |
|---|---|
| 0 / COMPLETE | **仅本份声明快照**完整解析，无未解决的格式/清单问题；不代表可以执行迁移 |
| 1 / REVIEW_REQUIRED | 未知预设、未映射世界、UUID冲突、未知玩家字段或非闭合快照等需处理 |
| 2 / FORMAT_ERROR | JSON/BSON损坏或不支持、字段类型、哈希、路径、读取/输出错误等；禁止据此迁移 |

报告始终 `migrationExecuted=false`、`migrationReady=false`、`userAcceptance=PENDING`、`captureAuthenticity=NOT_VERIFIED`。合成记录为 `NOT_ELIGIBLE_SYNTHETIC`，真实副本仍 `MANUAL_REVIEW_REQUIRED`。报告不提供自动导入文件。

`players`保留原玩家键与规范UUID、保存条目数量和未知字段名；`choices`保留原玩家/世界键、规范UUID、原预设和识别结果。UUID只规范大小写；冲突全部列出，不合并；无保存值不补成modern。未知字段的值留在原文件中，报告指向原文件哈希，不丢弃原文件进行转换。字段损坏可导致部分清单，FORMAT_ERROR时不得使用其中条目迁移。

文件上限64MiB，清单2MiB；BSON最大8层、20万元素、单字符串128KiB；只支持文档与字符串，其他合法BSON类型也会明确拒绝，不静默跳过。诊断最多列200条并统计省略数，状态仍反映全部检查，条目不会因诊断上限静默丢失。

## 依据与实际验证

数据语义及迁移限制见[专题文档](../../docs/stage04/MIGRATION-INVENTORY.md)，实际时间和失败/通过记录见04-003交付（历史工作记录，未随包分发）。38项测试使用合成数据，另用固定OCM JAR中的 `PlayerDataCodec` 和官方BSON `DocumentCodec` 生成副本进行交叉读取；没有启动服务器或加载玩家账号。

BSON字节格式依据[官方BSON 1.1规范](https://bsonspec.org/spec.html)独立实现；不是通用BSON库。真实符号链接创建受宿主限制，本工具用解析后路径的模拟测试检查越界拒绝，不将其称为真实链接实验。
