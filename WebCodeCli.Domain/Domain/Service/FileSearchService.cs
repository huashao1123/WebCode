using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 文件搜索服务实现
/// </summary>
[ServiceDescription(typeof(IFileSearchService), ServiceLifetime.Singleton)]
public class FileSearchService : IFileSearchService
{
    /// <summary>
    /// 在文件中搜索文本
    /// </summary>
    public async Task<List<SearchResult>> SearchInFilesAsync(string workspacePath, string searchTerm, SearchOptions options)
    {
        var results = new List<SearchResult>();
        
        if (string.IsNullOrEmpty(searchTerm) || !Directory.Exists(workspacePath))
        {
            return results;
        }
        
        try
        {
            var files = GetSearchableFiles(workspacePath, options);
            var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            
            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    if (results.Count >= options.MaxResults)
                    {
                        break;
                    }
                    
                    try
                    {
                        var lines = File.ReadAllLines(file);
                        
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (results.Count >= options.MaxResults)
                            {
                                break;
                            }
                            
                            var line = lines[i];
                            var matches = FindMatches(line, searchTerm, options);
                            
                            if (matches.Any())
                            {
                                var relativePath = Path.GetRelativePath(workspacePath, file);
                                var result = new SearchResult
                                {
                                    FilePath = relativePath,
                                    LineNumber = i + 1,
                                    LineContent = line,
                                    Matches = matches
                                };
                                
                                // 添加上下文
                                AddContext(result, lines, i, options.ContextLines);
                                
                                results.Add(result);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"搜索文件失败 {file}: {ex.Message}");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"搜索失败: {ex.Message}");
        }
        
        return results;
    }
    
    /// <summary>
    /// 使用正则表达式搜索
    /// </summary>
    public async Task<List<SearchResult>> SearchByRegexAsync(string workspacePath, string pattern, SearchOptions options)
    {
        var results = new List<SearchResult>();
        
        if (string.IsNullOrEmpty(pattern) || !Directory.Exists(workspacePath))
        {
            return results;
        }
        
        try
        {
            var regexOptions = RegexOptions.Compiled;
            if (!options.CaseSensitive)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }
            
            var regex = new Regex(pattern, regexOptions);
            var files = GetSearchableFiles(workspacePath, options);
            
            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    if (results.Count >= options.MaxResults)
                    {
                        break;
                    }
                    
                    try
                    {
                        var lines = File.ReadAllLines(file);
                        
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (results.Count >= options.MaxResults)
                            {
                                break;
                            }
                            
                            var line = lines[i];
                            var matches = regex.Matches(line);
                            
                            if (matches.Count > 0)
                            {
                                var relativePath = Path.GetRelativePath(workspacePath, file);
                                var matchRanges = matches.Select(m => new MatchRange
                                {
                                    Start = m.Index,
                                    End = m.Index + m.Length,
                                    Text = m.Value
                                }).ToList();
                                
                                var result = new SearchResult
                                {
                                    FilePath = relativePath,
                                    LineNumber = i + 1,
                                    LineContent = line,
                                    Matches = matchRanges
                                };
                                
                                // 添加上下文
                                AddContext(result, lines, i, options.ContextLines);
                                
                                results.Add(result);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"搜索文件失败 {file}: {ex.Message}");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"正则搜索失败: {ex.Message}");
        }
        
        return results;
    }
    
    /// <summary>
    /// 搜索文件名
    /// </summary>
    public async Task<List<string>> SearchFileNamesAsync(string workspacePath, string pattern)
    {
        var results = new List<string>();
        
        if (string.IsNullOrEmpty(pattern) || !Directory.Exists(workspacePath))
        {
            return results;
        }
        
        try
        {
            await Task.Run(() =>
            {
                var files = Directory.GetFiles(workspacePath, $"*{pattern}*", SearchOption.AllDirectories);
                
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(workspacePath, file);
                    results.Add(relativePath);
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"搜索文件名失败: {ex.Message}");
        }
        
        return results;
    }
    
    /// <summary>
    /// 获取可搜索的文件列表
    /// </summary>
    private List<string> GetSearchableFiles(string workspacePath, SearchOptions options)
    {
        var files = new List<string>();
        
        try
        {
            var allFiles = Directory.GetFiles(workspacePath, "*.*", SearchOption.AllDirectories);
            
            // 解析排除的文件夹
            var excludedDirs = string.IsNullOrEmpty(options.ExcludeFolders) 
                ? new HashSet<string>() 
                : new HashSet<string>(options.ExcludeFolders.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()));
            
            // 解析文件类型过滤
            var fileTypeFilters = string.IsNullOrEmpty(options.FileTypeFilter)
                ? new HashSet<string>()
                : new HashSet<string>(options.FileTypeFilter.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim().StartsWith(".") ? f.Trim() : $".{f.Trim()}"));
            
            foreach (var file in allFiles)
            {
                // 检查是否在排除目录中
                var relativePath = Path.GetRelativePath(workspacePath, file);
                if (excludedDirs.Any(dir => relativePath.Contains(dir, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                
                // 检查文件类型过滤
                if (fileTypeFilters.Any())
                {
                    var extension = Path.GetExtension(file);
                    if (!fileTypeFilters.Contains(extension))
                    {
                        continue;
                    }
                }
                
                // 跳过二进制文件和大文件
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > 10 * 1024 * 1024) // 10MB
                {
                    continue;
                }
                
                files.Add(file);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取文件列表失败: {ex.Message}");
        }
        
        return files;
    }
    
    /// <summary>
    /// 查找匹配项
    /// </summary>
    private List<MatchRange> FindMatches(string line, string searchTerm, SearchOptions options)
    {
        var matches = new List<MatchRange>();
        var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        int index = 0;
        while (index < line.Length)
        {
            int foundIndex = line.IndexOf(searchTerm, index, comparison);
            if (foundIndex == -1)
            {
                break;
            }
            
            // 检查全词匹配
            if (options.WholeWord)
            {
                bool isWordStart = foundIndex == 0 || !char.IsLetterOrDigit(line[foundIndex - 1]);
                bool isWordEnd = foundIndex + searchTerm.Length >= line.Length || 
                                 !char.IsLetterOrDigit(line[foundIndex + searchTerm.Length]);
                
                if (!isWordStart || !isWordEnd)
                {
                    index = foundIndex + 1;
                    continue;
                }
            }
            
            matches.Add(new MatchRange
            {
                Start = foundIndex,
                End = foundIndex + searchTerm.Length,
                Text = line.Substring(foundIndex, searchTerm.Length)
            });
            
            index = foundIndex + searchTerm.Length;
        }
        
        return matches;
    }
    
    /// <summary>
    /// 添加上下文行
    /// </summary>
    private void AddContext(SearchResult result, string[] lines, int lineIndex, int contextLines)
    {
        // 添加前面的上下文
        for (int i = Math.Max(0, lineIndex - contextLines); i < lineIndex; i++)
        {
            result.ContextLines.Add(new ContextLine
            {
                LineNumber = i + 1,
                Content = lines[i],
                IsMatch = false
            });
        }
        
        // 添加匹配行本身
        result.ContextLines.Add(new ContextLine
        {
            LineNumber = lineIndex + 1,
            Content = lines[lineIndex],
            IsMatch = true
        });
        
        // 添加后面的上下文
        for (int i = lineIndex + 1; i < Math.Min(lines.Length, lineIndex + contextLines + 1); i++)
        {
            result.ContextLines.Add(new ContextLine
            {
                LineNumber = i + 1,
                Content = lines[i],
                IsMatch = false
            });
        }
    }
}
