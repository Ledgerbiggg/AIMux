---
name: push-changes
description: 同步推送 AIMux 本地改动到远端：先拉取再推送本地提交，可附带英文约定式提交说明。只做同步，不升版本、不改更新说明、不触发发版工作流。使用场景：用户说"推送/同步代码/push/push changes/提交并推送/同步到远端"时。
---

# 推送本地改动

当前工作目录是 AIMux 仓库根。本技能只负责把本地改动拉取并推送到远端，不升级版本号、不触发 GitHub 发版工作流。

## 参数
- 用户提供的提交说明文本（可选）。
- 有说明：先按约定式提交格式把改动 commit，再做 pull + push。
- 无说明：若工作区还有未提交改动，先提示用户是否补一条提交说明；否则直接 pull + push 已有提交。

## 提交消息规范（提供了说明时）
⚠️ **铁规：commit 消息一律英文（纯 ASCII），绝不使用中文，避免乱码风险。**
采用「约定式提交」英文格式，便于后续 publish-release 自动分类：
```
<type>(<scope>): <english description>
```
- type（英文）：feat / fix / ui / perf / refactor / ci / doc / test / chore
- 描述用英文，简洁。例：`feat: add sidebar drag-to-reorder`

## 执行步骤
1. 查看状态：
   ```
   git status
   git diff --stat
   ```
2. 若提供了说明且有改动：整理为英文约定式提交消息，`git add -A`，`git commit -m "<english commit message>"`。
3. 若没有任何改动也没有未推送提交：停止并提示「没有需要同步的改动」。
4. **先拉取**：
   ```
   git pull --ff-only
   ```
   若 pull 因分叉失败，向用户报告并说明可改用 `git pull --rebase`，由用户决定是否继续，不要擅自 force。
5. **再推送**：
   ```
   git push
   ```
6. ⚠️ 只做 pull + push 同步：不要修改 `version.json` 的 version、不要改 csproj 版本号、不要写 release notes。普通推送不会触发发版，符合预期。

## 完成汇报
向用户汇报：是否新建了提交（及消息）、pull 结果、push 结果（推送的提交数），并提示「这是常规同步推送，未升版本、未触发发版；需要发版时使用 publish-release」。
