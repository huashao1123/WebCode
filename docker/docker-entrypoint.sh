#!/bin/bash
# ============================================
# WebCodeCli Docker 启动脚本
# 动态生成 Codex 配置文件
# ============================================

set -e

echo "============================================"
echo "WebCodeCli Docker Container Starting..."
echo "============================================"

# ============================================
# 生成 Codex 配置文件
# ============================================
echo "Generating Codex configuration..."

CODEX_CONFIG_DIR="$HOME/.codex"
CODEX_CONFIG_FILE="${CODEX_CONFIG_DIR}/config.toml"

# 确保配置目录存在
mkdir -p "${CODEX_CONFIG_DIR}"

# 使用环境变量生成配置文件
cat > "${CODEX_CONFIG_FILE}" << EOF
# Codex CLI 配置文件（由 Docker 启动脚本自动生成）
# 生成时间: $(date)

model = "${CODEX_MODEL:-glm-4.7}"
model_reasoning_effort = "${CODEX_MODEL_REASONING_EFFORT:-medium}"

profile = "${CODEX_PROFILE:-webcode}"
windows_wsl_setup_acknowledged = true

[model_providers.${CODEX_PROFILE:-webcode}]
name = "${CODEX_PROVIDER_NAME:-webcode codex}"
base_url = "${CODEX_BASE_URL:-https://api.antsk.cn/v1}"
env_key = "NEW_API_KEY"
wire_api = "${CODEX_WIRE_API:-chat}"


[profiles.${CODEX_PROFILE:-webcode}]
# 深度模型
model = "${CODEX_MODEL:-glm-4.7}"
# provider id
model_provider = "${CODEX_PROFILE:-webcode}"
# 审批策略
approval_policy = "${CODEX_APPROVAL_POLICY:-never}"
# 推理强度
model_reasoning_effort = "${CODEX_MODEL_REASONING_EFFORT:-medium}"
# 推理总结粒度
model_reasoning_summary = "detailed"
# 是否强制开启推理总结
model_supports_reasoning_summaries = true
model_reasoning_summary_format = "experimental"
sandbox_mode = "${CODEX_SANDBOX_MODE:-danger-full-access}"
EOF

echo "Codex configuration generated at: ${CODEX_CONFIG_FILE}"
cat "${CODEX_CONFIG_FILE}"
echo ""

# ============================================
# 验证环境变量
# ============================================
echo "Validating environment variables..."

# Claude Code 配置检查
if [ -z "${ANTHROPIC_AUTH_TOKEN}" ]; then
    echo "WARNING: ANTHROPIC_AUTH_TOKEN is not set. Claude Code may not work properly."
fi

if [ -n "${ANTHROPIC_BASE_URL}" ]; then
    echo "Claude Code Base URL: ${ANTHROPIC_BASE_URL}"
fi

if [ -n "${ANTHROPIC_MODEL}" ]; then
    echo "Claude Code Model: ${ANTHROPIC_MODEL}"
fi

# Codex 配置检查
if [ -z "${NEW_API_KEY}" ]; then
    echo "WARNING: NEW_API_KEY is not set. Codex may not work properly."
fi

echo ""

# ============================================
# 验证 CLI 工具
# ============================================
echo "Verifying CLI tools..."

echo -n "Claude CLI: "
claude --version 2>/dev/null || echo "Not available or version check failed"

echo -n "Codex CLI: "
codex --version 2>/dev/null || echo "Not available or version check failed"

echo -n "Node.js: "
node --version

echo -n "Python: "
python3 --version

echo -n "Git: "
git --version

echo ""

# ============================================
# 创建必要的目录
# ============================================
echo "Creating required directories..."
mkdir -p /app/data
mkdir -p /app/workspaces
mkdir -p /app/logs

# 修复目录权限（entrypoint 以 root 运行，可以修改挂载卷权限）
echo "Fixing directory permissions for mounted volumes..."
chown -R appuser:appuser /app/data /app/workspaces /app/logs /app 2>/dev/null || echo "Note: Some directories could not be chown'd"

# 修复 home 目录权限
if [ -d "/home/appuser" ]; then
    chown -R appuser:appuser /home/appuser 2>/dev/null || true
fi

# ============================================
# 配置 Claude Code Skills
# ============================================
echo "Configuring Claude Code Skills..."
CLAUDE_SKILLS_DIR="/home/appuser/.claude/skills"
if [ -d "/app/skills/claude" ]; then
    echo "Copying Claude Code skills to $CLAUDE_SKILLS_DIR..."
    mkdir -p "$CLAUDE_SKILLS_DIR"
    cp -r /app/skills/claude/* "$CLAUDE_SKILLS_DIR/"
    echo "Claude Code skills installed:"
    ls "$CLAUDE_SKILLS_DIR" || echo "No skills found"
fi

# ============================================
# 配置 Codex Skills
# ============================================
echo "Configuring Codex Skills..."
CODEX_SKILLS_DIR="/home/appuser/.codex/skills"
if [ -d "/app/skills/codex" ]; then
    echo "Copying Codex skills to $CODEX_SKILLS_DIR..."
    mkdir -p "$CODEX_SKILLS_DIR"
    cp -r /app/skills/codex/* "$CODEX_SKILLS_DIR/"
    echo "Codex skills installed:"
    ls "$CODEX_SKILLS_DIR" || echo "No skills found"
fi

echo ""

# ============================================
# 启动应用（切换到 appuser 用户运行）
# ============================================
echo "Starting WebCodeCli application as appuser..."
echo "============================================"

# 使用 gosu 切换到 appuser 用户执行命令
exec gosu appuser "$@"
