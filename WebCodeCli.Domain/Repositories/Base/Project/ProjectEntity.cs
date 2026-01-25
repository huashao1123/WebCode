using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.Project;

/// <summary>
/// 项目实体 - 用于存储 Git 仓库配置
/// </summary>
[SugarTable("Project")]
public class ProjectEntity
{
    /// <summary>
    /// 项目ID（主键）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string ProjectId { get; set; } = string.Empty;
    
    /// <summary>
    /// 用户名（多用户支持）
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string Username { get; set; } = "default";
    
    /// <summary>
    /// 项目名称
    /// </summary>
    [SugarColumn(Length = 256, IsNullable = false)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Git 仓库地址
    /// </summary>
    [SugarColumn(Length = 1024, IsNullable = false)]
    public string GitUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// 认证方式：none（公开仓库）、https（HTTPS Token认证）、ssh（SSH密钥认证）
    /// </summary>
    [SugarColumn(Length = 16, IsNullable = false)]
    public string AuthType { get; set; } = "none";
    
    /// <summary>
    /// HTTPS 用户名（当 AuthType 为 https 时使用）
    /// </summary>
    [SugarColumn(Length = 256, IsNullable = true)]
    public string? HttpsUsername { get; set; }
    
    /// <summary>
    /// HTTPS Token（加密存储，当 AuthType 为 https 时使用）
    /// </summary>
    [SugarColumn(Length = 2048, IsNullable = true)]
    public string? HttpsToken { get; set; }
    
    /// <summary>
    /// SSH 私钥（加密存储，当 AuthType 为 ssh 时使用）
    /// </summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string? SshPrivateKey { get; set; }
    
    /// <summary>
    /// SSH 私钥密码（加密存储，可选）
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? SshPassphrase { get; set; }
    
    /// <summary>
    /// 分支名称
    /// </summary>
    [SugarColumn(Length = 256, IsNullable = false)]
    public string Branch { get; set; } = "main";
    
    /// <summary>
    /// 本地克隆路径
    /// </summary>
    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? LocalPath { get; set; }
    
    /// <summary>
    /// 最后同步时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? LastSyncAt { get; set; }
    
    /// <summary>
    /// 项目状态：pending（待克隆）、cloning（克隆中）、ready（就绪）、error（错误）
    /// </summary>
    [SugarColumn(Length = 16, IsNullable = false)]
    public string Status { get; set; } = "pending";
    
    /// <summary>
    /// 错误信息（当状态为 error 时）
    /// </summary>
    [SugarColumn(Length = 2048, IsNullable = true)]
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// 创建时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 更新时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
