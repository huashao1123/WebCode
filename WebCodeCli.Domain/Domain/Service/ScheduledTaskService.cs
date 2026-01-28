using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.ScheduledTask;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 定时任务服务实现
/// </summary>
[ServiceDescription(typeof(IScheduledTaskService), ServiceLifetime.Scoped)]
public class ScheduledTaskService : IScheduledTaskService
{
    private readonly ILogger<ScheduledTaskService> _logger;
    private readonly IScheduledTaskRepository _taskRepository;
    private readonly ITaskExecutionRepository _executionRepository;
    private readonly IServiceProvider _serviceProvider;
    
    public ScheduledTaskService(
        ILogger<ScheduledTaskService> logger,
        IScheduledTaskRepository taskRepository,
        ITaskExecutionRepository executionRepository,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _taskRepository = taskRepository;
        _executionRepository = executionRepository;
        _serviceProvider = serviceProvider;
    }
    
    #region 任务管理
    
    public async Task<List<ScheduledTaskEntity>> GetTasksAsync(string username)
    {
        return await _taskRepository.GetByUsernameOrderByUpdatedAtAsync(username);
    }
    
    public async Task<ScheduledTaskEntity?> GetTaskByIdAsync(string taskId, string username)
    {
        return await _taskRepository.GetByIdAndUsernameAsync(taskId, username);
    }
    
    public async Task<(bool Success, string? ErrorMessage, ScheduledTaskEntity? Task)> CreateTaskAsync(
        string username,
        string name,
        string? description,
        string cronExpression,
        string toolId,
        string prompt,
        string? sessionId,
        int timeoutSeconds = 3600,
        int maxRetries = 0,
        bool isEnabled = true)
    {
        // 验证任务名称
        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "任务名称不能为空", null);
        }
        
        // 检查名称是否已存在
        if (await _taskRepository.ExistsByNameAndUsernameAsync(name, username))
        {
            return (false, $"任务名称 '{name}' 已存在", null);
        }
        
        // 验证 Cron 表达式
        var (isValid, errorMessage) = ValidateCronExpression(cronExpression);
        if (!isValid)
        {
            return (false, $"无效的 Cron 表达式: {errorMessage}", null);
        }
        
        // 验证工具ID
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return (false, "必须选择一个 CLI 工具", null);
        }
        
        // 验证提示词
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return (false, "执行提示词不能为空", null);
        }
        
        // 创建任务实体
        var task = new ScheduledTaskEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = username,
            Name = name.Trim(),
            Description = description?.Trim(),
            CronExpression = cronExpression.Trim(),
            ToolId = toolId,
            Prompt = prompt,
            SessionId = sessionId,
            TimeoutSeconds = Math.Max(60, timeoutSeconds), // 最小60秒
            MaxRetries = Math.Max(0, maxRetries),
            IsEnabled = isEnabled,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        
        // 保存到数据库
        var insertResult = await _taskRepository.InsertAsync(task);
        if (!insertResult)
        {
            return (false, "创建任务失败", null);
        }
        
        _logger.LogInformation("创建定时任务: {TaskId} - {TaskName}", task.Id, task.Name);
        
        // 如果任务启用，添加到调度器
        if (isEnabled)
        {
            try
            {
                var quartzService = _serviceProvider.GetService<QuartzHostedService>();
                if (quartzService != null)
                {
                    await quartzService.ScheduleTaskAsync(task);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "将任务添加到调度器失败: {TaskId}", task.Id);
                // 不回滚任务创建，只记录错误
            }
        }
        
        return (true, null, task);
    }
    
    public async Task<(bool Success, string? ErrorMessage)> UpdateTaskAsync(
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
        bool isEnabled)
    {
        // 获取现有任务
        var task = await _taskRepository.GetByIdAndUsernameAsync(taskId, username);
        if (task == null)
        {
            return (false, "任务不存在");
        }
        
        // 验证任务名称
        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "任务名称不能为空");
        }
        
        // 检查名称是否已存在（排除当前任务）
        if (await _taskRepository.ExistsByNameAndUsernameAsync(name, username, taskId))
        {
            return (false, $"任务名称 '{name}' 已存在");
        }
        
        // 验证 Cron 表达式
        var (isValid, errorMessage) = ValidateCronExpression(cronExpression);
        if (!isValid)
        {
            return (false, $"无效的 Cron 表达式: {errorMessage}");
        }
        
        // 验证工具ID
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return (false, "必须选择一个 CLI 工具");
        }
        
        // 验证提示词
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return (false, "执行提示词不能为空");
        }
        
        // 更新任务属性
        task.Name = name.Trim();
        task.Description = description?.Trim();
        task.CronExpression = cronExpression.Trim();
        task.ToolId = toolId;
        task.Prompt = prompt;
        task.SessionId = sessionId;
        task.TimeoutSeconds = Math.Max(60, timeoutSeconds);
        task.MaxRetries = Math.Max(0, maxRetries);
        task.IsEnabled = isEnabled;
        task.UpdatedAt = DateTime.Now;
        
        // 保存到数据库
        var updateResult = await _taskRepository.UpdateAsync(task);
        if (!updateResult)
        {
            return (false, "更新任务失败");
        }
        
        _logger.LogInformation("更新定时任务: {TaskId} - {TaskName}", task.Id, task.Name);
        
        // 更新调度器中的任务
        try
        {
            var quartzService = _serviceProvider.GetService<QuartzHostedService>();
            if (quartzService != null)
            {
                if (isEnabled)
                {
                    await quartzService.ScheduleTaskAsync(task);
                }
                else
                {
                    await quartzService.UnscheduleTaskAsync(taskId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新调度器中的任务失败: {TaskId}", task.Id);
        }
        
        return (true, null);
    }
    
    public async Task<(bool Success, string? ErrorMessage)> DeleteTaskAsync(string taskId, string username, bool deleteExecutions = true)
    {
        // 获取现有任务
        var task = await _taskRepository.GetByIdAndUsernameAsync(taskId, username);
        if (task == null)
        {
            return (false, "任务不存在");
        }
        
        // 从调度器中移除
        try
        {
            var quartzService = _serviceProvider.GetService<QuartzHostedService>();
            if (quartzService != null)
            {
                await quartzService.UnscheduleTaskAsync(taskId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从调度器移除任务失败: {TaskId}", taskId);
        }
        
        // 删除执行记录
        if (deleteExecutions)
        {
            await _executionRepository.DeleteByTaskIdAsync(taskId);
        }
        
        // 删除任务
        var deleteResult = await _taskRepository.DeleteByIdAndUsernameAsync(taskId, username);
        if (!deleteResult)
        {
            return (false, "删除任务失败");
        }
        
        _logger.LogInformation("删除定时任务: {TaskId} - {TaskName}", task.Id, task.Name);
        
        return (true, null);
    }
    
    public async Task<(bool Success, string? ErrorMessage)> ToggleTaskAsync(string taskId, string username, bool isEnabled)
    {
        // 获取现有任务
        var task = await _taskRepository.GetByIdAndUsernameAsync(taskId, username);
        if (task == null)
        {
            return (false, "任务不存在");
        }
        
        if (task.IsEnabled == isEnabled)
        {
            return (true, null); // 状态未变
        }
        
        // 更新状态
        task.IsEnabled = isEnabled;
        task.UpdatedAt = DateTime.Now;
        
        var updateResult = await _taskRepository.UpdateAsync(task);
        if (!updateResult)
        {
            return (false, "更新任务状态失败");
        }
        
        // 更新调度器
        try
        {
            var quartzService = _serviceProvider.GetService<QuartzHostedService>();
            if (quartzService != null)
            {
                if (isEnabled)
                {
                    await quartzService.ScheduleTaskAsync(task);
                }
                else
                {
                    await quartzService.UnscheduleTaskAsync(taskId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新调度器中的任务状态失败: {TaskId}", taskId);
        }
        
        _logger.LogInformation("切换定时任务状态: {TaskId} - {TaskName}, 启用: {IsEnabled}", task.Id, task.Name, isEnabled);
        
        return (true, null);
    }
    
    #endregion
    
    #region 任务执行
    
    public async Task<(bool Success, string? ErrorMessage, long? ExecutionId)> TriggerTaskAsync(string taskId, string username)
    {
        // 获取任务
        var task = await _taskRepository.GetByIdAndUsernameAsync(taskId, username);
        if (task == null)
        {
            return (false, "任务不存在", null);
        }
        
        // 创建执行记录
        var execution = new TaskExecutionEntity
        {
            TaskId = taskId,
            Status = TaskExecutionStatus.Pending,
            StartTime = DateTime.Now,
            TriggerType = TaskTriggerType.Manual
        };
        
        var insertResult = await _executionRepository.InsertAsync(execution);
        if (!insertResult)
        {
            return (false, "创建执行记录失败", null);
        }
        
        _logger.LogInformation("手动触发定时任务: {TaskId} - {TaskName}, 执行ID: {ExecutionId}", task.Id, task.Name, execution.Id);
        
        // 触发任务执行
        try
        {
            var quartzService = _serviceProvider.GetService<QuartzHostedService>();
            if (quartzService != null)
            {
                await quartzService.TriggerTaskNowAsync(taskId);
            }
            else
            {
                return (false, "调度服务不可用", execution.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发任务执行失败: {TaskId}", taskId);
            
            // 更新执行记录状态为失败
            await _executionRepository.UpdateStatusAsync(
                execution.Id, 
                TaskExecutionStatus.Failed, 
                DateTime.Now, 
                0, 
                null, 
                ex.Message);
            
            return (false, $"触发任务执行失败: {ex.Message}", execution.Id);
        }
        
        return (true, null, execution.Id);
    }
    
    public async Task<(bool Success, string? ErrorMessage)> CancelExecutionAsync(long executionId)
    {
        var execution = await _executionRepository.GetByIdAsync(executionId);
        if (execution == null)
        {
            return (false, "执行记录不存在");
        }
        
        if (execution.Status != TaskExecutionStatus.Running && execution.Status != TaskExecutionStatus.Pending)
        {
            return (false, "只能取消正在执行或等待中的任务");
        }
        
        // 更新状态为已取消
        await _executionRepository.UpdateStatusAsync(
            executionId, 
            TaskExecutionStatus.Cancelled, 
            DateTime.Now, 
            (int)(DateTime.Now - execution.StartTime).TotalMilliseconds, 
            null, 
            "用户取消");
        
        _logger.LogInformation("取消任务执行: {ExecutionId}", executionId);
        
        return (true, null);
    }
    
    #endregion
    
    #region 执行记录
    
    public async Task<List<TaskExecutionEntity>> GetExecutionsAsync(string taskId, string username, int limit = 50)
    {
        // 验证任务归属
        var task = await _taskRepository.GetByIdAndUsernameAsync(taskId, username);
        if (task == null)
        {
            return new List<TaskExecutionEntity>();
        }
        
        return await _executionRepository.GetByTaskIdAsync(taskId, limit);
    }
    
    public async Task<TaskExecutionEntity?> GetExecutionByIdAsync(long executionId)
    {
        return await _executionRepository.GetByIdAsync(executionId);
    }
    
    public async Task<int> CleanupExecutionsAsync(string taskId, string username, int keepCount = 100)
    {
        // 验证任务归属
        var task = await _taskRepository.GetByIdAndUsernameAsync(taskId, username);
        if (task == null)
        {
            return 0;
        }
        
        return await _executionRepository.CleanupOldExecutionsAsync(taskId, keepCount);
    }
    
    #endregion
    
    #region Cron 表达式
    
    public (bool IsValid, string? ErrorMessage) ValidateCronExpression(string cronExpression)
    {
        return QuartzHostedService.ValidateCronExpression(cronExpression, out var errorMessage) 
            ? (true, null) 
            : (false, errorMessage);
    }
    
    public List<DateTime> GetNextFireTimes(string cronExpression, int count = 5)
    {
        return QuartzHostedService.GetNextFireTimes(cronExpression, count);
    }
    
    public List<CronPreset> GetCronPresets()
    {
        return new List<CronPreset>
        {
            new() { Name = "每分钟", Expression = "0 * * * * ?", Description = "每分钟执行一次" },
            new() { Name = "每5分钟", Expression = "0 */5 * * * ?", Description = "每5分钟执行一次" },
            new() { Name = "每15分钟", Expression = "0 */15 * * * ?", Description = "每15分钟执行一次" },
            new() { Name = "每30分钟", Expression = "0 */30 * * * ?", Description = "每30分钟执行一次" },
            new() { Name = "每小时", Expression = "0 0 * * * ?", Description = "每小时整点执行" },
            new() { Name = "每2小时", Expression = "0 0 */2 * * ?", Description = "每2小时执行一次" },
            new() { Name = "每天凌晨", Expression = "0 0 0 * * ?", Description = "每天凌晨0点执行" },
            new() { Name = "每天早上9点", Expression = "0 0 9 * * ?", Description = "每天早上9点执行" },
            new() { Name = "每天中午12点", Expression = "0 0 12 * * ?", Description = "每天中午12点执行" },
            new() { Name = "每天晚上6点", Expression = "0 0 18 * * ?", Description = "每天晚上6点执行" },
            new() { Name = "工作日早上9点", Expression = "0 0 9 ? * MON-FRI", Description = "周一至周五早上9点执行" },
            new() { Name = "每周一", Expression = "0 0 0 ? * MON", Description = "每周一凌晨执行" },
            new() { Name = "每月1号", Expression = "0 0 0 1 * ?", Description = "每月1号凌晨执行" },
            new() { Name = "每月15号", Expression = "0 0 0 15 * ?", Description = "每月15号凌晨执行" }
        };
    }
    
    #endregion
}
