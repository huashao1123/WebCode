using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.ChatSession;

/// <summary>
/// 聊天会话实体
/// </summary>
[SugarTable("ChatSession")]
public class ChatSessionEntity
{
    /// <summary>
    /// 会话ID（主键）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string SessionId { get; set; } = string.Empty;
    
    /// <summary>
    /// 用户名（多用户支持）
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string Username { get; set; } = "default";
    
    /// <summary>
    /// 会话标题
    /// </summary>
    [SugarColumn(Length = 256, IsNullable = true)]
    public string? Title { get; set; }
    
    /// <summary>
    /// 工作区路径
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? WorkspacePath { get; set; }
    
    /// <summary>
    /// 使用的工具ID
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ToolId { get; set; }
    
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
    
    /// <summary>
    /// 工作区是否有效
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public bool IsWorkspaceValid { get; set; } = true;
    
    /// <summary>
    /// 关联的项目ID（可选）
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ProjectId { get; set; }
}
