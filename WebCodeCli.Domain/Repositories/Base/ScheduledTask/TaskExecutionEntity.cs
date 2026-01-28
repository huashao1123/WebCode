using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.ScheduledTask;

/// <summary>
/// 任务执行记录实体
/// </summary>
[SugarTable("TaskExecution")]
public class TaskExecutionEntity
{
    /// <summary>
    /// 执行记录ID（主键，自增）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnDataType = "INTEGER")]
    public long Id { get; set; }
    
    /// <summary>
    /// 任务ID（外键）
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = false)]
    public string TaskId { get; set; } = string.Empty;
    
    /// <summary>
    /// 执行状态：Pending, Running, Success, Failed, Timeout, Cancelled
    /// </summary>
    [SugarColumn(Length = 32, IsNullable = false)]
    public string Status { get; set; } = "Pending";
    
    /// <summary>
    /// 开始时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime StartTime { get; set; }
    
    /// <summary>
    /// 结束时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? EndTime { get; set; }
    
    /// <summary>
    /// 执行耗时（毫秒）
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? DurationMs { get; set; }
    
    /// <summary>
    /// 执行输出（完整的 CLI 输出）
    /// </summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string? Output { get; set; }
    
    /// <summary>
    /// 错误信息
    /// </summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// 重试次数
    /// </summary>
    [SugarColumn(IsNullable = false, DefaultValue = "0")]
    public int RetryCount { get; set; } = 0;
    
    /// <summary>
    /// 触发类型：Scheduled（定时触发）, Manual（手动触发）
    /// </summary>
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? TriggerType { get; set; }
}

/// <summary>
/// 执行状态枚举
/// </summary>
public static class TaskExecutionStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Success = "Success";
    public const string Failed = "Failed";
    public const string Timeout = "Timeout";
    public const string Cancelled = "Cancelled";
}

/// <summary>
/// 触发类型枚举
/// </summary>
public static class TaskTriggerType
{
    public const string Scheduled = "Scheduled";
    public const string Manual = "Manual";
}
