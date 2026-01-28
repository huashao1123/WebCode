using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.ScheduledTask;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// Quartz 宿主服务 - 管理定时任务调度器
/// 注意：此服务在 Program.cs 中手动注册为 Singleton 和 HostedService
/// </summary>
public class QuartzHostedService : IHostedService
{
    private readonly ILogger<QuartzHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private IScheduler? _scheduler;
    private readonly TaskCompletionSource<bool> _startedTcs = new();
    
    public QuartzHostedService(
        ILogger<QuartzHostedService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    
    /// <summary>
    /// 获取调度器实例
    /// </summary>
    public IScheduler? Scheduler => _scheduler;
    
    /// <summary>
    /// 调度器是否已启动
    /// </summary>
    public bool IsStarted => _scheduler != null && _scheduler.IsStarted;
    
    /// <summary>
    /// 等待调度器启动完成
    /// </summary>
    public Task WaitForStartAsync(CancellationToken cancellationToken = default)
    {
        return _startedTcs.Task.WaitAsync(cancellationToken);
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Quartz 定时任务服务正在启动...");
        
        try
        {
            // 创建调度器工厂
            var schedulerFactory = new StdSchedulerFactory();
            _scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            
            // 设置 Job 工厂以支持依赖注入
            _scheduler.JobFactory = new ServiceProviderJobFactory(_serviceProvider);
            
            // 启动调度器
            await _scheduler.Start(cancellationToken);
            
            _logger.LogInformation("Quartz 调度器已启动");
            
            // 标记启动完成
            _startedTcs.TrySetResult(true);
            
            // 从数据库加载所有启用的定时任务
            await LoadScheduledTasksAsync(cancellationToken);
            
            _logger.LogInformation("Quartz 定时任务服务启动完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quartz 定时任务服务启动失败");
            _startedTcs.TrySetException(ex);
            throw;
        }
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Quartz 定时任务服务正在停止...");
        
        if (_scheduler != null)
        {
            await _scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken);
            _logger.LogInformation("Quartz 调度器已停止");
        }
    }
    
    /// <summary>
    /// 从数据库加载所有启用的定时任务
    /// </summary>
    private async Task LoadScheduledTasksAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var taskRepository = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
            
            var enabledTasks = await taskRepository.GetAllEnabledAsync();
            
            _logger.LogInformation("从数据库加载了 {Count} 个启用的定时任务", enabledTasks.Count);
            
            foreach (var task in enabledTasks)
            {
                try
                {
                    await ScheduleTaskAsync(task, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "加载定时任务失败: {TaskId} - {TaskName}", task.Id, task.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载定时任务列表失败");
        }
    }
    
    /// <summary>
    /// 调度一个任务
    /// </summary>
    public async Task ScheduleTaskAsync(ScheduledTaskEntity task, CancellationToken cancellationToken = default)
    {
        await EnsureSchedulerStartedAsync(cancellationToken);
        
        var jobKey = new JobKey(task.Id, "ScheduledTasks");
        var triggerKey = new TriggerKey(task.Id, "ScheduledTasks");
        
        // 如果任务已存在，先删除
        if (await _scheduler!.CheckExists(jobKey, cancellationToken))
        {
            await _scheduler.DeleteJob(jobKey, cancellationToken);
            _logger.LogDebug("已删除旧的任务调度: {TaskId}", task.Id);
        }
        
        // 创建 Job
        var job = JobBuilder.Create<CliTaskJob>()
            .WithIdentity(jobKey)
            .UsingJobData("TaskId", task.Id)
            .Build();
        
        // 解析 Cron 表达式并创建 Trigger
        ITrigger trigger;
        try
        {
            trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithCronSchedule(task.CronExpression, x => x.WithMisfireHandlingInstructionDoNothing())
                .Build();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "无效的 Cron 表达式: {CronExpression} (任务: {TaskId})", task.CronExpression, task.Id);
            throw new ArgumentException($"无效的 Cron 表达式: {task.CronExpression}", ex);
        }
        
        // 调度任务
        await _scheduler.ScheduleJob(job, trigger, cancellationToken);
        
        // 更新下次执行时间
        var nextFireTime = trigger.GetNextFireTimeUtc()?.LocalDateTime;
        
        using var scope = _serviceProvider.CreateScope();
        var taskRepository = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
        await taskRepository.UpdateNextFireTimeAsync(task.Id, nextFireTime);
        
        _logger.LogInformation("已调度定时任务: {TaskId} - {TaskName}, 下次执行: {NextFireTime}", 
            task.Id, task.Name, nextFireTime);
    }
    
    /// <summary>
    /// 取消调度一个任务
    /// </summary>
    public async Task UnscheduleTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        // 如果调度器未启动，直接返回（取消调度不需要等待）
        if (_scheduler == null)
        {
            _logger.LogWarning("调度器尚未初始化，跳过取消调度: {TaskId}", taskId);
            return;
        }
        
        var jobKey = new JobKey(taskId, "ScheduledTasks");
        
        if (await _scheduler.CheckExists(jobKey, cancellationToken))
        {
            await _scheduler.DeleteJob(jobKey, cancellationToken);
            _logger.LogInformation("已取消定时任务调度: {TaskId}", taskId);
        }
    }
    
    /// <summary>
    /// 立即触发一个任务
    /// </summary>
    public async Task TriggerTaskNowAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await EnsureSchedulerStartedAsync(cancellationToken);
        
        var jobKey = new JobKey(taskId, "ScheduledTasks");
        
        if (await _scheduler!.CheckExists(jobKey, cancellationToken))
        {
            await _scheduler.TriggerJob(jobKey, cancellationToken);
            _logger.LogInformation("已手动触发定时任务: {TaskId}", taskId);
        }
        else
        {
            // 如果任务不在调度器中，创建一个临时任务立即执行
            var job = JobBuilder.Create<CliTaskJob>()
                .WithIdentity(Guid.NewGuid().ToString(), "ManualTrigger")
                .UsingJobData("TaskId", taskId)
                .UsingJobData("TriggerType", TaskTriggerType.Manual)
                .Build();
            
            var trigger = TriggerBuilder.Create()
                .StartNow()
                .Build();
            
            await _scheduler.ScheduleJob(job, trigger, cancellationToken);
            _logger.LogInformation("已创建临时任务并立即执行: {TaskId}", taskId);
        }
    }
    
    /// <summary>
    /// 确保调度器已启动
    /// </summary>
    private async Task EnsureSchedulerStartedAsync(CancellationToken cancellationToken)
    {
        if (_scheduler != null) return;
        
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            await WaitForStartAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("调度器尚未初始化，等待超时");
        }
        
        if (_scheduler == null)
        {
            throw new InvalidOperationException("调度器尚未初始化");
        }
    }
    
    /// <summary>
    /// 获取任务的下次执行时间
    /// </summary>
    public async Task<DateTime?> GetNextFireTimeAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_scheduler == null)
        {
            return null;
        }
        
        var triggerKey = new TriggerKey(taskId, "ScheduledTasks");
        var trigger = await _scheduler.GetTrigger(triggerKey, cancellationToken);
        
        return trigger?.GetNextFireTimeUtc()?.LocalDateTime;
    }
    
    /// <summary>
    /// 验证 Cron 表达式是否有效
    /// </summary>
    public static bool ValidateCronExpression(string cronExpression, out string? errorMessage)
    {
        try
        {
            var cronSchedule = new CronExpression(cronExpression);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
    
    /// <summary>
    /// 获取 Cron 表达式的下N次执行时间
    /// </summary>
    public static List<DateTime> GetNextFireTimes(string cronExpression, int count = 5)
    {
        var result = new List<DateTime>();
        
        try
        {
            var cronSchedule = new CronExpression(cronExpression);
            var currentTime = DateTimeOffset.Now;
            
            for (int i = 0; i < count; i++)
            {
                var nextTime = cronSchedule.GetNextValidTimeAfter(currentTime);
                if (nextTime.HasValue)
                {
                    result.Add(nextTime.Value.LocalDateTime);
                    currentTime = nextTime.Value;
                }
                else
                {
                    break;
                }
            }
        }
        catch
        {
            // 忽略无效的 Cron 表达式
        }
        
        return result;
    }
}

/// <summary>
/// 支持依赖注入的 Job 工厂
/// </summary>
public class ServiceProviderJobFactory : IJobFactory
{
    private readonly IServiceProvider _serviceProvider;
    
    public ServiceProviderJobFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var jobType = bundle.JobDetail.JobType;
        
        // 创建一个新的作用域
        var scope = _serviceProvider.CreateScope();
        
        // 从作用域中获取 Job 实例
        var job = scope.ServiceProvider.GetService(jobType) as IJob;
        
        if (job == null)
        {
            // 如果服务容器中没有注册，使用 Activator 创建
            job = Activator.CreateInstance(jobType) as IJob;
        }
        
        if (job == null)
        {
            throw new SchedulerException($"无法创建 Job 实例: {jobType.FullName}");
        }
        
        // 如果 Job 实现了 IServiceScopeAware，设置作用域
        if (job is IServiceScopeAware scopeAware)
        {
            scopeAware.ServiceScope = scope;
        }
        
        return job;
    }
    
    public void ReturnJob(IJob job)
    {
        // 如果 Job 实现了 IServiceScopeAware，释放作用域
        if (job is IServiceScopeAware scopeAware)
        {
            scopeAware.ServiceScope?.Dispose();
        }
        
        // 如果 Job 实现了 IDisposable，释放资源
        if (job is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

/// <summary>
/// 用于在 Job 中访问服务作用域的接口
/// </summary>
public interface IServiceScopeAware
{
    IServiceScope? ServiceScope { get; set; }
}
