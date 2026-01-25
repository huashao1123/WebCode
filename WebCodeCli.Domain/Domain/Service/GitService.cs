using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// Git 服务实现
/// </summary>
[ServiceDescription(typeof(IGitService), ServiceLifetime.Singleton)]
public class GitService : IGitService
{
    private readonly ILogger<GitService>? _logger;
    
    public GitService(ILogger<GitService>? logger = null)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// 检测工作区是否为 Git 仓库
    /// </summary>
    public bool IsGitRepository(string workspacePath)
    {
        try
        {
            return Repository.IsValid(workspacePath);
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 获取文件的提交历史
    /// </summary>
    public async Task<List<GitCommit>> GetFileHistoryAsync(string workspacePath, string filePath, int maxCount = 50)
    {
        return await Task.Run(() =>
        {
            var commits = new List<GitCommit>();
            
            try
            {
                if (!IsGitRepository(workspacePath))
                {
                    return commits;
                }
                
                using var repo = new Repository(workspacePath);
                
                // 获取文件的提交历史
                var filter = new CommitFilter
                {
                    SortBy = CommitSortStrategies.Time
                };
                
                var fileCommits = repo.Commits
                    .QueryBy(filePath, filter)
                    .Take(maxCount);
                
                foreach (var commit in fileCommits)
                {
                    commits.Add(new GitCommit
                    {
                        Hash = commit.Commit.Sha,
                        ShortHash = commit.Commit.Sha.Substring(0, 7),
                        Author = commit.Commit.Author.Name,
                        AuthorEmail = commit.Commit.Author.Email,
                        CommitDate = commit.Commit.Author.When.DateTime,
                        Message = commit.Commit.MessageShort,
                        ParentHashes = commit.Commit.Parents.Select(p => p.Sha).ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取文件提交历史失败: {ex.Message}");
            }
            
            return commits;
        });
    }
    
    /// <summary>
    /// 获取特定版本的文件内容
    /// </summary>
    public async Task<string> GetFileContentAtCommitAsync(string workspacePath, string filePath, string commitHash)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!IsGitRepository(workspacePath))
                {
                    return string.Empty;
                }
                
                using var repo = new Repository(workspacePath);
                var commit = repo.Lookup<Commit>(commitHash);
                
                if (commit == null)
                {
                    return string.Empty;
                }
                
                // 标准化文件路径（使用正斜杠）
                var normalizedPath = filePath.Replace("\\", "/");
                
                var treeEntry = commit[normalizedPath];
                if (treeEntry == null || treeEntry.TargetType != TreeEntryTargetType.Blob)
                {
                    return string.Empty;
                }
                
                var blob = (Blob)treeEntry.Target;
                return blob.GetContentText();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取文件内容失败: {ex.Message}");
                return string.Empty;
            }
        });
    }
    
    /// <summary>
    /// 获取文件差异
    /// </summary>
    public async Task<GitDiffResult> GetFileDiffAsync(string workspacePath, string filePath, string fromCommit, string toCommit)
    {
        return await Task.Run(() =>
        {
            var result = new GitDiffResult();
            
            try
            {
                if (!IsGitRepository(workspacePath))
                {
                    return result;
                }
                
                using var repo = new Repository(workspacePath);
                
                // 获取两个版本的内容
                result.OldContent = GetFileContentAtCommitAsync(workspacePath, filePath, fromCommit).Result;
                result.NewContent = GetFileContentAtCommitAsync(workspacePath, filePath, toCommit).Result;
                
                // 使用 DiffPlex 计算差异
                var differ = new DiffPlex.Differ();
                var inlineDiffer = new DiffPlex.DiffBuilder.InlineDiffBuilder(differ);
                var diff = inlineDiffer.BuildDiffModel(result.OldContent, result.NewContent);
                
                int oldLineNum = 1;
                int newLineNum = 1;
                
                foreach (var line in diff.Lines)
                {
                    var diffLine = new DiffLine
                    {
                        Content = line.Text
                    };
                    
                    switch (line.Type)
                    {
                        case DiffPlex.DiffBuilder.Model.ChangeType.Unchanged:
                            diffLine.Type = DiffLineType.Unchanged;
                            diffLine.OldLineNumber = oldLineNum++;
                            diffLine.NewLineNumber = newLineNum++;
                            break;
                        case DiffPlex.DiffBuilder.Model.ChangeType.Deleted:
                            diffLine.Type = DiffLineType.Deleted;
                            diffLine.OldLineNumber = oldLineNum++;
                            result.DeletedLines++;
                            break;
                        case DiffPlex.DiffBuilder.Model.ChangeType.Inserted:
                            diffLine.Type = DiffLineType.Added;
                            diffLine.NewLineNumber = newLineNum++;
                            result.AddedLines++;
                            break;
                        case DiffPlex.DiffBuilder.Model.ChangeType.Modified:
                            diffLine.Type = DiffLineType.Modified;
                            diffLine.OldLineNumber = oldLineNum++;
                            diffLine.NewLineNumber = newLineNum++;
                            result.AddedLines++;
                            result.DeletedLines++;
                            break;
                    }
                    
                    result.DiffLines.Add(diffLine);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取文件差异失败: {ex.Message}");
            }
            
            return result;
        });
    }
    
    /// <summary>
    /// 获取工作区状态
    /// </summary>
    public async Task<GitStatus> GetWorkspaceStatusAsync(string workspacePath)
    {
        return await Task.Run(() =>
        {
            var status = new GitStatus();
            
            try
            {
                if (!IsGitRepository(workspacePath))
                {
                    return status;
                }
                
                using var repo = new Repository(workspacePath);
                var repoStatus = repo.RetrieveStatus();
                
                foreach (var item in repoStatus)
                {
                    if (item.State.HasFlag(FileStatus.ModifiedInWorkdir) || 
                        item.State.HasFlag(FileStatus.ModifiedInIndex))
                    {
                        status.ModifiedFiles.Add(item.FilePath);
                    }
                    
                    if (item.State.HasFlag(FileStatus.NewInWorkdir) || 
                        item.State.HasFlag(FileStatus.NewInIndex))
                    {
                        status.UntrackedFiles.Add(item.FilePath);
                    }
                    
                    if (item.State.HasFlag(FileStatus.DeletedFromWorkdir) || 
                        item.State.HasFlag(FileStatus.DeletedFromIndex))
                    {
                        status.DeletedFiles.Add(item.FilePath);
                    }
                    
                    if (item.State.HasFlag(FileStatus.NewInIndex) || 
                        item.State.HasFlag(FileStatus.ModifiedInIndex) ||
                        item.State.HasFlag(FileStatus.DeletedFromIndex))
                    {
                        status.StagedFiles.Add(item.FilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取工作区状态失败: {ex.Message}");
            }
            
            return status;
        });
    }
    
    /// <summary>
    /// 获取所有提交历史
    /// </summary>
    public async Task<List<GitCommit>> GetAllCommitsAsync(string workspacePath, int maxCount = 100)
    {
        return await Task.Run(() =>
        {
            var commits = new List<GitCommit>();
            
            try
            {
                if (!IsGitRepository(workspacePath))
                {
                    return commits;
                }
                
                using var repo = new Repository(workspacePath);
                
                foreach (var commit in repo.Commits.Take(maxCount))
                {
                    commits.Add(new GitCommit
                    {
                        Hash = commit.Sha,
                        ShortHash = commit.Sha.Substring(0, 7),
                        Author = commit.Author.Name,
                        AuthorEmail = commit.Author.Email,
                        CommitDate = commit.Author.When.DateTime,
                        Message = commit.MessageShort,
                        ParentHashes = commit.Parents.Select(p => p.Sha).ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取所有提交历史失败: {ex.Message}");
            }
            
            return commits;
        });
    }
    
    /// <summary>
    /// 克隆远程仓库
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> CloneAsync(
        string gitUrl, 
        string localPath, 
        string branch, 
        GitCredentials? credentials = null,
        Action<CloneProgress>? progress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger?.LogInformation("开始克隆仓库: {GitUrl} -> {LocalPath}, 分支: {Branch}", gitUrl, localPath, branch);
                
                // 确保目标目录存在
                if (Directory.Exists(localPath))
                {
                    // 如果目录已存在且不为空，删除它
                    Directory.Delete(localPath, true);
                }
                Directory.CreateDirectory(localPath);
                
                var cloneOptions = new CloneOptions
                {
                    BranchName = branch,
                    Checkout = true
                };
                
                // 设置凭据提供器
                if (credentials != null && credentials.AuthType == "https" && !string.IsNullOrEmpty(credentials.HttpsToken))
                {
                    var credentialsHandler = CreateCredentialsHandler(credentials);
                    cloneOptions.FetchOptions.CredentialsProvider = credentialsHandler;
                }
                
                // 设置进度回调
                if (progress != null)
                {
                    cloneOptions.OnCheckoutProgress = (path, completedSteps, totalSteps) =>
                    {
                        var percentage = totalSteps > 0 ? (int)((double)completedSteps / totalSteps * 100) : 0;
                        progress(new CloneProgress
                        {
                            Percentage = percentage,
                            Stage = "检出文件",
                            Details = path
                        });
                    };
                }
                
                Repository.Clone(gitUrl, localPath, cloneOptions);
                
                _logger?.LogInformation("仓库克隆成功: {LocalPath}", localPath);
                return (true, null);
            }
            catch (LibGit2SharpException ex)
            {
                var errorMessage = $"Git 操作失败: {ex.Message}";
                _logger?.LogError(ex, "克隆仓库失败: {GitUrl}", gitUrl);
                
                // 清理失败的克隆目录
                try
                {
                    if (Directory.Exists(localPath))
                    {
                        Directory.Delete(localPath, true);
                    }
                }
                catch { }
                
                return (false, errorMessage);
            }
            catch (Exception ex)
            {
                var errorMessage = $"克隆失败: {ex.Message}";
                _logger?.LogError(ex, "克隆仓库失败: {GitUrl}", gitUrl);
                
                // 清理失败的克隆目录
                try
                {
                    if (Directory.Exists(localPath))
                    {
                        Directory.Delete(localPath, true);
                    }
                }
                catch { }
                
                return (false, errorMessage);
            }
        });
    }
    
    /// <summary>
    /// 拉取远程更新
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> PullAsync(
        string localPath, 
        GitCredentials? credentials = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!IsGitRepository(localPath))
                {
                    return (false, "指定路径不是有效的 Git 仓库");
                }
                
                _logger?.LogInformation("开始拉取更新: {LocalPath}", localPath);
                
                using var repo = new Repository(localPath);
                
                var options = new PullOptions
                {
                    FetchOptions = new FetchOptions()
                };
                
                // 设置凭据提供器
                if (credentials != null)
                {
                    options.FetchOptions.CredentialsProvider = CreateCredentialsHandler(credentials);
                }
                
                // 获取签名信息
                var signature = new Signature(
                    new Identity("WebCode", "webcode@local"), 
                    DateTimeOffset.Now);
                
                // 执行拉取
                Commands.Pull(repo, signature, options);
                
                _logger?.LogInformation("拉取更新成功: {LocalPath}", localPath);
                return (true, null);
            }
            catch (LibGit2SharpException ex)
            {
                var errorMessage = $"Git 操作失败: {ex.Message}";
                _logger?.LogError(ex, "拉取更新失败: {LocalPath}", localPath);
                return (false, errorMessage);
            }
            catch (Exception ex)
            {
                var errorMessage = $"拉取失败: {ex.Message}";
                _logger?.LogError(ex, "拉取更新失败: {LocalPath}", localPath);
                return (false, errorMessage);
            }
        });
    }
    
    /// <summary>
    /// 获取远程仓库的分支列表
    /// </summary>
    public async Task<(List<string> Branches, string? ErrorMessage)> ListRemoteBranchesAsync(
        string gitUrl, 
        GitCredentials? credentials = null)
    {
        return await Task.Run(() =>
        {
            var branches = new List<string>();
            
            try
            {
                _logger?.LogInformation("获取远程分支列表: {GitUrl}", gitUrl);
                
                // 创建临时目录用于 ls-remote
                var tempPath = Path.Combine(Path.GetTempPath(), $"git-ls-remote-{Guid.NewGuid():N}");
                
                try
                {
                    // 使用 Repository.ListRemoteReferences 获取远程引用
                    var remoteRefs = Repository.ListRemoteReferences(gitUrl, (url, usernameFromUrl, types) =>
                    {
                        if (credentials == null || credentials.AuthType == "none")
                        {
                            return new DefaultCredentials();
                        }
                        
                        if (credentials.AuthType == "https" && !string.IsNullOrEmpty(credentials.HttpsToken))
                        {
                            return new UsernamePasswordCredentials
                            {
                                Username = credentials.HttpsUsername ?? "git",
                                Password = credentials.HttpsToken
                            };
                        }
                        
                        if (credentials.AuthType == "ssh" && !string.IsNullOrEmpty(credentials.SshPrivateKey))
                        {
                            // SSH 认证：LibGit2Sharp 对 SSH 的支持有限
                            // 在 Windows 上需要配置 SSH agent 或使用 HTTPS Token 替代
                            _logger?.LogWarning("SSH 认证需要系统级 SSH agent 支持，建议使用 HTTPS Token 认证");
                            return new DefaultCredentials();
                        }
                        
                        return new DefaultCredentials();
                    });
                    
                    foreach (var reference in remoteRefs)
                    {
                        // 过滤分支引用 (refs/heads/)
                        if (reference.CanonicalName.StartsWith("refs/heads/"))
                        {
                            var branchName = reference.CanonicalName.Replace("refs/heads/", "");
                            branches.Add(branchName);
                        }
                    }
                    
                    _logger?.LogInformation("获取到 {Count} 个分支", branches.Count);
                }
                finally
                {
                    // 清理临时目录
                    try
                    {
                        if (Directory.Exists(tempPath))
                        {
                            Directory.Delete(tempPath, true);
                        }
                    }
                    catch { }
                }
                
                return (branches, null);
            }
            catch (LibGit2SharpException ex)
            {
                var errorMessage = $"获取分支列表失败: {ex.Message}";
                _logger?.LogError(ex, "获取远程分支列表失败: {GitUrl}", gitUrl);
                return (branches, errorMessage);
            }
            catch (Exception ex)
            {
                var errorMessage = $"获取分支列表失败: {ex.Message}";
                _logger?.LogError(ex, "获取远程分支列表失败: {GitUrl}", gitUrl);
                return (branches, errorMessage);
            }
        });
    }
    
    /// <summary>
    /// 获取本地仓库的当前分支
    /// </summary>
    public string? GetCurrentBranch(string localPath)
    {
        try
        {
            if (!IsGitRepository(localPath))
            {
                return null;
            }
            
            using var repo = new Repository(localPath);
            return repo.Head?.FriendlyName;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取当前分支失败: {LocalPath}", localPath);
            return null;
        }
    }
    
    /// <summary>
    /// 创建凭据处理器
    /// </summary>
    private CredentialsHandler CreateCredentialsHandler(GitCredentials credentials)
    {
        return (url, usernameFromUrl, types) =>
        {
            if (credentials.AuthType == "https" && !string.IsNullOrEmpty(credentials.HttpsToken))
            {
                return new UsernamePasswordCredentials
                {
                    Username = credentials.HttpsUsername ?? "git",
                    Password = credentials.HttpsToken
                };
            }
            
            if (credentials.AuthType == "ssh" && !string.IsNullOrEmpty(credentials.SshPrivateKey))
            {
                // SSH 认证：LibGit2Sharp 对 SSH 的支持在不同平台上有限
                // 建议使用 HTTPS Token 认证替代
                _logger?.LogWarning("SSH 认证需要系统级 SSH agent 支持，建议使用 HTTPS Token 认证");
                return new DefaultCredentials();
            }
            
            return new DefaultCredentials();
        };
    }
}
