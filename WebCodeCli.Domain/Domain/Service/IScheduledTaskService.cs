using WebCodeCli.Domain.Repositories.Base.ScheduledTask;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 定时任务服务接口
/// </summary>
public interface IScheduledTaskService
{
    #region 任务管理
    
    /// <summary>
    /// 获取用户的所有定时任务
    /// </summary>
    Task<List<ScheduledTaskEntity>> GetTasksAsync(string username);
    
    /// <summary>
    /// 根据ID获取任务
    /// </summary>
    Task<ScheduledTaskEntity?> GetTaskByIdAsync(string taskId, string username);
    
    /// <summary>
    /// 创建定时任务
    /// </summary>
    Task<(bool Success, string? ErrorMessage, ScheduledTaskEntity? Task)> CreateTaskAsync(
        string username,
        string name,
        string? description,
        string cronExpression,
        string toolId,
        string prompt,
        string? sessionId,
        int timeoutSeconds = 3600,
        int maxRetries = 0,
        bool isEnabled = true);
    
    /// <summary>
    /// 更新定时任务
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> UpdateTaskAsync(
        string taskId,
        string username,
        string name,
        string? description,
        string cronExpression,
        string toolId,
        string prompt,
        string? sessionId,
        int timeoutSeconds,
        int maxRetries,
        bool isEnabled);
    
    /// <summary>
    /// 删除定时任务
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> DeleteTaskAsync(string taskId, string username, bool deleteExecutions = true);
    
    /// <summary>
    /// 启用/禁用定时任务
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> ToggleTaskAsync(string taskId, string username, bool isEnabled);
    
    #endregion
    
    #region 任务执行
    
    /// <summary>
    /// 立即执行任务
    /// </summary>
    Task<(bool Success, string? ErrorMessage, long? ExecutionId)> TriggerTaskAsync(string taskId, string username);
    
    /// <summary>
    /// 取消正在执行的任务
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> CancelExecutionAsync(long executionId);
    
    #endregion
    
    #region 执行记录
    
    /// <summary>
    /// 获取任务的执行记录
    /// </summary>
    Task<List<TaskExecutionEntity>> GetExecutionsAsync(string taskId, string username, int limit = 50);
    
    /// <summary>
    /// 获取单个执行记录详情
    /// </summary>
    Task<TaskExecutionEntity?> GetExecutionByIdAsync(long executionId);
    
    /// <summary>
    /// 清理任务的旧执行记录
    /// </summary>
    Task<int> CleanupExecutionsAsync(string taskId, string username, int keepCount = 100);
    
    #endregion
    
    #region Cron 表达式
    
    /// <summary>
    /// 验证 Cron 表达式
    /// </summary>
    (bool IsValid, string? ErrorMessage) ValidateCronExpression(string cronExpression);
    
    /// <summary>
    /// 获取 Cron 表达式的下N次执行时间
    /// </summary>
    List<DateTime> GetNextFireTimes(string cronExpression, int count = 5);
    
    /// <summary>
    /// 获取常用的 Cron 表达式预设
    /// </summary>
    List<CronPreset> GetCronPresets();
    
    #endregion
}

/// <summary>
/// Cron 表达式预设
/// </summary>
public class CronPreset
{
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
