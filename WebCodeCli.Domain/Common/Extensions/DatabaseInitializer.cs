using Microsoft.Extensions.Logging;
using SqlSugar;
using WebCodeCli.Domain.Common.Map;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.SessionShare;
using WebCodeCli.Domain.Repositories.Base.ChatSession;
using WebCodeCli.Domain.Repositories.Base.SessionOutput;
using WebCodeCli.Domain.Repositories.Base.Template;
using WebCodeCli.Domain.Repositories.Base.InputHistory;
using WebCodeCli.Domain.Repositories.Base.QuickAction;
using WebCodeCli.Domain.Repositories.Base.UserSetting;
using WebCodeCli.Domain.Repositories.Base.Project;

namespace WebCodeCli.Domain.Common.Extensions;

/// <summary>
/// 数据库表初始化扩展
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// 初始化CLI工具相关的数据库表
    /// </summary>
    public static void InitializeCliToolTables(this SqlSugarScope db, ILogger? logger = null)
    {
        try
        {
            // 创建CLI工具环境变量表
            logger?.LogInformation("开始初始化 CLI 工具环境变量表...");
            
            db.CodeFirst.InitTables<CliToolEnvironmentVariable>();
            
            logger?.LogInformation("CLI 工具环境变量表初始化成功");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "初始化 CLI 工具环境变量表失败");
            throw;
        }
    }
    
    /// <summary>
    /// 初始化会话分享相关的数据库表
    /// </summary>
    public static void InitializeSessionShareTables(this SqlSugarScope db, ILogger? logger = null)
    {
        try
        {
            // 创建会话分享表
            logger?.LogInformation("开始初始化会话分享表...");
            
            db.CodeFirst.InitTables<SessionShare>();
            
            logger?.LogInformation("会话分享表初始化成功");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "初始化会话分享表失败");
            throw;
        }
    }
    
    /// <summary>
    /// 初始化项目管理相关的数据库表
    /// </summary>
    public static void InitializeProjectTables(this SqlSugarScope db, ILogger? logger = null)
    {
        try
        {
            logger?.LogInformation("开始初始化项目管理表...");
            
            // 创建项目表
            db.CodeFirst.InitTables<ProjectEntity>();
            
            logger?.LogInformation("项目管理表初始化成功");
            
            // 创建索引
            InitializeProjectIndexes(db, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "初始化项目管理表失败");
            throw;
        }
    }
    
    /// <summary>
    /// 为项目表创建索引
    /// </summary>
    private static void InitializeProjectIndexes(SqlSugarScope db, ILogger? logger = null)
    {
        try
        {
            logger?.LogInformation("开始创建项目相关索引...");
            
            // Project: Username + UpdatedAt 索引（项目列表排序）
            CreateIndexIfNotExists(db, "Project", "IX_Project_Username_UpdatedAt", 
                new[] { "Username", "UpdatedAt" }, logger);
            
            // Project: Username + Name 唯一索引（确保同一用户的项目名称唯一）
            CreateIndexIfNotExists(db, "Project", "IX_Project_Username_Name", 
                new[] { "Username", "Name" }, logger, isUnique: true);
            
            logger?.LogInformation("项目相关索引创建成功");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "创建项目索引时发生警告（可能索引已存在）");
        }
    }
    
    /// <summary>
    /// 初始化聊天会话相关的数据库表（从 IndexedDB 迁移）
    /// </summary>
    public static void InitializeChatSessionTables(this SqlSugarScope db, ILogger? logger = null)
    {
        try
        {
            logger?.LogInformation("开始初始化聊天会话相关表...");
            
            // 创建聊天会话表
            db.CodeFirst.InitTables<ChatSessionEntity>();
            
            // 创建聊天消息表
            db.CodeFirst.InitTables<ChatMessageEntity>();
            
            // 创建会话输出状态表
            db.CodeFirst.InitTables<SessionOutputEntity>();
            
            // 创建提示模板表
            db.CodeFirst.InitTables<PromptTemplateEntity>();
            
            // 创建输入历史表
            db.CodeFirst.InitTables<InputHistoryEntity>();
            
            // 创建快捷操作表
            db.CodeFirst.InitTables<QuickActionEntity>();
            
            // 创建用户设置表
            db.CodeFirst.InitTables<UserSettingEntity>();
            
            logger?.LogInformation("聊天会话相关表初始化成功");
            
            // 创建索引
            InitializeChatSessionIndexes(db, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "初始化聊天会话相关表失败");
            throw;
        }
    }
    
    /// <summary>
    /// 为聊天会话相关表创建索引
    /// </summary>
    private static void InitializeChatSessionIndexes(SqlSugarScope db, ILogger? logger = null)
    {
        try
        {
            logger?.LogInformation("开始创建聊天会话相关索引...");
            
            // ChatSession: Username + UpdatedAt 索引（会话列表排序）
            CreateIndexIfNotExists(db, "ChatSession", "IX_ChatSession_Username_UpdatedAt", 
                new[] { "Username", "UpdatedAt" }, logger);
            
            // ChatMessage: SessionId 索引（按会话查询消息）
            CreateIndexIfNotExists(db, "ChatMessage", "IX_ChatMessage_SessionId", 
                new[] { "SessionId" }, logger);
            
            // ChatMessage: Username + SessionId 索引
            CreateIndexIfNotExists(db, "ChatMessage", "IX_ChatMessage_Username_SessionId", 
                new[] { "Username", "SessionId" }, logger);
            
            // PromptTemplate: Username + Category 索引
            CreateIndexIfNotExists(db, "PromptTemplate", "IX_PromptTemplate_Username_Category", 
                new[] { "Username", "Category" }, logger);
            
            // InputHistory: Username + Timestamp 索引
            CreateIndexIfNotExists(db, "InputHistory", "IX_InputHistory_Username_Timestamp", 
                new[] { "Username", "Timestamp" }, logger);
            
            // QuickAction: Username + Order 索引
            CreateIndexIfNotExists(db, "QuickAction", "IX_QuickAction_Username_Order", 
                new[] { "Username", "\"Order\"" }, logger);
            
            // UserSetting: Username + Key 唯一索引
            CreateIndexIfNotExists(db, "UserSetting", "IX_UserSetting_Username_Key", 
                new[] { "Username", "Key" }, logger, isUnique: true);
            
            // SessionOutput: Username 索引
            CreateIndexIfNotExists(db, "SessionOutput", "IX_SessionOutput_Username", 
                new[] { "Username" }, logger);
            
            logger?.LogInformation("聊天会话相关索引创建成功");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "创建索引时发生警告（可能索引已存在）");
        }
    }
    
    /// <summary>
    /// 如果索引不存在则创建
    /// </summary>
    private static void CreateIndexIfNotExists(SqlSugarScope db, string tableName, string indexName, 
        string[] columns, ILogger? logger, bool isUnique = false)
    {
        try
        {
            // 检查索引是否存在（SQLite 语法）
            var checkSql = $"SELECT name FROM sqlite_master WHERE type='index' AND name='{indexName}'";
            var exists = db.Ado.GetDataTable(checkSql).Rows.Count > 0;
            
            if (!exists)
            {
                var columnsStr = string.Join(", ", columns);
                var uniqueStr = isUnique ? "UNIQUE " : "";
                var createSql = $"CREATE {uniqueStr}INDEX {indexName} ON {tableName} ({columnsStr})";
                
                db.Ado.ExecuteCommand(createSql);
                logger?.LogDebug("索引 {IndexName} 创建成功", indexName);
            }
            else
            {
                logger?.LogDebug("索引 {IndexName} 已存在，跳过创建", indexName);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "创建索引 {IndexName} 失败", indexName);
        }
    }
}
