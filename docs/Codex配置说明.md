# Codex CLI 配置说明

## 正确的配置

### 最终配置

```json
{
  "Id": "codex",
  "Name": "OpenAI Codex",
  "Description": "OpenAI Codex 代码生成",
  "Command": "codex",
  "ArgumentTemplate": "exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox {prompt}",
  "WorkingDirectory": "",
  "Enabled": true,
  "TimeoutSeconds": 300,
  "EnvironmentVariables": {}
}
```

## 关键要点

### 1. 使用 `exec` 子命令

Codex 默认是交互式模式，需要终端。要在非交互环境中运行，必须使用 `exec` 子命令：

```bash
codex exec "你的问题"
```

### 2. 必需的标志

#### `--skip-git-repo-check`
- **作用：** 跳过 Git 仓库检查
- **原因：** 否则 codex 会因为"不在可信目录"而拒绝执行

#### `--dangerously-bypass-approvals-and-sandbox`
- **作用：** 跳过所有确认提示并无沙箱执行命令
- **原因：** 在非交互环境中无法响应确认提示
- **警告：** 极其危险，仅适用于外部已沙箱化的环境

### 3. 为什么不用 PowerShell 包装

最初配置：
```json
{
  "Command": "powershell",
  "ArgumentTemplate": "-NoProfile -ExecutionPolicy Bypass -Command \"codex {prompt}\""
}
```

**问题：**
- 引号嵌套导致参数传递错误
- PowerShell 字符串转义复杂
- codex 默认需要交互式终端

**解决：**
直接调用 `codex` 命令，使用 `exec` 子命令

## Codex 输出格式

Codex 的输出包含多个部分：

```
OpenAI Codex v0.46.0 (research preview)
--------
workdir: D:\git\WebCodeCli
model: gpt-5-codex
provider: webcode
...
--------
user
[用户问题]

thinking
[AI 思考过程]

codex
[代码和回答]

[重复的最终回答]

tokens used
3,315
```

### 输出特点

1. **元信息头部** - 版本、工作目录、模型等信息
2. **思考过程** (`thinking`) - AI 的推理过程
3. **代码回答** (`codex`) - 实际的代码和解释
4. **重复回答** - 最终答案会重复一次
5. **Token 统计** - 使用的 token 数量

### 输出处理建议

在前端展示时，可以：
1. **完整展示** - 保留所有内容（推荐）
2. **过滤标记** - 去除 `thinking`、`codex` 等标记
3. **仅显示最终答案** - 跳过思考过程

## 其他有用的 Codex 选项

### 指定模型

```json
{
  "ArgumentTemplate": "exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox -m o3 {prompt}"
}
```

### 启用网络搜索

```json
{
  "ArgumentTemplate": "exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox --search {prompt}"
}
```

### 指定工作目录

```json
{
  "ArgumentTemplate": "exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox -C /path/to/project {prompt}"
}
```

### 附加图片（需要多行命令）

```bash
codex exec --skip-git-repo-check -i image.png "描述这张图片中的代码"
```

## 常见问题

### Q: 为什么退出代码是 1 但没有输出？

**A:** 可能的原因：
1. 没有使用 `exec` 子命令
2. 缺少 `--skip-git-repo-check` 标志
3. 在非交互环境中没有 `--dangerously-bypass-approvals-and-sandbox`

### Q: 输出中的中文显示为乱码？

**A:** 这是控制台编码问题。Codex 会自动处理并正确识别（如示例中所示）。

### Q: 可以使用其他审批策略吗？

**A:** 可以，但在非交互环境中：
- `--ask-for-approval never` - 从不询问
- `--ask-for-approval on-failure` - 仅失败时询问（但仍需交互）
- `--full-auto` - 便捷别名（沙箱 + 自动执行）

对于我们的用例，`--dangerously-bypass-approvals-and-sandbox` 是最简单的选择。

### Q: 如何限制 Codex 的权限？

**A:** 使用沙箱选项：

```bash
# 只读模式
codex exec --sandbox read-only {prompt}

# 只能写工作区
codex exec --sandbox workspace-write {prompt}

# 完全访问（危险）
codex exec --sandbox danger-full-access {prompt}
```

但这些都需要审批提示，不适合非交互环境。

## 安全建议

⚠️ **警告：** `--dangerously-bypass-approvals-and-sandbox` 非常危险！

### 建议的安全措施

1. **在隔离环境运行**
   - 使用 Docker 容器
   - 虚拟机
   - 专用的开发服务器

2. **限制用户输入**
   - 过滤危险命令关键词
   - 限制提示长度
   - 记录所有执行的命令

3. **监控执行**
   - 记录所有 Codex 调用
   - 设置资源限制
   - 监控异常活动

4. **用户教育**
   - 明确告知用户风险
   - 要求用户确认
   - 提供使用指南

## 测试命令

```bash
# 测试基本功能
codex exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox "写一个 Hello World"

# 测试代码生成
codex exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox "创建一个快速排序算法"

# 测试文件操作（危险！）
codex exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox "列出当前目录的所有文件"
```

## 参考资源

- [Codex CLI GitHub](https://github.com/your-repo/codex-cli) （如果有的话）
- Codex 官方文档
- `codex --help` - 查看所有选项
- `codex exec --help` - 查看 exec 子命令选项

---

**配置更新后请重启应用以加载新配置！**

**测试前请确保：**
1. ✅ Codex CLI 已登录：`codex login`
2. ✅ 有可用的 API 额度
3. ✅ 网络连接正常

