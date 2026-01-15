using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 开发服务器管理服务实现
/// </summary>
[ServiceDescription(typeof(IDevServerManager), ServiceLifetime.Singleton)]
public class DevServerManager : IDevServerManager, IDisposable
{
    private readonly ILogger<DevServerManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, DevServerInfo> _servers = new();
    private readonly ConcurrentDictionary<string, Process> _processes = new();
    private readonly SemaphoreSlim _portAllocationLock = new(1, 1);
    private readonly int _portRangeStart;
    private readonly int _portRangeEnd;
    private readonly int _maxConcurrentServers;
    private readonly int _serverStartupTimeout;
    private bool _disposed;

    public DevServerManager(
        ILogger<DevServerManager> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _portRangeStart = configuration.GetValue<int>("FrontendPreview:PortRangeStart", 40000);
        _portRangeEnd = configuration.GetValue<int>("FrontendPreview:PortRangeEnd", 50000);
        _maxConcurrentServers = configuration.GetValue<int>("FrontendPreview:MaxConcurrentServers", 10);
        _serverStartupTimeout = configuration.GetValue<int>("FrontendPreview:ServerStartupTimeout", 120);
    }

    public async Task<DevServerInfo> StartDevServerAsync(string sessionId, FrontendProjectInfo projectInfo)
    {
        // 处理项目Key，将"."替换为"root"以避免路径问题
        var projectKey = string.IsNullOrEmpty(projectInfo.Key) || projectInfo.Key == "." 
            ? "root" 
            : projectInfo.Key.Replace("/", "_").Replace("\\", "_");
        
        var serverKey = $"{sessionId}_{projectKey}";

        // 检查是否已存在
        if (_servers.TryGetValue(serverKey, out var existingServer))
        {
            if (existingServer.Status == DevServerStatus.Running)
            {
                _logger.LogInformation("服务器已在运行: {ServerKey}", serverKey);
                return existingServer;
            }
        }

        // 检查并发限制
        if (_servers.Count >= _maxConcurrentServers)
        {
            throw new InvalidOperationException($"已达到最大并发服务器数量限制 ({_maxConcurrentServers})");
        }

        var serverInfo = new DevServerInfo
        {
            ServerKey = serverKey,
            SessionId = sessionId,
            ProjectInfo = projectInfo,
            Status = DevServerStatus.Starting,
            IsBuildMode = false
        };

        _servers[serverKey] = serverInfo;

        try
        {
            // 检查是否需要安装依赖
            if (projectInfo.NeedsDependencyInstall)
            {
                serverInfo.Status = DevServerStatus.Installing;
                await InstallDependenciesAsync(serverInfo);
            }

            // 分配端口
            var port = await AllocatePortAsync();
            serverInfo.Port = port;
            // 末尾的 "/" 很关键：确保路由能命中 PreviewProxyController 的 catch-all 规则
            serverInfo.ProxyUrl = $"/api/preview/{sessionId}/{projectKey}/";

            // 启动开发服务器
            await StartProcessAsync(serverInfo, projectInfo.DevCommand);

            // 等待服务器就绪
            await WaitForServerReadyAsync(serverInfo);

            serverInfo.Status = DevServerStatus.Running;
            serverInfo.StartedAt = DateTime.Now;

            _logger.LogInformation("开发服务器启动成功: {ServerKey} on port {Port}", serverKey, port);
            return serverInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动开发服务器失败: {ServerKey}", serverKey);
            serverInfo.Status = DevServerStatus.Failed;
            serverInfo.ErrorMessage = ex.Message;
            
            // 清理失败的服务器
            await CleanupServerAsync(serverKey);
            
            throw;
        }
    }

    public async Task<DevServerInfo> StartBuildPreviewAsync(string sessionId, FrontendProjectInfo projectInfo)
    {
        // 处理项目Key，将"."替换为"root"以避免路径问题
        var projectKey = string.IsNullOrEmpty(projectInfo.Key) || projectInfo.Key == "." 
            ? "root" 
            : projectInfo.Key.Replace("/", "_").Replace("\\", "_");
        
        var serverKey = $"{sessionId}_{projectKey}_build";

        var serverInfo = new DevServerInfo
        {
            ServerKey = serverKey,
            SessionId = sessionId,
            ProjectInfo = projectInfo,
            Status = DevServerStatus.Building,
            IsBuildMode = true
        };

        _servers[serverKey] = serverInfo;

        try
        {
            // 检查是否需要安装依赖
            if (projectInfo.NeedsDependencyInstall)
            {
                serverInfo.Status = DevServerStatus.Installing;
                await InstallDependenciesAsync(serverInfo);
            }

            // 执行构建
            serverInfo.Status = DevServerStatus.Building;
            await BuildProjectAsync(serverInfo);

            // 分配端口并启动静态文件服务器
            var port = await AllocatePortAsync();
            serverInfo.Port = port;
            // 末尾的 "/" 很关键：确保路由能命中 PreviewProxyController 的 catch-all 规则
            serverInfo.ProxyUrl = $"/api/preview/{sessionId}/{projectKey}_build/";

            // 启动静态文件服务器（使用简单的 http-server 或类似工具）
            await StartStaticServerAsync(serverInfo);

            serverInfo.Status = DevServerStatus.Running;
            serverInfo.StartedAt = DateTime.Now;

            _logger.LogInformation("构建预览服务器启动成功: {ServerKey} on port {Port}", serverKey, port);
            return serverInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动构建预览失败: {ServerKey}", serverKey);
            serverInfo.Status = DevServerStatus.Failed;
            serverInfo.ErrorMessage = ex.Message;
            
            await CleanupServerAsync(serverKey);
            throw;
        }
    }

    public async Task StopDevServerAsync(string sessionId, string serverKey)
    {
        // 如果serverKey不包含sessionId，则组合完整key
        var fullServerKey = serverKey.StartsWith(sessionId) 
            ? serverKey 
            : $"{sessionId}_{serverKey}";
            
        if (!_servers.TryGetValue(fullServerKey, out var serverInfo))
        {
            _logger.LogWarning("服务器不存在: {ServerKey}", fullServerKey);
            return;
        }

        await CleanupServerAsync(fullServerKey);
        _logger.LogInformation("服务器已停止: {ServerKey}", fullServerKey);
    }

    public Task<DevServerInfo?> GetServerInfoAsync(string sessionId, string serverKey)
    {
        // 如果serverKey不包含sessionId，则组合完整key
        var fullServerKey = serverKey.StartsWith(sessionId) 
            ? serverKey 
            : $"{sessionId}_{serverKey}";
            
        _servers.TryGetValue(fullServerKey, out var serverInfo);
        return Task.FromResult(serverInfo);
    }

    public List<DevServerInfo> GetSessionServers(string sessionId)
    {
        return _servers.Values
            .Where(s => s.SessionId == sessionId)
            .ToList();
    }

    public async Task StopAllSessionServersAsync(string sessionId)
    {
        var sessionServers = GetSessionServers(sessionId);
        
        foreach (var server in sessionServers)
        {
            await StopDevServerAsync(sessionId, server.ServerKey);
        }

        _logger.LogInformation("已停止会话的所有服务器: {SessionId}, 数量: {Count}", sessionId, sessionServers.Count);
    }

    public async Task<bool> IsPortAvailableAsync(int port)
    {
        try
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = ipGlobalProperties.GetActiveTcpConnections();
            var tcpListeners = ipGlobalProperties.GetActiveTcpListeners();

            if (tcpConnections.Any(c => c.LocalEndPoint.Port == port))
                return false;

            if (tcpListeners.Any(l => l.Port == port))
                return false;

            // 双重检查：尝试绑定端口
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> AllocatePortAsync()
    {
        await _portAllocationLock.WaitAsync();
        try
        {
            for (int port = _portRangeStart; port <= _portRangeEnd; port++)
            {
                // 检查是否已被分配
                if (_servers.Values.Any(s => s.Port == port))
                    continue;

                // 检查端口是否可用
                if (await IsPortAvailableAsync(port))
                {
                    _logger.LogDebug("分配端口: {Port}", port);
                    return port;
                }
            }

            throw new InvalidOperationException($"无可用端口（范围: {_portRangeStart}-{_portRangeEnd}）");
        }
        finally
        {
            _portAllocationLock.Release();
        }
    }

    private async Task InstallDependenciesAsync(DevServerInfo serverInfo)
    {
        var projectPath = serverInfo.ProjectInfo.AbsolutePath;
        var packageManager = serverInfo.ProjectInfo.PackageManager;

        _logger.LogInformation("开始安装依赖: {ProjectPath}, 包管理器: {PackageManager}", 
            projectPath, packageManager);

        var installCommand = packageManager switch
        {
            "yarn" => "yarn install",
            "pnpm" => "pnpm install",
            _ => "npm install"
        };

        var process = CreateProcess(projectPath, packageManager, "install");
        
        var outputBuilder = new StringBuilder();
        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                AddLog(serverInfo, args.Data);
                outputBuilder.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                AddLog(serverInfo, $"[ERROR] {args.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"依赖安装失败: {outputBuilder}");
        }

        _logger.LogInformation("依赖安装完成: {ProjectPath}", projectPath);
    }

    private async Task BuildProjectAsync(DevServerInfo serverInfo)
    {
        var projectPath = serverInfo.ProjectInfo.AbsolutePath;
        var packageManager = serverInfo.ProjectInfo.PackageManager;
        var buildCommand = serverInfo.ProjectInfo.BuildCommand;

        _logger.LogInformation("开始构建项目: {ProjectPath}", projectPath);

        var process = CreateProcess(projectPath, packageManager, $"run {buildCommand}");
        
        var outputBuilder = new StringBuilder();
        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                AddLog(serverInfo, args.Data);
                outputBuilder.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                AddLog(serverInfo, $"[ERROR] {args.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"项目构建失败: {outputBuilder}");
        }

        _logger.LogInformation("项目构建完成: {ProjectPath}", projectPath);
    }

    private async Task StartProcessAsync(DevServerInfo serverInfo, string command)
    {
        var projectPath = serverInfo.ProjectInfo.AbsolutePath;
        var packageManager = serverInfo.ProjectInfo.PackageManager;
        var port = serverInfo.Port;

        _logger.LogInformation("启动进程: {Command} in {ProjectPath} on port {Port}", 
            command, projectPath, port);

        // 根据项目类型修改启动命令以包含端口参数
        var fullCommand = command;
        var projectType = serverInfo.ProjectInfo.Type;
        
        if (projectType == FrontendProjectType.VueVite || projectType == FrontendProjectType.ReactVite)
        {
            // Vite 项目需要通过命令行参数指定端口
            fullCommand = $"{command} -- --port {port} --host 0.0.0.0";
        }
        else if (projectType == FrontendProjectType.NextJs)
        {
            fullCommand = $"{command} -- -p {port}";
        }
        else if (projectType == FrontendProjectType.NuxtJs)
        {
            fullCommand = $"{command} -- --port {port}";
        }

        var process = CreateProcess(projectPath, packageManager, $"run {fullCommand}", port);

        var serverReady = false;
        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                AddLog(serverInfo, args.Data);
                
                // 检测服务器就绪的标志
                if (args.Data.Contains("Local:") || 
                    args.Data.Contains("ready in") ||
                    args.Data.Contains("started server on") ||
                    args.Data.Contains("Listening on"))
                {
                    serverReady = true;
                }
            }
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                AddLog(serverInfo, $"[ERROR] {args.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        serverInfo.ProcessId = process.Id;
        _processes[serverInfo.ServerKey] = process;

        _logger.LogInformation("进程已启动: PID={ProcessId}", process.Id);
        
        // 等待服务器就绪信号（从日志中检测）或超时
        var waitTime = 0;
        var maxWaitTime = 30000; // 30秒
        while (!serverReady && waitTime < maxWaitTime && !process.HasExited)
        {
            await Task.Delay(500);
            waitTime += 500;
        }
        
        if (process.HasExited)
        {
            throw new Exception($"进程意外退出，退出码: {process.ExitCode}");
        }
        
        if (!serverReady)
        {
            _logger.LogWarning("未检测到服务器就绪信号，尝试连接端口...");
        }
    }

    private async Task StartStaticServerAsync(DevServerInfo serverInfo)
    {
        var projectPath = serverInfo.ProjectInfo.AbsolutePath;
        var buildOutputDir = serverInfo.ProjectInfo.BuildOutputDir;
        var outputPath = Path.Combine(projectPath, buildOutputDir);
        var port = serverInfo.Port;

        if (!Directory.Exists(outputPath))
        {
            throw new DirectoryNotFoundException($"构建输出目录不存在: {outputPath}");
        }

        _logger.LogInformation("启动静态文件服务器: {OutputPath} on port {Port}", outputPath, port);

        Process process;
        
        // 方案1：尝试使用 npx http-server（更稳定）
        try
        {
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = outputPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = $"/c npx --yes http-server -p {port} --cors";
            }
            else
            {
                startInfo.FileName = "npx";
                startInfo.Arguments = $"--yes http-server -p {port} --cors";
            }

            process = new Process { StartInfo = startInfo };
            _logger.LogInformation("使用 http-server 启动静态服务器");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "http-server 不可用，尝试使用 Python");
            
            // 方案2：使用 Python HTTP 服务器
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"-m http.server {port}",
                    WorkingDirectory = outputPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            _logger.LogInformation("使用 Python HTTP 服务器");
        }

        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                AddLog(serverInfo, args.Data);
                _logger.LogInformation("[Static Server Output] {Output}", args.Data);
            }
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                AddLog(serverInfo, $"[ERROR] {args.Data}");
                _logger.LogWarning("[Static Server Error] {Error}", args.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            serverInfo.ProcessId = process.Id;
            _processes[serverInfo.ServerKey] = process;

            _logger.LogInformation("静态服务器进程已启动: PID={ProcessId}", process.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动静态服务器进程失败");
            throw new Exception($"启动静态服务器进程失败: {ex.Message}", ex);
        }

        // 等待服务器启动
        await Task.Delay(3000); // 增加等待时间到3秒
        
        // 检查进程是否还在运行
        if (process.HasExited)
        {
            var exitCode = process.ExitCode;
            var logs = string.Join("\n", serverInfo.RecentLogs.TakeLast(10));
            throw new Exception($"静态文件服务器启动后立即退出，退出码: {exitCode}\n最近日志:\n{logs}");
        }

        // 验证端口是否可访问
        var isReady = await IsPortAccessibleAsync(port);
        if (!isReady)
        {
            var logs = string.Join("\n", serverInfo.RecentLogs.TakeLast(10));
            throw new Exception($"静态文件服务器端口 {port} 无法访问\n最近日志:\n{logs}");
        }

        _logger.LogInformation("静态文件服务器验证成功: port {Port}", port);
    }

    private async Task<bool> IsPortAccessibleAsync(int port)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await httpClient.GetAsync($"http://localhost:{port}/");
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch
        {
            return false;
        }
    }

    private Process CreateProcess(string workingDirectory, string packageManager, string arguments, int? port = null)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // 设置环境变量
        if (port.HasValue)
        {
            startInfo.EnvironmentVariables["PORT"] = port.Value.ToString();
            startInfo.EnvironmentVariables["VITE_PORT"] = port.Value.ToString();
        }

        // 根据操作系统设置命令
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/c {packageManager} {arguments}";
        }
        else
        {
            startInfo.FileName = packageManager;
            startInfo.Arguments = arguments;
        }

        return new Process { StartInfo = startInfo };
    }

    private async Task WaitForServerReadyAsync(DevServerInfo serverInfo)
    {
        var maxAttempts = 20; // 最多尝试20次，每次1秒 = 20秒
        var port = serverInfo.Port;

        _logger.LogInformation("等待服务器就绪: {ServerKey}, 端口: {Port}", serverInfo.ServerKey, port);

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = await client.GetAsync($"http://localhost:{port}");
                
                if (response.IsSuccessStatusCode || 
                    response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                    response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
                {
                    _logger.LogInformation("服务器已就绪: {ServerKey}, 尝试次数: {Attempts}", serverInfo.ServerKey, i + 1);
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // 连接失败，继续等待
            }
            catch (TaskCanceledException)
            {
                // 超时，继续等待
            }

            if (i < maxAttempts - 1)
            {
                await Task.Delay(1000);
            }
        }

        // 超时后记录警告但不抛出异常，因为有些服务器可能需要更长时间
        _logger.LogWarning("服务器响应超时，但进程仍在运行: {ServerKey}", serverInfo.ServerKey);
    }

    private void AddLog(DevServerInfo serverInfo, string message)
    {
        lock (serverInfo.RecentLogs)
        {
            serverInfo.RecentLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            
            // 只保留最近100条日志
            if (serverInfo.RecentLogs.Count > 100)
            {
                serverInfo.RecentLogs.RemoveAt(0);
            }
        }
    }

    private async Task CleanupServerAsync(string serverKey)
    {
        _servers.TryRemove(serverKey, out _);

        if (_processes.TryRemove(serverKey, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true); // 杀死进程树
                    await process.WaitForExitAsync();
                }
                process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理进程失败: {ServerKey}", serverKey);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 停止所有服务器
        foreach (var serverKey in _servers.Keys.ToList())
        {
            CleanupServerAsync(serverKey).Wait();
        }

        _portAllocationLock.Dispose();
    }
}

