# Codex Session Resume 功能说明

## 概述

为了支持 OpenAI Codex 的会话恢复功能，系统现在能够自动解析 Codex 返回的 session id，并在后续请求中使用 `resume` 命令来保持会话上下文。

**注意：** 使用**一次性进程模式**而非持久化进程模式，因为Web环境中持久化进程管理较为复杂且不稳定。

## 功能特性

### 1. 自动解析 Session ID

当第一次使用 Codex 时，系统会从输出中自动解析 session id：

```
OpenAI Codex v0.53.0 (research preview)
--------
workdir: D:\Temp\WebCodeCli\Workspaces\77ce780e-845a-40d9-bed9-7a00b27b5be1
model: gpt-5-codex
provider: webcode
approval: never
sandbox: danger-full-access
reasoning effort: high
reasoning summaries: detailed
session id: 019a3994-f978-7fe2-8028-3bf4365b4af7
--------
```

系统会提取 `session id: 019a3994-f978-7fe2-8028-3bf4365b4af7` 并存储起来。

### 2. 自动使用 Resume 命令

- **第一次请求**：发送完整的初始命令
  ```
  exec --sandbox danger-full-access --json "创建一个HTML页面"
  ```

- **后续请求**：自动添加 resume 参数
  ```
  exec --sandbox danger-full-access --json "添加CSS样式" resume 019a3994-f978-7fe2-8028-3bf4365b4af7
  ```

### 3. 会话隔离

每个浏览器会话都有独立的 Codex session id，确保不同用户之间的对话不会相互干扰。

## 技术实现

### 数据模型扩展

在 `ChatMessage` 中添加了 `CodexSessionId` 字段：

```csharp
public class ChatMessage
{
    // ... 其他字段 ...
    
    /// <summary>
    /// Codex Session ID（用于resume）
    /// </summary>
    public string? CodexSessionId { get; set; }
}
```

### 服务层实现

在 `CliExecutorService` 中：

1. **存储 Session ID 映射**
   ```csharp
   // 存储每个会话的Codex Session ID
   private readonly Dictionary<string, string> _codexSessionIds = new();
   ```

2. **解析 Session ID**
   ```csharp
   private string? ParseCodexSessionId(string output)
   {
       // 从输出中查找 "session id: xxx" 行
       // 提取并返回 session id
   }
   ```

3. **构建命令**
   ```csharp
   if (isCodexTool)
   {
       if (!string.IsNullOrEmpty(codexSessionId))
       {
           // 使用 resume 命令
           actualInput = $"exec --sandbox danger-full-access --json \"{prompt}\" resume {codexSessionId}";
       }
       else
       {
           // 第一次请求，使用初始命令
           actualInput = tool.ArgumentTemplate.Replace("{prompt}", prompt);
       }
   }
   ```

### 配置文件

在 `appsettings.json` 中配置 Codex 工具：

```json
{
  "Id": "codex",
  "Name": "OpenAI Codex",
  "Description": "OpenAI Codex 代码生成",
  "Command": "codex",
  "ArgumentTemplate": "exec --sandbox danger-full-access --json \"{prompt}\"",
  "PersistentModeArguments": "",
  "UsePersistentProcess": false,
  "WorkingDirectory": "",
  "Enabled": true,
  "TimeoutSeconds": 0,
  "EnvironmentVariables": {},
  "SupportsSessionResume": true
}
```

**重要说明：**
- `UsePersistentProcess` 设置为 `false` - 使用一次性进程模式
- 每次请求都会启动新的 `codex` 进程
- 通过 `resume SESSION_ID` 参数来保持上下文连续性

## 使用流程

### 用户视角

1. 用户打开编程助手页面
2. 选择 "OpenAI Codex" 工具
3. 输入第一个问题，例如："创建一个HTML页面"
   - 系统执行：`codex exec --sandbox danger-full-access --json "创建一个HTML页面"`
   - 解析输出中的 session id
4. 继续输入后续问题，例如："添加CSS样式"
   - 系统自动执行：`codex exec --sandbox danger-full-access --json "添加CSS样式" resume 019a3994...`
5. Codex 能够记住之前的对话上下文，提供连贯的代码修改

### 系统行为

```
用户会话 A (sessionId: abc-123)
├─ 第1次请求 → 启动进程: codex exec --sandbox danger-full-access --json "创建HTML页面"
│               解析得到 codexSessionId: xyz-789
├─ 第2次请求 → 启动进程: codex exec --sandbox danger-full-access --json "添加CSS" resume xyz-789
└─ 第3次请求 → 启动进程: codex exec --sandbox danger-full-access --json "添加JS" resume xyz-789

用户会话 B (sessionId: def-456)
├─ 第1次请求 → 启动进程: codex exec --sandbox danger-full-access --json "创建登录页"
│               解析得到 codexSessionId: uvw-012
└─ 第2次请求 → 启动进程: codex exec --sandbox danger-full-access --json "添加验证" resume uvw-012
```

**关键点：**
- 每次请求都是**独立的进程**
- Session ID 保存在服务器内存中
- 通过 `resume` 参数连接到 Codex 的云端会话

## 清理机制

当用户清空对话或关闭页面时，系统会自动清理：
1. Codex session id 映射（从内存中删除）
2. 工作区文件

**注意：** 由于使用一次性进程模式，不需要清理持久化进程。

## 错误处理

1. **解析失败**：如果无法从输出中解析 session id，会记录警告日志，但不影响功能
2. **Session 过期**：如果 Codex session id 过期，Codex会返回错误，系统会创建新的 session
3. **进程异常**：一次性进程异常会记录错误，下次请求会重新尝试

## 日志输出

系统会记录关键操作的日志：

```
[Information] 【一次性进程模式】工具: OpenAI Codex, UsePersistentProcess=False
[Information] 执行 CLI 工具: OpenAI Codex, 会话: abc-123, 命令: codex exec --sandbox danger-full-access --json "创建HTML页面"
[Information] 进程已启动，PID: 12345，开始读取输出流
[Debug] 从输出中解析到session id: 019a3994-f978-7fe2-8028-3bf4365b4af7
[Information] 【Codex】解析到Session ID: 019a3994-f978-7fe2-8028-3bf4365b4af7 for 会话: abc-123
[Information] CLI 工具执行完成: OpenAI Codex, 退出代码: 0

# 后续请求
[Information] 【一次性进程模式】工具: OpenAI Codex, UsePersistentProcess=False
[Information] 使用Codex Resume模式, Session=019a3994-f978-7fe2-8028-3bf4365b4af7
[Information] 执行 CLI 工具: OpenAI Codex, 会话: abc-123, 命令: codex exec --sandbox danger-full-access --json "添加CSS" resume 019a3994-f978-7fe2-8028-3bf4365b4af7
```

## 测试建议

1. **基本功能测试**
   - 第一次请求能否成功解析 session id
   - 后续请求是否使用 resume 命令
   - 上下文是否正确保持

2. **边界条件测试**
   - Session id 不存在的情况
   - 输出中没有 session id 的情况
   - 多个用户同时使用

3. **性能测试**
   - 一次性进程的启动速度
   - Resume 命令的执行效率
   - 多次请求的响应时间对比

## 未来扩展

1. 支持手动指定 session id
2. 支持导出/导入 session
3. 支持 session 历史记录查看
4. 支持跨浏览器会话的 session 恢复

