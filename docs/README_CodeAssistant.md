# 编程助手使用说明

## 功能概述

编程助手是一个基于 .NET 10 和 Blazor Server 构建的应用，它通过调用外部 CLI 工具（如 ClaudeCode、GitHub Copilot CLI 等）来提供智能代码生成和编程建议。

## 核心特性

1. **左右分栏界面**
   - 左侧：聊天交互区，包含消息历史和输入框
   - 右侧：预览区，支持代码预览、Markdown 渲染和原始输出三种模式

2. **多 CLI 工具支持**
   - 支持多种编程助手 CLI 工具的无缝切换
   - 可在配置文件中灵活添加新的 CLI 工具

3. **实时流式输出**
   - 使用 SSE (Server-Sent Events) 实现流式响应
   - 打字机效果展示 CLI 工具的输出

4. **语法高亮**
   - 集成 Monaco Editor 提供代码编辑和高亮功能
   - 自动检测代码语言

5. **Markdown 渲染**
   - 使用 Markdig 库解析和渲染 Markdown 内容
   - 支持高级 Markdown 扩展

## 配置说明

### 添加 CLI 工具

在 `appsettings.json` 或 `appsettings.Development.json` 中配置 CLI 工具：

```json
{
  "CliTools": {
    "MaxConcurrentExecutions": 3,
    "DefaultTimeoutSeconds": 300,
    "EnableCommandWhitelist": true,
    "Tools": [
      {
        "Id": "your-tool-id",
        "Name": "工具显示名称",
        "Description": "工具描述",
        "Command": "命令路径或名称",
        "ArgumentTemplate": "参数模板，使用 {prompt} 作为占位符",
        "WorkingDirectory": "工作目录（可选）",
        "Enabled": true,
        "TimeoutSeconds": 300,
        "EnvironmentVariables": {
          "ENV_VAR": "value"
        }
      }
    ]
  }
}
```

### 配置项说明

- **Id**: 工具的唯一标识符
- **Name**: 在界面上显示的工具名称
- **Description**: 工具的简短描述
- **Command**: CLI 工具的可执行文件路径或命令名
- **ArgumentTemplate**: 参数模板，`{prompt}` 会被替换为用户输入
- **WorkingDirectory**: CLI 工具的工作目录（可选）
- **Enabled**: 是否启用该工具
- **TimeoutSeconds**: 执行超时时间（秒）
- **EnvironmentVariables**: 环境变量（可选）

## 支持的 CLI 工具示例

### 1. GitHub Copilot CLI

```json
{
  "Id": "copilot-cli",
  "Name": "GitHub Copilot CLI",
  "Description": "GitHub Copilot 命令行工具",
  "Command": "gh",
  "ArgumentTemplate": "copilot suggest {prompt}",
  "Enabled": true,
  "TimeoutSeconds": 300
}
```

安装: `gh extension install github/gh-copilot`

### 2. Claude Code (示例)

```json
{
  "Id": "claude-code",
  "Name": "Claude Code",
  "Description": "Claude AI 代码助手",
  "Command": "claude",
  "ArgumentTemplate": "code {prompt}",
  "Enabled": true,
  "TimeoutSeconds": 300
}
```

### 3. OpenAI Codex (示例)

```json
{
  "Id": "codex",
  "Name": "OpenAI Codex",
  "Description": "OpenAI Codex 代码生成",
  "Command": "codex",
  "ArgumentTemplate": "{prompt}",
  "Enabled": true,
  "TimeoutSeconds": 300
}
```

## 测试工具

项目包含一个测试用的回显工具，位于 `wwwroot/test-cli-tools/` 目录：

- `echo-assistant.ps1` - PowerShell 版本
- `echo-assistant.bat` - 批处理版本

在 `appsettings.Development.json` 中已经配置了测试工具，可直接使用。

## 安全性

### 命令注入防护

- 用户输入会自动转义，防止命令注入攻击
- Windows 和 Linux/Mac 使用不同的转义策略

### 超时控制

- 每个 CLI 工具都有超时设置
- 超时后会自动终止进程

### 并发限制

- 通过 `MaxConcurrentExecutions` 限制同时执行的 CLI 工具数量
- 防止资源耗尽

### 白名单验证

- 可通过 `EnableCommandWhitelist` 启用命令白名单验证
- 只允许配置文件中定义的命令执行

## 使用技巧

1. **快捷键**
   - `Ctrl + Enter` - 快速发送消息

2. **切换预览模式**
   - 代码预览：适合查看代码片段
   - Markdown 渲染：适合查看格式化的文档
   - 原始输出：查看未处理的原始内容

3. **清空对话**
   - 点击"清空对话"按钮可以重置当前会话
   - 不影响其他用户的会话

## 故障排除

### CLI 工具无法执行

1. 确认 CLI 工具已正确安装
2. 检查命令路径是否正确
3. 验证环境变量是否配置
4. 查看应用日志获取详细错误信息

### 输出显示异常

1. 检查 Monaco Editor 是否正确加载
2. 确认网络连接正常（CDN 资源）
3. 查看浏览器控制台的错误信息

### 超时错误

1. 增加 `TimeoutSeconds` 配置值
2. 检查 CLI 工具是否正常工作
3. 确认网络连接稳定

## 技术架构

- **前端**: Blazor Server 
- **后端**: ASP.NET Core 9.0
- **代码编辑器**: Monaco Editor
- **Markdown 解析**: Markdig
- **进程管理**: System.Diagnostics.Process
- **流式传输**: Server-Sent Events (SSE)

## 开发建议

1. 在开发环境使用测试工具验证功能
2. 生产环境配置真实的 CLI 工具
3. 根据实际需求调整超时和并发限制
4. 定期检查和更新 CLI 工具版本

## 扩展功能建议

1. 添加对话历史持久化存储
2. 支持多个并行会话
3. 添加代码差异对比功能
4. 集成更多 AI 编程助手
5. 添加用户认证和授权

## 许可证

请遵守所使用的各个 CLI 工具的许可证条款。
