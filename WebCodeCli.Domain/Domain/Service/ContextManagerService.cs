using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 上下文管理服务实现
/// </summary>
[ServiceDescription(typeof(IContextManagerService), ServiceLifetime.Singleton)]
public class ContextManagerService : IContextManagerService
{
    private readonly Dictionary<string, List<ContextItem>> _sessionContexts = new();
    private readonly Dictionary<string, ContextManagerConfig> _sessionConfigs = new();
    private readonly ICliExecutorService _cliExecutorService;
    
    public ContextManagerService(ICliExecutorService cliExecutorService)
    {
        _cliExecutorService = cliExecutorService;
    }
    
    /// <summary>
    /// 从消息列表构建上下文
    /// </summary>
    public async Task<List<ContextItem>> BuildContextFromMessagesAsync(string sessionId, List<ChatMessage> messages)
    {
        var config = GetOrCreateConfig(sessionId);
        var contextItems = new List<ContextItem>();
        
        foreach (var message in messages)
        {
            // 添加消息本身
            var messageItem = new ContextItem
            {
                Id = message.Id ?? Guid.NewGuid().ToString(),
                Type = message.Role == "user" ? ContextItemType.UserMessage : ContextItemType.AssistantMessage,
                Content = message.Content,
                MessageId = message.Id,
                EstimatedTokens = TokenEstimator.EstimateTokens(message.Content),
                CreatedAt = message.CreatedAt,
                Priority = message.Role == "user" ? 7 : 5, // 用户消息优先级更高
                IsIncluded = true
            };
            
            // 如果是错误消息，提高优先级
            if (message.HasError)
            {
                messageItem.Type = ContextItemType.ErrorMessage;
                messageItem.Priority = 9;
                messageItem.Tags.Add("error");
            }
            
            contextItems.Add(messageItem);
            
            // 自动提取代码片段
            if (config.AutoExtractCodeSnippets)
            {
                var snippets = CodeSnippetExtractor.ExtractCodeSnippets(message.Content);
                foreach (var snippet in snippets)
                {
                    var snippetItem = new ContextItem
                    {
                        Type = ContextItemType.CodeSnippet,
                        Content = snippet.Code,
                        MessageId = message.Id,
                        Language = snippet.Language,
                        EstimatedTokens = TokenEstimator.EstimateCodeTokens(snippet.Code),
                        Priority = 6,
                        IsIncluded = true,
                        Tags = new List<string> { "code", snippet.Language }
                    };
                    
                    // 生成摘要
                    snippetItem.Summary = GenerateCodeSnippetSummary(snippet.Code, snippet.Language);
                    
                    contextItems.Add(snippetItem);
                }
            }
            
            // 自动提取文件引用
            if (config.AutoExtractFileReferences)
            {
                var fileRefs = CodeSnippetExtractor.ExtractFileReferencesWithLines(message.Content);
                foreach (var (filePath, startLine, endLine) in fileRefs)
                {
                    var fileItem = new ContextItem
                    {
                        Type = ContextItemType.FileReference,
                        Content = $"文件引用: {filePath}" + (startLine.HasValue ? $" (行 {startLine}-{endLine ?? startLine})" : ""),
                        MessageId = message.Id,
                        FilePath = filePath,
                        LineRange = startLine.HasValue ? (startLine.Value, endLine ?? startLine.Value) : null,
                        EstimatedTokens = 10, // 文件引用本身占用很少 Token
                        Priority = 4,
                        IsIncluded = true,
                        Tags = new List<string> { "file-reference" }
                    };
                    
                    contextItems.Add(fileItem);
                }
            }
        }
        
        // 保存到会话
        _sessionContexts[sessionId] = contextItems;
        
        // 检查是否需要自动压缩
        await AutoCompressIfNeededAsync(sessionId);
        
        return contextItems;
    }
    
    /// <summary>
    /// 添加上下文项
    /// </summary>
    public void AddContextItem(string sessionId, ContextItem item)
    {
        if (!_sessionContexts.ContainsKey(sessionId))
        {
            _sessionContexts[sessionId] = new List<ContextItem>();
        }
        
        _sessionContexts[sessionId].Add(item);
    }
    
    /// <summary>
    /// 移除上下文项
    /// </summary>
    public void RemoveContextItem(string sessionId, string itemId)
    {
        if (_sessionContexts.TryGetValue(sessionId, out var items))
        {
            items.RemoveAll(i => i.Id == itemId);
        }
    }
    
    /// <summary>
    /// 切换上下文项的包含状态
    /// </summary>
    public void ToggleContextItem(string sessionId, string itemId)
    {
        if (_sessionContexts.TryGetValue(sessionId, out var items))
        {
            var item = items.FirstOrDefault(i => i.Id == itemId);
            if (item != null)
            {
                item.IsIncluded = !item.IsIncluded;
            }
        }
    }
    
    /// <summary>
    /// 获取会话的所有上下文项
    /// </summary>
    public List<ContextItem> GetContextItems(string sessionId)
    {
        return _sessionContexts.TryGetValue(sessionId, out var items) 
            ? items 
            : new List<ContextItem>();
    }
    
    /// <summary>
    /// 获取会话的上下文统计
    /// </summary>
    public ContextStatistics GetContextStatistics(string sessionId)
    {
        var config = GetOrCreateConfig(sessionId);
        var items = GetContextItems(sessionId);
        
        var stats = new ContextStatistics
        {
            TotalTokens = config.MaxContextTokens,
            ItemCount = items.Count,
            IncludedItemCount = items.Count(i => i.IsIncluded),
            ExcludedItemCount = items.Count(i => !i.IsIncluded)
        };
        
        // 计算已使用的 Token
        stats.UsedTokens = items.Where(i => i.IsIncluded).Sum(i => i.EstimatedTokens);
        
        // 按类型统计
        foreach (var type in Enum.GetValues<ContextItemType>())
        {
            stats.ItemsByType[type] = items.Count(i => i.Type == type);
        }
        
        return stats;
    }
    
    /// <summary>
    /// 压缩上下文
    /// </summary>
    public async Task<CompressionResult> CompressContextAsync(string sessionId, CompressionStrategy strategy)
    {
        var config = GetOrCreateConfig(sessionId);
        var items = GetContextItems(sessionId);
        
        var result = new CompressionResult
        {
            Strategy = strategy,
            TokensBefore = items.Where(i => i.IsIncluded).Sum(i => i.EstimatedTokens)
        };
        
        switch (strategy)
        {
            case CompressionStrategy.KeepRecent:
                await CompressKeepRecentAsync(sessionId, config, items, result);
                break;
                
            case CompressionStrategy.KeepHighPriority:
                await CompressKeepHighPriorityAsync(sessionId, config, items, result);
                break;
                
            case CompressionStrategy.SmartSummary:
                await CompressSmartSummaryAsync(sessionId, config, items, result);
                break;
                
            case CompressionStrategy.RemoveDuplicates:
                await CompressRemoveDuplicatesAsync(sessionId, config, items, result);
                break;
        }
        
        result.TokensAfter = items.Where(i => i.IsIncluded).Sum(i => i.EstimatedTokens);
        
        return result;
    }
    
    /// <summary>
    /// 自动压缩（当达到阈值时）
    /// </summary>
    public async Task<CompressionResult?> AutoCompressIfNeededAsync(string sessionId)
    {
        var stats = GetContextStatistics(sessionId);
        var config = GetOrCreateConfig(sessionId);
        
        // 检查是否达到压缩阈值
        if (stats.UsagePercentage >= config.AutoCompressThreshold * 100)
        {
            Console.WriteLine($"[上下文管理] 达到压缩阈值 ({stats.UsagePercentage:F1}%)，开始自动压缩...");
            
            // 优先使用智能摘要策略
            return await CompressContextAsync(sessionId, CompressionStrategy.SmartSummary);
        }
        
        return null;
    }
    
    /// <summary>
    /// 清空会话上下文
    /// </summary>
    public void ClearContext(string sessionId)
    {
        _sessionContexts.Remove(sessionId);
        _sessionConfigs.Remove(sessionId);
    }
    
    /// <summary>
    /// 从工作区文件添加上下文
    /// </summary>
    public async Task AddWorkspaceFileToContextAsync(string sessionId, string filePath, string? content = null)
    {
        try
        {
            // 如果没有提供内容，从工作区读取
            if (string.IsNullOrEmpty(content))
            {
                var workspacePath = _cliExecutorService.GetSessionWorkspacePath(sessionId);
                var fullPath = Path.Combine(workspacePath, filePath);
                
                if (File.Exists(fullPath))
                {
                    content = await File.ReadAllTextAsync(fullPath);
                }
                else
                {
                    Console.WriteLine($"[上下文管理] 文件不存在: {fullPath}");
                    return;
                }
            }
            
            var language = CodeSnippetExtractor.DetectLanguage(content, filePath);
            
            var item = new ContextItem
            {
                Type = ContextItemType.WorkspaceFile,
                Content = content,
                FilePath = filePath,
                Language = language,
                EstimatedTokens = TokenEstimator.EstimateCodeTokens(content),
                Priority = 5,
                IsIncluded = true,
                Tags = new List<string> { "workspace-file", language },
                Summary = $"工作区文件: {Path.GetFileName(filePath)} ({content.Split('\n').Length} 行)"
            };
            
            AddContextItem(sessionId, item);
            
            Console.WriteLine($"[上下文管理] 已添加工作区文件: {filePath} ({item.EstimatedTokens} tokens)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[上下文管理] 添加工作区文件失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 批量添加工作区文件到上下文
    /// </summary>
    public async Task AddWorkspaceFilesToContextAsync(string sessionId, List<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            await AddWorkspaceFileToContextAsync(sessionId, filePath);
        }
    }
    
    /// <summary>
    /// 搜索上下文项
    /// </summary>
    public List<ContextItem> SearchContextItems(string sessionId, string keyword)
    {
        var items = GetContextItems(sessionId);
        
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return items;
        }
        
        return items.Where(i => 
            i.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            (i.FilePath != null && i.FilePath.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
            (i.Summary != null && i.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
            i.Tags.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }
    
    /// <summary>
    /// 按类型筛选上下文项
    /// </summary>
    public List<ContextItem> FilterContextItemsByType(string sessionId, ContextItemType type)
    {
        return GetContextItems(sessionId).Where(i => i.Type == type).ToList();
    }
    
    /// <summary>
    /// 按标签筛选上下文项
    /// </summary>
    public List<ContextItem> FilterContextItemsByTag(string sessionId, string tag)
    {
        return GetContextItems(sessionId).Where(i => i.Tags.Contains(tag)).ToList();
    }
    
    /// <summary>
    /// 更新上下文项优先级
    /// </summary>
    public void UpdateContextItemPriority(string sessionId, string itemId, int priority)
    {
        if (_sessionContexts.TryGetValue(sessionId, out var items))
        {
            var item = items.FirstOrDefault(i => i.Id == itemId);
            if (item != null)
            {
                item.Priority = Math.Clamp(priority, 0, 10);
            }
        }
    }
    
    /// <summary>
    /// 生成上下文摘要
    /// </summary>
    public async Task<string> GenerateContextSummaryAsync(string sessionId)
    {
        var items = GetContextItems(sessionId);
        var stats = GetContextStatistics(sessionId);
        
        var summary = new StringBuilder();
        summary.AppendLine("# 上下文摘要");
        summary.AppendLine();
        summary.AppendLine($"- 总项数: {stats.ItemCount}");
        summary.AppendLine($"- 包含项数: {stats.IncludedItemCount}");
        summary.AppendLine($"- Token 使用: {stats.UsedTokens:N0} / {stats.TotalTokens:N0} ({stats.UsagePercentage:F1}%)");
        summary.AppendLine();
        
        summary.AppendLine("## 按类型分组:");
        foreach (var (type, count) in stats.ItemsByType.OrderByDescending(kv => kv.Value))
        {
            if (count > 0)
            {
                summary.AppendLine($"- {GetTypeDisplayName(type)}: {count}");
            }
        }
        
        summary.AppendLine();
        summary.AppendLine("## 高优先级项:");
        var highPriorityItems = items.Where(i => i.Priority >= 7 && i.IsIncluded)
            .OrderByDescending(i => i.Priority)
            .Take(10);
        
        foreach (var item in highPriorityItems)
        {
            var preview = item.Summary ?? (item.Content.Length > 50 ? item.Content.Substring(0, 50) + "..." : item.Content);
            summary.AppendLine($"- [{GetTypeDisplayName(item.Type)}] {preview} (优先级: {item.Priority})");
        }
        
        return await Task.FromResult(summary.ToString());
    }
    
    /// <summary>
    /// 导出上下文
    /// </summary>
    public async Task<string> ExportContextAsync(string sessionId, string format = "json")
    {
        var items = GetContextItems(sessionId);
        var stats = GetContextStatistics(sessionId);
        
        var exportData = new
        {
            SessionId = sessionId,
            ExportedAt = DateTime.Now,
            Statistics = stats,
            Items = items
        };
        
        return format.ToLower() switch
        {
            "json" => await Task.FromResult(JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            })),
            _ => throw new NotSupportedException($"不支持的导出格式: {format}")
        };
    }
    
    /// <summary>
    /// 导入上下文
    /// </summary>
    public async Task ImportContextAsync(string sessionId, string data, string format = "json")
    {
        try
        {
            if (format.ToLower() == "json")
            {
                var importData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(data);
                if (importData != null && importData.TryGetValue("Items", out var itemsElement))
                {
                    var items = JsonSerializer.Deserialize<List<ContextItem>>(itemsElement.GetRawText());
                    if (items != null)
                    {
                        _sessionContexts[sessionId] = items;
                        Console.WriteLine($"[上下文管理] 已导入 {items.Count} 个上下文项");
                    }
                }
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[上下文管理] 导入上下文失败: {ex.Message}");
            throw;
        }
    }
    
    // 私有辅助方法
    
    private ContextManagerConfig GetOrCreateConfig(string sessionId)
    {
        if (!_sessionConfigs.ContainsKey(sessionId))
        {
            _sessionConfigs[sessionId] = new ContextManagerConfig();
        }
        
        return _sessionConfigs[sessionId];
    }
    
    private async Task CompressKeepRecentAsync(string sessionId, ContextManagerConfig config, List<ContextItem> items, CompressionResult result)
    {
        // 保留最近的 N 条消息，排除其他
        var messageItems = items.Where(i => i.Type == ContextItemType.UserMessage || i.Type == ContextItemType.AssistantMessage)
            .OrderByDescending(i => i.CreatedAt)
            .ToList();
        
        var toKeep = messageItems.Take(config.KeepRecentMessages).Select(i => i.Id).ToHashSet();
        
        foreach (var item in items)
        {
            if ((item.Type == ContextItemType.UserMessage || item.Type == ContextItemType.AssistantMessage) && 
                !toKeep.Contains(item.Id) && 
                item.IsIncluded)
            {
                item.IsIncluded = false;
                result.RemovedItems.Add(item);
            }
        }
        
        await Task.CompletedTask;
    }
    
    private async Task CompressKeepHighPriorityAsync(string sessionId, ContextManagerConfig config, List<ContextItem> items, CompressionResult result)
    {
        // 按优先级排序，移除低优先级项直到满足 Token 限制
        var targetTokens = (int)(config.MaxContextTokens * 0.7); // 压缩到 70%
        var currentTokens = items.Where(i => i.IsIncluded).Sum(i => i.EstimatedTokens);
        
        if (currentTokens <= targetTokens)
        {
            await Task.CompletedTask;
            return;
        }
        
        var sortedItems = items.Where(i => i.IsIncluded)
            .OrderBy(i => i.Priority)
            .ThenByDescending(i => i.EstimatedTokens)
            .ToList();
        
        foreach (var item in sortedItems)
        {
            if (currentTokens <= targetTokens)
            {
                break;
            }
            
            item.IsIncluded = false;
            currentTokens -= item.EstimatedTokens;
            result.RemovedItems.Add(item);
        }
        
        await Task.CompletedTask;
    }
    
    private async Task CompressSmartSummaryAsync(string sessionId, ContextManagerConfig config, List<ContextItem> items, CompressionResult result)
    {
        // 智能摘要：保留关键信息，压缩冗余内容
        var targetTokens = (int)(config.MaxContextTokens * 0.7);
        var currentTokens = items.Where(i => i.IsIncluded).Sum(i => i.EstimatedTokens);
        
        if (currentTokens <= targetTokens)
        {
            await Task.CompletedTask;
            return;
        }
        
        // 1. 保留高优先级项（优先级 >= 7）
        var highPriorityItems = items.Where(i => i.Priority >= 7).Select(i => i.Id).ToHashSet();
        
        // 2. 保留最近的用户消息
        var recentUserMessages = items
            .Where(i => i.Type == ContextItemType.UserMessage)
            .OrderByDescending(i => i.CreatedAt)
            .Take(config.KeepRecentMessages)
            .Select(i => i.Id)
            .ToHashSet();
        
        // 3. 移除低优先级且非最近的项
        foreach (var item in items.Where(i => i.IsIncluded))
        {
            if (currentTokens <= targetTokens)
            {
                break;
            }
            
            if (!highPriorityItems.Contains(item.Id) && !recentUserMessages.Contains(item.Id))
            {
                // 对于代码片段和文件内容，尝试生成摘要而不是完全移除
                if (item.Type == ContextItemType.CodeSnippet || item.Type == ContextItemType.WorkspaceFile)
                {
                    if (string.IsNullOrEmpty(item.Summary))
                    {
                        item.Summary = GenerateCodeSnippetSummary(item.Content, item.Language ?? "text");
                    }
                    
                    // 用摘要替换内容
                    var originalTokens = item.EstimatedTokens;
                    item.Content = item.Summary;
                    item.EstimatedTokens = TokenEstimator.EstimateTokens(item.Summary);
                    
                    currentTokens -= (originalTokens - item.EstimatedTokens);
                    result.CompressedItems.Add(item);
                }
                else
                {
                    item.IsIncluded = false;
                    currentTokens -= item.EstimatedTokens;
                    result.RemovedItems.Add(item);
                }
            }
        }
        
        await Task.CompletedTask;
    }
    
    private async Task CompressRemoveDuplicatesAsync(string sessionId, ContextManagerConfig config, List<ContextItem> items, CompressionResult result)
    {
        // 移除重复的代码片段和文件引用
        var seenContent = new HashSet<string>();
        
        foreach (var item in items.Where(i => i.IsIncluded))
        {
            if (item.Type == ContextItemType.CodeSnippet || item.Type == ContextItemType.FileReference)
            {
                var contentHash = GetContentHash(item.Content);
                
                if (seenContent.Contains(contentHash))
                {
                    item.IsIncluded = false;
                    result.RemovedItems.Add(item);
                }
                else
                {
                    seenContent.Add(contentHash);
                }
            }
        }
        
        await Task.CompletedTask;
    }
    
    private string GenerateCodeSnippetSummary(string code, string language)
    {
        var lines = code.Split('\n');
        var lineCount = lines.Length;
        
        // 提取第一行作为摘要（通常是函数/类定义）
        var firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        
        if (firstLine.Length > 60)
        {
            firstLine = firstLine.Substring(0, 57) + "...";
        }
        
        return $"[{language}] {firstLine} ({lineCount} 行)";
    }
    
    private string GetContentHash(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hash);
    }
    
    private string GetTypeDisplayName(ContextItemType type)
    {
        return type switch
        {
            ContextItemType.UserMessage => "用户消息",
            ContextItemType.AssistantMessage => "助手消息",
            ContextItemType.CodeSnippet => "代码片段",
            ContextItemType.FileReference => "文件引用",
            ContextItemType.WorkspaceFile => "工作区文件",
            ContextItemType.ErrorMessage => "错误消息",
            _ => type.ToString()
        };
    }
}

