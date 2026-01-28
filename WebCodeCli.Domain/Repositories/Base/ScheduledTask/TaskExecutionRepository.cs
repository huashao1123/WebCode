using AntSK.Domain.Repositories.Base;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.ScheduledTask;

/// <summary>
/// 任务执行记录仓储实现
/// </summary>
[ServiceDescription(typeof(ITaskExecutionRepository), ServiceLifetime.Scoped)]
public class TaskExecutionRepository : Repository<TaskExecutionEntity>, ITaskExecutionRepository
{
    public TaskExecutionRepository(ISqlSugarClient context = null) : base(context)
    {
    }

    /// <summary>
    /// 根据任务ID获取执行记录（按开始时间降序）
    /// </summary>
    public async Task<List<TaskExecutionEntity>> GetByTaskIdAsync(string taskId, int limit = 50)
    {
        return await GetDB().Queryable<TaskExecutionEntity>()
            .Where(x => x.TaskId == taskId)
            .OrderBy(x => x.StartTime, OrderByType.Desc)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// 根据任务ID获取最近一次执行记录
    /// </summary>
    public async Task<TaskExecutionEntity?> GetLatestByTaskIdAsync(string taskId)
    {
        return await GetDB().Queryable<TaskExecutionEntity>()
            .Where(x => x.TaskId == taskId)
            .OrderBy(x => x.StartTime, OrderByType.Desc)
            .FirstAsync();
    }

    /// <summary>
    /// 根据任务ID删除所有执行记录
    /// </summary>
    public async Task<int> DeleteByTaskIdAsync(string taskId)
    {
        return await GetDB().Deleteable<TaskExecutionEntity>()
            .Where(x => x.TaskId == taskId)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 更新执行记录状态
    /// </summary>
    public async Task<bool> UpdateStatusAsync(long executionId, string status, DateTime? endTime, int? durationMs, string? output, string? errorMessage)
    {
        var updateable = GetDB().Updateable<TaskExecutionEntity>()
            .SetColumns(x => x.Status == status)
            .Where(x => x.Id == executionId);
        
        if (endTime.HasValue)
        {
            updateable = updateable.SetColumns(x => x.EndTime == endTime.Value);
        }
        
        if (durationMs.HasValue)
        {
            updateable = updateable.SetColumns(x => x.DurationMs == durationMs.Value);
        }
        
        if (output != null)
        {
            updateable = updateable.SetColumns(x => x.Output == output);
        }
        
        if (errorMessage != null)
        {
            updateable = updateable.SetColumns(x => x.ErrorMessage == errorMessage);
        }
        
        return await updateable.ExecuteCommandAsync() > 0;
    }

    /// <summary>
    /// 追加输出内容
    /// </summary>
    public async Task<bool> AppendOutputAsync(long executionId, string outputChunk)
    {
        // 先获取当前输出
        var execution = await GetByIdAsync(executionId);
        if (execution == null) return false;
        
        var newOutput = (execution.Output ?? "") + outputChunk;
        
        // 限制输出大小（最大10MB）
        const int maxOutputSize = 10 * 1024 * 1024;
        if (newOutput.Length > maxOutputSize)
        {
            newOutput = newOutput.Substring(newOutput.Length - maxOutputSize);
        }
        
        return await GetDB().Updateable<TaskExecutionEntity>()
            .SetColumns(x => x.Output == newOutput)
            .Where(x => x.Id == executionId)
            .ExecuteCommandAsync() > 0;
    }

    /// <summary>
    /// 获取正在运行的执行记录
    /// </summary>
    public async Task<List<TaskExecutionEntity>> GetRunningExecutionsAsync()
    {
        return await GetListAsync(x => x.Status == TaskExecutionStatus.Running);
    }

    /// <summary>
    /// 清理过期的执行记录（保留最近N条）
    /// </summary>
    public async Task<int> CleanupOldExecutionsAsync(string taskId, int keepCount)
    {
        // 获取需要保留的记录ID
        var keepIds = await GetDB().Queryable<TaskExecutionEntity>()
            .Where(x => x.TaskId == taskId)
            .OrderBy(x => x.StartTime, OrderByType.Desc)
            .Take(keepCount)
            .Select(x => x.Id)
            .ToListAsync();
        
        if (keepIds.Count == 0)
        {
            return 0;
        }
        
        // 删除不在保留列表中的记录
        return await GetDB().Deleteable<TaskExecutionEntity>()
            .Where(x => x.TaskId == taskId && !keepIds.Contains(x.Id))
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 根据时间范围获取执行记录
    /// </summary>
    public async Task<List<TaskExecutionEntity>> GetByTimeRangeAsync(string taskId, DateTime startTime, DateTime endTime)
    {
        return await GetDB().Queryable<TaskExecutionEntity>()
            .Where(x => x.TaskId == taskId && x.StartTime >= startTime && x.StartTime <= endTime)
            .OrderBy(x => x.StartTime, OrderByType.Desc)
            .ToListAsync();
    }
}
