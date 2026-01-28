namespace WebCodeCli.Domain.Repositories.Base.ScheduledTask;

/// <summary>
/// 任务执行记录仓储接口
/// </summary>
public interface ITaskExecutionRepository : IRepository<TaskExecutionEntity>
{
    /// <summary>
    /// 根据任务ID获取执行记录（按开始时间降序）
    /// </summary>
    Task<List<TaskExecutionEntity>> GetByTaskIdAsync(string taskId, int limit = 50);
    
    /// <summary>
    /// 根据任务ID获取最近一次执行记录
    /// </summary>
    Task<TaskExecutionEntity?> GetLatestByTaskIdAsync(string taskId);
    
    /// <summary>
    /// 根据任务ID删除所有执行记录
    /// </summary>
    Task<int> DeleteByTaskIdAsync(string taskId);
    
    /// <summary>
    /// 更新执行记录状态
    /// </summary>
    Task<bool> UpdateStatusAsync(long executionId, string status, DateTime? endTime, int? durationMs, string? output, string? errorMessage);
    
    /// <summary>
    /// 追加输出内容
    /// </summary>
    Task<bool> AppendOutputAsync(long executionId, string outputChunk);
    
    /// <summary>
    /// 获取正在运行的执行记录
    /// </summary>
    Task<List<TaskExecutionEntity>> GetRunningExecutionsAsync();
    
    /// <summary>
    /// 清理过期的执行记录（保留最近N条）
    /// </summary>
    Task<int> CleanupOldExecutionsAsync(string taskId, int keepCount);
    
    /// <summary>
    /// 根据时间范围获取执行记录
    /// </summary>
    Task<List<TaskExecutionEntity>> GetByTimeRangeAsync(string taskId, DateTime startTime, DateTime endTime);
}
