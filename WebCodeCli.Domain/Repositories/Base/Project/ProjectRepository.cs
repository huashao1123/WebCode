using AntSK.Domain.Repositories.Base;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.Project;

/// <summary>
/// 项目仓储实现
/// </summary>
[ServiceDescription(typeof(IProjectRepository), ServiceLifetime.Scoped)]
public class ProjectRepository : Repository<ProjectEntity>, IProjectRepository
{
    public ProjectRepository(ISqlSugarClient context = null) : base(context)
    {
    }

    /// <summary>
    /// 根据用户名获取所有项目
    /// </summary>
    public async Task<List<ProjectEntity>> GetByUsernameAsync(string username)
    {
        return await GetListAsync(x => x.Username == username);
    }

    /// <summary>
    /// 根据项目ID和用户名获取项目
    /// </summary>
    public async Task<ProjectEntity?> GetByIdAndUsernameAsync(string projectId, string username)
    {
        return await GetFirstAsync(x => x.ProjectId == projectId && x.Username == username);
    }

    /// <summary>
    /// 根据项目ID和用户名删除项目
    /// </summary>
    public async Task<bool> DeleteByIdAndUsernameAsync(string projectId, string username)
    {
        return await DeleteAsync(x => x.ProjectId == projectId && x.Username == username);
    }

    /// <summary>
    /// 根据用户名获取项目列表（按更新时间降序）
    /// </summary>
    public async Task<List<ProjectEntity>> GetByUsernameOrderByUpdatedAtAsync(string username)
    {
        return await GetDB().Queryable<ProjectEntity>()
            .Where(x => x.Username == username)
            .OrderBy(x => x.UpdatedAt, OrderByType.Desc)
            .ToListAsync();
    }

    /// <summary>
    /// 检查项目名称是否已存在
    /// </summary>
    public async Task<bool> ExistsByNameAndUsernameAsync(string name, string username, string? excludeProjectId = null)
    {
        if (string.IsNullOrEmpty(excludeProjectId))
        {
            return await IsAnyAsync(x => x.Name == name && x.Username == username);
        }
        
        return await IsAnyAsync(x => x.Name == name && x.Username == username && x.ProjectId != excludeProjectId);
    }
}
