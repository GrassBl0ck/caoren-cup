# 第一次开源到 GitHub：从这里开始

这份压缩包已经整理成适合开源的项目结构。你不需要再上传原来的两个 zip，直接使用本文件所在的 `caoren-cup-open-source` 文件夹即可。

## 1. 解压到哪里

建议放到一个专门的工作目录，例如：

### Windows 推荐路径

```text
D:\OpenSourceWork\caoren-cup-open-source\
```

### macOS / Linux 推荐路径

```text
~/OpenSourceWork/caoren-cup-open-source/
```

不要放在微信、QQ、网盘同步目录里，也不要放在原始 zip 解压目录里，避免误传旧文件或备份文件。

## 2. 解压后你应该看到什么

```text
caoren-cup-open-source/
├─ README.md
├─ START_HERE.md
├─ LICENSE
├─ SECURITY.md
├─ .gitignore
├─ docs/
├─ game-plugin/
└─ web-command-center/
```

如果你看到了下面这些，说明你可能解压错了，或者混入了旧文件：

```text
.vs/
node_modules/
bin/
obj/
*.dll
*.pdb
*.zip
caoren_config.json
ecosystem.config.cjs
backup_*/
```

这些不应该出现在准备开源的仓库里。

## 3. 安装 Git

Windows 用户先安装 Git for Windows。安装完成后，在项目文件夹空白处右键，应该可以看到：

```text
Open Git Bash here
```

打开后输入：

```bash
git --version
```

能显示版本号就说明安装成功。

## 4. 配置 Git 用户名和邮箱

只需要设置一次：

```bash
git config --global user.name "你的名字"
git config --global user.email "你的邮箱"
```

示例：

```bash
git config --global user.name "CaorenCup"
git config --global user.email "your-email@example.com"
```

## 5. 本地先检查一遍

进入项目根目录：

```bash
cd /你的路径/caoren-cup-open-source
```

检查是否还有不该上传的文件：

```bash
find . -name "node_modules" -o -name "bin" -o -name "obj" -o -name ".vs"
find . \( -name "*.dll" -o -name "*.pdb" -o -name "*.zip" -o -name "*.out" \)
```

如果没有输出，说明比较干净。

再检查敏感词：

```bash
grep -RniE "admin123|dev-plugin-token|password|secret|真实|835750" .
```

如果只看到 README、示例配置、代码变量名，一般没问题；如果看到真实密码或真实 token，请先删除或替换。

## 6. 初始化 Git 仓库

在项目根目录执行：

```bash
git init
git status
```

然后把文件加入版本管理：

```bash
git add .
git commit -m "Initial open source release"
```

## 7. 在 GitHub 创建空仓库

1. 登录 GitHub。
2. 右上角点击 `+`。
3. 选择 `New repository`。
4. 仓库名建议填写：

```text
caoren-cup
```

5. 可以先选 `Private`，确认无敏感信息后再改成 `Public`。
6. 不要勾选 `Add a README file`、`.gitignore`、`License`，因为本地已经有了。
7. 点击 `Create repository`。

## 8. 关联远程仓库并上传

GitHub 创建仓库后，会给你一个地址，类似：

```text
https://github.com/你的用户名/caoren-cup.git
```

在本地执行：

```bash
git branch -M main
git remote add origin https://github.com/你的用户名/caoren-cup.git
git push -u origin main
```

注意把上面的地址换成你自己的仓库地址。

## 9. 上传后检查

打开 GitHub 仓库页面，确认没有这些内容：

```text
node_modules/
bin/
obj/
.vs/
*.dll
*.pdb
*.zip
caoren_config.json
ecosystem.config.cjs
```

然后在 GitHub 页面搜索：

```text
admin123
```

```text
dev-plugin-token
```

```text
password
```

```text
token
```

确认没有真实密码和真实 token 后，再把仓库改成 Public。

## 10. 以后怎么更新

每次改完代码后，在项目根目录执行：

```bash
git status
git add .
git commit -m "说明这次修改了什么"
git push
```

常用命令只有这几个：

```bash
git status
git add .
git commit -m "message"
git push
```
