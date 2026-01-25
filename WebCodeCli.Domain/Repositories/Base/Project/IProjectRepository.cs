namespace WebCodeCli.Domain.Repositories.Base.Project;

/// <summary>
/// 项目仓储接口
/// </summary>
public interface IProjectRepository : IRepository<ProjectEntity>
{
    /// <summary>
    /// 根据用户名获取所有项目
    /// </summary>
    Task<List<ProjectEntity>> GetByUsernameAsync(string username);
    
    /// <summary>
    /// 根据项目ID和用户名获取项目
    /// </summary>
    Task<ProjectEntity?> GetByIdAndUsernameAsync(string projectId, string username);
    
    /// <summary>
    /// 根据项目ID和用户名删除项目
    /// </summary>
    Task<bool> DeleteByIdAndUsernameAsync(string projectId, string username);
    
    /// <summary>
    /// 根据用户名获取项目列表（按更新时间降序）
    /// </summary>
    Task<List<ProjectEntity>> GetByUsernameOrderByUpdatedAtAsync(string username);
    
    /// <summary>
    /// 检查项目名称是否已存在
    /// </summary>
    Task<bool> ExistsByNameAndUsernameAsync(string name, string username, string? excludeProjectId = null);
}
