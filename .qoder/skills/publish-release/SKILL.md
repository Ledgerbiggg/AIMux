---
name: publish-release
description: 执行 AIMux 版本发布工作流：升级版本号、收集上次发版以来的全部提交、生成中文更新清单写入 version.json、提交并推送，push 后 GitHub 工作流自动生成 Release 说明。使用场景：用户说"发版/发布新版本/发布 release/publish release/bump version/升级版本"时。
---

# 发布新版本

当前工作目录是 AIMux 仓库根。按顺序完成下列完整工作流，完成一次版本发布。

## 参数
- 用户可指定版本增量：`patch` / `minor` / `major`，默认 `patch`（末位 +1，满 10 进位）。

## 执行步骤

### 1. 读取当前版本
读取 `AiMux.Shell/AiMux.Shell.csproj` 的 `<Version>` 值作为旧版本号。文件不存在或提取失败则停止并报告错误。

### 2. 升级版本号
优先调用 `scripts/bump_version.ps1`：
```
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bump_version.ps1 -Part <part>
```
脚本不存在时直接改写 csproj 的 `<Version>`（minor: 中间位+1 末尾归零；patch: 末位+1；major: 首位+1 其余归零）。
完成后重新读取 csproj，确认得到新版本号 `NEW_VER` 且 `NEW_VER != 旧版本号`。

### 3. 收集「上次发版提交 → 当前 HEAD」的全部提交（关键）
- 定位上一次发版提交：
  ```
  git log --pretty=format:%H --grep="^release: bump to " -1
  ```
- 找到则区间为 `LAST..HEAD`；无发版记录则区间为全部历史（`HEAD`）。
- 取该区间**所有**提交消息（每行一条）：
  ```
  git log <range> --pretty=format:%s
  ```
- ⚠️ 必须取区间内全部提交，绝不能只取最新一条，这是生成完整更新清单的前提。

### 4. 生成中文更新清单并写入 version.json
- 按类型前缀分类：feat→✨新功能、fix→🐛问题修复、ui→🎨界面优化、perf→🚀性能优化、refactor→♻️代码重构、ci→🔧构建/流水线、doc→📝文档、test→✅测试、chore→🧹杂项；无前缀归「其他」。
- 生成如下格式的多行文本：
  ```
  ## v<NEW_VER> 更新说明
  > 生成时间：YYYY-MM-DD HH:mm

  ### ✨ 新功能
  - <条目>

  ### 🐛 问题修复
  - <条目>

  （其余分类按需）
  共 N 条提交。
  ```
- 写入仓库根 `version.json` 的 `notes` 字段（保留原有 `version`/`productName`/`releaseDate`，仅更新 `notes`），缩进 JSON 写出。

### 5. 版本提交
⚠️ **铁规：commit 消息一律英文（纯 ASCII），绝不使用中文，避免乱码风险。**
```
git add -A
git commit -m "release: bump to <NEW_VER>"
```
（此英文格式便于下次发版时 `--grep` 再次定位到本次提交。）

### 6. 先拉取再推送
```
git pull --ff-only
git push
```
若 pull 因分叉失败，向用户报告并说明可改用 `git pull --rebase`，由用户决定是否继续，不要擅自 force。

## 完成汇报
向用户简要汇报：旧版本 → 新版本、本次纳入的提交条数、生成的更新清单内容，并说明「push 后 GitHub 工作流会自动读取 version.json 的 notes 生成 Release 说明」。

不要在代码里新增任何功能或文件，本技能只做版本发布与文档整理。
