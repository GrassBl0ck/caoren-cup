# 第三方边界

- 自研源代码采用本目录 MIT LICENSE。
- Gradle Wrapper 9.8.2 从本仓库阶段03原样复制，使用 Apache-2.0；保留脚本头，分发文件的 LICENSE/NOTICE 在 `third-party/gradle/`。Wrapper 下载的分发包固定 SHA-256，见 properties。
- Paper API 固定 `26.3.build.223-stable`，Gson 固定 `2.14.0`；仅编译依赖，运行时由 Paper 提供。
- 官方 OCM v2.8.0 是独立运行依赖和 compileOnly 输入，不复制 API 类或打入本插件。其源码及 MPL-2.0 在 `../../third-party/old-combat-mechanics/`。该项目说明含 PacketEvents 的组合二进制按 GPLv3 分发；本工程不将 OCM JAR 纳入源码或自己的 JAR。
- 本地 `libs/OldCombatMechanics.jar` 被忽略；可由固定源码构建，或将已核验的 v2.8.0 JAR 通过 `-PocmJar=绝对路径` 提供。构建会检查版本和公开 API 类。
- JUnit Jupiter 5.13.4 / JUnit Platform 仅用于测试，不进入插件 JAR。
- `examples/ocm-config.yml` 是为本实验手写的配置，不是对上游默认文件的覆盖或再许可。

只构建本插件不代表已通过 OCM 二进制重新分发、其他服务端版本或原版还原验收。
