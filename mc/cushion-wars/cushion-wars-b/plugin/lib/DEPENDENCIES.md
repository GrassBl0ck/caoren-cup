# 编译依赖清单

这些 jar **只用于编译**，不打进业务 JAR。来源为 A 插件 `mc/cushion-wars/cushion-wars-a/plugin/lib/` 的现有本地固定版本，
本轮未联网升级、未引入新的主要依赖。

- 采集日期沿用 A：2026-10-25
- Paper API 版本：`26.3.build.223-stable`
- 编译 JDK：Microsoft OpenJDK 25.0.3；`javac` 产出字节码 major 69（Java 25）
- `paper-api-26.3.build.223-stable.jar` SHA-256：`fa309e1f97827079ab40df93d4e5c6dd570137d13623b4655b0693eb0c27f226`

| 文件 | 版本 |
|---|---|
| `paper-api-26.3.build.223-stable.jar` | 26.3 build 223 stable |
| `brigadier-1.3.11.jar` | 1.3.11 |
| `bungeecord-chat-1.21-R0.2-deprecated+build.21.jar` | 1.21-R0.2 |
| `guava-33.6.0-jre.jar` | 33.6.0-jre |
| `failureaccess-1.0.2.jar` | 1.0.2 |
| `gson-2.14.0.jar` | 2.14.0 |
| `snakeyaml-2.2.jar` | 2.2 |
| `joml-1.10.9.jar` | 1.10.9 |
| `fastutil-8.5.18.jar` | 8.5.18 |
| `log4j-api-2.26.0.jar` | 2.26.0 |
| `slf4j-api-2.0.17.jar` | 2.0.17 |
| `jspecify-1.0.0.jar` | 1.0.0 |
| `checker-qual-4.2.3.jar` | 4.2.3 |
| `adventure-api-5.2.0.jar` | 5.2.0 |
| `adventure-key-5.2.0.jar` | 5.2.0 |
| `adventure-text-minimessage-5.2.0.jar` | 5.2.0 |
| `adventure-text-serializer-gson-5.2.0.jar` | 5.2.0 |
| `adventure-text-serializer-legacy-5.2.0.jar` | 5.2.0 |
| `adventure-text-serializer-plain-5.2.0.jar` | 5.2.0 |
| `adventure-text-logger-slf4j-5.2.0.jar` | 5.2.0 |
| `examination-api-1.3.0.jar` | 1.3.0 |

`plugin/lib/*.jar` 被 `mc/.gitignore` 与本地 `.gitignore` 忽略，不进入版本库。

## 为什么用 javac 而不是 Maven/Gradle

本轮使用现有本地固定依赖做离线构建，不启用Maven/Gradle依赖解析。依赖固定在 `plugin/lib/`，
`tools/build_plugin.py` 调用 `javac` 与 `jar`，全过程不联网解析。

## 2026-11-01 补充编译注解

扩展API事件回归时javac报告缺少org.jetbrains.annotations.NotNull。已从既有本地工作区缓存只读
复制 `org.jetbrains:annotations:26.1.0` 到B的忽略lib目录。未联网升级Paper或其他依赖，
未引入测试框架；此注解依赖不进入业务JAR，保留上游许可。

- 文件：`annotations-26.1.0.jar`
- SHA-256：`ebc7aec252ed0c7d2d04c039d7f00e69f7b86b1f493c741d67b3ef31b986b054`
- 来源：`D:/OpenSourcework/04-artifacts/cross-era-pvp-stage03-20261030/.cache/gradle/caches/modules-2/files-2.1/org.jetbrains/annotations/26.1.0/c1e9226d7ca6661020c58c94397da0cc3673d5e4/annotations-26.1.0.jar`

总编译依赖现为22个JAR，原21个固定版本不变。
