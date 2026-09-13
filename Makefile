
# AiMux - AI 聊天平台聚合桌面客户端
# .NET 9 + WPF + Prism.Unity + WPF-UI 4.0 + WebView2 + Hardcodet.NotifyIcon
#
# 常用:
#   make dev       - 开发：杀进程 + 构建(Debug) + 运行
#   make watch     - 热部署：启动后监听源码变化，自动重建并重启（按 Q 退出）
#   make release   - 发布（云端出包）：升版本 + 写 notes + 提交 + 推送；GitHub 自动打包发 Release
#                    用法: make release NOTES="本次更新内容"
#
# 版本号单一真相源：AiMux.Shell/AiMux.Shell.csproj 里的 <Version>。
# 发布时由 release.ps1 自动 +1，无需手工同步。

# 解决方案与主程序
SLN      := AiMux.sln
APP_NAME := AiMux.exe

# 输出目录（Debug 配置）
OUT_DIR  := AiMux.Shell\bin\Debug\net9.0-windows
APP_PATH := $(OUT_DIR)\$(APP_NAME)

# 默认配置：Debug
CONFIG   ?= Debug

# 发布时默认升版本号的方式：patch（0.6.0 -> 0.6.1 ... 0.6.9 -> 0.7.0 进位）。可覆盖：make release BUMP_PART=minor
BUMP_PART ?= patch

# 默认目标：热部署（直接 make 即进入监听重建模式，修改代码自动重启）
.DEFAULT_GOAL := watch

# 杀掉残留的 AiMux 进程（避免锁文件导致构建失败 / 单实例 Mutex 抢占）
.PHONY: kill
kill:
	@echo "[kill] 清理残留 AiMux 进程..."
	@taskkill /F /IM $(APP_NAME) 2>nul || echo "(无运行实例)"
	@echo "[kill] 完成"

# 构建解决方案（Debug）
.PHONY: build
build: kill
	@echo "[build] 构建 $(SLN) (Config=$(CONFIG))..."
	dotnet build $(SLN) -c $(CONFIG) --nologo
	@echo "[build] 完成"

# 启动主程序（需先 build）
.PHONY: run
run:
	@echo "[run] 启动 $(APP_PATH)..."
	@if exist "$(APP_PATH)" ( \
		start "" "$(APP_PATH)"; \
	) else ( \
		echo "[run] 未找到 $(APP_PATH)，请先执行: make build"; \
	)

# 一键启动：杀进程 → 构建 → 运行
.PHONY: dev
dev: build run
	@echo "[dev] 已启动 AiMux"

# 热部署：启动后监听源码变化，自动重新构建并重启应用（按 Q 退出）
.PHONY: watch
watch:
	powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev_watch.ps1

# 发布（云端出包）：升版本号 + 写 version.json notes + 提交 + 推送。
# 真正的打包与发 Release 由 GitHub 工作流(build.yml)完成。
# 用法: make release NOTES="本次更新内容"  （可选 BUMP_PART=major|minor|patch，默认 patch）
.PHONY: release
release:
	powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Notes "$(NOTES)" -Part $(BUMP_PART)

# 查看可用目标
.PHONY: help
help:
	@echo "AiMux - AI 聊天平台聚合桌面客户端"
	@echo.
	@echo "默认目标: make = make watch（热部署，Q 退出）"
	@echo.
	@echo "可用目标:"
	@echo "  watch      - 热部署：启动后监听源码，修改自动重建重启（按 Q 退出）"
	@echo "  dev        - 一键启动：杀进程 + 构建 + 运行"
	@echo "  build      - 构建解决方案（Debug）"
	@echo "  run        - 启动主程序（需先 build）"
	@echo "  kill       - 杀掉残留 AiMux 进程（修复构建权限问题）"
	@echo "  release    - 发布：升版本 + 写 notes + 提交 + 推送；GitHub 自动出包"
	@echo "  help       - show this help"
	@echo.
	@echo "Tip: MSB3021 (access denied) -> 'make kill' then 'make build'"
	@echo "Tip: no window after launch -> stale Mutex from prior instance; 'make kill' then retry"
