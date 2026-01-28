namespace WebCodeCli.Domain.Repositories.Base.ScheduledTask;

/// <summary>
/// 定时任务仓储接口
/// </summary>
public interface IScheduledTaskRepository : IRepository<ScheduledTaskEntity>
{
    /// <summary>
    /// 根据用户名获取所有定时任务
    /// </summary>
    Task<List<ScheduledTaskEntity>> GetByUsernameAsync(string username);
    
    /// <summary>
    /// 根据任务ID和用户名获取任务
    /// </summary>
    Task<ScheduledTaskEntity?> GetByIdAndUsernameAsync(string taskId, string username);
    
    /// <summary>
    /// 根据任务ID和用户名删除任务
    /// </summary>
    Task<bool> DeleteByIdAndUsernameAsync(string taskId, string username);
    
    /// <summary>
    /// 根据用户名获取任务列表（按更新时间降序）
    /// </summary>
    Task<List<ScheduledTaskEntity>> GetByUsernameOrderByUpdatedAtAsync(string username);
    
    /// <summary>
    /// 获取所有启用的定时任务
    /// </summary>
    Task<List<ScheduledTaskEntity>> GetAllEnabledAsync();
    
    /// <summary>
    /// 检查任务名称是否已存在
    /// </summary>
    Task<bool> ExistsByNameAndUsernameAsync(string name, string username, string? excludeTaskId = null);
    
    /// <summary>
    /// 更新任务的下次执行时间
    /// </summary>
    Task<bool> UpdateNextFireTimeAsync(string taskId, DateTime? nextFireTime);
    
    /// <summary>
    /// 更新任务的执行状态
    /// </summary>
    Task<bool> UpdateExecutionStatusAsync(string taskId, DateTime? lastFireTime, string? lastExecutionStatus);
}
