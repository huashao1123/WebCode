using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.ScheduledTask;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// CLI 任务 Job - 执行定时的 CLI 命令
/// </summary>
[ServiceDescription(typeof(CliTaskJob), ServiceLifetime.Transient)]
[DisallowConcurrentExecution] // 禁止同一任务并发执行
public class CliTaskJob : IJob, IServiceScopeAware
{
    private readonly ILogger<CliTaskJob> _logger;
    
    public IServiceScope? ServiceScope { get; set; }
    
    public CliTaskJob(ILogger<CliTaskJob> logger)
    {
        _logger = logger;
    }
    
    public async Task Execute(IJobExecutionContext context)
    {
        // 安全地获取 JobDataMap 中的值
        var jobDataMap = context.JobDetail.JobDataMap;
        var taskId = jobDataMap.ContainsKey("TaskId") ? jobDataMap["TaskId"]?.ToString() : null;
        var triggerType = jobDataMap.ContainsKey("TriggerType") ? jobDataMap["TriggerType"]?.ToString() ?? TaskTriggerType.Scheduled : TaskTriggerType.Scheduled;
        
        if (string.IsNullOrEmpty(taskId))
        {
            _logger.LogError("任务ID为空，无法执行。JobDataMap 内容: {Keys}", string.Join(", ", jobDataMap.Keys));
            return;
        }
        
        _logger.LogInformation("开始执行定时任务: {TaskId}, 触发类型: {TriggerType}", taskId, triggerType);
        
        // 获取服务
        var scope = ServiceScope ?? throw new InvalidOperationException("ServiceScope 未设置");
        var taskRepository = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
        var executionRepository = scope.ServiceProvider.GetRequiredService<ITaskExecutionRepository>();
        var cliExecutorService = scope.ServiceProvider.GetRequiredService<ICliExecutorService>();
        
        // 获取任务配置
        var task = await taskRepository.GetByIdAsync(taskId);
        if (task == null)
        {
            _logger.LogError("任务不存在: {TaskId}", taskId);
            return;
        }
        
        if (!task.IsEnabled)
        {
            _logger.LogWarning("任务已禁用，跳过执行: {TaskId} - {TaskName}", task.Id, task.Name);
            return;
        }
        
        // 创建执行记录
        var execution = new TaskExecutionEntity
        {
            TaskId = taskId,
            Status = TaskExecutionStatus.Running,
            StartTime = DateTime.Now,
            TriggerType = triggerType
        };
        
        // 使用 InsertReturnBigIdentityAsync 获取自增ID
        var executionId = await executionRepository.InsertReturnBigIdentityAsync(execution);
        if (executionId <= 0)
        {
            _logger.LogError("创建执行记录失败: {TaskId}", taskId);
            return;
        }
        
        _logger.LogDebug("创建执行记录成功: ExecutionId={ExecutionId}, TaskId={TaskId}", executionId, taskId);
        var outputBuilder = new StringBuilder();
        string? errorMessage = null;
        var finalStatus = TaskExecutionStatus.Success;
        
        try
        {
            // 创建超时取消令牌
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(task.TimeoutSeconds));
            
            // 确定使用的会话ID
            var sessionId = task.SessionId ?? $"scheduled-task-{taskId}";
            
            _logger.LogInformation("执行 CLI 任务: {TaskId} - {TaskName}, 会话: {SessionId}, 工具: {ToolId}", 
                task.Id, task.Name, sessionId, task.ToolId);
            
            // 调用 CLI 执行服务
            await foreach (var chunk in cliExecutorService.ExecuteStreamAsync(
                sessionId,
                task.ToolId,
                task.Prompt,
                timeoutCts.Token))
            {
                if (chunk.IsError)
                {
                    if (!string.IsNullOrEmpty(chunk.ErrorMessage))
                    {
                        errorMessage = chunk.ErrorMessage;
                        outputBuilder.AppendLine($"[ERROR] {chunk.ErrorMessage}");
                    }
                    
                    if (chunk.ErrorMessage?.Contains("超时") == true || chunk.ErrorMessage?.Contains("timeout") == true)
                    {
                        finalStatus = TaskExecutionStatus.Timeout;
                    }
                    else if (chunk.ErrorMessage?.Contains("取消") == true || chunk.ErrorMessage?.Contains("cancel") == true)
                    {
                        finalStatus = TaskExecutionStatus.Cancelled;
                    }
                    else
                    {
                        finalStatus = TaskExecutionStatus.Failed;
                    }
                }
                else if (!string.IsNullOrEmpty(chunk.Content))
                {
                    outputBuilder.Append(chunk.Content);
                }
                
                if (chunk.IsCompleted)
                {
                    break;
                }
            }
            
            // 如果没有错误，设置为成功
            if (finalStatus == TaskExecutionStatus.Success && !string.IsNullOrEmpty(errorMessage))
            {
                finalStatus = TaskExecutionStatus.Failed;
            }
        }
        catch (OperationCanceledException)
        {
            finalStatus = TaskExecutionStatus.Timeout;
            errorMessage = $"执行超时（{task.TimeoutSeconds} 秒）";
            outputBuilder.AppendLine($"[ERROR] {errorMessage}");
            _logger.LogWarning("任务执行超时: {TaskId} - {TaskName}", task.Id, task.Name);
        }
        catch (Exception ex)
        {
            finalStatus = TaskExecutionStatus.Failed;
            errorMessage = ex.Message;
            outputBuilder.AppendLine($"[ERROR] {ex.Message}");
            _logger.LogError(ex, "任务执行失败: {TaskId} - {TaskName}", task.Id, task.Name);
        }
        
        // 计算执行耗时
        var endTime = DateTime.Now;
        var durationMs = (int)(endTime - execution.StartTime).TotalMilliseconds;
        
        // 更新执行记录
        var output = outputBuilder.ToString();
        
        // 限制输出大小（最大5MB存储到数据库）
        const int maxOutputSize = 5 * 1024 * 1024;
        if (output.Length > maxOutputSize)
        {
            output = "... 输出过长，已截断 ...\n" + output.Substring(output.Length - maxOutputSize);
        }
        
        await executionRepository.UpdateStatusAsync(
            executionId,
            finalStatus,
            endTime,
            durationMs,
            output,
            errorMessage);
        
        // 更新任务的执行状态
        await taskRepository.UpdateExecutionStatusAsync(taskId, endTime, finalStatus);
        
        // 更新下次执行时间
        var nextFireTime = context.NextFireTimeUtc?.LocalDateTime;
        await taskRepository.UpdateNextFireTimeAsync(taskId, nextFireTime);
        
        _logger.LogInformation(
            "任务执行完成: {TaskId} - {TaskName}, 状态: {Status}, 耗时: {Duration}ms, 下次执行: {NextFireTime}", 
            task.Id, task.Name, finalStatus, durationMs, nextFireTime);
        
        // 如果失败且配置了重试，进行重试处理
        if (finalStatus == TaskExecutionStatus.Failed && task.MaxRetries > 0)
        {
            var currentRetryCount = execution.RetryCount;
            if (currentRetryCount < task.MaxRetries)
            {
                _logger.LogInformation("任务将重试: {TaskId} - {TaskName}, 当前重试次数: {RetryCount}/{MaxRetries}", 
                    task.Id, task.Name, currentRetryCount + 1, task.MaxRetries);
                
                // 创建重试执行记录
                var retryExecution = new TaskExecutionEntity
                {
                    TaskId = taskId,
                    Status = TaskExecutionStatus.Pending,
                    StartTime = DateTime.Now,
                    TriggerType = "Retry",
                    RetryCount = currentRetryCount + 1
                };
                
                await executionRepository.InsertAsync(retryExecution);
                
                // 延迟后重试（指数退避）
                var delaySeconds = Math.Pow(2, currentRetryCount) * 10; // 10s, 20s, 40s, 80s...
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                
                // 重新触发任务
                var quartzService = scope.ServiceProvider.GetService<QuartzHostedService>();
                if (quartzService != null)
                {
                    await quartzService.TriggerTaskNowAsync(taskId);
                }
            }
        }
        
        // 清理旧的执行记录（保留最近100条）
        try
        {
            var deletedCount = await executionRepository.CleanupOldExecutionsAsync(taskId, 100);
            if (deletedCount > 0)
            {
                _logger.LogDebug("清理了 {Count} 条旧执行记录: {TaskId}", deletedCount, taskId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理旧执行记录失败: {TaskId}", taskId);
        }
    }
}
