using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.ScheduledTask;

/// <summary>
/// 定时任务实体
/// </summary>
[SugarTable("ScheduledTask")]
public class ScheduledTaskEntity
{
    /// <summary>
    /// 任务ID（主键）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    /// <summary>
    /// 用户名（多用户支持）
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string Username { get; set; } = "default";
    
    /// <summary>
    /// 任务名称
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 任务描述
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? Description { get; set; }
    
    /// <summary>
    /// Cron 表达式
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string CronExpression { get; set; } = string.Empty;
    
    /// <summary>
    /// CLI 工具ID
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = false)]
    public string ToolId { get; set; } = string.Empty;
    
    /// <summary>
    /// 执行提示词
    /// </summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = false)]
    public string Prompt { get; set; } = string.Empty;
    
    /// <summary>
    /// 目标会话ID（可选，关联到特定会话/工作区）
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SessionId { get; set; }
    
    /// <summary>
    /// 是否启用
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// 执行超时时间（秒）
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public int TimeoutSeconds { get; set; } = 3600; // 默认1小时
    
    /// <summary>
    /// 最大重试次数
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public int MaxRetries { get; set; } = 0;
    
    /// <summary>
    /// 创建时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 更新时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 下次执行时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? NextFireTime { get; set; }
    
    /// <summary>
    /// 上次执行时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? LastFireTime { get; set; }
    
    /// <summary>
    /// 上次执行状态
    /// </summary>
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? LastExecutionStatus { get; set; }
}
