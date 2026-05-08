# Open Source Cleanup Report

本项目由两个原始压缩包整理而来：

- `CaorenCup (2).zip`
- `CaorenCupPluginweb.zip`

## 已保留

### `game-plugin/`

保留 CS2 娱乐玩法插件源码：

- `CaorenCupPlugin.cs`
- `CaorenCupConfig.cs`
- `CaorenCupUtils.cs`
- `ICaorenFeature.cs`
- `Features/`
- `CaorenCup.csproj`
- `CaorenCup.sln`

并将 `CaorenCup.csproj` 中的本地 DLL 路径引用改为 NuGet 引用：

```xml
<PackageReference Include="CounterStrikeSharp.API" Version="1.0.367" />
```

### `web-command-center/`

保留网页指挥台源码：

- `package.json`
- `package-lock.json`
- `tsconfig.json`
- `src/`
- `public/`
- `ecosystem.config.cjs.example`

### `web-command-center/CaorenCupPlugin/`

保留网页桥接插件源码：

- `CaorenCupPlugin.cs`
- `CaorenCupPlugin.csproj`
- `caoren_config.example.json`

## 已删除 / 未纳入

以下内容不适合开源，已删除或未纳入整理包：

- `.vs/`
- `node_modules/`
- `bin/`
- `obj/`
- `*.dll`
- `*.pdb`
- `*.zip`
- `*.out`
- `*.bak*`
- `*.broken*`
- 历史 backup 目录
- 重复版本目录
- 真实配置文件 `caoren_config.json`
- 真实部署配置 `ecosystem.config.cjs`

## 已新增

- `.gitignore`
- `README.md`
- `START_HERE.md`
- `LICENSE`
- `SECURITY.md`
- `docs/deployment.md`
- `docs/gameplay.md`
- `docs/plugin-web-bridge.md`

## 注意

我没有在当前环境里联网执行 `npm install` 或 `dotnet restore`，因此没有实际完成依赖恢复和编译验证。上传 GitHub 前，建议在你的本地电脑执行：

```bash
cd web-command-center
npm install
npm run typecheck
```

以及：

```bash
cd game-plugin
dotnet restore
dotnet build -c Release
```

```bash
cd web-command-center/CaorenCupPlugin
dotnet restore
dotnet build -c Release
```
