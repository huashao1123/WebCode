using AntSK.Domain.Repositories.Base;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.ScheduledTask;

/// <summary>
/// 定时任务仓储实现
/// </summary>
[ServiceDescription(typeof(IScheduledTaskRepository), ServiceLifetime.Scoped)]
public class ScheduledTaskRepository : Repository<ScheduledTaskEntity>, IScheduledTaskRepository
{
    public ScheduledTaskRepository(ISqlSugarClient context = null) : base(context)
    {
    }

    /// <summary>
    /// 根据用户名获取所有定时任务
    /// </summary>
    public async Task<List<ScheduledTaskEntity>> GetByUsernameAsync(string username)
    {
        return await GetListAsync(x => x.Username == username);
    }

    /// <summary>
    /// 根据任务ID和用户名获取任务
    /// </summary>
    public async Task<ScheduledTaskEntity?> GetByIdAndUsernameAsync(string taskId, string username)
    {
        return await GetFirstAsync(x => x.Id == taskId && x.Username == username);
    }

    /// <summary>
    /// 根据任务ID和用户名删除任务
    /// </summary>
    public async Task<bool> DeleteByIdAndUsernameAsync(string taskId, string username)
    {
        return await DeleteAsync(x => x.Id == taskId && x.Username == username);
    }

    /// <summary>
    /// 根据用户名获取任务列表（按更新时间降序）
    /// </summary>
    public async Task<List<ScheduledTaskEntity>> GetByUsernameOrderByUpdatedAtAsync(string username)
    {
        return await GetDB().Queryable<ScheduledTaskEntity>()
            .Where(x => x.Username == username)
            .OrderBy(x => x.UpdatedAt, OrderByType.Desc)
            .ToListAsync();
    }

    /// <summary>
    /// 获取所有启用的定时任务
    /// </summary>
    public async Task<List<ScheduledTaskEntity>> GetAllEnabledAsync()
    {
        return await GetListAsync(x => x.IsEnabled);
    }

    /// <summary>
    /// 检查任务名称是否已存在
    /// </summary>
    public async Task<bool> ExistsByNameAndUsernameAsync(string name, string username, string? excludeTaskId = null)
    {
        if (string.IsNullOrEmpty(excludeTaskId))
        {
            return await IsAnyAsync(x => x.Name == name && x.Username == username);
        }
        
        return await IsAnyAsync(x => x.Name == name && x.Username == username && x.Id != excludeTaskId);
    }

    /// <summary>
    /// 更新任务的下次执行时间
    /// </summary>
    public async Task<bool> UpdateNextFireTimeAsync(string taskId, DateTime? nextFireTime)
    {
        return await GetDB().Updateable<ScheduledTaskEntity>()
            .SetColumns(x => x.NextFireTime == nextFireTime)
            .SetColumns(x => x.UpdatedAt == DateTime.Now)
            .Where(x => x.Id == taskId)
            .ExecuteCommandAsync() > 0;
    }

    /// <summary>
    /// 更新任务的执行状态
    /// </summary>
    public async Task<bool> UpdateExecutionStatusAsync(string taskId, DateTime? lastFireTime, string? lastExecutionStatus)
    {
        return await GetDB().Updateable<ScheduledTaskEntity>()
            .SetColumns(x => x.LastFireTime == lastFireTime)
            .SetColumns(x => x.LastExecutionStatus == lastExecutionStatus)
            .SetColumns(x => x.UpdatedAt == DateTime.Now)
            .Where(x => x.Id == taskId)
            .ExecuteCommandAsync() > 0;
    }
}
