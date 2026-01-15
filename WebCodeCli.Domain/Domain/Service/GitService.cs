using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// Git 服务实现
/// </summary>
[ServiceDescription(typeof(IGitService), ServiceLifetime.Singleton)]
public class GitService : IGitService
{
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
}
