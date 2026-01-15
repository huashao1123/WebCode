using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 文件监控服务实现
/// </summary>
[ServiceDescription(typeof(IFileMonitorService), ServiceLifetime.Singleton)]
public class FileMonitorService : IFileMonitorService, IDisposable
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, List<FileChangeEvent>> _changeHistory = new();
    private readonly object _lockObject = new();
    
    /// <summary>
    /// 文件变更事件
    /// </summary>
    public event EventHandler<FileChangeEvent>? OnFileChanged;
    
    /// <summary>
    /// 开始监控指定会话的工作区
    /// </summary>
    public void StartMonitoring(string sessionId, string workspacePath)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(workspacePath))
        {
            return;
        }
        
        // 如果已经在监控，先停止
        StopMonitoring(sessionId);
        
        try
        {
            if (!Directory.Exists(workspacePath))
            {
                Console.WriteLine($"工作区路径不存在: {workspacePath}");
                return;
            }
            
            var watcher = new FileSystemWatcher(workspacePath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | 
                               NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };
            
            // 订阅事件
            watcher.Created += (sender, e) => OnFileSystemEvent(sessionId, e.FullPath, FileChangeType.Created);
            watcher.Changed += (sender, e) => OnFileSystemEvent(sessionId, e.FullPath, FileChangeType.Modified);
            watcher.Deleted += (sender, e) => OnFileSystemEvent(sessionId, e.FullPath, FileChangeType.Deleted);
            watcher.Renamed += (sender, e) => OnFileSystemRenamed(sessionId, e.OldFullPath, e.FullPath);
            
            _watchers[sessionId] = watcher;
            
            // 初始化变更历史
            if (!_changeHistory.ContainsKey(sessionId))
            {
                _changeHistory[sessionId] = new List<FileChangeEvent>();
            }
            
            Console.WriteLine($"开始监控会话 {sessionId} 的工作区: {workspacePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"启动文件监控失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 停止监控指定会话
    /// </summary>
    public void StopMonitoring(string sessionId)
    {
        if (_watchers.TryRemove(sessionId, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            Console.WriteLine($"停止监控会话: {sessionId}");
        }
    }
    
    /// <summary>
    /// 获取最近的文件变更事件
    /// </summary>
    public List<FileChangeEvent> GetRecentChanges(string sessionId, int count = 50)
    {
        if (_changeHistory.TryGetValue(sessionId, out var history))
        {
            lock (_lockObject)
            {
                return history.OrderByDescending(e => e.Timestamp).Take(count).ToList();
            }
        }
        
        return new List<FileChangeEvent>();
    }
    
    /// <summary>
    /// 清除指定会话的变更历史
    /// </summary>
    public void ClearHistory(string sessionId)
    {
        if (_changeHistory.TryGetValue(sessionId, out var history))
        {
            lock (_lockObject)
            {
                history.Clear();
            }
        }
    }
    
    /// <summary>
    /// 检查指定会话是否正在监控
    /// </summary>
    public bool IsMonitoring(string sessionId)
    {
        return _watchers.ContainsKey(sessionId) && _watchers[sessionId].EnableRaisingEvents;
    }
    
    /// <summary>
    /// 处理文件系统事件
    /// </summary>
    private void OnFileSystemEvent(string sessionId, string filePath, FileChangeType changeType)
    {
        try
        {
            // 忽略临时文件和隐藏文件
            var fileName = Path.GetFileName(filePath);
            if (fileName.StartsWith(".") || fileName.EndsWith("~") || fileName.EndsWith(".tmp"))
            {
                return;
            }
            
            long? fileSize = null;
            DateTime? lastModified = null;
            DateTime? createdTime = null;
            bool isDirectory = false;
            string? contentPreview = null;
            
            if (changeType != FileChangeType.Deleted && File.Exists(filePath))
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    fileSize = fileInfo.Length;
                    lastModified = fileInfo.LastWriteTime;
                    createdTime = fileInfo.CreationTime;
                    
                    // 对于小的文本文件，提供内容预览
                    if (fileSize < 10240) // 小于10KB
                    {
                        var ext = fileInfo.Extension.ToLower();
                        var textExtensions = new[] { ".txt", ".cs", ".razor", ".html", ".css", ".js", ".json", ".xml", ".md", ".yml", ".yaml", ".config" };
                        if (textExtensions.Contains(ext))
                        {
                            try
                            {
                                var content = File.ReadAllText(filePath);
                                if (content.Length > 100)
                                {
                                    contentPreview = content.Substring(0, 100) + "...";
                                }
                                else
                                {
                                    contentPreview = content;
                                }
                            }
                            catch
                            {
                                // 忽略内容读取失败
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略文件信息获取失败
                }
            }
            else if (changeType != FileChangeType.Deleted && Directory.Exists(filePath))
            {
                isDirectory = true;
                try
                {
                    var dirInfo = new DirectoryInfo(filePath);
                    createdTime = dirInfo.CreationTime;
                    lastModified = dirInfo.LastWriteTime;
                }
                catch
                {
                    // 忽略目录信息获取失败
                }
            }
            
            var changeEvent = new FileChangeEvent
            {
                SessionId = sessionId,
                FilePath = filePath,
                ChangeType = changeType,
                Timestamp = DateTime.Now,
                FileSize = fileSize,
                FileExtension = Path.GetExtension(filePath),
                LastModified = lastModified,
                CreatedTime = createdTime,
                IsDirectory = isDirectory,
                ContentPreview = contentPreview
            };
            
            // 添加到历史记录
            if (_changeHistory.TryGetValue(sessionId, out var history))
            {
                lock (_lockObject)
                {
                    history.Add(changeEvent);
                    
                    // 限制历史记录数量
                    if (history.Count > 1000)
                    {
                        history.RemoveRange(0, history.Count - 1000);
                    }
                }
            }
            
            // 触发事件
            OnFileChanged?.Invoke(this, changeEvent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"处理文件系统事件失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 处理文件重命名事件
    /// </summary>
    private void OnFileSystemRenamed(string sessionId, string oldPath, string newPath)
    {
        try
        {
            long? fileSize = null;
            DateTime? lastModified = null;
            DateTime? createdTime = null;
            bool isDirectory = false;
            
            if (File.Exists(newPath))
            {
                try
                {
                    var fileInfo = new FileInfo(newPath);
                    fileSize = fileInfo.Length;
                    lastModified = fileInfo.LastWriteTime;
                    createdTime = fileInfo.CreationTime;
                }
                catch
                {
                    // 忽略文件信息获取失败
                }
            }
            else if (Directory.Exists(newPath))
            {
                isDirectory = true;
                try
                {
                    var dirInfo = new DirectoryInfo(newPath);
                    createdTime = dirInfo.CreationTime;
                    lastModified = dirInfo.LastWriteTime;
                }
                catch
                {
                    // 忽略目录信息获取失败
                }
            }
            
            var changeEvent = new FileChangeEvent
            {
                SessionId = sessionId,
                FilePath = newPath,
                OldFilePath = oldPath,
                ChangeType = FileChangeType.Renamed,
                Timestamp = DateTime.Now,
                FileSize = fileSize,
                FileExtension = Path.GetExtension(newPath),
                LastModified = lastModified,
                CreatedTime = createdTime,
                IsDirectory = isDirectory
            };
            
            // 添加到历史记录
            if (_changeHistory.TryGetValue(sessionId, out var history))
            {
                lock (_lockObject)
                {
                    history.Add(changeEvent);
                    
                    // 限制历史记录数量
                    if (history.Count > 1000)
                    {
                        history.RemoveRange(0, history.Count - 1000);
                    }
                }
            }
            
            // 触发事件
            OnFileChanged?.Invoke(this, changeEvent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"处理文件重命名事件失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        
        _watchers.Clear();
        _changeHistory.Clear();
        
        GC.SuppressFinalize(this);
    }
}
